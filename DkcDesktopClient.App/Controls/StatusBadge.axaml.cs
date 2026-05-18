using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace DkcDesktopClient.App.Controls;

public partial class StatusBadge : UserControl
{
    public static readonly StyledProperty<string> StatusProperty = AvaloniaProperty.Register<StatusBadge, string>(nameof(Status), "info");
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<StatusBadge, string>(nameof(Text), string.Empty);

    private readonly Border _badge = new();
    private readonly TextBlock _label = new();

    public string Status { get => GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public StatusBadge()
    {
        AvaloniaXamlLoader.Load(this);
        _label.FontSize = 12;
        _label.FontWeight = FontWeight.SemiBold;
        _label.Foreground = Brushes.White;
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _badge.CornerRadius = new CornerRadius(999);
        _badge.Padding = new Thickness(8, 3);
        _badge.Child = _label;
        Content = _badge;
        Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StatusProperty || change.Property == TextProperty)
            Update();
    }

    private void Update()
    {
        var status = (Status ?? string.Empty).ToLowerInvariant();
        var (label, color) = status switch
        {
            "ok" or "success" => ("OK", "#10B981"),
            "warning" => ("Warnung", "#F59E0B"),
            "error" or "danger" => ("Fehler", "#EF4444"),
            _ => ("Info", "#3B82F6")
        };
        _label.Text = string.IsNullOrWhiteSpace(Text) ? label : Text;
        _badge.Background = Brush.Parse(color);
    }
}
