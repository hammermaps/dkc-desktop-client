using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DkcDesktopClient.Core.Services;

/// <summary>
/// Background service that monitors API reachability and exposes an <see cref="IsOnline"/> flag.
/// Uses exponential back-off when offline (max 60 s between retries).
/// Raises <see cref="ConnectivityChanged"/> whenever the online/offline state flips.
/// </summary>
public class ConnectivityService : BackgroundService
{
    private readonly TokenStore? _tokenStore;
    private readonly ILogger<ConnectivityService> _logger;
    private readonly Func<HttpClient> _createHttpClient;
    private readonly string? _serverUrlOverride;

    // Normal polling interval when the server is reachable.
    private static readonly TimeSpan NormalInterval = TimeSpan.FromSeconds(30);

    // Back-off steps when offline: 5 s, 10 s, 20 s, 40 s, 60 s (cap).
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    private bool _isOnline = true;
    private int _consecutiveFailures;

    /// <summary>Whether the server is currently considered reachable.</summary>
    public bool IsOnline
    {
        get => _isOnline;
        private set
        {
            if (_isOnline == value) return;
            _isOnline = value;
            ConnectivityChanged?.Invoke(this, value);
            _logger.LogInformation("Connectivity changed: {State}", value ? "Online" : "Offline");
        }
    }

    /// <summary>Raised (on the thread-pool) when the online/offline state changes.</summary>
    public event EventHandler<bool>? ConnectivityChanged;

    public ConnectivityService(TokenStore tokenStore, ILogger<ConnectivityService> logger)
        : this(tokenStore, logger,
               () => new HttpClient { Timeout = TimeSpan.FromSeconds(8) })
    { }

    /// <summary>Internal constructor allowing injection of a custom <see cref="HttpClient"/>
    /// factory for unit tests.</summary>
    internal ConnectivityService(
        TokenStore tokenStore,
        ILogger<ConnectivityService> logger,
        Func<HttpClient> createHttpClient)
        : this(tokenStore, logger, createHttpClient, serverUrlOverride: null)
    { }

    /// <summary>Internal constructor for unit tests: uses a fixed server URL so tests
    /// do not write to disk via <see cref="TokenStore"/>.</summary>
    internal ConnectivityService(
        string serverUrl,
        ILogger<ConnectivityService> logger,
        Func<HttpClient> createHttpClient)
        : this(tokenStore: null, logger, createHttpClient, serverUrlOverride: serverUrl)
    { }

    private ConnectivityService(
        TokenStore? tokenStore,
        ILogger<ConnectivityService> logger,
        Func<HttpClient> createHttpClient,
        string? serverUrlOverride)
    {
        _tokenStore        = tokenStore;
        _logger            = logger;
        _createHttpClient  = createHttpClient;
        _serverUrlOverride = serverUrlOverride;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ConnectivityService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckConnectivityAsync(stoppingToken);

            var delay = IsOnline
                ? NormalInterval
                : TimeSpan.FromSeconds(Math.Min(
                    MinBackoff.TotalSeconds * Math.Pow(2, _consecutiveFailures - 1),
                    MaxBackoff.TotalSeconds));

            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("ConnectivityService stopped");
    }

    /// <summary>Forces an immediate connectivity check (e.g. after network events).</summary>
    public Task ForceCheckAsync(CancellationToken ct = default)
        => CheckConnectivityAsync(ct);

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task CheckConnectivityAsync(CancellationToken ct)
    {
        var serverUrl = _serverUrlOverride ?? _tokenStore?.LoadServerUrl();
        if (string.IsNullOrEmpty(serverUrl))
        {
            // No server configured yet – treat as online (login screen will show any errors)
            IsOnline = true;
            _consecutiveFailures = 0;
            return;
        }

        try
        {
            using var http = _createHttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DkcDesktopClient/1.0 (ConnectivityCheck)");
            var url = $"{serverUrl.TrimEnd('/')}/api.php/health";
            var response = await http.GetAsync(url, ct).ConfigureAwait(false);

            // Any HTTP response with status < 500 means the server is reachable.
            if (response.IsSuccessStatusCode || (int)response.StatusCode < 500)
            {
                _consecutiveFailures = 0;
                IsOnline = true;
            }
            else
            {
                // 5xx: server is responding but unhealthy – count as a failure
                _consecutiveFailures++;
                _logger.LogDebug("Connectivity check: server returned {StatusCode} ({Failures} consecutive)",
                    (int)response.StatusCode, _consecutiveFailures);
                IsOnline = false;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Service is shutting down – exit silently.
        }
        catch (OperationCanceledException ex)
        {
            // HTTP request timed out (not a service shutdown).
            _consecutiveFailures++;
            _logger.LogDebug("Connectivity check timed out ({Failures} consecutive): {Message}",
                _consecutiveFailures, ex.Message);
            IsOnline = false;
        }
        catch (HttpRequestException ex)
        {
            _consecutiveFailures++;
            _logger.LogDebug("Connectivity check failed ({Failures} consecutive): {Message}",
                _consecutiveFailures, ex.Message);
            IsOnline = false;
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            _logger.LogWarning(ex, "Unexpected error during connectivity check");
            IsOnline = false;
        }
    }
}
