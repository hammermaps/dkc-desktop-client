namespace DkcDesktopClient.Core.Services;

/// <summary>
/// Configuration for background refresh intervals per data type.
/// Bind to "RefreshConfig" section in appsettings or configure via code.
/// </summary>
public class RefreshConfig
{
    /// <summary>Interval for Klima realtime status (default: 30 s – highest priority).</summary>
    public TimeSpan KlimaStatus { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Interval for Dashboard statistics (default: 60 s).</summary>
    public TimeSpan DashboardStats { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Interval for Notification count polling (default: 60 s).</summary>
    public TimeSpan NotificationCount { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Interval for MM list refresh (default: 2 min).</summary>
    public TimeSpan MmList { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Interval for NEA inspection status (default: 5 min).</summary>
    public TimeSpan NeaInspections { get; set; } = TimeSpan.FromMinutes(5);
}
