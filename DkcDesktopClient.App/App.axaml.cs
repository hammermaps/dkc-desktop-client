using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DkcDesktopClient.App.Services;
using DkcDesktopClient.App.ViewModels;
using DkcDesktopClient.App.Views;
using DkcDesktopClient.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

namespace DkcDesktopClient.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // ── Tray icon events ──────────────────────────────────────────────────────

    private void OnTrayIconClicked(object? sender, EventArgs e)
        => BringMainWindowToFront();

    private void OnTrayOpenClicked(object? sender, EventArgs e)
        => BringMainWindowToFront();

    private void OnTrayExitClicked(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private static void BringMainWindowToFront()
    {
        if (Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } win)
        {
            win.Show();
            win.BringIntoView();
            win.Activate();
            if (win.WindowState == WindowState.Minimized)
                win.WindowState = WindowState.Normal;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Start hosted background services
        var bgRefresh    = _serviceProvider.GetRequiredService<BackgroundRefreshService>();
        var notifPoll    = _serviceProvider.GetRequiredService<NotificationPollingService>();
        var connectivity = _serviceProvider.GetRequiredService<ConnectivityService>();
        _ = bgRefresh.StartAsync(CancellationToken.None);
        _ = notifPoll.StartAsync(CancellationToken.None);
        _ = connectivity.StartAsync(CancellationToken.None);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = _serviceProvider.GetRequiredService<MainWindowViewModel>();
            var splash = new SplashWindow();

            // Use the splash as the startup window so it appears immediately.
            // The MainWindow is created but not shown yet.
            desktop.MainWindow = splash;
            splash.Opened += async (_, _) =>
            {
                splash.SetStatus("Auto-Login wird geprüft…");
                var mainWindow = new MainWindow { DataContext = mainVm };
                desktop.MainWindow = mainWindow;
                await mainVm.InitializeAsync();
                mainWindow.Show();
                splash.Close();
            };

            desktop.ShutdownRequested += (_, _) =>
            {
                _ = bgRefresh.StopAsync(CancellationToken.None);
                _ = notifPoll.StopAsync(CancellationToken.None);
                _ = connectivity.StopAsync(CancellationToken.None);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DkcDesktopClient");
        Directory.CreateDirectory(dataDir);

        var cacheDir = Path.Combine(dataDir, "cache");
        var logPath  = Path.Combine(dataDir, "logs", "dkc-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        services.AddLogging(b => b.AddSerilog(Log.Logger, dispose: true));

        services.AddHttpClient();

        services.AddDataProtection()
            .PersistKeysToFileSystem(new System.IO.DirectoryInfo(Path.Combine(dataDir, "keys")));

        // ── Core infrastructure services ──────────────────────────────────────
        services.AddSingleton<TokenStore>();
        services.AddSingleton<DkcApiFactory>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<DataCacheService>(sp =>
            new DataCacheService(
                sp.GetRequiredService<ILogger<DataCacheService>>(),
                cacheDir));

        services.AddSingleton<AuthService>(sp =>
        {
            var factory    = sp.GetRequiredService<DkcApiFactory>();
            var tokenStore = sp.GetRequiredService<TokenStore>();
            var logger     = sp.GetRequiredService<ILogger<AuthService>>();
            var svc        = new AuthService(factory, tokenStore, logger);
            factory.SetAuthService(svc);
            return svc;
        });

        // ── RefreshConfig (default values; could be loaded from appsettings) ──
        services.AddSingleton<IOptions<RefreshConfig>>(
            _ => Options.Create(new RefreshConfig()));

        // ── Background / polling services ─────────────────────────────────────
        services.AddSingleton<BackgroundRefreshService>();
        services.AddSingleton<NotificationPollingService>();

        services.AddSingleton<ConnectivityService>();

        // ── App-layer services ────────────────────────────────────────────────
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();

        // ── ViewModels ────────────────────────────────────────────────────────
        services.AddTransient<LoginViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<NeaViewModel>();
        services.AddTransient<MmViewModel>();
        services.AddTransient<BuildingViewModel>();
        services.AddTransient<KlimaViewModel>();
        services.AddTransient<KeysViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<WlsViewModel>();
        services.AddTransient<NotificationsViewModel>();
        services.AddTransient<MainWindowViewModel>();
    }
}
