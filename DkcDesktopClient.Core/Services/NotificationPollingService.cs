using System.Collections.ObjectModel;
using DkcDesktopClient.Core.Api;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DkcDesktopClient.Core.Services;

/// <summary>
/// Polling-based notification service that checks for new notifications every 60 seconds.
/// Acts as a replacement for Server-Sent Events.
/// - Maintains an <see cref="UnreadNotifications"/> collection bound to the sidebar badge.
/// - Raises <see cref="NewNotificationsReceived"/> when new unread items arrive.
/// - Pauses automatically when the user is not authenticated.
/// </summary>
public class NotificationPollingService : BackgroundService
{
    private readonly AuthService _authService;
    private readonly DkcApiFactory _apiFactory;
    private readonly ILogger<NotificationPollingService> _logger;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    /// <summary>Current set of unread notification items. Updated on every poll.</summary>
    public ObservableCollection<NotificationItem> UnreadNotifications { get; } = new();

    /// <summary>Total unread count (mirrors <see cref="UnreadNotifications"/>.Count).</summary>
    public int UnreadCount => UnreadNotifications.Count;

    /// <summary>
    /// Raised when new unread notifications are received.
    /// The integer argument is the new unread count.
    /// </summary>
    public event EventHandler<IReadOnlyList<NotificationItem>>? NewNotificationsReceived;

    /// <summary>Raised whenever the unread count changes.</summary>
    public event EventHandler<int>? UnreadCountChanged;

    public NotificationPollingService(
        AuthService authService,
        DkcApiFactory apiFactory,
        ILogger<NotificationPollingService> logger)
    {
        _authService = authService;
        _apiFactory  = apiFactory;
        _logger      = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NotificationPollingService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_authService.IsAuthenticated)
            {
                await PollAsync(stoppingToken);
            }

            await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("NotificationPollingService stopped");
    }

    /// <summary>Forces an immediate poll (e.g. after the user opens the notifications view).</summary>
    public Task ForceRefreshAsync(CancellationToken ct = default)
        => PollAsync(ct);

    /// <summary>
    /// Marks a notification as read locally (removes it from <see cref="UnreadNotifications"/>).
    /// Does not currently call the API because the notification endpoint is session-based;
    /// future backend changes can enable full read-marking here.
    /// </summary>
    public void MarkAsRead(int notificationId)
    {
        var item = UnreadNotifications.FirstOrDefault(n => n.Id == notificationId);
        if (item == null) return;

        UnreadNotifications.Remove(item);
        UnreadCountChanged?.Invoke(this, UnreadNotifications.Count);
    }

    /// <summary>Marks all current notifications as read locally.</summary>
    public void MarkAllAsRead()
    {
        UnreadNotifications.Clear();
        UnreadCountChanged?.Invoke(this, 0);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task PollAsync(CancellationToken ct)
    {
        try
        {
            var api = _apiFactory.Create(_authService.CurrentToken);
            var response = await api.GetNotificationsAsync(ct);

            if (!response.Success || response.Notifications == null)
                return;

            var newUnread = response.Notifications
                .Where(n => !n.Read)
                .ToList();

            // Detect genuinely new items (not already in the collection)
            var existingIds = UnreadNotifications.Select(n => n.Id).ToHashSet();
            var brandNew    = newUnread.Where(n => !existingIds.Contains(n.Id)).ToList();

            // Remove items that are no longer unread on the server
            var serverUnreadIds = newUnread.Select(n => n.Id).ToHashSet();
            var toRemove = UnreadNotifications.Where(n => !serverUnreadIds.Contains(n.Id)).ToList();
            foreach (var item in toRemove)
                UnreadNotifications.Remove(item);

            // Add genuinely new items
            foreach (var item in brandNew)
                UnreadNotifications.Add(item);

            if (brandNew.Count > 0)
            {
                _logger.LogInformation("{Count} new notification(s) received", brandNew.Count);
                NewNotificationsReceived?.Invoke(this, brandNew);
            }

            if (toRemove.Count > 0 || brandNew.Count > 0)
                UnreadCountChanged?.Invoke(this, UnreadNotifications.Count);
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notification poll failed");
        }
    }
}
