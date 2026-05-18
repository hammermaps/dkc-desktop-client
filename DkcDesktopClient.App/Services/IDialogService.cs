using DkcDesktopClient.App.ViewModels;

namespace DkcDesktopClient.App.Services;

/// <summary>
/// Unified service for showing modal dialogs and slide-in detail panels.
/// All methods are async and return when the dialog is dismissed.
/// </summary>
public interface IDialogService
{
    /// <summary>Shows a confirmation dialog and returns <c>true</c> if the user confirmed.</summary>
    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>Shows an informational alert dialog.</summary>
    Task AlertAsync(string title, string message);

    /// <summary>
    /// Resolves <typeparamref name="TViewModel"/> from DI and presents it as a slide-in
    /// detail panel from the right side.
    /// </summary>
    Task ShowDetailPanelAsync<TViewModel>(object? parameter = null)
        where TViewModel : ViewModelBase;

    /// <summary>
    /// Resolves <typeparamref name="TViewModel"/> from DI and presents it as a centred
    /// modal form dialog.
    /// </summary>
    Task ShowFormDialogAsync<TViewModel>(object? parameter = null)
        where TViewModel : ViewModelBase;
}
