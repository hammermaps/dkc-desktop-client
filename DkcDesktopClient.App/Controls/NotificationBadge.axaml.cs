using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace DkcDesktopClient.App.Controls;

public partial class NotificationBadge : UserControl
{
    public static readonly StyledProperty<int> CountProperty = AvaloniaProperty.Register<NotificationBadge, int>(nameof(Count));

    private readonly Border _badge = new();
    private readonly TextBlock _label = new();

    public int Count { get => GetValue(CountProperty); set => SetValue(CountProperty, value); }

    public NotificationBadge()
    {
        AvaloniaXamlLoader.Load(this);
        Width = 22;
        Height = 22;
        _label.FontSize = 11;
        _label.FontWeight = FontWeight.Bold;
        _label.Foreground = Brushes.White;
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _badge.Background = Brush.Parse("#EF4444");
        _badge.CornerRadius = new CornerRadius(999);
        _badge.Child = _label;
        Content = _badge;
        Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CountProperty)
            Update();
    }

    private void Update()
    {
        IsVisible = Count > 0;
        _label.Text = Count > 99 ? "99+" : Count.ToString();
    }
}
