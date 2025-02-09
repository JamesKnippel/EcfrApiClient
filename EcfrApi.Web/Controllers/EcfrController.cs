using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using EcfrApi.Web.Services;
using EcfrApi.Web.Models;

namespace EcfrApi.Web.Controllers;

[ApiController]
[Route("[controller]")]
public class EcfrController : ControllerBase
{
    private readonly IEcfrClient _client;
    private readonly ILogger<EcfrController> _logger;

    public EcfrController(IEcfrClient client, ILogger<EcfrController> logger)
    {
        _client = client;
        _logger = logger;
    }

    [HttpGet("titles")]
    public async Task<ActionResult<TitlesResponse>> GetTitles()
    {
        try
        {
            return await _client.GetTitlesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting titles");
            return StatusCode(500, "An error occurred while retrieving titles");
        }
    }

    [HttpGet("agencies")]
    public async Task<ActionResult<AgenciesResponse>> GetAgencies()
    {
        try
        {
            return await _client.GetAgenciesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting agencies");
            return StatusCode(500, "An error occurred while retrieving agencies");
        }
    }

    [HttpGet("agencies/{slug}")]
    public async Task<ActionResult<Agency>> GetAgencyBySlug(string slug)
    {
        try
        {
            var response = await _client.GetAgenciesAsync();
            var agency = FindAgencyBySlug(response.Agencies, slug);
            
            if (agency == null)
            {
                return NotFound($"Agency with slug '{slug}' not found");
            }

            return agency;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting agency by slug");
            return StatusCode(500, "An error occurred while retrieving the agency");
        }
    }

    private Agency? FindAgencyBySlug(List<Agency> agencies, string slug)
    {
        foreach (var agency in agencies)
        {
            if (agency.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase))
            {
                return agency;
            }

            if (agency.Children != null)
            {
                var childAgency = FindAgencyBySlug(agency.Children, slug);
                if (childAgency != null)
                {
                    return childAgency;
                }
            }
        }

        return null;
    }

    [HttpGet("title/{titleNumber}/xml")]
    public async Task<ActionResult<string>> GetTitleXml(int titleNumber)
    {
        try
        {
            var result = await _client.GetTitleXmlAsync(titleNumber);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Title {TitleNumber} not found", titleNumber);
            return NotFound($"Title {titleNumber} not found");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Error with title {TitleNumber}: {Message}", titleNumber, ex.Message);
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving title XML for title {TitleNumber}", titleNumber);
            return StatusCode(500, $"An error occurred while retrieving XML for title {titleNumber}");
        }
    }

    [HttpGet("agencies/{slug}/titles")]
    public async Task<ActionResult<AgencyTitlesResult>> GetAgencyTitles(string slug)
    {
        try
        {
            return await _client.GetAgencyTitlesWithWordCountAsync(slug);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid agency slug: {Slug}", slug);
            return NotFound($"Agency with slug '{slug}' not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting titles for agency {Slug}", slug);
            return StatusCode(500, "An error occurred while retrieving agency titles");
        }
    }

    [HttpGet("agencies/{slug}/word-count-history")]
    public async Task<ActionResult<AgencyWordCountHistory>> GetAgencyWordCountHistory(
        string slug,
        [FromQuery] DateTimeOffset? startDate = null,
        [FromQuery] DateTimeOffset? endDate = null,
        [FromQuery] int intervalDays = 90)
    {
        try
        {
            startDate ??= DateTimeOffset.UtcNow.AddYears(-1);
            endDate ??= DateTimeOffset.UtcNow;

            return await _client.GetAgencyWordCountHistoryAsync(slug, startDate.Value, endDate.Value, intervalDays);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting agency word count history");
            return StatusCode(500, "An error occurred while retrieving agency word count history");
        }
    }
}
