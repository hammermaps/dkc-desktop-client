using System.Net.Http.Headers;
using System.Text.Json;
using DkcDesktopClient.Core.Api;
using Microsoft.Extensions.Logging;
using Refit;

namespace DkcDesktopClient.Core.Services;

internal static class DebugLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DkcDesktopClient", "debug.log");

    private static readonly object _lock = new();

    public static void Write(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            lock (_lock) File.AppendAllText(LogPath, line);
        }
        catch { /* Logging darf die App nicht zum Absturz bringen */ }
    }
}

public class DkcApiFactory
{
    private readonly TokenStore _tokenStore;
    private readonly ILogger<DkcApiFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private AuthService? _authService;

    public DkcApiFactory(TokenStore tokenStore, ILogger<DkcApiFactory> logger, ILoggerFactory loggerFactory)
    {
        _tokenStore = tokenStore;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public void SetAuthService(AuthService authService) => _authService = authService;

    public IDkcApi Create(string? token = null, string? serverUrl = null, HttpMessageHandler? innerHandler = null)
    {
        var url = serverUrl ?? _tokenStore.LoadServerUrl() ?? "https://localhost";
        var handler = new AuthorizationHandler(token, _authService, _loggerFactory.CreateLogger<AuthorizationHandler>(), innerHandler);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(url) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DkcDesktopClient/1.0 (Avalonia; .NET8)");
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
}

internal class AuthorizationHandler : DelegatingHandler
{
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

        // --- Request Debug-Ausgabe ---
        var requestBody = string.Empty;
        if (request.Content != null)
            requestBody = await request.Content.ReadAsStringAsync(ct);

        var requestMsg = $"[API REQUEST] {request.Method} {request.RequestUri}" +
            (string.IsNullOrEmpty(requestBody) ? string.Empty : $"\n  Body: {requestBody}");
        _logger.LogDebug("{Message}", requestMsg);
        DebugLog.Write(requestMsg);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await base.SendAsync(request, ct);
        stopwatch.Stop();

        // --- Response Debug-Ausgabe ---
        var responseBody = string.Empty;
        if (response.Content != null)
        {
            responseBody = await response.Content.ReadAsStringAsync(ct);
            // Inhalt neu schreiben, damit Refit ihn noch lesen kann
            response.Content = new StringContent(responseBody,
                System.Text.Encoding.UTF8,
                response.Content.Headers.ContentType?.MediaType ?? "application/json");
        }

        var responseMsg = $"[API RESPONSE] {request.Method} {request.RequestUri} => {(int)response.StatusCode} ({stopwatch.ElapsedMilliseconds} ms)" +
            (string.IsNullOrEmpty(responseBody) ? string.Empty : $"\n  Body: {responseBody}");
        _logger.LogDebug("{Message}", responseMsg);
        DebugLog.Write(responseMsg);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && _authService != null)
        {
            _logger.LogWarning("Received 401 - triggering logout");
            DebugLog.Write("[WARNING] Received 401 - triggering logout");
            await _authService.LogoutAsync(ct);
        }

        return response;
    }
}
