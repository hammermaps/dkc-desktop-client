using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.App.Services;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Protobuf;
using DkcDesktopClient.Core.Services;
using ProtoKeyInventorySaveRequest = DkcDesktopClient.Core.Protocol.KeyInventorySaveRequest;
using ProtoKeyIssueRequest = DkcDesktopClient.Core.Protocol.KeyIssueRequest;
using ProtoKeyReturnRequest = DkcDesktopClient.Core.Protocol.KeyReturnRequest;
using ProtoKeyDeleteRequest = DkcDesktopClient.Core.Protocol.KeyDeleteRequest;
using ProtoKeysInventoryRequest = DkcDesktopClient.Core.Protocol.KeysInventoryRequest;
using KeyTypeOption = DkcDesktopClient.Core.Protocol.KeyTypeItem;
using KeyCabinetOption = DkcDesktopClient.Core.Protocol.KeyCabinetItem;

namespace DkcDesktopClient.App.ViewModels;

public partial class KeysViewModel : ViewModelBase, INavigationTarget
{
    private readonly DkcApiFactory _apiFactory;
    private readonly AuthService _authService;
    private readonly IFilePickerService _filePicker;
    private readonly BackgroundRefreshService _backgroundRefreshService;
    private readonly IDialogService _dialogService;

    /// <summary>
    /// Schlüssel-Schreib-Actions (keys_create/update/issue/return/delete) sind in api.php nie
    /// als REST-Route registriert worden — nur die vollständig implementierten Protobuf-Actions
    /// funktionieren. Lesezugriffe (keys_inventory/keys_issued) bleiben REST (funktionieren dort).
    /// </summary>
    private DkcProtobufApi ProtoApi => new(_apiFactory.CreateProtobuf(_authService.CurrentToken));

    // List state
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private ObservableCollection<KeyInventoryItem> _inventory = new();
    [ObservableProperty] private KeyInventoryItem? _selectedInventoryItem;
    [ObservableProperty] private ObservableCollection<KeyIssuedItem> _issuedKeys = new();
    [ObservableProperty] private KeyIssuedItem? _selectedIssuedItem;
    [ObservableProperty] private int _selectedTabIndex;

    // Stats tiles
    [ObservableProperty] private int _statTotalKeyTypes;
    [ObservableProperty] private int _statTotalKeys;
    [ObservableProperty] private int _statIssuedKeys;
    [ObservableProperty] private int _statAvailableKeys;

    // Key create/edit form
    [ObservableProperty] private bool _isKeyFormVisible;
    [ObservableProperty] private bool _isSavingKey;
    [ObservableProperty] private string? _keyFormError;
    [ObservableProperty] private bool _isEditingKey;
    private int? _editingKeyId;

    [ObservableProperty] private string _formKeyName = string.Empty;
    [ObservableProperty] private string _formKeyDescription = string.Empty;
    [ObservableProperty] private int _formKeyTotal = 1;
    [ObservableProperty] private string _formKeyNumber = string.Empty;
    [ObservableProperty] private KeyTypeOption? _formKeyType;
    [ObservableProperty] private KeyCabinetOption? _formKeyCabinet;
    [ObservableProperty] private ObservableCollection<KeyTypeOption> _keyTypeOptions = new();
    [ObservableProperty] private ObservableCollection<KeyCabinetOption> _keyCabinetOptions = new();

    // Issue form
    [ObservableProperty] private bool _isIssueFormVisible;
    [ObservableProperty] private bool _isSavingIssue;
    [ObservableProperty] private string? _issueFormError;

    [ObservableProperty] private string _formIssuedTo = string.Empty;
    [ObservableProperty] private string _formIssuedAt = string.Empty;
    [ObservableProperty] private string _formIssueNotes = string.Empty;

    // Return form
    [ObservableProperty] private bool _isReturnFormVisible;
    [ObservableProperty] private bool _isSavingReturn;
    [ObservableProperty] private string? _returnFormError;
    [ObservableProperty] private string _formReturnDate = string.Empty;
    [ObservableProperty] private string _formReturnNotes = string.Empty;

    public KeysViewModel(
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
        // Wire CollectionChanged so the CSV export button reflects loaded state
        Inventory.CollectionChanged += (_, _) =>
            ExportInventoryToCsvCommand.NotifyCanExecuteChanged();

        _backgroundRefreshService.DataRefreshed += OnBackgroundDataRefreshed;
    }

    /// <summary>Called by <see cref="Services.INavigationService"/> when this view becomes active.</summary>
    public Task OnNavigatedToAsync(object? parameter = null) => LoadDataAsync();

    private void OnBackgroundDataRefreshed(object? sender, string key)
    {
        if (key != CacheKeys.KeysInventory) return;
        _ = Dispatcher.UIThread.InvokeAsync(() => LoadDataInternalAsync(silent: true));
    }

    [RelayCommand]
    public Task LoadDataAsync() => LoadDataInternalAsync(silent: false);

    private async Task LoadDataInternalAsync(bool silent)
    {
        var ct = StartLoad();
        IsLoading = true;
        if (!silent)
            ErrorMessage = null;
        var previouslySelectedInventoryId = silent ? SelectedInventoryItem?.Id : null;
        var previouslySelectedIssuedId = silent ? SelectedIssuedItem?.Id : null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            // Inventar läuft über Protobuf: KeysSaveHandler verlangt number/type_id, die REST
            // (keys_inventory) nicht liefert — die Liste braucht daher denselben Kanal wie das
            // Speichern, sonst fehlen beim Bearbeiten die Pflichtfelder.
            var inventoryTask = ProtoApi.GetKeysInventoryAsync(new ProtoKeysInventoryRequest(), ct);
            var issuedTask = api.GetKeysIssuedAsync(ct);
            var typesTask = ProtoApi.GetKeysTypesListAsync(ct);
            var cabinetsTask = ProtoApi.GetKeysCabinetsListAsync(ct);
            await Task.WhenAll(inventoryTask, issuedTask, typesTask, cabinetsTask);

            Inventory.Clear();
            foreach (var k in inventoryTask.Result.Keys)
            {
                Inventory.Add(new KeyInventoryItem(
                    k.Id, k.Name, k.Description, k.TotalCount, k.Available,
                    k.Number, k.TypeId, k.CabinetId, k.Enabled));
            }

            KeyTypeOptions = new ObservableCollection<KeyTypeOption>(typesTask.Result.Types_);
            KeyCabinetOptions = new ObservableCollection<KeyCabinetOption>(cabinetsTask.Result.Cabinets);

            IssuedKeys.Clear();
            if (issuedTask.Result.Success)
            {
                var issued = issuedTask.Result.Keys ?? issuedTask.Result.Issued;
                if (issued != null)
                {
                    foreach (var k in issued)
                    {
                        IssuedKeys.Add(k with { IssuedTo = k.IssuedTo ?? k.RecipientName });
                    }
                }
            }

            if (previouslySelectedInventoryId != null)
                SelectedInventoryItem = Inventory.FirstOrDefault(k => k.Id == previouslySelectedInventoryId);
            if (previouslySelectedIssuedId != null)
                SelectedIssuedItem = IssuedKeys.FirstOrDefault(k => k.Id == previouslySelectedIssuedId);

            _backgroundRefreshService.NotifyUserActivity(CacheKeys.KeysInventory);
        }
        catch (OperationCanceledException)
        {
            // Navigation away – discard silently.
        }
        catch (Exception ex)
        {
            if (!silent)
                ErrorMessage = $"Error loading keys data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            StatTotalKeyTypes = Inventory.Count;
            StatTotalKeys = Inventory.Sum(k => k.Total ?? 0);
            StatIssuedKeys = IssuedKeys.Count(k => k.ReturnedAt == null);
            StatAvailableKeys = Inventory.Sum(k => k.Available ?? 0);
        }
    }

    // — Key inventory CRUD —
    [RelayCommand]
    public void ShowCreateKeyForm()
    {
        IsIssueFormVisible  = false;
        IsReturnFormVisible = false;
        IssueFormError      = null;
        ReturnFormError     = null;

        IsEditingKey = false;
        _editingKeyId = null;
        FormKeyName = string.Empty;
        FormKeyDescription = string.Empty;
        FormKeyTotal = 1;
        FormKeyNumber = string.Empty;
        FormKeyType = KeyTypeOptions.FirstOrDefault();
        FormKeyCabinet = null;
        KeyFormError = null;
        IsKeyFormVisible = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedInventoryItem))]
    public void ShowEditKeyForm()
    {
        if (SelectedInventoryItem == null) return;

        IsIssueFormVisible  = false;
        IsReturnFormVisible = false;
        IssueFormError      = null;
        ReturnFormError     = null;

        IsEditingKey = true;
        _editingKeyId = SelectedInventoryItem.Id;
        FormKeyName = SelectedInventoryItem.Name ?? string.Empty;
        FormKeyDescription = SelectedInventoryItem.Description ?? string.Empty;
        FormKeyTotal = SelectedInventoryItem.Total ?? 1;
        FormKeyNumber = SelectedInventoryItem.Number;
        FormKeyType = KeyTypeOptions.FirstOrDefault(t => t.Id == SelectedInventoryItem.TypeId);
        FormKeyCabinet = KeyCabinetOptions.FirstOrDefault(c => c.Id == SelectedInventoryItem.CabinetId);
        KeyFormError = null;
        IsKeyFormVisible = true;
    }

    [RelayCommand]
    public void CancelKeyForm()
    {
        IsKeyFormVisible = false;
        KeyFormError = null;
    }

    [RelayCommand(CanExecute = nameof(CanSaveKey))]
    public async Task SaveKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(FormKeyName))
        {
            KeyFormError = "Name is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(FormKeyNumber))
        {
            KeyFormError = "Nummer ist ein Pflichtfeld.";
            return;
        }
        if (FormKeyType == null)
        {
            KeyFormError = "Typ ist ein Pflichtfeld.";
            return;
        }
        IsSavingKey = true;
        KeyFormError = null;
        try
        {
            var req = new ProtoKeyInventorySaveRequest
            {
                Id = IsEditingKey && _editingKeyId.HasValue ? _editingKeyId.Value : 0,
                Number = FormKeyNumber,
                Name = FormKeyName,
                Description = FormKeyDescription,
                TotalCount = FormKeyTotal,
                TypeId = FormKeyType.Id,
                CabinetId = FormKeyCabinet?.Id ?? 0,
                Enabled = true,
            };

            if (IsEditingKey && _editingKeyId.HasValue)
            {
                await ProtoApi.UpdateKeyAsync(req);
            }
            else
            {
                await ProtoApi.CreateKeyAsync(req);
            }

            IsKeyFormVisible = false;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            KeyFormError = $"Error: {ex.Message}";
        }
        finally
        {
            IsSavingKey = false;
        }
    }

    // — Issue key —
    [RelayCommand(CanExecute = nameof(HasSelectedInventoryItem))]
    public void ShowIssueForm()
    {
        // Close other inline forms first to avoid overlapping panels
        IsKeyFormVisible    = false;
        IsReturnFormVisible = false;
        ReturnFormError     = null;

        FormIssuedTo = string.Empty;
        FormIssuedAt = DateTime.Today.ToString("yyyy-MM-dd");
        FormIssueNotes = string.Empty;
        IssueFormError = null;
        IsIssueFormVisible = true;
    }

    [RelayCommand]
    public void CancelIssueForm()
    {
        IsIssueFormVisible = false;
        IssueFormError = null;
    }

    [RelayCommand(CanExecute = nameof(CanSaveIssue))]
    public async Task IssueKeyAsync()
    {
        if (SelectedInventoryItem == null) return;
        if (string.IsNullOrWhiteSpace(FormIssuedTo))
        {
            IssueFormError = "Issued To is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(FormIssuedAt))
        {
            IssueFormError = "Date is required.";
            return;
        }
        IsSavingIssue = true;
        IssueFormError = null;
        try
        {
            await ProtoApi.IssueKeyAsync(new ProtoKeyIssueRequest
            {
                KeyId = SelectedInventoryItem.Id,
                IssuedTo = FormIssuedTo,
                IssuedAt = FormIssuedAt,
                Notes = FormIssueNotes,
            });
            IsIssueFormVisible = false;
            await LoadDataAsync();
            SelectedTabIndex = 1; // switch to Issued Keys tab
        }
        catch (Exception ex)
        {
            IssueFormError = $"Error: {ex.Message}";
        }
        finally
        {
            IsSavingIssue = false;
        }
    }

    // — Return key —
    [RelayCommand(CanExecute = nameof(HasActiveIssuedItem))]
    public void ShowReturnForm()
    {
        if (SelectedIssuedItem == null) return;

        // Close other inline forms first to avoid overlapping panels
        IsKeyFormVisible   = false;
        IsIssueFormVisible = false;
        IssueFormError     = null;

        FormReturnDate  = DateTime.Today.ToString("yyyy-MM-dd");
        FormReturnNotes = string.Empty;
        ReturnFormError = null;
        IsReturnFormVisible = true;
    }

    [RelayCommand]
    public void CancelReturnForm()
    {
        IsReturnFormVisible = false;
        ReturnFormError     = null;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedIssuedItem))]
    public async Task ReturnKeyAsync()
    {
        if (SelectedIssuedItem == null) return;
        if (IsReturnFormVisible && string.IsNullOrWhiteSpace(FormReturnDate))
        {
            ReturnFormError = "Rückgabedatum ist erforderlich.";
            return;
        }
        IsSavingReturn = true;
        ReturnFormError = null;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var date = IsReturnFormVisible && !string.IsNullOrWhiteSpace(FormReturnDate)
                ? FormReturnDate
                : DateTime.Today.ToString("yyyy-MM-dd");
            var notes = IsReturnFormVisible ? FormReturnNotes : string.Empty;
            await ProtoApi.ReturnKeyAsync(new ProtoKeyReturnRequest
            {
                Id = SelectedIssuedItem.Id,
                ReturnedAt = date,
                Notes = notes,
            });
            IsReturnFormVisible = false;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading      = false;
            IsSavingReturn = false;
        }
    }

    /// <summary>Löscht einen Schlüssel-Inventartyp. Web-Pendant: KeysInventoryTrait::deleteInventory().</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedInventoryItem))]
    public async Task DeleteKeyAsync()
    {
        if (SelectedInventoryItem == null) return;
        var confirmed = await _dialogService.ConfirmAsync(
            "Schlüsseltyp löschen",
            $"Soll der Schlüsseltyp „{SelectedInventoryItem.Name}“ wirklich unwiderruflich gelöscht werden?");
        if (!confirmed) return;

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await ProtoApi.DeleteKeyAsync(new ProtoKeyDeleteRequest { Id = SelectedInventoryItem.Id });
            Inventory.Remove(SelectedInventoryItem);
            SelectedInventoryItem = null;
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

    private bool CanSaveKey() => !IsSavingKey;
    private bool CanSaveIssue() => !IsSavingIssue;
    private bool HasSelectedInventoryItem() => SelectedInventoryItem != null;
    private bool HasSelectedIssuedItem() => SelectedIssuedItem != null;
    private bool HasActiveIssuedItem() => SelectedIssuedItem?.ReturnedAt == null;

    partial void OnSelectedInventoryItemChanged(KeyInventoryItem? value)
    {
        ShowEditKeyFormCommand.NotifyCanExecuteChanged();
        ShowIssueFormCommand.NotifyCanExecuteChanged();
        DeleteKeyCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedIssuedItemChanged(KeyIssuedItem? value)
    {
        ReturnKeyCommand.NotifyCanExecuteChanged();
        ShowReturnFormCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSavingKeyChanged(bool value) => SaveKeyCommand.NotifyCanExecuteChanged();
    partial void OnIsSavingIssueChanged(bool value) => IssueKeyCommand.NotifyCanExecuteChanged();

    // ── CSV Export ────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanExportInventory))]
    public async Task ExportInventoryToCsvAsync()
    {
        var path = await _filePicker.PickSaveFileAsync(
            $"schluessel_inventar_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            new[] { ("CSV-Datei", "*.csv") });
        if (path == null) return;

        var columns = new (string, Func<KeyInventoryItem, string?>)[]
        {
            ("ID",          k => k.Id.ToString()),
            ("Name",        k => k.Name),
            ("Beschreibung", k => k.Description),
            ("Gesamt",      k => k.Total?.ToString()),
            ("Verfügbar",   k => k.Available?.ToString()),
        };

        try
        {
            CsvExportService.ExportToCsv(path, Inventory, columns);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"CSV-Export fehlgeschlagen: {ex.Message}";
        }
    }

    private bool CanExportInventory() => Inventory.Count > 0;

    partial void OnInventoryChanged(ObservableCollection<KeyInventoryItem>? oldValue, ObservableCollection<KeyInventoryItem> newValue)
    {
        if (oldValue != null)
            oldValue.CollectionChanged -= OnInventoryCollectionChanged;
        newValue.CollectionChanged += OnInventoryCollectionChanged;
        ExportInventoryToCsvCommand.NotifyCanExecuteChanged();
    }

    private void OnInventoryCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => ExportInventoryToCsvCommand.NotifyCanExecuteChanged();
}
