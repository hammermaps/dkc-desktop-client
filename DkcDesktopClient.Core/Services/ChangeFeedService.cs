using System.Text;
using System.Text.Json;
using DkcDesktopClient.Core.Protobuf;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DkcDesktopClient.Core.Services;

/// <summary>
/// Hält den SSE-Change-Feed des Servers offen (Ticket über Protobuf <c>ChangeFeedTicket</c>) und
/// meldet pro Ereignis nur die betroffenen Cache-Schlüssel an den <see cref="BackgroundRefreshService"/>.
/// Das Ereignis ist ausschließlich ein Nachlade-Signal; fachliche Daten laden die Ansichten wie
/// bisher über ihre Protobuf-Actions. Ohne bestätigte Verbindung bleibt das Polling aktiv.
/// </summary>
public class ChangeFeedService : BackgroundService
{
    private static readonly TimeSpan[] Backoff =
    {
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60),
    };

    private readonly AuthService _authService;
    private readonly DkcApiFactory _apiFactory;
    private readonly TokenStore _tokenStore;
    private readonly BackgroundRefreshService _refresh;
    private readonly ILogger<ChangeFeedService> _logger;
    private volatile bool _connected;

    /// <summary>True, sobald der Stream bestätigt ist (Server-Kommentar <c>: connected</c>).</summary>
    public bool IsConnected => _connected;

    public event EventHandler<bool>? ConnectionChanged;

    public ChangeFeedService(
        AuthService authService,
        DkcApiFactory apiFactory,
        TokenStore tokenStore,
        BackgroundRefreshService refresh,
        ILogger<ChangeFeedService> logger)
    {
        _authService = authService;
        _apiFactory  = apiFactory;
        _tokenStore  = tokenStore;
        _refresh     = refresh;
        _logger      = logger;
    }

    /// <summary>Cache-Schlüssel, die zu einer Change-Feed-Domäne gehören. Unbekannte Domänen: leer.</summary>
    public static IReadOnlyList<string> KeysForDomain(string domain) => domain switch
    {
        "mm" => new[] { CacheKeys.MmList, CacheKeys.MmListOpen, CacheKeys.MmListInProgress, CacheKeys.DashboardData },
        "nea" => new[] { CacheKeys.NeaDashboard, CacheKeys.NeaSystems, CacheKeys.NeaInspections },
        "klima" => new[] { CacheKeys.KlimaStatus, CacheKeys.KlimaDevices },
        "notifications" => new[] { CacheKeys.Notifications, CacheKeys.NotificationCount },
        _ => Array.Empty<string>(),
    };

    /// <summary>
    /// SSE-Endpunkt: <c>DKC_CHANGE_FEED_URL</c>, sonst lokal direkt auf Port 8098, sonst
    /// <c>&lt;Server&gt;/change-feed/events</c> (nginx-Weiterleitung).
    /// </summary>
    public static string ResolveStreamUrl(string serverUrl, string ticket)
    {
        var configured = Environment.GetEnvironmentVariable("DKC_CHANGE_FEED_URL");
        string baseUrl;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            baseUrl = configured!;
        }
        else
        {
            var uri = new Uri(serverUrl);
            baseUrl = uri.IsLoopback
                ? $"{uri.Scheme}://{uri.Host}:8098/events"
                : $"{uri.GetLeftPart(UriPartial.Authority)}/change-feed/events";
        }
        return baseUrl + (baseUrl.Contains('?') ? "&" : "?") + "ticket=" + Uri.EscapeDataString(ticket);
    }

    /// <summary>Verarbeitet ein SSE-Datenfeld; liefert die Domäne oder null.</summary>
    public static string? ParseDomain(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("domain", out var d) ? d.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_authService.IsAuthenticated)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                var wasConfirmed = await RunStreamAsync(stoppingToken).ConfigureAwait(false);
                failures = wasConfirmed ? 0 : failures + 1;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                failures++;
                _logger.LogDebug(ex, "Change-Feed-Verbindung fehlgeschlagen");
            }

            SetConnected(false);
            var delay = Backoff[Math.Min(failures, Backoff.Length - 1)];
            try { await Task.Delay(delay, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        SetConnected(false);
    }

    /// <returns>true, wenn der Stream bestätigt wurde (Backoff dann zurücksetzen).</returns>
    private async Task<bool> RunStreamAsync(CancellationToken ct)
    {
        var proto = new DkcProtobufApi(_apiFactory.CreateProtobuf(_authService.CurrentToken));
        var ticket = await proto.GetChangeFeedTicketAsync(ct).ConfigureAwait(false);
        if (!ticket.Enabled || string.IsNullOrEmpty(ticket.Ticket))
        {
            _logger.LogDebug("Change Feed serverseitig deaktiviert");
            return false;
        }

        var serverUrl = _tokenStore.LoadServerUrl();
        if (string.IsNullOrWhiteSpace(serverUrl)) return false;

        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Get, ResolveStreamUrl(serverUrl!, ticket.Ticket));
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = new StringBuilder();
        var confirmed = false;

        // Ticket läuft ab; der Server hält den Stream so lange offen, danach neu verbinden.
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lifetime.CancelAfter(TimeSpan.FromSeconds(Math.Max(30, ticket.ExpiresIn > 0 ? ticket.ExpiresIn : 300)));
        try
        {
            while (await reader.ReadLineAsync(lifetime.Token).ConfigureAwait(false) is { } line)
            {
                if (line.StartsWith(": connected", StringComparison.Ordinal))
                {
                    confirmed = true;
                    SetConnected(true);
                }
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    data.Append(line.AsSpan(5).Trim());
                }
                else if (line.Length == 0 && data.Length > 0)
                {
                    HandleEvent(data.ToString());
                    data.Clear();
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Ticket-Laufzeit erreicht: regulärer Neuaufbau mit frischem Ticket.
        }
        return confirmed;
    }

    private void HandleEvent(string json)
    {
        var domain = ParseDomain(json);
        if (domain == null) return;
        foreach (var key in KeysForDomain(domain))
            _refresh.NotifyChanged(key);
    }

    private void SetConnected(bool value)
    {
        if (_connected == value) return;
        _connected = value;
        _refresh.SetPushConfirmed(value);
        ConnectionChanged?.Invoke(this, value);
    }
}
