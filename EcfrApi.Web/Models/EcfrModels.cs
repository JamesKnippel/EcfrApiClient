using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Linq;

namespace EcfrApi.Web.Models;

public class TitlesResponse
{
    [JsonPropertyName("titles")]
    public List<Title> Titles { get; set; } = new();

    [JsonPropertyName("meta")]
    public Meta Meta { get; set; } = new();
}

public class Title
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("latest_amended_on")]
    public string? LatestAmendedOn { get; set; }

    [JsonPropertyName("latest_issue_date")]
    public string? LatestIssueDate { get; set; }

    [JsonPropertyName("up_to_date_as_of")]
    public string? UpToDateAsOf { get; set; }

    [JsonPropertyName("reserved")]
    public bool Reserved { get; set; }

    public int WordCount { get; set; }
}

public class Meta
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("import_in_progress")]
    public bool ImportInProgress { get; set; }
}

public class AgenciesResponse
{
    [JsonPropertyName("agencies")]
    public List<Agency> Agencies { get; set; } = new();

    [JsonPropertyName("meta")]
    public Meta Meta { get; set; } = new();
}

public class Agency
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("short_name")]
    public string? ShortName { get; set; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("sortable_name")]
    public string SortableName { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("children")]
    public List<Agency> Children { get; set; } = new();

    [JsonPropertyName("cfr_references")]
    public List<CfrReference> CfrReferences { get; set; } = new();
}

public class CfrReference
{
    [JsonPropertyName("title")]
    public int Title { get; set; }

    [JsonPropertyName("chapter")]
    public string Chapter { get; set; } = string.Empty;

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }
}

public class AgencyTitlesResult
{
    public Agency Agency { get; set; } = new();
    public List<Title> Titles { get; set; } = new();
    public int TotalWordCount => (Titles?.Sum(t => t.WordCount)) ?? 0;
}

public class TitleWordCountHistory
{
    public int TitleNumber { get; set; }
    public string TitleName { get; set; } = string.Empty;
    public List<WordCountSnapshot> WordCounts { get; set; } = new();
    
    public int TotalWordsAdded => 
        WordCounts.Count > 1 
            ? WordCounts[^1].WordCount - WordCounts[0].WordCount 
            : 0;
            
    public double AverageWordsPerDay =>
        WordCounts.Count > 1
            ? (double)TotalWordsAdded / (WordCounts[^1].Date - WordCounts[0].Date).TotalDays
            : 0;
}

public class WordCountSnapshot
{
    public DateTimeOffset Date { get; set; }
    public int WordCount { get; set; }
    
    public int WordsAddedSinceLastSnapshot { get; set; }
    public int DaysSinceLastSnapshot { get; set; }
    public double WordsPerDay { get; set; }
}

public class AgencyWordCountHistory
{
    public Agency Agency { get; set; } = new();
    public List<TitleWordCountHistory> TitleHistories { get; set; } = new();
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }
    
    public int TotalWordsAdded => TitleHistories.Sum(t => t.TotalWordsAdded);
    public double AverageWordsPerDay => TitleHistories.Sum(t => t.AverageWordsPerDay);
}
