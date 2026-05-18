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
    private readonly BackgroundRefreshService _backgroundRefreshService;
    private bool _refreshingFromBackground;

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
    [ObservableProperty] private string? _globalControlResult;

    private List<KlimaDeviceStatus>? _savedState;

    public static IReadOnlyList<string> ModeOptions { get; } =
        new[] { "cooling", "heating", "fan", "auto", "dry" };
    public static IReadOnlyList<string> FanSpeedOptions { get; } =
        new[] { "auto", "low", "medium", "high" };

    private CancellationTokenSource? _pollCts;

    public KlimaViewModel(
        DkcApiFactory apiFactory,
        AuthService authService,
        BackgroundRefreshService backgroundRefreshService)
    {
        _apiFactory = apiFactory;
        _authService = authService;
        _backgroundRefreshService = backgroundRefreshService;
        _backgroundRefreshService.DataRefreshed += OnDataRefreshed;
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var devicesTask = api.GetKlimaDevicesAsync();
            var groupsTask = api.GetKlimaGroupsAsync();
            await Task.WhenAll(devicesTask, groupsTask);

            var devicesResult = devicesTask.Result;
            var groupsResult = groupsTask.Result;

            if (!devicesResult.Success)
            {
                ErrorMessage = $"Error loading devices: {devicesResult.Error ?? "Unknown error"}";
            }
            else if (devicesResult.Devices != null)
            {
                Devices.Clear();
                foreach (var d in devicesResult.Devices)
                    Devices.Add(d);
            }

            if (!groupsResult.Success)
            {
                var groupError = $"Error loading groups: {groupsResult.Error ?? "Unknown error"}";
                ErrorMessage = ErrorMessage != null ? $"{ErrorMessage}; {groupError}" : groupError;
                // Fallback: build groups from the already-loaded device list
                var grouped = Devices
                    .Where(d => d.GroupId.HasValue)
                    .GroupBy(d => d.GroupId!.Value)
                    .OrderBy(g => g.Key);
                Groups.Clear();
                foreach (var g in grouped)
                    Groups.Add(new KlimaGroup(g.Key, $"Gruppe {g.Key}", g.Count()));
            }
            else if (groupsResult.Groups != null && groupsResult.Groups.Count > 0)
            {
                Groups.Clear();
                foreach (var g in groupsResult.Groups)
                    Groups.Add(g);
            }
            else
            {
                // API returned success but empty groups — build from device group IDs
                var grouped = Devices
                    .Where(d => d.GroupId.HasValue)
                    .GroupBy(d => d.GroupId!.Value)
                    .OrderBy(g => g.Key);
                Groups.Clear();
                foreach (var g in grouped)
                    Groups.Add(new KlimaGroup(g.Key, $"Gruppe {g.Key}", g.Count()));
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading climate data: {ex.GetBaseException().Message}";
        }
        finally
        {
            IsLoading = false;
        }

        _backgroundRefreshService.NotifyUserActivity(CacheKeys.KlimaStatus);
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
                if (!_refreshingFromBackground)
                    _backgroundRefreshService.NotifyUserActivity(CacheKeys.KlimaStatus);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Status refresh error: {ex.Message}";
        }
    }

    private void OnDataRefreshed(object? sender, string key)
    {
        if (key != CacheKeys.KlimaStatus || IsPolling)
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            _refreshingFromBackground = true;
            try
            {
                await RefreshStatusAsync();
            }
            finally
            {
                _refreshingFromBackground = false;
            }
        });
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

    [RelayCommand(CanExecute = nameof(HasSelectedGroup))]
    public void ShowGroupControl()
    {
        if (SelectedGroup == null) return;
        ControlPower = true;
        ControlMode = "cooling";
        ControlSetpoint = 22.0;
        ControlFanSpeed = "auto";
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

    partial void OnSelectedGroupChanged(KlimaGroup? value)
    {
        ApplyGroupControlCommand.NotifyCanExecuteChanged();
        ShowGroupControlCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSendingControlChanged(bool value) =>
        ApplyDeviceControlCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    public async Task AllDevicesOnAsync()
    {
        IsSendingControl = true;
        GlobalControlResult = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            foreach (var s in DeviceStatuses)
            {
                await api.ControlKlimaDeviceAsync(new KlimaDeviceControlRequest(
                    s.Address, true, ControlMode, ControlSetpoint, ControlFanSpeed));
            }
            GlobalControlResult = $"Alle {DeviceStatuses.Count} Geräte eingeschaltet.";
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            GlobalControlResult = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsSendingControl = false;
        }
    }

    [RelayCommand]
    public async Task AllDevicesOffAsync()
    {
        IsSendingControl = true;
        GlobalControlResult = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            foreach (var s in DeviceStatuses)
            {
                await api.ControlKlimaDeviceAsync(new KlimaDeviceControlRequest(
                    s.Address, false, null, null, null));
            }
            GlobalControlResult = $"Alle {DeviceStatuses.Count} Geräte ausgeschaltet.";
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            GlobalControlResult = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsSendingControl = false;
        }
    }

    [RelayCommand]
    public void SaveState()
    {
        _savedState = new List<KlimaDeviceStatus>(DeviceStatuses);
        GlobalControlResult = $"Status gespeichert ({_savedState.Count} Geräte).";
    }

    [RelayCommand]
    public async Task RestoreLastStateAsync()
    {
        if (_savedState == null || _savedState.Count == 0)
        {
            GlobalControlResult = "Kein gespeicherter Status vorhanden.";
            return;
        }
        IsSendingControl = true;
        GlobalControlResult = null;
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            foreach (var s in _savedState)
            {
                await api.ControlKlimaDeviceAsync(new KlimaDeviceControlRequest(
                    s.Address, s.Power, s.Mode, s.Setpoint, s.FanSpeed));
            }
            GlobalControlResult = $"Letzter Status auf {_savedState.Count} Geräten wiederhergestellt.";
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            GlobalControlResult = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsSendingControl = false;
        }
    }
}
