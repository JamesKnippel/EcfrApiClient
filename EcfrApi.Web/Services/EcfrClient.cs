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
    Task<AgencyWordCountHistory> GetAgencyWordCountHistoryAsync(string slug, DateTimeOffset startDate, DateTimeOffset endDate, int intervalDays = 90);
}

public class EcfrClient : IEcfrClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EcfrClient>? _logger;
    private const string BaseUrl = "https://www.ecfr.gov";

    public EcfrClient(HttpClient httpClient, ILogger<EcfrClient>? logger = null)
    {
        _httpClient = httpClient;
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
        string slug, DateTimeOffset startDate, DateTimeOffset endDate, int intervalDays = 90)
    {
        if (startDate > endDate)
        {
            throw new ArgumentException("Start date must be before end date");
        }

        var result = await GetAgencyTitlesAsync(slug);
        var agency = result.Agency;
        var titles = result.Titles;  

        var history = new AgencyWordCountHistory
        {
            Agency = agency,
            TitleHistories = new List<TitleWordCountHistory>(),
            StartDate = startDate,
            EndDate = endDate
        };

        foreach (var title in titles)
        {
            _logger?.LogInformation($"Processing history for Title {title.Number}");

            var titleHistory = new TitleWordCountHistory
            {
                TitleNumber = title.Number,
                TitleName = title.Name,
                WordCounts = new List<WordCountSnapshot>()
            };

            // Calculate dates at regular intervals
            var currentDate = startDate;
            DateTimeOffset? previousDate = null;
            int? previousWordCount = null;

            while (currentDate <= endDate)
            {
                try
                {
                    var xml = await GetTitleXmlForDateAsync(title.Number, currentDate);
                    var wordCount = CountWordsInXml(xml);

                    var snapshot = new WordCountSnapshot
                    {
                        Date = currentDate,
                        WordCount = wordCount,
                        DaysSinceLastSnapshot = previousDate.HasValue ? (int)(currentDate - previousDate.Value).TotalDays : 0,
                        WordsAddedSinceLastSnapshot = previousWordCount.HasValue ? wordCount - previousWordCount.Value : 0
                    };

                    // Calculate words per day rate
                    if (snapshot.DaysSinceLastSnapshot > 0)
                    {
                        snapshot.WordsPerDay = (double)snapshot.WordsAddedSinceLastSnapshot / snapshot.DaysSinceLastSnapshot;
                    }

                    titleHistory.WordCounts.Add(snapshot);
                    _logger?.LogInformation($"Title {title.Number} at {currentDate:yyyy-MM-dd}: {wordCount:N0} words");

                    previousDate = currentDate;
                    previousWordCount = wordCount;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to get word count for Title {title.Number} at {currentDate:yyyy-MM-dd}");
                }

                currentDate = currentDate.AddDays(intervalDays);
            }

            // Add final snapshot at end date if not already included
            if (currentDate > endDate && previousDate.HasValue && previousDate.Value < endDate)
            {
                try
                {
                    var xml = await GetTitleXmlForDateAsync(title.Number, endDate);
                    var wordCount = CountWordsInXml(xml);

                    var snapshot = new WordCountSnapshot
                    {
                        Date = endDate,
                        WordCount = wordCount,
                        DaysSinceLastSnapshot = (int)(endDate - previousDate.Value).TotalDays,
                        WordsAddedSinceLastSnapshot = previousWordCount.HasValue ? wordCount - previousWordCount.Value : 0
                    };

                    // Calculate words per day rate
                    if (snapshot.DaysSinceLastSnapshot > 0)
                    {
                        snapshot.WordsPerDay = (double)snapshot.WordsAddedSinceLastSnapshot / snapshot.DaysSinceLastSnapshot;
                    }

                    titleHistory.WordCounts.Add(snapshot);
                    _logger?.LogInformation($"Title {title.Number} at {endDate:yyyy-MM-dd}: {wordCount:N0} words");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to get word count for Title {title.Number} at {endDate:yyyy-MM-dd}");
                }
            }

            if (titleHistory.WordCounts.Any())
            {
                history.TitleHistories.Add(titleHistory);
            }
        }

        _logger?.LogInformation($"Total words added across all titles: {history.TotalWordsAdded:N0}");
        _logger?.LogInformation($"Average words added per day: {history.AverageWordsPerDay:N0}");

        return history;
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
        
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
