using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.Core.Services;

namespace DkcDesktopClient.App.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly AuthService _authService;

    [ObservableProperty] private string _serverUrl = "https://";
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isLoading;

    public LoginViewModel(AuthService authService, TokenStore tokenStore)
    {
        _authService = authService;
        ServerUrl = tokenStore.LoadServerUrl() ?? "https://";
        Username  = tokenStore.LoadUsername()  ?? string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        ErrorMessage = null;
        IsLoading = true;
        try
        {
            var result = await _authService.LoginAsync(ServerUrl, Username, Password);
            if (!result.Success)
                ErrorMessage = result.Error ?? "Login failed.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanLogin() =>
        !string.IsNullOrWhiteSpace(ServerUrl) &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !IsLoading;

    partial void OnServerUrlChanged(string value) => LoginCommand.NotifyCanExecuteChanged();
    partial void OnUsernameChanged(string value)  => LoginCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string value)  => LoginCommand.NotifyCanExecuteChanged();
    partial void OnIsLoadingChanged(bool value)   => LoginCommand.NotifyCanExecuteChanged();
}
