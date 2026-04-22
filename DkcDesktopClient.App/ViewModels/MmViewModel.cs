using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Services;

namespace DkcDesktopClient.App.ViewModels;

public partial class MmViewModel : ViewModelBase
{
    private readonly DkcApiFactory _apiFactory;
    private readonly AuthService _authService;
    private const int PageSize = 50;

    // List state
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private ObservableCollection<MmMessage> _messages = new();
    [ObservableProperty] private MmMessage? _selectedMessage;
    [ObservableProperty] private MmDetail? _selectedDetail;
    [ObservableProperty] private int? _filterStatus;
    [ObservableProperty] private string? _filterStreet;
    [ObservableProperty] private int _totalMessages;
    [ObservableProperty] private int _currentOffset;

    // Form state
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

    // Status/contractor quick-edit on detail
    [ObservableProperty] private int _detailStatus;
    [ObservableProperty] private string _detailNachunternehmer = string.Empty;

    public static IReadOnlyList<string> DringlichkeitOptions { get; } =
        new[] { "normal", "dringend", "notfall" };

    public MmViewModel(DkcApiFactory apiFactory, AuthService authService)
    {
        _apiFactory = apiFactory;
        _authService = authService;
    }

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
                status: FilterStatus,
                street: FilterStreet,
                limit: PageSize,
                offset: 0);
            Messages.Clear();
            TotalMessages = result.Total ?? 0;
            if (result.Success && result.Messages != null)
                foreach (var m in result.Messages)
                    Messages.Add(m);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading Maengelmeldungen: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
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
                DetailStatus = result.Message.Status;
                DetailNachunternehmer = result.Message.Nachunternehmer ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading detail: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Create
    [RelayCommand]
    public void ShowCreateForm()
    {
        IsEditingMessage = false;
        _editingUid = null;
        FormBetreff = string.Empty;
        FormMeldung = string.Empty;
        FormStreet = string.Empty;
        FormWhg = string.Empty;
        FormMelder = string.Empty;
        FormTel = string.Empty;
        FormEmail = string.Empty;
        FormDringlichkeit = "normal";
        FormNachunternehmer = string.Empty;
        FormZugeh = string.Empty;
        FormError = null;
        IsFormVisible = true;
    }

    // Edit
    [RelayCommand(CanExecute = nameof(HasSelectedDetail))]
    public void ShowEditForm()
    {
        if (SelectedDetail == null) return;
        IsEditingMessage = true;
        _editingUid = SelectedDetail.Uid;
        FormBetreff = SelectedDetail.Betreff ?? string.Empty;
        FormMeldung = SelectedDetail.MeldungMassage ?? string.Empty;
        FormStreet = SelectedDetail.Street ?? string.Empty;
        FormWhg = SelectedDetail.Whg ?? string.Empty;
        FormMelder = SelectedDetail.Melder ?? string.Empty;
        FormTel = SelectedDetail.Tel ?? string.Empty;
        FormEmail = SelectedDetail.Email ?? string.Empty;
        FormDringlichkeit = SelectedDetail.Dringlichkeit ?? "normal";
        FormNachunternehmer = SelectedDetail.Nachunternehmer ?? string.Empty;
        FormZugeh = SelectedDetail.Zugeh ?? string.Empty;
        FormError = null;
        IsFormVisible = true;
    }

    [RelayCommand]
    public void CancelForm()
    {
        IsFormVisible = false;
        FormError = null;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(FormBetreff))
        {
            FormError = "Betreff is required.";
            return;
        }
        IsSaving = true;
        FormError = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var req = new MmSaveRequest(
                FormBetreff,
                string.IsNullOrWhiteSpace(FormMeldung) ? null : FormMeldung,
                string.IsNullOrWhiteSpace(FormStreet) ? null : FormStreet,
                string.IsNullOrWhiteSpace(FormWhg) ? null : FormWhg,
                string.IsNullOrWhiteSpace(FormMelder) ? null : FormMelder,
                string.IsNullOrWhiteSpace(FormTel) ? null : FormTel,
                string.IsNullOrWhiteSpace(FormEmail) ? null : FormEmail,
                string.IsNullOrWhiteSpace(FormDringlichkeit) ? null : FormDringlichkeit,
                string.IsNullOrWhiteSpace(FormNachunternehmer) ? null : FormNachunternehmer,
                string.IsNullOrWhiteSpace(FormZugeh) ? null : FormZugeh);

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
                FormError = result.Error ?? "Save failed.";
            }
        }
        catch (Exception ex)
        {
            FormError = $"Error: {ex.Message}";
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
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.DeleteMmAsync(SelectedMessage.Uid);
            if (result.Success)
            {
                Messages.Remove(SelectedMessage);
                SelectedDetail = null;
                TotalMessages = Math.Max(0, TotalMessages - 1);
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

    [RelayCommand(CanExecute = nameof(HasSelectedDetail))]
    public async Task UpdateStatusAsync()
    {
        if (SelectedDetail == null) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.UpdateMmStatusAsync(SelectedDetail.Uid,
                new MmStatusUpdateRequest(DetailStatus, null));
            if (result.Success)
                await LoadDetailAsync();
            else
                ErrorMessage = result.Error ?? "Status update failed.";
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

    [RelayCommand(CanExecute = nameof(HasSelectedDetail))]
    public async Task AssignContractorAsync()
    {
        if (SelectedDetail == null || string.IsNullOrWhiteSpace(DetailNachunternehmer)) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.AssignMmContractorAsync(SelectedDetail.Uid,
                new MmAssignContractorRequest(DetailNachunternehmer));
            if (result.Success)
                await LoadDetailAsync();
            else
                ErrorMessage = result.Error ?? "Assign failed.";
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

    private bool CanSave() => !IsSaving;
    private bool HasSelectedMessage() => SelectedMessage != null;
    private bool HasSelectedDetail() => SelectedDetail != null;

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
