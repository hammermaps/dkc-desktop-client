using System.Net.Http.Headers;
using DkcDesktopClient.Core.I18n;

namespace DkcDesktopClient.Core.Services;

/// <summary>Wartungszustand des Servers aus Sicht des angemeldeten Benutzers.</summary>
public enum MaintenanceState
{
    /// <summary>Kein Wartungsmodus.</summary>
    Off,
    /// <summary>Wartungsmodus aktiv, der Benutzer steht in allowed_users und arbeitet weiter.</summary>
    Allowed,
    /// <summary>Wartungsmodus aktiv, Anfragen des Benutzers werden mit 503 abgewiesen.</summary>
    Blocked,
}

/// <summary>
/// Wertet die Header <c>X-DKC-Maintenance</c>, <c>-Message</c> und <c>-End</c> aus, die
/// api.php (REST und Protobuf) im Wartungsmodus setzt (siehe MaintenanceMode::sendApiHeaders()
/// im ProxyServer). Fehlt der Header in einer Server-Antwort, ist kein Wartungsmodus aktiv.
/// </summary>
public sealed class MaintenanceMonitor
{
    public const string StateHeader = "X-DKC-Maintenance";
    public const string MessageHeader = "X-DKC-Maintenance-Message";
    public const string EndHeader = "X-DKC-Maintenance-End";

    private readonly object _lock = new();

    public MaintenanceState State { get; private set; } = MaintenanceState.Off;
    public string Message { get; private set; } = string.Empty;
    public string? EstimatedEnd { get; private set; }

    /// <summary>Wird bei jeder Änderung von Zustand, Nachricht oder Ende ausgelöst (beliebiger Thread).</summary>
    public event EventHandler<MaintenanceState>? Changed;

    /// <summary>Liest den Zustand aus Antwort-Headern; null, wenn die Antwort nicht vom Server stammt.</summary>
    public static (MaintenanceState State, string Message, string? End) Parse(HttpResponseHeaders headers)
    {
        var state = headers.TryGetValues(StateHeader, out var values) ? values.FirstOrDefault() : null;
        var parsed = state?.Trim().ToLowerInvariant() switch
        {
            "allowed" => MaintenanceState.Allowed,
            "blocked" => MaintenanceState.Blocked,
            _ => MaintenanceState.Off,
        };
        if (parsed == MaintenanceState.Off)
            return (MaintenanceState.Off, string.Empty, null);

        return (parsed, Decode(headers, MessageHeader) ?? string.Empty, Decode(headers, EndHeader));
    }

    private static string? Decode(HttpResponseHeaders headers, string name)
    {
        if (!headers.TryGetValues(name, out var values)) return null;
        var raw = values.FirstOrDefault();
        if (string.IsNullOrEmpty(raw)) return null;
        try { return Uri.UnescapeDataString(raw); }
        catch (UriFormatException) { return raw; }
    }

    /// <summary>Übernimmt den Zustand einer Server-Antwort.</summary>
    public void Observe(HttpResponseMessage response)
    {
        // Antworten ohne Server-Beteiligung (z. B. Proxy-Fehler 502/504) sagen nichts über den Wartungsmodus.
        if (response.StatusCode is System.Net.HttpStatusCode.BadGateway or System.Net.HttpStatusCode.GatewayTimeout)
            return;
        var (state, message, end) = Parse(response.Headers);
        Update(state, message, end);
    }

    public void Update(MaintenanceState state, string message, string? end)
    {
        lock (_lock)
        {
            if (State == state && Message == message && EstimatedEnd == end) return;
            State = state;
            Message = message;
            EstimatedEnd = end;
        }
        Changed?.Invoke(this, state);
    }

    /// <summary>Nach dem Abmelden: Zustand gilt nur für den bisherigen Benutzer.</summary>
    public void Reset() => Update(MaintenanceState.Off, string.Empty, null);
}

/// <summary>REST-Anfrage im Wartungsmodus abgewiesen; Nachricht ist für die Anzeige gedacht.</summary>
public sealed class MaintenanceBlockedException : HttpRequestException
{
    public MaintenanceBlockedException(string serverMessage)
        : base(BuildMessage(serverMessage), null, System.Net.HttpStatusCode.ServiceUnavailable) { }

    public static string BuildMessage(string serverMessage)
        => string.IsNullOrWhiteSpace(serverMessage)
            ? Translator.T("desktop.maintenance.request_blocked")
            : Translator.T("desktop.maintenance.request_blocked_message", ("message", serverMessage.Trim()));
}

/// <summary>
/// Meldet jede API-Antwort an den <see cref="MaintenanceMonitor"/>. Eine im Wartungsmodus
/// abgewiesene REST-Antwort wird zur <see cref="MaintenanceBlockedException"/>, damit Ansichten
/// statt „Response status code does not indicate success: 503“ einen lesbaren Text zeigen.
/// Protobuf-Antworten bleiben unverändert (der Envelope trägt Meldung und Details).
/// </summary>
public sealed class MaintenanceHandler : DelegatingHandler
{
    private readonly MaintenanceMonitor _monitor;

    public MaintenanceHandler(MaintenanceMonitor monitor, HttpMessageHandler inner) : base(inner)
        => _monitor = monitor;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        _monitor.Observe(response);

        if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable
            && MaintenanceMonitor.Parse(response.Headers).State == MaintenanceState.Blocked
            && response.Content.Headers.ContentType?.MediaType != "application/x-protobuf")
        {
            var message = MaintenanceMonitor.Parse(response.Headers).Message;
            response.Dispose();
            throw new MaintenanceBlockedException(message);
        }
        return response;
    }
}
