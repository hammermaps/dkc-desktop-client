using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DkcDesktopClient.Core.I18n;

/// <summary>
/// Übersetzungen des Clients. Quelle sind die Schlüssel <c>desktop.*</c> aus <c>lang/*.json</c>
/// des ProxyServers – derselbe Bestand und dieselben <c>{name}</c>-Platzhalter wie in der
/// Web-Oberfläche. Mitgeliefert wird ein Stand als eingebettete Ressource (I18n/lang/*.json),
/// <see cref="TranslationService"/> gleicht ihn mit dem Server ab.
/// Fallback-Kette wie im Server-TranslationHelper: aktive Sprache → Deutsch → Schlüssel.
/// </summary>
public sealed class Translator : INotifyPropertyChanged
{
    public const string Prefix = "desktop.";
    public const string DefaultLanguage = "de";

    /// <summary>Unterstützte Sprachen, Reihenfolge wie im Server (TranslationHelper::LANGUAGES).</summary>
    public static readonly IReadOnlyList<string> Languages = ["de", "en", "fr", "es", "tr", "ru", "pl"];

    public static Translator Instance { get; } = new();

    private readonly object _lock = new();
    private IReadOnlyDictionary<string, string> _entries;
    private readonly IReadOnlyDictionary<string, string> _fallback;

    internal Translator()
    {
        _fallback = LoadEmbedded(DefaultLanguage);
        _entries = _fallback;
    }

    /// <summary>Aktive Sprache (ISO-639-1).</summary>
    public string Language { get; private set; } = DefaultLanguage;

    /// <summary>Steigt bei jedem Sprach- oder Bestandswechsel; XAML-Bindungen hängen daran.</summary>
    public int Revision { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Führt Änderungsbenachrichtigungen aus; die App setzt hier den UI-Thread-Dispatcher, weil der
    /// Abgleich mit dem Server im Hintergrund endet. Standard: sofort im aufrufenden Thread.
    /// </summary>
    public static Action<Action> Notify { get; set; } = action => action();

    /// <summary>Nach einem Sprach- oder Bestandswechsel (beliebiger Thread).</summary>
    public event EventHandler? Changed;

    public string this[string key] => Get(key);

    /// <summary>Text zu <paramref name="key"/>; fehlt er, der deutsche Text, sonst der Schlüssel.</summary>
    public string Get(string key)
    {
        var entries = _entries;
        if (entries.TryGetValue(key, out var text)) return text;
        return _fallback.TryGetValue(key, out text) ? text : key;
    }

    /// <summary>Text mit ersetzten <c>{name}</c>-Platzhaltern (wie TranslationHelper::t()).</summary>
    public string Get(string key, params (string Name, object? Value)[] args)
        => Fill(Get(key), args);

    /// <summary>Kurzform für <see cref="Get(string)"/> der globalen Instanz.</summary>
    public static string T(string key) => Instance.Get(key);

    /// <summary>Kurzform für <see cref="Get(string, ValueTuple{string, object}[])"/> der globalen Instanz.</summary>
    public static string T(string key, params (string Name, object? Value)[] args) => Instance.Get(key, args);

    public static string Fill(string text, params (string Name, object? Value)[] args)
    {
        foreach (var (name, value) in args)
        {
            var formatted = value switch
            {
                null => string.Empty,
                IFormattable f => f.ToString(null, CultureInfo.CurrentCulture),
                _ => value.ToString() ?? string.Empty,
            };
            text = text.Replace("{" + name + "}", formatted, StringComparison.Ordinal);
        }
        return text;
    }

    /// <summary>Aktiviert eine Sprache mit dem gegebenen Bestand (fehlende Schlüssel fallen auf Deutsch zurück).</summary>
    public void Use(string language, IReadOnlyDictionary<string, string> entries)
    {
        lock (_lock)
        {
            Language = Normalize(language);
            _entries = entries;
            Revision++;
        }
        var active = Language;
        Notify(() =>
        {
            TrySetCulture(active);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Revision)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>Unbekannte oder leere Sprachcodes ergeben Deutsch.</summary>
    public static string Normalize(string? language)
    {
        var code = (language ?? string.Empty).Trim().ToLowerInvariant();
        if (code.Length > 2) code = code[..2];
        return Languages.Contains(code) ? code : DefaultLanguage;
    }

    /// <summary>Sprache des Betriebssystems, sofern unterstützt, sonst Deutsch.</summary>
    public static string SystemLanguage()
        => Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    /// <summary>Mitgelieferter Bestand einer Sprache (leer, wenn keine Ressource vorhanden ist).</summary>
    public static IReadOnlyDictionary<string, string> LoadEmbedded(string language)
    {
        var asm = typeof(Translator).Assembly;
        var name = $"DkcDesktopClient.Core.I18n.lang.{Normalize(language)}.json";
        using var stream = asm.GetManifestResourceStream(name);
        return stream is null ? new Dictionary<string, string>() : Parse(stream);
    }

    public static Dictionary<string, string> Parse(Stream json)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();

    /// <summary>Hash wie TranslationsHandler::hash() im Server: SHA-1 über „schlüssel\twert\n“, ordinal sortiert.</summary>
    public static string ComputeHash(IReadOnlyDictionary<string, string> entries)
    {
        var sb = new StringBuilder();
        foreach (var key in entries.Keys.Where(k => k.StartsWith(Prefix, StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal))
            sb.Append(key).Append('\t').Append(entries[key]).Append('\n');
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    private static void TrySetCulture(string language)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(language);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // Invariant-Globalization: Texte funktionieren trotzdem.
        }
    }
}
