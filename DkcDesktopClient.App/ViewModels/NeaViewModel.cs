using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Services;

namespace DkcDesktopClient.App.ViewModels;

public partial class NeaViewModel : ViewModelBase
{
    private readonly DkcApiFactory _apiFactory;
    private readonly AuthService _authService;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private ObservableCollection<NeaSystem> _systems = new();
    [ObservableProperty] private NeaSystem? _selectedSystem;
    [ObservableProperty] private ObservableCollection<NeaInspection> _inspections = new();
    [ObservableProperty] private NeaInspection? _selectedInspection;
    [ObservableProperty] private NeaInspectionDetail? _inspectionDetail;
    [ObservableProperty] private int? _filterYear;
    [ObservableProperty] private string? _filterStatus;
    [ObservableProperty] private int _totalInspections;
    [ObservableProperty] private int _currentOffset;
    private const int PageSize = 50;

    public NeaViewModel(DkcApiFactory apiFactory, AuthService authService)
    {
        _apiFactory = apiFactory;
        _authService = authService;
    }

    [RelayCommand]
    public async Task LoadSystemsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.GetNeaSystemsAsync();
            Systems.Clear();
            if (result.Success && result.Systems != null)
                foreach (var s in result.Systems)
                    Systems.Add(s);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading NEA systems: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task LoadInspectionsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        CurrentOffset = 0;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.GetNeaInspectionsAsync(
                systemId: SelectedSystem?.Id,
                year: FilterYear,
                status: FilterStatus,
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
            var result = await api.GetNeaInspectionDetailAsync(SelectedInspection.Id);
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

    partial void OnSelectedSystemChanged(NeaSystem? value)
    {
        if (value != null)
            _ = LoadInspectionsAsync();
    }

    partial void OnSelectedInspectionChanged(NeaInspection? value)
    {
        if (value != null)
            _ = LoadInspectionDetailAsync();
    }
}
