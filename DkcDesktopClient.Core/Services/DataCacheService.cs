using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DkcDesktopClient.Core.Services;

/// <summary>
/// Thread-safe, TTL-based in-memory cache with optional JSON disk persistence.
/// Supports generic typed values and per-key async fetch coalescing via SemaphoreSlim.
/// </summary>
public class DataCacheService
{
    private sealed record CacheEntry(object? Value, DateTime ExpiresAt);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly ILogger<DataCacheService> _logger;
    private readonly string? _cacheDir;

    public DataCacheService(ILogger<DataCacheService> logger, string? cacheDir = null)
    {
        _logger = logger;
        _cacheDir = cacheDir;
        if (_cacheDir != null)
            Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>
    /// Returns the cached value if still valid, otherwise calls <paramref name="fetcher"/>
    /// to obtain a fresh value, stores it with the given TTL, and returns it.
    /// Concurrent callers for the same key are coalesced: only one fetch is executed.
    /// </summary>
    public async Task<T?> GetOrFetchAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> fetcher,
        TimeSpan ttl,
        bool persistToDisk = false,
        CancellationToken ct = default)
    {
        // Fast path: valid in-memory entry
        if (_cache.TryGetValue(key, out var entry) && DateTime.UtcNow < entry.ExpiresAt)
        {
            _logger.LogDebug("Cache hit (memory): {Key}", key);
            return (T?)entry.Value;
        }

        // Per-key lock to coalesce concurrent fetches
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_cache.TryGetValue(key, out entry) && DateTime.UtcNow < entry.ExpiresAt)
            {
                _logger.LogDebug("Cache hit (after lock): {Key}", key);
                return (T?)entry.Value;
            }

            // Try loading from disk if in-memory is empty (e.g. on startup)
            if (!_cache.ContainsKey(key) && persistToDisk && _cacheDir != null)
            {
                var diskValue = TryLoadFromDisk<T>(key);
                if (diskValue is not null)
                {
                    _logger.LogDebug("Cache hit (disk): {Key}", key);
                    // Store with an already-expired timestamp so the next call will
                    // refresh via the normal semaphore-protected path.
                    _cache[key] = new CacheEntry(diskValue, DateTime.MinValue);
                    return diskValue;
                }
            }

            return await FetchAndStoreAsync(key, fetcher, ttl, persistToDisk, ct);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<T?> FetchAndStoreAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> fetcher,
        TimeSpan ttl,
        bool persistToDisk,
        CancellationToken ct)
    {
        _logger.LogDebug("Cache miss – fetching: {Key}", key);
        var value = await fetcher(ct);
        _cache[key] = new CacheEntry(value, DateTime.UtcNow + ttl);
        if (persistToDisk && _cacheDir != null)
            TrySaveToDisk(key, value);
        return value;
    }

    /// <summary>Removes a single entry from the in-memory cache (disk entry is not removed).</summary>
    public void Invalidate(string key)
    {
        _cache.TryRemove(key, out _);
        // Also remove the semaphore so it can be garbage-collected.
        if (_locks.TryRemove(key, out var sem))
            sem.Dispose();
        _logger.LogDebug("Cache invalidated: {Key}", key);
    }

    /// <summary>Removes all entries from the in-memory cache.</summary>
    public void InvalidateAll()
    {
        _cache.Clear();
        // Dispose and remove all per-key semaphores.
        foreach (var key in _locks.Keys.ToList())
        {
            if (_locks.TryRemove(key, out var sem))
                sem.Dispose();
        }
        _logger.LogDebug("Cache fully invalidated");
    }

    /// <summary>Returns true if a valid (non-expired) entry exists for <paramref name="key"/>.</summary>
    public bool IsValid(string key)
        => _cache.TryGetValue(key, out var e) && DateTime.UtcNow < e.ExpiresAt;

    // ── Disk helpers ──────────────────────────────────────────────────────────

    private void TrySaveToDisk<T>(string key, T? value)
    {
        try
        {
            var path = GetDiskPath(key);
            var json = JsonSerializer.Serialize(value);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist cache entry to disk: {Key}", key);
        }
    }

    private T? TryLoadFromDisk<T>(string key)
    {
        try
        {
            var path = GetDiskPath(key);
            if (!File.Exists(path)) return default;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load cache entry from disk: {Key}", key);
            return default;
        }
    }

    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    private string GetDiskPath(string key)
    {
        // Sanitise key to a safe file name
        var safeName = string.Concat(key.Select(c => InvalidFileNameChars.Contains(c) ? '_' : c));
        return Path.Combine(_cacheDir!, $"{safeName}.json");
    }
}

/// <summary>Well-known cache keys used throughout the application.</summary>
public static class CacheKeys
{
    public const string DashboardData    = "dashboard_data";
    public const string NeaSystems       = "nea_systems";
    public const string NeaInspections   = "nea_inspections";
    public const string MmList           = "mm_list";
    public const string BuildingList     = "building_list";
    public const string KlimaDevices     = "klima_devices";
    public const string KlimaStatus      = "klima_status";
    public const string KeysInventory    = "keys_inventory";
    public const string ProjectsList     = "projects_list";
    public const string UsersList        = "users_list";
    public const string Notifications      = "notifications";
    public const string NotificationCount  = "notification_count";
}

/// <summary>Default TTL values per data type (from the ROADMAP specification).</summary>
public static class CacheTtl
{
    public static readonly TimeSpan DashboardStats   = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan NeaSystems       = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan NeaInspections   = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan MmList           = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan BuildingList     = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan KlimaDevices     = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan KlimaStatus      = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan KeysInventory    = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ProjectsList     = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan UsersList        = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Notifications    = TimeSpan.FromSeconds(60);
}
