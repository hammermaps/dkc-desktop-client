using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.App.Services;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Services;

namespace DkcDesktopClient.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly DkcApiFactory _apiFactory;
    private readonly AuthService _authService;
    private readonly DataCacheService _cache;
    private readonly BackgroundRefreshService _backgroundRefreshService;
    private readonly INavigationService _navigationService;
    private readonly MmViewModel _mmViewModel;
    private readonly NeaViewModel _neaViewModel;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private ObservableCollection<Project> _projects = new();
    [ObservableProperty] private Project? _selectedProject;
    [ObservableProperty] private int _mmTotal;
    [ObservableProperty] private int _keysAvailable;
    [ObservableProperty] private int _neaTotalSystems;
    [ObservableProperty] private int _neaOverdueInspections;
    [ObservableProperty] private bool _hasOverdueItems;
    [ObservableProperty] private ObservableCollection<NeaOverdueItem> _overdueItems = new();
    [ObservableProperty] private ObservableCollection<NeaRecentInspection> _recentInspections = new();

    public string MmTotalText => MmTotal.ToString("N0");
    public string KeysAvailableText => KeysAvailable.ToString("N0");
    public string NeaTotalSystemsText => NeaTotalSystems.ToString("N0");
    public string NeaOverdueInspectionsText => NeaOverdueInspections.ToString("N0");

    public DashboardViewModel(
        DkcApiFactory apiFactory,
        AuthService authService,
        DataCacheService cache,
        BackgroundRefreshService backgroundRefreshService,
        INavigationService navigationService,
        MmViewModel mmViewModel,
        NeaViewModel neaViewModel)
    {
        _apiFactory = apiFactory;
        _authService = authService;
        _cache = cache;
        _backgroundRefreshService = backgroundRefreshService;
        _navigationService = navigationService;
        _mmViewModel = mmViewModel;
        _neaViewModel = neaViewModel;
        _backgroundRefreshService.DataRefreshed += OnDataRefreshed;
    }

    partial void OnMmTotalChanged(int value) => OnPropertyChanged(nameof(MmTotalText));
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
            var dashboardTask = api.GetNeaDashboardAsync();
            var mmTask = _cache.GetOrFetchAsync(
                CacheKeys.MmList,
                ct => api.GetMmListAsync(limit: 1, ct: ct),
                CacheTtl.MmList);
            var keysTask = _cache.GetOrFetchAsync(
                CacheKeys.KeysInventory,
                ct => api.GetKeysInventoryAsync(ct),
                CacheTtl.KeysInventory);
            await Task.WhenAll(projectsTask, dashboardTask, mmTask, keysTask);

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

            if (mmTask.Result?.Success == true)
                MmTotal = mmTask.Result.Total ?? mmTask.Result.Messages?.Count ?? 0;

            if (keysTask.Result?.Success == true)
            {
                var keys = keysTask.Result.Keys;
                KeysAvailable = keys == null ? 0 : keys.Sum(k => k.Available ?? 0);
            }

            _backgroundRefreshService.NotifyUserActivity(CacheKeys.DashboardData);
            _backgroundRefreshService.NotifyUserActivity(CacheKeys.MmList);
            _backgroundRefreshService.NotifyUserActivity(CacheKeys.KeysInventory);
        }
        catch (Exception ex)
        {
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
        _mmViewModel.ShowCreateForm();
        _navigationService.NavigateTo(_mmViewModel, "Maengelmeldungen");
    }

    [RelayCommand]
    private void StartNeaInspection()
    {
        _navigationService.NavigateTo(_neaViewModel, "NEA");
    }

    private void OnDataRefreshed(object? sender, string key)
    {
        if (key != CacheKeys.DashboardData && key != CacheKeys.MmList && key != CacheKeys.KeysInventory)
            return;

        Dispatcher.UIThread.Post(async () =>
        {
            if (IsLoading)
                return;

            IsRefreshing = true;
            try
            {
                await LoadDataAsync();
            }
            finally
            {
                IsRefreshing = false;
            }
        });
    }
}
