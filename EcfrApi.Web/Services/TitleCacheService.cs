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
        var cached = await _context.TitleWordCounts
            .FirstOrDefaultAsync(t => t.TitleNumber == titleNumber && t.Date.Date == date.Date);

        if (cached != null)
        {
            return cached.WordCount;
        }

        // If we don't have the exact date, try to find the closest previous date
        var closestPrevious = await _context.TitleWordCounts
            .Where(t => t.TitleNumber == titleNumber && t.Date.Date <= date.Date)
            .OrderByDescending(t => t.Date)
            .FirstOrDefaultAsync();

        return closestPrevious?.WordCount ?? 0;
    }

    public async Task UpdateTitleCacheAsync(int titleNumber, DateTimeOffset date, string xmlContent)
    {
        var checksum = ComputeChecksum(xmlContent);
        var wordCount = CountWordsInXml(xmlContent);

        // Update or create TitleVersionCache
        var existingVersion = await _context.TitleVersions
            .FirstOrDefaultAsync(t => t.TitleNumber == titleNumber && t.IssueDate.Date == date.Date);

        if (existingVersion != null)
        {
            if (existingVersion.Checksum != checksum)
            {
                existingVersion.XmlContent = xmlContent;
                existingVersion.WordCount = wordCount;
                existingVersion.Checksum = checksum;
                existingVersion.LastUpdated = DateTimeOffset.UtcNow;
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
        var existingWordCount = await _context.TitleWordCounts
            .FirstOrDefaultAsync(t => t.TitleNumber == titleNumber && t.Date.Date == date.Date);

        if (existingWordCount != null)
        {
            existingWordCount.WordCount = wordCount;
            existingWordCount.LastUpdated = DateTimeOffset.UtcNow;
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
        return await _context.TitleWordCounts
            .AnyAsync(t => t.TitleNumber == titleNumber && t.Date.Date == date.Date);
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
