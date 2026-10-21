using DkcDesktopClient.Core.Api;
using Microsoft.Extensions.Logging;
using Refit;

namespace DkcDesktopClient.Core.Services;

public class AuthService
{
    private readonly DkcApiFactory _apiFactory;
    private readonly TokenStore _tokenStore;
    private readonly ILogger<AuthService> _logger;
    private string? _pendingUsername;

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

    private const string TokenName = "DKC Desktop Client";

    public async Task<LoginResponse> LoginAsync(string serverUrl, string username, string password, CancellationToken ct = default)
    {
        _tokenStore.SaveServerUrl(serverUrl);
        var api = _apiFactory.Create(null, serverUrl);
        var request = new LoginRequest(username, password, TokenName, DeviceToken: _tokenStore.LoadDeviceToken());
        var response = await CallAsync(() => api.LoginAsync(request, ct));
        if (response.Success && response.Token != null)
            await CompleteLoginAsync(response, serverUrl, username, ct);
        else if (response.RequiresTwoFactor)
            _pendingUsername = username;
        return response;
    }

    /// <summary>
    /// Zweiter Anmeldeschritt: Ticket aus <see cref="LoginAsync"/> plus Code.
    /// Mit <paramref name="trustDevice"/> merkt sich der Server das Gerät 30 Tage.
    /// </summary>
    public async Task<LoginResponse> VerifyTwoFactorAsync(string ticket, string method, string code, bool trustDevice, CancellationToken ct = default)
    {
        var serverUrl = _tokenStore.LoadServerUrl();
        var api = _apiFactory.Create(null, serverUrl);
        var response = await CallAsync(() => api.VerifyTwoFactorAsync(
            new VerifyTwoFactorRequest(ticket, method, code.Trim(), trustDevice, TokenName), ct));
        if (response.Success && response.Token != null)
        {
            if (!string.IsNullOrEmpty(response.DeviceTrustToken))
                _tokenStore.SaveDeviceToken(response.DeviceTrustToken);
            await CompleteLoginAsync(response, serverUrl, _pendingUsername, ct);
        }
        return response;
    }

    /// <summary>Lässt einen Anmeldecode per E-Mail oder SMS senden.</summary>
    public async Task<SendTwoFactorCodeResponse> SendTwoFactorCodeAsync(string ticket, string method, CancellationToken ct = default)
    {
        var api = _apiFactory.Create(null, _tokenStore.LoadServerUrl());
        try
        {
            return await api.SendTwoFactorCodeAsync(new SendTwoFactorCodeRequest(ticket, method), ct);
        }
        catch (ApiException ex)
        {
            return await ReadErrorAsync<SendTwoFactorCodeResponse>(ex)
                   ?? new SendTwoFactorCodeResponse(false, false, ex.Message);
        }
    }

    private async Task CompleteLoginAsync(LoginResponse response, string? serverUrl, string? username, CancellationToken ct)
    {
        CurrentToken = response.Token;
        CurrentUser = response.User;
        _tokenStore.SaveToken(response.Token!);
        if (!string.IsNullOrEmpty(username))
            _tokenStore.SaveUsername(username);
        _pendingUsername = null;
        var authedApi = _apiFactory.Create(response.Token, serverUrl);
        await LoadPermissionsAsync(authedApi, ct);
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Refit wirft bei 4xx/5xx eine <see cref="ApiException"/>; der Server liefert
    /// dabei eine JSON-Fehlermeldung (falsches Passwort, falscher Code, Sperre),
    /// die hier als normale Antwort zurückgegeben wird.
    /// </summary>
    private static async Task<LoginResponse> CallAsync(Func<Task<LoginResponse>> call)
    {
        try
        {
            return await call();
        }
        catch (ApiException ex)
        {
            return await ReadErrorAsync<LoginResponse>(ex)
                   ?? new LoginResponse(false, null, null, null, null, ex.Message);
        }
    }

    private static async Task<T?> ReadErrorAsync<T>(ApiException ex) where T : class
    {
        try
        {
            return string.IsNullOrWhiteSpace(ex.Content) ? null : await ex.GetContentAsAsync<T>();
        }
        catch (Exception)
        {
            return null;
        }
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
