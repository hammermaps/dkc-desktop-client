using DkcDesktopClient.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DkcDesktopClient.Tests;

public class AuthServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly TokenStore _tokenStore;

    public AuthServiceTests()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        _provider = services.BuildServiceProvider();
        var dp = _provider.GetRequiredService<IDataProtectionProvider>();
        _tokenStore = new TokenStore(dp, NullLogger<TokenStore>.Instance);
        _tokenStore.DeleteToken();
    }

    private DkcApiFactory CreateFactory()
    {
        return new DkcApiFactory(_tokenStore, NullLogger<DkcApiFactory>.Instance, NullLoggerFactory.Instance);
    }

    [Fact]
    public void IsAuthenticated_Initially_False()
    {
        var factory = CreateFactory();
        var svc = new AuthService(factory, _tokenStore, NullLogger<AuthService>.Instance);
        Assert.False(svc.IsAuthenticated);
    }

    [Fact]
    public async Task TryAutoLogin_WithNoSavedToken_ReturnsFalse()
    {
        var factory = CreateFactory();
        var svc = new AuthService(factory, _tokenStore, NullLogger<AuthService>.Instance);
        var result = await svc.TryAutoLoginAsync();
        Assert.False(result);
    }

    [Fact]
    public async Task LogoutAsync_WithNoCurrentToken_DoesNotThrow()
    {
        var factory = CreateFactory();
        var svc = new AuthService(factory, _tokenStore, NullLogger<AuthService>.Instance);
        await svc.LogoutAsync();
        Assert.False(svc.IsAuthenticated);
    }

    [Fact]
    public void HasPermission_WithNoPermissions_ReturnsFalse()
    {
        var factory = CreateFactory();
        var svc = new AuthService(factory, _tokenStore, NullLogger<AuthService>.Instance);
        Assert.False(svc.HasPermission("nea"));
    }

    [Fact]
    public void AuthStateChanged_FiredOnLogout()
    {
        var factory = CreateFactory();
        var svc = new AuthService(factory, _tokenStore, NullLogger<AuthService>.Instance);
        var fired = false;
        svc.AuthStateChanged += (_, _) => fired = true;
        _ = svc.LogoutAsync();
        Assert.True(fired);
    }

    public void Dispose()
    {
        _tokenStore.DeleteToken();
        _provider.Dispose();
    }
}
