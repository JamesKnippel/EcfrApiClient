using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using EcfrApi.Web.Data;
using EcfrApi.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EcfrApi.Web.Services;

public interface ITitleCacheService
{
    Task<int> GetWordCountAsync(int titleNumber, DateTimeOffset date);
    Task UpdateTitleCacheAsync(int titleNumber, DateTimeOffset date, string xmlContent);
    Task<bool> IsCachedAsync(int titleNumber, DateTimeOffset date);
}

public class TitleCacheService : ITitleCacheService
{
    private readonly EcfrDbContext _context;
    private readonly ILogger<TitleCacheService> _logger;

    public TitleCacheService(EcfrDbContext context, ILogger<TitleCacheService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<int> GetWordCountAsync(int titleNumber, DateTimeOffset date)
    {
        var targetDate = date.Date;
        
        // Get all relevant entries and evaluate in memory
        var entries = await _context.TitleWordCounts
            .Where(t => t.TitleNumber == titleNumber)
            .AsNoTracking()
            .ToListAsync();

        // Try to find exact date match
        var exactMatch = entries.FirstOrDefault(t => t.Date.Date == targetDate);
        if (exactMatch != null)
        {
            return exactMatch.WordCount;
        }

        // If no exact match, find closest previous date
        var closestPrevious = entries
            .Where(t => t.Date.Date <= targetDate)
            .OrderByDescending(t => t.Date)
            .FirstOrDefault();

        return closestPrevious?.WordCount ?? 0;
    }

    public async Task UpdateTitleCacheAsync(int titleNumber, DateTimeOffset date, string xmlContent)
    {
        var checksum = ComputeChecksum(xmlContent);
        var wordCount = CountWordsInXml(xmlContent);
        var targetDate = date.Date;

        // Update or create TitleVersionCache
        var existingVersions = await _context.TitleVersions
            .Where(t => t.TitleNumber == titleNumber)
            .AsNoTracking()
            .ToListAsync();

        var existingVersion = existingVersions.FirstOrDefault(t => t.IssueDate.Date == targetDate);

        if (existingVersion != null)
        {
            if (existingVersion.Checksum != checksum)
            {
                existingVersion.XmlContent = xmlContent;
                existingVersion.WordCount = wordCount;
                existingVersion.Checksum = checksum;
                existingVersion.LastUpdated = DateTimeOffset.UtcNow;
                _context.TitleVersions.Update(existingVersion);
            }
        }
        else
        {
            await _context.TitleVersions.AddAsync(new TitleVersionCache
            {
                TitleNumber = titleNumber,
                IssueDate = date,
                XmlContent = xmlContent,
                WordCount = wordCount,
                Checksum = checksum,
                LastUpdated = DateTimeOffset.UtcNow
            });
        }

        // Update or create TitleWordCountCache
        var existingWordCounts = await _context.TitleWordCounts
            .Where(t => t.TitleNumber == titleNumber)
            .AsNoTracking()
            .ToListAsync();

        var existingWordCount = existingWordCounts.FirstOrDefault(t => t.Date.Date == targetDate);

        if (existingWordCount != null)
        {
            existingWordCount.WordCount = wordCount;
            existingWordCount.LastUpdated = DateTimeOffset.UtcNow;
            _context.TitleWordCounts.Update(existingWordCount);
        }
        else
        {
            await _context.TitleWordCounts.AddAsync(new TitleWordCountCache
            {
                TitleNumber = titleNumber,
                Date = date,
                WordCount = wordCount,
                LastUpdated = DateTimeOffset.UtcNow
            });
        }

        await _context.SaveChangesAsync();
    }

    public async Task<bool> IsCachedAsync(int titleNumber, DateTimeOffset date)
    {
        // Convert input date to a comparable format
        var targetDate = date.Date;
        
        // Get all relevant entries and evaluate in memory
        var entries = await _context.TitleWordCounts
            .Where(t => t.TitleNumber == titleNumber)
            .AsNoTracking()
            .ToListAsync();

        // Check for exact date match
        if (entries.Any(t => t.Date.Date == targetDate))
        {
            return true;
        }

        // Check for any previous date
        return entries.Any(t => t.Date.Date <= targetDate);
    }

    private string ComputeChecksum(string content)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private int CountWordsInXml(string xml)
    {
        // Remove XML tags
        var text = System.Text.RegularExpressions.Regex.Replace(xml, "<[^>]+>", " ");
        
        // Remove extra whitespace
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        
        // Split and count words
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
