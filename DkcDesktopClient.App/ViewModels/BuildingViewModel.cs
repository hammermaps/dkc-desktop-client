using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Services;

namespace DkcDesktopClient.App.ViewModels;

public partial class BuildingViewModel : ViewModelBase
{
    private readonly DkcApiFactory _apiFactory;
    private readonly AuthService _authService;
    private const int PageSize = 50;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private ObservableCollection<Building> _buildings = new();
    [ObservableProperty] private Building? _selectedBuilding;
    [ObservableProperty] private ObservableCollection<BuildingInspection> _inspections = new();
    [ObservableProperty] private BuildingInspection? _selectedInspection;
    [ObservableProperty] private BuildingInspectionDetail? _inspectionDetail;
    [ObservableProperty] private string? _filterStatus;
    [ObservableProperty] private int? _filterYear;
    [ObservableProperty] private int _totalInspections;

    public BuildingViewModel(DkcApiFactory apiFactory, AuthService authService)
    {
        _apiFactory = apiFactory;
        _authService = authService;
    }

    [RelayCommand]
    public async Task LoadBuildingsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.GetBuildingListAsync();
            Buildings.Clear();
            if (result.Success && result.Buildings != null)
                foreach (var b in result.Buildings)
                    Buildings.Add(b);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading buildings: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task LoadInspectionsAsync()
    {
        if (SelectedBuilding == null) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.GetBuildingInspectionsAsync(
                buildingId: SelectedBuilding.Id,
                status: FilterStatus,
                year: FilterYear,
                limit: PageSize,
                offset: 0);
            Inspections.Clear();
            TotalInspections = result.Total ?? 0;
            if (result.Success && result.Inspections != null)
                foreach (var i in result.Inspections)
                    Inspections.Add(i);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading inspections: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task LoadInspectionDetailAsync()
    {
        if (SelectedInspection == null) return;
        IsLoading = true;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.GetBuildingInspectionDetailAsync(SelectedInspection.Id);
            if (result.Success)
                InspectionDetail = result.Inspection;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading inspection detail: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedBuildingChanged(Building? value)
    {
        if (value != null)
            _ = LoadInspectionsAsync();
    }

    partial void OnSelectedInspectionChanged(BuildingInspection? value)
    {
        if (value != null)
            _ = LoadInspectionDetailAsync();
    }
}
