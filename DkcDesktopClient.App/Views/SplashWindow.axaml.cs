using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DkcDesktopClient.App.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();

        // Show current assembly version
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null && VersionText != null)
            VersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>Updates the status message on the UI thread.</summary>
    public void SetStatus(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (StatusText != null)
                StatusText.Text = message;
        });
    }
}
