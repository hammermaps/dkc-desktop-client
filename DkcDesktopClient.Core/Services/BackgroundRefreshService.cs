using System.Collections.Concurrent;
using DkcDesktopClient.Core.Api;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DkcDesktopClient.Core.Services;

/// <summary>
/// Hosted background service that periodically refreshes cached data without user interaction.
/// - Respects per-data-type intervals from <see cref="RefreshConfig"/>.
/// - Pauses automatically when the user is not authenticated.
/// - Exposes <see cref="DataRefreshed"/> so ViewModels can react to fresh data.
/// - Calling <see cref="NotifyUserActivity(string)"/> resets the timer for a key, preventing
///   unnecessary background fetches right after a manual load.
/// </summary>
public class BackgroundRefreshService : BackgroundService
{
    private readonly AuthService _authService;
    private readonly DkcApiFactory _apiFactory;
    private readonly DataCacheService _cache;
    private readonly RefreshConfig _config;
    private readonly ILogger<BackgroundRefreshService> _logger;

    /// <summary>
    /// Raised on the thread-pool when a background refresh for a data key completes.
    /// <c>args</c> contains the cache key that was refreshed.
    /// </summary>
    public event EventHandler<string>? DataRefreshed;

    // Tracks when each key was last successfully loaded (manual or background).
    // Background refresh only runs when Now >= lastLoad + interval.
    private readonly ConcurrentDictionary<string, DateTime> _lastLoaded = new(StringComparer.Ordinal);

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(10);

    public BackgroundRefreshService(
        AuthService authService,
        DkcApiFactory apiFactory,
        DataCacheService cache,
        IOptions<RefreshConfig> config,
        ILogger<BackgroundRefreshService> logger)
    {
        _authService = authService;
        _apiFactory  = apiFactory;
        _cache       = cache;
        _config      = config.Value;
        _logger      = logger;
    }

    /// <summary>
    /// Call this after a manual data load to defer the next background refresh for <paramref name="cacheKey"/>.
    /// </summary>
    public void NotifyUserActivity(string cacheKey)
    {
        _lastLoaded[cacheKey] = DateTime.UtcNow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BackgroundRefreshService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TickInterval, stoppingToken).ConfigureAwait(false);

            if (!_authService.IsAuthenticated)
                continue; // Pause when logged out

            var api = _apiFactory.Create(_authService.CurrentToken);
            var now = DateTime.UtcNow;

            await TryRefreshAsync(
                CacheKeys.KlimaStatus,
                _config.KlimaStatus,
                now,
                ct => api.GetKlimaRealtimeStatusAsync(ct),
                stoppingToken);

            await TryRefreshAsync(
                CacheKeys.DashboardData,
                _config.DashboardStats,
                now,
                ct => api.GetDashboardDataAsync(ct),
                stoppingToken);

            await TryRefreshAsync(
                CacheKeys.NeaDashboard,
                _config.DashboardStats,
                now,
                ct => api.GetNeaDashboardAsync(ct),
                stoppingToken);

            await TryRefreshAsync(
                CacheKeys.NotificationCount,
                _config.NotificationCount,
                now,
                ct => api.GetNotificationCountAsync(ct),
                stoppingToken);

            await TryRefreshAsync(
                CacheKeys.MmList,
                _config.MmList,
                now,
                ct => api.GetMmListAsync(ct: ct),
                stoppingToken);

            await TryRefreshAsync(
                CacheKeys.KeysInventory,
                CacheTtl.KeysInventory,
                now,
                ct => api.GetKeysInventoryAsync(ct),
                stoppingToken);

            await TryRefreshAsync(
                CacheKeys.NeaInspections,
                _config.NeaInspections,
                now,
                ct => api.GetNeaInspectionsAsync(ct: ct),
                stoppingToken);
        }

        _logger.LogInformation("BackgroundRefreshService stopped");
    }

    private async Task TryRefreshAsync<T>(
        string key,
        TimeSpan interval,
        DateTime now,
        Func<CancellationToken, Task<T>> fetcher,
        CancellationToken ct)
    {
        // Skip if this key was loaded (manually or via background) within the interval
        if (_lastLoaded.TryGetValue(key, out var last) && now - last < interval)
            return;

        // Only refresh when the in-memory cache is actually expired
        if (_cache.IsValid(key))
        {
            _lastLoaded[key] = now;
            return;
        }

        try
        {
            _cache.Invalidate(key);
            await _cache.GetOrFetchAsync(key, fetcher, interval, ct: ct);
            _lastLoaded[key] = DateTime.UtcNow;
            _logger.LogDebug("Background refresh completed: {Key}", key);
            DataRefreshed?.Invoke(this, key);
        }
        catch (OperationCanceledException)
        {
            // Shutting down – ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background refresh failed for key: {Key}", key);
        }
    }
}
