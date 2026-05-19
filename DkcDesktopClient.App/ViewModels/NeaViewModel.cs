using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.App.Services;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Services;

namespace DkcDesktopClient.App.ViewModels;

/// <summary>An editable checklist item within a NEA inspection.</summary>
public partial class NeaChecklistEditItem : ObservableObject
{
    public int CheckpointId { get; init; }
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _status = "ok";
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private string _comment = string.Empty;

    public static IReadOnlyList<string> StatusOptions { get; } =
        new[] { "ok", "nok", "n_a" };
}

public partial class NeaViewModel : ViewModelBase
{
    private readonly DkcApiFactory _apiFactory;
    private readonly AuthService _authService;
    private readonly IFilePickerService _filePicker;

    // List state
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

    // System form state
    [ObservableProperty] private bool _isSystemFormVisible;
    [ObservableProperty] private bool _isSavingSystem;
    [ObservableProperty] private string? _systemFormError;
    [ObservableProperty] private bool _isEditingSystem;
    private int? _editingSystemId;

    // System form fields
    [ObservableProperty] private string _formSystemName = string.Empty;
    [ObservableProperty] private string _formSystemDescription = string.Empty;
    [ObservableProperty] private string _formSystemLocation = string.Empty;
    [ObservableProperty] private string _formSystemManufacturer = string.Empty;
    [ObservableProperty] private string _formSystemModel = string.Empty;
    [ObservableProperty] private string _formSystemSerialNumber = string.Empty;
    [ObservableProperty] private string _formSystemInstallationDate = string.Empty;
    [ObservableProperty] private bool _formSystemEnabled = true;
    [ObservableProperty] private double? _formSystemRatedPower;
    [ObservableProperty] private string _formSystemFuelType = "Diesel";

    // Inspection form state
    [ObservableProperty] private bool _isInspectionFormVisible;
    [ObservableProperty] private bool _isSavingInspection;
    [ObservableProperty] private string? _inspectionFormError;
    [ObservableProperty] private bool _isEditingInspection;
    private int? _editingInspectionId;

    // Inspection form fields
    [ObservableProperty] private string _formInspectionType = "annual";
    [ObservableProperty] private string _formInspectionDate = string.Empty;
    [ObservableProperty] private string _formInspectionStatus = "open";
    [ObservableProperty] private string _formInspectionResult = "ok";
    [ObservableProperty] private int? _formRuntimeHours;
    [ObservableProperty] private string _formInspectionNotes = string.Empty;
    [ObservableProperty] private string _formDefectsFound = string.Empty;
    [ObservableProperty] private string _formCorrectiveActions = string.Empty;

    // Checklist
    [ObservableProperty] private bool _isChecklistVisible;
    [ObservableProperty] private bool _isSavingChecklist;
    [ObservableProperty] private string? _checklistError;
    [ObservableProperty] private ObservableCollection<NeaChecklistEditItem> _checklistItems = new();

    public static IReadOnlyList<string> InspectionTypeOptions { get; } =
        new[] { "annual", "monthly", "quarterly", "ad_hoc" };
    public static IReadOnlyList<string> StatusOptions { get; } =
        new[] { "open", "in_progress", "completed" };
    public static IReadOnlyList<string> ResultOptions { get; } =
        new[] { "ok", "defects_found", "failed" };
    public static IReadOnlyList<string> FuelTypeOptions { get; } =
        new[] { "Diesel", "Gas", "Hybrid", "Benzin", "Erdgas" };

    public NeaViewModel(DkcApiFactory apiFactory, AuthService authService, IFilePickerService filePicker)
    {
        _apiFactory = apiFactory;
        _authService = authService;
        _filePicker = filePicker;
        // Wire CollectionChanged on the initial collection instance;
        // OnInspectionsChanged handles re-subscription if the collection property is replaced.
        Inspections.CollectionChanged += OnInspectionsCollectionChanged;
    }

    [RelayCommand]
    public async Task LoadSystemsAsync()
    {
        var ct = StartLoad();
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.GetNeaSystemsAsync(ct: ct);
            Systems.Clear();
            if (result.Success && result.Systems != null)
                foreach (var s in result.Systems)
                    Systems.Add(s);
        }
        catch (OperationCanceledException)
        {
            // Navigation away – discard silently.
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
        var ct = StartLoad();
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
                offset: 0,
                ct: ct);
            Inspections.Clear();
            TotalInspections = result.Total ?? 0;
            if (result.Success && result.Inspections != null)
                foreach (var i in result.Inspections)
                    Inspections.Add(i);
        }
        catch (OperationCanceledException)
        {
            // Navigation away – discard silently.
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

    // — System CRUD —
    [RelayCommand]
    public void ShowCreateSystemForm()
    {
        IsEditingSystem = false;
        _editingSystemId = null;
        FormSystemName = string.Empty;
        FormSystemDescription = string.Empty;
        FormSystemLocation = string.Empty;
        FormSystemManufacturer = string.Empty;
        FormSystemModel = string.Empty;
        FormSystemSerialNumber = string.Empty;
        FormSystemInstallationDate = string.Empty;
        FormSystemEnabled = true;
        FormSystemRatedPower = null;
        FormSystemFuelType = "Diesel";
        SystemFormError = null;
        IsSystemFormVisible = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSystem))]
    public void ShowEditSystemForm()
    {
        if (SelectedSystem == null) return;
        IsEditingSystem = true;
        _editingSystemId = SelectedSystem.Id;
        FormSystemName = SelectedSystem.Name;
        FormSystemDescription = SelectedSystem.Description ?? string.Empty;
        FormSystemLocation = SelectedSystem.Location ?? string.Empty;
        FormSystemManufacturer = SelectedSystem.Manufacturer ?? string.Empty;
        FormSystemModel = SelectedSystem.Model ?? string.Empty;
        FormSystemSerialNumber = SelectedSystem.SerialNumber ?? string.Empty;
        FormSystemInstallationDate = SelectedSystem.InstallationDate ?? string.Empty;
        FormSystemEnabled = SelectedSystem.Enabled;
        FormSystemRatedPower = SelectedSystem.RatedPower;
        FormSystemFuelType = SelectedSystem.FuelType ?? "Diesel";
        SystemFormError = null;
        IsSystemFormVisible = true;
    }

    [RelayCommand]
    public void CancelSystemForm()
    {
        IsSystemFormVisible = false;
        SystemFormError = null;
    }

    [RelayCommand(CanExecute = nameof(CanSaveSystem))]
    public async Task SaveSystemAsync()
    {
        if (string.IsNullOrWhiteSpace(FormSystemName))
        {
            SystemFormError = "Name is required.";
            return;
        }
        IsSavingSystem = true;
        SystemFormError = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var req = new NeaSystemSaveRequest(
                FormSystemName,
                Nz(FormSystemDescription),
                Nz(FormSystemLocation),
                Nz(FormSystemManufacturer),
                Nz(FormSystemModel),
                Nz(FormSystemSerialNumber),
                Nz(FormSystemInstallationDate),
                FormSystemEnabled,
                null,
                FormSystemRatedPower,
                Nz(FormSystemFuelType));

            ApiError result;
            if (IsEditingSystem && _editingSystemId.HasValue)
            {
                result = await api.UpdateNeaSystemAsync(_editingSystemId.Value, req);
            }
            else
            {
                var cr = await api.CreateNeaSystemAsync(req);
                result = new ApiError(cr.Success, cr.Error);
            }

            if (result.Success)
            {
                IsSystemFormVisible = false;
                await LoadSystemsAsync();
            }
            else
            {
                SystemFormError = result.Error ?? "Save failed.";
            }
        }
        catch (Exception ex)
        {
            SystemFormError = $"Error: {ex.Message}";
        }
        finally
        {
            IsSavingSystem = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSystem))]
    public async Task DeleteSystemAsync()
    {
        if (SelectedSystem == null) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.DeleteNeaSystemAsync(SelectedSystem.Id);
            if (result.Success)
            {
                Systems.Remove(SelectedSystem);
                Inspections.Clear();
            }
            else
            {
                ErrorMessage = result.Error ?? "Delete failed.";
            }
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

    // — Inspection CRUD —
    [RelayCommand(CanExecute = nameof(HasSelectedSystem))]
    public void ShowCreateInspectionForm()
    {
        if (SelectedSystem == null) return;
        IsEditingInspection = false;
        _editingInspectionId = null;
        FormInspectionType = "annual";
        FormInspectionDate = DateTime.Today.ToString("yyyy-MM-dd");
        FormInspectionStatus = "open";
        FormInspectionResult = "ok";
        FormRuntimeHours = null;
        FormInspectionNotes = string.Empty;
        FormDefectsFound = string.Empty;
        FormCorrectiveActions = string.Empty;
        InspectionFormError = null;
        IsInspectionFormVisible = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedInspection))]
    public void ShowEditInspectionForm()
    {
        if (SelectedInspection == null) return;
        IsEditingInspection = true;
        _editingInspectionId = SelectedInspection.Id;
        FormInspectionType = SelectedInspection.InspectionType;
        FormInspectionDate = SelectedInspection.InspectionDate;
        FormInspectionStatus = SelectedInspection.Status;
        FormInspectionResult = SelectedInspection.OverallResult;
        FormRuntimeHours = SelectedInspection.RuntimeHours;
        FormInspectionNotes = SelectedInspection.Notes ?? string.Empty;
        FormDefectsFound = string.Empty;
        FormCorrectiveActions = string.Empty;
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
        if (SelectedSystem == null) return;
        if (string.IsNullOrWhiteSpace(FormInspectionDate))
        {
            InspectionFormError = "Inspection date is required.";
            return;
        }
        IsSavingInspection = true;
        InspectionFormError = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var req = new NeaInspectionSaveRequest(
                SelectedSystem.Id,
                FormInspectionType,
                FormInspectionDate,
                FormInspectionStatus,
                FormInspectionResult,
                FormRuntimeHours,
                Nz(FormInspectionNotes),
                Nz(FormDefectsFound),
                Nz(FormCorrectiveActions));

            ApiError result;
            if (IsEditingInspection && _editingInspectionId.HasValue)
            {
                result = await api.UpdateNeaInspectionAsync(_editingInspectionId.Value, req);
            }
            else
            {
                var cr = await api.CreateNeaInspectionAsync(req);
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
            var result = await api.CompleteNeaInspectionAsync(SelectedInspection.Id,
                new NeaInspectionCompleteRequest(FormInspectionResult, Nz(FormInspectionNotes)));
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

    private bool CanSaveSystem() => !IsSavingSystem;
    private bool CanSaveInspection() => !IsSavingInspection;
    private bool HasSelectedSystem() => SelectedSystem != null;
    private bool HasSelectedInspection() => SelectedInspection != null;
    private bool CanSaveChecklist() => !IsSavingChecklist;

    private static string? Nz(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    // ── Checklist commands ────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(HasSelectedInspection))]
    public void ShowChecklist()
    {
        BuildChecklistItems();
        IsChecklistVisible = true;
    }

    [RelayCommand]
    public void HideChecklist() => IsChecklistVisible = false;

    [RelayCommand(CanExecute = nameof(CanSaveChecklist))]
    public async Task SaveChecklistAsync()
    {
        if (SelectedInspection == null || ChecklistItems.Count == 0) return;
        IsSavingChecklist = true;
        ChecklistError    = null;
        try
        {
            var api   = _apiFactory.Create(_authService.CurrentToken);
            var items = ChecklistItems
                .Select(ci => new NeaChecklistUpdateItem(ci.CheckpointId, ci.Status, Nz(ci.Note), Nz(ci.Comment)))
                .ToList();
            var result = await api.UpdateNeaChecklistAsync(SelectedInspection.Id, new NeaChecklistUpdateRequest(items));
            if (!result.Success)
                ChecklistError = result.Error ?? "Checkliste speichern fehlgeschlagen.";
        }
        catch (Exception ex)
        {
            ChecklistError = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsSavingChecklist = false;
        }
    }

    private void BuildChecklistItems()
    {
        ChecklistItems.Clear();
        if (InspectionDetail?.ChecklistData == null) return;
        try
        {
            var json = JsonSerializer.Serialize(InspectionDetail.ChecklistData);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray())
                {
                    var item = new NeaChecklistEditItem
                    {
                        CheckpointId = el.TryGetProperty("checkpoint_id", out var cpid) ? cpid.GetInt32() : 0,
                        Name         = el.TryGetProperty("name",          out var n)    ? n.GetString() ?? string.Empty : string.Empty,
                        Status       = el.TryGetProperty("status",        out var s)    ? s.GetString() ?? "ok"         : "ok",
                        Note         = el.TryGetProperty("note",          out var nt)   ? nt.GetString() ?? string.Empty : string.Empty,
                        Comment      = el.TryGetProperty("comment",       out var c)    ? c.GetString() ?? string.Empty : string.Empty,
                    };
                    ChecklistItems.Add(item);
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                int i = 0;
                foreach (var prop in root.EnumerateObject())
                {
                    int checkpointId = int.TryParse(prop.Name, out var pid) ? pid : ++i;
                    var valKind = prop.Value.ValueKind;
                    var item = new NeaChecklistEditItem
                    {
                        CheckpointId = checkpointId,
                        Name         = prop.Name,
                        Status       = valKind == JsonValueKind.Object && prop.Value.TryGetProperty("status", out var s) ? s.GetString() ?? "ok" : "ok",
                        Note         = valKind == JsonValueKind.Object && prop.Value.TryGetProperty("note",   out var nt) ? nt.GetString() ?? string.Empty : string.Empty,
                        Comment      = valKind == JsonValueKind.Object && prop.Value.TryGetProperty("comment", out var c) ? c.GetString() ?? string.Empty : string.Empty,
                    };
                    ChecklistItems.Add(item);
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // If JSON parsing fails, start with an empty checklist — this is expected if the backend
            // returns a format we don't recognize or an empty object.
        }
    }

    partial void OnSelectedSystemChanged(NeaSystem? value)
    {
        if (value != null) _ = LoadInspectionsAsync();
        ShowEditSystemFormCommand.NotifyCanExecuteChanged();
        DeleteSystemCommand.NotifyCanExecuteChanged();
        ShowCreateInspectionFormCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedInspectionChanged(NeaInspection? value)
    {
        if (value != null) _ = LoadInspectionDetailAsync();
        ShowEditInspectionFormCommand.NotifyCanExecuteChanged();
        CompleteInspectionCommand.NotifyCanExecuteChanged();
        ShowChecklistCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSavingSystemChanged(bool value) => SaveSystemCommand.NotifyCanExecuteChanged();
    partial void OnIsSavingInspectionChanged(bool value) => SaveInspectionCommand.NotifyCanExecuteChanged();
    partial void OnIsSavingChecklistChanged(bool value) => SaveChecklistCommand.NotifyCanExecuteChanged();

    // ── CSV Export ────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanExportInspections))]
    public async Task ExportInspectionsToCsvAsync()
    {
        var path = await _filePicker.PickSaveFileAsync(
            $"nea_pruefungen_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            new[] { ("CSV-Datei", "*.csv") });
        if (path == null) return;

        var columns = new (string, Func<NeaInspection, string?>)[]
        {
            ("ID",              i => i.Id.ToString()),
            ("Anlage",          i => i.SystemName),
            ("Typ",             i => i.InspectionType),
            ("Datum",           i => i.InspectionDate),
            ("Prüfer",          i => i.InspectorName),
            ("Status",          i => i.Status),
            ("Ergebnis",        i => i.OverallResult),
            ("Laufzeit (h)",    i => i.RuntimeHours?.ToString()),
            ("Notizen",         i => i.Notes),
        };

        try
        {
            CsvExportService.ExportToCsv(path, Inspections, columns);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"CSV-Export fehlgeschlagen: {ex.Message}";
        }
    }

    private bool CanExportInspections() => Inspections.Count > 0;

    partial void OnInspectionsChanged(ObservableCollection<NeaInspection>? oldValue, ObservableCollection<NeaInspection> newValue)
    {
        // Re-subscribe to the new collection instance so CollectionChanged keeps notifying the command
        if (oldValue != null)
            oldValue.CollectionChanged -= OnInspectionsCollectionChanged;
        newValue.CollectionChanged += OnInspectionsCollectionChanged;
        ExportInspectionsToCsvCommand.NotifyCanExecuteChanged();
    }

    private void OnInspectionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => ExportInspectionsToCsvCommand.NotifyCanExecuteChanged();
}
