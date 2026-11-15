using System.Text.Json;
using DkcDesktopClient.Core.Protobuf;
using DkcDesktopClient.Core.Protocol;
using DkcDesktopClient.Core.Services;
using Microsoft.Extensions.Logging;

namespace DkcDesktopClient.Core.I18n;

/// <summary>
/// Wählt die Sprache und hält den Übersetzungsbestand aktuell:
/// <list type="bullet">
/// <item>Einstellung „auto“ (Standard): Sprache des Benutzerkontos (users.language), vor der Anmeldung die des Systems.</item>
/// <item>Bestand = mitgelieferte Ressource, überlagert vom zuletzt vom Server geladenen Stand (data/i18n/{sprache}.json).</item>
/// <item><see cref="SyncAsync"/> fragt TRANSLATIONS mit dem Hash des lokalen Stands ab und lädt nur bei Änderungen.</item>
/// </list>
/// </summary>
public sealed class TranslationService
{
    public const string Auto = "auto";

    private readonly string _dir;
    private readonly string _settingPath;
    private readonly Translator _translator;
    private readonly ILogger<TranslationService>? _logger;
    private string? _accountLanguage;

    public TranslationService(string dataDir, ILogger<TranslationService>? logger = null, Translator? translator = null)
    {
        _dir = Path.Combine(dataDir, "i18n");
        _settingPath = Path.Combine(dataDir, "language.txt");
        _translator = translator ?? Translator.Instance;
        _logger = logger;
    }

    /// <summary>Gewählte Einstellung: <see cref="Auto"/> oder ein Sprachcode.</summary>
    public string Preference
    {
        get
        {
            try
            {
                if (File.Exists(_settingPath))
                {
                    var value = File.ReadAllText(_settingPath).Trim().ToLowerInvariant();
                    if (value == Auto || Translator.Languages.Contains(value)) return value;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return Auto;
        }
    }

    /// <summary>Sprache, die sich aus Einstellung, Benutzerkonto und System ergibt.</summary>
    public string EffectiveLanguage => Resolve(Preference, _accountLanguage, Translator.SystemLanguage());

    public static string Resolve(string preference, string? accountLanguage, string systemLanguage)
    {
        if (preference != Auto) return Translator.Normalize(preference);
        if (!string.IsNullOrWhiteSpace(accountLanguage)) return Translator.Normalize(accountLanguage);
        return Translator.Normalize(systemLanguage);
    }

    /// <summary>Beim Start: Sprache aus Einstellung bzw. System aktivieren (ohne Netzwerk).</summary>
    public void Apply() => Activate(EffectiveLanguage);

    /// <summary>Nach der Anmeldung: Kontosprache übernehmen (wirkt nur bei Einstellung „auto“).</summary>
    public void SetAccountLanguage(string? language)
    {
        _accountLanguage = string.IsNullOrWhiteSpace(language) ? null : language;
        if (EffectiveLanguage != _translator.Language) Activate(EffectiveLanguage);
    }

    /// <summary>
    /// Nach An- oder Abmeldung: Kontosprache (USER_INFO.language) übernehmen und den Bestand der
    /// dann aktiven Sprache abgleichen. Ohne Token gilt wieder die Systemsprache.
    /// </summary>
    public async Task FollowAccountAsync(DkcApiFactory factory, string? token, CancellationToken ct = default)
    {
        string? language = null;
        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                var info = await new DkcProtobufApi(factory.CreateProtobuf(token)).GetUserInfoAsync(ct).ConfigureAwait(false);
                language = info.User?.Language;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Kontosprache nicht abrufbar");
                return;
            }
        }
        SetAccountLanguage(language);
        await SyncAsync(factory, ct).ConfigureAwait(false);
    }

    /// <summary>Einstellung speichern und sofort anwenden.</summary>
    public void SetPreference(string preference)
    {
        var value = preference == Auto ? Auto : Translator.Normalize(preference);
        Directory.CreateDirectory(Path.GetDirectoryName(_settingPath)!);
        File.WriteAllText(_settingPath, value);
        Activate(EffectiveLanguage);
    }

    /// <summary>Lädt den Server-Stand der aktiven Sprache, wenn er sich vom lokalen unterscheidet.</summary>
    /// <returns>true, wenn sich der Bestand geändert hat.</returns>
    public async Task<bool> SyncAsync(DkcApiFactory factory, CancellationToken ct = default)
    {
        var language = _translator.Language;
        var cached = LoadCache(language);
        try
        {
            using var client = factory.CreateProtobuf();
            var response = await client.SendAsync<TranslationsResponse>(
                Protocol.Action.Translations,
                new TranslationsRequest { Language = language, KnownHash = cached is null ? string.Empty : Translator.ComputeHash(cached) },
                ct: ct).ConfigureAwait(false);
            if (response.Unchanged || response.Entries.Count == 0) return false;

            var entries = new Dictionary<string, string>(response.Entries);
            SaveCache(response.Language, entries);
            if (Translator.Normalize(response.Language) == _translator.Language) Activate(_translator.Language);
            _logger?.LogInformation("Übersetzungen für {Language} aktualisiert ({Count} Einträge)", response.Language, entries.Count);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Älterer Server ohne TRANSLATIONS oder offline: mitgelieferter Stand genügt.
            _logger?.LogDebug(ex, "Übersetzungsabgleich nicht möglich");
            return false;
        }
    }

    /// <summary>Mitgelieferter Stand, überlagert vom Server-Stand.</summary>
    public IReadOnlyDictionary<string, string> Entries(string language)
    {
        var merged = new Dictionary<string, string>(Translator.LoadEmbedded(language));
        var cached = LoadCache(language);
        if (cached is not null)
            foreach (var (key, value) in cached) merged[key] = value;
        return merged;
    }

    private void Activate(string language) => _translator.Use(language, Entries(language));

    private string CachePath(string language) => Path.Combine(_dir, Translator.Normalize(language) + ".json");

    private Dictionary<string, string>? LoadCache(string language)
    {
        try
        {
            var path = CachePath(language);
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            return Translator.Parse(stream);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Übersetzungs-Cache für {Language} unlesbar", language);
            return null;
        }
    }

    private void SaveCache(string language, Dictionary<string, string> entries)
    {
        Directory.CreateDirectory(_dir);
        var tmp = CachePath(language) + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(entries));
        File.Move(tmp, CachePath(language), overwrite: true);
    }
}
