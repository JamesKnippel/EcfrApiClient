using EcfrApi.Web.Services;
using EcfrApi.Web.Models;
using EcfrApi.Web.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace EcfrApi.Tests.Integration;

public class WordCountHistoryTests
{
    private readonly IEcfrClient _client;
    private readonly ITestOutputHelper _output;
    private const string TestSubAgencySlug = "national-telecommunications-and-information-administration";

    public WordCountHistoryTests(ITestOutputHelper output)
    {
        _output = output;
        var services = new ServiceCollection();
        services.AddDbContext<EcfrDbContext>(options => 
            options.UseInMemoryDatabase("TestDb"));
        services.AddScoped<ITitleCacheService, TitleCacheService>();
        services.AddHttpClient<IEcfrClient, EcfrClient>();
        
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        
        var provider = services.BuildServiceProvider();
        _client = provider.GetRequiredService<IEcfrClient>();
    }

    private void LogWordCountHistory(AgencyWordCountHistory history)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\n=== Word Count History Report ===");
        sb.AppendLine($"Agency: {history.Agency.Name} ({history.Agency.ShortName})");
        sb.AppendLine($"Period: {history.StartDate:yyyy-MM-dd} to {history.EndDate:yyyy-MM-dd}");
        sb.AppendLine("--------------------------------");

        foreach (var titleHistory in history.TitleHistories.OrderBy(t => t.TitleNumber))
        {
            sb.AppendLine($"\nTitle {titleHistory.TitleNumber}: {titleHistory.TitleName}");
            sb.AppendLine($"Total Words Added: {titleHistory.TotalWordsAdded:N0}");
            sb.AppendLine($"Average Words/Day: {titleHistory.AverageWordsPerDay:N2}");
            sb.AppendLine("\nSnapshots:");
            
            foreach (var snapshot in titleHistory.WordCounts)
            {
                sb.AppendLine($"  {snapshot.Date:yyyy-MM-dd}: {snapshot.WordCount:N0} words");
                if (snapshot.WordsAddedSinceLastSnapshot != 0)
                {
                    var change = snapshot.WordsAddedSinceLastSnapshot > 0 ? "+" : "";
                    sb.AppendLine($"    Change: {change}{snapshot.WordsAddedSinceLastSnapshot:N0} words");
                    sb.AppendLine($"    Rate: {snapshot.WordsPerDay:N2} words/day");
                }
            }
        }

        sb.AppendLine("\nAgency Summary");
        sb.AppendLine("--------------------------------");
        sb.AppendLine($"Total Words Added: {history.TotalWordsAdded:N0}");
        sb.AppendLine($"Average Words/Day: {history.AverageWordsPerDay:N2}");
        sb.AppendLine("================================");

        _output.WriteLine(sb.ToString());
    }

    [Fact]
    public async Task GetWordCountHistory_ForNTIA_ShouldShowChangesOverTime()
    {
        // Arrange
        var endDate = DateTimeOffset.Parse("2025-02-06"); // Latest issue date
        var startDate = DateTimeOffset.Parse("2024-01-01"); // One year ago

        // Act
        var history = await _client.GetAgencyWordCountHistoryAsync(
            TestSubAgencySlug, startDate, endDate);

        // Log the results
        LogWordCountHistory(history);

        // Assert
        history.Should().NotBeNull();
        history.Agency.Should().NotBeNull();
        history.Agency.Name.Should().Be("National Telecommunications and Information Administration");
        
        // Verify we have history for both titles
        history.TitleHistories.Should().HaveCount(2);
        history.TitleHistories.Should().Contain(t => t.TitleNumber == 15);
        history.TitleHistories.Should().Contain(t => t.TitleNumber == 47);

        // Each title should have snapshots
        foreach (var titleHistory in history.TitleHistories)
        {
            titleHistory.WordCounts.Should().NotBeEmpty();
            titleHistory.WordCounts.Should().BeInAscendingOrder(s => s.Date);
            
            // Each snapshot should have reasonable word counts
            foreach (var snapshot in titleHistory.WordCounts)
            {
                snapshot.WordCount.Should().BeGreaterThan(0);
                snapshot.WordCount.Should().BeLessThan(10_000_000);
                
                // Verify rate calculations for snapshots after the first one
                if (snapshot != titleHistory.WordCounts.First() && snapshot != titleHistory.WordCounts.Last())
                {
                    snapshot.DaysSinceLastSnapshot.Should().BeGreaterThan(0);
                    
                    if (snapshot.WordsAddedSinceLastSnapshot != 0)
                    {
                        snapshot.WordsPerDay.Should().BeApproximately(
                            (double)snapshot.WordsAddedSinceLastSnapshot / snapshot.DaysSinceLastSnapshot,
                            0.01);
                    }
                }
            }
        }
    }

    [Fact]
    public async Task GetWordCountHistory_WithInvalidSlug_ShouldThrowArgumentException()
    {
        // Arrange
        var invalidSlug = "invalid-agency-slug";
        var endDate = DateTimeOffset.Parse("2025-02-06");
        var startDate = DateTimeOffset.Parse("2024-01-01");

        // Act & Assert
        await _client.Invoking(c => c.GetAgencyWordCountHistoryAsync(invalidSlug, startDate, endDate))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Agency with slug 'invalid-agency-slug' not found*")
            .Where(ex => ex.Message.Contains("Please use one of the following slugs:"));
    }

    [Fact]
    public async Task GetWordCountHistory_WithInvalidDateRange_ShouldThrowArgumentException()
    {
        // Arrange
        var startDate = DateTimeOffset.Parse("2025-02-06");
        var endDate = DateTimeOffset.Parse("2024-01-01"); // End date before start date

        // Act & Assert
        await _client.Invoking(c => c.GetAgencyWordCountHistoryAsync(TestSubAgencySlug, startDate, endDate))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("Start date must be before end date");
    }
}
