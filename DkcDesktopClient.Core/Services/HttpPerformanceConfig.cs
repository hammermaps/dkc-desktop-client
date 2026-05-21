namespace DkcDesktopClient.Core.Services;

/// <summary>
/// HTTP-Client-Konfiguration für optimale Performance und Zuverlässigkeit.
/// Diese Settings beeinflussen die Kommunikation mit dem Backend-Server.
/// </summary>
public class HttpPerformanceConfig
{
    /// <summary>
    /// Globales Timeout für alle HTTP-Requests (default: 30s).
    /// Bei langsamen Verbindungen erhöhen, aber nicht über 60s.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Timeout für die TCP-Verbindung (default: 10s).
    /// Hilft bei langsamen oder instabilen Netzwerkverbindungen.
    /// </summary>
    public int ConnectTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Maximale Anzahl von gleichzeitigen Verbindungen (default: 10).
    /// Höher = mehr parallele Requests, aber auch mehr Ressourcenverbrauch.
    /// </summary>
    public int MaxConnectionsPerServer { get; set; } = 10;

    /// <summary>
    /// Keep-Alive Timeout in Sekunden (default: 5s).
    /// Ermöglicht Connection-Wiederverwendung innerhalb dieses Zeitraums.
    /// </summary>
    public int KeepAliveTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Maximale Keep-Alive Requests pro Verbindung (default: 100).
    /// Nach dieser Anzahl wird die Verbindung geschlossen und neu geöffnet.
    /// </summary>
    public int MaxKeepAliveRequests { get; set; } = 100;

    /// <summary>
    /// Aktiviert Gzip/Deflate Kompression für Request/Response (default: true).
    /// Reduziert Bandbreite, benötigt aber etwas CPU.
    /// </summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>
    /// Versucht HTTP/2 zu verwenden wenn möglich (default: true).
    /// Bessere Performance bei multiplen parallelen Requests.
    /// </summary>
    public bool EnableHttp2 { get; set; } = true;

    /// <summary>
    /// Anzahl der Wiederholung bei fehlgeschlagenen Requests (default: 2).
    /// 0 = keine Wiederholungen, erhöhen für weniger zuverlässige Verbindungen.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// Anfangsverzögerung für Retry in Millisekunden (default: 500ms).
    /// Wird exponentiell erhöht mit jedem Retry (backoff).
    /// </summary>
    public int RetryDelayMilliseconds { get; set; } = 500;

    /// <summary>
    /// Aktiviert aggressives Caching von DNS-Einträgen (default: true).
    /// Reduziert DNS-Lookups, kann aber bei Serveränderungen zu Problemen führen.
    /// </summary>
    public bool EnableDnsCaching { get; set; } = true;

    /// <summary>
    /// TTL für DNS-Cache in Sekunden (default: 300s = 5 Min).
    /// </summary>
    public int DnsCacheTtlSeconds { get; set; } = 300;
}

