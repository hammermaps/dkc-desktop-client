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

    // List state
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

    // Building form state
    [ObservableProperty] private bool _isBuildingFormVisible;
    [ObservableProperty] private bool _isSavingBuilding;
    [ObservableProperty] private string? _buildingFormError;
    [ObservableProperty] private bool _isEditingBuilding;
    private int? _editingBuildingId;

    // Building form fields
    [ObservableProperty] private string _formBuildingName = string.Empty;
    [ObservableProperty] private string _formBuildingAddress = string.Empty;
    [ObservableProperty] private string _formBuildingDescription = string.Empty;
    [ObservableProperty] private bool _formBuildingEnabled = true;

    // Inspection form state
    [ObservableProperty] private bool _isInspectionFormVisible;
    [ObservableProperty] private bool _isSavingInspection;
    [ObservableProperty] private string? _inspectionFormError;
    [ObservableProperty] private bool _isEditingInspection;
    private int? _editingInspectionId;

    // Inspection form fields
    [ObservableProperty] private string _formInspectionTitle = string.Empty;
    [ObservableProperty] private string _formInspectionDate = string.Empty;
    [ObservableProperty] private string _formInspectionStatus = "open";
    [ObservableProperty] private string _formInspectionWeather = string.Empty;
    [ObservableProperty] private string _formInspectionAttendees = string.Empty;
    [ObservableProperty] private string _formInspectionNotes = string.Empty;

    // Complete-inspection quick fields
    [ObservableProperty] private string _formCompleteResult = "ok";
    [ObservableProperty] private string _formCompleteNotes = string.Empty;

    public static IReadOnlyList<string> InspectionStatusOptions { get; } =
        new[] { "open", "in_progress", "completed" };
    public static IReadOnlyList<string> ResultOptions { get; } =
        new[] { "ok", "defects_found", "failed" };

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

    // — Building CRUD —
    [RelayCommand]
    public void ShowCreateBuildingForm()
    {
        IsEditingBuilding = false;
        _editingBuildingId = null;
        FormBuildingName = string.Empty;
        FormBuildingAddress = string.Empty;
        FormBuildingDescription = string.Empty;
        FormBuildingEnabled = true;
        BuildingFormError = null;
        IsBuildingFormVisible = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedBuilding))]
    public void ShowEditBuildingForm()
    {
        if (SelectedBuilding == null) return;
        IsEditingBuilding = true;
        _editingBuildingId = SelectedBuilding.Id;
        FormBuildingName = SelectedBuilding.Name;
        FormBuildingAddress = SelectedBuilding.Address ?? string.Empty;
        FormBuildingDescription = SelectedBuilding.Description ?? string.Empty;
        FormBuildingEnabled = SelectedBuilding.Enabled;
        BuildingFormError = null;
        IsBuildingFormVisible = true;
    }

    [RelayCommand]
    public void CancelBuildingForm()
    {
        IsBuildingFormVisible = false;
        BuildingFormError = null;
    }

    [RelayCommand(CanExecute = nameof(CanSaveBuilding))]
    public async Task SaveBuildingAsync()
    {
        if (string.IsNullOrWhiteSpace(FormBuildingName))
        {
            BuildingFormError = "Name is required.";
            return;
        }
        IsSavingBuilding = true;
        BuildingFormError = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var req = new BuildingSaveRequest(
                FormBuildingName,
                Nz(FormBuildingAddress),
                Nz(FormBuildingDescription),
                FormBuildingEnabled,
                null);

            ApiError result;
            if (IsEditingBuilding && _editingBuildingId.HasValue)
            {
                result = await api.UpdateBuildingAsync(_editingBuildingId.Value, req);
            }
            else
            {
                var cr = await api.CreateBuildingAsync(req);
                result = new ApiError(cr.Success, cr.Error);
            }

            if (result.Success)
            {
                IsBuildingFormVisible = false;
                await LoadBuildingsAsync();
            }
            else
            {
                BuildingFormError = result.Error ?? "Save failed.";
            }
        }
        catch (Exception ex)
        {
            BuildingFormError = $"Error: {ex.Message}";
        }
        finally
        {
            IsSavingBuilding = false;
        }
    }

    // — Inspection CRUD —
    [RelayCommand(CanExecute = nameof(HasSelectedBuilding))]
    public void ShowCreateInspectionForm()
    {
        if (SelectedBuilding == null) return;
        IsEditingInspection = false;
        _editingInspectionId = null;
        FormInspectionTitle = string.Empty;
        FormInspectionDate = DateTime.Today.ToString("yyyy-MM-dd");
        FormInspectionStatus = "open";
        FormInspectionWeather = string.Empty;
        FormInspectionAttendees = string.Empty;
        FormInspectionNotes = string.Empty;
        InspectionFormError = null;
        IsInspectionFormVisible = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedInspection))]
    public void ShowEditInspectionForm()
    {
        if (SelectedInspection == null) return;
        IsEditingInspection = true;
        _editingInspectionId = SelectedInspection.Id;
        FormInspectionTitle = SelectedInspection.Title ?? string.Empty;
        FormInspectionDate = SelectedInspection.InspectionDate ?? string.Empty;
        FormInspectionStatus = SelectedInspection.Status ?? "open";
        FormInspectionWeather = SelectedInspection.Weather ?? string.Empty;
        FormInspectionAttendees = SelectedInspection.Attendees ?? string.Empty;
        FormInspectionNotes = string.Empty;
        InspectionFormError = null;
        IsInspectionFormVisible = true;
    }

    [RelayCommand]
    public void CancelInspectionForm()
    {
        IsInspectionFormVisible = false;
        InspectionFormError = null;
    }

    [RelayCommand(CanExecute = nameof(CanSaveInspection))]
    public async Task SaveInspectionAsync()
    {
        if (SelectedBuilding == null) return;
        IsSavingInspection = true;
        InspectionFormError = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var req = new BuildingInspectionSaveRequest(
                SelectedBuilding.Id,
                Nz(FormInspectionTitle),
                Nz(FormInspectionDate),
                Nz(FormInspectionStatus),
                Nz(FormInspectionWeather),
                Nz(FormInspectionAttendees),
                Nz(FormInspectionNotes));

            ApiError result;
            if (IsEditingInspection && _editingInspectionId.HasValue)
            {
                result = await api.UpdateBuildingInspectionAsync(_editingInspectionId.Value, req);
            }
            else
            {
                var cr = await api.CreateBuildingInspectionAsync(req);
                result = new ApiError(cr.Success, cr.Error);
            }

            if (result.Success)
            {
                IsInspectionFormVisible = false;
                await LoadInspectionsAsync();
            }
            else
            {
                InspectionFormError = result.Error ?? "Save failed.";
            }
        }
        catch (Exception ex)
        {
            InspectionFormError = $"Error: {ex.Message}";
        }
        finally
        {
            IsSavingInspection = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedInspection))]
    public async Task CompleteInspectionAsync()
    {
        if (SelectedInspection == null) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.CompleteBuildingInspectionAsync(SelectedInspection.Id,
                new BuildingInspectionCompleteRequest(FormCompleteResult, Nz(FormCompleteNotes)));
            if (result.Success)
                await LoadInspectionsAsync();
            else
                ErrorMessage = result.Error ?? "Complete failed.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasInspectionDetail))]
    public async Task SaveCheckpointAsync(CheckpointResult checkpoint)
    {
        if (InspectionDetail == null) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.UpdateBuildingCheckpointAsync(InspectionDetail.Id,
                new BuildingCheckpointUpdateRequest(
                    checkpoint.CheckpointId,
                    checkpoint.Status ?? "ok",
                    checkpoint.Note,
                    checkpoint.Comment));
            if (!result.Success)
                ErrorMessage = result.Error ?? "Checkpoint update failed.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanSaveBuilding() => !IsSavingBuilding;
    private bool CanSaveInspection() => !IsSavingInspection;
    private bool HasSelectedBuilding() => SelectedBuilding != null;
    private bool HasSelectedInspection() => SelectedInspection != null;
    private bool HasInspectionDetail() => InspectionDetail != null;

    private static string? Nz(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    partial void OnSelectedBuildingChanged(Building? value)
    {
        if (value != null) _ = LoadInspectionsAsync();
        ShowEditBuildingFormCommand.NotifyCanExecuteChanged();
        ShowCreateInspectionFormCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedInspectionChanged(BuildingInspection? value)
    {
        if (value != null) _ = LoadInspectionDetailAsync();
        ShowEditInspectionFormCommand.NotifyCanExecuteChanged();
        CompleteInspectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnInspectionDetailChanged(BuildingInspectionDetail? value) =>
        SaveCheckpointCommand.NotifyCanExecuteChanged();

    partial void OnIsSavingBuildingChanged(bool value) => SaveBuildingCommand.NotifyCanExecuteChanged();
    partial void OnIsSavingInspectionChanged(bool value) => SaveInspectionCommand.NotifyCanExecuteChanged();
}
