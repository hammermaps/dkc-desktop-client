using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Services;

namespace DkcDesktopClient.App.ViewModels;

public partial class NotificationsViewModel : ViewModelBase
{
    private readonly DkcApiFactory _apiFactory;
    private readonly AuthService _authService;
    private readonly NotificationPollingService _pollingService;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private ObservableCollection<NotificationItem> _notifications = new();
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private bool _showUnreadOnly;

    public bool HasNoNotifications => !IsLoading && Notifications.Count == 0;

    public NotificationsViewModel(
        DkcApiFactory apiFactory,
        AuthService authService,
        NotificationPollingService pollingService)
    {
        _apiFactory     = apiFactory;
        _authService    = authService;
        _pollingService = pollingService;

        _pollingService.UnreadCountChanged += OnUnreadCountChanged;
    }

    private void OnUnreadCountChanged(object? sender, int count)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => TotalCount = count);
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(HasNoNotifications));

    [RelayCommand]
    public async Task LoadNotificationsAsync()
    {
        IsLoading    = true;
        ErrorMessage = null;
        try
        {
            var api    = _apiFactory.Create(_authService.CurrentToken);
            var result = await api.GetNotificationsAsync();
            Notifications.Clear();
            TotalCount = result.Count;
            if (result.Success && result.Notifications != null)
            {
                var items = ShowUnreadOnly
                    ? result.Notifications.Where(n => !n.Read)
                    : result.Notifications;
                foreach (var n in items)
                    Notifications.Add(n);
            }
            else if (!result.Success)
            {
                ErrorMessage = result.Error ?? "Laden der Benachrichtigungen fehlgeschlagen.";
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

    partial void OnShowUnreadOnlyChanged(bool value) => _ = LoadNotificationsAsync();
}
