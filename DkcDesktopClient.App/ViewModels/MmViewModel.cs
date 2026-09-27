using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.App.Services;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Protobuf;
using DkcDesktopClient.Core.Services;
using ProtoMmListRequest = DkcDesktopClient.Core.Protocol.MmListRequest;
using ProtoMmDetailRequest = DkcDesktopClient.Core.Protocol.MmDetailRequest;
using ProtoMmSaveRequest = DkcDesktopClient.Core.Protocol.MmSaveRequest;
using ProtoMmUpdateStatusRequest = DkcDesktopClient.Core.Protocol.MmUpdateStatusRequest;
using ProtoMmAssignContractorRequest = DkcDesktopClient.Core.Protocol.MmAssignContractorRequest;
using ProtoMmDeleteRequest = DkcDesktopClient.Core.Protocol.MmDeleteRequest;

namespace DkcDesktopClient.App.ViewModels;

/// <summary>Represents a selectable status option in the MM status dropdowns.</summary>
public record MmStatusOption(int? Value, string Label);

/// <summary>Generic string-valued filter option (e.g. Dringlichkeit filter), "" = kein Filter/Alle.</summary>
public record MmFilterOption(string Value, string Label);

/// <summary>A selectable server-side sort order for the MM list (Web-Pendant: sortierbare Spalten in mm_list.tpl).</summary>
public record MmSortOption(string Label, string Column, bool Ascending);

public partial class MmViewModel : ViewModelBase, INavigationTarget
{
    private readonly DkcApiFactory _apiFactory;
    private readonly AuthService _authService;
    private readonly IFilePickerService _filePicker;
    private readonly BackgroundRefreshService _backgroundRefreshService;
    private readonly IDialogService _dialogService;
    private const int PageSize = 50;

    // ── List state ────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private ObservableCollection<MmMessage> _messages = new();
    [ObservableProperty] private MmMessage? _selectedMessage;
    [ObservableProperty] private MmDetail? _selectedDetail;
    [ObservableProperty] private int _totalMessages;
    [ObservableProperty] private int _currentOffset;
    public bool HasNoMessages => !IsLoading && Messages.Count == 0;

    // Filter (Web-Pendant: mm_list.tpl Filterleiste)
    [ObservableProperty] private MmStatusOption _filterStatusOption = StatusFilterOptions[0];
    [ObservableProperty] private string? _filterStreet;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private MmFilterOption _filterDringlichkeitOption = DringlichkeitFilterOptions[0];
    [ObservableProperty] private string _filterYear = string.Empty;
    [ObservableProperty] private MmSortOption _sortOption = SortOptions[0];
    public bool CanGoToPreviousPage => CurrentOffset > 0;
    public bool CanGoToNextPage => CurrentOffset + PageSize < TotalMessages;
    public int CurrentPageNumber => CurrentOffset / PageSize + 1;
    public int TotalPages => TotalMessages <= 0 ? 1 : (int)Math.Ceiling(TotalMessages / (double)PageSize);

    // ── Form state ────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isFormVisible;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string? _formError;
    [ObservableProperty] private bool _isEditingMessage;
    private string? _editingUid;

    // Form fields
    [ObservableProperty] private string _formBetreff = string.Empty;
    [ObservableProperty] private string _formMeldung = string.Empty;
    [ObservableProperty] private string _formStreet = string.Empty;
    [ObservableProperty] private string _formWhg = string.Empty;
    [ObservableProperty] private string _formMelder = string.Empty;
    [ObservableProperty] private string _formTel = string.Empty;
    [ObservableProperty] private string _formEmail = string.Empty;
    [ObservableProperty] private string _formDringlichkeit = "normal";
    [ObservableProperty] private string _formNachunternehmer = string.Empty;
    [ObservableProperty] private string _formZugeh = string.Empty;

    // Status / contractor quick-edit on detail panel
    [ObservableProperty] private MmStatusOption _detailStatusOption = StatusEditOptions[0];
    [ObservableProperty] private string _detailStatusComment = string.Empty;
    [ObservableProperty] private string _detailNachunternehmer = string.Empty;

    // ── Dropdown data ─────────────────────────────────────────────────────────
    /// <summary>Streets accumulated from loaded messages — used as suggestions in the form.</summary>
    [ObservableProperty] private ObservableCollection<string> _streetOptions = new();

    /// <summary>Contractor names accumulated from loaded messages — used as suggestions.</summary>
    [ObservableProperty] private ObservableCollection<string> _contractorOptions = new();

    /// <summary>User full-names loaded from users_list (admin only). Used for the Melder ComboBox.</summary>
    [ObservableProperty] private ObservableCollection<string> _melderOptions = new();

    // Guards against triggering LoadUserInfoAsync more than once concurrently
    private bool _userInfoLoading;

    // ── Static option lists ───────────────────────────────────────────────────
    /// <summary>Status options for the filter ComboBox (includes "Alle" = null).</summary>
    public static IReadOnlyList<MmStatusOption> StatusFilterOptions { get; } =
        new[]
        {
            new MmStatusOption(null, "— Alle —"),
            new MmStatusOption(0,    MmStatusHelper.StatusLabel(0)),
            new MmStatusOption(1,    MmStatusHelper.StatusLabel(1)),
            new MmStatusOption(2,    MmStatusHelper.StatusLabel(2)),
            new MmStatusOption(3,    MmStatusHelper.StatusLabel(3)),
        };

    /// <summary>Status options for the detail / create-form ComboBox (no "Alle").</summary>
    public static IReadOnlyList<MmStatusOption> StatusEditOptions { get; } =
        new[]
        {
            new MmStatusOption(0, MmStatusHelper.StatusLabel(0)),
            new MmStatusOption(1, MmStatusHelper.StatusLabel(1)),
            new MmStatusOption(2, MmStatusHelper.StatusLabel(2)),
            new MmStatusOption(3, MmStatusHelper.StatusLabel(3)),
        };

    // Entspricht dem DB-Enum mm_messages.dringlichkeit (Web-Pendant: mm.tpl-Formular).
    public static IReadOnlyList<string> DringlichkeitOptions { get; } =
        new[] { "niedrig", "normal", "hoch", "kritisch" };

    /// <summary>Dringlichkeits-Filteroptionen inkl. "Alle" (Web-Pendant: mm_list.tpl filter_dringlichkeit).</summary>
    public static IReadOnlyList<MmFilterOption> DringlichkeitFilterOptions { get; } =
        new[]
        {
            new MmFilterOption("", "— Alle —"),
            new MmFilterOption("niedrig", "🔵 Niedrig"),
            new MmFilterOption("normal", "⚪ Normal"),
            new MmFilterOption("hoch", "🔴 Hoch"),
            new MmFilterOption("kritisch", "🚨 Kritisch"),
        };

    public static IReadOnlyList<MmSortOption> SortOptions { get; } =
        new[]
        {
            new MmSortOption("Neueste zuerst", "created_at", false),
            new MmSortOption("Älteste zuerst", "created_at", true),
            new MmSortOption("Betreff A–Z", "betreff", true),
            new MmSortOption("Dringlichkeit", "dringlichkeit", false),
            new MmSortOption("Status", "status", true),
        };

    public MmViewModel(
        DkcApiFactory apiFactory,
        AuthService authService,
        IFilePickerService filePicker,
        BackgroundRefreshService backgroundRefreshService,
        IDialogService dialogService)
    {
        _apiFactory = apiFactory;
        _authService = authService;
        _filePicker = filePicker;
        _backgroundRefreshService = backgroundRefreshService;
        _dialogService = dialogService;
        Messages.CollectionChanged += OnMessagesCollectionChanged;

        // Der zentrale Hintergrund-Dienst aktualisiert CacheKeys.MmList bereits periodisch
        // (siehe BackgroundRefreshService) — hier nur darauf reagieren und die aktuell
        // sichtbare (ggf. gefilterte) Liste im Hintergrund nachziehen.
        _backgroundRefreshService.DataRefreshed += OnBackgroundDataRefreshed;
    }

    /// <summary>
    /// Wird von <see cref="Services.INavigationService"/> aufgerufen, sobald diese Ansicht aktiv wird.
    /// Lädt die Liste sofort, statt auf einen manuellen Klick auf „Laden" zu warten.
    /// </summary>
    public Task OnNavigatedToAsync(object? parameter = null) => LoadMessagesAsync();

    /// <summary>
    /// MM-Lesezugriffe (Liste/Detail) laufen über Protobuf statt REST, weil die Server-REST-Route
    /// (system\api\MmApiHandler) nur status/street kennt und die MM-Schreib-Actions (mm_create,
    /// mm_update, mm_update_status, mm_assign_contractor, mm_delete) in api.php nicht registriert
    /// sind — nur die Protobuf-Actions (system\protobuf\Actions\ActionRegistry) sind vollständig
    /// implementiert. Siehe docs/reports/MM_LISTE_DESKTOP_CLIENT_LUECKENANALYSE.md.
    /// </summary>
    private DkcProtobufApi ProtoApi => new(_apiFactory.CreateProtobuf(_authService.CurrentToken));

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(HasNoMessages));

    private void OnBackgroundDataRefreshed(object? sender, string key)
    {
        if (key != CacheKeys.MmList) return;
        _ = Dispatcher.UIThread.InvokeAsync(() => LoadMessagesInternalAsync(silent: true));
    }

    // ── CSV Export ────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanExportMessages))]
    public async Task ExportMessagesToCsvAsync()
    {
        var path = await _filePicker.PickSaveFileAsync(
            $"maengelmeldungen_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            new[] { ("CSV-Datei", "*.csv") });
        if (path == null) return;

        var columns = new (string, Func<MmMessage, string?>)[]
        {
            ("UID",           m => m.Uid),
            ("Status",        m => m.StatusText),
            ("Betreff",       m => m.Betreff),
            ("Dringlichkeit", m => m.DringlichkeitText),
            ("Melder",        m => m.Melder),
            ("Datum",         m => m.Datetime),
            ("Zugehörigkeit", m => m.Zugeh),
        };

        try
        {
            CsvExportService.ExportToCsv(path, Messages, columns);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"CSV-Export fehlgeschlagen: {ex.Message}";
        }
    }

    private bool CanExportMessages() => Messages.Count > 0;

    partial void OnMessagesChanged(ObservableCollection<MmMessage>? oldValue, ObservableCollection<MmMessage> newValue)
    {
        if (oldValue != null)
            oldValue.CollectionChanged -= OnMessagesCollectionChanged;
        newValue.CollectionChanged += OnMessagesCollectionChanged;
        OnPropertyChanged(nameof(HasNoMessages));
        ExportMessagesToCsvCommand.NotifyCanExecuteChanged();
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasNoMessages));
        ExportMessagesToCsvCommand.NotifyCanExecuteChanged();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    public Task LoadMessagesAsync()
    {
        CurrentOffset = 0;
        return LoadMessagesInternalAsync(silent: false);
    }

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    public Task NextPageAsync()
    {
        CurrentOffset += PageSize;
        return LoadMessagesInternalAsync(silent: false);
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    public Task PreviousPageAsync()
    {
        CurrentOffset = Math.Max(0, CurrentOffset - PageSize);
        return LoadMessagesInternalAsync(silent: false);
    }

    /// <summary>
    /// Loads the (filtered) message list. When <paramref name="silent"/> is true (periodic
    /// background refresh via <see cref="OnBackgroundDataRefreshed"/>), a failure does not
    /// overwrite <see cref="ErrorMessage"/> and the current selection is restored by UID
    /// afterwards, so an unattended refresh never disrupts what the user is looking at. Silent
    /// refreshes keep the current page (offset) instead of resetting to page 1.
    /// </summary>
    private async Task LoadMessagesInternalAsync(bool silent)
    {
        var ct = StartLoad();
        IsLoading = true;
        if (!silent)
            ErrorMessage = null;
        var previouslySelectedUid = silent ? SelectedMessage?.Uid : null;
        try
        {
            var request = new ProtoMmListRequest
            {
                Status = FilterStatusOption.Value ?? -1,
                Street = FilterStreet ?? string.Empty,
                Limit = PageSize,
                Offset = CurrentOffset,
                Search = SearchText.Trim(),
                Dringlichkeit = FilterDringlichkeitOption.Value,
                Year = int.TryParse(FilterYear, out var year) ? year : 0,
                SortColumn = SortOption.Column,
                SortAscending = SortOption.Ascending,
            };
            var result = await ProtoApi.GetMmListAsync(request, ct);

            Messages.Clear();
            TotalMessages = result.Page?.Total ?? 0;
            foreach (var m in result.Messages)
            {
                Messages.Add(new MmMessage(
                    m.Uid, m.Status, m.Betreff, m.Street, m.Whg, m.Melder, m.Datetime,
                    m.Dringlichkeit, m.Nachunternehmer, m.Scanned, m.Zugeh,
                    m.StreetName, m.NachunternehmerName, m.FollowupCount));
            }
            RefreshDropdownSuggestions(Messages);
            ErrorMessage = null;
            if (previouslySelectedUid != null)
                SelectedMessage = Messages.FirstOrDefault(m => m.Uid == previouslySelectedUid);

            // Defer the next background refresh for this key — we just fetched fresh data.
            _backgroundRefreshService.NotifyUserActivity(CacheKeys.MmList);
        }
        catch (OperationCanceledException)
        {
            // Navigation away – discard silently.
        }
        catch (Exception ex)
        {
            if (!silent)
                ErrorMessage = $"Fehler beim Laden der Mängelmeldungen: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(CanGoToPreviousPage));
            OnPropertyChanged(nameof(CanGoToNextPage));
            OnPropertyChanged(nameof(CurrentPageNumber));
            OnPropertyChanged(nameof(TotalPages));
            PreviousPageCommand.NotifyCanExecuteChanged();
            NextPageCommand.NotifyCanExecuteChanged();
        }

        // Lazily populate the Melder dropdown the first time messages are loaded;
        // the flag prevents duplicate concurrent loads if LoadMessagesAsync is called again before the first completes.
        if (MelderOptions.Count == 0 && !_userInfoLoading)
            _ = LoadUserInfoAsync();
    }

    [RelayCommand]
    public async Task LoadDetailAsync()
    {
        if (SelectedMessage == null) return;
        IsLoading = true;
        try
        {
            var result = await ProtoApi.GetMmDetailAsync(new ProtoMmDetailRequest { Uid = SelectedMessage.Uid });
            var d = result.Message;
            if (d != null)
            {
                SelectedDetail = new MmDetail(
                    d.Uid, d.Status, d.Betreff, d.MeldungMassage, d.Street, d.Whg, d.Melder,
                    d.Tel, d.Email, d.Datetime, d.Dringlichkeit, d.Nachunternehmer, d.Scanned,
                    d.Zugeh, d.Zeit, d.Planon, d.StreetName, d.NachunternehmerName, d.Instructions.ToList());
                DetailStatusOption = StatusEditOptions.FirstOrDefault(o => o.Value == d.Status)
                                     ?? StatusEditOptions[0];
                DetailStatusComment = string.Empty;
                // MmAssignContractorHandler erwartet zwingend eine numerische NU-ID als String
                // (kein Freitext-Name) — der aufgelöste Name wird separat über
                // SelectedDetail.NachunternehmerName angezeigt.
                DetailNachunternehmer = d.Nachunternehmer > 0 ? d.Nachunternehmer.ToString() : string.Empty;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Fehler beim Laden des Details: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Tries to load the user list from the API so that Melder can be selected from a dropdown.
    /// Requires admin permission; silently skipped when the logged-in user is not an admin.
    /// Other unexpected errors are surfaced as a non-blocking warning message.
    /// </summary>
    [RelayCommand]
    public async Task LoadUserInfoAsync()
    {
        // users_list requires admin permission — skip silently for non-admin users
        if (!_authService.HasPermission("admin"))
            return;

        _userInfoLoading = true;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.GetUsersListAsync();
            if (result.Success && result.Users != null)
            {
                var names = result.Users
                    .Select(u => $"{u.Vname} {u.Nname}".Trim())
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();

                MelderOptions.Clear();
                foreach (var name in names)
                    MelderOptions.Add(name);
            }
        }
        catch (Exception ex)
        {
            // Surface unexpected errors (network, deserialization, etc.) as a non-blocking warning
            ErrorMessage = $"Benutzerliste konnte nicht geladen werden: {ex.Message}";
        }
        finally
        {
            _userInfoLoading = false;
        }
    }

    // ── Create / Edit form ────────────────────────────────────────────────────

    [RelayCommand]
    public void ShowCreateForm()
    {
        IsEditingMessage = false;
        _editingUid = null;
        FormBetreff = string.Empty;
        FormMeldung = string.Empty;
        FormStreet = string.Empty;
        FormWhg = string.Empty;

        // Pre-fill reporter info from the currently logged-in user
        var user = _authService.CurrentUser;
        FormMelder = user != null ? $"{user.Vname} {user.Nname}".Trim() : string.Empty;
        FormTel    = string.Empty;
        FormEmail  = user?.Email ?? string.Empty;

        FormDringlichkeit   = "normal";
        FormNachunternehmer = string.Empty;
        FormZugeh           = string.Empty;
        FormError           = null;
        IsFormVisible       = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedDetail))]
    public void ShowEditForm()
    {
        if (SelectedDetail == null) return;
        IsEditingMessage    = true;
        _editingUid         = SelectedDetail.Uid;
        FormBetreff         = SelectedDetail.Betreff         ?? string.Empty;
        FormMeldung         = SelectedDetail.MeldungMassage  ?? string.Empty;
        FormStreet          = SelectedDetail.Street?.ToString()          ?? string.Empty;
        FormWhg             = SelectedDetail.Whg             ?? string.Empty;
        FormMelder          = SelectedDetail.Melder          ?? string.Empty;
        FormTel             = SelectedDetail.Tel             ?? string.Empty;
        FormEmail           = SelectedDetail.Email           ?? string.Empty;
        FormDringlichkeit   = SelectedDetail.Dringlichkeit   ?? "normal";
        FormNachunternehmer = SelectedDetail.Nachunternehmer?.ToString() ?? string.Empty;
        FormZugeh           = SelectedDetail.Zugeh           ?? string.Empty;
        FormError           = null;
        IsFormVisible       = true;
    }

    [RelayCommand]
    public void CancelForm()
    {
        IsFormVisible = false;
        FormError     = null;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(FormBetreff))
        {
            FormError = "Betreff ist ein Pflichtfeld.";
            return;
        }
        IsSaving  = true;
        FormError = null;
        try
        {
            var req = new ProtoMmSaveRequest
            {
                Uid = IsEditingMessage && _editingUid != null ? _editingUid : string.Empty,
                Betreff = FormBetreff,
                MeldungMassage = FormMeldung,
                Street = FormStreet,
                Whg = FormWhg,
                Melder = FormMelder,
                Tel = FormTel,
                Email = FormEmail,
                Dringlichkeit = FormDringlichkeit,
                Nachunternehmer = FormNachunternehmer,
                Zugeh = FormZugeh,
            };

            if (IsEditingMessage && _editingUid != null)
            {
                await ProtoApi.UpdateMmAsync(req);
            }
            else
            {
                await ProtoApi.CreateMmAsync(req);
            }

            IsFormVisible = false;
            await LoadMessagesAsync();
        }
        catch (Exception ex)
        {
            FormError = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMessage))]
    public async Task DeleteAsync()
    {
        if (SelectedMessage == null) return;
        var confirmed = await _dialogService.ConfirmAsync(
            "Mängelmeldung löschen",
            $"Soll die Mängelmeldung „{SelectedMessage.Betreff}“ ({SelectedMessage.Uid}) wirklich unwiderruflich gelöscht werden?");
        if (!confirmed) return;

        IsLoading    = true;
        ErrorMessage = null;
        try
        {
            await ProtoApi.DeleteMmAsync(new ProtoMmDeleteRequest { Uid = SelectedMessage.Uid });
            Messages.Remove(SelectedMessage);
            SelectedDetail = null;
            TotalMessages  = Math.Max(0, TotalMessages - 1);
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

    [RelayCommand(CanExecute = nameof(HasSelectedDetail))]
    public async Task UpdateStatusAsync()
    {
        if (SelectedDetail == null) return;
        IsLoading    = true;
        ErrorMessage = null;
        try
        {
            await ProtoApi.UpdateMmStatusAsync(new ProtoMmUpdateStatusRequest
            {
                Uid = SelectedDetail.Uid,
                Status = DetailStatusOption.Value ?? 0,
                Comment = DetailStatusComment,
            });
            await LoadDetailAsync();
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

    [RelayCommand(CanExecute = nameof(HasSelectedDetail))]
    public async Task AssignContractorAsync()
    {
        if (SelectedDetail == null || string.IsNullOrWhiteSpace(DetailNachunternehmer)) return;
        IsLoading    = true;
        ErrorMessage = null;
        try
        {
            await ProtoApi.AssignMmContractorAsync(new ProtoMmAssignContractorRequest
            {
                Uid = SelectedDetail.Uid,
                Nachunternehmer = DetailNachunternehmer,
            });
            await LoadDetailAsync();
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

    // ── Quick filter chip colors ──────────────────────────────────────────────
    private const string ChipActiveBg  = "#3B82F6";
    private const string ChipActiveFg  = "White";
    private const string ChipDefaultBg = "#E5E7EB";
    private const string ChipDefaultFg = "#374151";

    public string FilterChipAllBg         => FilterStatusOption.Value == null ? ChipActiveBg : ChipDefaultBg;
    public string FilterChipAllFg         => FilterStatusOption.Value == null ? ChipActiveFg : ChipDefaultFg;
    public string FilterChipOpenBg        => FilterStatusOption.Value == 0   ? ChipActiveBg : ChipDefaultBg;
    public string FilterChipOpenFg        => FilterStatusOption.Value == 0   ? ChipActiveFg : ChipDefaultFg;
    public string FilterChipInProgressBg  => FilterStatusOption.Value == 1   ? ChipActiveBg : ChipDefaultBg;
    public string FilterChipInProgressFg  => FilterStatusOption.Value == 1   ? ChipActiveFg : ChipDefaultFg;
    public string FilterChipCompletedBg   => FilterStatusOption.Value == 2   ? ChipActiveBg : ChipDefaultBg;
    public string FilterChipCompletedFg   => FilterStatusOption.Value == 2   ? ChipActiveFg : ChipDefaultFg;

    [RelayCommand]
    public async Task SetFilterAllAsync()
    {
        FilterStatusOption = StatusFilterOptions[0];
        await LoadMessagesAsync();
    }

    [RelayCommand]
    public async Task SetFilterOpenAsync()
    {
        FilterStatusOption = StatusFilterOptions[1];
        await LoadMessagesAsync();
    }

    [RelayCommand]
    public async Task SetFilterInProgressAsync()
    {
        FilterStatusOption = StatusFilterOptions[2];
        await LoadMessagesAsync();
    }

    [RelayCommand]
    public async Task SetFilterCompletedAsync()
    {
        FilterStatusOption = StatusFilterOptions[3];
        await LoadMessagesAsync();
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private bool CanSave()           => !IsSaving;
    private bool HasSelectedMessage() => SelectedMessage != null;
    private bool HasSelectedDetail()  => SelectedDetail  != null;

    /// <summary>
    /// Refreshes the street and contractor suggestion lists from the currently
    /// loaded message batch.  Uses a HashSet for O(1) duplicate detection.
    /// </summary>
    private void RefreshDropdownSuggestions(IEnumerable<MmMessage> loaded)
    {
        var existingStreets     = new HashSet<string>(StreetOptions,     StringComparer.OrdinalIgnoreCase);
        var existingContractors = new HashSet<string>(ContractorOptions, StringComparer.OrdinalIgnoreCase);

        foreach (var m in loaded)
        {
            if (m.Street.HasValue)
            {
                var streetStr = m.Street.Value.ToString();
                if (existingStreets.Add(streetStr))
                    StreetOptions.Add(streetStr);
            }
            if (m.Nachunternehmer.HasValue)
            {
                var contractorStr = m.Nachunternehmer.Value.ToString();
                if (existingContractors.Add(contractorStr))
                    ContractorOptions.Add(contractorStr);
            }
        }
    }

    // ── Property-change hooks ─────────────────────────────────────────────────

    partial void OnSelectedMessageChanged(MmMessage? value)
    {
        if (value != null) _ = LoadDetailAsync();
        DeleteCommand.NotifyCanExecuteChanged();
        ShowEditFormCommand.NotifyCanExecuteChanged();
        UpdateStatusCommand.NotifyCanExecuteChanged();
        AssignContractorCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedDetailChanged(MmDetail? value)
    {
        ShowEditFormCommand.NotifyCanExecuteChanged();
        UpdateStatusCommand.NotifyCanExecuteChanged();
        AssignContractorCommand.NotifyCanExecuteChanged();
    }

    partial void OnFilterStatusOptionChanged(MmStatusOption value)
    {
        OnPropertyChanged(nameof(FilterChipAllBg));
        OnPropertyChanged(nameof(FilterChipAllFg));
        OnPropertyChanged(nameof(FilterChipOpenBg));
        OnPropertyChanged(nameof(FilterChipOpenFg));
        OnPropertyChanged(nameof(FilterChipInProgressBg));
        OnPropertyChanged(nameof(FilterChipInProgressFg));
        OnPropertyChanged(nameof(FilterChipCompletedBg));
        OnPropertyChanged(nameof(FilterChipCompletedFg));
    }

    partial void OnIsSavingChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();
}
