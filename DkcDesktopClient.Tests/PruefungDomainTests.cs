using DkcDesktopClient.App.ViewModels;
using DkcDesktopClient.Core.Protobuf;
using DkcDesktopClient.Core.Protocol;
using DkcDesktopClient.Core.Pruefung;
using DkcDesktopClient.Core.Services;
using Moq;
using Xunit;

namespace DkcDesktopClient.Tests;

/// <summary>Domänen-Adapter Aufzug/Sprinkler, Snapshot-Abgleich, Passwort- und Historien-Hilfen.</summary>
public class PruefungDomainTests
{
    private static PruefungChecklistStep Step(string id, string type = "checkbox", string value = "", bool required = true, string note = "")
    {
        var s = new PruefungChecklistStep { Id = id, Title = id.ToUpperInvariant(), Type = type, Required = required, Value = value, DefectNote = note };
        if (type == "select") s.Options.AddRange(new[] { "OK", "Mangel" });
        return s;
    }

    [Fact]
    public async Task Aufzug_MapsSystemsAndDetailWithSteps()
    {
        var api = new Mock<IDkcProtobufApi>();
        var systems = new AufzugSystemsResponse();
        systems.Systems.Add(new AufzugSystem { Id = 9, Name = "Aufzug A", AufzugNummer = "A-1", HausId = 3, HausName = "Haus 3", Zugehoerigkeit = "haus", Active = true });
        api.Setup(a => a.GetAufzugSystemsAsync(It.IsAny<AufzugSystemsRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(systems);

        var detail = new AufzugInspectionDetail { Id = 145, AufzugAnlageId = 9, SystemName = "Aufzug A", Status = "in_progress" };
        detail.ChecklistSteps.Add(Step("notruf", value: "In Ordnung"));
        detail.ChecklistSteps.Add(Step("ventile", "select", "Mangel", note: "undicht"));
        api.Setup(a => a.GetAufzugInspectionDetailAsync(It.Is<AufzugInspectionDetailRequest>(r => r.Id == 145), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new AufzugInspectionDetailResponse { Inspection = detail });

        var domain = new AufzugPruefungApi(() => api.Object);
        var list = await domain.GetSystemsAsync();
        Assert.Equal(new PruefungSystem(9, "Aufzug A", "A-1", 3, "Haus 3", "haus", true, "", ""), list.Single());

        var d = await domain.GetInspectionDetailAsync(145);
        Assert.Equal(9, d.SystemId);
        Assert.Equal(2, d.Steps.Count);
        Assert.Equal(new[] { "OK", "Mangel" }, d.Steps[1].Options);
        Assert.Equal("undicht", d.Steps[1].DefectNote);
    }

    [Fact]
    public async Task Sprinkler_SendsDomainSpecificFieldsAndDefaultType()
    {
        var api = new Mock<IDkcProtobufApi>();
        SprinklerInspectionSaveRequest? sent = null;
        api.Setup(a => a.CreateSprinklerInspectionAsync(It.IsAny<SprinklerInspectionSaveRequest>(), It.IsAny<CancellationToken>()))
           .Callback<SprinklerInspectionSaveRequest, CancellationToken>((r, _) => sent = r)
           .ReturnsAsync(new SprinklerInspectionCreateResponse { Id = 77 });
        SprinklerSystemSaveRequest? sys = null;
        api.Setup(a => a.UpdateSprinklerSystemAsync(It.IsAny<SprinklerSystemSaveRequest>(), It.IsAny<CancellationToken>()))
           .Callback<SprinklerSystemSaveRequest, CancellationToken>((r, _) => sys = r)
           .ReturnsAsync(new Ack());

        var domain = new SprinklerPruefungApi(() => api.Object);
        var id = await domain.SaveInspectionAsync(new PruefungInspectionInput(0, 4, "2026-11-14 10:00:00", "in_progress", "", 0, "", "", ""));
        Assert.Equal(77, id);
        Assert.Equal(4, sent!.SprinklerAnlageId);
        Assert.Equal("weekly_check", sent.InspectionType);

        Assert.Equal(5, await domain.SaveSystemAsync(new PruefungSystemInput(5, "S1", "SP-9", 2, "gemischt", false)));
        Assert.Equal("SP-9", sys!.AnlageNummer);
        Assert.Equal("gemischt", sys.Zugehoerigkeit);
        api.Verify(a => a.CreateSprinklerSystemAsync(It.IsAny<SprinklerSystemSaveRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Checklist_IsSentWithWizardValues()
    {
        var api = new Mock<IDkcProtobufApi>();
        AufzugChecklistUpdateRequest? sent = null;
        api.Setup(a => a.UpdateAufzugChecklistAsync(It.IsAny<AufzugChecklistUpdateRequest>(), It.IsAny<CancellationToken>()))
           .Callback<AufzugChecklistUpdateRequest, CancellationToken>((r, _) => sent = r)
           .ReturnsAsync(new Ack());

        await new AufzugPruefungApi(() => api.Object).SaveChecklistAsync(12, new[]
        {
            new PruefungChecklistValue("notruf", PruefungValues.CheckboxNok, "Taste klemmt"),
        });
        Assert.Equal(12, sent!.InspectionId);
        Assert.Equal("Nicht in Ordnung", sent.Items.Single().Status);
        Assert.Equal("Taste klemmt", sent.Items.Single().Note);
    }

    [Fact]
    public void Values_MissingRequiredAndDefects()
    {
        var steps = new[]
        {
            new PruefungChecklistStepData("a", "", "A", "", "checkbox", Array.Empty<string>(), true, "", ""),
            new PruefungChecklistStepData("b", "", "B", "", "checkbox", Array.Empty<string>(), false, "", ""),
            new PruefungChecklistStepData("c", "", "C", "", "checkbox", Array.Empty<string>(), true, PruefungValues.CheckboxNok, ""),
            new PruefungChecklistStepData("d", "", "D", "", "select", new[] { "OK" }, true, "OK", "Hinweis"),
        };
        Assert.Equal(new[] { "a" }, PruefungValues.MissingRequired(steps).Select(s => s.Id));
        Assert.Equal(new[] { "c", "d" }, steps.Where(PruefungValues.IsDefect).Select(s => s.Id));
    }

    [Fact]
    public void ChecklistEditItem_OffersWizardChoices()
    {
        var checkbox = new PruefungChecklistEditItem(new PruefungChecklistStepData("a", "", "A", "", "checkbox", Array.Empty<string>(), true, "", ""));
        Assert.Equal(new[] { "In Ordnung", "Nicht in Ordnung" }, checkbox.Choices);
        Assert.Equal("A *", checkbox.Title);
        Assert.False(checkbox.IsDefect);
        checkbox.Value = PruefungValues.CheckboxNok;
        Assert.True(checkbox.IsDefect);

        var text = new PruefungChecklistEditItem(new PruefungChecklistStepData("t", "", "T", "", "text", Array.Empty<string>(), false, "", ""));
        Assert.True(text.IsFreeText);
        Assert.Empty(text.Choices);
    }

    [Fact]
    public void Snapshot_FirstIsBaselineThenReportsChangedDomains()
    {
        var known = new Dictionary<string, long>();
        ChangeFeedSnapshotResponse Snap(long mm, long nea)
        {
            var scope = new ChangeFeedScope { Scope = "project:1", Revision = mm + nea };
            scope.Domains.Add("mm", mm);
            scope.Domains.Add("nea", nea);
            var r = new ChangeFeedSnapshotResponse { Enabled = true };
            r.Scopes.Add(scope);
            return r;
        }

        Assert.Empty(ChangeFeedService.ApplySnapshot(known, Snap(3, 1)));
        Assert.Empty(ChangeFeedService.ApplySnapshot(known, Snap(3, 1)));
        Assert.Equal(new[] { "nea" }, ChangeFeedService.ApplySnapshot(known, Snap(3, 2)));
        Assert.Empty(ChangeFeedService.ApplySnapshot(known, new ChangeFeedSnapshotResponse { Enabled = false }));
    }

    [Fact]
    public void ParseEvent_ReadsScopeDomainRevision()
    {
        Assert.Equal(("project:1", "mm", 7L), ChangeFeedService.ParseEvent("{\"scope\":\"project:1\",\"domain\":\"mm\",\"revision\":7}"));
        Assert.Null(ChangeFeedService.ParseEvent("[1,2]"));
        Assert.Null(ChangeFeedService.ParseEvent("{\"scope\":\"x\"}"));
    }

    [Theory]
    [InlineData("", "neu", "neu", "Bitte das aktuelle Passwort eingeben.")]
    [InlineData("alt", "", "", "Bitte ein neues Passwort eingeben.")]
    [InlineData("alt", "neu1", "neu2", "Die Wiederholung stimmt nicht mit dem neuen Passwort überein.")]
    [InlineData("gleich", "gleich", "gleich", "Das neue Passwort muss sich vom aktuellen unterscheiden.")]
    [InlineData("alt", "neu", "neu", null)]
    public void PasswordChange_ValidatesBeforeSending(string current, string next, string repeat, string? expected)
        => Assert.Equal(expected, SettingsViewModel.ValidatePasswordChange(current, next, repeat));

    [Fact]
    public void KeyHistoryRow_PrefersRecipientNameAndDerivesState()
    {
        var open = KeyHistoryRow.From(new KeyIssuedItem { Id = 1, IssuedTo = "u12", RecipientName = "Max", IssuedAt = "2026-11-01" });
        Assert.Equal("Max", open.Recipient);
        Assert.Equal("ausgegeben", open.State);
        var back = KeyHistoryRow.From(new KeyIssuedItem { Id = 2, IssuedTo = "Firma X", ReturnedAt = "2026-11-02" });
        Assert.Equal("Firma X", back.Recipient);
        Assert.Equal("zurück", back.State);
    }
}
