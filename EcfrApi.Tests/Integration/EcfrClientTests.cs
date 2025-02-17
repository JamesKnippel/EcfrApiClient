using EcfrApi.Web.Services;
using EcfrApi.Web.Models;
using EcfrApi.Web.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using Xunit;

namespace EcfrApi.Tests.Integration;

public class EcfrClientTests
{
    private readonly IEcfrClient _client;
    private const string TestAgencySlug = "commerce-department";
    private const string TestSubAgencySlug = "national-telecommunications-and-information-administration";

    public EcfrClientTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<EcfrDbContext>(options => 
            options.UseInMemoryDatabase("TestDb"));
        services.AddScoped<ITitleCacheService, TitleCacheService>();
        services.AddHttpClient<IEcfrClient, EcfrClient>();
        var provider = services.BuildServiceProvider();
        _client = provider.GetRequiredService<IEcfrClient>();
    }

    [Fact]
    public async Task GetTitles_ShouldReturnTitles()
    {
        // Act
        var result = await _client.GetTitlesAsync();

        // Assert
        result.Should().NotBeNull();
        result.Titles.Should().NotBeEmpty();
        result.Titles.Should().Contain(t => t.Number > 0);
        result.Meta.Should().NotBeNull();
        result.Meta.Date.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetAgencies_ShouldReturnAgencies()
    {
        // Act
        var result = await _client.GetAgenciesAsync();

        // Assert
        result.Should().NotBeNull();
        result.Agencies.Should().NotBeEmpty();
        
        // Find Department of Commerce
        var commerce = result.Agencies.Should().Contain(a => a.Slug == TestAgencySlug).Subject;
        commerce.Name.Should().Be("Department of Commerce");
        commerce.ShortName.Should().Be("DOC");
        commerce.Children.Should().NotBeEmpty();
        
        // Find NTIA within Department of Commerce
        var ntia = commerce.Children.Should().Contain(a => a.Slug == TestSubAgencySlug).Subject;
        ntia.Name.Should().Be("National Telecommunications and Information Administration");
        ntia.ShortName.Should().Be("NTIA");
        ntia.CfrReferences.Should().NotBeEmpty();
        ntia.CfrReferences.Should().Contain(r => r.Title == 15 && r.Chapter == "XXIII");
        ntia.CfrReferences.Should().Contain(r => r.Title == 47 && r.Chapter == "III");
        ntia.CfrReferences.Should().Contain(r => r.Title == 47 && r.Chapter == "IV");
    }

    [Fact]
    public async Task GetAgencyBySlug_WithValidParentAgencySlug_ShouldReturnAgency()
    {
        // Act
        var result = await _client.GetAgencyBySlugAsync(TestAgencySlug);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Department of Commerce");
        result.ShortName.Should().Be("DOC");
        result.Children.Should().NotBeEmpty();
        result.CfrReferences.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetAgencyBySlug_WithValidSubAgencySlug_ShouldReturnAgency()
    {
        // Act
        var result = await _client.GetAgencyBySlugAsync(TestSubAgencySlug);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("National Telecommunications and Information Administration");
        result.ShortName.Should().Be("NTIA");
        result.CfrReferences.Should().NotBeEmpty();
        result.CfrReferences.Should().Contain(r => r.Title == 15 && r.Chapter == "XXIII");
        result.CfrReferences.Should().Contain(r => r.Title == 47 && r.Chapter == "III");
        result.CfrReferences.Should().Contain(r => r.Title == 47 && r.Chapter == "IV");
    }

    [Fact]
    public async Task GetAgencyBySlug_WithInvalidSlug_ShouldThrowArgumentException()
    {
        // Arrange
        var invalidSlug = "invalid-agency-slug";

        // Act & Assert
        await _client.Invoking(c => c.GetAgencyBySlugAsync(invalidSlug))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Agency with slug 'invalid-agency-slug' not found*");
    }

    [Fact]
    public async Task GetAgencyTitles_WithValidSubAgencySlug_ShouldReturnTitles()
    {
        // Act
        var result = await _client.GetAgencyTitlesAsync(TestSubAgencySlug);

        // Assert
        result.Should().NotBeNull();
        result.Agency.Should().NotBeNull();
        result.Agency.Name.Should().Be("National Telecommunications and Information Administration");
        result.Agency.ShortName.Should().Be("NTIA");
        
        result.Titles.Should().NotBeEmpty();
        result.Titles.Should().Contain(t => t.Number == 15);
        result.Titles.Should().Contain(t => t.Number == 47);
        
        // Each title should have a latest issue date
        result.Titles.Should().OnlyContain(t => !string.IsNullOrEmpty(t.LatestIssueDate));
    }

    [Fact]
    public async Task GetAgencyTitlesWithWordCount_WithValidSubAgencySlug_ShouldReturnTitlesWithWordCounts()
    {
        // Act
        var result = await _client.GetAgencyTitlesWithWordCountAsync(TestSubAgencySlug);

        // Assert
        result.Should().NotBeNull();
        result.Agency.Should().NotBeNull();
        result.Agency.Name.Should().Be("National Telecommunications and Information Administration");
        result.Agency.ShortName.Should().Be("NTIA");
        
        result.Titles.Should().NotBeEmpty();
        result.Titles.Should().OnlyContain(t => t.WordCount > 0);
        
        // Each title should have a word count
        foreach (var title in result.Titles)
        {
            title.WordCount.Should().BeGreaterThan(0, $"Title {title.Number} should have words");
        }
        
        // Total word count should be sum of individual title word counts
        result.TotalWordCount.Should().Be(result.Titles.Sum(t => t.WordCount));
    }

    [Fact]
    public async Task GetAgencyTitlesWithWordCount_WithInvalidSlug_ShouldThrowArgumentException()
    {
        // Arrange
        var invalidSlug = "invalid-agency-slug";

        // Act & Assert
        await _client.Invoking(c => c.GetAgencyTitlesWithWordCountAsync(invalidSlug))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Agency with slug 'invalid-agency-slug' not found*");
    }

    [Fact]
    public async Task GetTitleXml_WithValidTitle_ShouldReturnXmlContent()
    {
        // Arrange
        var titleNumber = 1; // Using Title 1 as an example

        // Act
        var result = await _client.GetTitleXmlAsync(titleNumber);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("<?xml");
    }

    [Fact]
    public async Task GetTitleXml_WithInvalidTitle_ShouldThrowArgumentException()
    {
        // Arrange
        var titleNumber = 999; // Invalid title number

        // Act & Assert
        await _client.Invoking(c => c.GetTitleXmlAsync(titleNumber))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Title {titleNumber} not found");
    }
}
