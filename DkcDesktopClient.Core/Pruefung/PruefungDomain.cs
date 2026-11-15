using DkcDesktopClient.Core.Protobuf;
using DkcDesktopClient.Core.I18n;
using DkcDesktopClient.Core.Protocol;

namespace DkcDesktopClient.Core.Pruefung;

// Aufzug- und Sprinklerprüfung sind im Vertrag (aufzug.proto/sprinkler.proto) strukturgleich;
// nur Feldnamen unterscheiden sich (aufzug_nummer/anlage_nummer, aufzug_anlage_id/sprinkler_anlage_id).
// Die Oberfläche arbeitet deshalb mit diesen neutralen Modellen und je einem Domänen-Adapter –
// dieselbe Aufteilung wie bei den gemeinsamen Web-Templates unter template/default/pruefung/.

/// <summary>Anlage (Aufzug bzw. Sprinkleranlage).</summary>
public sealed record PruefungSystem(
    int Id,
    string Name,
    string Nummer,
    int HausId,
    string HausName,
    string Zugehoerigkeit,
    bool Active,
    string LastInspectionDate,
    string LastInspectionResult)
{
    /// <summary>Nur das Datum (Server liefert ggf. mit Uhrzeit).</summary>
    public string LastInspectionText => LastInspectionDate.Length > 10 ? LastInspectionDate[..10] : LastInspectionDate;
}

/// <summary>Zeile der Prüfungsliste.</summary>
public sealed record PruefungInspection(
    int Id,
    int SystemId,
    string SystemName,
    string InspectionType,
    string InspectionDate,
    string InspectorName,
    string Status,
    string OverallResult,
    int DurationMinutes,
    string Notes)
{
    public string StatusText => PruefungValues.StatusLabel(Status);
    public string ResultText => PruefungValues.ResultLabel(OverallResult);
    /// <summary>Datum mit Uhrzeit ohne Sekunden.</summary>
    public string DateText => InspectionDate.Length > 16 ? InspectionDate[..16] : InspectionDate;
}

/// <summary>Prüfpunkt aus Vorlage + gespeichertem Wert (Server: PruefungChecklistStep).</summary>
public sealed record PruefungChecklistStepData(
    string Id,
    string Category,
    string Title,
    string Description,
    string Type,
    IReadOnlyList<string> Options,
    bool Required,
    string Value,
    string DefectNote);

public sealed record PruefungInspectionDetail(
    int Id,
    int SystemId,
    string SystemName,
    string InspectionType,
    string InspectionDate,
    string InspectorName,
    string Status,
    string OverallResult,
    int DurationMinutes,
    string DefectsFound,
    string CorrectiveActions,
    string Notes,
    IReadOnlyList<PruefungChecklistStepData> Steps)
{
    public string StatusText => PruefungValues.StatusLabel(Status);
    public string ResultText => PruefungValues.ResultLabel(OverallResult);
}

public sealed record PruefungSystemInput(int Id, string Name, string Nummer, int HausId, string Zugehoerigkeit, bool Active);

public sealed record PruefungInspectionInput(
    int Id, int SystemId, string InspectionDate, string Status, string OverallResult,
    int DurationMinutes, string Notes, string DefectsFound, string CorrectiveActions);

public sealed record PruefungChecklistValue(string StepId, string Value, string Note);

public sealed record PruefungDashboard(
    int TotalSystems,
    int InspectionsThisWeek,
    int InspectionsThisMonth,
    int FailedLast30Days,
    IReadOnlyList<PruefungOverdue> Overdue);

public sealed record PruefungOverdue(int SystemId, string SystemName, int DaysOverdue, string LastInspection);

public sealed record PruefungInspectionPage(int Total, IReadOnlyList<PruefungInspection> Items);

/// <summary>Fachliche Konstanten und Werte, die der Web-Wizard speichert.</summary>
public static class PruefungValues
{
    public const string StatusInProgress = "in_progress";
    public const string StatusCompleted  = "completed";
    public const string ResultPassed     = "passed";
    public const string ResultFailed     = "failed";

    public static string StatusLabel(string status) => status switch
    {
        StatusInProgress => Translator.T("desktop.pruefung.status_in_progress"),
        StatusCompleted  => Translator.T("desktop.pruefung.status_completed"),
        _                => status,
    };

    public static string ResultLabel(string result) => result switch
    {
        ResultPassed => Translator.T("desktop.pruefung.result_passed"),
        ResultFailed => Translator.T("desktop.pruefung.result_failed"),
        _            => result,
    };

    /// <summary>Anzeigetext eines gespeicherten Checklisten-Werts (Checkbox-Werte übersetzt, sonst unverändert).</summary>
    public static string ChoiceLabel(string value) => value switch
    {
        CheckboxOk  => Translator.T("desktop.pruefung.check_ok"),
        CheckboxNok => Translator.T("desktop.pruefung.check_nok"),
        _           => value,
    };

    /// <summary>
    /// Gespeicherte Werte einer Checkbox-Frage, identisch mit template/default/pruefung/wizard.tpl.
    /// Sprachunabhängig – angezeigt wird <see cref="ChoiceLabel"/>.
    /// </summary>
    public const string CheckboxOk  = "In Ordnung";
    public const string CheckboxNok = "Nicht in Ordnung";

    /// <summary>Pflichtpunkte ohne Wert; der Server prüft das beim Abschluss nicht, der Web-Wizard schon.</summary>
    public static IReadOnlyList<PruefungChecklistStepData> MissingRequired(IEnumerable<PruefungChecklistStepData> steps)
        => steps.Where(s => s.Required && string.IsNullOrWhiteSpace(s.Value)).ToList();

    /// <summary>Mängel: Checkbox „Nicht in Ordnung“ sowie Punkte mit Mängelnotiz.</summary>
    public static bool IsDefect(PruefungChecklistStepData step)
        => step.Value == CheckboxNok || !string.IsNullOrWhiteSpace(step.DefectNote);
}

/// <summary>Alle Aktionen einer Prüfungsdomäne, unabhängig von Aufzug/Sprinkler.</summary>
public interface IPruefungApi
{
    /// <summary>Schlüssel wie im Web (<c>aufzug</c>, <c>sprinkler</c>).</summary>
    string Key { get; }
    string Title { get; }
    string NummerLabel { get; }
    /// <summary>Prüftyp neuer Prüfungen (wie der Server-Standard).</summary>
    string DefaultInspectionType { get; }
    IReadOnlyList<string> ZugehoerigkeitOptions { get; }
    string ViewPermission { get; }
    string EditPermission { get; }
    string AdminPermission { get; }

    Task<IReadOnlyList<PruefungSystem>> GetSystemsAsync(CancellationToken ct = default);
    Task<int> SaveSystemAsync(PruefungSystemInput input, CancellationToken ct = default);
    Task DeleteSystemAsync(int id, CancellationToken ct = default);
    Task<PruefungInspectionPage> GetInspectionsAsync(int? systemId, int? year, string? status, int limit, int offset, CancellationToken ct = default);
    Task<PruefungInspectionDetail> GetInspectionDetailAsync(int id, CancellationToken ct = default);
    Task<int> SaveInspectionAsync(PruefungInspectionInput input, CancellationToken ct = default);
    Task CompleteInspectionAsync(int id, string overallResult, string notes, CancellationToken ct = default);
    Task SaveChecklistAsync(int inspectionId, IEnumerable<PruefungChecklistValue> values, CancellationToken ct = default);
    Task<PruefungDashboard> GetDashboardAsync(CancellationToken ct = default);
}

internal static class PruefungMapping
{
    public static IReadOnlyList<PruefungChecklistStepData> Steps(IEnumerable<PruefungChecklistStep> steps)
        => steps.Select(s => new PruefungChecklistStepData(
            s.Id, s.Category, s.Title, s.Description, s.Type, s.Options.ToList(), s.Required, s.Value, s.DefectNote)).ToList();
}

/// <summary>Adapter für die Aufzugprüfung (Actions AUFZUG_*).</summary>
public sealed class AufzugPruefungApi : IPruefungApi
{
    private readonly Func<IDkcProtobufApi> _api;
    public AufzugPruefungApi(Func<IDkcProtobufApi> api) => _api = api;

    public string Key => "aufzug";
    public string Title => Translator.T("desktop.nav.aufzug");
    public string NummerLabel => Translator.T("desktop.pruefung.aufzug_number");
    public string DefaultInspectionType => "quarterly_check";
    public IReadOnlyList<string> ZugehoerigkeitOptions { get; } = new[] { "haus", "gw" };
    public string ViewPermission => "aufzug_pruefung_view";
    public string EditPermission => "aufzug_pruefung_edit";
    public string AdminPermission => "aufzug_pruefung_admin";

    public async Task<IReadOnlyList<PruefungSystem>> GetSystemsAsync(CancellationToken ct = default)
    {
        var r = await _api().GetAufzugSystemsAsync(new AufzugSystemsRequest(), ct).ConfigureAwait(false);
        return r.Systems.Select(s => new PruefungSystem(s.Id, s.Name, s.AufzugNummer, s.HausId, s.HausName,
            s.Zugehoerigkeit, s.Active, s.LastInspectionDate, s.LastInspectionResult)).ToList();
    }

    public async Task<int> SaveSystemAsync(PruefungSystemInput i, CancellationToken ct = default)
    {
        var req = new AufzugSystemSaveRequest { Id = i.Id, Name = i.Name, AufzugNummer = i.Nummer, HausId = i.HausId, Zugehoerigkeit = i.Zugehoerigkeit, Active = i.Active };
        if (i.Id > 0) { await _api().UpdateAufzugSystemAsync(req, ct).ConfigureAwait(false); return i.Id; }
        return (await _api().CreateAufzugSystemAsync(req, ct).ConfigureAwait(false)).Id;
    }

    public Task DeleteSystemAsync(int id, CancellationToken ct = default)
        => _api().DeleteAufzugSystemAsync(new AufzugSystemDeleteRequest { Id = id }, ct);

    public async Task<PruefungInspectionPage> GetInspectionsAsync(int? systemId, int? year, string? status, int limit, int offset, CancellationToken ct = default)
    {
        var r = await _api().GetAufzugInspectionsAsync(new AufzugInspectionsRequest
        {
            SystemId = systemId ?? 0, Year = year ?? 0, Status = status ?? string.Empty, Limit = limit, Offset = offset,
        }, ct).ConfigureAwait(false);
        return new PruefungInspectionPage(r.Page?.Total ?? r.Inspections.Count, r.Inspections.Select(x => new PruefungInspection(
            x.Id, x.AufzugAnlageId, x.SystemName, x.InspectionType, x.InspectionDate, x.InspectorName,
            x.Status, x.OverallResult, x.DurationMinutes, x.Notes)).ToList());
    }

    public async Task<PruefungInspectionDetail> GetInspectionDetailAsync(int id, CancellationToken ct = default)
    {
        var d = (await _api().GetAufzugInspectionDetailAsync(new AufzugInspectionDetailRequest { Id = id }, ct).ConfigureAwait(false)).Inspection
                ?? throw new InvalidOperationException(Translator.T("desktop.pruefung.not_found"));
        return new PruefungInspectionDetail(d.Id, d.AufzugAnlageId, d.SystemName, d.InspectionType, d.InspectionDate,
            d.InspectorName, d.Status, d.OverallResult, d.DurationMinutes, d.DefectsFound, d.CorrectiveActions, d.Notes,
            PruefungMapping.Steps(d.ChecklistSteps));
    }

    public async Task<int> SaveInspectionAsync(PruefungInspectionInput i, CancellationToken ct = default)
    {
        var req = new AufzugInspectionSaveRequest
        {
            Id = i.Id, AufzugAnlageId = i.SystemId, InspectionType = DefaultInspectionType, InspectionDate = i.InspectionDate,
            Status = i.Status, OverallResult = i.OverallResult, DurationMinutes = i.DurationMinutes,
            Notes = i.Notes, DefectsFound = i.DefectsFound, CorrectiveActions = i.CorrectiveActions,
        };
        if (i.Id > 0) { await _api().UpdateAufzugInspectionAsync(req, ct).ConfigureAwait(false); return i.Id; }
        return (await _api().CreateAufzugInspectionAsync(req, ct).ConfigureAwait(false)).Id;
    }

    public Task CompleteInspectionAsync(int id, string overallResult, string notes, CancellationToken ct = default)
        => _api().CompleteAufzugInspectionAsync(new AufzugInspectionCompleteRequest { Id = id, OverallResult = overallResult, Notes = notes }, ct);

    public Task SaveChecklistAsync(int inspectionId, IEnumerable<PruefungChecklistValue> values, CancellationToken ct = default)
    {
        var req = new AufzugChecklistUpdateRequest { InspectionId = inspectionId };
        req.Items.AddRange(values.Select(v => new AufzugChecklistItem { CheckpointId = v.StepId, Status = v.Value, Note = v.Note }));
        return _api().UpdateAufzugChecklistAsync(req, ct);
    }

    public async Task<PruefungDashboard> GetDashboardAsync(CancellationToken ct = default)
    {
        var r = await _api().GetAufzugDashboardAsync(ct).ConfigureAwait(false);
        return new PruefungDashboard(r.Stats?.TotalSystems ?? 0, r.Stats?.InspectionsThisWeek ?? 0, r.Stats?.InspectionsThisMonth ?? 0,
            r.Stats?.FailedLast30Days ?? 0,
            r.DueTests.Select(o => new PruefungOverdue(o.SystemId, o.SystemName, o.DaysOverdue, o.LastInspection)).ToList());
    }
}

/// <summary>Adapter für die Sprinklerprüfung (Actions SPRINKLER_*).</summary>
public sealed class SprinklerPruefungApi : IPruefungApi
{
    private readonly Func<IDkcProtobufApi> _api;
    public SprinklerPruefungApi(Func<IDkcProtobufApi> api) => _api = api;

    public string Key => "sprinkler";
    public string Title => Translator.T("desktop.nav.sprinkler");
    public string NummerLabel => Translator.T("desktop.pruefung.sprinkler_number");
    public string DefaultInspectionType => "weekly_check";
    public IReadOnlyList<string> ZugehoerigkeitOptions { get; } = new[] { "haus", "gw", "gemischt" };
    public string ViewPermission => "sprinkler_pruefung_view";
    public string EditPermission => "sprinkler_pruefung_edit";
    public string AdminPermission => "sprinkler_pruefung_admin";

    public async Task<IReadOnlyList<PruefungSystem>> GetSystemsAsync(CancellationToken ct = default)
    {
        var r = await _api().GetSprinklerSystemsAsync(new SprinklerSystemsRequest(), ct).ConfigureAwait(false);
        return r.Systems.Select(s => new PruefungSystem(s.Id, s.Name, s.AnlageNummer, s.HausId, s.HausName,
            s.Zugehoerigkeit, s.Active, s.LastInspectionDate, s.LastInspectionResult)).ToList();
    }

    public async Task<int> SaveSystemAsync(PruefungSystemInput i, CancellationToken ct = default)
    {
        var req = new SprinklerSystemSaveRequest { Id = i.Id, Name = i.Name, AnlageNummer = i.Nummer, HausId = i.HausId, Zugehoerigkeit = i.Zugehoerigkeit, Active = i.Active };
        if (i.Id > 0) { await _api().UpdateSprinklerSystemAsync(req, ct).ConfigureAwait(false); return i.Id; }
        return (await _api().CreateSprinklerSystemAsync(req, ct).ConfigureAwait(false)).Id;
    }

    public Task DeleteSystemAsync(int id, CancellationToken ct = default)
        => _api().DeleteSprinklerSystemAsync(new SprinklerSystemDeleteRequest { Id = id }, ct);

    public async Task<PruefungInspectionPage> GetInspectionsAsync(int? systemId, int? year, string? status, int limit, int offset, CancellationToken ct = default)
    {
        var r = await _api().GetSprinklerInspectionsAsync(new SprinklerInspectionsRequest
        {
            SystemId = systemId ?? 0, Year = year ?? 0, Status = status ?? string.Empty, Limit = limit, Offset = offset,
        }, ct).ConfigureAwait(false);
        return new PruefungInspectionPage(r.Page?.Total ?? r.Inspections.Count, r.Inspections.Select(x => new PruefungInspection(
            x.Id, x.SprinklerAnlageId, x.SystemName, x.InspectionType, x.InspectionDate, x.InspectorName,
            x.Status, x.OverallResult, x.DurationMinutes, x.Notes)).ToList());
    }

    public async Task<PruefungInspectionDetail> GetInspectionDetailAsync(int id, CancellationToken ct = default)
    {
        var d = (await _api().GetSprinklerInspectionDetailAsync(new SprinklerInspectionDetailRequest { Id = id }, ct).ConfigureAwait(false)).Inspection
                ?? throw new InvalidOperationException(Translator.T("desktop.pruefung.not_found"));
        return new PruefungInspectionDetail(d.Id, d.SprinklerAnlageId, d.SystemName, d.InspectionType, d.InspectionDate,
            d.InspectorName, d.Status, d.OverallResult, d.DurationMinutes, d.DefectsFound, d.CorrectiveActions, d.Notes,
            PruefungMapping.Steps(d.ChecklistSteps));
    }

    public async Task<int> SaveInspectionAsync(PruefungInspectionInput i, CancellationToken ct = default)
    {
        var req = new SprinklerInspectionSaveRequest
        {
            Id = i.Id, SprinklerAnlageId = i.SystemId, InspectionType = DefaultInspectionType, InspectionDate = i.InspectionDate,
            Status = i.Status, OverallResult = i.OverallResult, DurationMinutes = i.DurationMinutes,
            Notes = i.Notes, DefectsFound = i.DefectsFound, CorrectiveActions = i.CorrectiveActions,
        };
        if (i.Id > 0) { await _api().UpdateSprinklerInspectionAsync(req, ct).ConfigureAwait(false); return i.Id; }
        return (await _api().CreateSprinklerInspectionAsync(req, ct).ConfigureAwait(false)).Id;
    }

    public Task CompleteInspectionAsync(int id, string overallResult, string notes, CancellationToken ct = default)
        => _api().CompleteSprinklerInspectionAsync(new SprinklerInspectionCompleteRequest { Id = id, OverallResult = overallResult, Notes = notes }, ct);

    public Task SaveChecklistAsync(int inspectionId, IEnumerable<PruefungChecklistValue> values, CancellationToken ct = default)
    {
        var req = new SprinklerChecklistUpdateRequest { InspectionId = inspectionId };
        req.Items.AddRange(values.Select(v => new SprinklerChecklistItem { CheckpointId = v.StepId, Status = v.Value, Note = v.Note }));
        return _api().UpdateSprinklerChecklistAsync(req, ct);
    }

    public async Task<PruefungDashboard> GetDashboardAsync(CancellationToken ct = default)
    {
        var r = await _api().GetSprinklerDashboardAsync(ct).ConfigureAwait(false);
        return new PruefungDashboard(r.Stats?.TotalSystems ?? 0, r.Stats?.InspectionsThisWeek ?? 0, r.Stats?.InspectionsThisMonth ?? 0,
            r.Stats?.FailedLast30Days ?? 0,
            r.DueTests.Select(o => new PruefungOverdue(o.SystemId, o.SystemName, o.DaysOverdue, o.LastInspection)).ToList());
    }
}
