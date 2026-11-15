using System.Text.RegularExpressions;
using DkcDesktopClient.Core.I18n;
using Xunit;

namespace DkcDesktopClient.Tests;

/// <summary>
/// Übersetzungen: Bestand aus lang/*.json des Servers (desktop.*), mitgeliefert als Ressource.
/// Prüft Vollständigkeit, Platzhalter, Server-kompatiblen Hash und dass keine festen Texte mehr in
/// den Ansichten stehen.
/// </summary>
public class TranslationTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DkcDesktopClient.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository-Wurzel nicht gefunden");
    }

    private static IEnumerable<string> SourceFiles(string pattern)
        => Directory.EnumerateFiles(RepoRoot, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains("DkcDesktopClient.Tests") && !f.Contains("DkcDesktopClient.UiTests"));

    private static HashSet<string> Placeholders(string text)
        => Regex.Matches(text, @"\{(\w+)\}").Select(m => m.Groups[1].Value).ToHashSet();

    [Fact]
    public void EveryUsedKey_ExistsInGerman()
    {
        var german = Translator.LoadEmbedded("de");
        var used = new SortedSet<string>(StringComparer.Ordinal);
        var keyPattern = new Regex(@"""(desktop\.[a-z0-9_.]+)""|\{i18n:T (desktop\.[a-z0-9_.]+)\}");
        foreach (var file in SourceFiles("*.cs").Concat(SourceFiles("*.axaml")))
            foreach (Match m in keyPattern.Matches(File.ReadAllText(file)))
                used.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);

        Assert.True(used.Count > 400, $"nur {used.Count} Schlüssel gefunden – Suchmuster prüfen");
        // Präfixe (z. B. "desktop.klima.mode_" + Wert) prüft Klima_OptionsHaveTranslations
        var missing = used.Where(k => !k.EndsWith('_') && !german.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0,
            "Fehlende Schlüssel (in lang/de.json des Servers ergänzen, dann tools/export-desktop-translations.php): "
            + string.Join(", ", missing));
    }

    [Fact]
    public void AllLanguages_HaveSameKeysAndPlaceholdersAsGerman()
    {
        var german = Translator.LoadEmbedded("de");
        Assert.NotEmpty(german);
        foreach (var language in Translator.Languages.Where(l => l != "de"))
        {
            var entries = Translator.LoadEmbedded(language);
            Assert.Equal(german.Keys.OrderBy(k => k), entries.Keys.OrderBy(k => k));
            foreach (var (key, text) in german)
                Assert.True(Placeholders(text).SetEquals(Placeholders(entries[key])), $"{language}: Platzhalter in {key} weichen ab");
        }
    }

    [Fact]
    public void Views_ContainNoHardCodedTexts()
    {
        // Feste Attribute mit Buchstaben; Markennamen und reine Symbole sind erlaubt.
        var allowed = new HashSet<string> { "DK-Control", "DK", "✕", "°C", "%" };
        var attr = new Regex(@"\b(Text|Content|Header|PlaceholderText|Watermark|ToolTip\.Tip|Title)=""([^""{][^""]*)""");
        var found = new List<string>();
        foreach (var file in SourceFiles("*.axaml"))
            foreach (Match m in attr.Matches(File.ReadAllText(file)))
            {
                var value = m.Groups[2].Value.Trim();
                if (allowed.Contains(value) || !Regex.IsMatch(value, @"\p{L}{2,}")) continue;
                found.Add($"{Path.GetFileName(file)}: {m.Value}");
            }
        // StringFormat mit Wörtern („Gesamt: {0}“) statt Labels.Format
        var format = new Regex(@"StringFormat=(?:'([^']*)'|""([^""]*)"")");
        foreach (var file in SourceFiles("*.axaml"))
            foreach (Match m in format.Matches(File.ReadAllText(file)))
                if (Regex.IsMatch(m.Groups[1].Value + m.Groups[2].Value, @"\p{L}{2,}"))
                    found.Add($"{Path.GetFileName(file)}: {m.Value}");
        Assert.True(found.Count == 0, "Feste Texte statt {i18n:T …} bzw. Labels.Format: " + string.Join("; ", found));
    }

    [Fact]
    public void Klima_OptionsHaveTranslations()
    {
        var german = Translator.LoadEmbedded("de");
        foreach (var mode in DkcDesktopClient.App.ViewModels.KlimaViewModel.ModeOptions)
            Assert.True(german.ContainsKey("desktop.klima.mode_" + mode), mode);
        foreach (var fan in DkcDesktopClient.App.ViewModels.KlimaViewModel.FanSpeedOptions)
            Assert.True(german.ContainsKey("desktop.klima.fan_" + fan), fan);
    }

    [Fact]
    public void ComputeHash_MatchesServer()
    {
        // php -r 'echo sha1("desktop.a\tÄ\ndesktop.b\t{n} x\n");' – TranslationsHandler::hash()
        var entries = new Dictionary<string, string> { ["desktop.b"] = "{n} x", ["x.y"] = "z", ["desktop.a"] = "Ä" };
        Assert.Equal("6a80ca058af5e3f91b471ea46987b4d1d60f8f1f", Translator.ComputeHash(entries));
    }

    [Fact]
    public void Get_FallsBackToGermanThenKey_AndFillsPlaceholders()
    {
        var t = new Translator();
        t.Use("en", new Dictionary<string, string> { ["desktop.common.error"] = "Error: {message}" });

        Assert.Equal("Error: boom", t.Get("desktop.common.error", ("message", "boom")));
        Assert.Equal("Speichern", t.Get("desktop.common.save"));       // fehlt in "en" → Deutsch
        Assert.Equal("desktop.unknown.key", t.Get("desktop.unknown.key"));
        Assert.Equal("en", t.Language);
    }

    [Fact]
    public void Use_RaisesChangeNotificationsForBindings()
    {
        var t = new Translator();
        var changed = new List<string?>();
        t.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var revision = t.Revision;

        t.Use("fr", Translator.LoadEmbedded("fr"));

        Assert.Equal(revision + 1, t.Revision);
        Assert.Contains(nameof(Translator.Revision), changed);
        Assert.Equal("Enregistrer", t.Get("desktop.common.save"));
    }

    [Theory]
    [InlineData("auto", "en", "fr", "en")]   // Kontosprache vor Systemsprache
    [InlineData("auto", null, "fr", "fr")]
    [InlineData("auto", null, "ja", "de")]   // nicht unterstützt → Deutsch
    [InlineData("pl", "en", "fr", "pl")]     // feste Wahl schlägt alles
    [InlineData("auto", "EN-us", "fr", "en")]
    public void Resolve_FollowsPreferenceAccountSystem(string preference, string? account, string system, string expected)
        => Assert.Equal(expected, TranslationService.Resolve(preference, account, system));

    [Fact]
    public void Service_PersistsPreference_AndOverlaysServerCache()
    {
        var dir = Directory.CreateTempSubdirectory("dkc-i18n-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "i18n"));
            File.WriteAllText(Path.Combine(dir, "i18n", "en.json"), "{\"desktop.common.save\":\"Store\"}");
            var t = new Translator();
            var service = new TranslationService(dir, translator: t);

            service.SetPreference("en");

            Assert.Equal("en", new TranslationService(dir, translator: new Translator()).Preference);
            Assert.Equal("Store", t.Get("desktop.common.save"));            // Server-Stand überlagert
            Assert.Equal("Cancel", t.Get("desktop.common.cancel"));         // Rest aus der Ressource
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
