using System;

namespace EcfrApi.Web.Models;

public class TitleVersionCache
{
    public int Id { get; set; }
    public int TitleNumber { get; set; }
    public DateTimeOffset IssueDate { get; set; }
    public string XmlContent { get; set; } = string.Empty;
    public int WordCount { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
    public string? Checksum { get; set; } // To detect if content has changed
}

public class TitleWordCountCache
{
    public int Id { get; set; }
    public int TitleNumber { get; set; }
    public DateTimeOffset Date { get; set; }
    public int WordCount { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
}
