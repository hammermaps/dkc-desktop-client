using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace DkcDesktopClient.App.Controls;

public partial class StatCard : UserControl
{
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<StatCard, string>(nameof(Title), string.Empty);
    public static readonly StyledProperty<string> ValueProperty = AvaloniaProperty.Register<StatCard, string>(nameof(Value), string.Empty);
    public static readonly StyledProperty<string> IconProperty = AvaloniaProperty.Register<StatCard, string>(nameof(Icon), string.Empty);
    public static readonly StyledProperty<string> TrendIconProperty = AvaloniaProperty.Register<StatCard, string>(nameof(TrendIcon), string.Empty);
    public static readonly StyledProperty<string> TrendColorProperty = AvaloniaProperty.Register<StatCard, string>(nameof(TrendColor), "#4A5568");
    public static readonly StyledProperty<string> BackgroundColorProperty = AvaloniaProperty.Register<StatCard, string>(nameof(BackgroundColor), "#FFFFFF");

    private readonly Border _card = new();
    private readonly TextBlock _icon = new();
    private readonly TextBlock _title = new();
    private readonly TextBlock _value = new();
    private readonly TextBlock _trend = new();

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string TrendIcon { get => GetValue(TrendIconProperty); set => SetValue(TrendIconProperty, value); }
    public string TrendColor { get => GetValue(TrendColorProperty); set => SetValue(TrendColorProperty, value); }
    public string BackgroundColor { get => GetValue(BackgroundColorProperty); set => SetValue(BackgroundColorProperty, value); }

    public StatCard()
    {
        AvaloniaXamlLoader.Load(this);
        MinWidth = 170;
        MinHeight = 112;

        var header = new DockPanel();
        _icon.FontSize = 22;
        _icon.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(_icon, Dock.Left);
        header.Children.Add(_icon);
        _title.FontSize = 13;
        _title.FontWeight = FontWeight.SemiBold;
        _title.Foreground = Brush.Parse("#4A5568");
        _title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(_title);

        _value.FontSize = 32;
        _value.FontWeight = FontWeight.Bold;
        _value.Margin = new Thickness(0, 10, 0, 4);

        _trend.FontSize = 12;
        _trend.HorizontalAlignment = HorizontalAlignment.Right;

        _card.CornerRadius = new CornerRadius(12);
        _card.Padding = new Thickness(16);
        _card.BorderBrush = Brush.Parse("#E2E8F0");
        _card.BorderThickness = new Thickness(1);
        _card.BoxShadow = BoxShadows.Parse("0 2 8 0 #14000000");
        _card.Child = new StackPanel { Children = { header, _value, _trend } };
        Content = _card;
        Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty || change.Property == ValueProperty || change.Property == IconProperty ||
            change.Property == TrendIconProperty || change.Property == TrendColorProperty || change.Property == BackgroundColorProperty)
            Update();
    }

    private void Update()
    {
        _title.Text = Title;
        _value.Text = Value;
        _icon.Text = Icon;
        _trend.Text = TrendIcon;
        _trend.IsVisible = !string.IsNullOrWhiteSpace(TrendIcon);
        _trend.Foreground = ParseBrush(TrendColor, "#4A5568");
        _card.Background = ParseBrush(BackgroundColor, "#FFFFFF");
    }

    private static IBrush ParseBrush(string value, string fallback)
    {
        try { return Brush.Parse(string.IsNullOrWhiteSpace(value) ? fallback : value); }
        catch (FormatException) { return Brush.Parse(fallback); }
        catch (ArgumentException) { return Brush.Parse(fallback); }
    }
}
