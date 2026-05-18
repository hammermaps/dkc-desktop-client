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
    private readonly TokenStore _tokenStore;
    private readonly ILogger<ConnectivityService> _logger;

    // Normal polling interval (in seconds) when the server is reachable.
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
    {
        _tokenStore = tokenStore;
        _logger     = logger;
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

            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("ConnectivityService stopped");
    }

    /// <summary>Forces an immediate connectivity check (e.g. after network events).</summary>
    public Task ForceCheckAsync(CancellationToken ct = default)
        => CheckConnectivityAsync(ct);

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task CheckConnectivityAsync(CancellationToken ct)
    {
        var serverUrl = _tokenStore.LoadServerUrl();
        if (string.IsNullOrEmpty(serverUrl))
        {
            // No server configured yet – treat as online (login screen will show any errors)
            IsOnline = true;
            _consecutiveFailures = 0;
            return;
        }

        try
        {
            // Use a lightweight HEAD/GET to the health endpoint.
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DkcDesktopClient/1.0 (ConnectivityCheck)");
            var url = $"{serverUrl.TrimEnd('/')}/api.php/health";
            var response = await http.GetAsync(url, ct).ConfigureAwait(false);

            // Any HTTP response (even 4xx) means the server is reachable.
            IsOnline = response.IsSuccessStatusCode || (int)response.StatusCode < 500;
            _consecutiveFailures = 0;
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
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
