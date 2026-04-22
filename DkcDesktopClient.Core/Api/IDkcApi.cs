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

    // NEA write
    [Post("/api.php?action=nea_system_create")]
    Task<CreateIdResponse> CreateNeaSystemAsync([Body] NeaSystemSaveRequest request, CancellationToken ct = default);

    [Put("/api.php?action=nea_system_update")]
    Task<ApiError> UpdateNeaSystemAsync([Query] int id, [Body] NeaSystemSaveRequest request, CancellationToken ct = default);

    [Delete("/api.php?action=nea_system_delete")]
    Task<ApiError> DeleteNeaSystemAsync([Query] int id, CancellationToken ct = default);

    [Post("/api.php?action=nea_inspection_create")]
    Task<CreateIdResponse> CreateNeaInspectionAsync([Body] NeaInspectionSaveRequest request, CancellationToken ct = default);

    [Put("/api.php?action=nea_inspection_update")]
    Task<ApiError> UpdateNeaInspectionAsync([Query] int id, [Body] NeaInspectionSaveRequest request, CancellationToken ct = default);

    [Post("/api.php?action=nea_inspection_complete")]
    Task<ApiError> CompleteNeaInspectionAsync([Query] int id, [Body] NeaInspectionCompleteRequest request, CancellationToken ct = default);

    [Post("/api.php?action=nea_checklist_update")]
    Task<ApiError> UpdateNeaChecklistAsync([Query("inspection_id")] int inspectionId, [Body] NeaChecklistUpdateRequest request, CancellationToken ct = default);

    // MM write
    [Post("/api.php?action=mm_create")]
    Task<CreateUidResponse> CreateMmAsync([Body] MmSaveRequest request, CancellationToken ct = default);

    [Put("/api.php?action=mm_update")]
    Task<ApiError> UpdateMmAsync([Query] string uid, [Body] MmSaveRequest request, CancellationToken ct = default);

    [Post("/api.php?action=mm_update_status")]
    Task<ApiError> UpdateMmStatusAsync([Query] string uid, [Body] MmStatusUpdateRequest request, CancellationToken ct = default);

    [Post("/api.php?action=mm_assign_contractor")]
    Task<ApiError> AssignMmContractorAsync([Query] string uid, [Body] MmAssignContractorRequest request, CancellationToken ct = default);

    [Delete("/api.php?action=mm_delete")]
    Task<ApiError> DeleteMmAsync([Query] string uid, CancellationToken ct = default);

    // Building write
    [Post("/api.php?action=building_create")]
    Task<CreateIdResponse> CreateBuildingAsync([Body] BuildingSaveRequest request, CancellationToken ct = default);

    [Put("/api.php?action=building_update")]
    Task<ApiError> UpdateBuildingAsync([Query] int id, [Body] BuildingSaveRequest request, CancellationToken ct = default);

    [Post("/api.php?action=building_inspection_create")]
    Task<CreateIdResponse> CreateBuildingInspectionAsync([Body] BuildingInspectionSaveRequest request, CancellationToken ct = default);

    [Put("/api.php?action=building_inspection_update")]
    Task<ApiError> UpdateBuildingInspectionAsync([Query] int id, [Body] BuildingInspectionSaveRequest request, CancellationToken ct = default);

    [Post("/api.php?action=building_inspection_complete")]
    Task<ApiError> CompleteBuildingInspectionAsync([Query] int id, [Body] BuildingInspectionCompleteRequest request, CancellationToken ct = default);

    [Post("/api.php?action=building_checkpoint_update")]
    Task<ApiError> UpdateBuildingCheckpointAsync([Query("inspection_id")] int inspectionId, [Body] BuildingCheckpointUpdateRequest request, CancellationToken ct = default);

    [Get("/api.php?action=building_checkpoints_list")]
    Task<BuildingCheckpointsResponse> GetBuildingCheckpointsAsync([Query("building_id")] int? buildingId = null, CancellationToken ct = default);

    // Klima write/control
    [Get("/api.php?action=klima_realtime_status")]
    Task<KlimaRealtimeStatusResponse> GetKlimaRealtimeStatusAsync(CancellationToken ct = default);

    [Post("/api.php?action=klima_device_control")]
    Task<ApiError> ControlKlimaDeviceAsync([Body] KlimaDeviceControlRequest request, CancellationToken ct = default);

    [Post("/api.php?action=klima_group_control")]
    Task<ApiError> ControlKlimaGroupAsync([Body] KlimaGroupControlRequest request, CancellationToken ct = default);

    [Get("/api.php?action=klima_groups_list")]
    Task<KlimaGroupsResponse> GetKlimaGroupsAsync(CancellationToken ct = default);

    [Put("/api.php?action=klima_device_update")]
    Task<ApiError> UpdateKlimaDeviceAsync([Query] int address, [Body] KlimaDeviceUpdateRequest request, CancellationToken ct = default);

    // Keys write
    [Post("/api.php?action=keys_create")]
    Task<CreateIdResponse> CreateKeyAsync([Body] KeyInventorySaveRequest request, CancellationToken ct = default);

    [Put("/api.php?action=keys_update")]
    Task<ApiError> UpdateKeyAsync([Query] int id, [Body] KeyInventorySaveRequest request, CancellationToken ct = default);

    [Post("/api.php?action=keys_issue")]
    Task<CreateIdResponse> IssueKeyAsync([Body] KeyIssueRequest request, CancellationToken ct = default);

    [Post("/api.php?action=keys_return")]
    Task<ApiError> ReturnKeyAsync([Query] int id, [Body] KeyReturnRequest request, CancellationToken ct = default);

    [Delete("/api.php?action=keys_delete")]
    Task<ApiError> DeleteKeyIssuedAsync([Query] int id, CancellationToken ct = default);

    // Projects write
    [Post("/api.php?action=project_create")]
    Task<CreateIdResponse> CreateProjectAsync([Body] ProjectSaveRequest request, CancellationToken ct = default);

    [Put("/api.php?action=project_update")]
    Task<ApiError> UpdateProjectAsync([Query] int id, [Body] ProjectSaveRequest request, CancellationToken ct = default);

    [Post("/api.php?action=project_set_active")]
    Task<ApiError> SetActiveProjectAsync([Body] ProjectSetActiveRequest request, CancellationToken ct = default);

    // Admin users
    [Get("/api.php?action=users_list")]
    Task<AdminUsersListResponse> GetUsersListAsync(CancellationToken ct = default);

    [Post("/api.php?action=user_create")]
    Task<CreateIdResponse> CreateUserAsync([Body] UserSaveRequest request, CancellationToken ct = default);

    [Put("/api.php?action=user_update")]
    Task<ApiError> UpdateUserAsync([Query] int id, [Body] UserSaveRequest request, CancellationToken ct = default);

    [Delete("/api.php?action=user_delete")]
    Task<ApiError> DeleteUserAsync([Query] int id, CancellationToken ct = default);
}
