using DkcDesktopClient.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace DkcDesktopClient.Tests;

public class ConnectivityServiceTests
{
    private static TokenStore CreateTokenStore()
    {
        // Use an ephemeral data protection provider for tests
        var dp = new EphemeralDataProtectionProvider();
        var logger = NullLogger<TokenStore>.Instance;
        return new TokenStore(dp, logger);
    }

    [Fact]
    public void IsOnline_DefaultsToTrue()
    {
        var svc = new ConnectivityService(CreateTokenStore(),
            NullLogger<ConnectivityService>.Instance);
        Assert.True(svc.IsOnline);
    }

    [Fact]
    public void ConnectivityChanged_EventHookup_DoesNotFire_BeforeFirstCheck()
    {
        var svc = new ConnectivityService(CreateTokenStore(),
            NullLogger<ConnectivityService>.Instance);
        bool eventFired = false;
        svc.ConnectivityChanged += (_, _) => eventFired = true;

        // No check has been performed yet, so the event must not have fired.
        Assert.False(eventFired);
        Assert.True(svc.IsOnline);
    }
}
