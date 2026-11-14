using System.Text.Json;
using DkcDesktopClient.Core.Api;
using Xunit;

namespace DkcDesktopClient.Tests;

/// <summary>
/// Echte REST-Antworten des Servers (gekürzt) mit denselben Einstellungen wie Refit in
/// DkcApiFactory. Beide Fehler fielen erst im Live-Test auf: Dashboard und NEA luden nicht.
/// </summary>
public class RestDtoDeserializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public void KeysInventory_ToleratesNullCabinetId()
    {
        const string json = """
            {"success":true,"keys":[
              {"id":1,"name":"Haustür","description":null,"total_count":3,"available":1,"type_id":2,"cabinet_id":4,"number":"H1","enabled":1},
              {"id":2,"name":"Putzraum","description":"","total_count":1,"available":1,"type_id":1,"cabinet_id":null,"number":null}
            ]}
            """;
        var r = JsonSerializer.Deserialize<KeysInventoryResponse>(json, Options)!;
        Assert.Equal(2, r.Keys!.Count);
        Assert.Equal(1, r.Keys[1].Available);
        Assert.Equal(0, r.Keys[1].CabinetId); // nur über Protobuf befüllt
    }

    [Fact]
    public void NeaInspections_AcceptsDecimalRuntimeHours()
    {
        const string json = """
            {"success":true,"total":1,"inspections":[
              {"id":7,"nea_system_id":1,"system_name":"NEA 1","inspection_type":"monthly","inspection_date":"2026-11-01",
               "inspector_name":"test","status":"completed","overall_result":"ok","runtime_hours":161.3,"notes":null}
            ]}
            """;
        var r = JsonSerializer.Deserialize<NeaInspectionsResponse>(json, Options)!;
        Assert.Equal(161.3m, r.Inspections!.Single().RuntimeHours);
    }
}
