using System.Collections.ObjectModel;
using System.Net;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Protobuf;
using DkcDesktopClient.Core.I18n;
using DkcDesktopClient.Core.Services;
using Refit;
using ProtoProjectSaveRequest = DkcDesktopClient.Core.Protocol.ProjectSaveRequest;
using ProtoUserSaveRequest = DkcDesktopClient.Core.Protocol.UserSaveRequest;
using ProtoUserDeleteRequest = DkcDesktopClient.Core.Protocol.UserDeleteRequest;
using static DkcDesktopClient.Core.I18n.Translator;

namespace DkcDesktopClient.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly DkcApiFactory _apiFactory;
    private readonly AuthService _authService;
    private readonly TokenStore _tokenStore;
    private readonly UpdateService _updateService;
    private readonly TranslationService? _translations;

    // Sprache
    /// <summary>„Automatisch“ plus alle Sprachen des Servers, jeweils in ihrer eigenen Schreibweise.</summary>
    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new(TranslationService.Auto, null),
        new("de", "Deutsch"), new("en", "English"), new("fr", "Français"), new("es", "Español"),
        new("tr", "Türkçe"), new("ru", "Русский"), new("pl", "Polski"),
    ];

    [ObservableProperty] private LanguageOption _selectedLanguage;

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (_translations == null || value.Code == _translations.Preference) return;
        _translations.SetPreference(value.Code);
        _ = _translations.SyncAsync(_apiFactory);
    }

    /// <summary>
    /// project_create/project_update/user_create/user_update/user_delete sind in api.php nie als
    /// REST-Route registriert worden — nur die Protobuf-Actions funktionieren.
    /// </summary>
    private DkcProtobufApi ProtoApi => new(_apiFactory.CreateProtobuf(_authService.CurrentToken));

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string _serverUrl = string.Empty;
    [ObservableProperty] private ObservableCollection<TokenListItem> _tokens = new();
    [ObservableProperty] private TokenListItem? _selectedToken;

    // Projects
    [ObservableProperty] private ObservableCollection<Project> _projects = new();
    [ObservableProperty] private Project? _selectedProject;
    [ObservableProperty] private bool _isProjectFormVisible;
    [ObservableProperty] private bool _isSavingProject;
    [ObservableProperty] private string? _projectFormError;
    [ObservableProperty] private bool _isEditingProject;
    private int? _editingProjectId;
    [ObservableProperty] private string _formProjectName = string.Empty;
    [ObservableProperty] private string _formProjectDescription = string.Empty;

    // Admin users
    [ObservableProperty] private ObservableCollection<AdminUser> _users = new();
    [ObservableProperty] private AdminUser? _selectedUser;
    [ObservableProperty] private bool _isUserFormVisible;
    [ObservableProperty] private bool _isSavingUser;
    [ObservableProperty] private string? _userFormError;
    [ObservableProperty] private bool _isEditingUser;
    private int? _editingUserId;
    [ObservableProperty] private string _formUsername = string.Empty;
    [ObservableProperty] private string _formPassword = string.Empty;
    [ObservableProperty] private string _formVname = string.Empty;
    [ObservableProperty] private string _formNname = string.Empty;
    [ObservableProperty] private string _formEmail = string.Empty;
    [ObservableProperty] private bool _formIsAdmin;

    // Update
    [ObservableProperty] private bool _isCheckingForUpdates;
    [ObservableProperty] private bool _isDownloadingUpdate;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private UpdateInfo? _availableUpdate;

    // Logging
    private static readonly string[] SupportedLogLevels =
    [
        "Verbose",
        "Debug",
        "Information",
        "Warning",
        "Error",
        "Fatal"
    ];
    private readonly string _dataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DkcDesktopClient");
    [ObservableProperty] private ObservableCollection<string> _logLevelOptions = new(SupportedLogLevels);
    [ObservableProperty] private string _selectedLogLevel = "Debug";
    [ObservableProperty] private string? _loggingMessage;

    // Admin API status
    [ObservableProperty] private bool _isRunningApiStatusCheck;
    [ObservableProperty] private ObservableCollection<ApiStatusItem> _apiStatusItems = new();

    // Passwort ändern (USER_CHANGE_PASSWORD)
    [ObservableProperty] private string _currentPassword = string.Empty;
    [ObservableProperty] private string _newPassword = string.Empty;
    [ObservableProperty] private string _newPasswordRepeat = string.Empty;
    [ObservableProperty] private string? _passwordError;
    [ObservableProperty] private string? _passwordMessage;
    [ObservableProperty] private bool _isChangingPassword;

    public bool IsAdmin => _authService.CurrentUser?.IsAdmin ?? false;
    /// <summary>Wie im Web: Recht edit_profile_password oder Administrator.</summary>
    public bool CanChangePassword => IsAdmin || _authService.HasPermission("edit_profile_password");
    public string CurrentVersion => UpdateService.CurrentVersion.ToString(3);
    public string? CurrentUsername => _authService.CurrentUser?.Username;

    public SettingsViewModel(DkcApiFactory apiFactory, AuthService authService, TokenStore tokenStore, UpdateService updateService,
        TranslationService? translations = null)
    {
        _translations = translations;
        _selectedLanguage = LanguageOptions.FirstOrDefault(o => o.Code == (translations?.Preference ?? TranslationService.Auto))
                            ?? LanguageOptions[0];
        _apiFactory = apiFactory;
        _authService = authService;
        _tokenStore = tokenStore;
        _updateService = updateService;
        ServerUrl = tokenStore.LoadServerUrl() ?? string.Empty;
        _authService.AuthStateChanged += OnAuthStateChanged;
        LoadLoggingSettings();
    }

    private void OnAuthStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(CanChangePassword));
        OnPropertyChanged(nameof(CurrentUsername));
        RunApiStatusCheckCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    public void SaveServerUrl()
    {
        if (!string.IsNullOrWhiteSpace(ServerUrl))
        {
            _tokenStore.SaveServerUrl(ServerUrl);
            StatusMessage = T("desktop.settings.server_url_saved");
        }
    }

    [RelayCommand]
    public void SaveLoggingSettings()
    {
        if (!LogLevelOptions.Contains(SelectedLogLevel))
        {
            LoggingMessage = T("desktop.settings.invalid_log_level");
            return;
        }

        try
        {
            Directory.CreateDirectory(_dataDir);
            var filePath = Path.Combine(_dataDir, "logging.json");
            var json = JsonSerializer.Serialize(new LoggingConfig(SelectedLogLevel), new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(filePath, json);
            LoggingMessage = T("desktop.settings.log_level_saved");
            StatusMessage = T("desktop.settings.logging_saved");
        }
        catch (Exception ex)
        {
            LoggingMessage = T("desktop.settings.save_error", ("message", ex.Message));
        }
    }

    [RelayCommand]
    public void ReloadLoggingSettings()
    {
        LoadLoggingSettings();
        LoggingMessage = T("desktop.settings.logging_reloaded");
    }

    [RelayCommand(CanExecute = nameof(CanRunApiStatusCheck))]
    public async Task RunApiStatusCheckAsync()
    {
        if (!IsAdmin)
            return;

        IsRunningApiStatusCheck = true;
        ErrorMessage = null;
        ApiStatusItems.Clear();

        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            await CheckEndpointAsync("user_info", async () => await api.GetUserInfoAsync(timeoutCts.Token));
            await CheckEndpointAsync("projects_list", async () => await api.GetProjectsListAsync(timeoutCts.Token));
            await CheckEndpointAsync("dashboard_data", async () => await api.GetDashboardDataAsync(timeoutCts.Token));
            await CheckEndpointAsync("keys_inventory", async () => await api.GetKeysInventoryAsync(timeoutCts.Token));
            await CheckEndpointAsync("klima_status", async () => await api.GetKlimaRealtimeStatusAsync(timeoutCts.Token));
            await CheckEndpointAsync("nea_dashboard", async () => await api.GetNeaDashboardAsync(timeoutCts.Token));

            StatusMessage = T("desktop.settings.status_check_done");
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = T("desktop.settings.status_check_timeout");
        }
        finally
        {
            IsRunningApiStatusCheck = false;
        }
    }

    /// <summary>Prüfung vor dem Senden; Komplexitätsregeln prüft der Server (Passwortrichtlinie).</summary>
    public static string? ValidatePasswordChange(string current, string next, string repeat)
    {
        if (string.IsNullOrEmpty(current)) return T("desktop.settings.enter_current_password");
        if (string.IsNullOrEmpty(next)) return T("desktop.settings.enter_new_password");
        if (next != repeat) return T("desktop.settings.password_mismatch");
        if (next == current) return T("desktop.settings.password_same");
        return null;
    }

    [RelayCommand(CanExecute = nameof(CanSubmitPasswordChange))]
    public async Task ChangePasswordAsync()
    {
        PasswordMessage = null;
        PasswordError = ValidatePasswordChange(CurrentPassword, NewPassword, NewPasswordRepeat);
        if (PasswordError != null) return;

        IsChangingPassword = true;
        try
        {
            await ProtoApi.ChangePasswordAsync(new DkcDesktopClient.Core.Protocol.ChangePasswordRequest
            {
                CurrentPassword = CurrentPassword,
                NewPassword = NewPassword,
            });
            CurrentPassword = NewPassword = NewPasswordRepeat = string.Empty;
            PasswordMessage = T("desktop.settings.password_changed");
        }
        catch (DkcProtobufApiException ex)
        {
            // Server-Meldung durchreichen (falsches aktuelles Passwort, Passwortrichtlinie, …).
            PasswordError = ex.Message;
        }
        catch (Exception ex)
        {
            PasswordError = T("desktop.settings.password_change_failed", ("message", ex.Message));
        }
        finally
        {
            IsChangingPassword = false;
        }
    }

    private bool CanSubmitPasswordChange() => !IsChangingPassword;

    partial void OnIsChangingPasswordChanged(bool value) => ChangePasswordCommand.NotifyCanExecuteChanged();

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
            ErrorMessage = T("desktop.settings.tokens_failed", ("message", ex.Message));
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
            var result = await api.DeleteTokenAsync(new TokenDeleteRequest(SelectedToken.Id));
            if (result.Success)
            {
                Tokens.Remove(SelectedToken);
                StatusMessage = T("desktop.settings.token_deleted");
            }
            else
            {
                ErrorMessage = result.Error ?? T("desktop.settings.token_delete_failed");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = T("desktop.settings.token_delete_error", ("message", ex.Message));
        }
        finally
        {
            IsLoading = false;
        }
    }

    // — Projects —
    [RelayCommand]
    public async Task LoadProjectsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.GetProjectsListAsync();
            Projects.Clear();
            if (result.Success && result.Projects != null)
                foreach (var p in result.Projects)
                    Projects.Add(p);
        }
        catch (Exception ex)
        {
            ErrorMessage = T("desktop.settings.projects_failed", ("message", ex.Message));
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void ShowCreateProjectForm()
    {
        IsEditingProject = false;
        _editingProjectId = null;
        FormProjectName = string.Empty;
        FormProjectDescription = string.Empty;
        ProjectFormError = null;
        IsProjectFormVisible = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProject))]
    public void ShowEditProjectForm()
    {
        if (SelectedProject == null) return;
        IsEditingProject = true;
        _editingProjectId = SelectedProject.Id;
        FormProjectName = SelectedProject.Name;
        FormProjectDescription = SelectedProject.Description ?? string.Empty;
        ProjectFormError = null;
        IsProjectFormVisible = true;
    }

    [RelayCommand]
    public void CancelProjectForm()
    {
        IsProjectFormVisible = false;
        ProjectFormError = null;
    }

    [RelayCommand(CanExecute = nameof(CanSaveProject))]
    public async Task SaveProjectAsync()
    {
        if (string.IsNullOrWhiteSpace(FormProjectName))
        {
            ProjectFormError = T("desktop.common.name_is_required");
            return;
        }
        IsSavingProject = true;
        ProjectFormError = null;
        try
        {
            var req = new ProtoProjectSaveRequest
            {
                Id = IsEditingProject && _editingProjectId.HasValue ? _editingProjectId.Value : 0,
                Name = FormProjectName,
                Description = FormProjectDescription,
            };

            if (IsEditingProject && _editingProjectId.HasValue)
                await ProtoApi.UpdateProjectAsync(req);
            else
                await ProtoApi.CreateProjectAsync(req);

            IsProjectFormVisible = false;
            await LoadProjectsAsync();
            StatusMessage = T("desktop.settings.project_saved");
        }
        catch (Exception ex)
        {
            ProjectFormError = T("desktop.common.error", ("message", ex.Message));
        }
        finally
        {
            IsSavingProject = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProject))]
    public async Task SetActiveProjectAsync()
    {
        if (SelectedProject == null) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.SetActiveProjectAsync(new ProjectSetActiveRequest(SelectedProject.Id));
            if (result.Success)
                StatusMessage = T("desktop.settings.active_project_set", ("name", SelectedProject.Name));
            else
                ErrorMessage = result.Error ?? T("desktop.settings.active_project_failed");
        }
        catch (Exception ex)
        {
            ErrorMessage = T("desktop.common.error", ("message", ex.Message));
        }
        finally
        {
            IsLoading = false;
        }
    }

    // — Admin users —
    [RelayCommand]
    public async Task LoadUsersAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.GetUsersListAsync();
            Users.Clear();
            if (result.Success && result.Users != null)
                foreach (var u in result.Users)
                    Users.Add(u);
        }
        catch (Exception ex)
        {
            ErrorMessage = T("desktop.settings.users_failed", ("message", ex.Message));
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void ShowCreateUserForm()
    {
        IsEditingUser = false;
        _editingUserId = null;
        FormUsername = string.Empty;
        FormPassword = string.Empty;
        FormVname = string.Empty;
        FormNname = string.Empty;
        FormEmail = string.Empty;
        FormIsAdmin = false;
        UserFormError = null;
        IsUserFormVisible = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedUser))]
    public void ShowEditUserForm()
    {
        if (SelectedUser == null) return;
        IsEditingUser = true;
        _editingUserId = SelectedUser.Id;
        FormUsername = SelectedUser.Username;
        FormPassword = string.Empty;
        FormVname = SelectedUser.Vname ?? string.Empty;
        FormNname = SelectedUser.Nname ?? string.Empty;
        FormEmail = SelectedUser.Email ?? string.Empty;
        FormIsAdmin = SelectedUser.IsAdmin;
        UserFormError = null;
        IsUserFormVisible = true;
    }

    [RelayCommand]
    public void CancelUserForm()
    {
        IsUserFormVisible = false;
        UserFormError = null;
    }

    [RelayCommand(CanExecute = nameof(CanSaveUser))]
    public async Task SaveUserAsync()
    {
        if (string.IsNullOrWhiteSpace(FormUsername))
        {
            UserFormError = T("desktop.settings.username_is_required");
            return;
        }
        if (!IsEditingUser && string.IsNullOrWhiteSpace(FormPassword))
        {
            UserFormError = T("desktop.settings.password_required_new_user");
            return;
        }
        IsSavingUser = true;
        UserFormError = null;
        try
        {
            var req = new ProtoUserSaveRequest
            {
                Id = IsEditingUser && _editingUserId.HasValue ? _editingUserId.Value : 0,
                Username = FormUsername,
                Password = FormPassword,
                Vname = FormVname,
                Nname = FormNname,
                Email = FormEmail,
                IsAdmin = FormIsAdmin,
            };

            if (IsEditingUser && _editingUserId.HasValue)
                await ProtoApi.UpdateUserAsync(req);
            else
                await ProtoApi.CreateUserAsync(req);

            IsUserFormVisible = false;
            await LoadUsersAsync();
            StatusMessage = T("desktop.settings.user_saved");
        }
        catch (Exception ex)
        {
            UserFormError = T("desktop.common.error", ("message", ex.Message));
        }
        finally
        {
            IsSavingUser = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedUser))]
    public async Task DeleteUserAsync()
    {
        if (SelectedUser == null) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await ProtoApi.DeleteUserAsync(new ProtoUserDeleteRequest { Id = SelectedUser.Id });
            Users.Remove(SelectedUser);
            StatusMessage = T("desktop.settings.user_deleted");
        }
        catch (Exception ex)
        {
            ErrorMessage = T("desktop.common.error", ("message", ex.Message));
        }
        finally
        {
            IsLoading = false;
        }
    }

    // — Update —
    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    public async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            var update = await _updateService.CheckForUpdateAsync(_tokenStore.LoadServerUrl(), _authService.CurrentToken);
            if (update != null)
            {
                AvailableUpdate = update;
            }
            else
            {
                AvailableUpdate = null;
                StatusMessage = T("desktop.settings.up_to_date");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = T("desktop.settings.update_check_failed", ("message", ex.Message));
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
            var success = await _updateService.DownloadAndInstallAsync(AvailableUpdate, _authService.CurrentToken, progress);
            if (!success)
                ErrorMessage = T("desktop.settings.download_failed");
        }
        catch (Exception ex)
        {
            ErrorMessage = T("desktop.settings.update_failed", ("message", ex.Message));
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    private bool CanDownloadUpdate() => AvailableUpdate != null && !IsDownloadingUpdate && !IsCheckingForUpdates;

    private bool CanDeleteToken() => SelectedToken != null && !IsLoading;
    private bool CanSaveProject() => !IsSavingProject;
    private bool CanSaveUser() => !IsSavingUser;
    private bool HasSelectedProject() => SelectedProject != null;
    private bool HasSelectedUser() => SelectedUser != null;
    private bool CanRunApiStatusCheck() => IsAdmin && !IsRunningApiStatusCheck;

    partial void OnServerUrlChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _tokenStore.SaveServerUrl(value);
            StatusMessage = T("desktop.settings.server_url_saved");
        }
    }

    partial void OnSelectedTokenChanged(TokenListItem? value) => DeleteTokenCommand.NotifyCanExecuteChanged();
    partial void OnIsRunningApiStatusCheckChanged(bool value) => RunApiStatusCheckCommand.NotifyCanExecuteChanged();

    private void LoadLoggingSettings()
    {
        var fromEnv = Environment.GetEnvironmentVariable("DKC_LOG_LEVEL");
        if (TryNormalizeLogLevel(fromEnv, out var envLevel))
        {
            SelectedLogLevel = envLevel;
            LoggingMessage = T("desktop.settings.log_level_from_env");
            return;
        }

        var configPath = Path.Combine(_dataDir, "logging.json");
        if (File.Exists(configPath))
        {
            try
            {
                var raw = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<LoggingConfig>(raw);
                if (TryNormalizeLogLevel(config?.LogLevel, out var fileLevel))
                {
                    SelectedLogLevel = fileLevel;
                    return;
                }
            }
            catch
            {
                // Fallback auf Default erfolgt unten.
            }
        }

#if DEBUG
        SelectedLogLevel = "Debug";
#else
        SelectedLogLevel = "Information";
#endif
    }

    private bool TryNormalizeLogLevel(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        normalized = SupportedLogLevels.FirstOrDefault(l =>
            string.Equals(l, raw.Trim(), StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

        return !string.IsNullOrWhiteSpace(normalized);
    }

    private async Task CheckEndpointAsync(string endpoint, Func<Task> call)
    {
        try
        {
            await call();
            ApiStatusItems.Add(new ApiStatusItem(endpoint, "OK", "Erreichbar", "#2F855A"));
        }
        catch (ApiException ex)
        {
            var statusCode = ex.StatusCode == HttpStatusCode.Unused ? "HTTP ?" : $"HTTP {(int)ex.StatusCode}";
            var details = string.IsNullOrWhiteSpace(ex.Content) ? ex.Message : ex.Content;
            ApiStatusItems.Add(new ApiStatusItem(endpoint, statusCode, TrimForUi(details), "#C53030"));
        }
        catch (Exception ex)
        {
            ApiStatusItems.Add(new ApiStatusItem(endpoint, "Fehler", TrimForUi(ex.Message), "#C53030"));
        }
    }

    private static string TrimForUi(string message)
    {
        const int maxLength = 220;
        if (string.IsNullOrWhiteSpace(message) || message.Length <= maxLength)
            return message;

        return message[..maxLength] + "...";
    }

    private sealed record LoggingConfig(string LogLevel);
    public sealed record ApiStatusItem(string Endpoint, string Status, string Details, string StatusColor);
    partial void OnIsLoadingChanged(bool value) => DeleteTokenCommand.NotifyCanExecuteChanged();
    partial void OnSelectedProjectChanged(Project? value)
    {
        ShowEditProjectFormCommand.NotifyCanExecuteChanged();
        SetActiveProjectCommand.NotifyCanExecuteChanged();
    }
    partial void OnSelectedUserChanged(AdminUser? value)
    {
        ShowEditUserFormCommand.NotifyCanExecuteChanged();
        DeleteUserCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsSavingProjectChanged(bool value) => SaveProjectCommand.NotifyCanExecuteChanged();
    partial void OnIsSavingUserChanged(bool value) => SaveUserCommand.NotifyCanExecuteChanged();
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

/// <summary>Eintrag der Sprachauswahl; ohne Namen ist es „Automatisch“ (übersetzt).</summary>
public sealed record LanguageOption(string Code, string? NativeName)
{
    public string Label => NativeName ?? T("desktop.settings.language_auto");
    public override string ToString() => Label;
}
