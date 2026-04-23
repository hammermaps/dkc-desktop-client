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

    public bool IsAdmin => _authService.CurrentUser?.IsAdmin ?? false;
    public string? CurrentUsername => _authService.CurrentUser?.Username;

    public SettingsViewModel(DkcApiFactory apiFactory, AuthService authService, TokenStore tokenStore)
    {
        _apiFactory = apiFactory;
        _authService = authService;
        _tokenStore = tokenStore;
        ServerUrl = tokenStore.LoadServerUrl() ?? string.Empty;
        _authService.AuthStateChanged += OnAuthStateChanged;
    }

    private void OnAuthStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(CurrentUsername));
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
            var result = await api.DeleteTokenAsync(new TokenDeleteRequest(SelectedToken.Id));
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
            ErrorMessage = $"Error loading projects: {ex.Message}";
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
            ProjectFormError = "Name is required.";
            return;
        }
        IsSavingProject = true;
        ProjectFormError = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var req = new ProjectSaveRequest(
                FormProjectName,
                string.IsNullOrWhiteSpace(FormProjectDescription) ? null : FormProjectDescription);

            ApiError result;
            if (IsEditingProject && _editingProjectId.HasValue)
            {
                result = await api.UpdateProjectAsync(_editingProjectId.Value, req);
            }
            else
            {
                var cr = await api.CreateProjectAsync(req);
                result = new ApiError(cr.Success, cr.Error);
            }

            if (result.Success)
            {
                IsProjectFormVisible = false;
                await LoadProjectsAsync();
                StatusMessage = "Project saved.";
            }
            else
            {
                ProjectFormError = result.Error ?? "Save failed.";
            }
        }
        catch (Exception ex)
        {
            ProjectFormError = $"Error: {ex.Message}";
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
                StatusMessage = $"Active project set to '{SelectedProject.Name}'.";
            else
                ErrorMessage = result.Error ?? "Failed to set active project.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
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
            ErrorMessage = $"Error loading users: {ex.Message}";
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
            UserFormError = "Username is required.";
            return;
        }
        if (!IsEditingUser && string.IsNullOrWhiteSpace(FormPassword))
        {
            UserFormError = "Password is required for new users.";
            return;
        }
        IsSavingUser = true;
        UserFormError = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var req = new UserSaveRequest(
                FormUsername,
                string.IsNullOrWhiteSpace(FormPassword) ? null : FormPassword,
                string.IsNullOrWhiteSpace(FormVname) ? null : FormVname,
                string.IsNullOrWhiteSpace(FormNname) ? null : FormNname,
                string.IsNullOrWhiteSpace(FormEmail) ? null : FormEmail,
                FormIsAdmin);

            ApiError result;
            if (IsEditingUser && _editingUserId.HasValue)
            {
                result = await api.UpdateUserAsync(_editingUserId.Value, req);
            }
            else
            {
                var cr = await api.CreateUserAsync(req);
                result = new ApiError(cr.Success, cr.Error);
            }

            if (result.Success)
            {
                IsUserFormVisible = false;
                await LoadUsersAsync();
                StatusMessage = "User saved.";
            }
            else
            {
                UserFormError = result.Error ?? "Save failed.";
            }
        }
        catch (Exception ex)
        {
            UserFormError = $"Error: {ex.Message}";
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
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.DeleteUserAsync(SelectedUser.Id);
            if (result.Success)
            {
                Users.Remove(SelectedUser);
                StatusMessage = "User deleted.";
            }
            else
            {
                ErrorMessage = result.Error ?? "Delete failed.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanDeleteToken() => SelectedToken != null && !IsLoading;
    private bool CanSaveProject() => !IsSavingProject;
    private bool CanSaveUser() => !IsSavingUser;
    private bool HasSelectedProject() => SelectedProject != null;
    private bool HasSelectedUser() => SelectedUser != null;

    partial void OnServerUrlChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _tokenStore.SaveServerUrl(value);
            StatusMessage = "Server-URL gespeichert.";
        }
    }

    partial void OnSelectedTokenChanged(TokenListItem? value) => DeleteTokenCommand.NotifyCanExecuteChanged();
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
}
