using EcfrApi.Web.Models;
using EcfrApi.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net.Http;
using Xunit;

namespace EcfrApi.Tests.Unit;

public class WordCountTests
{
    private readonly IEcfrClient _client;
    private readonly Mock<ITitleCacheService> _mockCacheService;
    private readonly Mock<ILogger<EcfrClient>> _mockLogger;

    public WordCountTests()
    {
        _mockCacheService = new Mock<ITitleCacheService>();
        _mockLogger = new Mock<ILogger<EcfrClient>>();
        var httpClient = new HttpClient();
        _client = new EcfrClient(httpClient, _mockCacheService.Object, _mockLogger.Object);
    }

    [Fact]
    public void AgencyTitlesResult_TotalWordCount_ShouldSumAllTitleWordCounts()
    {
        // Arrange
        var result = new AgencyTitlesResult
        {
            Agency = new Agency { Name = "Test Agency" },
            Titles = new List<Title>
            {
                new Title { Number = 1, WordCount = 100 },
                new Title { Number = 2, WordCount = 200 },
                new Title { Number = 3, WordCount = 300 }
            }
        };

        // Act & Assert
        result.TotalWordCount.Should().Be(600);
    }

    [Fact]
    public void AgencyTitlesResult_TotalWordCount_ShouldBeZeroForEmptyTitles()
    {
        // Arrange
        var result = new AgencyTitlesResult
        {
            Agency = new Agency { Name = "Test Agency" },
            Titles = new List<Title>()
        };

        // Act & Assert
        result.TotalWordCount.Should().Be(0);
    }

    [Fact]
    public void AgencyTitlesResult_TotalWordCount_ShouldHandleNullTitles()
    {
        // Arrange
        var result = new AgencyTitlesResult
        {
            Agency = new Agency { Name = "Test Agency" },
            Titles = null!
        };

        // Act & Assert
        result.TotalWordCount.Should().Be(0);
    }

    [Fact]
    public async Task CountWordsInXml_WithSimpleXml_ShouldCountWords()
    {
        // Arrange
        var xml = "<root>This is a test</root>";

        // Act
        var wordCount = await _client.CountWordsInXml(xml);

        // Assert
        wordCount.Should().Be(4);
    }

    [Fact]
    public async Task CountWordsInXml_WithNestedXml_ShouldCountWords()
    {
        // Arrange
        var xml = @"
            <root>
                <child>This is</child>
                <child>a nested</child>
                <child>test case</child>
            </root>";

        // Act
        var wordCount = await _client.CountWordsInXml(xml);

        // Assert
        wordCount.Should().Be(6);
    }

    [Fact]
    public async Task CountWordsInXml_WithAttributes_ShouldIgnoreAttributes()
    {
        // Arrange
        var xml = @"
            <root attr1='ignore' attr2='these'>
                <child id='123'>Count these words</child>
                <child class='test'>But not attributes</child>
            </root>";

        // Act
        var wordCount = await _client.CountWordsInXml(xml);

        // Assert
        wordCount.Should().Be(6);
    }
}
