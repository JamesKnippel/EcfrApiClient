using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
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
    Task<int> CountWordsInXml(string xml);
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
        
        var response = await GetWithRetryAsync(request.RequestUri.ToString());
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TitlesResponse>(content) ?? new TitlesResponse();
    }

    public async Task<AgenciesResponse> GetAgenciesAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/v1/agencies.json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        
        var response = await GetWithRetryAsync(request.RequestUri.ToString());
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<AgenciesResponse>(content) ?? new AgenciesResponse();
    }

    public async Task<Agency> GetAgencyBySlugAsync(string slug)
    {
        var response = await GetAgenciesAsync();
        var agency = FindAgencyBySlug(response.Agencies, slug);

        if (agency == null)
        {
            _logger?.LogWarning("Agency with slug '{Slug}' not found. Available agencies: {Agencies}", 
                slug, 
                string.Join(", ", response.Agencies.Select(a => $"{a.Name} ({a.Slug})")));
            throw new ArgumentException($"Agency with slug '{slug}' not found. Please use one of the following slugs: {string.Join(", ", response.Agencies.Select(a => a.Slug))}");
        }

        return agency;
    }

    private Agency? FindAgencyBySlug(List<Agency> agencies, string slug)
    {
        foreach (var agency in agencies)
        {
            if (agency.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase))
                return agency;
                
            if (agency.Children != null && agency.Children.Any())
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
        if (agency == null)
        {
            throw new ArgumentException($"Agency with slug '{slug}' not found");
        }

        var allTitles = await GetTitlesAsync();
        if (allTitles?.Titles == null)
        {
            throw new InvalidOperationException("Failed to retrieve titles from eCFR API");
        }
        
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
            title.WordCount = await CountWordsInXml(xml);
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
        if (startDate >= endDate)
        {
            throw new ArgumentException("Start date must be before end date");
        }

        var agency = await GetAgencyBySlugAsync(agencySlug);
        if (agency == null)
        {
            throw new ArgumentException($"Agency with slug '{agencySlug}' not found");
        }

        var history = new AgencyWordCountHistory
        { 
            Agency = agency,
            TitleHistories = new List<TitleWordCountHistory>(),
            StartDate = startDate,
            EndDate = endDate
        };

        var agencyTitles = await GetAgencyTitlesAsync(agencySlug);
        if (agencyTitles?.Titles == null || !agencyTitles.Titles.Any())
        {
            _logger?.LogWarning("No titles found for agency {AgencySlug}", agencySlug);
            return history;
        }
        
        // Process titles in parallel with rate limiting
        using var semaphore = new SemaphoreSlim(3); // Allow 3 concurrent requests
        var titleTasks = agencyTitles.Titles.Select(async title =>
        {
            await semaphore.WaitAsync();
            try
            {
                // Get weekly snapshots within the range
                var issueDates = GetDateRangeList(startDate, endDate);
                
                // Get the latest issue date
                if (!string.IsNullOrEmpty(title.LatestIssueDate))
                {
                    var latestDate = DateTimeOffset.Parse(title.LatestIssueDate);
                    issueDates = issueDates.Where(d => d <= latestDate).ToList();
                }

                if (!issueDates.Any())
                {
                    _logger?.LogInformation("Title {TitleNumber} has no issue dates in requested range", title.Number);
                    return null;
                }

                var titleHistory = new TitleWordCountHistory
                {
                    TitleNumber = title.Number,
                    TitleName = title.Name,
                    WordCounts = new List<WordCountSnapshot>()
                };

                WordCountSnapshot? lastSnapshot = null;
                foreach (var issueDate in issueDates.OrderBy(d => d))
                {
                    try
                    {
                        int wordCount;
                        // First check cache
                        if (await _cacheService.IsCachedAsync(title.Number, issueDate))
                        {
                            wordCount = await _cacheService.GetWordCountAsync(title.Number, issueDate);
                        }
                        else
                        {
                            // Get from API and cache
                            await Task.Delay(RateLimitDelayMs);
                            var xml = await GetTitleXmlForDateAsync(title.Number, issueDate);
                            wordCount = await CountWordsInXml(xml);
                            
                            // Cache the result
                            await _cacheService.UpdateTitleCacheAsync(title.Number, issueDate, xml);
                        }

                        var snapshot = new WordCountSnapshot
                        {
                            Date = issueDate,
                            WordCount = wordCount
                        };
                        
                        if (lastSnapshot != null)
                        {
                            // Calculate rate metrics
                            var daysSinceLastSnapshot = Math.Max(1, (int)(snapshot.Date - lastSnapshot.Date).TotalDays);
                            snapshot.DaysSinceLastSnapshot = daysSinceLastSnapshot;
                            snapshot.WordsAddedSinceLastSnapshot = snapshot.WordCount - lastSnapshot.WordCount;
                            snapshot.WordsPerDay = (double)snapshot.WordsAddedSinceLastSnapshot / daysSinceLastSnapshot;
                        }
                        
                        titleHistory.WordCounts.Add(snapshot);
                        lastSnapshot = snapshot;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error getting word count for title {TitleNumber} on {Date}", 
                            title.Number, issueDate);
                    }
                }

                // Only return history if we have at least one valid word count
                return titleHistory.WordCounts.Any() ? titleHistory : null;
            }
            finally
            {
                semaphore.Release();
            }
        });

        var histories = await Task.WhenAll(titleTasks);
        history.TitleHistories = histories.Where(h => h != null).ToList()!;
        
        // Sort titles by number for consistent ordering
        history.TitleHistories = history.TitleHistories
            .OrderBy(h => h.TitleNumber)
            .ToList();
            
        return history;
    }

    private List<DateTimeOffset> GetDateRangeList(DateTimeOffset start, DateTimeOffset end)
    {
        var dates = new List<DateTimeOffset>();
        var current = start;
        
        // Get weekly snapshots
        while (current <= end)
        {
            dates.Add(current);
            current = current.AddDays(7); // Weekly intervals
        }
        
        // Always include the end date if it's not already included
        if (!dates.Contains(end))
        {
            dates.Add(end);
        }
        
        return dates.OrderBy(d => d).ToList();
    }

    public async Task<int> CountWordsInXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return 0;

        return await Task.Run(() =>
        {
            // First remove all XML attributes
            var noAttrXml = Regex.Replace(xml, @"\s+\w+\s*=\s*""[^""]*""|\s+\w+\s*=\s*'[^']*'", "");

            // Then remove all XML tags
            var noTagsXml = Regex.Replace(noAttrXml, @"<[^>]*>", " ");

            // Normalize whitespace
            var normalizedText = Regex.Replace(noTagsXml, @"\s+", " ").Trim();

            // Split by space and count non-empty words
            return normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        });
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
        
        var response = await GetWithRetryAsync(request.RequestUri.ToString());
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<T> RetryWithBackoffAsync<T>(Func<Task<T>> action, int maxRetries = 3)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                return await action();
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("429"))
            {
                if (i == maxRetries - 1)
                    throw;

                var delay = TimeSpan.FromSeconds(Math.Pow(2, i)); // Exponential backoff: 1s, 2s, 4s
                _logger?.LogWarning("Rate limited. Retrying in {Delay} seconds...", delay.TotalSeconds);
                await Task.Delay(delay);
            }
        }
        throw new Exception("Should not reach here");
    }

    private async Task<HttpResponseMessage> GetWithRetryAsync(string url)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);
        return await RetryWithBackoffAsync(async () =>
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return response;
        });
    }
}
