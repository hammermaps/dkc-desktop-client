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
    private readonly UpdateService _updateService;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string _serverUrl = string.Empty;
    [ObservableProperty] private ObservableCollection<TokenListItem> _tokens = new();
    [ObservableProperty] private TokenListItem? _selectedToken;

    [ObservableProperty] private bool _isCheckingForUpdates;
    [ObservableProperty] private bool _isDownloadingUpdate;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private UpdateInfo? _availableUpdate;

    public string CurrentVersion => UpdateService.CurrentVersion.ToString(3);
    public string? CurrentUsername => _authService.CurrentUser?.Username;

    public SettingsViewModel(DkcApiFactory apiFactory, AuthService authService, TokenStore tokenStore, UpdateService updateService)
    {
        _apiFactory = apiFactory;
        _authService = authService;
        _tokenStore = tokenStore;
        _updateService = updateService;
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

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    public async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            var update = await _updateService.CheckForUpdateAsync();
            if (update != null)
            {
                AvailableUpdate = update;
                StatusMessage = null;
            }
            else
            {
                AvailableUpdate = null;
                StatusMessage = "You are running the latest version.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Update check failed: {ex.Message}";
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private bool CanCheckForUpdates() => !IsCheckingForUpdates && !IsDownloadingUpdate;

    [RelayCommand(CanExecute = nameof(CanDownloadUpdate))]
    public async Task DownloadUpdateAsync()
    {
        if (AvailableUpdate == null) return;

        IsDownloadingUpdate = true;
        DownloadProgress = 0;
        ErrorMessage = null;
        try
        {
            var progress = new Progress<double>(p => DownloadProgress = p * 100);
            var success = await _updateService.DownloadAndInstallAsync(AvailableUpdate, progress);
            if (!success)
                ErrorMessage = "Download failed. Please try again or update manually.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Update failed: {ex.Message}";
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    private bool CanDownloadUpdate() => AvailableUpdate != null && !IsDownloadingUpdate && !IsCheckingForUpdates;

    partial void OnIsCheckingForUpdatesChanged(bool value)
    {
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        DownloadUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsDownloadingUpdateChanged(bool value)
    {
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        DownloadUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnAvailableUpdateChanged(UpdateInfo? value) => DownloadUpdateCommand.NotifyCanExecuteChanged();
}
