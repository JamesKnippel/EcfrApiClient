using System.Text.Json;
using System.Text.RegularExpressions;
using EcfrApi.Web.Models;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace EcfrApi.Web.Services;

public interface IEcfrClient
{
    Task<TitlesResponse> GetTitlesAsync();
    Task<AgenciesResponse> GetAgenciesAsync();
    Task<string> GetTitleXmlAsync(int titleNumber);
    Task<string> GetTitleXmlForDateAsync(int titleNumber, DateTimeOffset date);
    Task<Agency> GetAgencyBySlugAsync(string slug);
    Task<AgencyTitlesResult> GetAgencyTitlesAsync(string slug);
    Task<AgencyTitlesResult> GetAgencyTitlesWithWordCountAsync(string slug);
    Task<AgencyWordCountHistory> GetAgencyWordCountHistoryAsync(string slug, DateTimeOffset startDate, DateTimeOffset endDate);
}

public class EcfrClient : IEcfrClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EcfrClient>? _logger;
    private readonly ITitleCacheService _cacheService;
    private static readonly SemaphoreSlim _rateLimitSemaphore = new SemaphoreSlim(2);
    private const int RateLimitDelayMs = 100;
    private const string BaseUrl = "https://www.ecfr.gov";

    public EcfrClient(
        HttpClient httpClient,
        ITitleCacheService cacheService,
        ILogger<EcfrClient>? logger = null)
    {
        _httpClient = httpClient;
        _cacheService = cacheService;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(BaseUrl);
    }

    public async Task<TitlesResponse> GetTitlesAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/versioner/v1/titles");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TitlesResponse>(content) ?? new TitlesResponse();
    }

    public async Task<AgenciesResponse> GetAgenciesAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/v1/agencies.json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<AgenciesResponse>(content) ?? new AgenciesResponse();
    }

    public async Task<Agency> GetAgencyBySlugAsync(string slug)
    {
        var agencies = await GetAgenciesAsync();
        var agency = FindAgencyBySlug(agencies.Agencies, slug);
        
        if (agency == null)
            throw new ArgumentException($"Agency with slug '{slug}' not found");
            
        return agency;
    }

    private Agency? FindAgencyBySlug(List<Agency> agencies, string slug)
    {
        foreach (var agency in agencies)
        {
            if (agency.Slug == slug)
                return agency;
                
            if (agency.Children.Count > 0)
            {
                var childAgency = FindAgencyBySlug(agency.Children, slug);
                if (childAgency != null)
                    return childAgency;
            }
        }
        
        return null;
    }

    public async Task<AgencyTitlesResult> GetAgencyTitlesAsync(string slug)
    {
        var agency = await GetAgencyBySlugAsync(slug);
        var allTitles = await GetTitlesAsync();
        
        // Get distinct title numbers from agency and all its children
        var allTitleNumbers = new HashSet<int>();
        CollectTitleNumbers(agency, allTitleNumbers);
        
        var agencyTitles = allTitles.Titles
            .Where(t => allTitleNumbers.Contains(t.Number))
            .ToList();
        
        return new AgencyTitlesResult
        {
            Agency = agency,
            Titles = agencyTitles
        };
    }

    private void CollectTitleNumbers(Agency agency, HashSet<int> titleNumbers)
    {
        foreach (var reference in agency.CfrReferences)
        {
            titleNumbers.Add(reference.Title);
        }

        foreach (var child in agency.Children)
        {
            CollectTitleNumbers(child, titleNumbers);
        }
    }

    public async Task<AgencyTitlesResult> GetAgencyTitlesWithWordCountAsync(string slug)
    {
        _logger?.LogInformation("Getting titles with word count for agency: {Slug}", slug);
        var result = await GetAgencyTitlesAsync(slug);
        
        _logger?.LogInformation("Found {TitleCount} titles for {AgencyName}", 
            result.Titles.Count, result.Agency.Name);
        
        foreach (var title in result.Titles)
        {
            _logger?.LogInformation("Processing Title {TitleNumber}...", title.Number);
            var xml = await GetTitleXmlAsync(title.Number);
            title.WordCount = CountWordsInXml(xml);
            _logger?.LogInformation("Title {TitleNumber} has {WordCount:N0} words", 
                title.Number, title.WordCount);
        }
        
        _logger?.LogInformation("Total word count for {AgencyName}: {TotalWords:N0}", 
            result.Agency.Name, result.TotalWordCount);
        
        return result;
    }

    public async Task<AgencyWordCountHistory> GetAgencyWordCountHistoryAsync(
        string agencySlug,
        DateTimeOffset startDate,
        DateTimeOffset endDate)
    {
        var agency = await GetAgencyBySlugAsync(agencySlug);
        var titles = await GetAgencyTitlesAsync(agencySlug);
        
        _logger?.LogInformation("Processing word counts for agency {Agency} between {StartDate} and {EndDate}", 
            agency.Name, startDate, endDate);

        var history = new AgencyWordCountHistory 
        { 
            Agency = agency,
            TitleHistories = new List<TitleWordCountHistory>(),
            Dates = new List<DateWordCount>(),
            StartDate = startDate,
            EndDate = endDate
        };

        // Process titles in parallel with a semaphore to control concurrency
        using var semaphore = new SemaphoreSlim(2);
        var titleTasks = titles.Titles.Select(async title =>
        {
            await semaphore.WaitAsync();
            try
            {
                var titleHistory = new TitleWordCountHistory
                {
                    TitleNumber = title.Number,
                    TitleName = title.Name,
                    WordCounts = new List<WordCountSnapshot>()
                };

                var dates = GetDateRange(startDate, endDate);
                var snapshots = await ProcessDatesForTitleConcurrently(title.Number, dates);
                titleHistory.WordCounts.AddRange(snapshots);

                return (titleHistory, snapshots);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(titleTasks);
        history.TitleHistories = results.Select(r => r.titleHistory).ToList();

        // Aggregate word counts by date across all titles
        var dateGroups = results
            .SelectMany(r => r.snapshots)
            .GroupBy(s => s.Date)
            .OrderBy(g => g.Key);

        foreach (var group in dateGroups)
        {
            history.Dates.Add(new DateWordCount
            {
                Date = group.Key,
                WordCount = group.Sum(s => s.WordCount)
            });
        }

        return history;
    }

    private async Task<List<WordCountSnapshot>> ProcessDatesForTitleConcurrently(
        int titleNumber,
        List<DateTimeOffset> dates)
    {
        var snapshots = new List<WordCountSnapshot>();
        using var semaphore = new SemaphoreSlim(3);
        
        var tasks = dates.Select(async date =>
        {
            await semaphore.WaitAsync();
            try
            {
                // First try to get from cache
                if (await _cacheService.IsCachedAsync(titleNumber, date))
                {
                    var wordCount = await _cacheService.GetWordCountAsync(titleNumber, date);
                    return new WordCountSnapshot
                    {
                        Date = date,
                        WordCount = wordCount
                    };
                }

                // If not in cache, get from API and cache it
                await Task.Delay(RateLimitDelayMs);
                var xml = await GetTitleXmlForDateAsync(titleNumber, date);
                await _cacheService.UpdateTitleCacheAsync(titleNumber, date, xml);
                
                return new WordCountSnapshot
                {
                    Date = date,
                    WordCount = CountWordsInXml(xml)
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing title {TitleNumber} for date {Date}",
                    titleNumber, date);
                return new WordCountSnapshot { Date = date, WordCount = 0 };
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        snapshots.AddRange(results.OrderBy(s => s.Date));

        // Calculate words added since last snapshot
        for (int i = 1; i < snapshots.Count; i++)
        {
            var current = snapshots[i];
            var previous = snapshots[i - 1];
            
            current.WordsAddedSinceLastSnapshot = current.WordCount - previous.WordCount;
            current.DaysSinceLastSnapshot = (int)(current.Date - previous.Date).TotalDays;
            
            if (current.DaysSinceLastSnapshot > 0)
            {
                current.WordsPerDay = (double)current.WordsAddedSinceLastSnapshot / current.DaysSinceLastSnapshot;
            }
        }

        return snapshots;
    }

    internal int CountWordsInXml(string xml)
    {
        // First remove all XML attributes by removing everything between quotes, handling both single and double quotes
        xml = Regex.Replace(xml, @"\s+\w+\s*=\s*""[^""]*""|\s+\w+\s*=\s*'[^']*'", "");
        
        // Then remove all XML tags (including tag names)
        xml = Regex.Replace(xml, @"<[^>]*>", " ");
        
        // Remove multiple spaces and trim
        xml = Regex.Replace(xml, @"\s+", " ").Trim();
        
        return xml.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    public async Task<string> GetTitleXmlAsync(int titleNumber)
    {
        // First get the title to get its latest issue date
        var titlesResponse = await GetTitlesAsync();
        var title = titlesResponse.Titles.FirstOrDefault(t => t.Number == titleNumber);
        
        if (title == null)
            throw new ArgumentException($"Title {titleNumber} not found");
            
        if (string.IsNullOrEmpty(title.LatestIssueDate))
            throw new InvalidOperationException($"Title {titleNumber} has no latest issue date");

        _logger?.LogInformation("Retrieving XML for Title {TitleNumber} (Issue Date: {IssueDate})", 
            titleNumber, title.LatestIssueDate);

        return await GetTitleXmlForDateAsync(titleNumber, DateTimeOffset.Parse(title.LatestIssueDate));
    }

    public async Task<string> GetTitleXmlForDateAsync(int titleNumber, DateTimeOffset date)
    {
        var formattedDate = date.ToString("yyyy-MM-dd");
        
        _logger?.LogInformation("Retrieving XML for Title {TitleNumber} at date {Date}", 
            titleNumber, formattedDate);

        using var request = new HttpRequestMessage(
            HttpMethod.Get, 
            $"/api/versioner/v1/full/{formattedDate}/title-{titleNumber}.xml");
        
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        
        var response = await _httpClient.GetAsync(request.RequestUri);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadAsStringAsync();
    }
}
