using DkcDesktopClient.Core.Protocol;

namespace DkcDesktopClient.Core.Protobuf;

/// <summary>
/// High-level, action-oriented Protobuf API for the DKC backend. Every method
/// maps 1:1 to an <see cref="DkcDesktopClient.Core.Protocol.Action"/> value.
///
/// All requests use the single <c>POST /api.php</c> Protobuf endpoint with
/// LZ4 (preferred) or Gzip-compressed envelopes. See
/// <c>docs/PROTOBUF_API.md</c> for the wire-level documentation.
///
/// Errors from the server arrive as <see cref="DkcDesktopClient.Core.Services.DkcProtobufApiException"/>.
/// </summary>
public interface IDkcProtobufApi
{
    // ── Auth & users ────────────────────────────────────────────────────────
    Task<AuthLoginResponse> LoginAsync(AuthLoginRequest request, CancellationToken ct = default);
    Task<Ack> LogoutAsync(CancellationToken ct = default);
    Task<AuthStatusResponse> GetAuthStatusAsync(CancellationToken ct = default);
    Task<UserInfoResponse> GetUserInfoAsync(CancellationToken ct = default);
    Task<UserTokensListResponse> GetUserTokensAsync(CancellationToken ct = default);
    Task<Ack> DeleteUserTokenAsync(UserTokenDeleteRequest request, CancellationToken ct = default);

    // ── Mängelmeldungen ─────────────────────────────────────────────────────
    Task<MmListResponse> GetMmListAsync(MmListRequest request, CancellationToken ct = default);
    Task<MmDetailResponse> GetMmDetailAsync(MmDetailRequest request, CancellationToken ct = default);
    Task<MmCreateResponse> CreateMmAsync(MmSaveRequest request, CancellationToken ct = default);
    Task<Ack> UpdateMmAsync(MmSaveRequest request, CancellationToken ct = default);
    Task<Ack> UpdateMmStatusAsync(MmUpdateStatusRequest request, CancellationToken ct = default);
    Task<Ack> AssignMmContractorAsync(MmAssignContractorRequest request, CancellationToken ct = default);
    Task<Ack> DeleteMmAsync(MmDeleteRequest request, CancellationToken ct = default);

    // ── NEA ─────────────────────────────────────────────────────────────────
    Task<NeaSystemsResponse> GetNeaSystemsAsync(NeaSystemsRequest request, CancellationToken ct = default);
    Task<NeaSystemCreateResponse> CreateNeaSystemAsync(NeaSystemSaveRequest request, CancellationToken ct = default);
    Task<Ack> UpdateNeaSystemAsync(NeaSystemSaveRequest request, CancellationToken ct = default);
    Task<Ack> DeleteNeaSystemAsync(NeaSystemDeleteRequest request, CancellationToken ct = default);
    Task<NeaInspectionsResponse> GetNeaInspectionsAsync(NeaInspectionsRequest request, CancellationToken ct = default);
    Task<NeaInspectionDetailResponse> GetNeaInspectionDetailAsync(NeaInspectionDetailRequest request, CancellationToken ct = default);
    Task<NeaInspectionCreateResponse> CreateNeaInspectionAsync(NeaInspectionSaveRequest request, CancellationToken ct = default);
    Task<Ack> UpdateNeaInspectionAsync(NeaInspectionSaveRequest request, CancellationToken ct = default);
    Task<Ack> CompleteNeaInspectionAsync(NeaInspectionCompleteRequest request, CancellationToken ct = default);
    Task<Ack> UpdateNeaChecklistAsync(NeaChecklistUpdateRequest request, CancellationToken ct = default);
    Task<NeaDashboardResponse> GetNeaDashboardAsync(CancellationToken ct = default);

    // ── Gebäudebegehungen ───────────────────────────────────────────────────
    Task<BuildingListResponse> GetBuildingListAsync(BuildingListRequest request, CancellationToken ct = default);
    Task<BuildingCreateResponse> CreateBuildingAsync(BuildingSaveRequest request, CancellationToken ct = default);
    Task<Ack> UpdateBuildingAsync(BuildingSaveRequest request, CancellationToken ct = default);
    Task<BuildingInspectionsResponse> GetBuildingInspectionsAsync(BuildingInspectionsRequest request, CancellationToken ct = default);
    Task<BuildingInspectionDetailResponse> GetBuildingInspectionDetailAsync(BuildingInspectionDetailRequest request, CancellationToken ct = default);
    Task<BuildingInspectionCreateResponse> CreateBuildingInspectionAsync(BuildingInspectionSaveRequest request, CancellationToken ct = default);
    Task<Ack> UpdateBuildingInspectionAsync(BuildingInspectionSaveRequest request, CancellationToken ct = default);
    Task<Ack> CompleteBuildingInspectionAsync(BuildingInspectionCompleteRequest request, CancellationToken ct = default);
    Task<Ack> UpdateBuildingCheckpointAsync(BuildingCheckpointUpdateRequest request, CancellationToken ct = default);
    Task<BuildingCheckpointsListResponse> GetBuildingCheckpointsAsync(BuildingCheckpointsListRequest request, CancellationToken ct = default);

    // ── Klima ───────────────────────────────────────────────────────────────
    Task<KlimaDevicesResponse> GetKlimaDevicesAsync(CancellationToken ct = default);
    Task<KlimaStatusResponse> GetKlimaStatusAsync(CancellationToken ct = default);
    Task<KlimaRealtimeStatusResponse> GetKlimaRealtimeStatusAsync(CancellationToken ct = default);
    Task<Ack> ControlKlimaDeviceAsync(KlimaDeviceControlRequest request, CancellationToken ct = default);
    Task<Ack> ControlKlimaGroupAsync(KlimaGroupControlRequest request, CancellationToken ct = default);
    Task<KlimaGroupsResponse> GetKlimaGroupsAsync(CancellationToken ct = default);
    Task<Ack> UpdateKlimaDeviceAsync(KlimaDeviceUpdateRequest request, CancellationToken ct = default);

    // ── Schlüsselverwaltung ─────────────────────────────────────────────────
    Task<KeysInventoryResponse> GetKeysInventoryAsync(KeysInventoryRequest request, CancellationToken ct = default);
    Task<KeysIssuedResponse> GetKeysIssuedAsync(KeysIssuedRequest request, CancellationToken ct = default);
    Task<KeyCreateResponse> CreateKeyAsync(KeyInventorySaveRequest request, CancellationToken ct = default);
    Task<Ack> UpdateKeyAsync(KeyInventorySaveRequest request, CancellationToken ct = default);
    Task<KeyIssueResponse> IssueKeyAsync(KeyIssueRequest request, CancellationToken ct = default);
    Task<Ack> ReturnKeyAsync(KeyReturnRequest request, CancellationToken ct = default);
    Task<Ack> DeleteKeyAsync(KeyDeleteRequest request, CancellationToken ct = default);

    // ── Dashboard & Projekte ────────────────────────────────────────────────
    Task<DashboardDataResponse> GetDashboardDataAsync(CancellationToken ct = default);
    Task<ProjectsListResponse> GetProjectsListAsync(CancellationToken ct = default);
    Task<ProjectCreateResponse> CreateProjectAsync(ProjectSaveRequest request, CancellationToken ct = default);
    Task<Ack> UpdateProjectAsync(ProjectSaveRequest request, CancellationToken ct = default);
    Task<Ack> SetActiveProjectAsync(ProjectSetActiveRequest request, CancellationToken ct = default);

    // ── Benutzerverwaltung (Admin) ──────────────────────────────────────────
    Task<UsersListResponse> GetUsersAsync(CancellationToken ct = default);
    Task<UserCreateResponse> CreateUserAsync(UserSaveRequest request, CancellationToken ct = default);
    Task<Ack> UpdateUserAsync(UserSaveRequest request, CancellationToken ct = default);
    Task<Ack> DeleteUserAsync(UserDeleteRequest request, CancellationToken ct = default);

    // ── Benachrichtigungen ──────────────────────────────────────────────────
    Task<NotificationsResponse> GetNotificationsAsync(CancellationToken ct = default);
    Task<NotificationCountResponse> GetNotificationCountAsync(CancellationToken ct = default);

    // ── WLS ─────────────────────────────────────────────────────────────────
    Task<WlsBuildingsListResponse> GetWlsBuildingsAsync(CancellationToken ct = default);
    Task<WlsBuildingResponse> CreateWlsBuildingAsync(WlsBuildingSaveRequest request, CancellationToken ct = default);
    Task<WlsBuildingResponse> UpdateWlsBuildingAsync(WlsBuildingSaveRequest request, CancellationToken ct = default);
    Task<Ack> DeleteWlsBuildingAsync(WlsBuildingDeleteRequest request, CancellationToken ct = default);
    Task<WlsApartmentsListResponse> GetWlsApartmentsAsync(CancellationToken ct = default);
    Task<WlsApartmentsListResponse> GetWlsApartmentsByBuildingAsync(WlsApartmentsListRequest request, CancellationToken ct = default);
    Task<WlsApartmentResponse> CreateWlsApartmentAsync(WlsApartmentSaveRequest request, CancellationToken ct = default);
    Task<WlsApartmentResponse> UpdateWlsApartmentAsync(WlsApartmentSaveRequest request, CancellationToken ct = default);
    Task<Ack> DeleteWlsApartmentAsync(WlsApartmentDeleteRequest request, CancellationToken ct = default);
    Task<WlsRecordsListResponse> GetWlsRecordsAsync(WlsRecordsListRequest request, CancellationToken ct = default);
    Task<WlsRecordResponse> CreateWlsRecordAsync(WlsRecordSaveRequest request, CancellationToken ct = default);
    Task<WlsRecordResponse> UpdateWlsRecordAsync(WlsRecordSaveRequest request, CancellationToken ct = default);
    Task<Ack> DeleteWlsRecordAsync(WlsRecordDeleteRequest request, CancellationToken ct = default);
}
