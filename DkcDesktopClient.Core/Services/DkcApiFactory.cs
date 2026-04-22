using System.Net.Http.Headers;
using System.Text.Json;
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

    public IDkcApi Create(string? token = null, string? serverUrl = null, HttpMessageHandler? innerHandler = null)
    {
        var url = serverUrl ?? _tokenStore.LoadServerUrl() ?? "https://localhost";
        var handler = new AuthorizationHandler(token, _authService, _loggerFactory.CreateLogger<AuthorizationHandler>(), innerHandler);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(url) };
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

        var response = await base.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && _authService != null)
        {
            _logger.LogWarning("Received 401 - triggering logout");
            await _authService.LogoutAsync(ct);
        }

        return response;
    }
}
