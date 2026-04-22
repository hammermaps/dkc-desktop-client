using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Services;

namespace DkcDesktopClient.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly DkcApiFactory _apiFactory;
    private readonly AuthService _authService;
    private readonly TokenStore _tokenStore;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string _serverUrl = string.Empty;
    [ObservableProperty] private ObservableCollection<TokenListItem> _tokens = new();
    [ObservableProperty] private TokenListItem? _selectedToken;

    public string? CurrentUsername => _authService.CurrentUser?.Username;

    public SettingsViewModel(DkcApiFactory apiFactory, AuthService authService, TokenStore tokenStore)
    {
        _apiFactory = apiFactory;
        _authService = authService;
        _tokenStore = tokenStore;
        ServerUrl = tokenStore.LoadServerUrl() ?? string.Empty;
    }

    [RelayCommand]
    public void SaveServerUrl()
    {
        if (!string.IsNullOrWhiteSpace(ServerUrl))
        {
            _tokenStore.SaveServerUrl(ServerUrl);
            StatusMessage = "Server URL saved.";
        }
    }

    [RelayCommand]
    public async Task LoadTokensAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.GetTokensListAsync();
            Tokens.Clear();
            if (result.Success && result.Tokens != null)
                foreach (var t in result.Tokens)
                    Tokens.Add(t);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading tokens: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteToken))]
    public async Task DeleteTokenAsync()
    {
        if (SelectedToken == null) return;
        IsLoading = true;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.DeleteTokenAsync(SelectedToken.Id);
            if (result.Success)
            {
                Tokens.Remove(SelectedToken);
                StatusMessage = "Token deleted.";
            }
            else
            {
                ErrorMessage = result.Error ?? "Failed to delete token.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error deleting token: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanDeleteToken() => SelectedToken != null && !IsLoading;

    partial void OnSelectedTokenChanged(TokenListItem? value) => DeleteTokenCommand.NotifyCanExecuteChanged();
    partial void OnIsLoadingChanged(bool value) => DeleteTokenCommand.NotifyCanExecuteChanged();
}
