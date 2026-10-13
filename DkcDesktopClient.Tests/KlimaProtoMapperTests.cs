using DkcDesktopClient.Core.Api;
using Xunit;
using Proto = DkcDesktopClient.Core.Protocol;

namespace DkcDesktopClient.Tests;

public class KlimaProtoMapperTests
{
    [Fact]
    public void Devices_MissingControllerId_MapsToControllerOne()
    {
        var src = new Proto.KlimaDevicesResponse();
        src.Devices.Add(new Proto.KlimaDevice { Address = 7, Name = "A", GroupId = 3, Enabled = true });
        src.Devices.Add(new Proto.KlimaDevice { Address = 7, Name = "B", GroupId = 3, Enabled = true, ControllerId = 2 });

        var dto = KlimaProtoMapper.ToDto(src);

        Assert.Equal(new[] { 1, 2 }, dto.Devices!.Select(d => d.ControllerId));
        Assert.All(dto.Devices!, d => Assert.Equal(7, d.Address));
    }

    [Fact]
    public void Status_KeepsControllerAndEmptyStringsBecomeNull()
    {
        var src = new Proto.KlimaRealtimeStatusResponse { Timestamp = "t" };
        src.Devices.Add(new Proto.KlimaRealtimeStatusDevice { Address = 5, ControllerId = 2, Power = true });

        var dto = KlimaProtoMapper.ToDto(src);

        var d = Assert.Single(dto.Devices!);
        Assert.Equal(2, d.ControllerId);
        Assert.Null(d.Mode);
        Assert.Null(d.ErrorCode);
    }

    [Fact]
    public void Groups_SameIdOnTwoControllers_StayDistinct()
    {
        var src = new Proto.KlimaGroupsResponse();
        src.Groups.Add(new Proto.KlimaGroup { Id = 3, Name = "Gruppe 3", DeviceCount = 2 });
        src.Groups.Add(new Proto.KlimaGroup { Id = 3, Name = "Gruppe 3", DeviceCount = 4, ControllerId = 2 });

        var dto = KlimaProtoMapper.ToDto(src);

        Assert.Equal(new[] { (3, 1), (3, 2) }, dto.Groups!.Select(g => (g.Id, g.ControllerId)));
    }
}
