using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using DkcDesktopClient.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DkcDesktopClient.App.Services;

/// <summary>
/// Avalonia implementation of <see cref="IDialogService"/>.
/// Uses native Avalonia message boxes for Confirm/Alert and a simple overlay
/// pattern for detail panels and form dialogs.
///
/// Phase-2 note: once the design system (Phase 2) introduces styled
/// <c>ConfirmDialog</c>, <c>DetailSidePanel</c>, etc., this implementation
/// should be updated to use those components.
/// </summary>
public class DialogService : IDialogService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DialogService> _logger;

    public DialogService(IServiceProvider serviceProvider, ILogger<DialogService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger          = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var owner = GetMainWindow();
        if (owner == null)
        {
            _logger.LogWarning("ConfirmAsync called but no main window is available");
            return false;
        }

        // Use a simple Window-based confirmation dialog until Phase-2 styled dialogs are ready
        var dialog = new ConfirmationDialog(title, message);
        return await dialog.ShowDialog<bool>(owner);
    }

    /// <inheritdoc/>
    public async Task AlertAsync(string title, string message)
    {
        var owner = GetMainWindow();
        if (owner == null)
        {
            _logger.LogWarning("AlertAsync called but no main window is available");
            return;
        }

        var dialog = new AlertDialog(title, message);
        await dialog.ShowDialog(owner);
    }

    /// <inheritdoc/>
    public async Task ShowDetailPanelAsync<TViewModel>(object? parameter = null)
        where TViewModel : ViewModelBase
    {
        var owner = GetMainWindow();
        if (owner == null) return;

        var vm = _serviceProvider.GetRequiredService<TViewModel>();
        if (vm is INavigationTarget target)
            await target.OnNavigatedToAsync(parameter);

        var dialog = new DetailPanelDialog(vm);
        await dialog.ShowDialog(owner);
    }

    /// <inheritdoc/>
    public async Task ShowFormDialogAsync<TViewModel>(object? parameter = null)
        where TViewModel : ViewModelBase
    {
        var owner = GetMainWindow();
        if (owner == null) return;

        var vm = _serviceProvider.GetRequiredService<TViewModel>();
        if (vm is INavigationTarget target)
            await target.OnNavigatedToAsync(parameter);

        var dialog = new FormDialog(vm);
        await dialog.ShowDialog(owner);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Window? GetMainWindow()
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
           ?.MainWindow;
}

// ── Lightweight dialog windows (Phase-2 will replace with styled components) ──

/// <summary>Simple Yes/No confirmation window used by <see cref="DialogService"/> until Phase 2.</summary>
internal sealed class ConfirmationDialog : Window
{
    public ConfirmationDialog(string title, string message)
    {
        Title           = title;
        Width           = 420;
        Height          = 180;
        CanResize       = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing     = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };

        var yes = new Button { Content = "Ja" };
        var no  = new Button { Content = "Nein" };
        yes.Click += (_, _) => Close(true);
        no.Click  += (_, _) => Close(false);
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        panel.Children.Add(buttons);

        Content = panel;
    }
}

/// <summary>Simple alert window used by <see cref="DialogService"/> until Phase 2.</summary>
internal sealed class AlertDialog : Window
{
    public AlertDialog(string title, string message)
    {
        Title           = title;
        Width           = 420;
        Height          = 160;
        CanResize       = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });

        var ok = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        ok.Click += (_, _) => Close();
        panel.Children.Add(ok);

        Content = panel;
    }
}

/// <summary>Slide-in-style detail panel dialog (simplified until Phase-2 <c>DetailSidePanel</c>).</summary>
internal sealed class DetailPanelDialog : Window
{
    public DetailPanelDialog(object dataContext)
    {
        Title                  = string.Empty;
        Width                  = 480;
        Height                 = 640;
        CanResize              = true;
        WindowStartupLocation  = WindowStartupLocation.CenterOwner;
        DataContext            = dataContext;
        Content                = new ContentControl { DataContext = dataContext, [!ContentControl.ContentProperty] = new Avalonia.Data.Binding() };
    }
}

/// <summary>Centred form dialog (simplified until Phase-2 <c>ShowFormDialogAsync</c>).</summary>
internal sealed class FormDialog : Window
{
    public FormDialog(object dataContext)
    {
        Title                  = string.Empty;
        Width                  = 560;
        Height                 = 520;
        CanResize              = true;
        WindowStartupLocation  = WindowStartupLocation.CenterOwner;
        DataContext            = dataContext;
        Content                = new ContentControl { DataContext = dataContext, [!ContentControl.ContentProperty] = new Avalonia.Data.Binding() };
    }
}
