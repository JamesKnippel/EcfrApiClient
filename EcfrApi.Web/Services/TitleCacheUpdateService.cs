using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EcfrApi.Web.Services;

public class TitleCacheUpdateService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TitleCacheUpdateService> _logger;
    private readonly TimeSpan _updateInterval = TimeSpan.FromHours(24); // Update daily
    private readonly SemaphoreSlim _updateTrigger = new SemaphoreSlim(0);
    private bool _isUpdating;

    public TitleCacheUpdateService(
        IServiceProvider services,
        ILogger<TitleCacheUpdateService> logger)
    {
        _services = services;
        _logger = logger;
    }

    public void TriggerUpdate()
    {
        if (!_isUpdating)
        {
            _updateTrigger.Release();
        }
    }

    public bool IsUpdating => _isUpdating;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait for either the update interval or a manual trigger
                var timeoutTask = Task.Delay(_updateInterval, stoppingToken);
                var triggerTask = _updateTrigger.WaitAsync(stoppingToken);
                
                await Task.WhenAny(timeoutTask, triggerTask);

                _isUpdating = true;
                await UpdateCacheAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while updating title cache");
            }
            finally
            {
                _isUpdating = false;
            }
        }
    }

    private async Task UpdateCacheAsync(CancellationToken stoppingToken)
    {
        using var scope = _services.CreateScope();
        var ecfrClient = scope.ServiceProvider.GetRequiredService<IEcfrClient>();
        var cacheService = scope.ServiceProvider.GetRequiredService<ITitleCacheService>();

        // Get all titles
        var titles = await ecfrClient.GetTitlesAsync();
        var totalTitles = titles.Titles.Count;
        var processedTitles = 0;
        
        foreach (var title in titles.Titles)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            try
            {
                var latestDate = DateTimeOffset.Parse(title.LatestIssueDate);
                
                // Only update if not already cached
                if (!await cacheService.IsCachedAsync(title.Number, latestDate))
                {
                    var xml = await ecfrClient.GetTitleXmlForDateAsync(title.Number, latestDate);
                    await cacheService.UpdateTitleCacheAsync(title.Number, latestDate, xml);
                    
                    processedTitles++;
                    _logger.LogInformation(
                        "Updated cache for Title {TitleNumber} at date {Date} ({Progress}% complete)",
                        title.Number, latestDate, (processedTitles * 100) / totalTitles);

                    // Add delay to avoid rate limiting
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error updating cache for Title {TitleNumber}",
                    title.Number);
            }
        }

        _logger.LogInformation("Cache update completed. Processed {Count} titles", processedTitles);
    }
}
