using DkcDesktopClient.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DkcDesktopClient.Tests;

public class TokenStoreTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly TokenStore _tokenStore;

    public TokenStoreTests()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        _provider = services.BuildServiceProvider();
        var dp = _provider.GetRequiredService<IDataProtectionProvider>();
        _tokenStore = new TokenStore(dp, NullLogger<TokenStore>.Instance);
        _tokenStore.DeleteToken();
    }

    [Fact]
    public void SaveAndLoad_Token_RoundTrips()
    {
        const string token = "test-token-abc123";
        _tokenStore.SaveToken(token);
        var loaded = _tokenStore.LoadToken();
        Assert.Equal(token, loaded);
    }

    [Fact]
    public void LoadToken_WhenNoneExists_ReturnsNull()
    {
        _tokenStore.DeleteToken();
        var loaded = _tokenStore.LoadToken();
        Assert.Null(loaded);
    }

    [Fact]
    public void DeleteToken_RemovesToken()
    {
        _tokenStore.SaveToken("my-token");
        _tokenStore.DeleteToken();
        var loaded = _tokenStore.LoadToken();
        Assert.Null(loaded);
    }

    [Fact]
    public void SaveAndLoad_ServerUrl_RoundTrips()
    {
        const string url = "https://example.com";
        _tokenStore.SaveServerUrl(url);
        var loaded = _tokenStore.LoadServerUrl();
        Assert.Equal(url, loaded);
    }

    public void Dispose()
    {
        _tokenStore.DeleteToken();
        _provider.Dispose();
    }
}
