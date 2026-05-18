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
    private readonly ConnectivityService _connectivityService;

    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private string _userDisplayName = string.Empty;
    [ObservableProperty] private string _userInitials = string.Empty;
    [ObservableProperty] private bool _isPaneOpen = true;
    [ObservableProperty] private NavItem? _selectedNavItem;
    [ObservableProperty] private UpdateInfo? _availableUpdate;
    [ObservableProperty] private int _unreadNotificationCount;
    [ObservableProperty] private IReadOnlyList<BreadcrumbItem> _breadcrumbs = Array.Empty<BreadcrumbItem>();
    [ObservableProperty] private bool _isOffline;

    public LoginViewModel LoginViewModel { get; }
    public DashboardViewModel DashboardViewModel { get; }
    public NeaViewModel NeaViewModel { get; }
    public MmViewModel MmViewModel { get; }
    public BuildingViewModel BuildingViewModel { get; }
    public KlimaViewModel KlimaViewModel { get; }
    public KeysViewModel KeysViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }
    public WlsViewModel WlsViewModel { get; }
    public NotificationsViewModel NotificationsViewModel { get; }

    /// <summary>The currently displayed view, driven by <see cref="INavigationService"/>.</summary>
    public ViewModelBase? CurrentView => _navigationService.CurrentView;

    public ObservableCollection<NavItem> NavItems { get; } = new();

    public MainWindowViewModel(
        AuthService authService,
        UpdateService updateService,
        INavigationService navigationService,
        NotificationPollingService notificationPollingService,
        ConnectivityService connectivityService,
        LoginViewModel loginViewModel,
        DashboardViewModel dashboardViewModel,
        NeaViewModel neaViewModel,
        MmViewModel mmViewModel,
        BuildingViewModel buildingViewModel,
        KlimaViewModel klimaViewModel,
        KeysViewModel keysViewModel,
        SettingsViewModel settingsViewModel,
        WlsViewModel wlsViewModel,
        NotificationsViewModel notificationsViewModel)
    {
        _authService                = authService;
        _updateService              = updateService;
        _navigationService          = navigationService;
        _notificationPollingService = notificationPollingService;
        _connectivityService        = connectivityService;
        LoginViewModel         = loginViewModel;
        DashboardViewModel     = dashboardViewModel;
        NeaViewModel           = neaViewModel;
        MmViewModel            = mmViewModel;
        BuildingViewModel      = buildingViewModel;
        KlimaViewModel         = klimaViewModel;
        KeysViewModel          = keysViewModel;
        SettingsViewModel      = settingsViewModel;
        WlsViewModel           = wlsViewModel;
        NotificationsViewModel = notificationsViewModel;

        _authService.AuthStateChanged += OnAuthStateChanged;
        _navigationService.CurrentViewChanged  += (_, _) => OnPropertyChanged(nameof(CurrentView));
        _navigationService.BreadcrumbsChanged  += (_, _) => Breadcrumbs = _navigationService.Breadcrumbs;
        DashboardViewModel.CreateMmRequested += (_, _) =>
        {
            MmViewModel.ShowCreateForm();
            SelectNavItem(MmViewModel, "Maengelmeldungen");
        };
        DashboardViewModel.StartNeaInspectionRequested += (_, _) => SelectNavItem(NeaViewModel, "NEA");
        _notificationPollingService.UnreadCountChanged += (_, count) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => UnreadNotificationCount = count);
        _connectivityService.ConnectivityChanged += (_, isOnline) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => IsOffline = !isOnline);

        // Initialise from current connectivity state (may already be known)
        IsOffline = !_connectivityService.IsOnline;

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
            UserInitials = BuildInitials(UserDisplayName);
            RebuildNavItems();
            SelectedNavItem = NavItems.FirstOrDefault(n => n.ViewModel == DashboardViewModel);
            _navigationService.NavigateToRoot(DashboardViewModel, "Dashboard");
            _ = DashboardViewModel.LoadDataAsync();
        }
        else
        {
            NavItems.Clear();
            UserInitials = string.Empty;
            _navigationService.NavigateToRoot(LoginViewModel, "Login");
        }
    }

    private void RebuildNavItems()
    {
        NavItems.Clear();
        NavItems.Add(new NavItem("📊", "Dashboard", DashboardViewModel));
        NavItems.Add(new NavItem("⚡", "NEA", NeaViewModel));
        NavItems.Add(new NavItem("🔧", "Maengelmeldungen", MmViewModel));
        NavItems.Add(new NavItem("🏢", "Buildings", BuildingViewModel));
        NavItems.Add(new NavItem("❄️", "Climate", KlimaViewModel));
        NavItems.Add(new NavItem("🔑", "Keys", KeysViewModel));
        NavItems.Add(new NavItem("🏠", "WLS", WlsViewModel));
        NavItems.Add(new NavItem("🔔", "Benachrichtigungen", NotificationsViewModel));
        NavItems.Add(new NavItem("⚙️", "Settings", SettingsViewModel));
    }

    private static string BuildInitials(string displayName)
    {
        var parts = displayName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0)
            .Take(2)
            .Select(p => char.ToUpperInvariant(p[0]));
        var initials = string.Concat(parts);
        return string.IsNullOrWhiteSpace(initials) ? "DK" : initials;
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (value != null)
        {
            _navigationService.NavigateTo(value.ViewModel, value.Title);
        }
    }

    private void SelectNavItem(ViewModelBase viewModel, string title)
    {
        var navItem = NavItems.FirstOrDefault(n => n.ViewModel == viewModel);
        if (navItem != null)
        {
            SelectedNavItem = navItem;
        }
        else
        {
            _navigationService.NavigateTo(viewModel, title);
        }
    }

    /// <summary>Navigates to a module by 1-based index (Ctrl+1 … Ctrl+9).</summary>
    [RelayCommand]
    private void NavigateToModule(string? indexStr)
    {
        if (!int.TryParse(indexStr, out var index)) return;
        if (index < 1 || index > NavItems.Count) return;
        SelectedNavItem = NavItems[index - 1];
    }

    /// <summary>Triggers a manual refresh on the current view if it supports it.</summary>
    [RelayCommand]
    private void RefreshCurrentModule()
    {
        switch (CurrentView)
        {
            case DashboardViewModel vm:     _ = vm.LoadDataCommand.ExecuteAsync(null);          break;
            case NeaViewModel vm:           _ = vm.LoadSystemsCommand.ExecuteAsync(null);       break;
            case MmViewModel vm:            _ = vm.LoadMessagesCommand.ExecuteAsync(null);      break;
            case BuildingViewModel vm:      _ = vm.LoadBuildingsCommand.ExecuteAsync(null);     break;
            case KlimaViewModel vm:         _ = vm.LoadDataCommand.ExecuteAsync(null);          break;
            case KeysViewModel vm:          _ = vm.LoadDataCommand.ExecuteAsync(null);          break;
            case WlsViewModel vm:           _ = vm.LoadBuildingsCommand.ExecuteAsync(null);     break;
            case NotificationsViewModel vm: _ = vm.LoadNotificationsCommand.ExecuteAsync(null); break;
        }
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
    public string Icon { get; }
    public string Title { get; }
    public ViewModelBase ViewModel { get; }

    /// <param name="icon">Short visual icon shown before the navigation title.</param>
    /// <param name="title">Navigation label shown in the sidebar.</param>
    /// <param name="viewModel">ViewModel displayed when the item is selected.</param>
    public NavItem(string icon, string title, ViewModelBase viewModel)
    {
        Icon      = icon;
        Title     = title;
        ViewModel = viewModel;
    }
}
