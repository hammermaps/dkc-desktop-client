using DkcDesktopClient.App.ViewModels;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DkcDesktopClient.Tests;

/// <summary>Unit tests for ViewModel state, derived properties, and command events.</summary>
public class ViewModelTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly TokenStore _tokenStore;
    private readonly DkcApiFactory _factory;
    private readonly AuthService _authService;
    private readonly DataCacheService _cache;

    public ViewModelTests()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        _provider = services.BuildServiceProvider();
        var dp = _provider.GetRequiredService<IDataProtectionProvider>();
        _tokenStore  = new TokenStore(dp, NullLogger<TokenStore>.Instance);
        _factory     = new DkcApiFactory(_tokenStore, NullLogger<DkcApiFactory>.Instance, NullLoggerFactory.Instance);
        _authService = new AuthService(_factory, _tokenStore, NullLogger<AuthService>.Instance);
        _cache       = new DataCacheService(NullLogger<DataCacheService>.Instance);
    }

    public void Dispose() => _provider.Dispose();

    // ── DashboardViewModel ─────────────────────────────────────────────────────

    private DashboardViewModel CreateDashboard()
    {
        var brs = CreateBrs();
        return new DashboardViewModel(_factory, _authService, _cache, brs);
    }

    [Fact]
    public void Dashboard_InitialState_IsNotLoading()
    {
        var vm = CreateDashboard();
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public void Dashboard_InitialState_CountsAreZero()
    {
        var vm = CreateDashboard();
        Assert.Equal(0, vm.MmTotal);
        Assert.Equal(0, vm.MmOpen);
        Assert.Equal(0, vm.KeysAvailable);
        Assert.Equal(0, vm.NeaTotalSystems);
        Assert.Equal(0, vm.NeaOverdueInspections);
    }

    [Fact]
    public void Dashboard_MmTotalText_FormatsWithThousandsSeparator()
    {
        var vm = CreateDashboard();
        vm.MmTotal = 1234;
        Assert.Equal(1234.ToString("N0"), vm.MmTotalText);
    }

    [Fact]
    public void Dashboard_KeysAvailableText_UpdatesWithProperty()
    {
        var vm = CreateDashboard();
        vm.KeysAvailable = 42;
        Assert.Equal(42.ToString("N0"), vm.KeysAvailableText);
    }

    [Fact]
    public void Dashboard_NeaTotalSystemsText_UpdatesWithProperty()
    {
        var vm = CreateDashboard();
        vm.NeaTotalSystems = 7;
        Assert.Equal("7", vm.NeaTotalSystemsText);
    }

    [Fact]
    public void Dashboard_NeaOverdueInspectionsText_UpdatesWithProperty()
    {
        var vm = CreateDashboard();
        vm.NeaOverdueInspections = 3;
        Assert.Equal("3", vm.NeaOverdueInspectionsText);
    }

    [Fact]
    public void Dashboard_HasOverdueItems_FalseInitially()
    {
        var vm = CreateDashboard();
        Assert.False(vm.HasOverdueItems);
    }

    [Fact]
    public void Dashboard_CanSetProject_FalseWhenNoProjectSelected()
    {
        var vm = CreateDashboard();
        vm.SelectedProject = null;
        Assert.False(vm.SetActiveProjectCommand.CanExecute(null));
    }

    [Fact]
    public void Dashboard_CanSetProject_TrueWhenProjectIsSelected()
    {
        var vm = CreateDashboard();
        vm.SelectedProject = new Project(1, "Test Project", "desc");
        Assert.True(vm.SetActiveProjectCommand.CanExecute(null));
    }

    [Fact]
    public void Dashboard_CanSetProject_FalseWhenAlreadySetting()
    {
        var vm = CreateDashboard();
        vm.SelectedProject = new Project(1, "Test", "desc");
        vm.IsSettingProject = true;
        Assert.False(vm.SetActiveProjectCommand.CanExecute(null));
    }

    [Fact]
    public void Dashboard_CreateMmCommand_FiresCreateMmRequestedEvent()
    {
        var vm = CreateDashboard();
        var fired = false;
        vm.CreateMmRequested += (_, _) => fired = true;
        vm.CreateMmCommand.Execute(null);
        Assert.True(fired);
    }

    [Fact]
    public void Dashboard_StartNeaInspectionCommand_FiresEvent()
    {
        var vm = CreateDashboard();
        var fired = false;
        vm.StartNeaInspectionRequested += (_, _) => fired = true;
        vm.StartNeaInspectionCommand.Execute(null);
        Assert.True(fired);
    }

    // ── NotificationsViewModel ─────────────────────────────────────────────────

    private NotificationsViewModel CreateNotifications()
    {
        var polling = CreatePollingService();
        return new NotificationsViewModel(_factory, _authService, polling);
    }

    [Fact]
    public void Notifications_HasUnreadNotifications_FalseWhenTotalCountIsZero()
    {
        var vm = CreateNotifications();
        vm.TotalCount = 0;
        Assert.False(vm.HasUnreadNotifications);
    }

    [Fact]
    public void Notifications_HasUnreadNotifications_TrueWhenTotalCountIsPositive()
    {
        var vm = CreateNotifications();
        vm.TotalCount = 5;
        Assert.True(vm.HasUnreadNotifications);
    }

    [Fact]
    public void Notifications_HasNoNotifications_TrueWhenEmptyAndNotLoading()
    {
        var vm = CreateNotifications();
        Assert.False(vm.IsLoading);
        Assert.Empty(vm.Notifications);
        Assert.True(vm.HasNoNotifications);
    }

    [Fact]
    public void Notifications_HasNoNotifications_FalseWhenLoading()
    {
        var vm = CreateNotifications();
        vm.IsLoading = true;
        Assert.False(vm.HasNoNotifications);
    }

    [Fact]
    public void Notifications_InitialState_NotLoading()
    {
        var vm = CreateNotifications();
        Assert.False(vm.IsLoading);
        Assert.Null(vm.ErrorMessage);
    }

    // ── MmViewModel – static options ──────────────────────────────────────────

    [Fact]
    public void Mm_StatusFilterOptions_ContainsAllStatus()
    {
        // "Alle" (null) + 4 known statuses
        Assert.Equal(5, MmViewModel.StatusFilterOptions.Count);
        Assert.Null(MmViewModel.StatusFilterOptions[0].Value);    // "— Alle —"
        Assert.Equal(0, MmViewModel.StatusFilterOptions[1].Value); // Offen
        Assert.Equal(1, MmViewModel.StatusFilterOptions[2].Value); // In Bearbeitung
        Assert.Equal(2, MmViewModel.StatusFilterOptions[3].Value); // Geschlossen
        Assert.Equal(3, MmViewModel.StatusFilterOptions[4].Value); // Abgebrochen
    }

    [Fact]
    public void Mm_StatusEditOptions_DoesNotContainAllOption()
    {
        // Edit options should not have a null/"Alle" entry
        Assert.All(MmViewModel.StatusEditOptions, o => Assert.NotNull(o.Value));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private BackgroundRefreshService CreateBrs()
    {
        var config = Options.Create(new RefreshConfig());
        return new BackgroundRefreshService(
            _authService, _factory, _cache, config,
            NullLogger<BackgroundRefreshService>.Instance);
    }

    private NotificationPollingService CreatePollingService() =>
        new(_authService, _factory, NullLogger<NotificationPollingService>.Instance);
}
