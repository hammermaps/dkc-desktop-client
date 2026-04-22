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

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private ObservableCollection<MmMessage> _messages = new();
    [ObservableProperty] private MmMessage? _selectedMessage;
    [ObservableProperty] private MmDetail? _selectedDetail;
    [ObservableProperty] private int? _filterStatus;
    [ObservableProperty] private string? _filterStreet;
    [ObservableProperty] private int _totalMessages;
    [ObservableProperty] private int _currentOffset;

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
            if (result.Success)
                SelectedDetail = result.Message;
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

    partial void OnSelectedMessageChanged(MmMessage? value)
    {
        if (value != null)
            _ = LoadDetailAsync();
    }
}
