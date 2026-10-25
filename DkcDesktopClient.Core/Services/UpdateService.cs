using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
    /// Erwarteter SHA-256 des Binaries (Hex). Ohne diesen Wert wird kein Update installiert:
    /// DKC-Kanal: Feld <c>sha256</c> aus desktop_app_version, GitHub: <c>digest</c> des Assets.
    /// </summary>
    public string Sha256 { get; init; } = string.Empty;

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
            var sha256 = string.Empty;

            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    if (name == assetName)
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
                        // GitHub liefert pro Asset "digest": "sha256:<hex>".
                        if (asset.TryGetProperty("digest", out var digest)
                            && digest.GetString() is { } digestValue
                            && digestValue.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                            sha256 = digestValue["sha256:".Length..];
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
                ReleaseNotes = releaseNotes,
                Sha256 = sha256
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
            var sha256 = data.TryGetProperty("sha256", out var sh) ? sh.GetString() ?? string.Empty : string.Empty;

            return new UpdateInfo
            {
                LatestVersion = latestVersion,
                TagName = latestVersionString,
                DownloadUrl = $"{baseUrl}/api.php?action=desktop_app_download_binary&platform={platform}",
                ReleaseNotes = notes,
                Sha256 = sha256,
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
        var binaryPath = await DownloadVerifiedAsync(updateInfo, authToken, progress, ct);
        if (binaryPath == null)
            return false;

        try
        {
            LaunchUpdaterAndExit(binaryPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch updater");
            return false;
        }
    }

    /// <summary>
    /// Lädt das Update in ein privates Temp-Verzeichnis und prüft den SHA-256 gegen
    /// <see cref="UpdateInfo.Sha256"/>. Fehlt der erwartete Hash oder weicht er ab, wird die
    /// Datei gelöscht und <c>null</c> zurückgegeben - es wird nie ein ungeprüftes Binary gestartet.
    /// </summary>
    internal async Task<string?> DownloadVerifiedAsync(UpdateInfo updateInfo, string? authToken, IProgress<double>? progress, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
        {
            _logger.LogWarning("No download URL available for update");
            return null;
        }

        if (!TryParseSha256(updateInfo.Sha256, out var expectedHash))
        {
            _logger.LogError("Update refused: no valid SHA-256 provided for {Url}", updateInfo.DownloadUrl);
            return null;
        }

        // Eigenes Verzeichnis (unter Unix 0700): Ein fester Pfad in /tmp ließe zwischen Prüfung und
        // Start das Austauschen der Datei durch andere lokale Benutzer zu.
        var workDir = Directory.CreateTempSubdirectory("dkc_update_").FullName;
        var tempPath = Path.Combine(workDir, GetAssetName());

        try
        {
            var client = CreateClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, updateInfo.DownloadUrl);
            if (updateInfo.RequiresAuth && !string.IsNullOrEmpty(authToken))
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken);

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long bytesRead = 0;
                int read;
                while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    hasher.AppendData(buffer, 0, read);
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    bytesRead += read;
                    if (totalBytes > 0)
                        progress?.Report((double)bytesRead / totalBytes);
                }
            }

            if (!CryptographicOperations.FixedTimeEquals(hasher.GetHashAndReset(), expectedHash))
            {
                _logger.LogError("Update refused: SHA-256 of the download does not match the expected value");
                DeleteQuietly(workDir);
                return null;
            }

            return tempPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download/verify update");
            DeleteQuietly(workDir);
            return null;
        }
    }

    private static bool TryParseSha256(string value, out byte[] hash)
    {
        hash = [];
        var hex = value.Trim();
        if (hex.Length != 64)
            return false;
        try
        {
            hash = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void DeleteQuietly(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (Exception) { /* Aufräumen ist best effort */ }
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
            var scriptPath = Path.Combine(Path.GetDirectoryName(newBinaryPath)!, "dkc_update.bat");
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
            var scriptPath = Path.Combine(Path.GetDirectoryName(newBinaryPath)!, "dkc_update.sh");
            File.WriteAllText(scriptPath,
                "#!/bin/bash\n" +
                "sleep 2\n" +
                $"cp -f \"{newBinaryPath}\" \"{currentExePath}\"\n" +
                $"chmod +x \"{currentExePath}\"\n" +
                $"\"{currentExePath}\" &\n" +
                $"rm -f \"{newBinaryPath}\"\n" +
                "rm -f \"$0\"\n" +
                "rmdir \"$(dirname \"$0\")\" 2>/dev/null\n");
            File.SetUnixFileMode(scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Process.Start(new ProcessStartInfo("/bin/bash", scriptPath)
            {
                UseShellExecute = false
            });
        }

        Environment.Exit(0);
    }
}
