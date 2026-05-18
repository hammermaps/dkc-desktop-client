using System.IO;
using Avalonia;
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

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Start hosted background services
        var bgRefresh = _serviceProvider.GetRequiredService<BackgroundRefreshService>();
        var notifPoll = _serviceProvider.GetRequiredService<NotificationPollingService>();
        _ = bgRefresh.StartAsync(CancellationToken.None);
        _ = notifPoll.StartAsync(CancellationToken.None);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = _serviceProvider.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = mainVm };
            desktop.ShutdownRequested += (_, _) =>
            {
                _ = bgRefresh.StopAsync(CancellationToken.None);
                _ = notifPoll.StopAsync(CancellationToken.None);
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

        // ── App-layer services ────────────────────────────────────────────────
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();

        // ── ViewModels ────────────────────────────────────────────────────────
        services.AddTransient<LoginViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<NeaViewModel>();
        services.AddTransient<MmViewModel>();
        services.AddTransient<BuildingViewModel>();
        services.AddTransient<KlimaViewModel>();
        services.AddTransient<KeysViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<MainWindowViewModel>();
    }
}
