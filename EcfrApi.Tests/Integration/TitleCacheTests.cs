using EcfrApi.Web.Services;
using EcfrApi.Web.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace EcfrApi.Tests.Integration;

public class TitleCacheTests : IDisposable
{
    private readonly IEcfrClient _client;
    private readonly ITitleCacheService _cacheService;
    private readonly EcfrDbContext _dbContext;
    private readonly ITestOutputHelper _output;
    private const string TestAgencySlug = "forest-service";

    public TitleCacheTests(ITestOutputHelper output)
    {
        _output = output;
        var services = new ServiceCollection();

        // Configure database
        services.AddDbContext<EcfrDbContext>(options =>
            options.UseSqlite("Data Source=:memory:"));

        // Add services
        services.AddHttpClient<IEcfrClient, EcfrClient>();
        services.AddScoped<ITitleCacheService, TitleCacheService>();
        
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        
        var provider = services.BuildServiceProvider();

        // Initialize services
        _dbContext = provider.GetRequiredService<EcfrDbContext>();
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureDeleted(); // Clear any existing database
        _dbContext.Database.Migrate(); // Apply migrations instead of EnsureCreated

        _client = provider.GetRequiredService<IEcfrClient>();
        _cacheService = provider.GetRequiredService<ITitleCacheService>();
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetWordCountHistory_ShouldCacheResults()
    {
        // Arrange
        var endDate = DateTimeOffset.Parse("2025-02-01");  
        var startDate = endDate.AddYears(-1);

        // Act 1: First request should hit the API and cache results
        _output.WriteLine("Making first request...");
        var firstResult = await _client.GetAgencyWordCountHistoryAsync(
            TestAgencySlug, startDate, endDate);

        // Assert 1: Should have results
        firstResult.Should().NotBeNull();
        firstResult.TitleHistories.Should().NotBeEmpty();

        // Verify data was cached
        var title36 = firstResult.TitleHistories.FirstOrDefault(t => t.TitleNumber == 36);
        title36.Should().NotBeNull();
        
        var latestCount = title36!.WordCounts.MaxBy(w => w.Date);
        latestCount.Should().NotBeNull();

        var isCached = await _cacheService.IsCachedAsync(36, latestCount!.Date);
        isCached.Should().BeTrue("Word count should be cached after first request");

        // Act 2: Second request should use cached results
        _output.WriteLine("Making second request...");
        var secondResult = await _client.GetAgencyWordCountHistoryAsync(
            TestAgencySlug, startDate, endDate);

        // Assert 2: Results should match
        secondResult.Should().NotBeNull();
        secondResult.TitleHistories.Should().BeEquivalentTo(
            firstResult.TitleHistories,
            "Second request should return same data from cache");
    }

    [Fact]
    public async Task GetWordCount_ShouldHandleDateRanges()
    {
        // Arrange
        var targetDate = DateTimeOffset.Parse("2025-02-10");
        var olderDate = targetDate.AddDays(-30);
        var newerDate = targetDate.AddDays(30);

        // Act 1: Cache word count for target date
        var wordCount = 1000000;
        await _cacheService.UpdateTitleCacheAsync(36, targetDate, GenerateXmlWithWordCount(wordCount));

        // Assert 1: Exact date match should return the cached word count
        var exactMatch = await _cacheService.GetWordCountAsync(36, targetDate);
        exactMatch.Should().Be(wordCount, "Should return exact word count for matching date");

        // Assert 2: Non-exact dates should throw KeyNotFoundException
        await Assert.ThrowsAsync<KeyNotFoundException>(() => 
            _cacheService.GetWordCountAsync(36, olderDate));
        
        await Assert.ThrowsAsync<KeyNotFoundException>(() => 
            _cacheService.GetWordCountAsync(36, newerDate));
    }

    private string GenerateXmlWithWordCount(int wordCount)
    {
        var words = string.Join(" ", Enumerable.Repeat("word", wordCount));
        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<TITLE>{words}</TITLE>";
    }
}
