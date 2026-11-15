using System.Net;
using System.Net.Http.Headers;
using DkcDesktopClient.Core.Services;
using Xunit;

namespace DkcDesktopClient.Tests;

/// <summary>Auswertung der Wartungs-Header (MaintenanceMode::sendApiHeaders() im ProxyServer).</summary>
public class MaintenanceMonitorTests
{
    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond());
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string? state, string mediaType = "application/json")
    {
        var r = new HttpResponseMessage(status) { Content = new StringContent("{}") };
        r.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        if (state != null)
        {
            r.Headers.TryAddWithoutValidation(MaintenanceMonitor.StateHeader, state);
            r.Headers.TryAddWithoutValidation(MaintenanceMonitor.MessageHeader, Uri.EscapeDataString("Update läuft"));
            r.Headers.TryAddWithoutValidation(MaintenanceMonitor.EndHeader, Uri.EscapeDataString("15.11.2026 18:00"));
        }
        return r;
    }

    [Fact]
    public void Parse_DecodesStateMessageAndEnd()
    {
        var (state, message, end) = MaintenanceMonitor.Parse(Response(HttpStatusCode.OK, "allowed").Headers);
        Assert.Equal(MaintenanceState.Allowed, state);
        Assert.Equal("Update läuft", message);
        Assert.Equal("15.11.2026 18:00", end);
        Assert.Equal(MaintenanceState.Off, MaintenanceMonitor.Parse(Response(HttpStatusCode.OK, null).Headers).State);
    }

    [Fact]
    public void Observe_FollowsServerAndIgnoresProxyErrors()
    {
        var monitor = new MaintenanceMonitor();
        var changes = 0;
        monitor.Changed += (_, _) => changes++;

        monitor.Observe(Response(HttpStatusCode.ServiceUnavailable, "blocked"));
        Assert.Equal(MaintenanceState.Blocked, monitor.State);
        monitor.Observe(Response(HttpStatusCode.BadGateway, null));   // Proxy, nicht der Server
        Assert.Equal(MaintenanceState.Blocked, monitor.State);
        monitor.Observe(Response(HttpStatusCode.OK, null));           // Server antwortet ohne Header
        Assert.Equal(MaintenanceState.Off, monitor.State);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task Handler_TurnsBlockedRestResponseIntoReadableException()
    {
        var monitor = new MaintenanceMonitor();
        using var client = new HttpClient(new MaintenanceHandler(monitor, new StubHandler(() => Response(HttpStatusCode.ServiceUnavailable, "blocked"))));
        var ex = await Assert.ThrowsAsync<MaintenanceBlockedException>(() => client.GetAsync("http://x/api.php?action=nea_inspections"));
        Assert.Equal("Wartungsmodus – Update läuft", ex.Message);
        Assert.Equal(MaintenanceState.Blocked, monitor.State);
    }

    [Fact]
    public async Task Handler_LeavesProtobufResponsesToTheEnvelope()
    {
        var monitor = new MaintenanceMonitor();
        using var client = new HttpClient(new MaintenanceHandler(monitor, new StubHandler(() => Response(HttpStatusCode.ServiceUnavailable, "blocked", "application/x-protobuf"))));
        var r = await client.PostAsync("http://x/api.php", new ByteArrayContent(Array.Empty<byte>()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal(MaintenanceState.Blocked, monitor.State);
    }
}
