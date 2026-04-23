using DkcDesktopClient.Core.Api;
using Microsoft.Extensions.Logging;

namespace DkcDesktopClient.Core.Services;

public class AuthService
{
    private readonly DkcApiFactory _apiFactory;
    private readonly TokenStore _tokenStore;
    private readonly ILogger<AuthService> _logger;

    public string? CurrentToken { get; private set; }
    public UserInfo? CurrentUser { get; private set; }
    public Dictionary<string, bool> Permissions { get; private set; } = new();
    public bool IsAuthenticated => CurrentToken != null;

    public event EventHandler? AuthStateChanged;

    public AuthService(DkcApiFactory apiFactory, TokenStore tokenStore, ILogger<AuthService> logger)
    {
        _apiFactory = apiFactory;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    public async Task<bool> TryAutoLoginAsync(CancellationToken ct = default)
    {
        var token = _tokenStore.LoadToken();
        if (token == null) return false;
        CurrentToken = token;
        var api = _apiFactory.Create(token);
        try
        {
            var status = await api.GetAuthStatusAsync(ct);
            if (status.Authenticated)
            {
                CurrentUser = status.User;
                await LoadPermissionsAsync(api, ct);
                AuthStateChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-login failed");
        }
        CurrentToken = null;
        _tokenStore.DeleteToken();
        return false;
    }

    public async Task<LoginResponse> LoginAsync(string serverUrl, string username, string password, CancellationToken ct = default)
    {
        const string tokenName = "DKC Desktop Client";
        _tokenStore.SaveServerUrl(serverUrl);
        var api = _apiFactory.Create(null, serverUrl);
        var response = await api.LoginAsync(new LoginRequest(username, password, tokenName), ct);
        if (response.Success && response.Token != null)
        {
            CurrentToken = response.Token;
            CurrentUser = response.User;
            _tokenStore.SaveToken(response.Token);
            _tokenStore.SaveUsername(username);
            var authedApi = _apiFactory.Create(response.Token, serverUrl);
            await LoadPermissionsAsync(authedApi, ct);
            AuthStateChanged?.Invoke(this, EventArgs.Empty);
        }
        return response;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (CurrentToken != null)
        {
            try
            {
                await _apiFactory.Create(CurrentToken).LogoutAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Logout API call failed");
            }
        }
        CurrentToken = null;
        CurrentUser = null;
        Permissions.Clear();
        _tokenStore.DeleteToken();
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool HasPermission(string key) => Permissions.TryGetValue(key, out var v) && v;

    private async Task LoadPermissionsAsync(IDkcApi api, CancellationToken ct)
    {
        try
        {
            var info = await api.GetUserInfoAsync(ct);
            if (info.Success && info.Permissions != null)
                Permissions = info.Permissions;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load permissions");
        }
    }
}
