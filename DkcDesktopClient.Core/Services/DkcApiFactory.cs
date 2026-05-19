using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DkcDesktopClient.Core.Api;
using Microsoft.Extensions.Logging;
using Refit;

namespace DkcDesktopClient.Core.Services;

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

    public virtual IDkcApi Create(string? token = null, string? serverUrl = null, HttpMessageHandler? innerHandler = null)
    {
        var url = serverUrl ?? _tokenStore.LoadServerUrl() ?? "https://localhost";
        _logger.LogDebug("Creating API client for base URL {BaseUrl}", url);
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

        var requestBody = await ReadContentSafelyAsync(request.Content, ct);
        var sanitizedRequestBody = SanitizeSensitiveContent(requestBody);
        _logger.LogDebug(
            "API request {Method} {Uri} Body: {RequestBody}",
            request.Method,
            request.RequestUri,
            sanitizedRequestBody);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await base.SendAsync(request, ct);
        stopwatch.Stop();

        var responseContentType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
        var responseBody = await ReadContentSafelyAsync(response.Content, ct);

        // Inhalt neu schreiben, damit Refit ihn weiter lesen kann.
        response.Content = new StringContent(responseBody, Encoding.UTF8, responseContentType);

        var sanitizedResponseBody = SanitizeSensitiveContent(responseBody);
        _logger.LogInformation(
            "API response {Method} {Uri} => {StatusCode} ({ElapsedMs} ms)",
            request.Method,
            request.RequestUri,
            (int)response.StatusCode,
            stopwatch.ElapsedMilliseconds);
        _logger.LogDebug(
            "API response body {Method} {Uri}: {ResponseBody}",
            request.Method,
            request.RequestUri,
            sanitizedResponseBody);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && _authService != null)
        {
            _logger.LogWarning("Received 401 - triggering logout");
            await _authService.LogoutAsync(ct);
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
}
