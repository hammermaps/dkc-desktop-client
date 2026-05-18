using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DkcDesktopClient.App.Controls;

public partial class RefreshIndicator : UserControl
{
    public static readonly StyledProperty<bool> IsRefreshingProperty = AvaloniaProperty.Register<RefreshIndicator, bool>(nameof(IsRefreshing));

    public bool IsRefreshing { get => GetValue(IsRefreshingProperty); set => SetValue(IsRefreshingProperty, value); }

    public RefreshIndicator()
    {
        AvaloniaXamlLoader.Load(this);
        Content = new ProgressBar { IsIndeterminate = true, Width = 120, Height = 4, Opacity = 0.75 };
        Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsRefreshingProperty)
            Update();
    }

    private void Update() => IsVisible = IsRefreshing;
}
