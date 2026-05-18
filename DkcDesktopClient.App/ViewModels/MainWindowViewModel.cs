using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.App.Services;
using DkcDesktopClient.Core.Services;

namespace DkcDesktopClient.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AuthService _authService;
    private readonly UpdateService _updateService;
    private readonly INavigationService _navigationService;
    private readonly NotificationPollingService _notificationPollingService;

    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private string _userDisplayName = string.Empty;
    [ObservableProperty] private bool _isPaneOpen = true;
    [ObservableProperty] private NavItem? _selectedNavItem;
    [ObservableProperty] private UpdateInfo? _availableUpdate;
    [ObservableProperty] private int _unreadNotificationCount;
    [ObservableProperty] private IReadOnlyList<BreadcrumbItem> _breadcrumbs = Array.Empty<BreadcrumbItem>();

    public LoginViewModel LoginViewModel { get; }
    public DashboardViewModel DashboardViewModel { get; }
    public NeaViewModel NeaViewModel { get; }
    public MmViewModel MmViewModel { get; }
    public BuildingViewModel BuildingViewModel { get; }
    public KlimaViewModel KlimaViewModel { get; }
    public KeysViewModel KeysViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }

    /// <summary>The currently displayed view, driven by <see cref="INavigationService"/>.</summary>
    public ViewModelBase? CurrentView => _navigationService.CurrentView;

    public ObservableCollection<NavItem> NavItems { get; } = new();

    public MainWindowViewModel(
        AuthService authService,
        UpdateService updateService,
        INavigationService navigationService,
        NotificationPollingService notificationPollingService,
        LoginViewModel loginViewModel,
        DashboardViewModel dashboardViewModel,
        NeaViewModel neaViewModel,
        MmViewModel mmViewModel,
        BuildingViewModel buildingViewModel,
        KlimaViewModel klimaViewModel,
        KeysViewModel keysViewModel,
        SettingsViewModel settingsViewModel)
    {
        _authService                = authService;
        _updateService              = updateService;
        _navigationService          = navigationService;
        _notificationPollingService = notificationPollingService;
        LoginViewModel     = loginViewModel;
        DashboardViewModel = dashboardViewModel;
        NeaViewModel       = neaViewModel;
        MmViewModel        = mmViewModel;
        BuildingViewModel  = buildingViewModel;
        KlimaViewModel     = klimaViewModel;
        KeysViewModel      = keysViewModel;
        SettingsViewModel  = settingsViewModel;

        _authService.AuthStateChanged += OnAuthStateChanged;
        _navigationService.CurrentViewChanged  += (_, _) => OnPropertyChanged(nameof(CurrentView));
        _navigationService.BreadcrumbsChanged  += (_, _) => Breadcrumbs = _navigationService.Breadcrumbs;
        _notificationPollingService.UnreadCountChanged += (_, count) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => UnreadNotificationCount = count);

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
            _navigationService.NavigateToRoot(DashboardViewModel, "Dashboard");
            _ = DashboardViewModel.LoadDataAsync();
        }
        else
        {
            NavItems.Clear();
            _navigationService.NavigateToRoot(LoginViewModel, "Login");
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
            _navigationService.NavigateTo(value.ViewModel, value.Title);
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
            _navigationService.NavigateToRoot(LoginViewModel, "Login");

        _ = CheckForUpdateAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            AvailableUpdate = await _updateService.CheckForUpdateAsync();
        }
        catch
        {
            // silently ignore update check failures on startup
        }
    }

    [RelayCommand]
    private void DismissUpdate()
    {
        AvailableUpdate = null;
    }

    [RelayCommand]
    private void ShowUpdateInSettings()
    {
        SelectedNavItem = NavItems.FirstOrDefault(n => n.ViewModel == SettingsViewModel);
    }
}

public class NavItem
{
    public string Title { get; }
    public ViewModelBase ViewModel { get; }

    public NavItem(string title, ViewModelBase viewModel)
    {
        Title     = title;
        ViewModel = viewModel;
    }
}
