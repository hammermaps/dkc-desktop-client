using DkcDesktopClient.Core.Services;
using Xunit;

namespace DkcDesktopClient.Tests;

public class ChangeFeedServiceTests
{
    [Fact]
    public void KeysForDomain_Klima_ReturnsStatusAndDevices()
        => Assert.Equal(new[] { CacheKeys.KlimaStatus, CacheKeys.KlimaDevices }, ChangeFeedService.KeysForDomain("klima"));

    [Fact]
    public void KeysForDomain_Unknown_IsEmpty()
        => Assert.Empty(ChangeFeedService.KeysForDomain("kincony"));

    [Theory]
    [InlineData("http://127.0.0.1:8099", "http://127.0.0.1:8098/events?ticket=a.b")]
    [InlineData("https://dkc.example.de/app", "https://dkc.example.de/change-feed/events?ticket=a.b")]
    public void ResolveStreamUrl_DerivesEndpoint(string server, string expected)
        => Assert.Equal(expected, ChangeFeedService.ResolveStreamUrl(server, "a.b"));

    [Fact]
    public void ParseDomain_ReadsDomainAndIgnoresGarbage()
    {
        Assert.Equal("mm", ChangeFeedService.ParseDomain("{\"scope\":\"project:1\",\"domain\":\"mm\",\"revision\":3}"));
        Assert.Null(ChangeFeedService.ParseDomain("kein json"));
    }
}
