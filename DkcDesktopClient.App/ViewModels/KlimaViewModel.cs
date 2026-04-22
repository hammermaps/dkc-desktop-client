using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Services;

namespace DkcDesktopClient.App.ViewModels;

public partial class KlimaViewModel : ViewModelBase
{
    private readonly DkcApiFactory _apiFactory;
    private readonly AuthService _authService;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private ObservableCollection<KlimaDevice> _devices = new();

    public KlimaViewModel(DkcApiFactory apiFactory, AuthService authService)
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
            var result = await api.GetKlimaDevicesAsync();
            Devices.Clear();
            if (result.Success && result.Devices != null)
                foreach (var d in result.Devices)
                    Devices.Add(d);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading climate devices: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
