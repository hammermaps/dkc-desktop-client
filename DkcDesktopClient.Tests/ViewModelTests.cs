using DkcDesktopClient.App.Services;
using DkcDesktopClient.App.ViewModels;
using DkcDesktopClient.Core.Api;
using DkcDesktopClient.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

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

    // ── BuildingViewModel ─────────────────────────────────────────────────────

    private BuildingViewModel CreateBuilding() => new(_factory, _authService);

    [Fact]
    public void Building_InitialState_IsNotLoading()
    {
        var vm = CreateBuilding();
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public void Building_InitialState_StatsAreZero()
    {
        var vm = CreateBuilding();
        Assert.Equal(0, vm.StatTotalBuildings);
        Assert.Equal(0, vm.StatOpenInspections);
        Assert.Equal(0, vm.StatCompletedInspections);
    }

    [Fact]
    public void Building_InspectionStatusOptions_ContainsExpectedValues()
    {
        Assert.Contains("open",        BuildingViewModel.InspectionStatusOptions);
        Assert.Contains("in_progress", BuildingViewModel.InspectionStatusOptions);
        Assert.Contains("completed",   BuildingViewModel.InspectionStatusOptions);
    }

    [Fact]
    public void Building_ResultOptions_ContainsExpectedValues()
    {
        Assert.Contains("ok",            BuildingViewModel.ResultOptions);
        Assert.Contains("defects_found", BuildingViewModel.ResultOptions);
        Assert.Contains("failed",        BuildingViewModel.ResultOptions);
    }

    [Fact]
    public void Building_IsBuildingFormVisible_FalseInitially()
    {
        var vm = CreateBuilding();
        Assert.False(vm.IsBuildingFormVisible);
    }

    [Fact]
    public void Building_IsInspectionFormVisible_FalseInitially()
    {
        var vm = CreateBuilding();
        Assert.False(vm.IsInspectionFormVisible);
    }

    // ── NeaViewModel ──────────────────────────────────────────────────────────

    private NeaViewModel CreateNea()
    {
        var filePicker = new Mock<IFilePickerService>().Object;
        return new NeaViewModel(_factory, _authService, filePicker);
    }

    [Fact]
    public void Nea_InitialState_IsNotLoading()
    {
        var vm = CreateNea();
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public void Nea_InspectionTypeOptions_ContainsExpectedValues()
    {
        Assert.Contains("annual",    NeaViewModel.InspectionTypeOptions);
        Assert.Contains("monthly",   NeaViewModel.InspectionTypeOptions);
        Assert.Contains("quarterly", NeaViewModel.InspectionTypeOptions);
        Assert.Contains("ad_hoc",    NeaViewModel.InspectionTypeOptions);
    }

    [Fact]
    public void Nea_StatusOptions_ContainsExpectedValues()
    {
        Assert.Contains("open",        NeaViewModel.StatusOptions);
        Assert.Contains("in_progress", NeaViewModel.StatusOptions);
        Assert.Contains("completed",   NeaViewModel.StatusOptions);
    }

    [Fact]
    public void Nea_FuelTypeOptions_ContainsDiesel()
    {
        Assert.Contains("Diesel", NeaViewModel.FuelTypeOptions);
    }

    [Fact]
    public void Nea_IsSystemFormVisible_FalseInitially()
    {
        var vm = CreateNea();
        Assert.False(vm.IsSystemFormVisible);
    }

    [Fact]
    public void Nea_IsInspectionFormVisible_FalseInitially()
    {
        var vm = CreateNea();
        Assert.False(vm.IsInspectionFormVisible);
    }

    // ── KlimaViewModel ────────────────────────────────────────────────────────

    private KlimaViewModel CreateKlima() => new(_factory, _authService, CreateBrs());

    [Fact]
    public void Klima_InitialState_IsNotLoading()
    {
        var vm = CreateKlima();
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public void Klima_ModeOptions_ContainsExpectedValues()
    {
        Assert.Contains("cooling", KlimaViewModel.ModeOptions);
        Assert.Contains("heating", KlimaViewModel.ModeOptions);
        Assert.Contains("fan",     KlimaViewModel.ModeOptions);
        Assert.Contains("auto",    KlimaViewModel.ModeOptions);
        Assert.Contains("dry",     KlimaViewModel.ModeOptions);
    }

    [Fact]
    public void Klima_FanSpeedOptions_ContainsExpectedValues()
    {
        Assert.Contains("auto",   KlimaViewModel.FanSpeedOptions);
        Assert.Contains("low",    KlimaViewModel.FanSpeedOptions);
        Assert.Contains("medium", KlimaViewModel.FanSpeedOptions);
        Assert.Contains("high",   KlimaViewModel.FanSpeedOptions);
    }

    [Fact]
    public void Klima_IsControlPanelVisible_FalseInitially()
    {
        var vm = CreateKlima();
        Assert.False(vm.IsControlPanelVisible);
    }

    [Fact]
    public void Klima_DefaultSetpoint_Is22Degrees()
    {
        var vm = CreateKlima();
        Assert.Equal(22.0, vm.ControlSetpoint);
    }

    // ── KeysViewModel ─────────────────────────────────────────────────────────

    private KeysViewModel CreateKeys()
    {
        var filePicker = new Mock<IFilePickerService>().Object;
        return new KeysViewModel(_factory, _authService, filePicker);
    }

    [Fact]
    public void Keys_InitialState_IsNotLoading()
    {
        var vm = CreateKeys();
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public void Keys_InitialState_StatsAreZero()
    {
        var vm = CreateKeys();
        Assert.Equal(0, vm.StatTotalKeys);
        Assert.Equal(0, vm.StatIssuedKeys);
        Assert.Equal(0, vm.StatAvailableKeys);
    }

    [Fact]
    public void Keys_IsKeyFormVisible_FalseInitially()
    {
        var vm = CreateKeys();
        Assert.False(vm.IsKeyFormVisible);
    }

    [Fact]
    public void Keys_IsIssueFormVisible_FalseInitially()
    {
        var vm = CreateKeys();
        Assert.False(vm.IsIssueFormVisible);
    }

    // ── LoginViewModel ────────────────────────────────────────────────────────

    private LoginViewModel CreateLogin() => new(_authService, _tokenStore);

    [Fact]
    public void Login_InitialState_IsNotLoading()
    {
        var vm = CreateLogin();
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public void Login_CanLogin_FalseWhenCredentialsEmpty()
    {
        var vm = CreateLogin();
        vm.ServerUrl = string.Empty;
        Assert.False(vm.LoginCommand.CanExecute(null));
    }

    [Fact]
    public void Login_CanLogin_FalseWhenUsernameEmpty()
    {
        var vm = CreateLogin();
        vm.ServerUrl = "https://example.com";
        vm.Username  = string.Empty;
        vm.Password  = "pass";
        Assert.False(vm.LoginCommand.CanExecute(null));
    }

    [Fact]
    public void Login_CanLogin_TrueWhenAllFieldsFilled()
    {
        var vm = CreateLogin();
        vm.ServerUrl = "https://example.com";
        vm.Username  = "user";
        vm.Password  = "pass";
        Assert.True(vm.LoginCommand.CanExecute(null));
    }

    [Fact]
    public void Login_CanLogin_FalseWhenLoading()
    {
        var vm = CreateLogin();
        vm.ServerUrl = "https://example.com";
        vm.Username  = "user";
        vm.Password  = "pass";
        vm.IsLoading = true;
        Assert.False(vm.LoginCommand.CanExecute(null));
    }

    // ── SettingsViewModel ─────────────────────────────────────────────────────

    private SettingsViewModel CreateSettings()
    {
        var httpFactory = new Mock<IHttpClientFactory>().Object;
        var updateService = new UpdateService(NullLogger<UpdateService>.Instance, httpFactory);
        return new SettingsViewModel(_factory, _authService, _tokenStore, updateService);
    }

    [Fact]
    public void Settings_InitialState_IsNotLoading()
    {
        var vm = CreateSettings();
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public void Settings_IsAdmin_FalseWhenNotAuthenticated()
    {
        var vm = CreateSettings();
        Assert.False(vm.IsAdmin);
    }

    [Fact]
    public void Settings_CurrentUsername_NullWhenNotAuthenticated()
    {
        var vm = CreateSettings();
        Assert.Null(vm.CurrentUsername);
    }

    [Fact]
    public void Settings_CurrentVersion_IsNotEmpty()
    {
        var vm = CreateSettings();
        Assert.NotEmpty(vm.CurrentVersion);
    }

    [Fact]
    public void Settings_IsProjectFormVisible_FalseInitially()
    {
        var vm = CreateSettings();
        Assert.False(vm.IsProjectFormVisible);
    }

    // ── WlsViewModel ──────────────────────────────────────────────────────────

    private WlsViewModel CreateWls()
    {
        var filePicker = new Mock<IFilePickerService>().Object;
        return new WlsViewModel(_factory, _authService, filePicker);
    }

    [Fact]
    public void Wls_InitialState_IsNotLoading()
    {
        var vm = CreateWls();
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public void Wls_InitialState_CollectionsAreEmpty()
    {
        var vm = CreateWls();
        Assert.Empty(vm.Buildings);
        Assert.Empty(vm.Apartments);
        Assert.Empty(vm.Records);
    }

    [Fact]
    public void Wls_IsBuildingFormVisible_FalseInitially()
    {
        var vm = CreateWls();
        Assert.False(vm.IsBuildingFormVisible);
    }

    [Fact]
    public void Wls_IsApartmentFormVisible_FalseInitially()
    {
        var vm = CreateWls();
        Assert.False(vm.IsApartmentFormVisible);
    }

    [Fact]
    public void Wls_IsRecordFormVisible_FalseInitially()
    {
        var vm = CreateWls();
        Assert.False(vm.IsRecordFormVisible);
    }
}
