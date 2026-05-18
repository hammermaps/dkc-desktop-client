using DkcDesktopClient.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DkcDesktopClient.Tests;

public class DataCacheServiceTests
{
    private static DataCacheService Create() =>
        new(NullLogger<DataCacheService>.Instance);

    // ── GetOrFetchAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrFetchAsync_CallsFetcherOnFirstCall()
    {
        var svc = Create();
        var callCount = 0;

        var result = await svc.GetOrFetchAsync(
            "key1",
            _ => { callCount++; return Task.FromResult(42); },
            TimeSpan.FromMinutes(1));

        Assert.Equal(42, result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetOrFetchAsync_ReturnsCachedValueWithinTtl()
    {
        var svc = Create();
        var callCount = 0;

        await svc.GetOrFetchAsync("key2", _ => { callCount++; return Task.FromResult("hello"); }, TimeSpan.FromMinutes(1));
        var second = await svc.GetOrFetchAsync("key2", _ => { callCount++; return Task.FromResult("world"); }, TimeSpan.FromMinutes(1));

        Assert.Equal("hello", second);
        Assert.Equal(1, callCount); // fetcher called only once
    }

    [Fact]
    public async Task GetOrFetchAsync_RefetchesAfterTtlExpiry()
    {
        var svc = Create();
        var callCount = 0;

        // Store with a 1 ms TTL so it expires immediately
        await svc.GetOrFetchAsync("key3", _ => { callCount++; return Task.FromResult(1); }, TimeSpan.FromMilliseconds(1));
        await Task.Delay(10); // Let it expire

        await svc.GetOrFetchAsync("key3", _ => { callCount++; return Task.FromResult(2); }, TimeSpan.FromMinutes(1));

        Assert.Equal(2, callCount);
    }

    // ── Invalidate ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Invalidate_ForcesRefetchOnNextCall()
    {
        var svc = Create();
        var callCount = 0;

        await svc.GetOrFetchAsync("key4", _ => { callCount++; return Task.FromResult(10); }, TimeSpan.FromMinutes(5));
        svc.Invalidate("key4");
        await svc.GetOrFetchAsync("key4", _ => { callCount++; return Task.FromResult(20); }, TimeSpan.FromMinutes(5));

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task Invalidate_UnknownKey_DoesNotThrow()
    {
        var svc = Create();
        svc.Invalidate("nonexistent"); // should not throw
        // Verify cache still works normally
        var val = await svc.GetOrFetchAsync("other", _ => Task.FromResult(99), TimeSpan.FromMinutes(1));
        Assert.Equal(99, val);
    }

    // ── InvalidateAll ──────────────────────────────────────────────────────────

    [Fact]
    public async Task InvalidateAll_ClearsAllEntries()
    {
        var svc = Create();
        var callsA = 0;
        var callsB = 0;

        await svc.GetOrFetchAsync("a", _ => { callsA++; return Task.FromResult("A"); }, TimeSpan.FromMinutes(5));
        await svc.GetOrFetchAsync("b", _ => { callsB++; return Task.FromResult("B"); }, TimeSpan.FromMinutes(5));

        svc.InvalidateAll();

        await svc.GetOrFetchAsync("a", _ => { callsA++; return Task.FromResult("A2"); }, TimeSpan.FromMinutes(5));
        await svc.GetOrFetchAsync("b", _ => { callsB++; return Task.FromResult("B2"); }, TimeSpan.FromMinutes(5));

        Assert.Equal(2, callsA);
        Assert.Equal(2, callsB);
    }

    // ── IsValid ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsValid_ReturnsTrueWhenCacheEntryFresh()
    {
        var svc = Create();
        await svc.GetOrFetchAsync("fresh", _ => Task.FromResult(1), TimeSpan.FromMinutes(5));
        Assert.True(svc.IsValid("fresh"));
    }

    [Fact]
    public void IsValid_ReturnsFalseForMissingKey()
    {
        var svc = Create();
        Assert.False(svc.IsValid("missing"));
    }

    [Fact]
    public async Task IsValid_ReturnsFalseAfterInvalidate()
    {
        var svc = Create();
        await svc.GetOrFetchAsync("inv", _ => Task.FromResult(1), TimeSpan.FromMinutes(5));
        svc.Invalidate("inv");
        Assert.False(svc.IsValid("inv"));
    }

    // ── Concurrency ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrFetchAsync_ConcurrentCalls_FetcherCalledOnlyOnce()
    {
        var svc = Create();
        var callCount = 0;
        var tcs = new TaskCompletionSource<int>();

        // All 10 tasks hit the cache simultaneously before the first fetch completes
        var tasks = Enumerable.Range(0, 10).Select(_ =>
            svc.GetOrFetchAsync(
                "concurrent",
                async _ =>
                {
                    Interlocked.Increment(ref callCount);
                    await tcs.Task; // block until released
                    return 7;
                },
                TimeSpan.FromMinutes(5)));

        tcs.SetResult(0); // release all waiters
        var results = await Task.WhenAll(tasks);

        // Fetcher should have been called exactly once despite 10 concurrent requests
        Assert.Equal(1, callCount);
        Assert.All(results, r => Assert.Equal(7, r));
    }

    [Fact]
    public async Task GetOrFetchAsync_DifferentKeys_FetchedIndependently()
    {
        var svc = Create();
        var callsA = 0;
        var callsB = 0;

        await Task.WhenAll(
            svc.GetOrFetchAsync("kA", _ => { callsA++; return Task.FromResult(1); }, TimeSpan.FromMinutes(1)),
            svc.GetOrFetchAsync("kB", _ => { callsB++; return Task.FromResult(2); }, TimeSpan.FromMinutes(1)));

        Assert.Equal(1, callsA);
        Assert.Equal(1, callsB);
    }

    // ── Typed values ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrFetchAsync_NullableValue_StoredAndReturnedCorrectly()
    {
        var svc = Create();
        string? stored = null;
        var result = await svc.GetOrFetchAsync<string?>("nullable", _ => Task.FromResult(stored), TimeSpan.FromMinutes(1));
        Assert.Null(result);

        // Second call should return cached null without re-fetching
        var callCount = 0;
        var second = await svc.GetOrFetchAsync<string?>("nullable", _ => { callCount++; return Task.FromResult<string?>("new"); }, TimeSpan.FromMinutes(1));
        Assert.Null(second);
        Assert.Equal(0, callCount);
    }
}
