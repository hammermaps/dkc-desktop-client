using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Services;

namespace DkcDesktopClient.App.ViewModels;

/// <summary>Represents a selectable status option in the MM status dropdowns.</summary>
public record MmStatusOption(int? Value, string Label);

public partial class MmViewModel : ViewModelBase
{
    private readonly DkcApiFactory _apiFactory;
    private readonly AuthService _authService;
    private const int PageSize = 50;

    // ── List state ────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private ObservableCollection<MmMessage> _messages = new();
    [ObservableProperty] private MmMessage? _selectedMessage;
    [ObservableProperty] private MmDetail? _selectedDetail;
    [ObservableProperty] private int _totalMessages;
    [ObservableProperty] private int _currentOffset;

    // Filter
    [ObservableProperty] private MmStatusOption _filterStatusOption = StatusFilterOptions[0];
    [ObservableProperty] private string? _filterStreet;

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
    [ObservableProperty] private string _detailNachunternehmer = string.Empty;

    // ── Dropdown data ─────────────────────────────────────────────────────────
    /// <summary>Streets accumulated from loaded messages — used as suggestions in the form.</summary>
    [ObservableProperty] private ObservableCollection<string> _streetOptions = new();

    /// <summary>Contractor names accumulated from loaded messages — used as suggestions.</summary>
    [ObservableProperty] private ObservableCollection<string> _contractorOptions = new();

    /// <summary>User full-names loaded from users_list (admin only). Used for the Melder ComboBox.</summary>
    [ObservableProperty] private ObservableCollection<string> _melderOptions = new();

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

    public static IReadOnlyList<string> DringlichkeitOptions { get; } =
        new[] { "normal", "dringend", "notfall" };

    public MmViewModel(DkcApiFactory apiFactory, AuthService authService)
    {
        _apiFactory = apiFactory;
        _authService = authService;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadMessagesAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        CurrentOffset = 0;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.GetMmListAsync(
                status: FilterStatusOption.Value,
                street: FilterStreet,
                limit: PageSize,
                offset: 0);
            if (result.Success)
            {
                Messages.Clear();
                TotalMessages = result.Total ?? 0;
                if (result.Messages != null)
                {
                    foreach (var m in result.Messages)
                        Messages.Add(m);
                    RefreshDropdownSuggestions(result.Messages);
                }
            }
            else
            {
                ErrorMessage = result.Error ?? "Laden der Mängelmeldungen fehlgeschlagen.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Fehler beim Laden der Mängelmeldungen: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }

        // Lazily populate the Melder dropdown the first time messages are loaded
        if (MelderOptions.Count == 0)
            _ = LoadUserInfoAsync();
    }

    [RelayCommand]
    public async Task LoadDetailAsync()
    {
        if (SelectedMessage == null) return;
        IsLoading = true;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.GetMmDetailAsync(SelectedMessage.Uid);
            if (result.Success && result.Message != null)
            {
                SelectedDetail = result.Message;
                DetailStatusOption = StatusEditOptions.FirstOrDefault(o => o.Value == result.Message.Status)
                                     ?? StatusEditOptions[0];
                DetailNachunternehmer = result.Message.Nachunternehmer ?? string.Empty;
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
        FormStreet          = SelectedDetail.Street          ?? string.Empty;
        FormWhg             = SelectedDetail.Whg             ?? string.Empty;
        FormMelder          = SelectedDetail.Melder          ?? string.Empty;
        FormTel             = SelectedDetail.Tel             ?? string.Empty;
        FormEmail           = SelectedDetail.Email           ?? string.Empty;
        FormDringlichkeit   = SelectedDetail.Dringlichkeit   ?? "normal";
        FormNachunternehmer = SelectedDetail.Nachunternehmer ?? string.Empty;
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
            var api = _apiFactory.Create(_authService.CurrentToken);
            var req = new MmSaveRequest(
                FormBetreff,
                Nz(FormMeldung),
                Nz(FormStreet),
                Nz(FormWhg),
                Nz(FormMelder),
                Nz(FormTel),
                Nz(FormEmail),
                Nz(FormDringlichkeit),
                Nz(FormNachunternehmer),
                Nz(FormZugeh));

            ApiError result;
            if (IsEditingMessage && _editingUid != null)
            {
                result = await api.UpdateMmAsync(_editingUid, req);
            }
            else
            {
                var cr = await api.CreateMmAsync(req);
                result = new ApiError(cr.Success, cr.Error);
            }

            if (result.Success)
            {
                IsFormVisible = false;
                await LoadMessagesAsync();
            }
            else
            {
                FormError = result.Error ?? "Speichern fehlgeschlagen.";
            }
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
        IsLoading    = true;
        ErrorMessage = null;
        try
        {
            var api    = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.DeleteMmAsync(SelectedMessage.Uid);
            if (result.Success)
            {
                Messages.Remove(SelectedMessage);
                SelectedDetail = null;
                TotalMessages  = Math.Max(0, TotalMessages - 1);
            }
            else
            {
                ErrorMessage = result.Error ?? "Löschen fehlgeschlagen.";
            }
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
            var api    = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.UpdateMmStatusAsync(SelectedDetail.Uid,
                new MmStatusUpdateRequest(DetailStatusOption.Value ?? 0, null));
            if (result.Success)
                await LoadDetailAsync();
            else
                ErrorMessage = result.Error ?? "Statusaktualisierung fehlgeschlagen.";
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
            var api    = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.AssignMmContractorAsync(SelectedDetail.Uid,
                new MmAssignContractorRequest(DetailNachunternehmer));
            if (result.Success)
                await LoadDetailAsync();
            else
                ErrorMessage = result.Error ?? "Zuweisung fehlgeschlagen.";
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

    // ── Helper ────────────────────────────────────────────────────────────────

    private bool CanSave()           => !IsSaving;
    private bool HasSelectedMessage() => SelectedMessage != null;
    private bool HasSelectedDetail()  => SelectedDetail  != null;

    private static string? Nz(string s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;

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
            if (!string.IsNullOrWhiteSpace(m.Street) && existingStreets.Add(m.Street))
                StreetOptions.Add(m.Street);
            if (!string.IsNullOrWhiteSpace(m.Nachunternehmer) && existingContractors.Add(m.Nachunternehmer))
                ContractorOptions.Add(m.Nachunternehmer);
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

    partial void OnIsSavingChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();
}
