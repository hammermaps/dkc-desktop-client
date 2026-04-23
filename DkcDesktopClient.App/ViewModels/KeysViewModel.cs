using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Services;

namespace DkcDesktopClient.App.ViewModels;

public partial class KeysViewModel : ViewModelBase
{
    private readonly DkcApiFactory _apiFactory;
    private readonly AuthService _authService;

    // List state
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private ObservableCollection<KeyInventoryItem> _inventory = new();
    [ObservableProperty] private KeyInventoryItem? _selectedInventoryItem;
    [ObservableProperty] private ObservableCollection<KeyIssuedItem> _issuedKeys = new();
    [ObservableProperty] private KeyIssuedItem? _selectedIssuedItem;
    [ObservableProperty] private int _selectedTabIndex;

    // Key create/edit form
    [ObservableProperty] private bool _isKeyFormVisible;
    [ObservableProperty] private bool _isSavingKey;
    [ObservableProperty] private string? _keyFormError;
    [ObservableProperty] private bool _isEditingKey;
    private int? _editingKeyId;

    [ObservableProperty] private string _formKeyName = string.Empty;
    [ObservableProperty] private string _formKeyDescription = string.Empty;
    [ObservableProperty] private int _formKeyTotal = 1;

    // Issue form
    [ObservableProperty] private bool _isIssueFormVisible;
    [ObservableProperty] private bool _isSavingIssue;
    [ObservableProperty] private string? _issueFormError;

    [ObservableProperty] private string _formIssuedTo = string.Empty;
    [ObservableProperty] private string _formIssuedAt = string.Empty;
    [ObservableProperty] private string _formIssueNotes = string.Empty;

    public KeysViewModel(DkcApiFactory apiFactory, AuthService authService)
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
            var inventoryTask = api.GetKeysInventoryAsync();
            var issuedTask = api.GetKeysIssuedAsync();
            await Task.WhenAll(inventoryTask, issuedTask);

            Inventory.Clear();
            if (inventoryTask.Result.Success && inventoryTask.Result.Keys != null)
                foreach (var k in inventoryTask.Result.Keys)
                    Inventory.Add(k);

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
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading keys data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // — Key inventory CRUD —
    [RelayCommand]
    public void ShowCreateKeyForm()
    {
        IsEditingKey = false;
        _editingKeyId = null;
        FormKeyName = string.Empty;
        FormKeyDescription = string.Empty;
        FormKeyTotal = 1;
        KeyFormError = null;
        IsKeyFormVisible = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedInventoryItem))]
    public void ShowEditKeyForm()
    {
        if (SelectedInventoryItem == null) return;
        IsEditingKey = true;
        _editingKeyId = SelectedInventoryItem.Id;
        FormKeyName = SelectedInventoryItem.Name ?? string.Empty;
        FormKeyDescription = SelectedInventoryItem.Description ?? string.Empty;
        FormKeyTotal = SelectedInventoryItem.Total ?? 1;
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
        IsSavingKey = true;
        KeyFormError = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var req = new KeyInventorySaveRequest(
                FormKeyName,
                string.IsNullOrWhiteSpace(FormKeyDescription) ? null : FormKeyDescription,
                FormKeyTotal);

            ApiError result;
            if (IsEditingKey && _editingKeyId.HasValue)
            {
                result = await api.UpdateKeyAsync(_editingKeyId.Value, req);
            }
            else
            {
                var cr = await api.CreateKeyAsync(req);
                result = new ApiError(cr.Success, cr.Error);
            }

            if (result.Success)
            {
                IsKeyFormVisible = false;
                await LoadDataAsync();
            }
            else
            {
                KeyFormError = result.Error ?? "Save failed.";
            }
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
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.IssueKeyAsync(new KeyIssueRequest(
                SelectedInventoryItem.Id,
                FormIssuedTo,
                FormIssuedAt,
                string.IsNullOrWhiteSpace(FormIssueNotes) ? null : FormIssueNotes));
            if (result.Success)
            {
                IsIssueFormVisible = false;
                await LoadDataAsync();
                SelectedTabIndex = 1; // switch to Issued Keys tab
            }
            else
            {
                IssueFormError = result.Error ?? "Issue failed.";
            }
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
    [RelayCommand(CanExecute = nameof(HasSelectedIssuedItem))]
    public async Task ReturnKeyAsync()
    {
        if (SelectedIssuedItem == null) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.ReturnKeyAsync(SelectedIssuedItem.Id,
                new KeyReturnRequest(DateTime.Today.ToString("yyyy-MM-dd"), null));
            if (result.Success)
                await LoadDataAsync();
            else
                ErrorMessage = result.Error ?? "Return failed.";
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

    [RelayCommand(CanExecute = nameof(HasSelectedIssuedItem))]
    public async Task DeleteIssuedAsync()
    {
        if (SelectedIssuedItem == null) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.DeleteKeyIssuedAsync(SelectedIssuedItem.Id);
            if (result.Success)
            {
                IssuedKeys.Remove(SelectedIssuedItem);
                SelectedIssuedItem = null;
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

    private bool CanSaveKey() => !IsSavingKey;
    private bool CanSaveIssue() => !IsSavingIssue;
    private bool HasSelectedInventoryItem() => SelectedInventoryItem != null;
    private bool HasSelectedIssuedItem() => SelectedIssuedItem != null;

    partial void OnSelectedInventoryItemChanged(KeyInventoryItem? value)
    {
        ShowEditKeyFormCommand.NotifyCanExecuteChanged();
        ShowIssueFormCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedIssuedItemChanged(KeyIssuedItem? value)
    {
        ReturnKeyCommand.NotifyCanExecuteChanged();
        DeleteIssuedCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSavingKeyChanged(bool value) => SaveKeyCommand.NotifyCanExecuteChanged();
    partial void OnIsSavingIssueChanged(bool value) => IssueKeyCommand.NotifyCanExecuteChanged();
}
