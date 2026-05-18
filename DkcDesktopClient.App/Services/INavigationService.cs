using DkcDesktopClient.App.ViewModels;

namespace DkcDesktopClient.App.Services;

/// <summary>Represents a single step in the navigation breadcrumb trail.</summary>
public record BreadcrumbItem(string Title, ViewModelBase ViewModel);

/// <summary>
/// Navigation service interface for the application shell.
/// Centralises all view transitions, maintains a back-stack and breadcrumb trail.
/// </summary>
public interface INavigationService
{
    /// <summary>The currently active view-model.</summary>
    ViewModelBase? CurrentView { get; }

    /// <summary>Read-only breadcrumb path (root → current).</summary>
    IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; }

    /// <summary>Whether <see cref="NavigateBack"/> can be invoked.</summary>
    bool CanNavigateBack { get; }

    /// <summary>Raised whenever <see cref="CurrentView"/> changes.</summary>
    event EventHandler? CurrentViewChanged;

    /// <summary>Raised whenever the breadcrumb trail changes.</summary>
    event EventHandler? BreadcrumbsChanged;

    /// <summary>
    /// Resolves <typeparamref name="TViewModel"/> from the DI container, optionally initialises it
    /// with <paramref name="parameter"/>, then navigates to it.
    /// </summary>
    void NavigateTo<TViewModel>(object? parameter = null, string? title = null)
        where TViewModel : ViewModelBase;

    /// <summary>Navigates to an already-constructed view-model instance.</summary>
    void NavigateTo(ViewModelBase viewModel, string? title = null);

    /// <summary>Navigates to the previous entry in the back-stack, if available.</summary>
    void NavigateBack();

    /// <summary>Clears the back-stack and breadcrumbs, then navigates to a root view-model.</summary>
    void NavigateToRoot(ViewModelBase viewModel, string title);
}

/// <summary>
/// Optional interface that ViewModels can implement to receive navigation parameters
/// and be notified when they are navigated to.
/// </summary>
public interface INavigationTarget
{
    /// <summary>Called by the <see cref="INavigationService"/> after navigation completes.</summary>
    Task OnNavigatedToAsync(object? parameter = null);
}
