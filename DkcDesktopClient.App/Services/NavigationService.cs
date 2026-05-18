using DkcDesktopClient.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DkcDesktopClient.App.Services;

/// <summary>
/// Default implementation of <see cref="INavigationService"/>.
/// Maintains a back-stack (<see cref="Stack{T}"/>) and a breadcrumb trail.
/// ViewModels are resolved from the DI container (using scoped/transient lifetimes).
/// </summary>
public class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NavigationService> _logger;

    private readonly Stack<BreadcrumbItem> _backStack = new();
    private readonly List<BreadcrumbItem> _breadcrumbs = new();

    public ViewModelBase? CurrentView { get; private set; }
    public IReadOnlyList<BreadcrumbItem> Breadcrumbs => _breadcrumbs;
    public bool CanNavigateBack => _backStack.Count > 0;

    public event EventHandler? CurrentViewChanged;
    public event EventHandler? BreadcrumbsChanged;

    public NavigationService(IServiceProvider serviceProvider, ILogger<NavigationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger          = logger;
    }

    /// <inheritdoc/>
    public void NavigateTo<TViewModel>(object? parameter = null, string? title = null)
        where TViewModel : ViewModelBase
    {
        var vm = _serviceProvider.GetRequiredService<TViewModel>();
        NavigateTo(vm, title ?? typeof(TViewModel).Name, parameter);
    }

    /// <inheritdoc/>
    public void NavigateTo(ViewModelBase viewModel, string? title = null)
        => NavigateTo(viewModel, title ?? viewModel.GetType().Name, null);

    /// <inheritdoc/>
    public void NavigateBack()
    {
        if (!CanNavigateBack) return;

        var previous = _backStack.Pop();
        _breadcrumbs.RemoveAt(_breadcrumbs.Count - 1);

        SetCurrentView(previous.ViewModel);
        BreadcrumbsChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogDebug("NavigateBack → {ViewModel}", previous.ViewModel.GetType().Name);
    }

    /// <inheritdoc/>
    public void NavigateToRoot(ViewModelBase viewModel, string title)
    {
        _backStack.Clear();
        _breadcrumbs.Clear();
        _breadcrumbs.Add(new BreadcrumbItem(title, viewModel));

        SetCurrentView(viewModel);
        BreadcrumbsChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogDebug("NavigateToRoot → {ViewModel}", viewModel.GetType().Name);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void NavigateTo(ViewModelBase viewModel, string title, object? parameter)
    {
        if (CurrentView != null)
        {
            // Push the current view onto the back-stack before switching
            var currentTitle = _breadcrumbs.Count > 0
                ? _breadcrumbs[^1].Title
                : CurrentView.GetType().Name;
            _backStack.Push(new BreadcrumbItem(currentTitle, CurrentView));
        }

        _breadcrumbs.Add(new BreadcrumbItem(title, viewModel));
        SetCurrentView(viewModel);
        BreadcrumbsChanged?.Invoke(this, EventArgs.Empty);

        // Notify the ViewModel about navigation; runs on the current synchronization context
        // (typically the UI thread) so observable-property updates are safe.
        if (viewModel is INavigationTarget target)
        {
            _ = InvokeNavigatedToAsync(target, parameter);
        }

        _logger.LogDebug("NavigateTo → {ViewModel} (parameter: {Parameter})",
            viewModel.GetType().Name, parameter);
    }

    private async Task InvokeNavigatedToAsync(INavigationTarget target, object? parameter)
    {
        try { await target.OnNavigatedToAsync(parameter); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnNavigatedToAsync failed for {ViewModel}",
                target.GetType().Name);
        }
    }

    private void SetCurrentView(ViewModelBase viewModel)
    {
        CurrentView = viewModel;
        CurrentViewChanged?.Invoke(this, EventArgs.Empty);
    }
}
