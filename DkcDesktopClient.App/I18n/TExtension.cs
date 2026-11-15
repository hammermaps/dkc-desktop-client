using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using DkcDesktopClient.Core.I18n;

namespace DkcDesktopClient.App.I18n;

/// <summary>
/// Übersetzter Text in XAML: <c>Text="{i18n:T desktop.common.save}"</c>.
/// Die Bindung hängt an <see cref="Translator.Revision"/>, ein Sprachwechsel aktualisiert die
/// Oberfläche daher sofort, ohne Ansichten neu aufzubauen.
/// </summary>
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }

    public TExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding(nameof(Translator.Revision))
        {
            Source = Translator.Instance,
            Mode = BindingMode.OneWay,
            Converter = KeyConverter.Instance,
            ConverterParameter = Key,
        };

    private sealed class KeyConverter : IValueConverter
    {
        public static readonly KeyConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Translator.Instance.Get(parameter as string ?? string.Empty);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
