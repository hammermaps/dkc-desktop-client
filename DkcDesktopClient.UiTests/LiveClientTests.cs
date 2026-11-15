using System.Collections;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using DkcDesktopClient.App;
using DkcDesktopClient.App.ViewModels;
using DkcDesktopClient.App.Views;
using DkcDesktopClient.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DkcDesktopClient.UiTests;

/// <summary>
/// End-to-End gegen einen laufenden ProxyServer mit der echten App (alle Dienste, echtes
/// Hauptfenster), aber eigenem Datenordner. Läuft nur mit Umgebungsvariablen:
///   DKC_LIVE_SERVER       z. B. http://127.0.0.1:8099 (nie gegen Produktion!)
///   DKC_LIVE_USER / DKC_LIVE_PASSWORD   Testkonto
///   DKC_LIVE_MAINTENANCE_CMD  optional: Skript mit Argument allowed|blocked|off, das den
///                             Wartungsmodus des Servers umschaltet (sonst entfällt dieser Teil)
/// Ergebnisse stehen im Testprotokoll (TestContext), Screenshots mit DKC_UI_SCREENSHOTS.
/// </summary>
public class LiveClientTests
{
    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    private static void Log(string line) => TestContext.Current.SendDiagnosticMessage(line);

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return true;
            await Task.Delay(50);
        }
        Dispatcher.UIThread.RunJobs();
        return condition();
    }

    private static object? Prop(object o, string name) => o.GetType().GetProperty(name)?.GetValue(o);

    /// <summary>Summe aller sichtbaren Listen eines ViewModels (Anzahl geladener Einträge).</summary>
    private static int Items(object vm) => vm.GetType().GetProperties()
        .Where(p => typeof(ICollection).IsAssignableFrom(p.PropertyType) && p.Name != "NavItems")
        .Sum(p => (p.GetValue(vm) as ICollection)?.Count ?? 0);

    private static void Screenshot(Window w, string name)
    {
        var dir = Env("DKC_UI_SCREENSHOTS");
        if (dir == null) return;
        Directory.CreateDirectory(dir);
        Dispatcher.UIThread.RunJobs();
        w.CaptureRenderedFrame()?.Save(Path.Combine(dir, "live_" + name + ".png"));
    }

    private static void Click(Window w, Button b)
    {
        var p = b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), w)!.Value;
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
    }

    private static T Find<T>(Control root, string name) where T : Control
        => root.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
           ?? throw new Xunit.Sdk.XunitException($"{typeof(T).Name} '{name}' nicht gefunden");

    private static async Task Maintenance(string mode)
    {
        var cmd = Env("DKC_LIVE_MAINTENANCE_CMD")!;
        using var p = Process.Start(new ProcessStartInfo("bash", new[] { cmd, mode }) { RedirectStandardOutput = true })!;
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        Log($"[Server] {output.Trim()}");
        Assert.Equal(0, p.ExitCode);
    }

    private sealed class RunningApp : IAsyncDisposable
    {
        public required ServiceProvider Services { get; init; }
        public required MainWindowViewModel Vm { get; init; }
        public required Window Window { get; init; }
        public async ValueTask DisposeAsync()
        {
            Window.Close();
            foreach (var s in new Microsoft.Extensions.Hosting.IHostedService[]
                     {
                         Services.GetRequiredService<BackgroundRefreshService>(),
                         Services.GetRequiredService<ChangeFeedService>(),
                         Services.GetRequiredService<ConnectivityService>(),
                         Services.GetRequiredService<NotificationPollingService>(),
                     })
                await s.StopAsync(CancellationToken.None);
            await Services.DisposeAsync();
        }
    }

    /// <summary>Startet die App wie App.OnFrameworkInitializationCompleted (ohne Splash).</summary>
    private static async Task<RunningApp> StartAsync(string dataDir)
    {
        var services = new ServiceCollection();
        App.App.ConfigureServices(services, dataDir);
        var sp = services.BuildServiceProvider();
        await sp.GetRequiredService<BackgroundRefreshService>().StartAsync(CancellationToken.None);
        await sp.GetRequiredService<NotificationPollingService>().StartAsync(CancellationToken.None);
        await sp.GetRequiredService<ConnectivityService>().StartAsync(CancellationToken.None);
        await sp.GetRequiredService<ChangeFeedService>().StartAsync(CancellationToken.None);
        var vm = sp.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow { DataContext = vm, Width = 1500, Height = 900 };
        window.Show();
        await vm.InitializeAsync();
        Dispatcher.UIThread.RunJobs();
        return new RunningApp { Services = sp, Vm = vm, Window = window };
    }

    /// <summary>Öffnet einen Navigationspunkt und wartet, bis die Ansicht geladen hat.</summary>
    private static async Task<(string? Error, int Items, long Ms)> OpenAsync(RunningApp app, NavItem item)
    {
        var sw = Stopwatch.StartNew();
        app.Vm.SelectedNavItem = item;
        await Task.Delay(150);
        await WaitUntil(() => Prop(item.ViewModel, "IsLoading") is not true, TimeSpan.FromSeconds(30));
        await Task.Delay(300); // nachgelagerte Ladevorgänge (Details, zweite Liste)
        await WaitUntil(() => Prop(item.ViewModel, "IsLoading") is not true, TimeSpan.FromSeconds(30));
        return (Prop(item.ViewModel, "ErrorMessage") as string, Items(item.ViewModel), sw.ElapsedMilliseconds);
    }

    [AvaloniaFact]
    public async Task EchteApp_AnmeldungAnsichtenVorladenNeustartUndWartung()
    {
        var server = Env("DKC_LIVE_SERVER");
        Assert.SkipUnless(server != null && Env("DKC_LIVE_PASSWORD") != null, "DKC_LIVE_SERVER/DKC_LIVE_PASSWORD nicht gesetzt");
        var dataDir = Path.Combine(Path.GetTempPath(), "dkc-live-" + Guid.NewGuid().ToString("N")[..8]);
        var problems = new List<string>();

        try
        {
            // ── 1. Anmeldung über die Login-Maske ───────────────────────────────
            var app = await StartAsync(dataDir);
            Assert.False(app.Vm.IsLoggedIn);
            // Vom Hintergrunddienst vorgeladene Daten (unabhängig von der Cache-Lebensdauer)
            var preloaded = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
            app.Services.GetRequiredService<BackgroundRefreshService>().DataRefreshed += (_, key) => preloaded[key] = 1;
            Find<TextBox>(app.Window, "ServerUrlBox").Text = server;
            Find<TextBox>(app.Window, "UsernameBox").Text = Env("DKC_LIVE_USER") ?? "test";
            Find<TextBox>(app.Window, "PasswordBox").Text = Env("DKC_LIVE_PASSWORD");
            Dispatcher.UIThread.RunJobs();
            Screenshot(app.Window, "01_login");
            var sw = Stopwatch.StartNew();
            Click(app.Window, Find<Button>(app.Window, "LoginButton"));
            Assert.True(await WaitUntil(() => app.Vm.IsLoggedIn, TimeSpan.FromSeconds(30)), "Anmeldung fehlgeschlagen: " + app.Vm.LoginViewModel.ErrorMessage);
            Log($"Anmeldung: {sw.ElapsedMilliseconds} ms, Benutzer {app.Vm.UserDisplayName}, {app.Vm.NavItems.Count} Navigationspunkte: {string.Join(", ", app.Vm.NavItems.Select(n => n.Title))}");
            await WaitUntil(() => app.Vm.DashboardViewModel.IsLoading is false, TimeSpan.FromSeconds(30));
            Screenshot(app.Window, "02_dashboard");

            // ── 2. Vorladen: nach der Anmeldung, bevor eine Ansicht geöffnet wird ─
            var cache = app.Services.GetRequiredService<DataCacheService>();
            var preloadKeys = new[] { CacheKeys.KlimaStatus, CacheKeys.DashboardData, CacheKeys.NeaDashboard, CacheKeys.MmList,
                CacheKeys.KeysInventory, CacheKeys.NeaInspections, CacheKeys.NeaSystems, CacheKeys.BuildingList,
                CacheKeys.KlimaDevices, CacheKeys.WlsBuildings, CacheKeys.Notifications, CacheKeys.NotificationCount };
            bool Loaded(string k) => preloaded.ContainsKey(k) || cache.IsValid(k);
            sw.Restart();
            await WaitUntil(() => preloadKeys.All(Loaded), TimeSpan.FromSeconds(40));
            var missing = preloadKeys.Where(k => !Loaded(k)).ToList();
            Log($"Vorladen: {preloadKeys.Length - missing.Count}/{preloadKeys.Length} Datenarten nach {sw.ElapsedMilliseconds} ms" + (missing.Count > 0 ? $", fehlt: {string.Join(", ", missing)}" : ""));
            if (missing.Count > 0) problems.Add("Nicht vorgeladen: " + string.Join(", ", missing));

            // ── 3. Jede Ansicht öffnen und neu laden ────────────────────────────
            foreach (var item in app.Vm.NavItems.ToList())
            {
                var (error, items, ms) = await OpenAsync(app, item);
                Log($"Ansicht {item.Title,-20} {ms,6} ms, {items,5} Einträge{(error != null ? ", FEHLER: " + error : "")}");
                if (error != null) problems.Add($"{item.Title}: {error}");
                Screenshot(app.Window, "10_" + item.Title.ToLowerInvariant());

                app.Vm.RefreshCurrentModuleCommand.Execute(null);
                await Task.Delay(150);
                await WaitUntil(() => Prop(item.ViewModel, "IsLoading") is not true, TimeSpan.FromSeconds(30));
                var reloadError = Prop(item.ViewModel, "ErrorMessage") as string;
                if (reloadError != null && reloadError != error) problems.Add($"{item.Title} (Neu laden): {reloadError}");
            }

            // Dashboard aus dem Cache: zweiter Aufruf ohne Netzwerkwartezeit
            sw.Restart();
            await app.Vm.DashboardViewModel.LoadDataCommand.ExecuteAsync(null);
            Log($"Dashboard aus Cache: {sw.ElapsedMilliseconds} ms");

            // ── 4. Neustart: automatische Anmeldung mit gespeichertem Token ─────
            await app.DisposeAsync();
            sw.Restart();
            app = await StartAsync(dataDir);
            Log($"Neustart: angemeldet={app.Vm.IsLoggedIn} nach {sw.ElapsedMilliseconds} ms (Auto-Login)");
            if (!app.Vm.IsLoggedIn) problems.Add("Auto-Login nach Neustart fehlgeschlagen");
            Screenshot(app.Window, "20_neustart");

            // ── 5. Wartungsmodus ────────────────────────────────────────────────
            if (Env("DKC_LIVE_MAINTENANCE_CMD") != null && app.Vm.IsLoggedIn)
            {
                var nea = app.Vm.NavItems.First(n => n.ViewModel is NeaViewModel);
                await Maintenance("allowed");
                await OpenAsync(app, nea);
                await WaitUntil(() => app.Vm.IsMaintenanceAllowed, TimeSpan.FromSeconds(10));
                Log($"Wartung (zugelassen): Banner={app.Vm.IsMaintenanceAllowed}, Fehler={Prop(nea.ViewModel, "ErrorMessage") ?? "-"}, Text: {app.Vm.MaintenanceText}");
                if (!app.Vm.IsMaintenanceAllowed) problems.Add("Wartung (zugelassen): kein Hinweis");
                if (Prop(nea.ViewModel, "ErrorMessage") != null) problems.Add("Wartung (zugelassen): Ansicht meldet Fehler");
                Screenshot(app.Window, "30_wartung_zugelassen");

                await Maintenance("blocked");
                app.Vm.RefreshCurrentModuleCommand.Execute(null);
                await WaitUntil(() => app.Vm.IsMaintenanceBlocked, TimeSpan.FromSeconds(15));
                await WaitUntil(() => Prop(nea.ViewModel, "IsLoading") is not true, TimeSpan.FromSeconds(15));
                Log($"Wartung (gesperrt): Banner={app.Vm.IsMaintenanceBlocked}, angemeldet={app.Vm.IsLoggedIn}, Fehler={Prop(nea.ViewModel, "ErrorMessage") ?? "-"}");
                if (!app.Vm.IsMaintenanceBlocked) problems.Add("Wartung (gesperrt): kein Hinweis");
                if (!app.Vm.IsLoggedIn) problems.Add("Wartung (gesperrt): Benutzer wurde abgemeldet");
                if (Prop(nea.ViewModel, "ErrorMessage") is string blockedError && !blockedError.Contains("Wartungsmodus"))
                    problems.Add("Wartung (gesperrt): technische statt lesbarer Meldung: " + blockedError);
                Screenshot(app.Window, "31_wartung_gesperrt");

                await Maintenance("off");
                sw.Restart();
                // Keine Benutzeraktion: der Hintergrunddienst muss das Ende selbst bemerken.
                var recovered = await WaitUntil(() => !app.Vm.IsMaintenanceBlocked && !app.Vm.IsMaintenanceAllowed, TimeSpan.FromSeconds(75));
                // Die Ansicht lädt nach dem Ende selbstständig neu (Rechte, Navigation, aktuelle Ansicht).
                await WaitUntil(() => Prop(nea.ViewModel, "ErrorMessage") == null && Prop(nea.ViewModel, "IsLoading") is not true, TimeSpan.FromSeconds(20));
                if (Prop(nea.ViewModel, "ErrorMessage") is string stale) problems.Add("Nach Wartungsende weiter Fehler: " + stale);
                Log($"Wartung beendet: erkannt={recovered} nach {sw.ElapsedMilliseconds} ms, Fehler={Prop(nea.ViewModel, "ErrorMessage") ?? "-"}, {app.Vm.NavItems.Count} Navigationspunkte");
                if (!recovered) problems.Add("Ende der Wartung nicht selbstständig erkannt");
                Screenshot(app.Window, "32_wartung_beendet");
                await Maintenance("allowed");
            }

            await app.Vm.LogoutCommand.ExecuteAsync(null);
            await app.DisposeAsync();
        }
        finally
        {
            try { Directory.Delete(dataDir, true); } catch (IOException) { }
        }

        Assert.True(problems.Count == 0, "Probleme:\n" + string.Join("\n", problems));
    }
}
