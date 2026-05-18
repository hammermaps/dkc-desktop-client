using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.App.Services;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Services;

namespace DkcDesktopClient.App.ViewModels;

public partial class WlsViewModel : ViewModelBase
{
    private readonly DkcApiFactory _apiFactory;
    private readonly AuthService _authService;
    private readonly IFilePickerService _filePicker;

    // ── Tab state ─────────────────────────────────────────────────────────────
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    // ── Buildings ─────────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<WlsBuilding> _buildings = new();
    [ObservableProperty] private WlsBuilding? _selectedBuilding;

    [ObservableProperty] private bool _isBuildingFormVisible;
    [ObservableProperty] private bool _isSavingBuilding;
    [ObservableProperty] private string? _buildingFormError;
    [ObservableProperty] private bool _isEditingBuilding;
    private int? _editingBuildingId;

    [ObservableProperty] private string _formBuildingName = string.Empty;
    [ObservableProperty] private bool _formBuildingHidden;
    [ObservableProperty] private int _formBuildingSorted;

    // ── Apartments ────────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<WlsApartment> _apartments = new();
    [ObservableProperty] private WlsApartment? _selectedApartment;

    [ObservableProperty] private bool _isApartmentFormVisible;
    [ObservableProperty] private bool _isSavingApartment;
    [ObservableProperty] private string? _apartmentFormError;
    [ObservableProperty] private bool _isEditingApartment;
    private int? _editingApartmentId;

    [ObservableProperty] private string _formApartmentValue = string.Empty;
    [ObservableProperty] private string _formApartmentName = string.Empty;
    [ObservableProperty] private int _formApartmentSorted;
    [ObservableProperty] private bool _formApartmentEmpty = true;

    // ── Records ───────────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<WlsRecord> _records = new();
    [ObservableProperty] private WlsRecord? _selectedRecord;

    [ObservableProperty] private bool _isRecordFormVisible;
    [ObservableProperty] private bool _isSavingRecord;
    [ObservableProperty] private string? _recordFormError;
    [ObservableProperty] private bool _isEditingRecord;
    private int? _editingRecordId;

    [ObservableProperty] private string _formRecordStartTime = string.Empty;
    [ObservableProperty] private string _formRecordEndTime = string.Empty;
    [ObservableProperty] private string _formRecordLatitude = string.Empty;
    [ObservableProperty] private string _formRecordLongitude = string.Empty;

    // Filter for records
    [ObservableProperty] private string _filterStartDate = string.Empty;
    [ObservableProperty] private string _filterEndDate = string.Empty;

    public WlsViewModel(DkcApiFactory apiFactory, AuthService authService, IFilePickerService filePicker)
    {
        _apiFactory  = apiFactory;
        _authService = authService;
        _filePicker  = filePicker;
    }

    // ══════════════════════════════  Buildings  ════════════════════════════════

    [RelayCommand]
    public async Task LoadBuildingsAsync()
    {
        IsLoading    = true;
        ErrorMessage = null;
        try
        {
            var api    = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.GetWlsBuildingsAsync();
            Buildings.Clear();
            if (result.Success && result.Data != null)
                foreach (var b in result.Data)
                    Buildings.Add(b);
            else if (!result.Success)
                ErrorMessage = result.Error ?? "Laden der Gebäude fehlgeschlagen.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void ShowCreateBuildingForm()
    {
        IsEditingBuilding   = false;
        _editingBuildingId  = null;
        FormBuildingName    = string.Empty;
        FormBuildingHidden  = false;
        FormBuildingSorted  = 0;
        BuildingFormError   = null;
        IsBuildingFormVisible = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedBuilding))]
    public void ShowEditBuildingForm()
    {
        if (SelectedBuilding == null) return;
        IsEditingBuilding   = true;
        _editingBuildingId  = SelectedBuilding.Id;
        FormBuildingName    = SelectedBuilding.Name;
        FormBuildingHidden  = SelectedBuilding.Hidden;
        FormBuildingSorted  = SelectedBuilding.Sorted;
        BuildingFormError   = null;
        IsBuildingFormVisible = true;
    }

    [RelayCommand]
    public void CancelBuildingForm()
    {
        IsBuildingFormVisible = false;
        BuildingFormError     = null;
    }

    [RelayCommand(CanExecute = nameof(CanSaveBuilding))]
    public async Task SaveBuildingAsync()
    {
        if (string.IsNullOrWhiteSpace(FormBuildingName))
        {
            BuildingFormError = "Name ist ein Pflichtfeld.";
            return;
        }
        IsSavingBuilding = true;
        BuildingFormError = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var req = new WlsBuildingSaveRequest(FormBuildingName, FormBuildingHidden, FormBuildingSorted);

            bool success;
            string? error;
            if (IsEditingBuilding && _editingBuildingId.HasValue)
            {
                var r = await api.UpdateWlsBuildingAsync(_editingBuildingId.Value, req);
                success = r.Success;
                error   = r.Error;
            }
            else
            {
                var r = await api.CreateWlsBuildingAsync(req);
                success = r.Success;
                error   = r.Error;
            }

            if (success)
            {
                IsBuildingFormVisible = false;
                await LoadBuildingsAsync();
            }
            else
            {
                BuildingFormError = error ?? "Speichern fehlgeschlagen.";
            }
        }
        catch (Exception ex)
        {
            BuildingFormError = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsSavingBuilding = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedBuilding))]
    public async Task DeleteBuildingAsync()
    {
        if (SelectedBuilding == null) return;
        IsLoading    = true;
        ErrorMessage = null;
        try
        {
            var api    = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.DeleteWlsBuildingAsync(SelectedBuilding.Id);
            if (result.Success)
                Buildings.Remove(SelectedBuilding);
            else
                ErrorMessage = result.Error ?? "Löschen fehlgeschlagen.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ══════════════════════════════  Apartments  ══════════════════════════════

    [RelayCommand]
    public async Task LoadApartmentsAsync()
    {
        IsLoading    = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            WlsApartmentListResponse result;
            if (SelectedBuilding != null)
                result = await api.GetWlsApartmentsByBuildingAsync(SelectedBuilding.Id);
            else
                result = await api.GetWlsApartmentsAsync();

            Apartments.Clear();
            if (result.Success && result.Data != null)
                foreach (var a in result.Data)
                    Apartments.Add(a);
            else if (!result.Success)
                ErrorMessage = result.Error ?? "Laden der Wohnungen fehlgeschlagen.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedBuilding))]
    public void ShowCreateApartmentForm()
    {
        if (SelectedBuilding == null) return;
        IsEditingApartment   = false;
        _editingApartmentId  = null;
        FormApartmentValue   = string.Empty;
        FormApartmentName    = string.Empty;
        FormApartmentSorted  = 0;
        FormApartmentEmpty   = true;
        ApartmentFormError   = null;
        IsApartmentFormVisible = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedApartment))]
    public void ShowEditApartmentForm()
    {
        if (SelectedApartment == null) return;
        IsEditingApartment   = true;
        _editingApartmentId  = SelectedApartment.Id;
        FormApartmentValue   = SelectedApartment.Value;
        FormApartmentName    = SelectedApartment.Name ?? string.Empty;
        FormApartmentSorted  = SelectedApartment.Sorted;
        FormApartmentEmpty   = SelectedApartment.Empty;
        ApartmentFormError   = null;
        IsApartmentFormVisible = true;
    }

    [RelayCommand]
    public void CancelApartmentForm()
    {
        IsApartmentFormVisible = false;
        ApartmentFormError     = null;
    }

    [RelayCommand(CanExecute = nameof(CanSaveApartment))]
    public async Task SaveApartmentAsync()
    {
        if (string.IsNullOrWhiteSpace(FormApartmentValue))
        {
            ApartmentFormError = "Wohnungsnummer ist ein Pflichtfeld.";
            return;
        }
        IsSavingApartment = true;
        ApartmentFormError = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);

            bool success;
            string? error;
            if (IsEditingApartment && _editingApartmentId.HasValue && SelectedBuilding != null)
            {
                var req = new WlsApartmentUpdateRequest(
                    FormApartmentValue,
                    Nz(FormApartmentName),
                    FormApartmentSorted,
                    FormApartmentEmpty,
                    SelectedBuilding.Id);
                var r = await api.UpdateWlsApartmentAsync(_editingApartmentId.Value, req);
                success = r.Success;
                error   = r.Error;
            }
            else
            {
                if (SelectedBuilding == null)
                {
                    ApartmentFormError = "Bitte zuerst ein Gebäude auswählen.";
                    return;
                }
                var req = new WlsApartmentCreateRequest(
                    SelectedBuilding.Id,
                    FormApartmentValue,
                    Nz(FormApartmentName),
                    FormApartmentSorted);
                var r = await api.CreateWlsApartmentAsync(req);
                success = r.Success;
                error   = r.Error;
            }

            if (success)
            {
                IsApartmentFormVisible = false;
                await LoadApartmentsAsync();
            }
            else
            {
                ApartmentFormError = error ?? "Speichern fehlgeschlagen.";
            }
        }
        catch (Exception ex)
        {
            ApartmentFormError = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsSavingApartment = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedApartment))]
    public async Task DeleteApartmentAsync()
    {
        if (SelectedApartment == null) return;
        IsLoading    = true;
        ErrorMessage = null;
        try
        {
            var api    = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.DeleteWlsApartmentAsync(SelectedApartment.Id);
            if (result.Success)
                Apartments.Remove(SelectedApartment);
            else
                ErrorMessage = result.Error ?? "Löschen fehlgeschlagen.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ══════════════════════════════  Records  ═════════════════════════════════

    [RelayCommand]
    public async Task LoadRecordsAsync()
    {
        IsLoading    = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var req = new WlsRecordListRequest(
                ApartmentId: SelectedApartment?.Id,
                BuildingId:  SelectedBuilding?.Id,
                UserId:      null,
                StartDate:   Nz(FilterStartDate),
                EndDate:     Nz(FilterEndDate),
                OrderBy:     "start_time",
                Order:       "DESC",
                Limit:       100,
                Offset:      0);
            var result = await api.GetWlsRecordsAsync(req);
            Records.Clear();
            if (result.Success && result.Data != null)
                foreach (var r in result.Data)
                    Records.Add(r);
            else if (!result.Success)
                ErrorMessage = result.Error ?? "Laden der Erfassungen fehlgeschlagen.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void ShowCreateRecordForm()
    {
        IsEditingRecord      = false;
        _editingRecordId     = null;
        FormRecordStartTime  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        FormRecordEndTime    = string.Empty;
        FormRecordLatitude   = string.Empty;
        FormRecordLongitude  = string.Empty;
        RecordFormError      = null;
        IsRecordFormVisible  = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedRecord))]
    public void ShowEditRecordForm()
    {
        if (SelectedRecord == null) return;
        IsEditingRecord     = true;
        _editingRecordId    = SelectedRecord.Id;
        FormRecordStartTime = SelectedRecord.StartTime ?? string.Empty;
        FormRecordEndTime   = SelectedRecord.EndTime   ?? string.Empty;
        FormRecordLatitude  = SelectedRecord.Latitude?.ToString("G") ?? string.Empty;
        FormRecordLongitude = SelectedRecord.Longitude?.ToString("G") ?? string.Empty;
        RecordFormError     = null;
        IsRecordFormVisible = true;
    }

    [RelayCommand]
    public void CancelRecordForm()
    {
        IsRecordFormVisible = false;
        RecordFormError     = null;
    }

    [RelayCommand(CanExecute = nameof(CanSaveRecord))]
    public async Task SaveRecordAsync()
    {
        if (string.IsNullOrWhiteSpace(FormRecordStartTime))
        {
            RecordFormError = "Startzeit ist ein Pflichtfeld.";
            return;
        }
        IsSavingRecord = true;
        RecordFormError = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);

            double? lat = TryParseDouble(FormRecordLatitude);
            double? lon = TryParseDouble(FormRecordLongitude);

            bool success;
            string? error;
            if (IsEditingRecord && _editingRecordId.HasValue)
            {
                var req = new WlsRecordUpdateRequest(
                    FormRecordStartTime,
                    Nz(FormRecordEndTime),
                    lat, lon, null);
                var r = await api.UpdateWlsRecordAsync(_editingRecordId.Value, req);
                success = r.Success;
                error   = r.Error;
            }
            else
            {
                if (SelectedApartment == null || SelectedBuilding == null)
                {
                    RecordFormError = "Bitte Gebäude und Wohnung auswählen.";
                    return;
                }
                var req = new WlsRecordCreateRequest(
                    SelectedApartment.Id,
                    SelectedBuilding.Id,
                    FormRecordStartTime,
                    Nz(FormRecordEndTime),
                    null, lat, lon, null);
                var r = await api.CreateWlsRecordAsync(req);
                success = r.Success;
                error   = r.Error;
            }

            if (success)
            {
                IsRecordFormVisible = false;
                await LoadRecordsAsync();
            }
            else
            {
                RecordFormError = error ?? "Speichern fehlgeschlagen.";
            }
        }
        catch (Exception ex)
        {
            RecordFormError = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsSavingRecord = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedRecord))]
    public async Task DeleteRecordAsync()
    {
        if (SelectedRecord == null) return;
        IsLoading    = true;
        ErrorMessage = null;
        try
        {
            var api    = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.DeleteWlsRecordAsync(SelectedRecord.Id);
            if (result.Success)
                Records.Remove(SelectedRecord);
            else
                ErrorMessage = result.Error ?? "Löschen fehlgeschlagen.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool HasSelectedBuilding()  => SelectedBuilding  != null;
    private bool HasSelectedApartment() => SelectedApartment != null;
    private bool HasSelectedRecord()    => SelectedRecord    != null;
    private bool CanSaveBuilding()      => !IsSavingBuilding;
    private bool CanSaveApartment()     => !IsSavingApartment;
    private bool CanSaveRecord()        => !IsSavingRecord;

    private static string? Nz(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static double? TryParseDouble(string s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    // ── Property-change hooks ─────────────────────────────────────────────────

    partial void OnSelectedBuildingChanged(WlsBuilding? value)
    {
        if (value != null) _ = LoadApartmentsAsync();
        ShowEditBuildingFormCommand.NotifyCanExecuteChanged();
        DeleteBuildingCommand.NotifyCanExecuteChanged();
        ShowCreateApartmentFormCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedApartmentChanged(WlsApartment? value)
    {
        ShowEditApartmentFormCommand.NotifyCanExecuteChanged();
        DeleteApartmentCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedRecordChanged(WlsRecord? value)
    {
        ShowEditRecordFormCommand.NotifyCanExecuteChanged();
        DeleteRecordCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSavingBuildingChanged(bool value)  => SaveBuildingCommand.NotifyCanExecuteChanged();
    partial void OnIsSavingApartmentChanged(bool value) => SaveApartmentCommand.NotifyCanExecuteChanged();
    partial void OnIsSavingRecordChanged(bool value)    => SaveRecordCommand.NotifyCanExecuteChanged();

    // ── CSV Export ────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanExportRecords))]
    public async Task ExportRecordsToCsvAsync()
    {
        var path = await _filePicker.PickSaveFileAsync(
            $"wls_erfassungen_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            new[] { ("CSV-Datei", "*.csv") });
        if (path == null) return;

        var columns = new (string, Func<WlsRecord, string?>)[]
        {
            ("ID",           r => r.Id.ToString()),
            ("Gebäude-ID",   r => r.BuildingId.ToString()),
            ("Wohnungs-ID",  r => r.ApartmentId.ToString()),
            ("Benutzer",     r => r.UserName),
            ("Startzeit",    r => r.StartTime),
            ("Endzeit",      r => r.EndTime),
            ("Dauer",        r => r.DurationText),
            ("Latitude",     r => r.Latitude?.ToString("F6")),
            ("Longitude",    r => r.Longitude?.ToString("F6")),
        };

        try
        {
            CsvExportService.ExportToCsv(path, Records, columns);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"CSV-Export fehlgeschlagen: {ex.Message}";
        }
    }

    private bool CanExportRecords() => Records.Count > 0;

    partial void OnRecordsChanged(ObservableCollection<WlsRecord>? oldValue, ObservableCollection<WlsRecord> newValue)
        => ExportRecordsToCsvCommand.NotifyCanExecuteChanged();
}
