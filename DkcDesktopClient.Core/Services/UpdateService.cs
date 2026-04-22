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
}

public class UpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/hammermaps/dkc-desktop-client/releases/latest";
    private const string UserAgent = "DkcDesktopClient-Updater";
    private const string HttpClientName = "UpdateService";

    private readonly ILogger<UpdateService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public static Version CurrentVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0, 0);

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

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
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

    public async Task<bool> DownloadAndInstallAsync(UpdateInfo updateInfo, IProgress<double>? progress = null, CancellationToken ct = default)
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

            using (var response = await client.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
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
