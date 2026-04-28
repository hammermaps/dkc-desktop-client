using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace DkcDesktopClient.Core.Services;

public class TokenStore
{
    private const string Purpose = "DkcDesktopClient.Token";
    private const string TokenFileName = "dkc_token.dat";
    private const string ServerUrlFileName = "dkc_server.dat";
    private const string UsernameFileName = "dkc_user.dat";
    private readonly IDataProtector _protector;
    private readonly ILogger<TokenStore> _logger;
    private readonly string _dataDir;

    public TokenStore(IDataProtectionProvider dataProtection, ILogger<TokenStore> logger)
    {
        _protector = dataProtection.CreateProtector(Purpose);
        _logger = logger;
        _dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DkcDesktopClient");
        Directory.CreateDirectory(_dataDir);
    }

    public void SaveToken(string token)
    {
        var path = Path.Combine(_dataDir, TokenFileName);
        File.WriteAllText(path, _protector.Protect(token));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public string? LoadToken()
    {
        var path = Path.Combine(_dataDir, TokenFileName);
        if (!File.Exists(path)) return null;
        try
        {
            return _protector.Unprotect(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to unprotect token");
            DeleteToken();
            return null;
        }
    }

    public void DeleteToken()
    {
        var path = Path.Combine(_dataDir, TokenFileName);
        if (File.Exists(path)) File.Delete(path);
    }

    public void SaveServerUrl(string url)
    {
        var path = Path.Combine(_dataDir, ServerUrlFileName);
        File.WriteAllText(path, url);
    }

    public string? LoadServerUrl()
    {
        var path = Path.Combine(_dataDir, ServerUrlFileName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void SaveUsername(string username)
    {
        var path = Path.Combine(_dataDir, UsernameFileName);
        File.WriteAllText(path, username);
    }

    public string? LoadUsername()
    {
        var path = Path.Combine(_dataDir, UsernameFileName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void DeleteUsername()
    {
        var path = Path.Combine(_dataDir, UsernameFileName);
        if (File.Exists(path)) File.Delete(path);
    }
}
