using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Protobuf;
using DkcDesktopClient.Core.Services;
using TemperatureHistoryPoint = DkcDesktopClient.Core.Protocol.TemperatureHistoryPoint;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace DkcDesktopClient.App.ViewModels;

/// <summary>Eine Alarmzeile der Sammelstörung (AC/SiBe/KinCony/RCO). Web-Pendant: ContDashboard::getSammelstoerung().</summary>
public record SammelstoerungAlarmDisplay(string Source, string SourceLabel, string DeviceName, string AlarmText, string Timestamp, string Url);

/// <summary>Ein aktuell ausgegebener Schlüssel/Bund. Web-Pendant: ContDashboard::getIssuedKeysData().</summary>
public record IssuedKeyDisplay(int Id, string EmpfaengerName, string AusgegebenAm, string RueckgabeGeplant, string Type, string Name);

/// <summary>Eine offene MM-Freigabe. Web-Pendant: ContDashboard::getApprovalStatus().</summary>
public record MmApprovalDisplay(int Id, string Uid, string NachunternehmerName, string Address, string Title, string CreatedAt, int DaysWaiting);

/// <summary>Eine überfällige Mängelmeldung (Freigabe &gt; 5 Tage bzw. beim NU &gt; 14 Tage). Web-Pendant: DashboardComplianceTrait::getOverdueComplaints().</summary>
public record OverdueComplaintDisplay(int Id, string Uid, string NachunternehmerName, string Address, string Title, string CreatedAt, int DaysOpen, int Status, string StatusLabel);

/// <summary>Eine WLS-fällige/überfällige Wohnung. Web-Pendant: DashboardComplianceTrait::getWlsDueFlushings().</summary>
public record WlsFlushDisplay(int Id, string Label, string BuildingName, string LastFlushDate, string NextFlushDue, int DaysOverdue, bool NeverFlushed);

public partial class DashboardViewModel : ViewModelBase
{
    private readonly DkcApiFactory _apiFactory;
    private readonly AuthService _authService;
    private readonly DataCacheService _cache;
    private readonly BackgroundRefreshService _backgroundRefreshService;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private ObservableCollection<Project> _projects = new();
    [ObservableProperty] private Project? _selectedProject;
    [ObservableProperty] private int _mmTotal;
    [ObservableProperty] private int _mmOpen;
    [ObservableProperty] private int _mmInProgress;
    [ObservableProperty] private int _keysAvailable;
    [ObservableProperty] private int _neaTotalSystems;
    [ObservableProperty] private int _neaOverdueInspections;
    [ObservableProperty] private bool _hasOverdueItems;
    [ObservableProperty] private ObservableCollection<NeaOverdueItem> _overdueItems = new();
    [ObservableProperty] private ObservableCollection<NeaRecentInspection> _recentInspections = new();
    [ObservableProperty] private bool _isSettingProject;

    // ── Dashboard-Widgets (Web-Pendant: dashboard_overview.tpl / DASHBOARD_WIDGETS) ──
    [ObservableProperty] private bool _sammelstoerungPermission;
    [ObservableProperty] private int _sammelstoerungCount;
    [ObservableProperty] private ObservableCollection<SammelstoerungAlarmDisplay> _sammelstoerung = new();

    [ObservableProperty] private bool _issuedKeysPermission;
    [ObservableProperty] private int _issuedKeysCount;
    [ObservableProperty] private ObservableCollection<IssuedKeyDisplay> _issuedKeysList = new();

    [ObservableProperty] private bool _approvalsPermission;
    [ObservableProperty] private int _approvalsCount;
    [ObservableProperty] private ObservableCollection<MmApprovalDisplay> _approvals = new();

    [ObservableProperty] private bool _overdueComplaintsPermission;
    [ObservableProperty] private int _overdueComplaintsCount;
    [ObservableProperty] private ObservableCollection<OverdueComplaintDisplay> _overdueComplaints = new();

    [ObservableProperty] private bool _wlsPermission;
    [ObservableProperty] private int _wlsCount;
    [ObservableProperty] private int _wlsOverdueCount;
    [ObservableProperty] private ObservableCollection<WlsFlushDisplay> _wlsFlushings = new();

    [ObservableProperty] private bool _temperaturePermission;
    [ObservableProperty] private double _temperatureAverage;
    [ObservableProperty] private int _temperatureActiveDevices;
    [ObservableProperty] private int _temperatureTotalDevices;
    [ObservableProperty] private int _temperatureOnlineDevices;
    [ObservableProperty] private ISeries[] _temperatureSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _temperatureXAxes = { new Axis() };
    [ObservableProperty] private Axis[] _temperatureYAxes = { new Axis { Labeler = value => $"{value:0.#} °C" } };

    public bool HasSammelstoerung => SammelstoerungCount > 0;

    public string MmTotalText => MmTotal.ToString("N0");
    public string MmOpenText => MmOpen.ToString("N0");
    public string KeysAvailableText => KeysAvailable.ToString("N0");
    public string NeaTotalSystemsText => NeaTotalSystems.ToString("N0");
    public string NeaOverdueInspectionsText => NeaOverdueInspections.ToString("N0");
    public string TemperatureAverageText => TemperaturePermission ? $"{TemperatureAverage:0.0} °C" : "–";
    public string SammelstoerungCountText => SammelstoerungCount.ToString("N0");
    public string IssuedKeysCountText => IssuedKeysCount.ToString("N0");

    public DashboardViewModel(
        DkcApiFactory apiFactory,
        AuthService authService,
        DataCacheService cache,
        BackgroundRefreshService backgroundRefreshService)
    {
        _apiFactory = apiFactory;
        _authService = authService;
        _cache = cache;
        _backgroundRefreshService = backgroundRefreshService;
        _backgroundRefreshService.DataRefreshed += OnDataRefreshed;
    }

    public event EventHandler? CreateMmRequested;
    public event EventHandler? StartNeaInspectionRequested;

    partial void OnMmTotalChanged(int value) => OnPropertyChanged(nameof(MmTotalText));
    partial void OnMmOpenChanged(int value) => OnPropertyChanged(nameof(MmOpenText));
    partial void OnKeysAvailableChanged(int value) => OnPropertyChanged(nameof(KeysAvailableText));
    partial void OnNeaTotalSystemsChanged(int value) => OnPropertyChanged(nameof(NeaTotalSystemsText));
    partial void OnNeaOverdueInspectionsChanged(int value) => OnPropertyChanged(nameof(NeaOverdueInspectionsText));
    partial void OnSammelstoerungCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSammelstoerung));
        OnPropertyChanged(nameof(SammelstoerungCountText));
    }
    partial void OnIssuedKeysCountChanged(int value) => OnPropertyChanged(nameof(IssuedKeysCountText));
    partial void OnTemperatureAverageChanged(double value) => OnPropertyChanged(nameof(TemperatureAverageText));
    partial void OnTemperaturePermissionChanged(bool value) => OnPropertyChanged(nameof(TemperatureAverageText));

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var projectsTask = api.GetProjectsListAsync();
            var dashboardTask = _cache.GetOrFetchAsync(
                CacheKeys.NeaDashboard,
                ct => api.GetNeaDashboardAsync(ct),
                CacheTtl.DashboardStats);
            // Three lightweight requests (limit: 1) — we only need the server-side
            // Total from the pagination header, not the actual message payload.
            var mmTotalTask      = _cache.GetOrFetchAsync(
                CacheKeys.MmList,
                ct => api.GetMmListAsync(limit: 1, ct: ct),
                CacheTtl.MmList);
            var mmOpenTask       = _cache.GetOrFetchAsync(
                CacheKeys.MmListOpen,
                ct => api.GetMmListAsync(status: 0, limit: 1, ct: ct),
                CacheTtl.MmList);
            var mmInProgressTask = _cache.GetOrFetchAsync(
                CacheKeys.MmListInProgress,
                ct => api.GetMmListAsync(status: 1, limit: 1, ct: ct),
                CacheTtl.MmList);
            var keysTask = _cache.GetOrFetchAsync(
                CacheKeys.KeysInventory,
                ct => api.GetKeysInventoryAsync(ct),
                CacheTtl.KeysInventory);
            await Task.WhenAll(projectsTask, dashboardTask, mmTotalTask, mmOpenTask, mmInProgressTask, keysTask);

            Projects.Clear();
            if (projectsTask.Result.Success && projectsTask.Result.Projects != null)
                foreach (var p in projectsTask.Result.Projects)
                    Projects.Add(p);

            if (dashboardTask.Result?.Success == true)
            {
                var d = dashboardTask.Result.Dashboard;
                NeaTotalSystems = d?.TotalSystems ?? dashboardTask.Result.Stats?.TotalSystems ?? 0;
                var overdueItems = d?.OverdueItems ?? dashboardTask.Result.DueTests;
                NeaOverdueInspections = d?.OverdueInspections ?? overdueItems?.Count ?? 0;
                OverdueItems.Clear();
                if (overdueItems != null)
                    foreach (var item in overdueItems)
                        OverdueItems.Add(item);
                HasOverdueItems = OverdueItems.Count > 0;
                RecentInspections.Clear();
                var recentInspections = d?.RecentInspections ?? dashboardTask.Result.RecentInspections;
                if (recentInspections != null)
                    foreach (var item in recentInspections)
                        RecentInspections.Add(item);
            }
            else
            {
                ClearNeaDashboardData();
            }

            if (mmTotalTask.Result?.Success == true)
            {
                MmTotal      = mmTotalTask.Result.Total ?? 0;
                MmOpen       = mmOpenTask.Result?.Success == true ? mmOpenTask.Result.Total ?? 0 : 0;
                MmInProgress = mmInProgressTask.Result?.Success == true ? mmInProgressTask.Result.Total ?? 0 : 0;
            }

            if (keysTask.Result?.Success == true)
            {
                var keys = keysTask.Result.Keys;
                KeysAvailable = keys == null ? 0 : keys.Sum(k => k.Available ?? 0);
            }

            _backgroundRefreshService.NotifyUserActivity(CacheKeys.NeaDashboard);
            _backgroundRefreshService.NotifyUserActivity(CacheKeys.MmList);
            _backgroundRefreshService.NotifyUserActivity(CacheKeys.MmListOpen);
            _backgroundRefreshService.NotifyUserActivity(CacheKeys.MmListInProgress);
            _backgroundRefreshService.NotifyUserActivity(CacheKeys.KeysInventory);
        }
        catch (Exception ex)
        {
            ClearNeaDashboardData();
            MmTotal = 0;
            KeysAvailable = 0;
            ErrorMessage = $"Error loading dashboard: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }

        // Eigener try/catch: die neueren Web-Dashboard-Karten (Sammelstörung, ausgegebene
        // Schlüssel, Freigaben, überfällige MM, WLS-Fälligkeiten, Durchschnittstemperatur)
        // kommen über die Protobuf-Aktion DASHBOARD_WIDGETS statt über den REST-Pfad oben —
        // ein Fehler hier darf die bereits erfolgreich geladenen REST-Kacheln nicht verwerfen.
        await LoadWidgetsAsync();
    }

    private async Task LoadWidgetsAsync()
    {
        try
        {
            var protobufApi = new DkcProtobufApi(_apiFactory.CreateProtobuf(_authService.CurrentToken));
            var widgets = await protobufApi.GetDashboardWidgetsAsync();

            SammelstoerungPermission = widgets.SammelstoerungPermission;
            SammelstoerungCount = widgets.SammelstoerungCount;
            Sammelstoerung.Clear();
            foreach (var a in widgets.Sammelstoerung)
                Sammelstoerung.Add(new SammelstoerungAlarmDisplay(a.Source, a.SourceLabel, a.DeviceName, a.AlarmText, a.Timestamp, a.Url));

            IssuedKeysPermission = widgets.IssuedKeysPermission;
            IssuedKeysCount = widgets.IssuedKeysCount;
            IssuedKeysList.Clear();
            foreach (var k in widgets.IssuedKeys)
                IssuedKeysList.Add(new IssuedKeyDisplay(k.Id, k.EmpfaengerName, k.AusgegebenAm, k.RueckgabeGeplant, k.Type, k.Name));

            ApprovalsPermission = widgets.ApprovalsPermission;
            ApprovalsCount = widgets.ApprovalsCount;
            Approvals.Clear();
            foreach (var m in widgets.Approvals)
                Approvals.Add(new MmApprovalDisplay(m.Id, m.Uid, m.NachunternehmerName, m.Address, m.Title, m.CreatedAt, m.DaysWaiting));

            OverdueComplaintsPermission = widgets.OverduePermission;
            OverdueComplaintsCount = widgets.OverdueCount;
            OverdueComplaints.Clear();
            foreach (var o in widgets.OverdueComplaints)
                OverdueComplaints.Add(new OverdueComplaintDisplay(o.Id, o.Uid, o.NachunternehmerName, o.Address, o.Title, o.CreatedAt, o.DaysOpen, o.Status, o.StatusLabel));

            WlsPermission = widgets.WlsPermission;
            WlsCount = widgets.WlsCount;
            WlsOverdueCount = widgets.WlsOverdueCount;
            WlsFlushings.Clear();
            foreach (var w in widgets.WlsFlushings)
                WlsFlushings.Add(new WlsFlushDisplay(w.Id, w.Label, w.BuildingName, w.LastFlushDate, w.NextFlushDue, w.DaysOverdue, w.NeverFlushed));

            TemperaturePermission = widgets.TemperaturePermission;
            TemperatureAverage = widgets.TemperatureAverage;
            TemperatureActiveDevices = widgets.TemperatureActiveDevices;
            TemperatureTotalDevices = widgets.TemperatureTotalDevices;
            TemperatureOnlineDevices = widgets.TemperatureOnlineDevices;
            UpdateTemperatureChart(widgets.TemperatureHistory);
        }
        catch (Exception ex)
        {
            // Nicht fatal: die klassischen Dashboard-Kacheln (REST, oben) bleiben nutzbar,
            // auch wenn der Protobuf-Endpunkt (noch) nicht erreichbar ist.
            ErrorMessage ??= $"Dashboard-Widgets konnten nicht geladen werden: {ex.Message}";
        }
    }

    /// <summary>
    /// Baut die 14-Tage-Temperaturverlauf-Balken aus dem Protobuf-Payload. Tage ohne
    /// Messwert (has_value=false, Web-Pendant: `null` in ContDashboard::getTemperatureHistory())
    /// werden als Lücke (kein Balken) statt als 0 dargestellt.
    /// </summary>
    private void UpdateTemperatureChart(IEnumerable<TemperatureHistoryPoint> history)
    {
        var points = history.ToList();

        TemperatureSeries = new ISeries[]
        {
            new ColumnSeries<double?>
            {
                Name = "Ø Temperatur",
                Values = points.Select(p => p.HasValue ? (double?)p.Value : null).ToArray(),
                Fill = new SolidColorPaint(SKColor.Parse("#3B82F6")),
                MaxBarWidth = 24,
            },
        };
        TemperatureXAxes = new[]
        {
            new Axis { Labels = points.Select(p => p.Label).ToArray(), LabelsRotation = 0 },
        };
    }

    [RelayCommand]
    private void CreateMm()
    {
        CreateMmRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void StartNeaInspection()
    {
        StartNeaInspectionRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanSetProject))]
    public async Task SetActiveProjectAsync()
    {
        if (SelectedProject == null) return;
        IsSettingProject = true;
        ErrorMessage     = null;
        try
        {
            var api    = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.SetActiveProjectAsync(new ProjectSetActiveRequest(SelectedProject.Id));
            if (!result.Success)
                ErrorMessage = result.Error ?? "Projekt konnte nicht gesetzt werden.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Fehler beim Setzen des Projekts: {ex.Message}";
        }
        finally
        {
            IsSettingProject = false;
        }
    }

    private bool CanSetProject() => SelectedProject != null && !IsSettingProject;

    partial void OnSelectedProjectChanged(Project? value) =>
        SetActiveProjectCommand.NotifyCanExecuteChanged();

    partial void OnIsSettingProjectChanged(bool value) =>
        SetActiveProjectCommand.NotifyCanExecuteChanged();

    private void OnDataRefreshed(object? sender, string key)
    {
        if (key != CacheKeys.NeaDashboard && key != CacheKeys.MmList && key != CacheKeys.KeysInventory)
            return;

        _ = Dispatcher.UIThread.InvokeAsync(RefreshFromBackgroundAsync);
    }

    private async Task RefreshFromBackgroundAsync()
    {
        if (IsLoading)
            return;

        IsRefreshing = true;
        try
        {
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error refreshing dashboard: {ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void ClearNeaDashboardData()
    {
        NeaTotalSystems = 0;
        NeaOverdueInspections = 0;
        OverdueItems.Clear();
        RecentInspections.Clear();
        HasOverdueItems = false;
    }
}
