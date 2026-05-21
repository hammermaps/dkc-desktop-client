using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DkcDesktopClient.Core.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Refit;

namespace DkcDesktopClient.Core.Services;

public class DkcApiFactory
{
    private readonly TokenStore _tokenStore;
    private readonly ILogger<DkcApiFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOptions<HttpPerformanceConfig> _httpConfig;
    private AuthService? _authService;

    public DkcApiFactory(TokenStore tokenStore, ILogger<DkcApiFactory> logger, ILoggerFactory loggerFactory, IOptions<HttpPerformanceConfig> httpConfig)
    {
        _tokenStore = tokenStore;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _httpConfig = httpConfig;
    }

    public void SetAuthService(AuthService authService) => _authService = authService;

    public virtual IDkcApi Create(string? token = null, string? serverUrl = null, HttpMessageHandler? innerHandler = null)
    {
        var url = ResolveBaseUrl(serverUrl);
        _logger.LogDebug("Creating API client for base URL {BaseUrl}", url);
        
        // Konfiguriere optimalen HTTP-Handler für Performance
        HttpMessageHandler handler = innerHandler ?? CreateOptimizedHttpHandler();
        
        // Wrap mit Authorization-Handler
        var authHandler = new AuthorizationHandler(token, _authService, _loggerFactory.CreateLogger<AuthorizationHandler>(), handler);
        
        var httpClient = new HttpClient(authHandler) { BaseAddress = new Uri(url) };
        
        // Optimierte Client-Konfiguration basierend auf HttpPerformanceConfig
        var config = _httpConfig.Value;
        httpClient.Timeout = TimeSpan.FromSeconds(config.RequestTimeoutSeconds);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DkcDesktopClient/1.0 (Avalonia; .NET8)");
        
        // Connection Reuse aktivieren (wichtig für Performance)
        httpClient.DefaultRequestHeaders.Connection.Add("keep-alive");
        httpClient.DefaultRequestHeaders.Add("Keep-Alive", $"timeout={config.KeepAliveTimeoutSeconds}, max={config.MaxKeepAliveRequests}");
        
        var settings = new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            })
        };
        return RestService.For<IDkcApi>(httpClient, settings);
    }

    public virtual DkcProtobufApiClient CreateProtobuf(
        string? token = null,
        string? serverUrl = null,
        HttpMessageHandler? innerHandler = null)
    {
        var url = ResolveBaseUrl(serverUrl);
        _logger.LogDebug("Creating protobuf API client for base URL {BaseUrl}", url);

        var httpClient = new HttpClient(innerHandler ?? new HttpClientHandler())
        {
            BaseAddress = new Uri(url)
        };

        return new DkcProtobufApiClient(
            httpClient,
            () => token ?? _authService?.CurrentToken,
            disposeHttpClient: true);
    }

    private string ResolveBaseUrl(string? serverUrl)
    {
        var url = serverUrl ?? _tokenStore.LoadServerUrl();
        if (!string.IsNullOrWhiteSpace(url))
            return url;

        _logger.LogWarning("No server URL configured; falling back to https://localhost");
        return "https://localhost";
    }
    
    /// <summary>
    /// Erstellt einen HTTP-Handler mit optimierten Einstellungen für Desktop-Anwendungen
    /// </summary>
    private HttpMessageHandler CreateOptimizedHttpHandler()
    {
        var config = _httpConfig.Value;
        var handler = new HttpClientHandler
        {
            // Enable connection pooling und Multiplexing (HTTP/2)
            AutomaticDecompression = config.EnableCompression 
                ? System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
                : System.Net.DecompressionMethods.None,
            
            // Verwende HTTP/2 wenn möglich (bessere Performance)
            SslProtocols = System.Security.Authentication.SslProtocols.Tls13 | 
                          System.Security.Authentication.SslProtocols.Tls12,
        };
        
        #if !DEBUG
        // In Release: strikte SSL-Validierung
        handler.ServerCertificateCustomValidationCallback = null;
        #else
        // In Debug: erlauben Sie self-signed Zertifikate für lokale Tests
        handler.ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) =>
        {
            // Für Entwicklung: akzeptiere localhost mit selbstsigniertem Zertifikat
            if (msg?.RequestUri?.Host == "localhost" || msg?.RequestUri?.Host == "127.0.0.1")
                return true;
            
            return errors == System.Net.Security.SslPolicyErrors.None;
        };
        #endif
        
        // Konfiguriere erweiterte HTTP-Einstellungen via Reflection/Type Check
        try
        {
            // Versuche auf SocketsHttpHandler zuzugreifen (Linux/Mac)
            var handlersType = handler.GetType();
            var pooledConnLifetimeProp = handlersType.GetProperty("PooledConnectionLifetime");
            var pooledConnIdleTimeoutProp = handlersType.GetProperty("PooledConnectionIdleTimeout");
            var maxConnPerServerProp = handlersType.GetProperty("MaxConnectionsPerServer");
            
            if (pooledConnLifetimeProp?.CanWrite == true)
                pooledConnLifetimeProp.SetValue(handler, TimeSpan.FromMinutes(2));
            
            if (pooledConnIdleTimeoutProp?.CanWrite == true)
                pooledConnIdleTimeoutProp.SetValue(handler, TimeSpan.FromSeconds(config.KeepAliveTimeoutSeconds));
            
            if (maxConnPerServerProp?.CanWrite == true)
            {
                maxConnPerServerProp.SetValue(handler, config.MaxConnectionsPerServer);
                _logger.LogDebug("HTTP Handler configured with MaxConnectionsPerServer={MaxConnections}", config.MaxConnectionsPerServer);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not configure HTTP handler advanced settings");
        }
        
        return handler;
    }
}

internal class AuthorizationHandler : DelegatingHandler
{
    private static readonly HashSet<string> SessionOnlyActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "notifications",
        "get_notification_count",
        "client_cache_version",
        "ckeditor_draft",
        "meter_list",
        "meter_submit",
        "meter_batch_sync",
        "meter_readings",
        "meter_qr_list",
        "meter_deactivate",
        "meter_activate",
        "meter_buildings",
        "meter_whg",
        "meter_users",
        "meter_topology",
        "dropdown_data"
    };

    private static readonly Regex SensitiveJsonFieldsRegex = new(
        "\\\"(password|token|apikey)\\\"\\s*:\\s*\\\".*?\\\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string? _token;
    private readonly AuthService? _authService;
    private readonly ILogger<AuthorizationHandler> _logger;

    public AuthorizationHandler(string? token, AuthService? authService, ILogger<AuthorizationHandler> logger, HttpMessageHandler? innerHandler = null)
        : base(innerHandler ?? new HttpClientHandler())
    {
        _token = token;
        _authService = authService;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var tok = _token ?? _authService?.CurrentToken;
        if (tok != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tok);

        // Verbessertes Logging mit optimiertem Body-Handling
        var requestBody = await ReadContentSafelyAsync(request.Content, ct);
        var sanitizedRequestBody = SanitizeSensitiveContent(requestBody);
        
        // Logge Request nur im Debug-Level (performance)
        _logger.LogDebug(
            "API request {Method} {Uri}",
            request.Method,
            request.RequestUri);
        
        if (!string.IsNullOrEmpty(sanitizedRequestBody))
        {
            _logger.LogDebug("API request body: {RequestBody}", sanitizedRequestBody);
        }

        // Messe Antwortzeit
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await base.SendAsync(request, ct);
        stopwatch.Stop();

        var responseContentType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
        
        // Lese Response-Body nur wenn nötig (für große Responses teuer)
        var responseBody = string.Empty;
        if (_logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
        {
            responseBody = await ReadContentSafelyAsync(response.Content, ct);
        }

        // Inhalt neu schreiben, damit Refit ihn weiter lesen kann.
        if (!string.IsNullOrEmpty(responseBody))
        {
            response.Content = new StringContent(responseBody, Encoding.UTF8, responseContentType);
        }

        // Logge Response mit Performance-Metriken
        var logLevel = (int)response.StatusCode >= 400 ? Microsoft.Extensions.Logging.LogLevel.Warning : Microsoft.Extensions.Logging.LogLevel.Information;
        if (_logger.IsEnabled(logLevel))
        {
            _logger.Log(
                logLevel,
                "API response {Method} {Uri} => {StatusCode} ({ElapsedMs} ms)",
                request.Method,
                request.RequestUri,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);
        }

        if (!string.IsNullOrEmpty(responseBody))
        {
            var sanitizedResponseBody = SanitizeSensitiveContent(responseBody);
            _logger.LogDebug(
                "API response body: {ResponseBody}",
                sanitizedResponseBody);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && _authService != null)
        {
            var action = TryGetActionName(request.RequestUri);
            var isSessionOnlyAction = !string.IsNullOrWhiteSpace(action) && SessionOnlyActions.Contains(action);
            var isLogoutAction = string.Equals(action, "auth_logout", StringComparison.OrdinalIgnoreCase);

            if (isSessionOnlyAction)
            {
                _logger.LogWarning(
                    "Received 401 on session-only action '{Action}'. Skipping auto-logout for token-based desktop auth.",
                    action);
            }
            else if (isLogoutAction)
            {
                _logger.LogDebug("Received 401 for auth_logout. Skipping recursive auto-logout.");
            }
            else
            {
                _logger.LogWarning("Received 401 for action '{Action}' - triggering logout", action ?? "<unknown>");
                await _authService.LogoutAsync(ct);
            }
        }

        return response;
    }

    private static async Task<string> ReadContentSafelyAsync(HttpContent? content, CancellationToken ct)
    {
        if (content == null)
            return string.Empty;

        try
        {
            return await content.ReadAsStringAsync(ct);
        }
        catch
        {
            return "<content-unavailable>";
        }
    }

    private static string SanitizeSensitiveContent(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return payload;

        return SensitiveJsonFieldsRegex.Replace(payload, "\"$1\":\"***\"");
    }

    private static string? TryGetActionName(Uri? requestUri)
    {
        if (requestUri == null)
            return null;

        var query = requestUri.Query;
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var trimmed = query.TrimStart('?');
        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex <= 0)
                continue;

            var key = pair[..separatorIndex];
            if (!key.Equals("action", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = pair[(separatorIndex + 1)..];
            return Uri.UnescapeDataString(value);
        }

        return null;
    }
}
