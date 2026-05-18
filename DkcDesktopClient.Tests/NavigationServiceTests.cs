using DkcDesktopClient.App.Services;
using DkcDesktopClient.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DkcDesktopClient.Tests;

/// <summary>Minimal concrete ViewModel for testing navigation without any business logic.</summary>
internal sealed class StubViewModel : ViewModelBase { }

/// <summary>Stub ViewModel that implements INavigationTarget to verify parameter passing.</summary>
internal sealed class ParameterViewModel : ViewModelBase, INavigationTarget
{
    public object? ReceivedParameter { get; private set; }

    public Task OnNavigatedToAsync(object? parameter = null)
    {
        ReceivedParameter = parameter;
        return Task.CompletedTask;
    }
}

public class NavigationServiceTests
{
    private static (NavigationService svc, IServiceProvider sp) Create(params ViewModelBase[] vms)
    {
        var services = new ServiceCollection();
        foreach (var vm in vms)
            services.AddSingleton(vm.GetType(), vm);

        // Always register stub types used in tests
        if (!vms.OfType<StubViewModel>().Any())
            services.AddTransient<StubViewModel>();
        if (!vms.OfType<ParameterViewModel>().Any())
            services.AddTransient<ParameterViewModel>();

        var sp  = services.BuildServiceProvider();
        var svc = new NavigationService(sp, NullLogger<NavigationService>.Instance);
        return (svc, sp);
    }

    // ── Initial state ──────────────────────────────────────────────────────────

    [Fact]
    public void InitialState_CurrentViewIsNull()
    {
        var (svc, _) = Create();
        Assert.Null(svc.CurrentView);
    }

    [Fact]
    public void InitialState_CanNavigateBackIsFalse()
    {
        var (svc, _) = Create();
        Assert.False(svc.CanNavigateBack);
    }

    [Fact]
    public void InitialState_BreadcrumbsIsEmpty()
    {
        var (svc, _) = Create();
        Assert.Empty(svc.Breadcrumbs);
    }

    // ── NavigateToRoot ─────────────────────────────────────────────────────────

    [Fact]
    public void NavigateToRoot_SetCurrentView()
    {
        var (svc, _) = Create();
        var vm = new StubViewModel();
        svc.NavigateToRoot(vm, "Root");

        Assert.Same(vm, svc.CurrentView);
    }

    [Fact]
    public void NavigateToRoot_ClearsBackStack()
    {
        var (svc, _) = Create();
        var vm1 = new StubViewModel();
        var vm2 = new StubViewModel();

        svc.NavigateToRoot(vm1, "Root");
        svc.NavigateTo(vm2, "Page2");
        svc.NavigateToRoot(vm1, "Root"); // should clear stack

        Assert.False(svc.CanNavigateBack);
    }

    [Fact]
    public void NavigateToRoot_AddsSingleBreadcrumb()
    {
        var (svc, _) = Create();
        var vm = new StubViewModel();
        svc.NavigateToRoot(vm, "Home");

        Assert.Single(svc.Breadcrumbs);
        Assert.Equal("Home", svc.Breadcrumbs[0].Title);
    }

    // ── NavigateTo (instance) ──────────────────────────────────────────────────

    [Fact]
    public void NavigateTo_Instance_UpdatesCurrentView()
    {
        var (svc, _) = Create();
        var vm1 = new StubViewModel();
        var vm2 = new StubViewModel();

        svc.NavigateToRoot(vm1, "Root");
        svc.NavigateTo(vm2, "Page2");

        Assert.Same(vm2, svc.CurrentView);
    }

    [Fact]
    public void NavigateTo_Instance_PushesCurrentToBackStack()
    {
        var (svc, _) = Create();
        var vm1 = new StubViewModel();
        var vm2 = new StubViewModel();

        svc.NavigateToRoot(vm1, "Root");
        svc.NavigateTo(vm2, "Page2");

        Assert.True(svc.CanNavigateBack);
    }

    [Fact]
    public void NavigateTo_Instance_AddsBreadcrumb()
    {
        var (svc, _) = Create();
        var vm1 = new StubViewModel();
        var vm2 = new StubViewModel();

        svc.NavigateToRoot(vm1, "Root");
        svc.NavigateTo(vm2, "Page2");

        Assert.Equal(2, svc.Breadcrumbs.Count);
        Assert.Equal("Page2", svc.Breadcrumbs[1].Title);
    }

    // ── NavigateTo<TViewModel> (generic) ──────────────────────────────────────

    [Fact]
    public void NavigateTo_Generic_ResolvesFromContainer()
    {
        var stub = new StubViewModel();
        var (svc, _) = Create(stub);

        var root = new StubViewModel();
        svc.NavigateToRoot(root, "Root");
        svc.NavigateTo<StubViewModel>();

        Assert.NotNull(svc.CurrentView);
        Assert.IsType<StubViewModel>(svc.CurrentView);
    }

    // ── NavigateBack ───────────────────────────────────────────────────────────

    [Fact]
    public void NavigateBack_ReturnsToPreviousView()
    {
        var (svc, _) = Create();
        var vm1 = new StubViewModel();
        var vm2 = new StubViewModel();

        svc.NavigateToRoot(vm1, "Root");
        svc.NavigateTo(vm2, "Page2");
        svc.NavigateBack();

        Assert.Same(vm1, svc.CurrentView);
    }

    [Fact]
    public void NavigateBack_RemovesBreadcrumb()
    {
        var (svc, _) = Create();
        var vm1 = new StubViewModel();
        var vm2 = new StubViewModel();

        svc.NavigateToRoot(vm1, "Root");
        svc.NavigateTo(vm2, "Page2");
        svc.NavigateBack();

        Assert.Single(svc.Breadcrumbs);
    }

    [Fact]
    public void NavigateBack_WhenStackEmpty_DoesNotThrow()
    {
        var (svc, _) = Create();
        svc.NavigateBack(); // should not throw
    }

    [Fact]
    public void NavigateBack_AfterMultipleNavigations_TracksCorrectly()
    {
        var (svc, _) = Create();
        var vm1 = new StubViewModel();
        var vm2 = new StubViewModel();
        var vm3 = new StubViewModel();

        svc.NavigateToRoot(vm1, "Root");
        svc.NavigateTo(vm2, "P2");
        svc.NavigateTo(vm3, "P3");

        svc.NavigateBack();
        Assert.Same(vm2, svc.CurrentView);

        svc.NavigateBack();
        Assert.Same(vm1, svc.CurrentView);

        Assert.False(svc.CanNavigateBack);
    }

    // ── Events ─────────────────────────────────────────────────────────────────

    [Fact]
    public void NavigateTo_RaisesCurrentViewChanged()
    {
        var (svc, _) = Create();
        var fired = false;
        svc.CurrentViewChanged += (_, _) => fired = true;

        svc.NavigateToRoot(new StubViewModel(), "Root");

        Assert.True(fired);
    }

    [Fact]
    public void NavigateTo_RaisesBreadcrumbsChanged()
    {
        var (svc, _) = Create();
        var fired = false;
        svc.BreadcrumbsChanged += (_, _) => fired = true;

        svc.NavigateToRoot(new StubViewModel(), "Root");

        Assert.True(fired);
    }
}
