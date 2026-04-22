using Refit;

namespace DkcDesktopClient.Core.Api;

public interface IDkcApi
{
    [Post("/api.php?action=auth_login")]
    Task<LoginResponse> LoginAsync([Body] LoginRequest request, CancellationToken ct = default);

    [Post("/api.php?action=auth_logout")]
    Task<LogoutResponse> LogoutAsync(CancellationToken ct = default);

    [Get("/api.php?action=auth_status")]
    Task<AuthStatusResponse> GetAuthStatusAsync(CancellationToken ct = default);

    [Get("/api.php?action=user_info")]
    Task<UserInfoResponse> GetUserInfoAsync(CancellationToken ct = default);

    [Get("/api.php?action=user_tokens_list")]
    Task<TokensListResponse> GetTokensListAsync(CancellationToken ct = default);

    [Delete("/api.php?action=user_token_delete")]
    Task<ApiError> DeleteTokenAsync([Query] int id, CancellationToken ct = default);

    [Get("/api.php?action=nea_dashboard")]
    Task<NeaDashboardResponse> GetNeaDashboardAsync(CancellationToken ct = default);

    [Get("/api.php?action=nea_systems")]
    Task<NeaSystemsResponse> GetNeaSystemsAsync([Query("project_id")] int? projectId = null, CancellationToken ct = default);

    [Get("/api.php?action=nea_inspections")]
    Task<NeaInspectionsResponse> GetNeaInspectionsAsync(
        [Query("system_id")] int? systemId = null,
        [Query] int? year = null,
        [Query] string? status = null,
        [Query] int? limit = null,
        [Query] int? offset = null,
        CancellationToken ct = default);

    [Get("/api.php?action=nea_inspection_detail")]
    Task<NeaInspectionDetailResponse> GetNeaInspectionDetailAsync([Query] int id, CancellationToken ct = default);

    [Get("/api.php?action=mm_list")]
    Task<MmListResponse> GetMmListAsync(
        [Query] int? status = null,
        [Query] string? street = null,
        [Query] int? limit = null,
        [Query] int? offset = null,
        CancellationToken ct = default);

    [Get("/api.php?action=mm_detail")]
    Task<MmDetailResponse> GetMmDetailAsync([Query] string uid, CancellationToken ct = default);

    [Get("/api.php?action=building_list")]
    Task<BuildingListResponse> GetBuildingListAsync([Query("project_id")] int? projectId = null, CancellationToken ct = default);

    [Get("/api.php?action=building_inspections")]
    Task<BuildingInspectionsResponse> GetBuildingInspectionsAsync(
        [Query("building_id")] int? buildingId = null,
        [Query] string? status = null,
        [Query] int? year = null,
        [Query] int? limit = null,
        [Query] int? offset = null,
        CancellationToken ct = default);

    [Get("/api.php?action=building_inspection_detail")]
    Task<BuildingInspectionDetailResponse> GetBuildingInspectionDetailAsync([Query] int id, CancellationToken ct = default);

    [Get("/api.php?action=klima_devices")]
    Task<KlimaDevicesResponse> GetKlimaDevicesAsync(CancellationToken ct = default);

    [Get("/api.php?action=klima_status")]
    Task<KlimaStatusResponse> GetKlimaStatusAsync(CancellationToken ct = default);

    [Get("/api.php?action=keys_inventory")]
    Task<KeysInventoryResponse> GetKeysInventoryAsync(CancellationToken ct = default);

    [Get("/api.php?action=keys_issued")]
    Task<KeysIssuedResponse> GetKeysIssuedAsync(CancellationToken ct = default);

    [Get("/api.php?action=dashboard_data")]
    Task<DashboardDataResponse> GetDashboardDataAsync(CancellationToken ct = default);

    [Get("/api.php?action=projects_list")]
    Task<ProjectsListResponse> GetProjectsListAsync(CancellationToken ct = default);
}
