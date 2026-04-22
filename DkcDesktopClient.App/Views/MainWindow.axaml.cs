using Avalonia.Controls;
using DkcDesktopClient.App.ViewModels;

namespace DkcDesktopClient.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainWindowViewModel vm)
            await vm.InitializeAsync();
    }
}
