using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace DkcDesktopClient.App.Controls;

public partial class EmptyStateView : UserControl
{
    public static readonly StyledProperty<string> IconProperty = AvaloniaProperty.Register<EmptyStateView, string>(nameof(Icon), "∅");
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<EmptyStateView, string>(nameof(Title), "Keine Daten");
    public static readonly StyledProperty<string> MessageProperty = AvaloniaProperty.Register<EmptyStateView, string>(nameof(Message), string.Empty);
    public static readonly StyledProperty<string> ActionTextProperty = AvaloniaProperty.Register<EmptyStateView, string>(nameof(ActionText), string.Empty);
    public static readonly StyledProperty<ICommand?> ActionCommandProperty = AvaloniaProperty.Register<EmptyStateView, ICommand?>(nameof(ActionCommand));

    private readonly TextBlock _icon = new();
    private readonly TextBlock _title = new();
    private readonly TextBlock _message = new();
    private readonly Button _action = new();

    public string Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Message { get => GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public string ActionText { get => GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
    public ICommand? ActionCommand { get => GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }

    public EmptyStateView()
    {
        AvaloniaXamlLoader.Load(this);
        _icon.FontSize = 42;
        _icon.HorizontalAlignment = HorizontalAlignment.Center;
        _title.FontSize = 18;
        _title.FontWeight = FontWeight.Bold;
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _message.Foreground = Brush.Parse("#718096");
        _message.TextWrapping = TextWrapping.Wrap;
        _message.HorizontalAlignment = HorizontalAlignment.Center;
        _action.HorizontalAlignment = HorizontalAlignment.Center;
        _action.Classes.Add("primary");
        Content = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24),
            Children = { _icon, _title, _message, _action }
        };
        Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconProperty || change.Property == TitleProperty || change.Property == MessageProperty ||
            change.Property == ActionTextProperty || change.Property == ActionCommandProperty)
            Update();
    }

    private void Update()
    {
        _icon.Text = Icon;
        _title.Text = Title;
        _message.Text = Message;
        _message.IsVisible = !string.IsNullOrWhiteSpace(Message);
        _action.Content = ActionText;
        _action.Command = ActionCommand;
        _action.IsVisible = !string.IsNullOrWhiteSpace(ActionText) && ActionCommand != null;
    }
}
