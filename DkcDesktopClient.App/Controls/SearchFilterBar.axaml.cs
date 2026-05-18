using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;

namespace DkcDesktopClient.App.Controls;

public partial class SearchFilterBar : UserControl
{
    public static readonly StyledProperty<string?> SearchTextProperty = AvaloniaProperty.Register<SearchFilterBar, string?>(nameof(SearchText), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<string> PlaceholderProperty = AvaloniaProperty.Register<SearchFilterBar, string>(nameof(Placeholder), "Suchen…");
    public static readonly StyledProperty<ICommand?> SearchCommandProperty = AvaloniaProperty.Register<SearchFilterBar, ICommand?>(nameof(SearchCommand));

    private readonly TextBox _searchBox = new();
    private readonly Button _searchButton = new();

    public string? SearchText { get => GetValue(SearchTextProperty); set => SetValue(SearchTextProperty, value); }
    public string Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public ICommand? SearchCommand { get => GetValue(SearchCommandProperty); set => SetValue(SearchCommandProperty, value); }

    public SearchFilterBar()
    {
        AvaloniaXamlLoader.Load(this);
        _searchBox.Width = 260;
        _searchBox.TextChanged += (_, _) => SetCurrentValue(SearchTextProperty, _searchBox.Text);
        _searchButton.Content = "Suchen";
        _searchButton.Classes.Add("primary");
        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "🔎", VerticalAlignment = VerticalAlignment.Center },
                _searchBox,
                _searchButton
            }
        };
        Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SearchTextProperty || change.Property == PlaceholderProperty || change.Property == SearchCommandProperty)
            Update();
    }

    private void Update()
    {
        if (_searchBox.Text != SearchText)
            _searchBox.Text = SearchText;
        _searchBox.PlaceholderText = Placeholder;
        _searchButton.Command = SearchCommand;
    }
}
