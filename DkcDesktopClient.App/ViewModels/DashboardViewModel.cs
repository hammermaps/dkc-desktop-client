using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Services;

namespace DkcDesktopClient.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly DkcApiFactory _apiFactory;
    private readonly AuthService _authService;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private ObservableCollection<Project> _projects = new();
    [ObservableProperty] private Project? _selectedProject;
    [ObservableProperty] private int _neaTotalSystems;
    [ObservableProperty] private int _neaOverdueInspections;
    [ObservableProperty] private ObservableCollection<NeaOverdueItem> _overdueItems = new();
    [ObservableProperty] private ObservableCollection<NeaRecentInspection> _recentInspections = new();

    public DashboardViewModel(DkcApiFactory apiFactory, AuthService authService)
    {
        _apiFactory = apiFactory;
        _authService = authService;
    }

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
            await Task.WhenAll(projectsTask, dashboardTask);

            Projects.Clear();
            if (projectsTask.Result.Success && projectsTask.Result.Projects != null)
                foreach (var p in projectsTask.Result.Projects)
                    Projects.Add(p);

            if (dashboardTask.Result.Success)
            {
                var d = dashboardTask.Result.Dashboard;
                NeaTotalSystems = d?.TotalSystems ?? dashboardTask.Result.Stats?.TotalSystems ?? 0;
                var overdueItems = d?.OverdueItems ?? dashboardTask.Result.DueTests;
                NeaOverdueInspections = d?.OverdueInspections ?? overdueItems?.Count ?? 0;
                OverdueItems.Clear();
                if (overdueItems != null)
                    foreach (var item in overdueItems)
                        OverdueItems.Add(item);
                RecentInspections.Clear();
                var recentInspections = d?.RecentInspections ?? dashboardTask.Result.RecentInspections;
                if (recentInspections != null)
                    foreach (var item in recentInspections)
                        RecentInspections.Add(item);
            }
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
}
