using Microsoft.AspNetCore.Mvc;
using EcfrApi.Web.Services;
using EcfrApi.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EcfrApi.Web.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly EcfrDbContext _context;
    private readonly ILogger<AdminController> _logger;
    private readonly TitleCacheUpdateService _updateService;

    public AdminController(
        EcfrDbContext context,
        ILogger<AdminController> logger,
        TitleCacheUpdateService updateService)
    {
        _context = context;
        _logger = logger;
        _updateService = updateService;
    }

    [HttpGet("cache/status")]
    public async Task<IActionResult> GetCacheStatus()
    {
        try
        {
            // Get the latest cache entry
            var latestEntry = await _context.TitleVersions
                .OrderByDescending(t => t.LastUpdated)
                .FirstOrDefaultAsync();

            if (latestEntry == null)
            {
                return Ok(new { status = "empty" });
            }

            // If the latest entry is less than 5 minutes old, consider the cache as being updated
            if (latestEntry.LastUpdated > DateTimeOffset.UtcNow.AddMinutes(-5))
            {
                return Ok(new { status = "updating" });
            }

            // Get cache statistics
            var totalEntries = await _context.TitleVersions.CountAsync();
            var distinctTitles = await _context.TitleVersions
                .Select(t => t.TitleNumber)
                .Distinct()
                .CountAsync();
            var oldestEntry = await _context.TitleVersions
                .OrderBy(t => t.LastUpdated)
                .Select(t => t.LastUpdated)
                .FirstOrDefaultAsync();

            return Ok(new
            {
                status = "completed",
                statistics = new
                {
                    totalEntries,
                    distinctTitles,
                    oldestEntry,
                    latestUpdate = latestEntry.LastUpdated
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache status");
            return StatusCode(500, new { status = "error", message = ex.Message });
        }
    }

    [HttpPost("cache/refresh")]
    public IActionResult TriggerCacheRefresh()
    {
        try
        {
            // Signal the background service to start a refresh
            _updateService.TriggerUpdate();
            return Ok(new { status = "started" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering cache refresh");
            return StatusCode(500, new { status = "error", message = ex.Message });
        }
    }
}
