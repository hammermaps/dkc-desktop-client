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

    /// <summary>Zuletzt bekannte Domänen-Revision je "scope|domain" (aus Snapshot und Ereignissen).</summary>
    private readonly Dictionary<string, long> _knownRevisions = new();
    private readonly object _revisionLock = new();

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
    public static string? ParseDomain(string json) => ParseEvent(json)?.Domain;

    /// <summary>Scope, Domäne und Revision eines SSE-Ereignisses; null bei unbrauchbaren Daten.</summary>
    public static (string Scope, string Domain, long Revision)? ParseEvent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("domain", out var d) || d.GetString() is not { } domain)
                return null;
            var scope = root.TryGetProperty("scope", out var sc) && sc.ValueKind == JsonValueKind.String ? sc.GetString() ?? string.Empty : string.Empty;
            var revision = root.TryGetProperty("revision", out var rv) && rv.TryGetInt64(out var r) ? r : 0;
            return (scope, domain, revision);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Vergleicht einen Snapshot mit den bekannten Revisionen und übernimmt ihn. Liefert die Domänen,
    /// deren Revision sich seit dem letzten Stand geändert hat – also Änderungen, die während einer
    /// Unterbrechung des Streams verpasst wurden. Beim ersten Snapshot (nichts bekannt) wird nur der
    /// Ausgangsstand gemerkt, die Ansichten laden beim Öffnen ohnehin frisch.
    /// </summary>
    public static IReadOnlyCollection<string> ApplySnapshot(IDictionary<string, long> known, Protocol.ChangeFeedSnapshotResponse snapshot)
    {
        var changed = new HashSet<string>(StringComparer.Ordinal);
        if (!snapshot.Enabled) return changed;
        var isBaseline = known.Count == 0;
        foreach (var scope in snapshot.Scopes)
        {
            foreach (var (domain, revision) in scope.Domains)
            {
                var key = scope.Scope + "|" + domain;
                if (!isBaseline && (!known.TryGetValue(key, out var previous) || previous != revision))
                    changed.Add(domain);
                known[key] = revision;
            }
        }
        return changed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_authService.IsAuthenticated)
            {
                lock (_revisionLock) _knownRevisions.Clear();
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
                    await ReconcileSnapshotAsync(proto, ct).ConfigureAwait(false);
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
        var ev = ParseEvent(json);
        if (ev == null) return;
        if (ev.Value.Revision > 0 && ev.Value.Scope.Length > 0)
            lock (_revisionLock) _knownRevisions[ev.Value.Scope + "|" + ev.Value.Domain] = ev.Value.Revision;
        NotifyDomain(ev.Value.Domain);
    }

    private void NotifyDomain(string domain)
    {
        foreach (var key in KeysForDomain(domain))
            _refresh.NotifyChanged(key);
    }

    /// <summary>Nach (Wieder-)Verbindung: verpasste Änderungen über CHANGE_FEED_SNAPSHOT nachholen.</summary>
    private async Task ReconcileSnapshotAsync(DkcProtobufApi proto, CancellationToken ct)
    {
        try
        {
            var snapshot = await proto.GetChangeFeedSnapshotAsync(ct).ConfigureAwait(false);
            IReadOnlyCollection<string> changed;
            lock (_revisionLock) changed = ApplySnapshot(_knownRevisions, snapshot);
            foreach (var domain in changed)
                NotifyDomain(domain);
            if (changed.Count > 0)
                _logger.LogDebug("Change Feed: verpasste Änderungen nachgeladen ({Domains})", string.Join(", ", changed));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Snapshot ist nur eine Ergänzung; der Stream läuft auch ohne ihn.
            _logger.LogDebug(ex, "Change-Feed-Snapshot fehlgeschlagen");
        }
    }

    private void SetConnected(bool value)
    {
        if (_connected == value) return;
        _connected = value;
        _refresh.SetPushConfirmed(value);
        ConnectionChanged?.Invoke(this, value);
    }
}
