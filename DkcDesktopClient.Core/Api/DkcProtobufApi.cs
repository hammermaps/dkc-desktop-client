using DkcDesktopClient.Core.Protocol;
using DkcDesktopClient.Core.Services;
using Google.Protobuf;

namespace DkcDesktopClient.Core.Protobuf;

/// <summary>
/// Default <see cref="IDkcProtobufApi"/> implementation backed by
/// <see cref="DkcProtobufApiClient"/>. Each method merely delegates to
/// <c>SendAsync</c> with the matching <see cref="Protocol.Action"/> and
/// strongly-typed response message.
/// </summary>
public sealed class DkcProtobufApi : IDkcProtobufApi
{
    private readonly DkcProtobufApiClient _client;
    private readonly CompressionPreference _preference;

    public DkcProtobufApi(DkcProtobufApiClient client, CompressionPreference preference = CompressionPreference.Lz4)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _preference = preference;
    }

    private Task<TResponse> Send<TResponse>(Protocol.Action action, IMessage? payload, CancellationToken ct)
        where TResponse : IMessage<TResponse>, new()
        => _client.SendAsync<TResponse>(action, payload, _preference, ct);

    // ── Auth & users ────────────────────────────────────────────────────────
    public Task<AuthLoginResponse> LoginAsync(AuthLoginRequest request, CancellationToken ct = default)
        => Send<AuthLoginResponse>(Protocol.Action.AuthLogin, request, ct);
    public Task<Ack> LogoutAsync(CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.AuthLogout, null, ct);
    public Task<AuthStatusResponse> GetAuthStatusAsync(CancellationToken ct = default)
        => Send<AuthStatusResponse>(Protocol.Action.AuthStatus, null, ct);
    public Task<UserInfoResponse> GetUserInfoAsync(CancellationToken ct = default)
        => Send<UserInfoResponse>(Protocol.Action.UserInfo, null, ct);
    public Task<UserTokensListResponse> GetUserTokensAsync(CancellationToken ct = default)
        => Send<UserTokensListResponse>(Protocol.Action.UserTokensList, null, ct);
    public Task<Ack> DeleteUserTokenAsync(UserTokenDeleteRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.UserTokenDelete, request, ct);

    // ── Mängelmeldungen ─────────────────────────────────────────────────────
    public Task<MmListResponse> GetMmListAsync(MmListRequest request, CancellationToken ct = default)
        => Send<MmListResponse>(Protocol.Action.MmList, request, ct);
    public Task<MmDetailResponse> GetMmDetailAsync(MmDetailRequest request, CancellationToken ct = default)
        => Send<MmDetailResponse>(Protocol.Action.MmDetail, request, ct);
    public Task<MmCreateResponse> CreateMmAsync(MmSaveRequest request, CancellationToken ct = default)
        => Send<MmCreateResponse>(Protocol.Action.MmCreate, request, ct);
    public Task<Ack> UpdateMmAsync(MmSaveRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.MmUpdate, request, ct);
    public Task<Ack> UpdateMmStatusAsync(MmUpdateStatusRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.MmUpdateStatus, request, ct);
    public Task<Ack> AssignMmContractorAsync(MmAssignContractorRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.MmAssignContractor, request, ct);
    public Task<Ack> DeleteMmAsync(MmDeleteRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.MmDelete, request, ct);

    // ── NEA ─────────────────────────────────────────────────────────────────
    public Task<NeaSystemsResponse> GetNeaSystemsAsync(NeaSystemsRequest request, CancellationToken ct = default)
        => Send<NeaSystemsResponse>(Protocol.Action.NeaSystems, request, ct);
    public Task<NeaSystemCreateResponse> CreateNeaSystemAsync(NeaSystemSaveRequest request, CancellationToken ct = default)
        => Send<NeaSystemCreateResponse>(Protocol.Action.NeaSystemCreate, request, ct);
    public Task<Ack> UpdateNeaSystemAsync(NeaSystemSaveRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.NeaSystemUpdate, request, ct);
    public Task<Ack> DeleteNeaSystemAsync(NeaSystemDeleteRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.NeaSystemDelete, request, ct);
    public Task<NeaInspectionsResponse> GetNeaInspectionsAsync(NeaInspectionsRequest request, CancellationToken ct = default)
        => Send<NeaInspectionsResponse>(Protocol.Action.NeaInspections, request, ct);
    public Task<NeaInspectionDetailResponse> GetNeaInspectionDetailAsync(NeaInspectionDetailRequest request, CancellationToken ct = default)
        => Send<NeaInspectionDetailResponse>(Protocol.Action.NeaInspectionDetail, request, ct);
    public Task<NeaInspectionCreateResponse> CreateNeaInspectionAsync(NeaInspectionSaveRequest request, CancellationToken ct = default)
        => Send<NeaInspectionCreateResponse>(Protocol.Action.NeaInspectionCreate, request, ct);
    public Task<Ack> UpdateNeaInspectionAsync(NeaInspectionSaveRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.NeaInspectionUpdate, request, ct);
    public Task<Ack> CompleteNeaInspectionAsync(NeaInspectionCompleteRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.NeaInspectionComplete, request, ct);
    public Task<Ack> UpdateNeaChecklistAsync(NeaChecklistUpdateRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.NeaChecklistUpdate, request, ct);
    public Task<NeaDashboardResponse> GetNeaDashboardAsync(CancellationToken ct = default)
        => Send<NeaDashboardResponse>(Protocol.Action.NeaDashboard, null, ct);

    // ── Gebäudebegehungen ───────────────────────────────────────────────────
    public Task<BuildingListResponse> GetBuildingListAsync(BuildingListRequest request, CancellationToken ct = default)
        => Send<BuildingListResponse>(Protocol.Action.BuildingList, request, ct);
    public Task<BuildingCreateResponse> CreateBuildingAsync(BuildingSaveRequest request, CancellationToken ct = default)
        => Send<BuildingCreateResponse>(Protocol.Action.BuildingCreate, request, ct);
    public Task<Ack> UpdateBuildingAsync(BuildingSaveRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.BuildingUpdate, request, ct);
    public Task<BuildingInspectionsResponse> GetBuildingInspectionsAsync(BuildingInspectionsRequest request, CancellationToken ct = default)
        => Send<BuildingInspectionsResponse>(Protocol.Action.BuildingInspections, request, ct);
    public Task<BuildingInspectionDetailResponse> GetBuildingInspectionDetailAsync(BuildingInspectionDetailRequest request, CancellationToken ct = default)
        => Send<BuildingInspectionDetailResponse>(Protocol.Action.BuildingInspectionDetail, request, ct);
    public Task<BuildingInspectionCreateResponse> CreateBuildingInspectionAsync(BuildingInspectionSaveRequest request, CancellationToken ct = default)
        => Send<BuildingInspectionCreateResponse>(Protocol.Action.BuildingInspectionCreate, request, ct);
    public Task<Ack> UpdateBuildingInspectionAsync(BuildingInspectionSaveRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.BuildingInspectionUpdate, request, ct);
    public Task<Ack> CompleteBuildingInspectionAsync(BuildingInspectionCompleteRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.BuildingInspectionComplete, request, ct);
    public Task<Ack> UpdateBuildingCheckpointAsync(BuildingCheckpointUpdateRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.BuildingCheckpointUpdate, request, ct);
    public Task<BuildingCheckpointsListResponse> GetBuildingCheckpointsAsync(BuildingCheckpointsListRequest request, CancellationToken ct = default)
        => Send<BuildingCheckpointsListResponse>(Protocol.Action.BuildingCheckpointsList, request, ct);

    // ── Klima ───────────────────────────────────────────────────────────────
    public Task<KlimaDevicesResponse> GetKlimaDevicesAsync(CancellationToken ct = default)
        => Send<KlimaDevicesResponse>(Protocol.Action.KlimaDevices, null, ct);
    public Task<KlimaStatusResponse> GetKlimaStatusAsync(CancellationToken ct = default)
        => Send<KlimaStatusResponse>(Protocol.Action.KlimaStatus, null, ct);
    public Task<KlimaRealtimeStatusResponse> GetKlimaRealtimeStatusAsync(CancellationToken ct = default)
        => Send<KlimaRealtimeStatusResponse>(Protocol.Action.KlimaRealtimeStatus, null, ct);
    public Task<Ack> ControlKlimaDeviceAsync(KlimaDeviceControlRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.KlimaDeviceControl, request, ct);
    public Task<Ack> ControlKlimaGroupAsync(KlimaGroupControlRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.KlimaGroupControl, request, ct);
    public Task<KlimaGroupsResponse> GetKlimaGroupsAsync(CancellationToken ct = default)
        => Send<KlimaGroupsResponse>(Protocol.Action.KlimaGroupsList, null, ct);
    public Task<Ack> UpdateKlimaDeviceAsync(KlimaDeviceUpdateRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.KlimaDeviceUpdate, request, ct);

    // ── Schlüsselverwaltung ─────────────────────────────────────────────────
    public Task<KeysInventoryResponse> GetKeysInventoryAsync(KeysInventoryRequest request, CancellationToken ct = default)
        => Send<KeysInventoryResponse>(Protocol.Action.KeysInventory, request, ct);
    public Task<KeysIssuedResponse> GetKeysIssuedAsync(KeysIssuedRequest request, CancellationToken ct = default)
        => Send<KeysIssuedResponse>(Protocol.Action.KeysIssued, request, ct);
    public Task<KeyCreateResponse> CreateKeyAsync(KeyInventorySaveRequest request, CancellationToken ct = default)
        => Send<KeyCreateResponse>(Protocol.Action.KeysCreate, request, ct);
    public Task<Ack> UpdateKeyAsync(KeyInventorySaveRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.KeysUpdate, request, ct);
    public Task<KeyIssueResponse> IssueKeyAsync(KeyIssueRequest request, CancellationToken ct = default)
        => Send<KeyIssueResponse>(Protocol.Action.KeysIssue, request, ct);
    public Task<Ack> ReturnKeyAsync(KeyReturnRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.KeysReturn, request, ct);
    public Task<Ack> DeleteKeyAsync(KeyDeleteRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.KeysDelete, request, ct);

    // ── Dashboard & Projekte ────────────────────────────────────────────────
    public Task<DashboardDataResponse> GetDashboardDataAsync(CancellationToken ct = default)
        => Send<DashboardDataResponse>(Protocol.Action.DashboardData, null, ct);
    public Task<ProjectsListResponse> GetProjectsListAsync(CancellationToken ct = default)
        => Send<ProjectsListResponse>(Protocol.Action.ProjectsList, null, ct);
    public Task<ProjectCreateResponse> CreateProjectAsync(ProjectSaveRequest request, CancellationToken ct = default)
        => Send<ProjectCreateResponse>(Protocol.Action.ProjectCreate, request, ct);
    public Task<Ack> UpdateProjectAsync(ProjectSaveRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.ProjectUpdate, request, ct);
    public Task<Ack> SetActiveProjectAsync(ProjectSetActiveRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.ProjectSetActive, request, ct);

    // ── Benutzerverwaltung (Admin) ──────────────────────────────────────────
    public Task<UsersListResponse> GetUsersAsync(CancellationToken ct = default)
        => Send<UsersListResponse>(Protocol.Action.UsersList, null, ct);
    public Task<UserCreateResponse> CreateUserAsync(UserSaveRequest request, CancellationToken ct = default)
        => Send<UserCreateResponse>(Protocol.Action.UserCreate, request, ct);
    public Task<Ack> UpdateUserAsync(UserSaveRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.UserUpdate, request, ct);
    public Task<Ack> DeleteUserAsync(UserDeleteRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.UserDelete, request, ct);

    // ── Benachrichtigungen ──────────────────────────────────────────────────
    public Task<NotificationsResponse> GetNotificationsAsync(CancellationToken ct = default)
        => Send<NotificationsResponse>(Protocol.Action.Notifications, null, ct);
    public Task<NotificationCountResponse> GetNotificationCountAsync(CancellationToken ct = default)
        => Send<NotificationCountResponse>(Protocol.Action.NotificationCount, null, ct);

    // ── WLS ─────────────────────────────────────────────────────────────────
    public Task<WlsBuildingsListResponse> GetWlsBuildingsAsync(CancellationToken ct = default)
        => Send<WlsBuildingsListResponse>(Protocol.Action.WlsBuildingsList, null, ct);
    public Task<WlsBuildingResponse> CreateWlsBuildingAsync(WlsBuildingSaveRequest request, CancellationToken ct = default)
        => Send<WlsBuildingResponse>(Protocol.Action.WlsBuildingCreate, request, ct);
    public Task<WlsBuildingResponse> UpdateWlsBuildingAsync(WlsBuildingSaveRequest request, CancellationToken ct = default)
        => Send<WlsBuildingResponse>(Protocol.Action.WlsBuildingUpdate, request, ct);
    public Task<Ack> DeleteWlsBuildingAsync(WlsBuildingDeleteRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.WlsBuildingDelete, request, ct);
    public Task<WlsApartmentsListResponse> GetWlsApartmentsAsync(CancellationToken ct = default)
        => Send<WlsApartmentsListResponse>(Protocol.Action.WlsApartmentsList, null, ct);
    public Task<WlsApartmentsListResponse> GetWlsApartmentsByBuildingAsync(WlsApartmentsListRequest request, CancellationToken ct = default)
        => Send<WlsApartmentsListResponse>(Protocol.Action.WlsApartmentsByBuilding, request, ct);
    public Task<WlsApartmentResponse> CreateWlsApartmentAsync(WlsApartmentSaveRequest request, CancellationToken ct = default)
        => Send<WlsApartmentResponse>(Protocol.Action.WlsApartmentCreate, request, ct);
    public Task<WlsApartmentResponse> UpdateWlsApartmentAsync(WlsApartmentSaveRequest request, CancellationToken ct = default)
        => Send<WlsApartmentResponse>(Protocol.Action.WlsApartmentUpdate, request, ct);
    public Task<Ack> DeleteWlsApartmentAsync(WlsApartmentDeleteRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.WlsApartmentDelete, request, ct);
    public Task<WlsRecordsListResponse> GetWlsRecordsAsync(WlsRecordsListRequest request, CancellationToken ct = default)
        => Send<WlsRecordsListResponse>(Protocol.Action.WlsRecordsList, request, ct);
    public Task<WlsRecordResponse> CreateWlsRecordAsync(WlsRecordSaveRequest request, CancellationToken ct = default)
        => Send<WlsRecordResponse>(Protocol.Action.WlsRecordCreate, request, ct);
    public Task<WlsRecordResponse> UpdateWlsRecordAsync(WlsRecordSaveRequest request, CancellationToken ct = default)
        => Send<WlsRecordResponse>(Protocol.Action.WlsRecordUpdate, request, ct);
    public Task<Ack> DeleteWlsRecordAsync(WlsRecordDeleteRequest request, CancellationToken ct = default)
        => Send<Ack>(Protocol.Action.WlsRecordDelete, request, ct);
}
