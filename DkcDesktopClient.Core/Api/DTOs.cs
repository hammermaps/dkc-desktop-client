using System.Text.Json.Serialization;

namespace DkcDesktopClient.Core.Api;

// Auth
public record LoginRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("token_name")] string TokenName = "DKC Desktop",
    [property: JsonPropertyName("ttl_days")] int TtlDays = 30);

public record LoginResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("token_type")] string? TokenType,
    [property: JsonPropertyName("expires_at")] string? ExpiresAt,
    [property: JsonPropertyName("user")] UserInfo? User,
    [property: JsonPropertyName("error")] string? Error);

public record UserInfo(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("vname")] string Vname,
    [property: JsonPropertyName("nname")] string Nname,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("is_admin")] bool IsAdmin,
    [property: JsonPropertyName("active_project_id")] int? ActiveProjectId);

public record AuthStatusResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("authenticated")] bool Authenticated,
    [property: JsonPropertyName("user")] UserInfo? User,
    [property: JsonPropertyName("error")] string? Error);

public record UserInfoResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("user")] UserInfo? User,
    [property: JsonPropertyName("permissions")] Dictionary<string, bool>? Permissions,
    [property: JsonPropertyName("error")] string? Error);

public record TokenListItem(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("expires_at")] string? ExpiresAt,
    [property: JsonPropertyName("last_used_at")] string? LastUsedAt,
    [property: JsonPropertyName("last_ip")] string? LastIp,
    [property: JsonPropertyName("created_at")] string CreatedAt);

public record TokensListResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("tokens")] List<TokenListItem>? Tokens,
    [property: JsonPropertyName("error")] string? Error);

public record LogoutResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("error")] string? Error);

// NEA
public record NeaDashboardResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("dashboard")] NeaDashboardData? Dashboard,
    [property: JsonPropertyName("stats")] NeaDashboardStats? Stats,
    [property: JsonPropertyName("due_tests")] List<NeaOverdueItem>? DueTests,
    [property: JsonPropertyName("recent_inspections")] List<NeaRecentInspection>? RecentInspections,
    [property: JsonPropertyName("error")] string? Error);

public record NeaDashboardStats(
    [property: JsonPropertyName("total_systems")] int TotalSystems,
    [property: JsonPropertyName("inspections_this_week")] int InspectionsThisWeek,
    [property: JsonPropertyName("inspections_this_month")] int InspectionsThisMonth,
    [property: JsonPropertyName("failed_last_30_days")] int FailedLast30Days);

public record NeaDashboardData(
    [property: JsonPropertyName("total_systems")] int TotalSystems,
    [property: JsonPropertyName("overdue_inspections")] int OverdueInspections,
    [property: JsonPropertyName("overdue_items")] List<NeaOverdueItem>? OverdueItems,
    [property: JsonPropertyName("recent_inspections")] List<NeaRecentInspection>? RecentInspections);

public record NeaOverdueItem(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("system_id")] int? SystemId,
    [property: JsonPropertyName("system_name")] string SystemName,
    [property: JsonPropertyName("days_overdue")] int DaysOverdue,
    [property: JsonPropertyName("last_inspection")] string? LastInspection);

public record NeaRecentInspection(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("nea_system_id")] int NeaSystemId,
    [property: JsonPropertyName("inspection_date")] string InspectionDate,
    [property: JsonPropertyName("inspector_name")] string InspectorName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("overall_result")] string OverallResult);

public record NeaSystem(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("location")] string? Location,
    [property: JsonPropertyName("manufacturer")] string? Manufacturer,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("serial_number")] string? SerialNumber,
    [property: JsonPropertyName("installation_date")] string? InstallationDate,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("project_id")] int ProjectId,
    [property: JsonPropertyName("last_inspection_date")] string? LastInspectionDate,
    [property: JsonPropertyName("last_inspection_result")] string? LastInspectionResult);

public record NeaSystemsResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("project_id")] int? ProjectId,
    [property: JsonPropertyName("systems")] List<NeaSystem>? Systems,
    [property: JsonPropertyName("error")] string? Error);

public record NeaInspection(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("nea_system_id")] int NeaSystemId,
    [property: JsonPropertyName("system_name")] string? SystemName,
    [property: JsonPropertyName("inspection_type")] string InspectionType,
    [property: JsonPropertyName("inspection_date")] string InspectionDate,
    [property: JsonPropertyName("inspector_name")] string? InspectorName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("overall_result")] string OverallResult,
    [property: JsonPropertyName("runtime_hours")] int? RuntimeHours,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("created_at")] string? CreatedAt);

public record NeaInspectionsResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("project_id")] int? ProjectId,
    [property: JsonPropertyName("total")] int? Total,
    [property: JsonPropertyName("limit")] int? Limit,
    [property: JsonPropertyName("offset")] int? Offset,
    [property: JsonPropertyName("inspections")] List<NeaInspection>? Inspections,
    [property: JsonPropertyName("error")] string? Error);

public record NeaInspectionDetail(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("nea_system_id")] int NeaSystemId,
    [property: JsonPropertyName("system")] object? System,
    [property: JsonPropertyName("inspection_type")] string InspectionType,
    [property: JsonPropertyName("inspection_date")] string InspectionDate,
    [property: JsonPropertyName("inspector_id")] int? InspectorId,
    [property: JsonPropertyName("inspector_name")] string? InspectorName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("overall_result")] string OverallResult,
    [property: JsonPropertyName("runtime_hours")] int? RuntimeHours,
    [property: JsonPropertyName("runtime_hours_after")] int? RuntimeHoursAfter,
    [property: JsonPropertyName("defects_found")] string? DefectsFound,
    [property: JsonPropertyName("corrective_actions")] string? CorrectiveActions,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("checklist_data")] object? ChecklistData,
    [property: JsonPropertyName("created_at")] string? CreatedAt);

public record NeaInspectionDetailResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("inspection")] NeaInspectionDetail? Inspection,
    [property: JsonPropertyName("error")] string? Error);

// MM
public record MmMessage(
    [property: JsonPropertyName("uid")] string Uid,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("betreff")] string? Betreff,
    [property: JsonPropertyName("street")] string? Street,
    [property: JsonPropertyName("whg")] string? Whg,
    [property: JsonPropertyName("melder")] string? Melder,
    [property: JsonPropertyName("datetime")] string? Datetime,
    [property: JsonPropertyName("dringlichkeit")] string? Dringlichkeit,
    [property: JsonPropertyName("nachunternehmer")] string? Nachunternehmer,
    [property: JsonPropertyName("scanned")] bool Scanned,
    [property: JsonPropertyName("zugeh")] string? Zugeh);

public record MmListResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("total")] int? Total,
    [property: JsonPropertyName("limit")] int? Limit,
    [property: JsonPropertyName("offset")] int? Offset,
    [property: JsonPropertyName("messages")] List<MmMessage>? Messages,
    [property: JsonPropertyName("error")] string? Error);

public record MmDetail(
    [property: JsonPropertyName("uid")] string Uid,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("betreff")] string? Betreff,
    [property: JsonPropertyName("meldung_massage")] string? MeldungMassage,
    [property: JsonPropertyName("street")] string? Street,
    [property: JsonPropertyName("whg")] string? Whg,
    [property: JsonPropertyName("melder")] string? Melder,
    [property: JsonPropertyName("tel")] string? Tel,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("datetime")] string? Datetime,
    [property: JsonPropertyName("dringlichkeit")] string? Dringlichkeit,
    [property: JsonPropertyName("nachunternehmer")] string? Nachunternehmer,
    [property: JsonPropertyName("scanned")] bool Scanned,
    [property: JsonPropertyName("zugeh")] string? Zugeh);

public record MmDetailResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] MmDetail? Message,
    [property: JsonPropertyName("error")] string? Error);

// Building
public record Building(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("address")] string? Address,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("project_id")] int ProjectId);

public record BuildingListResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("project_id")] int? ProjectId,
    [property: JsonPropertyName("buildings")] List<Building>? Buildings,
    [property: JsonPropertyName("error")] string? Error);

public record BuildingInspection(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("building_id")] int BuildingId,
    [property: JsonPropertyName("building_name")] string? BuildingName,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("inspection_date")] string? InspectionDate,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("overall_result")] string? OverallResult,
    [property: JsonPropertyName("created_by_name")] string? CreatedByName,
    [property: JsonPropertyName("last_editor_name")] string? LastEditorName,
    [property: JsonPropertyName("weather")] string? Weather,
    [property: JsonPropertyName("attendees")] string? Attendees);

public record BuildingInspectionsResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("project_id")] int? ProjectId,
    [property: JsonPropertyName("total")] int? Total,
    [property: JsonPropertyName("limit")] int? Limit,
    [property: JsonPropertyName("offset")] int? Offset,
    [property: JsonPropertyName("inspections")] List<BuildingInspection>? Inspections,
    [property: JsonPropertyName("error")] string? Error);

public record CheckpointResult(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("checkpoint_id")] int CheckpointId,
    [property: JsonPropertyName("checkpoint_name")] string? CheckpointName,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("comment")] string? Comment,
    [property: JsonPropertyName("edited_by_name")] string? EditedByName,
    [property: JsonPropertyName("edited_at")] string? EditedAt);

public record BuildingInspectionDetail(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("building_id")] int BuildingId,
    [property: JsonPropertyName("building_name")] string? BuildingName,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("inspection_date")] string? InspectionDate,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("overall_result")] string? OverallResult,
    [property: JsonPropertyName("created_by_name")] string? CreatedByName,
    [property: JsonPropertyName("last_editor_name")] string? LastEditorName,
    [property: JsonPropertyName("weather")] string? Weather,
    [property: JsonPropertyName("attendees")] string? Attendees,
    [property: JsonPropertyName("general_notes")] string? GeneralNotes,
    [property: JsonPropertyName("results")] List<CheckpointResult>? Results);

public record BuildingInspectionDetailResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("inspection")] BuildingInspectionDetail? Inspection,
    [property: JsonPropertyName("error")] string? Error);

// Klima
public record KlimaDevice(
    [property: JsonPropertyName("address")] int Address,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("group_id")] int? GroupId,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("sort")] int Sort);

public record KlimaDevicesResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("devices")] List<KlimaDevice>? Devices,
    [property: JsonPropertyName("error")] string? Error);

public record KlimaStatusResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("status")] object? Status,
    [property: JsonPropertyName("error")] string? Error);

// Keys
public record KeyInventoryItem(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("total_count")] int? Total,
    [property: JsonPropertyName("available")] int? Available);

public record KeysInventoryResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("keys")] List<KeyInventoryItem>? Keys,
    [property: JsonPropertyName("error")] string? Error);

public record KeyIssuedItem(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("key_id")] int KeyId,
    [property: JsonPropertyName("key_name")] string? KeyName,
    [property: JsonPropertyName("issued_to")] string? IssuedTo,
    [property: JsonPropertyName("recipient_name")] string? RecipientName,
    [property: JsonPropertyName("issued_at")] string? IssuedAt,
    [property: JsonPropertyName("returned_at")] string? ReturnedAt);

public record KeysIssuedResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("keys")] List<KeyIssuedItem>? Keys,
    [property: JsonPropertyName("issued")] List<KeyIssuedItem>? Issued,
    [property: JsonPropertyName("error")] string? Error);

// Dashboard
public record Project(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description);

public record ProjectsListResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("projects")] List<Project>? Projects,
    [property: JsonPropertyName("error")] string? Error);

public record DashboardDataResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("data")] object? Data,
    [property: JsonPropertyName("error")] string? Error);

// Generic error
public record ApiError(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("error")] string? Error);

public record TokenDeleteRequest(
    [property: JsonPropertyName("token_id")] int TokenId);

// Generic create responses
public record CreateIdResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("error")] string? Error);

public record CreateUidResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("uid")] string? Uid,
    [property: JsonPropertyName("error")] string? Error);

// NEA write requests
public record NeaSystemSaveRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("location")] string? Location,
    [property: JsonPropertyName("manufacturer")] string? Manufacturer,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("serial_number")] string? SerialNumber,
    [property: JsonPropertyName("installation_date")] string? InstallationDate,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("project_id")] int? ProjectId);

public record NeaInspectionSaveRequest(
    [property: JsonPropertyName("nea_system_id")] int NeaSystemId,
    [property: JsonPropertyName("inspection_type")] string InspectionType,
    [property: JsonPropertyName("inspection_date")] string InspectionDate,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("overall_result")] string OverallResult,
    [property: JsonPropertyName("runtime_hours")] int? RuntimeHours,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("defects_found")] string? DefectsFound,
    [property: JsonPropertyName("corrective_actions")] string? CorrectiveActions);

public record NeaInspectionCompleteRequest(
    [property: JsonPropertyName("overall_result")] string OverallResult,
    [property: JsonPropertyName("notes")] string? Notes);

public record NeaChecklistUpdateItem(
    [property: JsonPropertyName("checkpoint_id")] int CheckpointId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("comment")] string? Comment);

public record NeaChecklistUpdateRequest(
    [property: JsonPropertyName("items")] List<NeaChecklistUpdateItem> Items);

// MM write requests
public record MmSaveRequest(
    [property: JsonPropertyName("betreff")] string Betreff,
    [property: JsonPropertyName("meldung_massage")] string? MeldungMassage,
    [property: JsonPropertyName("street")] string? Street,
    [property: JsonPropertyName("whg")] string? Whg,
    [property: JsonPropertyName("melder")] string? Melder,
    [property: JsonPropertyName("tel")] string? Tel,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("dringlichkeit")] string? Dringlichkeit,
    [property: JsonPropertyName("nachunternehmer")] string? Nachunternehmer,
    [property: JsonPropertyName("zugeh")] string? Zugeh);

public record MmStatusUpdateRequest(
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("comment")] string? Comment);

public record MmAssignContractorRequest(
    [property: JsonPropertyName("nachunternehmer")] string Nachunternehmer);

// Building write requests
public record BuildingSaveRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("address")] string? Address,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("project_id")] int? ProjectId);

public record BuildingInspectionSaveRequest(
    [property: JsonPropertyName("building_id")] int BuildingId,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("inspection_date")] string? InspectionDate,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("weather")] string? Weather,
    [property: JsonPropertyName("attendees")] string? Attendees,
    [property: JsonPropertyName("general_notes")] string? GeneralNotes);

public record BuildingInspectionCompleteRequest(
    [property: JsonPropertyName("overall_result")] string OverallResult,
    [property: JsonPropertyName("general_notes")] string? GeneralNotes);

public record BuildingCheckpointUpdateRequest(
    [property: JsonPropertyName("checkpoint_id")] int CheckpointId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("comment")] string? Comment);

public record CheckpointListItem(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("sort")] int Sort);

public record BuildingCheckpointsResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("checkpoints")] List<CheckpointListItem>? Checkpoints,
    [property: JsonPropertyName("error")] string? Error);

// Klima write/control requests + realtime response
public record KlimaDeviceControlRequest(
    [property: JsonPropertyName("address")] int Address,
    [property: JsonPropertyName("power")] bool? Power,
    [property: JsonPropertyName("mode")] string? Mode,
    [property: JsonPropertyName("setpoint")] double? Setpoint,
    [property: JsonPropertyName("fan_speed")] string? FanSpeed);

public record KlimaGroupControlRequest(
    [property: JsonPropertyName("group_id")] int GroupId,
    [property: JsonPropertyName("power")] bool? Power,
    [property: JsonPropertyName("mode")] string? Mode,
    [property: JsonPropertyName("setpoint")] double? Setpoint,
    [property: JsonPropertyName("fan_speed")] string? FanSpeed);

public record KlimaDeviceUpdateRequest(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("group_id")] int? GroupId,
    [property: JsonPropertyName("enabled")] bool? Enabled,
    [property: JsonPropertyName("sort")] int? Sort);

public record KlimaDeviceStatus(
    [property: JsonPropertyName("address")] int Address,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("online")] bool Online,
    [property: JsonPropertyName("power")] bool Power,
    [property: JsonPropertyName("operating_mode")] string? Mode,
    [property: JsonPropertyName("setpoint")] double? Setpoint,
    [property: JsonPropertyName("current_temp")] double? CurrentTemp,
    [property: JsonPropertyName("fan_speed")] string? FanSpeed,
    [property: JsonPropertyName("error_code")] string? ErrorCode);

public record KlimaRealtimeStatusResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("timestamp")] string? Timestamp,
    [property: JsonPropertyName("devices")] List<KlimaDeviceStatus>? Devices,
    [property: JsonPropertyName("error")] string? Error);

public record KlimaGroup(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("device_count")] int DeviceCount);

public record KlimaGroupsResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("groups")] List<KlimaGroup>? Groups,
    [property: JsonPropertyName("error")] string? Error);

// Keys write requests
public record KeyInventorySaveRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("total")] int Total);

public record KeyIssueRequest(
    [property: JsonPropertyName("key_id")] int KeyId,
    [property: JsonPropertyName("issued_to")] string IssuedTo,
    [property: JsonPropertyName("issued_at")] string IssuedAt,
    [property: JsonPropertyName("notes")] string? Notes);

public record KeyReturnRequest(
    [property: JsonPropertyName("returned_at")] string ReturnedAt,
    [property: JsonPropertyName("notes")] string? Notes);

// Projects write requests
public record ProjectSaveRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description);

public record ProjectSetActiveRequest(
    [property: JsonPropertyName("project_id")] int ProjectId);

// Admin users
public record AdminUser(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("vname")] string? Vname,
    [property: JsonPropertyName("nname")] string? Nname,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("is_admin")] bool IsAdmin,
    [property: JsonPropertyName("active_project_id")] int? ActiveProjectId);

public record AdminUsersListResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("users")] List<AdminUser>? Users,
    [property: JsonPropertyName("error")] string? Error);

public record UserSaveRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string? Password,
    [property: JsonPropertyName("vname")] string? Vname,
    [property: JsonPropertyName("nname")] string? Nname,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("is_admin")] bool IsAdmin);
