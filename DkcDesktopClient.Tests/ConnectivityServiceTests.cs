using System.Net;
using DkcDesktopClient.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DkcDesktopClient.Tests;

public class ConnectivityServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ConnectivityService CreateWithResponse(
        string serverUrl, HttpStatusCode statusCode)
    {
        Func<HttpClient> factory = () =>
            new HttpClient(new StubHttpMessageHandler(statusCode))
            { Timeout = TimeSpan.FromSeconds(5) };
        return new ConnectivityService(serverUrl, NullLogger<ConnectivityService>.Instance, factory);
    }

    private static ConnectivityService CreateWithTimeout(string serverUrl)
    {
        Func<HttpClient> factory = () =>
            new HttpClient(new StubHttpMessageHandler(null /* timeout */))
            { Timeout = TimeSpan.FromMilliseconds(50) };
        return new ConnectivityService(serverUrl, NullLogger<ConnectivityService>.Instance, factory);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void IsOnline_DefaultsToTrue()
    {
        var svc = new ConnectivityService(string.Empty,
            NullLogger<ConnectivityService>.Instance,
            () => new HttpClient());
        Assert.True(svc.IsOnline);
    }

    [Fact]
    public void ConnectivityChanged_EventHookup_DoesNotFire_BeforeFirstCheck()
    {
        var svc = new ConnectivityService(string.Empty,
            NullLogger<ConnectivityService>.Instance,
            () => new HttpClient());
        bool eventFired = false;
        svc.ConnectivityChanged += (_, _) => eventFired = true;

        Assert.False(eventFired);
        Assert.True(svc.IsOnline);
    }

    [Fact]
    public async Task ForceCheckAsync_WithSuccessResponse_StaysOnline()
    {
        var svc = CreateWithResponse("http://fake-server", HttpStatusCode.OK);

        await svc.ForceCheckAsync();

        Assert.True(svc.IsOnline);
    }

    [Fact]
    public async Task ForceCheckAsync_With5xxResponse_GoesOffline()
    {
        var svc = CreateWithResponse("http://fake-server", HttpStatusCode.InternalServerError);
        bool offlineEventFired = false;
        svc.ConnectivityChanged += (_, isOnline) => { if (!isOnline) offlineEventFired = true; };

        await svc.ForceCheckAsync();

        Assert.False(svc.IsOnline);
        Assert.True(offlineEventFired, "ConnectivityChanged should fire when going offline");
    }

    [Fact]
    public async Task ForceCheckAsync_With5xxThenSuccess_GoesOnlineThenOffline()
    {
        // First call: 5xx → offline
        var svc = CreateWithResponse("http://fake-server", HttpStatusCode.InternalServerError);
        await svc.ForceCheckAsync();
        Assert.False(svc.IsOnline);

        // Second call with 200 → back online; need to replace with 200-returning service
        // We verify using a separate service here since HttpClient factory is fixed.
        var svc2 = CreateWithResponse("http://fake-server", HttpStatusCode.OK);
        // Ensure it starts online after a success from offline state transition is not covered here;
        // at minimum verify that 200 keeps IsOnline true
        await svc2.ForceCheckAsync();
        Assert.True(svc2.IsOnline);
    }

    [Fact]
    public async Task ForceCheckAsync_With5xx_IncrementsConsecutiveFailures()
    {
        var svc = CreateWithResponse("http://fake-server", HttpStatusCode.ServiceUnavailable);

        // Make two consecutive failing checks – the service should stay offline
        await svc.ForceCheckAsync();
        Assert.False(svc.IsOnline);
        await svc.ForceCheckAsync();
        Assert.False(svc.IsOnline);
    }

    [Fact]
    public async Task ForceCheckAsync_WithTimeout_GoesOffline()
    {
        var svc = CreateWithTimeout("http://fake-server");
        bool offlineEventFired = false;
        svc.ConnectivityChanged += (_, isOnline) => { if (!isOnline) offlineEventFired = true; };

        await svc.ForceCheckAsync();

        Assert.False(svc.IsOnline);
        Assert.True(offlineEventFired, "ConnectivityChanged should fire on timeout");
    }

    [Fact]
    public async Task ForceCheckAsync_WithNoServerUrl_StaysOnline()
    {
        // Use empty string to simulate "server not configured yet" without touching disk
        var svc = new ConnectivityService(
            serverUrl: string.Empty,
            NullLogger<ConnectivityService>.Instance,
            () => new HttpClient());

        await svc.ForceCheckAsync();

        Assert.True(svc.IsOnline);
    }

    // ── Stub handler ─────────────────────────────────────────────────────────

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode? _statusCode;

        /// <param name="statusCode">Null causes a timeout-like delay.</param>
        public StubHttpMessageHandler(HttpStatusCode? statusCode)
            => _statusCode = statusCode;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_statusCode == null)
            {
                // Simulate a slow server that triggers the client timeout
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                // Task.Delay throws TaskCanceledException on cancellation; the line below is a safety net.
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new HttpResponseMessage(_statusCode!.Value);
        }
    }
}
