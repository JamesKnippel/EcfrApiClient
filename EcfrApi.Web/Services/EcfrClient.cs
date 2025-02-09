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
    Task<AgencyWordCountHistory> GetAgencyWordCountHistoryAsync(string slug, DateTimeOffset startDate, DateTimeOffset endDate, int intervalDays = 30);
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
        
        var agencyTitleNumbers = agency.CfrReferences.Select(r => r.Title).Distinct().ToList();
        var agencyTitles = allTitles.Titles.Where(t => agencyTitleNumbers.Contains(t.Number)).ToList();
        
        return new AgencyTitlesResult
        {
            Agency = agency,
            Titles = agencyTitles
        };
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
        string slug, DateTimeOffset startDate, DateTimeOffset endDate, int intervalDays = 30)
    {
        if (startDate > endDate)
        {
            throw new ArgumentException("Start date must be before end date");
        }

        _logger?.LogInformation("Getting word count history for agency {Slug} from {StartDate} to {EndDate}", 
            slug, startDate, endDate);

        var agency = await GetAgencyBySlugAsync(slug);
        var titles = await GetAgencyTitlesAsync(slug);

        var history = new AgencyWordCountHistory
        {
            Agency = agency,
            StartDate = startDate,
            EndDate = endDate
        };

        foreach (var title in titles.Titles)
        {
            _logger?.LogInformation("Processing history for Title {TitleNumber}", title.Number);

            var titleHistory = new TitleWordCountHistory
            {
                TitleNumber = title.Number,
                TitleName = title.Name
            };

            var currentDate = endDate;
            WordCountSnapshot? previousSnapshot = null;

            while (currentDate >= startDate)
            {
                try
                {
                    var xml = await GetTitleXmlForDateAsync(title.Number, currentDate);
                    var wordCount = CountWordsInXml(xml);

                    var snapshot = new WordCountSnapshot
                    {
                        Date = currentDate,
                        WordCount = wordCount
                    };

                    if (previousSnapshot != null)
                    {
                        snapshot.WordsAddedSinceLastSnapshot = wordCount - previousSnapshot.WordCount;
                        snapshot.DaysSinceLastSnapshot = (int)(previousSnapshot.Date - currentDate).TotalDays;
                        snapshot.WordsPerDay = snapshot.DaysSinceLastSnapshot > 0 
                            ? (double)snapshot.WordsAddedSinceLastSnapshot / snapshot.DaysSinceLastSnapshot 
                            : 0;
                    }

                    titleHistory.WordCounts.Add(snapshot);
                    previousSnapshot = snapshot;

                    _logger?.LogInformation("Title {TitleNumber} at {Date}: {WordCount:N0} words", 
                        title.Number, currentDate.ToString("yyyy-MM-dd"), wordCount);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to get word count for Title {TitleNumber} at {Date}", 
                        title.Number, currentDate.ToString("yyyy-MM-dd"));
                }

                currentDate = currentDate.AddDays(-intervalDays);
            }

            // Sort snapshots by date (oldest first)
            titleHistory.WordCounts = titleHistory.WordCounts
                .OrderBy(s => s.Date)
                .ToList();

            history.TitleHistories.Add(titleHistory);
        }

        _logger?.LogInformation("Total words added across all titles: {TotalWords:N0}", 
            history.TotalWordsAdded);
        _logger?.LogInformation("Average words added per day: {AverageWords:N0}", 
            history.AverageWordsPerDay);

        return history;
    }

    internal int CountWordsInXml(string xml)
    {
        // Remove XML tags and attributes
        var textOnly = Regex.Replace(xml, "<[^>]*>", " ");
        
        // Remove extra whitespace
        textOnly = Regex.Replace(textOnly, @"\s+", " ").Trim();
        
        // Split by space and count non-empty words
        var wordCount = textOnly.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        
        _logger?.LogDebug("Counted {WordCount:N0} words in XML content", wordCount);
        
        return wordCount;
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
