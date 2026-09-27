using System.Net;
using System.Net.Http;
using DkcDesktopClient.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace DkcDesktopClient.Tests;

public class UpdateServiceTests
{
    private static IHttpClientFactory CreateFactory(string jsonResponse, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new MockHttpMessageHandler(jsonResponse, statusCode);
        var client = new HttpClient(handler) { BaseAddress = null };
        var mock = new Mock<IHttpClientFactory>();
        mock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return mock.Object;
    }

    private static string BuildReleaseJson(string tagName, string? assetName = null, string? downloadUrl = null, string body = "")
    {
        var assetsJson = assetName is null
            ? "[]"
            : $@"[{{""name"":""{assetName}"",""browser_download_url"":""{downloadUrl ?? "https://example.com/asset"}"",""content_type"":""application/octet-stream""}}]";

        return $@"{{
            ""tag_name"":""{tagName}"",
            ""body"":""{body}"",
            ""assets"":{assetsJson}
        }}";
    }

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("v10.0.0", 10, 0, 0)]
    [InlineData("v0.9.1", 0, 9, 1)]
    public async Task CheckForUpdateAsync_ParsesTagVersionCorrectly(string tag, int major, int minor, int patch)
    {
        // Use v999.0.0 to ensure it's always newer than runtime CurrentVersion,
        // but for parsing test we just need to ensure any parseable tag is handled —
        // so use a version that IS newer (999) and verify DownloadUrl is non-empty.
        var assetName = UpdateService.GetAssetName();
        var json = BuildReleaseJson(tag, assetName, "https://example.com/dl");

        if (new Version(major, minor, patch) > UpdateService.CurrentVersion)
        {
            var factory = CreateFactory(json);
            var svc = new UpdateService(NullLogger<UpdateService>.Instance, factory);
            var result = await svc.CheckForUpdateAsync();
            Assert.NotNull(result);
            Assert.Equal(new Version(major, minor, patch), result.LatestVersion);
            Assert.Equal(tag, result.TagName);
        }
        else
        {
            var factory = CreateFactory(json);
            var svc = new UpdateService(NullLogger<UpdateService>.Instance, factory);
            var result = await svc.CheckForUpdateAsync();
            Assert.Null(result);
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_NewerVersionWithMatchingAsset_ReturnsUpdateInfo()
    {
        var assetName = UpdateService.GetAssetName();
        const string downloadUrl = "https://example.com/download/asset";
        var json = BuildReleaseJson("v999.0.0", assetName, downloadUrl, "New release notes");

        var factory = CreateFactory(json);
        var svc = new UpdateService(NullLogger<UpdateService>.Instance, factory);

        var result = await svc.CheckForUpdateAsync();

        Assert.NotNull(result);
        Assert.Equal(new Version(999, 0, 0), result.LatestVersion);
        Assert.Equal("v999.0.0", result.TagName);
        Assert.Equal(downloadUrl, result.DownloadUrl);
        Assert.Equal("New release notes", result.ReleaseNotes);
    }

    [Fact]
    public async Task CheckForUpdateAsync_VersionNotNewer_ReturnsNull()
    {
        // v0.0.1 will always be <= any real CurrentVersion
        var assetName = UpdateService.GetAssetName();
        var json = BuildReleaseJson("v0.0.1", assetName, "https://example.com/dl");

        var factory = CreateFactory(json);
        var svc = new UpdateService(NullLogger<UpdateService>.Instance, factory);

        var result = await svc.CheckForUpdateAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task CheckForUpdateAsync_NoMatchingAssetForPlatform_ReturnsNull()
    {
        // Newer version but asset name doesn't match current platform
        var json = BuildReleaseJson("v999.0.0", "DkcDesktopClient-unknown-platform", "https://example.com/dl");

        var factory = CreateFactory(json);
        var svc = new UpdateService(NullLogger<UpdateService>.Instance, factory);

        var result = await svc.CheckForUpdateAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task CheckForUpdateAsync_EmptyAssets_ReturnsNull()
    {
        // Newer version but no assets at all
        var json = BuildReleaseJson("v999.0.0");

        var factory = CreateFactory(json);
        var svc = new UpdateService(NullLogger<UpdateService>.Instance, factory);

        var result = await svc.CheckForUpdateAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task CheckForUpdateAsync_InvalidTag_ReturnsNull()
    {
        var json = BuildReleaseJson("not-a-version");

        var factory = CreateFactory(json);
        var svc = new UpdateService(NullLogger<UpdateService>.Instance, factory);

        var result = await svc.CheckForUpdateAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task CheckForUpdateAsync_HttpError_ReturnsNull()
    {
        var factory = CreateFactory("{}", HttpStatusCode.InternalServerError);
        var svc = new UpdateService(NullLogger<UpdateService>.Instance, factory);

        var result = await svc.CheckForUpdateAsync();

        Assert.Null(result);
    }

    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("1.2.3+build.metadata", 1, 2, 3)]
    [InlineData("1.2.3-beta.1", 1, 2, 3)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("  1.2.3  ", 1, 2, 3)]
    public void TryParseProductVersion_ValidVersionStrings_ParsesCorrectly(string input, int major, int minor, int patch)
    {
        var result = InvokeTryParseProductVersion(input, out var version);
        Assert.True(result);
        Assert.Equal(new Version(major, minor, patch), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("abc.def.ghi")]
    public void TryParseProductVersion_InvalidVersionStrings_ReturnsFalse(string? input)
    {
        var result = InvokeTryParseProductVersion(input, out _);
        Assert.False(result);
    }

    private static bool InvokeTryParseProductVersion(string? input, out Version version)
    {
        // Invoke via reflection since TryParseProductVersion is private
        var method = typeof(UpdateService).GetMethod(
            "TryParseProductVersion",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var parameters = new object?[] { input, null };
        var result = (bool)method!.Invoke(null, parameters)!;
        version = (Version?)parameters[1] ?? new Version(0, 0, 0);
        return result;
    }

    private static string BuildDkcVersionJson(string latestVersion, bool updateAvailable = true) => $@"{{
        ""success"": true,
        ""data"": {{
            ""platform"": ""linux"",
            ""current_version"": ""{UpdateService.CurrentVersion.ToString(3)}"",
            ""latest_version"": ""{latestVersion}"",
            ""update_available"": {(updateAvailable ? "true" : "false")},
            ""sha256"": """",
            ""notes"": ""Continuous build"",
            ""uploaded_at"": ""2026-09-27T10:00:00+02:00""
        }}
    }}";

    private static UpdateService CreateServiceWithRouting(string gitHubJson, string dkcJson)
    {
        var handler = new RoutingMockHttpMessageHandler(gitHubJson, dkcJson);
        var client = new HttpClient(handler) { BaseAddress = null };
        var mock = new Mock<IHttpClientFactory>();
        mock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return new UpdateService(NullLogger<UpdateService>.Instance, mock.Object);
    }

    [Fact]
    public async Task CheckForUpdateAsync_DkcVersionNewerThanCurrentVersion_ReturnsDkcUpdateWithRequiresAuth()
    {
        // GitHub has no update (v0.0.1 is never newer); DKC continuous channel reports a newer build.
        var gitHubJson = BuildReleaseJson("v0.0.1");
        var dkcJson = BuildDkcVersionJson("2999.01.01.1");

        var svc = CreateServiceWithRouting(gitHubJson, dkcJson);
        var result = await svc.CheckForUpdateAsync("https://dkc.example.test", "dkc_sometoken");

        Assert.NotNull(result);
        Assert.True(result.RequiresAuth);
        Assert.Equal(new Version(2999, 1, 1, 1), result.LatestVersion);
        Assert.Contains("desktop_app_download_binary", result.DownloadUrl);
    }

    [Fact]
    public async Task CheckForUpdateAsync_DkcVersionNotNewer_ReturnsNull()
    {
        var gitHubJson = BuildReleaseJson("v0.0.1");
        // latest_version equal to CurrentVersion -> not newer.
        var dkcJson = BuildDkcVersionJson(UpdateService.CurrentVersion.ToString(3), updateAvailable: false);

        var svc = CreateServiceWithRouting(gitHubJson, dkcJson);
        var result = await svc.CheckForUpdateAsync("https://dkc.example.test", null);

        Assert.Null(result);
    }

    [Fact]
    public async Task CheckForUpdateAsync_BothChannelsNewer_ReturnsTheNewerOne()
    {
        var gitHubJson = BuildReleaseJson("v999.0.0", UpdateService.GetAssetName(), "https://example.com/dl");
        var dkcJson = BuildDkcVersionJson("2999.01.01.1");

        var svc = CreateServiceWithRouting(gitHubJson, dkcJson);
        var result = await svc.CheckForUpdateAsync("https://dkc.example.test", null);

        Assert.NotNull(result);
        Assert.Equal(new Version(2999, 1, 1, 1), result.LatestVersion);
        Assert.True(result.RequiresAuth);
    }

    [Fact]
    public async Task CheckForUpdateAsync_NoServerUrl_OnlyChecksGitHub()
    {
        var gitHubJson = BuildReleaseJson("v999.0.0", UpdateService.GetAssetName(), "https://example.com/dl");
        // DKC response would report an update, but must never be requested without a serverUrl.
        var dkcJson = BuildDkcVersionJson("2999.01.01.1");

        var svc = CreateServiceWithRouting(gitHubJson, dkcJson);
        var result = await svc.CheckForUpdateAsync();

        Assert.NotNull(result);
        Assert.False(result.RequiresAuth);
        Assert.Equal(new Version(999, 0, 0), result.LatestVersion);
    }
}

internal class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly string _responseContent;
    private readonly HttpStatusCode _statusCode;

    public MockHttpMessageHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responseContent = responseContent;
        _statusCode = statusCode;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseContent, System.Text.Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}

/// <summary>Routes responses by URL substring so a test can mock the GitHub API and the DKC desktop_app_version endpoint differently within one CheckForUpdateAsync() call.</summary>
internal class RoutingMockHttpMessageHandler : HttpMessageHandler
{
    private readonly string _gitHubJson;
    private readonly string _dkcJson;

    public RoutingMockHttpMessageHandler(string gitHubJson, string dkcJson)
    {
        _gitHubJson = gitHubJson;
        _dkcJson = dkcJson;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? string.Empty;
        var body = url.Contains("api.github.com") ? _gitHubJson : _dkcJson;
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
