using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace DkcDesktopClient.Tests;

// ── Test helpers ──────────────────────────────────────────────────────────────

/// <summary>
/// DkcApiFactory subclass that returns a caller-supplied mock IDkcApi instead
/// of building a real Refit client. Requires DkcApiFactory.Create() to be virtual.
/// </summary>
internal sealed class FakeDkcApiFactory : DkcApiFactory
{
    private readonly IDkcApi _api;

    public FakeDkcApiFactory(IDkcApi api, TokenStore tokenStore)
        : base(tokenStore, NullLogger<DkcApiFactory>.Instance, NullLoggerFactory.Instance)
    {
        _api = api;
    }

    public override IDkcApi Create(
        string? token = null,
        string? serverUrl = null,
        HttpMessageHandler? innerHandler = null) => _api;
}

/// <summary>
/// BackgroundRefreshService with a fast configurable tick interval for tests
/// so we don't need to wait 10 seconds per cycle.
/// </summary>
internal sealed class FastBackgroundRefreshService : BackgroundRefreshService
{
    private readonly TimeSpan _testInterval;

    public FastBackgroundRefreshService(
        AuthService authService,
        DkcApiFactory apiFactory,
        DataCacheService cache,
        IOptions<RefreshConfig> config,
        TimeSpan testInterval)
        : base(authService, apiFactory, cache, config, NullLogger<BackgroundRefreshService>.Instance)
    {
        _testInterval = testInterval;
    }

    protected override TimeSpan TickInterval => _testInterval;
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class BackgroundRefreshServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly TokenStore _tokenStore;

    public BackgroundRefreshServiceTests()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        _provider = services.BuildServiceProvider();
        var dp = _provider.GetRequiredService<IDataProtectionProvider>();
        _tokenStore = new TokenStore(dp, NullLogger<TokenStore>.Instance);
        _tokenStore.DeleteToken();
    }

    public void Dispose()
    {
        _tokenStore.DeleteToken();
        _provider.Dispose();
    }

    // ── NotifyUserActivity ─────────────────────────────────────────────────────

    [Fact]
    public void NotifyUserActivity_DoesNotThrow()
    {
        var (svc, _) = Build();
        // Calling with any key must never throw.
        svc.NotifyUserActivity(CacheKeys.DashboardData);
        svc.NotifyUserActivity("unknown-key");
        svc.NotifyUserActivity(string.Empty);
    }

    [Fact]
    public void NotifyUserActivity_CalledRepeatedly_DoesNotThrow()
    {
        var (svc, _) = Build();
        for (var i = 0; i < 20; i++)
            svc.NotifyUserActivity(CacheKeys.MmList);
    }

    // ── Pause when not authenticated ───────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenNotAuthenticated_NeverCallsApi()
    {
        var mock = new Mock<IDkcApi>();
        var (svc, auth) = Build(mock.Object);

        // auth.IsAuthenticated == false (no token saved)
        Assert.False(auth.IsAuthenticated);

        await RunBrieflyAsync(svc, runMs: 180);

        mock.Verify(
            a => a.GetDashboardDataAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        mock.Verify(
            a => a.GetKlimaRealtimeStatusAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNotAuthenticated_DoesNotFireDataRefreshed()
    {
        var mock = new Mock<IDkcApi>();
        var (svc, _) = Build(mock.Object);

        var fired = false;
        svc.DataRefreshed += (_, _) => fired = true;

        await RunBrieflyAsync(svc, runMs: 180);

        Assert.False(fired);
    }

    // ── Authenticated refresh ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenAuthenticated_FiresDataRefreshed()
    {
        var mock = MakeAuthenticatedMock();
        var (svc, auth) = Build(mock.Object);

        // Authenticate via TryAutoLoginAsync (the mock returns Authenticated = true)
        _tokenStore.SaveToken("fake-token");
        _tokenStore.SaveServerUrl("http://localhost");
        await auth.TryAutoLoginAsync();
        Assert.True(auth.IsAuthenticated);

        var firedKeys = new List<string>();
        svc.DataRefreshed += (_, key) => { lock (firedKeys) firedKeys.Add(key); };

        await RunBrieflyAsync(svc, runMs: 300);

        Assert.NotEmpty(firedKeys);
    }

    [Fact]
    public async Task ExecuteAsync_AfterNotifyUserActivity_SkipsFreshKey()
    {
        var mock = MakeAuthenticatedMock();
        var (svc, auth) = Build(mock.Object);

        _tokenStore.SaveToken("fake-token");
        _tokenStore.SaveServerUrl("http://localhost");
        await auth.TryAutoLoginAsync();

        // Mark MmList as freshly loaded – it should not be refreshed in a short window
        svc.NotifyUserActivity(CacheKeys.MmList);

        var firedKeys = new List<string>();
        svc.DataRefreshed += (_, key) => { lock (firedKeys) firedKeys.Add(key); };

        await RunBrieflyAsync(svc, runMs: 250);

        Assert.DoesNotContain(CacheKeys.MmList, firedKeys);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Creates a test service wired to an optional fake API.</summary>
    private (FastBackgroundRefreshService svc, AuthService auth) Build(
        IDkcApi? fakeApi = null,
        TimeSpan? tick = null)
    {
        fakeApi ??= new Mock<IDkcApi>().Object;
        var factory = new FakeDkcApiFactory(fakeApi, _tokenStore);
        var auth    = new AuthService(factory, _tokenStore, NullLogger<AuthService>.Instance);
        var cache   = new DataCacheService(NullLogger<DataCacheService>.Instance);

        var config = Options.Create(new RefreshConfig
        {
            KlimaStatus       = TimeSpan.FromMilliseconds(50),
            DashboardStats    = TimeSpan.FromMilliseconds(50),
            NotificationCount = TimeSpan.FromMilliseconds(50),
            MmList            = TimeSpan.FromMinutes(5),    // long so we can test deferred skip
            NeaInspections    = TimeSpan.FromMilliseconds(50),
        });

        var svc = new FastBackgroundRefreshService(
            auth, factory, cache, config,
            testInterval: tick ?? TimeSpan.FromMilliseconds(30));

        return (svc, auth);
    }

    /// <summary>
    /// Creates a mock IDkcApi that returns success+authenticated for all
    /// methods called by BackgroundRefreshService and TryAutoLoginAsync.
    /// </summary>
    private static Mock<IDkcApi> MakeAuthenticatedMock()
    {
        var mock = new Mock<IDkcApi>();

        mock.Setup(a => a.GetAuthStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthStatusResponse(true, true,
                new UserInfo(1, "u", "F", "L", "u@x.com", false, null), null));
        mock.Setup(a => a.GetUserInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserInfoResponse(true, null, new Dictionary<string, bool>(), null));
        mock.Setup(a => a.GetKlimaRealtimeStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KlimaRealtimeStatusResponse(true, null, null, null));
        mock.Setup(a => a.GetDashboardDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardDataResponse(true, null, null));
        mock.Setup(a => a.GetNeaDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NeaDashboardResponse(true, null, null, null, null, null));
        mock.Setup(a => a.GetNotificationCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationCountResponse(0, true));
        mock.Setup(a => a.GetMmListAsync(
                It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MmListResponse(true, 0, 50, 0, null, null));
        mock.Setup(a => a.GetKeysInventoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KeysInventoryResponse(true, null, null));
        mock.Setup(a => a.GetNeaInspectionsAsync(
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NeaInspectionsResponse(true, null, 0, null, null, null, null));

        return mock;
    }

    private static async Task RunBrieflyAsync(FastBackgroundRefreshService svc, int runMs)
    {
        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(runMs);
        await svc.StopAsync(CancellationToken.None);
    }
}
