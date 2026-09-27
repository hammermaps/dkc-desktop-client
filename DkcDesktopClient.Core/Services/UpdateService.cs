using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DkcDesktopClient.Core.Services;

public class UpdateInfo
{
    public Version LatestVersion { get; init; } = new();
    public string TagName { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;

    /// <summary>
    /// True for updates from the DKC-continuous channel (desktop_app_download_binary),
    /// which requires an Authorization: Bearer header — false for public GitHub release assets.
    /// </summary>
    public bool RequiresAuth { get; init; }
}

public class UpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/hammermaps/dkc-desktop-client/releases/latest";
    private const string UserAgent = "DkcDesktopClient-Updater";
    private const string HttpClientName = "UpdateService";

    private readonly ILogger<UpdateService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public static Version CurrentVersion { get; } = GetCurrentVersion();

    private static Version GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (TryParseProductVersion(informationalVersion, out var productVersion))
            return productVersion;

        return assembly.GetName().Version ?? new Version(1, 0, 0, 0);
    }

    private static bool TryParseProductVersion(string? versionString, out Version version)
    {
        version = new Version(1, 0, 0, 0);

        if (string.IsNullOrWhiteSpace(versionString))
            return false;

        var normalizedVersion = versionString.Trim().TrimStart('v');

        var plusIndex = normalizedVersion.IndexOf('+');
        if (plusIndex >= 0)
            normalizedVersion = normalizedVersion[..plusIndex];

        var dashIndex = normalizedVersion.IndexOf('-');
        if (dashIndex >= 0)
            normalizedVersion = normalizedVersion[..dashIndex];

        return Version.TryParse(normalizedVersion, out version!);
    }

    public UpdateService(ILogger<UpdateService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        return client;
    }

    /// <summary>
    /// Checks both update channels (GitHub tag releases and, on Linux/Windows,
    /// the DKC-continuous channel from every push to main) and returns the newer
    /// of the two, or null if neither is newer than <see cref="CurrentVersion"/>.
    /// </summary>
    /// <param name="serverUrl">DKC-Basis-URL (z. B. aus TokenStore.LoadServerUrl()); ohne diese wird nur GitHub geprüft.</param>
    /// <param name="authToken">Bearer-Token für die authentifizierte DKC-Prüfung (nicht für den Versionscheck selbst nötig, aber für spätere Downloads relevant).</param>
    public async Task<UpdateInfo?> CheckForUpdateAsync(string? serverUrl = null, string? authToken = null, CancellationToken ct = default)
    {
        var gitHubUpdate = await CheckGitHubUpdateAsync(ct);

        UpdateInfo? dkcUpdate = null;
        if (!string.IsNullOrWhiteSpace(serverUrl) && GetDkcPlatform() != null)
            dkcUpdate = await CheckDkcContinuousUpdateAsync(serverUrl!, ct);

        if (gitHubUpdate == null) return dkcUpdate;
        if (dkcUpdate == null) return gitHubUpdate;
        return dkcUpdate.LatestVersion > gitHubUpdate.LatestVersion ? dkcUpdate : gitHubUpdate;
    }

    private async Task<UpdateInfo?> CheckGitHubUpdateAsync(CancellationToken ct)
    {
        try
        {
            var client = CreateClient();
            var json = await client.GetStringAsync(GitHubApiUrl, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? string.Empty;
            var versionString = tagName.TrimStart('v');

            if (!Version.TryParse(versionString, out var latestVersion))
            {
                _logger.LogWarning("Could not parse version from tag: {Tag}", tagName);
                return null;
            }

            if (latestVersion <= CurrentVersion)
                return null;

            var assetName = GetAssetName();
            var downloadUrl = string.Empty;

            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    if (name == assetName)
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(downloadUrl))
            {
                _logger.LogWarning("No matching asset found for platform: {AssetName}", assetName);
                return null;
            }

            var releaseNotes = root.TryGetProperty("body", out var body)
                ? body.GetString() ?? string.Empty
                : string.Empty;

            return new UpdateInfo
            {
                LatestVersion = latestVersion,
                TagName = tagName,
                DownloadUrl = downloadUrl,
                ReleaseNotes = releaseNotes
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for updates");
            return null;
        }
    }

    /// <summary>Plattform-Schlüssel für den DKC-continuous-Kanal (desktop_app_version/-download_binary); null auf macOS, das ausschließlich den GitHub-Pfad nutzt.</summary>
    private static string? GetDkcPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";
        return null;
    }

    private async Task<UpdateInfo?> CheckDkcContinuousUpdateAsync(string serverUrl, CancellationToken ct)
    {
        var platform = GetDkcPlatform();
        if (platform == null) return null;

        try
        {
            var baseUrl = serverUrl.TrimEnd('/');
            var currentVersion = Uri.EscapeDataString(CurrentVersion.ToString(3));
            var client = CreateClient();
            var json = await client.GetStringAsync(
                $"{baseUrl}/api.php?action=desktop_app_version&platform={platform}&current_version={currentVersion}", ct);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("success", out var successEl) || !successEl.GetBoolean())
                return null;
            if (!doc.RootElement.TryGetProperty("data", out var data))
                return null;

            var latestVersionString = data.TryGetProperty("latest_version", out var lv) ? lv.GetString() : null;
            if (string.IsNullOrWhiteSpace(latestVersionString) || !Version.TryParse(latestVersionString, out var latestVersion))
                return null;

            if (latestVersion <= CurrentVersion)
                return null;

            var notes = data.TryGetProperty("notes", out var n) ? n.GetString() ?? string.Empty : string.Empty;

            return new UpdateInfo
            {
                LatestVersion = latestVersion,
                TagName = latestVersionString,
                DownloadUrl = $"{baseUrl}/api.php?action=desktop_app_download_binary&platform={platform}",
                ReleaseNotes = notes,
                RequiresAuth = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check DKC-continuous update channel");
            return null;
        }
    }

    public async Task<bool> DownloadAndInstallAsync(UpdateInfo updateInfo, string? authToken = null, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
        {
            _logger.LogWarning("No download URL available for update");
            return false;
        }

        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), GetAssetName());
            var client = CreateClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, updateInfo.DownloadUrl);
            if (updateInfo.RequiresAuth && !string.IsNullOrEmpty(authToken))
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken);

            using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long bytesRead = 0;
                int read;
                while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    bytesRead += read;
                    if (totalBytes > 0)
                        progress?.Report((double)bytesRead / totalBytes);
                }
            }

            LaunchUpdaterAndExit(tempPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download/install update");
            return false;
        }
    }

    public static string GetAssetName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "DkcDesktopClient-win-x64.exe";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "DkcDesktopClient-linux-x64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "DkcDesktopClient-macos-x64";
        return "DkcDesktopClient";
    }

    private static void LaunchUpdaterAndExit(string newBinaryPath)
    {
        var currentExePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? string.Empty;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), "dkc_update.bat");
            File.WriteAllText(scriptPath,
                $"@echo off\r\n" +
                $"timeout /t 2 /nobreak > nul\r\n" +
                $"copy /y \"{newBinaryPath}\" \"{currentExePath}\"\r\n" +
                $"start \"\" \"{currentExePath}\"\r\n" +
                $"del \"{newBinaryPath}\"\r\n" +
                $"del \"%~f0\"\r\n");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        else
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), "dkc_update.sh");
            File.WriteAllText(scriptPath,
                "#!/bin/bash\n" +
                "sleep 2\n" +
                $"cp -f \"{newBinaryPath}\" \"{currentExePath}\"\n" +
                $"chmod +x \"{currentExePath}\"\n" +
                $"\"{currentExePath}\" &\n" +
                $"rm -f \"{newBinaryPath}\"\n" +
                "rm -f \"$0\"\n");
            File.SetUnixFileMode(scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            Process.Start(new ProcessStartInfo("/bin/bash", scriptPath)
            {
                UseShellExecute = false
            });
        }

        Environment.Exit(0);
    }
}
