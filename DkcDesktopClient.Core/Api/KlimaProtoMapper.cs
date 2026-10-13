using DkcDesktopClient.Core.Protocol;

namespace DkcDesktopClient.Core.Api;

/// <summary>
/// Übersetzt die Protobuf-Antworten der Klima-Actions in die vorhandenen Darstellungs-DTOs.
/// Controller-ID 0 (Feld nicht gesetzt) bedeutet Controller 1.
/// </summary>
public static class KlimaProtoMapper
{
    private static int Controller(int id) => id <= 0 ? 1 : id;

    public static KlimaDevicesResponse ToDto(Protocol.KlimaDevicesResponse source)
        => new(true,
            source.Devices.Select(d => new KlimaDevice(
                d.Address, d.Name, d.GroupId > 0 ? d.GroupId : null, d.Enabled, d.Sort, Controller(d.ControllerId))).ToList(),
            null);

    public static KlimaRealtimeStatusResponse ToDto(Protocol.KlimaRealtimeStatusResponse source)
        => new(true,
            source.Timestamp,
            source.Devices.Select(d => new KlimaDeviceStatus(
                d.Address, d.Name, d.Online, d.Power,
                string.IsNullOrEmpty(d.OperatingMode) ? null : d.OperatingMode,
                d.Setpoint, d.CurrentTemp,
                string.IsNullOrEmpty(d.FanSpeed) ? null : d.FanSpeed,
                string.IsNullOrEmpty(d.ErrorCode) ? null : d.ErrorCode,
                Controller(d.ControllerId))).ToList(),
            null);

    public static KlimaGroupsResponse ToDto(Protocol.KlimaGroupsResponse source)
        => new(true,
            source.Groups.Select(g => new KlimaGroup(g.Id, g.Name, g.DeviceCount, Controller(g.ControllerId))).ToList(),
            null);
}
