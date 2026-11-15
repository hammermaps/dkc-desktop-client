using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.App.Services;
using DkcDesktopClient.Core.Protobuf;
using DkcDesktopClient.Core.Pruefung;
using DkcDesktopClient.Core.Services;
using static DkcDesktopClient.Core.I18n.Translator;

namespace DkcDesktopClient.App.ViewModels;

/// <summary>Haus für die Anlagen-Zuordnung (Server: MM_FORM_OPTIONS.streets bzw. aus vorhandenen Anlagen).</summary>
public sealed record ResultChoice(string Value, string LabelKey)
{
    public string Label => T(LabelKey);
    public override string ToString() => Label;
}

public sealed record HausOption(int Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Bearbeitbarer Prüfpunkt; Eingabe je nach Typ wie im Web-Wizard.</summary>
public partial class PruefungChecklistEditItem : ObservableObject
{
    public PruefungChecklistEditItem(PruefungChecklistStepData step)
    {
        Step = step;
        _value = step.Value;
        _note = step.DefectNote;
    }

    public PruefungChecklistStepData Step { get; }
    public string Title => Step.Required ? Step.Title + " *" : Step.Title;
    public string Category => Step.Category;
    public string Description => Step.Description;

    /// <summary>Auswahlwerte: Checkbox wie im Wizard, Select aus der Vorlage; Text/Zahl: frei.</summary>
    public IReadOnlyList<string> Choices => Step.Type switch
    {
        "checkbox" => new[] { PruefungValues.CheckboxOk, PruefungValues.CheckboxNok },
        "select"   => Step.Options,
        _          => Array.Empty<string>(),
    };

    public bool IsChoice => Choices.Count > 0;
    public bool IsFreeText => !IsChoice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDefect))]
    private string _value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDefect))]
    private string _note;

    public bool IsDefect => PruefungValues.IsDefect(Step with { Value = Value ?? string.Empty, DefectNote = Note ?? string.Empty });

    public PruefungChecklistStepData Current => Step with { Value = Value ?? string.Empty, DefectNote = Note ?? string.Empty };
}

/// <summary>
/// Gemeinsame Oberfläche für Aufzug- und Sprinklerprüfungen. Die Domäne steckt vollständig in
/// <see cref="IPruefungApi"/>; Unterschiede (Nummernbezeichnung, Zugehörigkeiten, Rechte) liefert der Adapter.
/// </summary>
public abstract partial class PruefungViewModel : ViewModelBase, INavigationTarget
{
    private const int PageSize = 100;
    private readonly Func<string, bool> _hasPermission;
    private readonly Func<CancellationToken, Task<IReadOnlyList<HausOption>>> _loadHouses;

    protected PruefungViewModel(
        IPruefungApi domain,
        Func<string, bool> hasPermission,
        Func<CancellationToken, Task<IReadOnlyList<HausOption>>> loadHouses)
    {
        Domain = domain;
        _hasPermission = hasPermission;
        _loadHouses = loadHouses;
    }

    public IPruefungApi Domain { get; }
    public string Title => Domain.Title;
    public string NummerLabel => Domain.NummerLabel;
    public IReadOnlyList<string> ZugehoerigkeitOptions => Domain.ZugehoerigkeitOptions;
    public static IReadOnlyList<ResultChoice> ResultOptions { get; } = new[]
    {
        new ResultChoice(PruefungValues.ResultPassed, "desktop.pruefung.result_passed"),
        new ResultChoice(PruefungValues.ResultFailed, "desktop.pruefung.result_failed"),
    };
    public static IReadOnlyList<string> StatusFilterOptions { get; } = new[] { string.Empty, PruefungValues.StatusInProgress, PruefungValues.StatusCompleted };

    /// <summary>Prüfungen anlegen/erfassen/abschließen (wie Web: *_pruefung_edit).</summary>
    public bool CanEdit => _hasPermission(Domain.EditPermission) || _hasPermission(Domain.AdminPermission);
    /// <summary>Anlagen anlegen/ändern/löschen (wie Web: *_pruefung_admin).</summary>
    public bool CanAdmin => _hasPermission(Domain.AdminPermission);

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string _summary = string.Empty;

    public ObservableCollection<PruefungSystem> Systems { get; } = new();
    public ObservableCollection<PruefungInspection> Inspections { get; } = new();
    public ObservableCollection<PruefungChecklistEditItem> ChecklistItems { get; } = new();
    public ObservableCollection<HausOption> Houses { get; } = new();

    [ObservableProperty] private PruefungSystem? _selectedSystem;
    [ObservableProperty] private PruefungInspection? _selectedInspection;
    [ObservableProperty] private PruefungInspectionDetail? _detail;
    [ObservableProperty] private int? _filterYear = DateTime.Today.Year;
    [ObservableProperty] private string _filterStatus = string.Empty;
    [ObservableProperty] private int _totalInspections;

    // Abschluss
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompleteResultChoice))]
    private string _completeResult = PruefungValues.ResultPassed;

    /// <summary>Auswahl in der Oberfläche; gespeichert wird der Code (passed/failed).</summary>
    public ResultChoice CompleteResultChoice
    {
        get => ResultOptions.FirstOrDefault(r => r.Value == CompleteResult) ?? ResultOptions[0];
        set
        {
            if (value == null || value.Value == CompleteResult) return;
            _resultChosenByUser = true;
            CompleteResult = value.Value;
        }
    }

    /// <summary>Solange der Prüfer das Ergebnis nicht selbst wählt, folgt es den Mängeln der Checkliste.</summary>
    private bool _resultChosenByUser;

    private void SuggestResult()
    {
        if (_resultChosenByUser) return;
        CompleteResult = ChecklistItems.Any(c => c.IsDefect) ? PruefungValues.ResultFailed : PruefungValues.ResultPassed;
    }

    private bool _missingRequiredShown;
    private string? _missingRequiredMessage;

    private void OnChecklistItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PruefungChecklistEditItem.Value) && _missingRequiredShown && ErrorMessage == _missingRequiredMessage)
        {
            ErrorMessage = null; // Hinweis auf offene Pflichtpunkte ist nach einer Eingabe überholt
            _missingRequiredShown = false;
        }
        if (e.PropertyName == nameof(PruefungChecklistEditItem.IsDefect)) SuggestResult();
    }
    [ObservableProperty] private string _completeNotes = string.Empty;
    [ObservableProperty] private bool _isSaving;

    // Anlagen-Formular
    [ObservableProperty] private bool _isSystemFormVisible;
    [ObservableProperty] private string? _systemFormError;
    [ObservableProperty] private string _formName = string.Empty;
    [ObservableProperty] private string _formNummer = string.Empty;
    [ObservableProperty] private HausOption? _formHaus;
    [ObservableProperty] private string _formZugehoerigkeit = "haus";
    [ObservableProperty] private bool _formActive = true;
    private int _editingSystemId;

    public bool IsDetailEditable => Detail != null && Detail.Status != PruefungValues.StatusCompleted && CanEdit;
    public bool HasDetail => Detail != null;
    public bool HasChecklist => ChecklistItems.Count > 0;

    public async Task OnNavigatedToAsync(object? parameter = null) => await LoadAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        var ct = StartLoad();
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var selectedId = SelectedSystem?.Id;
            var systems = await Domain.GetSystemsAsync(ct);
            Systems.Clear();
            foreach (var s in systems) Systems.Add(s);
            if (selectedId != null) SelectedSystem = Systems.FirstOrDefault(s => s.Id == selectedId);

            try
            {
                var d = await Domain.GetDashboardAsync(ct);
                Summary = T("desktop.pruefung.summary", ("systems", d.TotalSystems), ("week", d.InspectionsThisWeek), ("month", d.InspectionsThisMonth))
                          + (d.Overdue.Count > 0 ? T("desktop.pruefung.summary_due", ("count", d.Overdue.Count)) : string.Empty)
                          + (d.FailedLast30Days > 0 ? T("desktop.pruefung.summary_failed", ("count", d.FailedLast30Days)) : string.Empty);
            }
            catch (DkcProtobufApiException)
            {
                Summary = T("desktop.pruefung.systems_count", ("count", Systems.Count));
            }

            await LoadInspectionsCoreAsync(ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorMessage = T("desktop.common.load_failed_message", ("message", ex.Message)); }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task LoadInspectionsAsync()
    {
        var ct = StartLoad();
        IsLoading = true;
        ErrorMessage = null;
        try { await LoadInspectionsCoreAsync(ct); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorMessage = T("desktop.common.inspections_failed", ("message", ex.Message)); }
        finally { IsLoading = false; }
    }

    private async Task LoadInspectionsCoreAsync(CancellationToken ct)
    {
        var selectedId = SelectedInspection?.Id;
        var page = await Domain.GetInspectionsAsync(SelectedSystem?.Id, FilterYear,
            string.IsNullOrEmpty(FilterStatus) ? null : FilterStatus, PageSize, 0, ct);
        Inspections.Clear();
        foreach (var i in page.Items) Inspections.Add(i);
        TotalInspections = page.Total;
        if (selectedId != null) SelectedInspection = Inspections.FirstOrDefault(i => i.Id == selectedId);
    }

    public async Task LoadDetailAsync(int id)
    {
        ErrorMessage = null;
        try
        {
            Detail = await Domain.GetInspectionDetailAsync(id);
            ClearChecklist();
            foreach (var step in Detail.Steps)
            {
                var item = new PruefungChecklistEditItem(step);
                item.PropertyChanged += OnChecklistItemChanged;
                ChecklistItems.Add(item);
            }
            CompleteNotes = Detail.Notes;
            _resultChosenByUser = false;
            SuggestResult();
        }
        catch (Exception ex)
        {
            Detail = null;
            ClearChecklist();
            ErrorMessage = T("desktop.common.inspection_failed", ("message", ex.Message));
        }
        OnPropertyChanged(nameof(HasChecklist));
        // Freigabe hängt an HasChecklist – erst nach dem Befüllen neu bewerten.
        SaveChecklistCommand.NotifyCanExecuteChanged();
        CompleteInspectionCommand.NotifyCanExecuteChanged();
    }

    private void ClearChecklist()
    {
        foreach (var item in ChecklistItems) item.PropertyChanged -= OnChecklistItemChanged;
        ChecklistItems.Clear();
    }

    // ── Prüfung erfassen ────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStartInspection))]
    public async Task StartInspectionAsync()
    {
        if (SelectedSystem == null) return;
        IsSaving = true;
        ErrorMessage = null;
        try
        {
            var id = await Domain.SaveInspectionAsync(new PruefungInspectionInput(0, SelectedSystem.Id,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), PruefungValues.StatusInProgress, string.Empty, 0,
                string.Empty, string.Empty, string.Empty));
            StatusMessage = T("desktop.pruefung.created", ("id", id), ("system", SelectedSystem.Name));
            await LoadInspectionsAsync();
            SelectedInspection = Inspections.FirstOrDefault(i => i.Id == id);
            if (SelectedInspection == null) await LoadDetailAsync(id);
        }
        catch (Exception ex) { ErrorMessage = T("desktop.pruefung.create_failed", ("message", ex.Message)); }
        finally { IsSaving = false; }
    }

    private bool CanStartInspection() => CanEdit && SelectedSystem != null && !IsSaving;

    [RelayCommand(CanExecute = nameof(CanSaveChecklist))]
    public async Task SaveChecklistAsync()
    {
        if (Detail == null) return;
        IsSaving = true;
        ErrorMessage = null;
        try
        {
            await Domain.SaveChecklistAsync(Detail.Id, ChecklistItems
                .Where(c => !string.IsNullOrWhiteSpace(c.Value) || !string.IsNullOrWhiteSpace(c.Note))
                .Select(c => new PruefungChecklistValue(c.Step.Id, c.Value ?? string.Empty, c.Note ?? string.Empty)));
            StatusMessage = T("desktop.pruefung.checklist_saved");
        }
        catch (Exception ex) { ErrorMessage = T("desktop.pruefung.checklist_save_failed", ("message", ex.Message)); }
        finally { IsSaving = false; }
    }

    private bool CanSaveChecklist() => IsDetailEditable && HasChecklist && !IsSaving;

    /// <summary>Speichert die Checkliste und schließt ab – nur wenn alle Pflichtpunkte erfasst sind (wie im Web-Wizard).</summary>
    [RelayCommand(CanExecute = nameof(CanSaveChecklist))]
    public async Task CompleteInspectionAsync()
    {
        if (Detail == null) return;
        var missing = PruefungValues.MissingRequired(ChecklistItems.Select(c => c.Current));
        if (missing.Count > 0)
        {
            var items = string.Join(", ", missing.Take(3).Select(m => m.Title)) + (missing.Count > 3 ? " …" : string.Empty);
            ErrorMessage = _missingRequiredMessage = T("desktop.pruefung.missing_required", ("count", missing.Count), ("items", items));
            _missingRequiredShown = true;
            return;
        }

        await SaveChecklistAsync();
        if (ErrorMessage != null) return;

        IsSaving = true;
        try
        {
            var id = Detail.Id;
            await Domain.CompleteInspectionAsync(id, CompleteResult, CompleteNotes ?? string.Empty);
            StatusMessage = T("desktop.pruefung.completed", ("id", id), ("result", ResultLabel(CompleteResult)));
            await LoadInspectionsAsync();
            await LoadDetailAsync(id);
        }
        catch (Exception ex) { ErrorMessage = T("desktop.pruefung.complete_failed", ("message", ex.Message)); }
        finally { IsSaving = false; }
    }

    public static string ResultLabel(string result) => PruefungValues.ResultLabel(result);

    // ── Anlagen ─────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanAdmin))]
    public async Task ShowCreateSystemFormAsync()
    {
        _editingSystemId = 0;
        FormName = string.Empty;
        FormNummer = string.Empty;
        FormZugehoerigkeit = ZugehoerigkeitOptions[0];
        FormActive = true;
        await EnsureHousesAsync();
        FormHaus = null;
        SystemFormError = null;
        IsSystemFormVisible = true;
    }

    [RelayCommand(CanExecute = nameof(CanEditSystem))]
    public async Task ShowEditSystemFormAsync()
    {
        if (SelectedSystem == null) return;
        var s = SelectedSystem;
        _editingSystemId = s.Id;
        FormName = s.Name;
        FormNummer = s.Nummer;
        FormZugehoerigkeit = ZugehoerigkeitOptions.Contains(s.Zugehoerigkeit) ? s.Zugehoerigkeit : ZugehoerigkeitOptions[0];
        FormActive = s.Active;
        await EnsureHousesAsync();
        FormHaus = Houses.FirstOrDefault(h => h.Id == s.HausId);
        SystemFormError = null;
        IsSystemFormVisible = true;
    }

    private bool CanEditSystem() => CanAdmin && SelectedSystem != null;

    [RelayCommand]
    public void CancelSystemForm() => IsSystemFormVisible = false;

    [RelayCommand]
    public async Task SaveSystemAsync()
    {
        if (string.IsNullOrWhiteSpace(FormName)) { SystemFormError = T("desktop.pruefung.designation_is_required"); return; }
        if (FormHaus == null) { SystemFormError = T("desktop.pruefung.choose_house_error"); return; }
        IsSaving = true;
        SystemFormError = null;
        try
        {
            var id = await Domain.SaveSystemAsync(new PruefungSystemInput(_editingSystemId, FormName.Trim(), FormNummer.Trim(),
                FormHaus.Id, FormZugehoerigkeit, FormActive));
            IsSystemFormVisible = false;
            StatusMessage = _editingSystemId > 0 ? T("desktop.pruefung.system_saved") : T("desktop.pruefung.system_created");
            await LoadAsync();
            SelectedSystem = Systems.FirstOrDefault(s => s.Id == id);
        }
        catch (Exception ex) { SystemFormError = T("desktop.common.save_failed_message", ("message", ex.Message)); }
        finally { IsSaving = false; }
    }

    [RelayCommand(CanExecute = nameof(CanEditSystem))]
    public async Task DeleteSystemAsync()
    {
        if (SelectedSystem == null) return;
        var s = SelectedSystem;
        ErrorMessage = null;
        try
        {
            await Domain.DeleteSystemAsync(s.Id);
            StatusMessage = T("desktop.pruefung.system_deleted", ("name", s.Name));
            SelectedSystem = null;
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = T("desktop.common.delete_failed_message", ("message", ex.Message)); }
    }

    /// <summary>
    /// Häuser für das Anlagen-Formular. Ohne Zugriff auf die MM-Auswahlliste (kein MM-Recht)
    /// bleiben die Häuser der vorhandenen Anlagen wählbar.
    /// </summary>
    private async Task EnsureHousesAsync()
    {
        if (Houses.Count > 0) return;
        IReadOnlyList<HausOption> houses;
        try { houses = await _loadHouses(CancellationToken.None); }
        catch (Exception) { houses = Array.Empty<HausOption>(); }
        if (houses.Count == 0)
            houses = Systems.Where(s => s.HausId > 0).GroupBy(s => s.HausId)
                .Select(g => new HausOption(g.Key, string.IsNullOrWhiteSpace(g.First().HausName) ? T("desktop.pruefung.house_n", ("number", g.Key)) : g.First().HausName))
                .OrderBy(h => h.Name).ToList();
        foreach (var h in houses) Houses.Add(h);
    }

    // ── Reaktionen ──────────────────────────────────────────────────────────

    partial void OnSelectedSystemChanged(PruefungSystem? value)
    {
        StartInspectionCommand.NotifyCanExecuteChanged();
        ShowEditSystemFormCommand.NotifyCanExecuteChanged();
        DeleteSystemCommand.NotifyCanExecuteChanged();
        _ = LoadInspectionsAsync();
    }

    partial void OnSelectedInspectionChanged(PruefungInspection? value)
    {
        if (value != null) _ = LoadDetailAsync(value.Id);
    }

    partial void OnDetailChanged(PruefungInspectionDetail? value)
    {
        OnPropertyChanged(nameof(IsDetailEditable));
        OnPropertyChanged(nameof(HasDetail));
        SaveChecklistCommand.NotifyCanExecuteChanged();
        CompleteInspectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSavingChanged(bool value)
    {
        StartInspectionCommand.NotifyCanExecuteChanged();
        SaveChecklistCommand.NotifyCanExecuteChanged();
        CompleteInspectionCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>Häuser-Quelle für die Anlagen-Formulare (MM_FORM_OPTIONS, Web: getHausOptionen()).</summary>
internal static class PruefungHouses
{
    public static async Task<IReadOnlyList<HausOption>> LoadAsync(DkcApiFactory factory, AuthService auth, CancellationToken ct)
    {
        var options = await new DkcProtobufApi(factory.CreateProtobuf(auth.CurrentToken)).GetMmFormOptionsAsync(ct);
        return options.Streets.Select(s => new HausOption(s.Id, s.Name)).ToList();
    }

    public static bool HasPermission(AuthService auth, string key)
        => (auth.CurrentUser?.IsAdmin ?? false) || auth.HasPermission(key);
}

public sealed class AufzugPruefungViewModel : PruefungViewModel
{
    public AufzugPruefungViewModel(DkcApiFactory factory, AuthService auth)
        : base(new AufzugPruefungApi(() => new DkcProtobufApi(factory.CreateProtobuf(auth.CurrentToken))),
               key => PruefungHouses.HasPermission(auth, key),
               ct => PruefungHouses.LoadAsync(factory, auth, ct)) { }

    /// <summary>Für Tests mit Stub-Domäne.</summary>
    public AufzugPruefungViewModel(IPruefungApi domain, Func<string, bool> hasPermission, Func<CancellationToken, Task<IReadOnlyList<HausOption>>> loadHouses)
        : base(domain, hasPermission, loadHouses) { }
}

public sealed class SprinklerPruefungViewModel : PruefungViewModel
{
    public SprinklerPruefungViewModel(DkcApiFactory factory, AuthService auth)
        : base(new SprinklerPruefungApi(() => new DkcProtobufApi(factory.CreateProtobuf(auth.CurrentToken))),
               key => PruefungHouses.HasPermission(auth, key),
               ct => PruefungHouses.LoadAsync(factory, auth, ct)) { }

    public SprinklerPruefungViewModel(IPruefungApi domain, Func<string, bool> hasPermission, Func<CancellationToken, Task<IReadOnlyList<HausOption>>> loadHouses)
        : base(domain, hasPermission, loadHouses) { }
}
