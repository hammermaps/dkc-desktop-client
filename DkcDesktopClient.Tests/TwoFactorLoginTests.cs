using System.Net;
using System.Text;
using System.Text.Json;
using DkcDesktopClient.App.ViewModels;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DkcDesktopClient.Tests;

/// <summary>
/// Zweiter Anmeldeschritt (auth_login → auth_send_2fa_code → auth_verify_2fa)
/// gegen einen Stub-Server.
/// </summary>
public class TwoFactorLoginTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly TokenStore _tokenStore;
    private readonly StubServer _server = new();
    private readonly AuthService _authService;

    public TwoFactorLoginTests()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        _provider = services.BuildServiceProvider();
        _tokenStore = new TokenStore(_provider.GetRequiredService<IDataProtectionProvider>(),
            NullLogger<TokenStore>.Instance,
            Path.Combine(Path.GetTempPath(), "DkcDesktopClientTests_" + Guid.NewGuid()));
        var factory = new StubApiFactory(_tokenStore, _server);
        _authService = new AuthService(factory, _tokenStore, NullLogger<AuthService>.Instance);
        factory.SetAuthService(_authService);
    }

    [Fact]
    public async Task Login_WithTwoFactor_ReturnsTicketWithoutToken()
    {
        var result = await _authService.LoginAsync("https://dkc.test", "max", "pw");

        Assert.True(result.RequiresTwoFactor);
        Assert.Equal("ticket-1", result.PendingTwoFactorTicket);
        Assert.Equal(["totp", "email"], result.TwoFactorMethods);
        Assert.False(_authService.IsAuthenticated);
        Assert.Null(_tokenStore.LoadToken());
    }

    [Fact]
    public async Task Verify_WithCorrectCode_StoresTokenAndDeviceToken()
    {
        await _authService.LoginAsync("https://dkc.test", "max", "pw");

        var result = await _authService.VerifyTwoFactorAsync("ticket-1", "totp", " 123456 ", trustDevice: true);

        Assert.True(result.Success);
        Assert.True(_authService.IsAuthenticated);
        Assert.Equal("dkc_token", _tokenStore.LoadToken());
        Assert.Equal("device-1", _tokenStore.LoadDeviceToken());
        Assert.Equal("max", _tokenStore.LoadUsername());
        Assert.True(_authService.HasPermission("admin"));
        Assert.Equal("123456", _server.LastBody!.Value.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Verify_WithWrongCode_ReturnsServerErrorInsteadOfThrowing()
    {
        var result = await _authService.VerifyTwoFactorAsync("ticket-1", "totp", "000000", trustDevice: false);

        Assert.False(result.Success);
        Assert.Equal("Der Code ist ungültig oder abgelaufen.", result.Error);
        Assert.False(_authService.IsAuthenticated);
    }

    [Fact]
    public async Task Login_SendsRememberedDeviceToken()
    {
        _tokenStore.SaveDeviceToken("device-1");

        var result = await _authService.LoginAsync("https://dkc.test", "max", "pw");

        Assert.Equal("device-1", _server.LastBody!.Value.GetProperty("device_token").GetString());
        Assert.True(result.Success);
        Assert.True(_authService.IsAuthenticated);
    }

    [Fact]
    public async Task SendCode_ReturnsServerResult()
    {
        var result = await _authService.SendTwoFactorCodeAsync("ticket-1", "email");

        Assert.True(result.Success);
        Assert.Equal("email", _server.LastBody!.Value.GetProperty("method").GetString());
    }

    [Fact]
    public async Task ViewModel_SwitchesToTwoFactorStepAndBack()
    {
        var vm = new LoginViewModel(_authService, _tokenStore)
        {
            ServerUrl = "https://dkc.test", Username = "max", Password = "pw",
        };

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.True(vm.IsTwoFactorStep);
        Assert.Equal(2, vm.TwoFactorMethods.Count);
        Assert.Equal("totp", vm.SelectedTwoFactorMethod!.Value);
        Assert.False(vm.SendTwoFactorCodeCommand.CanExecute(null));
        Assert.False(vm.VerifyTwoFactorCommand.CanExecute(null));
        Assert.Equal(string.Empty, vm.Password);

        vm.SelectedTwoFactorMethod = vm.TwoFactorMethods[1];
        Assert.True(vm.SendTwoFactorCodeCommand.CanExecute(null));

        vm.TwoFactorCode = "123456";
        Assert.True(vm.VerifyTwoFactorCommand.CanExecute(null));

        vm.CancelTwoFactorCommand.Execute(null);
        Assert.False(vm.IsTwoFactorStep);
        Assert.Empty(vm.TwoFactorMethods);
    }

    [Fact]
    public async Task ViewModel_WrongCodeShowsErrorAndClearsCode()
    {
        var vm = new LoginViewModel(_authService, _tokenStore)
        {
            ServerUrl = "https://dkc.test", Username = "max", Password = "pw",
        };
        await vm.LoginCommand.ExecuteAsync(null);

        vm.TwoFactorCode = "000000";
        await vm.VerifyTwoFactorCommand.ExecuteAsync(null);

        Assert.True(vm.IsTwoFactorStep);
        Assert.Equal("Der Code ist ungültig oder abgelaufen.", vm.ErrorMessage);
        Assert.Equal(string.Empty, vm.TwoFactorCode);

        vm.TwoFactorCode = "123456";
        await vm.VerifyTwoFactorCommand.ExecuteAsync(null);

        Assert.False(vm.IsTwoFactorStep);
        Assert.True(_authService.IsAuthenticated);
    }

    public void Dispose()
    {
        _tokenStore.DeleteToken();
        _tokenStore.DeleteDeviceToken();
        _provider.Dispose();
    }

    private sealed class StubApiFactory(TokenStore tokenStore, StubServer server)
        : DkcApiFactory(tokenStore, NullLogger<DkcApiFactory>.Instance, NullLoggerFactory.Instance, Options.Create(new HttpPerformanceConfig()))
    {
        public override IDkcApi Create(string? token = null, string? serverUrl = null, HttpMessageHandler? innerHandler = null)
            => base.Create(token, serverUrl, server);
    }

    /// <summary>Beantwortet die Auth-Aktionen wie api.php.</summary>
    private sealed class StubServer : HttpMessageHandler
    {
        private const string User = """{"id":7,"username":"max","vname":"Max","nname":"Muster","email":"max@example.test","is_admin":true}""";

        public JsonElement? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            _request = request;
            var query = request.RequestUri!.Query;
            if (request.Content != null)
            {
                var text = await request.Content.ReadAsStringAsync(ct);
                if (!string.IsNullOrEmpty(text))
                    LastBody = JsonDocument.Parse(text).RootElement.Clone();
            }

            if (query.Contains("action=auth_login"))
            {
                var deviceToken = LastBody?.TryGetProperty("device_token", out var d) == true ? d.GetString() : null;
                return deviceToken == "device-1"
                    ? Json(HttpStatusCode.OK, $$"""{"success":true,"token":"dkc_token","token_type":"Bearer","user":{{User}}}""")
                    : Json(HttpStatusCode.OK, """{"success":false,"requires_2fa":true,"pending_2fa_ticket":"ticket-1","two_factor_methods":["totp","email"],"error":"Zwei-Faktor-Anmeldung erforderlich."}""");
            }
            if (query.Contains("action=auth_verify_2fa"))
            {
                var code = LastBody?.GetProperty("code").GetString();
                return code == "123456"
                    ? Json(HttpStatusCode.OK, $$"""{"success":true,"token":"dkc_token","token_type":"Bearer","user":{{User}},"device_trust_token":"device-1"}""")
                    : Json(HttpStatusCode.Unauthorized, """{"success":false,"error":"Der Code ist ungültig oder abgelaufen."}""");
            }
            if (query.Contains("action=auth_send_2fa_code"))
                return Json(HttpStatusCode.OK, """{"success":true,"sent":true}""");
            if (query.Contains("action=user_info"))
                return Json(HttpStatusCode.OK, $$$"""{"success":true,"user":{{{User}}},"permissions":{"admin":true}}""");

            return Json(HttpStatusCode.NotFound, """{"success":false,"error":"unknown"}""");
        }

        private HttpRequestMessage? _request;

        private HttpResponseMessage Json(HttpStatusCode status, string body) =>
            // RequestMessage setzt auch HttpClientHandler; Refit braucht es für ApiException.
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json"), RequestMessage = _request };
    }
}
