using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace DkcDesktopClient.App.Controls;

public partial class LoadingOverlay : UserControl
{
    public static readonly StyledProperty<bool> IsLoadingProperty = AvaloniaProperty.Register<LoadingOverlay, bool>(nameof(IsLoading));

    public bool IsLoading { get => GetValue(IsLoadingProperty); set => SetValue(IsLoadingProperty, value); }

    public LoadingOverlay()
    {
        AvaloniaXamlLoader.Load(this);
        Content = new Border
        {
            Background = Brush.Parse("#80FFFFFF"),
            Child = new ProgressBar
            {
                IsIndeterminate = true,
                Width = 160,
                Height = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsLoadingProperty)
            Update();
    }

    private void Update() => IsVisible = IsLoading;
}
