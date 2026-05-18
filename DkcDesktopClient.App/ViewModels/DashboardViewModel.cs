using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Services;

namespace DkcDesktopClient.App.ViewModels;

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

    public string MmTotalText => MmTotal.ToString("N0");
    public string MmOpenText => MmOpen.ToString("N0");
    public string KeysAvailableText => KeysAvailable.ToString("N0");
    public string NeaTotalSystemsText => NeaTotalSystems.ToString("N0");
    public string NeaOverdueInspectionsText => NeaOverdueInspections.ToString("N0");

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
