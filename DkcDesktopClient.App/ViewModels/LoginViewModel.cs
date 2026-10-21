using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.Core.Services;

namespace DkcDesktopClient.App.ViewModels;

/// <summary>Eine vom Server gemeldete Zwei-Faktor-Methode mit Anzeigename.</summary>
public sealed record TwoFactorMethodOption(string Value, string Label)
{
    public bool CanSendCode => Value is "email" or "sms";

    public override string ToString() => Label;

    public static TwoFactorMethodOption From(string value) => new(value, value switch
    {
        "totp"         => "Authenticator app",
        "email"        => "Code by email",
        "sms"          => "Code by SMS",
        "backup_codes" => "Backup code",
        _              => value,
    });
}

public partial class LoginViewModel : ViewModelBase
{
    private readonly AuthService _authService;
    private string? _pendingTicket;

    [ObservableProperty] private string _serverUrl = "https://";
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;
    [ObservableProperty] private bool _isLoading;

    // Zweiter Anmeldeschritt (Zwei-Faktor)
    [ObservableProperty] private bool _isTwoFactorStep;
    [ObservableProperty] private TwoFactorMethodOption? _selectedTwoFactorMethod;
    [ObservableProperty] private string _twoFactorCode = string.Empty;
    [ObservableProperty] private bool _trustDevice;

    public ObservableCollection<TwoFactorMethodOption> TwoFactorMethods { get; } = new();

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
        InfoMessage = null;
        IsLoading = true;
        try
        {
            var result = await _authService.LoginAsync(ServerUrl, Username, Password);
            if (result.RequiresTwoFactor && !string.IsNullOrEmpty(result.PendingTwoFactorTicket))
            {
                StartTwoFactorStep(result.PendingTwoFactorTicket, result.TwoFactorMethods ?? []);
            }
            else if (!result.Success)
            {
                ErrorMessage = result.Error ?? "Login failed.";
            }
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

    [RelayCommand(CanExecute = nameof(CanVerifyTwoFactor))]
    private async Task VerifyTwoFactorAsync()
    {
        if (_pendingTicket == null || SelectedTwoFactorMethod == null) return;
        ErrorMessage = null;
        InfoMessage = null;
        IsLoading = true;
        try
        {
            var result = await _authService.VerifyTwoFactorAsync(
                _pendingTicket, SelectedTwoFactorMethod.Value, TwoFactorCode, TrustDevice);
            if (result.Success)
            {
                ResetTwoFactorStep();
            }
            else
            {
                ErrorMessage = result.Error ?? "Verification failed.";
                TwoFactorCode = string.Empty;
            }
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

    [RelayCommand(CanExecute = nameof(CanSendTwoFactorCode))]
    private async Task SendTwoFactorCodeAsync()
    {
        if (_pendingTicket == null || SelectedTwoFactorMethod == null) return;
        ErrorMessage = null;
        InfoMessage = null;
        IsLoading = true;
        try
        {
            var result = await _authService.SendTwoFactorCodeAsync(_pendingTicket, SelectedTwoFactorMethod.Value);
            if (result.Success)
                InfoMessage = "Code sent. It is valid for 10 minutes.";
            else
                ErrorMessage = result.Error ?? "The code could not be sent.";
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

    [RelayCommand]
    private void CancelTwoFactor()
    {
        ResetTwoFactorStep();
        ErrorMessage = null;
        InfoMessage = null;
    }

    private void StartTwoFactorStep(string ticket, IEnumerable<string> methods)
    {
        _pendingTicket = ticket;
        TwoFactorMethods.Clear();
        foreach (var method in methods)
            TwoFactorMethods.Add(TwoFactorMethodOption.From(method));
        SelectedTwoFactorMethod = TwoFactorMethods.FirstOrDefault();
        TwoFactorCode = string.Empty;
        TrustDevice = false;
        Password = string.Empty;
        IsTwoFactorStep = true;
    }

    private void ResetTwoFactorStep()
    {
        _pendingTicket = null;
        TwoFactorMethods.Clear();
        SelectedTwoFactorMethod = null;
        TwoFactorCode = string.Empty;
        IsTwoFactorStep = false;
    }

    private bool CanLogin() =>
        !string.IsNullOrWhiteSpace(ServerUrl) &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !IsLoading;

    private bool CanVerifyTwoFactor() =>
        IsTwoFactorStep && SelectedTwoFactorMethod != null &&
        !string.IsNullOrWhiteSpace(TwoFactorCode) && !IsLoading;

    private bool CanSendTwoFactorCode() =>
        IsTwoFactorStep && SelectedTwoFactorMethod?.CanSendCode == true && !IsLoading;

    partial void OnServerUrlChanged(string value) => LoginCommand.NotifyCanExecuteChanged();
    partial void OnUsernameChanged(string value)  => LoginCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string value)  => LoginCommand.NotifyCanExecuteChanged();
    partial void OnTwoFactorCodeChanged(string value) => VerifyTwoFactorCommand.NotifyCanExecuteChanged();
    partial void OnIsTwoFactorStepChanged(bool value) => NotifyTwoFactorCommands();
    partial void OnSelectedTwoFactorMethodChanged(TwoFactorMethodOption? value) => NotifyTwoFactorCommands();
    partial void OnIsLoadingChanged(bool value)
    {
        LoginCommand.NotifyCanExecuteChanged();
        NotifyTwoFactorCommands();
    }

    private void NotifyTwoFactorCommands()
    {
        VerifyTwoFactorCommand.NotifyCanExecuteChanged();
        SendTwoFactorCodeCommand.NotifyCanExecuteChanged();
    }
}
