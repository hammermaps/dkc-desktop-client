using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DkcDesktopClient.App.ViewModels;
using DkcDesktopClient.App.Views;
using DkcDesktopClient.Core.Pruefung;
using Moq;
using Xunit;

namespace DkcDesktopClient.UiTests;

/// <summary>
/// Bedient die gemeinsame Prüfungsansicht (Aufzug/Sprinkler) wie ein Benutzer: Anlage wählen,
/// Prüfung öffnen, Checkliste ausfüllen, abschließen. Die Domäne ist ein Stub, es gibt keinen Server.
/// </summary>
public class PruefungViewTests
{
    private static PruefungChecklistStepData Step(string id, string title, string type = "checkbox", bool required = true, string value = "", params string[] options)
        => new(id, "", title, "Beschreibung " + title, type, options, required, value, "");

    private static Mock<IPruefungApi> Domain(string inspectionStatus = PruefungValues.StatusInProgress)
    {
        var d = new Mock<IPruefungApi>();
        d.SetupGet(x => x.Title).Returns("Aufzugprüfung");
        d.SetupGet(x => x.NummerLabel).Returns("Aufzug-Nr.");
        d.SetupGet(x => x.ZugehoerigkeitOptions).Returns(new[] { "haus", "gw" });
        d.SetupGet(x => x.ViewPermission).Returns("aufzug_pruefung_view");
        d.SetupGet(x => x.EditPermission).Returns("aufzug_pruefung_edit");
        d.SetupGet(x => x.AdminPermission).Returns("aufzug_pruefung_admin");
        d.Setup(x => x.GetSystemsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new PruefungSystem(9, "Aufzug Haus 3", "A-3", 3, "Haus 3", "haus", true, "2026-11-10", "passed"),
            new PruefungSystem(10, "Aufzug Haus 4", "A-4", 4, "Haus 4", "haus", true, "", ""),
        });
        d.Setup(x => x.GetDashboardAsync(It.IsAny<CancellationToken>()))
         .ReturnsAsync(new PruefungDashboard(2, 1, 4, 0, new[] { new PruefungOverdue(10, "Aufzug Haus 4", 3, "") }));
        d.Setup(x => x.GetInspectionsAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync(new PruefungInspectionPage(1, new[]
         {
             new PruefungInspection(145, 9, "Aufzug Haus 3", "quarterly_check", "2026-11-14 09:00:00", "test", inspectionStatus, "", 0, ""),
         }));
        d.Setup(x => x.GetInspectionDetailAsync(145, It.IsAny<CancellationToken>()))
         .ReturnsAsync(new PruefungInspectionDetail(145, 9, "Aufzug Haus 3", "quarterly_check", "2026-11-14 09:00:00", "test",
             inspectionStatus, "", 0, "", "", "", new[]
             {
                 Step("notruf", "Notrufanlage funktionsfähig"),
                 Step("tueren", "Schachttür-Verriegelungen", "select", true, "", "OK", "Mangel", "Nicht prüfbar"),
                 Step("bemerkung", "Sonstiges", "text", required: false),
             }));
        return d;
    }

    private static (Window Window, PruefungViewModel Vm) Show(Mock<IPruefungApi> domain, params string[] permissions)
    {
        var vm = new AufzugPruefungViewModel(domain.Object, p => permissions.Contains(p),
            _ => Task.FromResult<IReadOnlyList<HausOption>>(new[] { new HausOption(3, "Haus 3"), new HausOption(4, "Haus 4") }));
        var window = new Window { Width = 1400, Height = 820, Content = new PruefungView { DataContext = vm } };
        window.Show();
        return (window, vm);
    }

    private static async Task Settle()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static T Find<T>(Window w, string name) where T : Control
        => w.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
           ?? throw new Xunit.Sdk.XunitException($"{typeof(T).Name} '{name}' nicht gefunden");

    /// <summary>Echter Mausklick auf die Button-Mitte (löst wie beim Benutzer das Command aus).</summary>
    private static void Click(Button b)
    {
        Assert.True(b.IsEffectivelyVisible, $"Button '{b.Name}' ist nicht sichtbar");
        Assert.True(b.IsEffectivelyEnabled, $"Button '{b.Name}' ist deaktiviert");
        var w = (Window)TopLevel.GetTopLevel(b)!;
        b.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var center = b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), w)
                     ?? throw new Xunit.Sdk.XunitException($"Button '{b.Name}' hat keine Position");
        w.MouseDown(center, MouseButton.Left);
        w.MouseUp(center, MouseButton.Left);
    }

    private static void Screenshot(Window w, string name)
    {
        var dir = Environment.GetEnvironmentVariable("DKC_UI_SCREENSHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        w.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png"));
    }

    private static List<ComboBox> ChecklistCombos(Window w)
        => Find<ItemsControl>(w, "ChecklistList").GetVisualDescendants().OfType<ComboBox>().Where(c => c.IsVisible).ToList();

    [AvaloniaFact]
    public async Task Pruefer_FuelltChecklisteAusUndSchliesstAb()
    {
        var domain = Domain();
        IReadOnlyList<PruefungChecklistValue>? saved = null;
        domain.Setup(x => x.SaveChecklistAsync(145, It.IsAny<IEnumerable<PruefungChecklistValue>>(), It.IsAny<CancellationToken>()))
              .Callback<int, IEnumerable<PruefungChecklistValue>, CancellationToken>((_, v, _) => saved = v.ToList())
              .Returns(Task.CompletedTask);
        var (w, vm) = Show(domain, "aufzug_pruefung_view", "aufzug_pruefung_edit");

        await vm.LoadAsync();
        await Settle();
        Assert.Equal("Aufzugprüfung", Find<TextBlock>(w, "TitleText").Text);
        Assert.Contains("2 Anlagen", Find<TextBlock>(w, "SummaryText").Text);
        Assert.Contains("1 fällig", Find<TextBlock>(w, "SummaryText").Text);
        Assert.False(Find<Button>(w, "NewSystemButton").IsVisible);   // kein Admin-Recht
        Assert.True(Find<Button>(w, "StartInspectionButton").IsVisible);

        Find<DataGrid>(w, "SystemsGrid").SelectedIndex = 0;
        await Settle();
        Find<DataGrid>(w, "InspectionsGrid").SelectedIndex = 0;
        await Settle();
        Assert.Contains("Aufzug Haus 3", Find<TextBlock>(w, "DetailTitle").Inlines!.Text);

        var combos = ChecklistCombos(w);
        Assert.Equal(2, combos.Count);                                  // Checkbox + Auswahl, Text als TextBox
        Assert.Equal(new[] { "In Ordnung", "Nicht in Ordnung" }, combos[0].Items.Cast<string>());
        Assert.Equal(new[] { "OK", "Mangel", "Nicht prüfbar" }, combos[1].Items.Cast<string>());

        // Pflichtpunkt offen → Abschluss wird verweigert, nichts gesendet
        combos[0].SelectedItem = "Nicht in Ordnung";
        await Settle();
        Click(Find<Button>(w, "CompleteButton"));
        await Settle();
        Assert.Contains("Pflichtpunkt", Find<TextBlock>(w, "ErrorText").Text);
        domain.Verify(x => x.CompleteInspectionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // Mängelnotiz erscheint bei „Nicht in Ordnung“
        var note = Find<ItemsControl>(w, "ChecklistList").GetVisualDescendants().OfType<TextBox>()
            .First(t => t.PlaceholderText == "Mängelbeschreibung" && t.IsEffectivelyVisible);
        note.Text = "Notruftaste klemmt";
        combos[1].SelectedItem = "OK";
        await Settle();
        Screenshot(w, "pruefung_ausgefuellt");

        Click(Find<Button>(w, "CompleteButton"));
        await Settle();

        Assert.NotNull(saved);
        Assert.Contains(saved!, v => v.StepId == "notruf" && v.Value == "Nicht in Ordnung" && v.Note == "Notruftaste klemmt");
        Assert.Contains(saved!, v => v.StepId == "tueren" && v.Value == "OK");
        Assert.DoesNotContain(saved!, v => v.StepId == "bemerkung");     // leer → nicht gesendet
        // Mangel vorhanden → Ergebnis wird als „nicht bestanden“ vorgeschlagen
        domain.Verify(x => x.CompleteInspectionAsync(145, PruefungValues.ResultFailed, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("abgeschlossen", Find<TextBlock>(w, "StatusText").Text);
        Screenshot(w, "pruefung_abgeschlossen");
    }

    [AvaloniaFact]
    public async Task NurLeserecht_ChecklisteIstGesperrt()
    {
        var (w, vm) = Show(Domain(), "aufzug_pruefung_view");
        await vm.LoadAsync();
        await Settle();
        Find<DataGrid>(w, "SystemsGrid").SelectedIndex = 0;
        await Settle();
        Find<DataGrid>(w, "InspectionsGrid").SelectedIndex = 0;
        await Settle();

        Assert.False(Find<Button>(w, "StartInspectionButton").IsVisible);
        Assert.False(Find<ItemsControl>(w, "ChecklistList").IsEffectivelyEnabled);
        Assert.False(Find<Button>(w, "CompleteButton").IsEffectivelyVisible);
        Screenshot(w, "pruefung_nur_lesen");
    }

    [AvaloniaFact]
    public async Task AbgeschlossenePruefung_BleibtUnveraenderbar()
    {
        var (w, vm) = Show(Domain(PruefungValues.StatusCompleted), "aufzug_pruefung_edit");
        await vm.LoadAsync();
        await Settle();
        Find<DataGrid>(w, "InspectionsGrid").SelectedIndex = 0;
        await Settle();

        Assert.True(vm.HasDetail);
        Assert.False(Find<ItemsControl>(w, "ChecklistList").IsEffectivelyEnabled);
        Assert.False(Find<Button>(w, "CompleteButton").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task Admin_LegtAnlageMitHausAn()
    {
        var domain = Domain();
        PruefungSystemInput? input = null;
        domain.Setup(x => x.SaveSystemAsync(It.IsAny<PruefungSystemInput>(), It.IsAny<CancellationToken>()))
              .Callback<PruefungSystemInput, CancellationToken>((i, _) => input = i)
              .ReturnsAsync(11);
        var (w, vm) = Show(domain, "aufzug_pruefung_admin");
        await vm.LoadAsync();
        await Settle();

        Click(Find<Button>(w, "NewSystemButton"));
        await Settle();
        Assert.True(Find<Border>(w, "SystemForm").IsVisible);

        // Ohne Haus → Fehlermeldung, kein Speichern
        Find<TextBox>(w, "FormNameBox").Text = "Aufzug Neu";
        Click(Find<Button>(w, "SaveSystemButton"));
        await Settle();
        Assert.Null(input);
        Assert.Equal("Bitte ein Haus wählen.", vm.SystemFormError);

        Find<ComboBox>(w, "FormHausBox").SelectedIndex = 1;
        await Settle();
        Screenshot(w, "anlage_formular");
        Click(Find<Button>(w, "SaveSystemButton"));
        await Settle();

        Assert.Equal(new PruefungSystemInput(0, "Aufzug Neu", "", 4, "haus", true), input);
        Assert.False(Find<Border>(w, "SystemForm").IsVisible);
    }
}
