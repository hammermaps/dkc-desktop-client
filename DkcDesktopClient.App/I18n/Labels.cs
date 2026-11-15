using System.Globalization;
using Avalonia.Data.Converters;
using DkcDesktopClient.Core.I18n;
using DkcDesktopClient.Core.Pruefung;

namespace DkcDesktopClient.App.I18n;

/// <summary>Konverter für gespeicherte, sprachunabhängige Werte, deren Anzeige übersetzt wird.</summary>
public static class Labels
{
    /// <summary>Checklisten-Auswahl: „In Ordnung“/„Nicht in Ordnung“ übersetzt, Vorlagen-Optionen unverändert.</summary>
    public static readonly IValueConverter ChecklistChoice =
        new FuncValueConverter<string?, string>(v => PruefungValues.ChoiceLabel(v ?? string.Empty));

    /// <summary>Klima-Betriebsart (cooling, heating, …) übersetzt; unbekannte Werte unverändert.</summary>
    public static readonly IValueConverter KlimaMode =
        new FuncValueConverter<string?, string>(v => Code("desktop.klima.mode_", v));

    /// <summary>Klima-Lüfterstufe (low, medium_low, …) übersetzt; unbekannte Werte unverändert.</summary>
    public static readonly IValueConverter KlimaFan =
        new FuncValueConverter<string?, string>(v => Code("desktop.klima.fan_", v));

    private static string Code(string prefix, string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var key = prefix + value;
        var text = Translator.T(key);
        return text == key ? value : text;
    }

    /// <summary>
    /// Gebundener Wert in einem übersetzten Text: <c>ConverterParameter="desktop.keys.issue_title|name"</c>
    /// setzt den Wert für <c>{name}</c> ein (Ersatz für StringFormat mit festem Text).
    /// </summary>
    public static readonly IValueConverter Format = new FormatConverter();

    /// <summary>Mehrere Werte: <c>ConverterParameter="desktop.mm.page_of|page|pages"</c>, Reihenfolge wie die Bindungen.</summary>
    public static readonly IMultiValueConverter FormatMany = new FormatConverter();

    private sealed class FormatConverter : IValueConverter, IMultiValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Convert(new[] { value }, targetType, parameter, culture);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var parts = (parameter as string ?? string.Empty).Split('|');
            var args = parts.Skip(1).Select((name, i) => (name, i < values.Count ? values[i] : null)).ToArray();
            return Translator.T(parts[0], args);
        }
    }
}
