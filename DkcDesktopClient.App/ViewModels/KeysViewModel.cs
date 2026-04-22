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

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private ObservableCollection<KeyInventoryItem> _inventory = new();
    [ObservableProperty] private ObservableCollection<KeyIssuedItem> _issuedKeys = new();
    [ObservableProperty] private int _selectedTabIndex;

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
            if (issuedTask.Result.Success && issuedTask.Result.Keys != null)
                foreach (var k in issuedTask.Result.Keys)
                    IssuedKeys.Add(k);
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
}
