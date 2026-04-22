using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.Core.Services;

namespace DkcDesktopClient.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AuthService _authService;

    [ObservableProperty] private ViewModelBase? _currentView;
    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private string _userDisplayName = string.Empty;
    [ObservableProperty] private bool _isPaneOpen = true;
    [ObservableProperty] private NavItem? _selectedNavItem;

    public LoginViewModel LoginViewModel { get; }
    public DashboardViewModel DashboardViewModel { get; }
    public NeaViewModel NeaViewModel { get; }
    public MmViewModel MmViewModel { get; }
    public BuildingViewModel BuildingViewModel { get; }
    public KlimaViewModel KlimaViewModel { get; }
    public KeysViewModel KeysViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }

    public ObservableCollection<NavItem> NavItems { get; } = new();

    public MainWindowViewModel(
        AuthService authService,
        LoginViewModel loginViewModel,
        DashboardViewModel dashboardViewModel,
        NeaViewModel neaViewModel,
        MmViewModel mmViewModel,
        BuildingViewModel buildingViewModel,
        KlimaViewModel klimaViewModel,
        KeysViewModel keysViewModel,
        SettingsViewModel settingsViewModel)
    {
        _authService = authService;
        LoginViewModel = loginViewModel;
        DashboardViewModel = dashboardViewModel;
        NeaViewModel = neaViewModel;
        MmViewModel = mmViewModel;
        BuildingViewModel = buildingViewModel;
        KlimaViewModel = klimaViewModel;
        KeysViewModel = keysViewModel;
        SettingsViewModel = settingsViewModel;

        _authService.AuthStateChanged += OnAuthStateChanged;
        UpdateAuthState();
    }

    private void OnAuthStateChanged(object? sender, EventArgs e) => UpdateAuthState();

    private void UpdateAuthState()
    {
        IsLoggedIn = _authService.IsAuthenticated;
        if (IsLoggedIn)
        {
            var user = _authService.CurrentUser;
            UserDisplayName = user != null ? $"{user.Vname} {user.Nname}".Trim() : _authService.CurrentUser?.Username ?? string.Empty;
            RebuildNavItems();
            CurrentView = DashboardViewModel;
            _ = DashboardViewModel.LoadDataAsync();
        }
        else
        {
            NavItems.Clear();
            CurrentView = LoginViewModel;
        }
    }

    private void RebuildNavItems()
    {
        NavItems.Clear();
        NavItems.Add(new NavItem("Dashboard", DashboardViewModel));
        NavItems.Add(new NavItem("NEA", NeaViewModel));
        NavItems.Add(new NavItem("Maengelmeldungen", MmViewModel));
        NavItems.Add(new NavItem("Buildings", BuildingViewModel));
        NavItems.Add(new NavItem("Climate", KlimaViewModel));
        NavItems.Add(new NavItem("Keys", KeysViewModel));
        NavItems.Add(new NavItem("Settings", SettingsViewModel));
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (value != null)
            CurrentView = value.ViewModel;
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _authService.LogoutAsync();
    }

    [RelayCommand]
    private void TogglePane()
    {
        IsPaneOpen = !IsPaneOpen;
    }

    public async Task InitializeAsync()
    {
        if (!await _authService.TryAutoLoginAsync())
            CurrentView = LoginViewModel;
    }
}

public class NavItem
{
    public string Title { get; }
    public ViewModelBase ViewModel { get; }

    public NavItem(string title, ViewModelBase viewModel)
    {
        Title = title;
        ViewModel = viewModel;
    }
}
