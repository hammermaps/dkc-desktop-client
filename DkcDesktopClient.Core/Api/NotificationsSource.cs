using DkcDesktopClient.Core.Protobuf;
using DkcDesktopClient.Core.Services;

namespace DkcDesktopClient.Core.Api;

/// <summary>
/// Benachrichtigungen über Protobuf (NOTIFICATIONS/NOTIFICATION_COUNT). Der REST-Endpunkt
/// <c>api.php?action=notifications</c> ist auf xhr.php umgezogen (HTTP 410) und kennt keine
/// dkc_-Tokens – der Desktop-Client erhielt darüber nie Benachrichtigungen. Liefert das bisherige
/// REST-Modell, damit Ansicht, Polling und Cache unverändert bleiben.
/// </summary>
public static class NotificationsSource
{
    public static async Task<NotificationsResponse> GetAsync(DkcApiFactory factory, string? token, CancellationToken ct = default)
    {
        var r = await new DkcProtobufApi(factory.CreateProtobuf(token)).GetNotificationsAsync(ct).ConfigureAwait(false);
        return Map(r);
    }

    public static async Task<NotificationCountResponse> GetCountAsync(DkcApiFactory factory, string? token, CancellationToken ct = default)
    {
        var r = await new DkcProtobufApi(factory.CreateProtobuf(token)).GetNotificationCountAsync(ct).ConfigureAwait(false);
        return new NotificationCountResponse(r.Count, true);
    }

    public static NotificationsResponse Map(Protocol.NotificationsResponse r) => new(
        Success: true,
        Authenticated: r.Authenticated,
        Count: r.Count,
        Notifications: r.Notifications
            .Select(n => new NotificationItem(n.Id, n.Type, n.Title, n.Message, n.Read, n.CreatedAt))
            .ToList(),
        Error: null);
}
