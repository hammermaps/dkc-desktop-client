using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DkcDesktopClient.App.ViewModels;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Protobuf;
using DkcDesktopClient.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DkcDesktopClient.UiTests;

/// <summary>
/// Klimasteuerung an ECHTEN Geräten (nur mit DKC_LIVE_KLIMA=1, z. B. sonntags): Einzelgerät,
/// Gruppe, Alle AUS/AN, Status speichern/wiederherstellen, Live-Abfrage. Jeder Schritt wird
/// unabhängig direkt am Controller geprüft (DKC_LIVE_KLIMA_READ_CMD gibt JSON
/// {adresse: {power, mode, set, fan}} aus). Am Ende wird der Ausgangszustand in jedem Fall
/// wiederhergestellt und erneut geprüft.
///   DKC_LIVE_KLIMA_DEVICE  Testgerät für Einzel-/Gruppenbefehle (Standard 7, „Leerstand“)
/// </summary>
public partial class LiveClientTests
{
    /// <param name="ModeAuto">rmiOperating.mode_auto: Betriebsart setzt das System selbst (nicht vergleichen).</param>
    private sealed record Ctl(bool Power, int Mode, double Set, int Fan, bool ModeAuto = false);

    private static readonly Dictionary<int, string> ModeNames = new() { [0] = "fan", [1] = "cooling", [2] = "heating", [3] = "dry", [4] = "auto", [6] = "auto" };
    private static readonly Dictionary<int, string> FanNames = new() { [0] = "low", [1] = "medium_low", [2] = "medium", [3] = "high", [6] = "auto" };

    private static int ModeKey(int code) => code == 6 ? 4 : code;
    /// <summary>Gleich wie <paramref name="b"/>; die Betriebsart nur, wenn sie bei b nicht automatisch ist.</summary>
    private static bool Same(Ctl a, Ctl b)
        => a.Power == b.Power && (b.ModeAuto || ModeKey(a.Mode) == ModeKey(b.Mode)) && Math.Abs(a.Set - b.Set) < 0.01 && a.Fan == b.Fan;

    private static async Task<Dictionary<int, Ctl>> ReadController()
    {
        var cmd = Env("DKC_LIVE_KLIMA_READ_CMD")!;
        using var p = Process.Start(new ProcessStartInfo("bash", new[] { "-c", cmd }) { RedirectStandardOutput = true, RedirectStandardError = true })!;
        var json = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        Assert.True(p.ExitCode == 0, "Controller nicht lesbar: " + await p.StandardError.ReadToEndAsync());
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        return raw.ToDictionary(kv => int.Parse(kv.Key), kv => new Ctl(
            kv.Value.GetProperty("power").GetBoolean(), kv.Value.GetProperty("mode").GetInt32(),
            kv.Value.GetProperty("set").GetDouble(), kv.Value.GetProperty("fan").GetInt32(),
            kv.Value.TryGetProperty("mode_auto", out var ma) && ma.GetBoolean()));
    }

    /// <summary>Wartet, bis der Controller für alle genannten Geräte den Erwartungen entspricht.</summary>
    private static async Task<(bool Ok, string Detail)> ExpectController(Func<int, Ctl, bool> ok, IEnumerable<int> addresses, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        var wanted = addresses.ToList();
        string detail = "";
        while (true)
        {
            var now = await ReadController();
            var bad = wanted.Where(a => !now.TryGetValue(a, out var c) || !ok(a, c)).ToList();
            detail = string.Join("; ", bad.Select(a => $"{a}: {(now.TryGetValue(a, out var c) ? c.ToString() : "fehlt")}"));
            if (bad.Count == 0) return (true, $"nach {sw.ElapsedMilliseconds} ms");
            if (sw.Elapsed > timeout) return (false, detail);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(1000);
        }
    }

    private static KlimaDeviceStatus AsStatus(int address, Ctl c, int controllerId = 1)
        => new(address, "", true, c.Power, ModeNames[c.Mode], c.Set, null, FanNames[c.Fan], null, controllerId);

    [AvaloniaFact]
    public async Task EchteApp_KlimasteuerungAnEchtenGeraeten()
    {
        var server = Env("DKC_LIVE_SERVER");
        Assert.SkipUnless(server != null && Env("DKC_LIVE_PASSWORD") != null && Env("DKC_LIVE_KLIMA") == "1"
                          && Env("DKC_LIVE_KLIMA_READ_CMD") != null,
            "Nur mit DKC_LIVE_KLIMA=1, DKC_LIVE_KLIMA_READ_CMD und Server-Zugang (schaltet echte Geräte)");
        var dev = int.Parse(Env("DKC_LIVE_KLIMA_DEVICE") ?? "7");
        var timeout = TimeSpan.FromSeconds(45);
        var timeoutAll = TimeSpan.FromSeconds(120); // Controller setzt Befehle nacheinander um (je ca. 4,5 s)
        var dataDir = Path.Combine(Path.GetTempPath(), "dkc-klima-" + Guid.NewGuid().ToString("N")[..8]);
        var problems = new List<string>();
        void Check(string step, (bool Ok, string Detail) r)
        {
            Log($"{(r.Ok ? "OK  " : "FEHL")} {step}: {r.Detail}");
            if (!r.Ok) problems.Add($"{step}: {r.Detail}");
        }

        var before = await ReadController();
        Log("Ausgangszustand: " + string.Join(", ", before.Select(kv => $"{kv.Key}={(kv.Value.Power ? "AN" : "aus")}/{kv.Value.Mode}/{kv.Value.Set}/{kv.Value.Fan}")));
        Assert.True(before.ContainsKey(dev), $"Testgerät {dev} nicht am Controller");

        var app = await StartAsync(dataDir);
        KlimaViewModel? vm = null;
        try
        {
            // ── Anmeldung und Klima-Ansicht ──────────────────────────────────
            Find<TextBox>(app.Window, "ServerUrlBox").Text = server;
            Find<TextBox>(app.Window, "UsernameBox").Text = Env("DKC_LIVE_USER") ?? "test";
            Find<TextBox>(app.Window, "PasswordBox").Text = Env("DKC_LIVE_PASSWORD");
            Dispatcher.UIThread.RunJobs();
            Click(app.Window, Find<Button>(app.Window, "LoginButton"));
            Assert.True(await WaitUntil(() => app.Vm.IsLoggedIn, TimeSpan.FromSeconds(30)), "Anmeldung fehlgeschlagen");

            var nav = app.Vm.NavItems.First(n => n.ViewModel is KlimaViewModel);
            vm = (KlimaViewModel)nav.ViewModel;
            app.Vm.SelectedNavItem = nav;
            Assert.True(await WaitUntil(() => vm.DeviceStatuses.Count > 0 && !vm.IsLoading, TimeSpan.FromSeconds(30)), "Klima-Status nicht geladen: " + vm.ErrorMessage);
            Screenshot(app.Window, "k01_klima");

            // Anzeige = Controller, Werte als Namen, die die Steuerung wieder annimmt
            var shown = vm.DeviceStatuses.ToList();
            var mismatch = shown.Where(s => !before.TryGetValue(s.Address, out var c) || !Same(ToCtl(s), c)).Select(s => s.Address).ToList();
            Check($"Anzeige ({shown.Count} Geräte) entspricht Controller", (mismatch.Count == 0, mismatch.Count == 0 ? "alle gleich" : "abweichend: " + string.Join(",", mismatch)));
            var badNames = shown.Where(s => !KlimaViewModel.ModeOptions.Contains(s.Mode ?? "") || !KlimaViewModel.FanSpeedOptions.Contains(s.FanSpeed ?? "")).ToList();
            Check("Modus/Lüfter als steuerbare Namen", (badNames.Count == 0, string.Join(", ", badNames.Select(s => $"{s.Address}:{s.Mode}/{s.FanSpeed}"))));
            var notShown = before.Keys.Except(shown.Select(s => s.Address)).ToList();
            if (notShown.Count > 0) Log($"Hinweis: am Controller, aber nicht in der Liste (Rechte/Konfiguration): {string.Join(",", notShown)}");

            // ── Live-Abfrage ─────────────────────────────────────────────────
            var ts = vm.LastStatusTimestamp;
            vm.StartPollingCommand.Execute(null);
            var polled = await WaitUntil(() => vm.LastStatusTimestamp != ts, TimeSpan.FromSeconds(25));
            vm.StopPollingCommand.Execute(null);
            Check("Live-Abfrage aktualisiert Stand", (polled && !vm.IsPolling, $"{ts} → {vm.LastStatusTimestamp}, läuft={vm.IsPolling}"));

            // ── Einzelgerät über das Steuerfeld ──────────────────────────────
            var orig = before[dev];
            async Task OpenPanel()
            {
                await vm.RefreshStatusAsync();
                vm.SelectedDeviceStatus = vm.DeviceStatuses.First(s => s.Address == dev);
                vm.ShowDeviceControlCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
            }
            await OpenPanel();
            Check("Steuerfeld mit aktuellem Zustand vorbelegt", (
                vm.ControlPower == orig.Power && vm.ControlMode == ModeNames[orig.Mode] && Math.Abs(vm.ControlSetpoint - orig.Set) < 0.01 && vm.ControlFanSpeed == FanNames[orig.Fan],
                $"{vm.ControlPower}/{vm.ControlMode}/{vm.ControlSetpoint}/{vm.ControlFanSpeed}"));

            vm.ControlPower = true; vm.ControlMode = "heating"; vm.ControlSetpoint = 22; vm.ControlFanSpeed = "low";
            Screenshot(app.Window, "k02_steuerfeld");
            await vm.ApplyDeviceControlCommand.ExecuteAsync(null);
            // Manche Geräte (z. B. Gerät 7) unterstützen keine eigenständige LOW-Stufe und melden
            // stattdessen MID2 (Fan=1) zurück – physische Einschränkung des M-Net-Gateways.
            Check($"Gerät {dev}: AN, Heizen, 22 °C, Lüfter niedrig (LOW oder MID2)", await ExpectController(
                (_, c) => c.Power && ModeKey(c.Mode) == 2 && Math.Abs(c.Set - 22) < 0.01 && c.Fan <= 1, [dev], timeout));
            Check("Kein Fehler im Steuerfeld", (vm.ControlError == null, vm.ControlError ?? ""));

            await OpenPanel();
            vm.ControlSetpoint = 23; vm.ControlFanSpeed = "medium_low";
            await vm.ApplyDeviceControlCommand.ExecuteAsync(null);
            Check($"Gerät {dev}: nur Sollwert 23 °C und Lüfter MID2 geändert", await ExpectController((_, c) => Same(c, new Ctl(true, 2, 23, 1)), [dev], timeout));
            await vm.RefreshStatusAsync();
            Screenshot(app.Window, "k03_geraet_geaendert");

            await OpenPanel();
            vm.ControlPower = orig.Power; vm.ControlMode = ModeNames[orig.Mode]; vm.ControlSetpoint = orig.Set; vm.ControlFanSpeed = FanNames[orig.Fan];
            await vm.ApplyDeviceControlCommand.ExecuteAsync(null);
            Check($"Gerät {dev}: Ausgangszustand über das Steuerfeld", await ExpectController((_, c) => Same(c, orig), [dev], timeout));

            // ── Gruppe (nur wenn das Testgerät allein in seiner Gruppe ist) ───
            var group = vm.Groups.FirstOrDefault(g => g.DeviceCount == 1
                && vm.Devices.Any(d => d.Address == dev && d.GroupId == g.Id));
            if (group == null)
            {
                Log($"Gruppentest übersprungen: Gerät {dev} ist nicht allein in einer Gruppe");
            }
            else
            {
                vm.SelectedGroup = group;
                vm.ShowGroupControlCommand.Execute(null);
                vm.ControlPower = true; vm.ControlMode = "cooling"; vm.ControlSetpoint = 20; vm.ControlFanSpeed = "medium";
                await vm.ApplyGroupControlCommand.ExecuteAsync(null);
                Check($"Gruppe {group.Id}: AN, Kühlen, 20 °C, Lüfter mittel", await ExpectController((_, c) => Same(c, new Ctl(true, 1, 20, 2)), [dev], timeout));
                Check("Kein Fehler bei der Gruppensteuerung", (vm.ControlError == null, vm.ControlError ?? ""));
                await OpenPanel();
                vm.ControlPower = orig.Power; vm.ControlMode = ModeNames[orig.Mode]; vm.ControlSetpoint = orig.Set; vm.ControlFanSpeed = FanNames[orig.Fan];
                await vm.ApplyDeviceControlCommand.ExecuteAsync(null);
                Check($"Gerät {dev}: Ausgangszustand nach Gruppentest", await ExpectController((_, c) => Same(c, orig), [dev], timeout));
            }

            // ── Alle AUS → Letzten Status ────────────────────────────────────
            var all = vm.DeviceStatuses.Select(s => s.Address).ToList();
            await vm.RefreshStatusAsync();
            vm.SaveStateCommand.Execute(null);
            await vm.AllDevicesOffCommand.ExecuteAsync(null);
            Log("Alle AUS: " + vm.GlobalControlResult);
            Check("Alle AUS", await ExpectController((_, c) => !c.Power, all, timeoutAll));
            Screenshot(app.Window, "k04_alle_aus");
            await vm.RestoreLastStateCommand.ExecuteAsync(null);
            Log("Letzten Status: " + vm.GlobalControlResult);
            Check("Letzten Status nach Alle AUS = Ausgangszustand", await ExpectController((a, c) => Same(c, before[a]), all, timeoutAll));

            // ── Alle AN → Letzten Status ─────────────────────────────────────
            await vm.AllDevicesOnCommand.ExecuteAsync(null);
            Log("Alle AN: " + vm.GlobalControlResult);
            Check("Alle AN (Betriebsart/Sollwert/Lüfter unverändert)", await ExpectController(
                (a, c) => c.Power && (before[a].ModeAuto || ModeKey(c.Mode) == ModeKey(before[a].Mode)) && Math.Abs(c.Set - before[a].Set) < 0.01 && c.Fan == before[a].Fan, all, timeoutAll));
            Screenshot(app.Window, "k05_alle_an");
            await vm.RestoreLastStateCommand.ExecuteAsync(null);
            Log("Letzten Status: " + vm.GlobalControlResult);
            Check("Letzten Status nach Alle AN = Ausgangszustand", await ExpectController((a, c) => Same(c, before[a]), all, timeoutAll));
            Screenshot(app.Window, "k06_wiederhergestellt");
        }
        finally
        {
            // Sicherheitsnetz: alles, was vom Ausgangszustand abweicht, gezielt zurückschreiben
            var now = await ReadController();
            var off = before.Where(kv => now.TryGetValue(kv.Key, out var c) && !Same(c, kv.Value)).ToList();
            if (off.Count > 0)
            {
                Log("Sicherheitsnetz stellt zurück: " + string.Join(", ", off.Select(kv => kv.Key)));
                var api = new DkcProtobufApi(app.Services.GetRequiredService<DkcApiFactory>()
                    .CreateProtobuf(app.Services.GetRequiredService<AuthService>().CurrentToken));
                foreach (var (address, target) in off)
                {
                    var req = KlimaViewModel.RestoreRequest(AsStatus(address, target), AsStatus(address, now[address]));
                    if (req != null) await api.ControlKlimaDeviceAsync(req);
                }
                var final = await ExpectController((a, c) => Same(c, before[a]), off.Select(kv => kv.Key), timeoutAll);
                Log($"Sicherheitsnetz: {(final.Ok ? "zurückgestellt" : "NICHT zurückgestellt: " + final.Detail)}");
                if (!final.Ok) problems.Add("ACHTUNG Endzustand weicht ab: " + final.Detail);
            }
            else
            {
                Log("Endzustand = Ausgangszustand (alle Geräte)");
            }
            await app.DisposeAsync();
        }

        Assert.True(problems.Count == 0, "Probleme:\n" + string.Join("\n", problems));
    }

    private static Ctl ToCtl(KlimaDeviceStatus s)
        => new(s.Power,
               ModeNames.FirstOrDefault(kv => kv.Value == s.Mode, new(-1, "")).Key,
               s.Setpoint ?? -1,
               FanNames.FirstOrDefault(kv => kv.Value == s.FanSpeed, new(-1, "")).Key);
}
