using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(DkcDesktopClient.UiTests.TestAppBuilder))]

namespace DkcDesktopClient.UiTests;

/// <summary>
/// Minimale Anwendung mit denselben Stilen wie die echte App (Fluent, DataGrid, AppTheme),
/// aber ohne DI, Tray-Icon und Hintergrunddienste.
/// </summary>
public sealed class TestApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://DkcDesktopClient.UiTests/"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml"),
        });
        Styles.Add(new StyleInclude(new Uri("avares://DkcDesktopClient.UiTests/"))
        {
            Source = new Uri("avares://DkcDesktopClient.App/Styles/AppTheme.axaml"),
        });
    }
}

public static class TestAppBuilder
{
    // Echte Skia-Darstellung statt Headless-Drawing, damit Screenshots den sichtbaren Zustand zeigen.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseSkia()
        .UseHarfBuzz()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
