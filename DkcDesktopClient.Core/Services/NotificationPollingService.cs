using DkcDesktopClient.Core.Api;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DkcDesktopClient.Core.Services;

/// <summary>
/// Polling-based notification service that checks for new notifications every 60 seconds.
/// Acts as a replacement for Server-Sent Events.
/// - Maintains an internal thread-safe list of unread notifications.
/// - Raises <see cref="NewNotificationsReceived"/> when new unread items arrive.
/// - Raises <see cref="UnreadCountChanged"/> when the count changes.
/// - Pauses automatically when the user is not authenticated.
/// </summary>
public class NotificationPollingService : BackgroundService
{
    private readonly AuthService _authService;
    private readonly DkcApiFactory _apiFactory;
    private readonly ILogger<NotificationPollingService> _logger;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly List<NotificationItem> _unreadNotifications = new();
    private readonly object _notificationLock = new();

    /// <summary>Total unread count. Thread-safe.</summary>
    public int UnreadCount
    {
        get { lock (_notificationLock) return _unreadNotifications.Count; }
    }

    /// <summary>Returns a snapshot of the current unread notification list. Thread-safe.</summary>
    public IReadOnlyList<NotificationItem> GetUnreadSnapshot()
    {
        lock (_notificationLock)
            return _unreadNotifications.ToList();
    }

    /// <summary>
    /// Raised when new unread notifications are received.
    /// The argument is the list of brand-new items.
    /// </summary>
    public event EventHandler<IReadOnlyList<NotificationItem>>? NewNotificationsReceived;

    /// <summary>Raised whenever the unread count changes. Argument is the new count.</summary>
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
    /// Marks a notification as read locally (removes it from the internal list).
    /// Raises <see cref="UnreadCountChanged"/>. Callers in the UI should marshal accordingly.
    /// </summary>
    public void MarkAsRead(int notificationId)
    {
        int newCount;
        lock (_notificationLock)
        {
            var item = _unreadNotifications.FirstOrDefault(n => n.Id == notificationId);
            if (item == null) return;
            _unreadNotifications.Remove(item);
            newCount = _unreadNotifications.Count;
        }
        UnreadCountChanged?.Invoke(this, newCount);
    }

    /// <summary>Marks all current notifications as read locally.</summary>
    public void MarkAllAsRead()
    {
        lock (_notificationLock)
            _unreadNotifications.Clear();
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

            List<NotificationItem> brandNew;
            int updatedCount;
            lock (_notificationLock)
            {
                // Detect genuinely new items (not already in the collection)
                var existingIds = _unreadNotifications.Select(n => n.Id).ToHashSet();
                brandNew = newUnread.Where(n => !existingIds.Contains(n.Id)).ToList();

                // Remove items that are no longer unread on the server
                var serverUnreadIds = newUnread.Select(n => n.Id).ToHashSet();
                _unreadNotifications.RemoveAll(n => !serverUnreadIds.Contains(n.Id));

                // Add genuinely new items
                _unreadNotifications.AddRange(brandNew);
                updatedCount = _unreadNotifications.Count;
            }

            if (brandNew.Count > 0)
            {
                _logger.LogInformation("{Count} new notification(s) received", brandNew.Count);
                NewNotificationsReceived?.Invoke(this, brandNew);
            }

            // Always raise the count event so subscribers stay in sync
            UnreadCountChanged?.Invoke(this, updatedCount);
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
