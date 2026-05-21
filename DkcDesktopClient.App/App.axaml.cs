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
using Serilog.Events;
using System.Text.Json;

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
        var logDir = ResolveLogDirectory(dataDir);
        var logPath = Path.Combine(logDir, "dkc-.log");

        var environment =
            Environment.GetEnvironmentVariable("DKC_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
#if DEBUG
            ?? "Development";
#else
            ?? "Production";
#endif

        var configuredLevel = ResolveLogLevel(dataDir, environment);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(configuredLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Environment", environment)
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({Environment}) {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("Logging initialized with level {LogLevel} in {Environment}. Log path: {LogPath}", configuredLevel, environment, logPath);

        services.AddLogging(b => b.AddSerilog(Log.Logger, dispose: true));

        services.AddHttpClient();
        
        // ── HTTP Performance Konfiguration ────────────────────────────────────────
        services.AddOptions<HttpPerformanceConfig>()
            .Configure(config =>
            {
                // Lade aus Umgebungsvariablen oder verwende Standardwerte
                if (int.TryParse(Environment.GetEnvironmentVariable("DKC_HTTP_REQUEST_TIMEOUT"), out var requestTimeout))
                    config.RequestTimeoutSeconds = requestTimeout;
                if (int.TryParse(Environment.GetEnvironmentVariable("DKC_HTTP_CONNECT_TIMEOUT"), out var connectTimeout))
                    config.ConnectTimeoutSeconds = connectTimeout;
                if (int.TryParse(Environment.GetEnvironmentVariable("DKC_HTTP_MAX_CONNECTIONS"), out var maxConnections))
                    config.MaxConnectionsPerServer = maxConnections;
            });

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

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

    private static LogEventLevel ResolveLogLevel(string dataDir, string environment)
    {
        var envLevel = Environment.GetEnvironmentVariable("DKC_LOG_LEVEL");
        if (TryParseLogLevel(envLevel, out var parsedFromEnv))
            return parsedFromEnv;

        var configPath = Path.Combine(dataDir, "logging.json");
        if (TryLoadLogLevelFromFile(configPath, out var parsedFromFile))
            return parsedFromFile;

        // Development soll standardmaessig maximal sichtbar loggen.
        return environment.Equals("Development", StringComparison.OrdinalIgnoreCase)
            ? LogEventLevel.Debug
            : LogEventLevel.Information;
    }

    private static bool TryLoadLogLevelFromFile(string configPath, out LogEventLevel level)
    {
        level = default;
        if (!File.Exists(configPath))
            return false;

        try
        {
            using var stream = File.OpenRead(configPath);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("logLevel", out var levelElement))
                return false;

            return TryParseLogLevel(levelElement.GetString(), out level);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseLogLevel(string? rawLevel, out LogEventLevel level)
    {
        level = default;
        if (string.IsNullOrWhiteSpace(rawLevel))
            return false;

        return Enum.TryParse(rawLevel.Trim(), ignoreCase: true, out level);
    }

    private static string ResolveLogDirectory(string dataDir)
    {
        const string centralLogDir = "/logs";
        var fallbackLogDir = Path.Combine(dataDir, "logs");

        try
        {
            Directory.CreateDirectory(centralLogDir);
            if (CanWriteToDirectory(centralLogDir))
                return centralLogDir;
        }
        catch
        {
            // Kein Zugriff auf /logs -> Fallback unten.
        }

        Directory.CreateDirectory(fallbackLogDir);
        return fallbackLogDir;
    }

    private static bool CanWriteToDirectory(string directory)
    {
        try
        {
            var probePath = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}.tmp");
            using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1,
                FileOptions.DeleteOnClose);
            stream.WriteByte(0x1);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
