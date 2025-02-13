using EcfrApi.Web.Services;
using EcfrApi.Web.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace EcfrApi.Tests.Integration;

public class WordCountIntegrationTests
{
    private readonly IEcfrClient _client;
    private const string TestSubAgencySlug = "national-telecommunications-and-information-administration";
    private readonly ITestOutputHelper _output;

    public WordCountIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        var services = new ServiceCollection();
        services.AddHttpClient<IEcfrClient, EcfrClient>();
        var provider = services.BuildServiceProvider();
        _client = provider.GetRequiredService<IEcfrClient>();
    }

    private void LogWordCounts(AgencyTitlesResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\n=== Word Count Report ===");
        sb.AppendLine($"Agency: {result.Agency.Name} ({result.Agency.ShortName})");
        sb.AppendLine("------------------------");
        
        foreach (var title in result.Titles.OrderBy(t => t.Number))
        {
            var wordCount = title.WordCount.ToString("N0"); // Format with thousands separator
            sb.AppendLine($"Title {title.Number}: {wordCount} words");
        }
        
        sb.AppendLine("------------------------");
        sb.AppendLine($"Total Words: {result.TotalWordCount:N0}");
        sb.AppendLine("======================");
        
        _output.WriteLine(sb.ToString());
    }

    [Fact]
    public async Task GetAgencyTitlesWithWordCount_ForNTIA_ShouldReturnExpectedTitles()
    {
        // Act
        var result = await _client.GetAgencyTitlesWithWordCountAsync(TestSubAgencySlug);

        // Log word counts
        LogWordCounts(result);

        // Assert
        result.Should().NotBeNull();
        result.Agency.Should().NotBeNull();
        result.Agency.Name.Should().Be("National Telecommunications and Information Administration");
        result.Agency.ShortName.Should().Be("NTIA");
        
        // Verify expected titles
        result.Titles.Should().NotBeEmpty();
        var titleNumbers = result.Titles.Select(t => t.Number).ToList();
        titleNumbers.Should().Contain(new[] { 15, 47 });
    }

    [Fact]
    public async Task GetAgencyTitlesWithWordCount_ForNTIA_ShouldHaveReasonableWordCounts()
    {
        // Act
        var result = await _client.GetAgencyTitlesWithWordCountAsync(TestSubAgencySlug);

        // Log word counts
        LogWordCounts(result);

        // Assert
        foreach (var title in result.Titles)
        {
            // Each title should have a reasonable number of words
            title.WordCount.Should().BeGreaterThan(0, 
                $"Title {title.Number} should have words");

            // Word count should be reasonable for a CFR title
            title.WordCount.Should().BeLessThan(10_000_000,
                $"Title {title.Number} word count seems unreasonably high");
        }

        // Total should be sum of parts
        result.TotalWordCount.Should().Be(result.Titles.Sum(t => t.WordCount));
    }

    [Fact]
    public async Task GetAgencyTitlesWithWordCount_ForNTIA_Title15And47ShouldDiffer()
    {
        // Act
        var result = await _client.GetAgencyTitlesWithWordCountAsync(TestSubAgencySlug);

        // Log word counts
        LogWordCounts(result);

        // Get specific titles
        var title15 = result.Titles.FirstOrDefault(t => t.Number == 15);
        var title47 = result.Titles.FirstOrDefault(t => t.Number == 47);

        // Assert
        title15.Should().NotBeNull("Title 15 should be present");
        title47.Should().NotBeNull("Title 47 should be present");

        // Log specific title comparison
        _output.WriteLine("\n=== Title Comparison ===");
        _output.WriteLine($"Title 15: {title15!.WordCount:N0} words");
        _output.WriteLine($"Title 47: {title47!.WordCount:N0} words");
        _output.WriteLine($"Difference: {Math.Abs(title15.WordCount - title47.WordCount):N0} words");

        // Titles should have different word counts as they are different regulations
        title15.WordCount.Should().NotBe(title47.WordCount,
            "Title 15 and 47 should have different word counts");
    }

    [Fact]
    public async Task GetAgencyTitlesWithWordCount_WithInvalidSlug_ShouldThrowArgumentException()
    {
        // Arrange
        var invalidSlug = "invalid-agency-slug";

        // Act & Assert
        await _client.Invoking(c => c.GetAgencyTitlesWithWordCountAsync(invalidSlug))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Agency with slug '{invalidSlug}' not found");
    }
}
