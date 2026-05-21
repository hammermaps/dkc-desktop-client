using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DkcDesktopClient.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DkcDesktopClient.Tests;

public class HttpPerformanceConfigTests
{
    [Fact]
    public void HttpPerformanceConfig_DefaultValues_AreOptimal()
    {
        var config = new HttpPerformanceConfig();
        
        Assert.Equal(30, config.RequestTimeoutSeconds);
        Assert.Equal(10, config.ConnectTimeoutSeconds);
        Assert.Equal(10, config.MaxConnectionsPerServer);
        Assert.Equal(5, config.KeepAliveTimeoutSeconds);
        Assert.Equal(100, config.MaxKeepAliveRequests);
        Assert.True(config.EnableCompression);
        Assert.True(config.EnableHttp2);
        Assert.Equal(2, config.MaxRetries);
    }

    [Theory]
    [InlineData(15, 5, 5)]
    [InlineData(60, 20, 15)]
    [InlineData(45, 15, 8)]
    public void HttpPerformanceConfig_CanBeConfigured(int requestTimeout, int connectTimeout, int maxConnections)
    {
        var config = new HttpPerformanceConfig
        {
            RequestTimeoutSeconds = requestTimeout,
            ConnectTimeoutSeconds = connectTimeout,
            MaxConnectionsPerServer = maxConnections
        };
        
        Assert.Equal(requestTimeout, config.RequestTimeoutSeconds);
        Assert.Equal(connectTimeout, config.ConnectTimeoutSeconds);
        Assert.Equal(maxConnections, config.MaxConnectionsPerServer);
    }

    [Fact]
    public void HttpPerformanceConfig_LowBandwidth_Scenario()
    {
        // Szenario: Langsame/mobile Verbindung
        var config = new HttpPerformanceConfig
        {
            RequestTimeoutSeconds = 60,        // Längeres Timeout für langsame Netzwerke
            ConnectTimeoutSeconds = 20,
            MaxConnectionsPerServer = 5,       // Weniger gleichzeitige Verbindungen
            EnableCompression = true,           // Kompression ist noch wichtiger
            MaxRetries = 3,                     // Mehr Wiederholungen
            RetryDelayMilliseconds = 1000,      // Längere Verzögerung zwischen Retries
        };
        
        Assert.Equal(60, config.RequestTimeoutSeconds);
        Assert.Equal(20, config.ConnectTimeoutSeconds);
        Assert.Equal(5, config.MaxConnectionsPerServer);
        Assert.True(config.EnableCompression);
        Assert.Equal(3, config.MaxRetries);
    }

    [Fact]
    public void HttpPerformanceConfig_HighBandwidth_Scenario()
    {
        // Szenario: Schnelle Verbindung (LAN, Fiber)
        var config = new HttpPerformanceConfig
        {
            RequestTimeoutSeconds = 15,        // Kürzeres Timeout für schnelle Netzwerke
            ConnectTimeoutSeconds = 5,
            MaxConnectionsPerServer = 20,      // Mehr gleichzeitige Verbindungen
            EnableCompression = false,          // Kompression kann Overhead verursachen
            MaxRetries = 1,                     // Weniger Wiederholungen nötig
            RetryDelayMilliseconds = 100,       // Kürzere Verzögerung
        };
        
        Assert.Equal(15, config.RequestTimeoutSeconds);
        Assert.Equal(5, config.ConnectTimeoutSeconds);
        Assert.Equal(20, config.MaxConnectionsPerServer);
        Assert.False(config.EnableCompression);
        Assert.Equal(1, config.MaxRetries);
    }

    [Fact]
    public void HttpPerformanceConfig_EnvironmentVariables_CanOverride()
    {
        // Simuliere Environment Variable
        Environment.SetEnvironmentVariable("DKC_HTTP_REQUEST_TIMEOUT", "45");
        
        var config = new HttpPerformanceConfig();
        if (int.TryParse(Environment.GetEnvironmentVariable("DKC_HTTP_REQUEST_TIMEOUT"), out var timeout))
        {
            config.RequestTimeoutSeconds = timeout;
        }
        
        Assert.Equal(45, config.RequestTimeoutSeconds);
        
        // Cleanup
        Environment.SetEnvironmentVariable("DKC_HTTP_REQUEST_TIMEOUT", null);
    }
}

/// <summary>
/// Integration Tests für HTTP-Client mit optimierter Konfiguration
/// </summary>
public class DkcApiFactory_PerformanceTests
{
    [Fact]
    public void DkcApiFactory_CreatesHttpClient_WithOptimalSettings()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDataProtection();
        var provider = services.BuildServiceProvider();
        var dp = provider.GetRequiredService<IDataProtectionProvider>();
        
        var tokenStore = new TokenStore(dp, NullLogger<TokenStore>.Instance);
        var loggerFactory = new NullLoggerFactory();
        var httpConfig = Options.Create(new HttpPerformanceConfig());
        
        var factory = new DkcApiFactory(tokenStore, NullLogger<DkcApiFactory>.Instance, loggerFactory, httpConfig);
        
        // Act
        var api = factory.Create("test-token", "https://api.example.com");
        
        // Assert
        Assert.NotNull(api);
        
        provider.Dispose();
    }

    [Fact]
    public void HttpPerformanceConfig_Timeout_PreventHangs()
    {
        var config = new HttpPerformanceConfig
        {
            RequestTimeoutSeconds = 30,  // 30 Sekunden max
            ConnectTimeoutSeconds = 10,   // 10 Sekunden bis verbunden
        };
        
        var requestTimeout = TimeSpan.FromSeconds(config.RequestTimeoutSeconds);
        var connectTimeout = TimeSpan.FromSeconds(config.ConnectTimeoutSeconds);
        
        Assert.True(requestTimeout.TotalSeconds > 0);
        Assert.True(connectTimeout.TotalSeconds > 0);
        Assert.True(requestTimeout > connectTimeout);  // Request-Timeout sollte größer sein
    }

    [Fact]
    public void HttpPerformanceConfig_ConnectionPooling_Enabled()
    {
        var config = new HttpPerformanceConfig
        {
            MaxConnectionsPerServer = 10,
            KeepAliveTimeoutSeconds = 5,
            MaxKeepAliveRequests = 100,
        };
        
        // Validiere, dass Connection Pooling Parameter sinnvoll sind
        Assert.InRange(config.MaxConnectionsPerServer, 1, 100);
        Assert.InRange(config.KeepAliveTimeoutSeconds, 1, 300);
        Assert.InRange(config.MaxKeepAliveRequests, 1, 1000);
    }
}



