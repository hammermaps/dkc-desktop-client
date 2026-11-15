using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.App.Services;
using DkcDesktopClient.Core.Services;
using static DkcDesktopClient.Core.I18n.Translator;

namespace DkcDesktopClient.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AuthService _authService;
    private readonly UpdateService _updateService;
    private readonly TokenStore _tokenStore;
    private readonly INavigationService _navigationService;
    private readonly NotificationPollingService _notificationPollingService;
    private readonly ConnectivityService _connectivityService;
    private readonly MaintenanceMonitor? _maintenance;

    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private string _userDisplayName = string.Empty;
    [ObservableProperty] private string _userInitials = string.Empty;
    [ObservableProperty] private bool _isPaneOpen = true;
    [ObservableProperty] private NavItem? _selectedNavItem;
    [ObservableProperty] private UpdateInfo? _availableUpdate;
    [ObservableProperty] private int _unreadNotificationCount;
    [ObservableProperty] private IReadOnlyList<BreadcrumbItem> _breadcrumbs = Array.Empty<BreadcrumbItem>();
    [ObservableProperty] private bool _isOffline;

    // Wartungsmodus des Servers (Header X-DKC-Maintenance, siehe MaintenanceMonitor)
    [ObservableProperty] private bool _isMaintenanceAllowed;
    [ObservableProperty] private bool _isMaintenanceBlocked;
    [ObservableProperty] private string _maintenanceText = string.Empty;

    public LoginViewModel LoginViewModel { get; }
    public DashboardViewModel DashboardViewModel { get; }
    public NeaViewModel NeaViewModel { get; }
    public AufzugPruefungViewModel AufzugPruefungViewModel { get; }
    public SprinklerPruefungViewModel SprinklerPruefungViewModel { get; }
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
        TokenStore tokenStore,
        INavigationService navigationService,
        NotificationPollingService notificationPollingService,
        ConnectivityService connectivityService,
        LoginViewModel loginViewModel,
        DashboardViewModel dashboardViewModel,
        NeaViewModel neaViewModel,
        AufzugPruefungViewModel aufzugPruefungViewModel,
        SprinklerPruefungViewModel sprinklerPruefungViewModel,
        MmViewModel mmViewModel,
        BuildingViewModel buildingViewModel,
        KlimaViewModel klimaViewModel,
        KeysViewModel keysViewModel,
        SettingsViewModel settingsViewModel,
        WlsViewModel wlsViewModel,
        NotificationsViewModel notificationsViewModel,
        MaintenanceMonitor? maintenance = null)
    {
        _maintenance = maintenance;
        _authService                = authService;
        _updateService              = updateService;
        _tokenStore                 = tokenStore;
        _navigationService          = navigationService;
        _notificationPollingService = notificationPollingService;
        _connectivityService        = connectivityService;
        LoginViewModel         = loginViewModel;
        DashboardViewModel     = dashboardViewModel;
        NeaViewModel           = neaViewModel;
        AufzugPruefungViewModel    = aufzugPruefungViewModel;
        SprinklerPruefungViewModel = sprinklerPruefungViewModel;
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
            _ = MmViewModel.ShowCreateFormAsync();
            SelectNavItem(MmViewModel, T("desktop.nav.mm"));
        };
        DashboardViewModel.StartNeaInspectionRequested += (_, _) => SelectNavItem(NeaViewModel, T("desktop.nav.nea"));
        _notificationPollingService.UnreadCountChanged += (_, count) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => UnreadNotificationCount = count);
        _connectivityService.ConnectivityChanged += (_, isOnline) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => IsOffline = !isOnline);

        // Initialise from current connectivity state (may already be known)
        IsOffline = !_connectivityService.IsOnline;

        if (_maintenance != null)
        {
            _maintenance.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(ApplyMaintenanceState);
            DkcDesktopClient.Core.I18n.Translator.Instance.Changed += (_, _) => ApplyMaintenanceState();
            ApplyMaintenanceState();
        }

        UpdateAuthState();
    }

    private void OnAuthStateChanged(object? sender, EventArgs e)
    {
        if (!_authService.IsAuthenticated) _maintenance?.Reset();
        UpdateAuthState();
    }

    private bool _wasMaintenanceBlocked;

    /// <summary>
    /// Zeigt den Wartungszustand an. Endet eine Sperre, fehlen dem Client die damals abgewiesenen
    /// Rechte – sie werden neu geladen, die Navigation neu aufgebaut und die Ansicht aktualisiert.
    /// </summary>
    private void ApplyMaintenanceState()
    {
        if (_maintenance == null) return;
        var state = _maintenance.State;
        IsMaintenanceAllowed = state == MaintenanceState.Allowed;
        IsMaintenanceBlocked = state == MaintenanceState.Blocked;
        MaintenanceText = BuildMaintenanceText(state, _maintenance.Message, _maintenance.EstimatedEnd);

        if (_wasMaintenanceBlocked && state != MaintenanceState.Blocked && IsLoggedIn)
            _ = RecoverAfterMaintenanceAsync();
        _wasMaintenanceBlocked = state == MaintenanceState.Blocked;
    }

    public static string BuildMaintenanceText(MaintenanceState state, string message, string? end)
    {
        if (state == MaintenanceState.Off) return string.Empty;
        var text = state == MaintenanceState.Blocked
            ? T("desktop.maintenance.blocked")
            : T("desktop.maintenance.allowed");
        if (!string.IsNullOrWhiteSpace(message)) text += " " + message.Trim();
        if (!string.IsNullOrWhiteSpace(end)) text += " " + T("desktop.maintenance.estimated_end", ("end", end));
        if (state == MaintenanceState.Blocked) text += " " + T("desktop.maintenance.auto_retry");
        return text;
    }

    private async Task RecoverAfterMaintenanceAsync()
    {
        await _authService.ReloadPermissionsAsync();
        var current = SelectedNavItem?.ViewModel;
        RebuildNavItems();
        var restored = NavItems.FirstOrDefault(n => n.ViewModel == current);
        if (restored != null && !ReferenceEquals(restored, SelectedNavItem))
            SelectedNavItem = restored;
        RefreshCurrentModule();
    }

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
            _navigationService.NavigateToRoot(DashboardViewModel, T("desktop.nav.dashboard"));
            _ = DashboardViewModel.LoadDataAsync();
        }
        else
        {
            NavItems.Clear();
            UserInitials = string.Empty;
            _navigationService.NavigateToRoot(LoginViewModel, T("desktop.login.login"));
        }
    }

    private void RebuildNavItems()
    {
        NavItems.Clear();
        NavItems.Add(new NavItem("📊", "desktop.nav.dashboard", DashboardViewModel));
        NavItems.Add(new NavItem("⚡", "desktop.nav.nea", NeaViewModel));
        // Wie im Web nur mit Leserecht sichtbar (Admins sehen alles).
        if (CanView(AufzugPruefungViewModel))
            NavItems.Add(new NavItem("🛗", "desktop.nav.aufzug", AufzugPruefungViewModel));
        if (CanView(SprinklerPruefungViewModel))
            NavItems.Add(new NavItem("💧", "desktop.nav.sprinkler", SprinklerPruefungViewModel));
        NavItems.Add(new NavItem("🔧", "desktop.nav.mm", MmViewModel));
        NavItems.Add(new NavItem("🏢", "desktop.nav.buildings", BuildingViewModel));
        NavItems.Add(new NavItem("❄️", "desktop.nav.klima", KlimaViewModel));
        NavItems.Add(new NavItem("🔑", "desktop.nav.keys", KeysViewModel));
        NavItems.Add(new NavItem("🏠", "desktop.nav.wls", WlsViewModel));
        NavItems.Add(new NavItem("🔔", "desktop.nav.notifications", NotificationsViewModel));
        NavItems.Add(new NavItem("⚙️", "desktop.nav.settings", SettingsViewModel));
    }

    private bool CanView(PruefungViewModel vm)
        => (_authService.CurrentUser?.IsAdmin ?? false)
           || _authService.HasPermission(vm.Domain.ViewPermission)
           || vm.CanEdit;

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
        // Cancel any in-flight load on the view we are leaving.
        CurrentView?.CancelLoad();
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
            case NeaViewModel vm:
                // Refresh both systems and inspections (inspection data depends on selected system)
                _ = vm.LoadSystemsCommand.ExecuteAsync(null);
                _ = vm.LoadInspectionsCommand.ExecuteAsync(null);
                break;
            case MmViewModel vm:            _ = vm.LoadMessagesCommand.ExecuteAsync(null);      break;
            case PruefungViewModel vm:      _ = vm.LoadCommand.ExecuteAsync(null);              break;
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
            _navigationService.NavigateToRoot(LoginViewModel, T("desktop.login.login"));

        _ = CheckForUpdateAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            AvailableUpdate = await _updateService.CheckForUpdateAsync(_tokenStore.LoadServerUrl(), _authService.CurrentToken);
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

public class NavItem : System.ComponentModel.INotifyPropertyChanged
{
    public string Icon { get; }
    public string TitleKey { get; }
    /// <summary>Übersetzter Titel; folgt einem Sprachwechsel.</summary>
    public string Title => T(TitleKey);
    public ViewModelBase ViewModel { get; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <param name="icon">Short visual icon shown before the navigation title.</param>
    /// <param name="titleKey">Übersetzungsschlüssel des Titels (desktop.nav.*).</param>
    /// <param name="viewModel">ViewModel displayed when the item is selected.</param>
    public NavItem(string icon, string titleKey, ViewModelBase viewModel)
    {
        Icon      = icon;
        TitleKey  = titleKey;
        ViewModel = viewModel;
        // Translator.Changed läuft bereits auf dem UI-Thread (Translator.Notify).
        DkcDesktopClient.Core.I18n.Translator.Instance.Changed += (_, _) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Title)));
    }
}
