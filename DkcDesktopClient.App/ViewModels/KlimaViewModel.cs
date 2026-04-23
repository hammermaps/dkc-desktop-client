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

    // Device list
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private ObservableCollection<KlimaDevice> _devices = new();
    [ObservableProperty] private KlimaDevice? _selectedDevice;

    // Realtime status
    [ObservableProperty] private bool _isPolling;
    [ObservableProperty] private ObservableCollection<KlimaDeviceStatus> _deviceStatuses = new();
    [ObservableProperty] private KlimaDeviceStatus? _selectedDeviceStatus;
    [ObservableProperty] private string? _lastStatusTimestamp;

    // Groups
    [ObservableProperty] private ObservableCollection<KlimaGroup> _groups = new();
    [ObservableProperty] private KlimaGroup? _selectedGroup;

    // Control panel
    [ObservableProperty] private bool _isControlPanelVisible;
    [ObservableProperty] private bool _controlPower;
    [ObservableProperty] private string _controlMode = "cooling";
    [ObservableProperty] private double _controlSetpoint = 22.0;
    [ObservableProperty] private string _controlFanSpeed = "auto";
    [ObservableProperty] private bool _isSendingControl;
    [ObservableProperty] private string? _controlError;

    public static IReadOnlyList<string> ModeOptions { get; } =
        new[] { "cooling", "heating", "fan", "auto", "dry" };
    public static IReadOnlyList<string> FanSpeedOptions { get; } =
        new[] { "auto", "low", "medium", "high" };

    private CancellationTokenSource? _pollCts;

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
            var devicesResult = await api.GetKlimaDevicesAsync();

            Devices.Clear();
            if (devicesResult.Success && devicesResult.Devices != null)
                foreach (var d in devicesResult.Devices)
                    Devices.Add(d);

            Groups.Clear();
            var grouped = Devices
                .Where(d => d.GroupId.HasValue)
                .GroupBy(d => d.GroupId!.Value)
                .OrderBy(g => g.Key);
            foreach (var g in grouped)
                Groups.Add(new KlimaGroup(g.Key, $"Gruppe {g.Key}", g.Count()));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading climate data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RefreshStatusAsync()
    {
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.GetKlimaRealtimeStatusAsync();
            if (result.Success && result.Devices != null)
            {
                DeviceStatuses.Clear();
                foreach (var s in result.Devices)
                    DeviceStatuses.Add(s);
                LastStatusTimestamp = result.Timestamp;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Status refresh error: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartPolling))]
    public void StartPolling()
    {
        _pollCts = new CancellationTokenSource();
        IsPolling = true;
        _ = RunPollingAsync(_pollCts.Token);
    }

    [RelayCommand(CanExecute = nameof(CanStopPolling))]
    public void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts = null;
        IsPolling = false;
    }

    private async Task RunPollingAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await RefreshStatusAsync();
                }
                catch (Exception ex)
                {
                    ErrorMessage = $"Polling error: {ex.Message}";
                }
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsPolling = false;
        }
    }

    // — Device control —
    [RelayCommand(CanExecute = nameof(HasSelectedDeviceStatus))]
    public void ShowDeviceControl()
    {
        if (SelectedDeviceStatus == null) return;
        ControlPower = SelectedDeviceStatus.Power;
        ControlMode = SelectedDeviceStatus.Mode ?? "cooling";
        ControlSetpoint = SelectedDeviceStatus.Setpoint ?? 22.0;
        ControlFanSpeed = SelectedDeviceStatus.FanSpeed ?? "auto";
        ControlError = null;
        IsControlPanelVisible = true;
    }

    [RelayCommand]
    public void HideDeviceControl()
    {
        IsControlPanelVisible = false;
        ControlError = null;
    }

    [RelayCommand(CanExecute = nameof(CanSendControl))]
    public async Task ApplyDeviceControlAsync()
    {
        if (SelectedDeviceStatus == null) return;
        IsSendingControl = true;
        ControlError = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.ControlKlimaDeviceAsync(new KlimaDeviceControlRequest(
                SelectedDeviceStatus.Address,
                ControlPower,
                ControlMode,
                ControlSetpoint,
                ControlFanSpeed));
            if (result.Success)
            {
                IsControlPanelVisible = false;
                await RefreshStatusAsync();
            }
            else
            {
                ControlError = result.Error ?? "Control command failed.";
            }
        }
        catch (Exception ex)
        {
            ControlError = $"Error: {ex.Message}";
        }
        finally
        {
            IsSendingControl = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedGroup))]
    public async Task ApplyGroupControlAsync()
    {
        if (SelectedGroup == null) return;
        IsSendingControl = true;
        ControlError = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.ControlKlimaGroupAsync(new KlimaGroupControlRequest(
                SelectedGroup.Id,
                ControlPower,
                ControlMode,
                ControlSetpoint,
                ControlFanSpeed));
            if (result.Success)
                await RefreshStatusAsync();
            else
                ControlError = result.Error ?? "Group control failed.";
        }
        catch (Exception ex)
        {
            ControlError = $"Error: {ex.Message}";
        }
        finally
        {
            IsSendingControl = false;
        }
    }

    private bool CanStartPolling() => !IsPolling;
    private bool CanStopPolling() => IsPolling;
    private bool HasSelectedDeviceStatus() => SelectedDeviceStatus != null;
    private bool HasSelectedGroup() => SelectedGroup != null;
    private bool CanSendControl() => !IsSendingControl && SelectedDeviceStatus != null;

    partial void OnIsPollingChanged(bool value)
    {
        StartPollingCommand.NotifyCanExecuteChanged();
        StopPollingCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedDeviceStatusChanged(KlimaDeviceStatus? value)
    {
        ShowDeviceControlCommand.NotifyCanExecuteChanged();
        ApplyDeviceControlCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedGroupChanged(KlimaGroup? value) =>
        ApplyGroupControlCommand.NotifyCanExecuteChanged();

    partial void OnIsSendingControlChanged(bool value) =>
        ApplyDeviceControlCommand.NotifyCanExecuteChanged();
}
