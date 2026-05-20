# DKC Protobuf API – Schnittstellen-Dokumentation

> Wire-level documentation for the DKC backend's protobuf API.
> Audience: backend developers (PHP / `api.php`), desktop-client developers,
> integration partners writing alternative clients.
>
> **Backend-Implementierung:** Eine vollständige, ausführbare Agent-Anweisung
> für das PHP-Backend (Router, Codegen, Dispatcher, Tests) liegt unter
> [`BACKEND_AGENT.md`](./BACKEND_AGENT.md).

The DKC platform exposes a single, action-oriented HTTP endpoint that speaks
[Protocol&nbsp;Buffers&nbsp;v3](https://protobuf.dev/) end-to-end. The desktop
client uses exactly this endpoint for everything except the lightweight
HTTP health probe; there is no parallel JSON/REST API in the new model.

- **Endpoint:** `POST /api.php`
- **Content-Type:** `application/x-protobuf` (request *and* response)
- **Protocol marker header:** `X-DKC-Protocol: protobuf`
- **Compression:** `dkc-lz4` preferred; `gzip` fallback; `identity` for
  small payloads or diagnostics.
- **Ping/health probe:** `GET /api.php/health` returns a plain
  `text/plain` body of `ok` (no protobuf, no compression). All other traffic
  uses the envelope below.

The full schema lives under [`/proto/dkc/`](../proto/dkc) and is the single
source of truth.

---

## 1. Envelope schema

All requests and responses are a single message wrapped in an
`ApiRequest` / `ApiResponse` envelope. The action-specific protobuf message
(for example `MmListRequest`) is serialised and placed in the envelope's
`bytes payload` field together with the chosen compression.

```proto
// proto/dkc/common.proto

message ApiRequest {
    uint32      protocol_version = 1; // current = 1
    string      request_id       = 2; // client-chosen GUID
    Action      action           = 3; // see § 4
    AuthContext auth             = 4;
    Compression compression      = 5; // applied to `payload`
    bytes       payload          = 6; // serialised action-specific message
}

message ApiResponse {
    uint32      protocol_version = 1;
    string      request_id       = 2; // echoed from the request
    bool        success          = 3;
    ApiError    error            = 4; // set iff success == false
    int64       server_time      = 5; // UTC unix seconds
    Compression compression      = 6;
    bytes       payload          = 7;
}
```

`AuthContext` carries one of:

| field        | description                                       |
|--------------|---------------------------------------------------|
| `bearer_token` | User-API token (`dkc_<64-hex>`) – default path  |
| `api_key`      | System-integration UUID-v4                      |
| `session_id`   | Optional PHP session id (browser bridge only)   |

`ApiError`:

| field                  | description                                  |
|------------------------|----------------------------------------------|
| `code`                 | Stable `ErrorCode` enum value (see § 5)      |
| `message`              | Human-readable, locale-free                  |
| `details`              | `map<string,string>` for field-level info    |
| `retry_after_seconds`  | Set for `RATE_LIMITED` / `SERVICE_UNAVAILABLE` |

---

## 2. HTTP transport

### 2.1 Request headers

```
POST /api.php HTTP/1.1
Host: <server>
Content-Type:     application/x-protobuf
Accept:           application/x-protobuf
X-DKC-Protocol:   protobuf
Accept-Encoding:  dkc-lz4, gzip
Content-Encoding: dkc-lz4   # or gzip / identity (must match envelope.compression)
Authorization:    Bearer dkc_<token>   # after login
User-Agent:       DkcDesktopClient/<version> (...; protobuf)
```

`Content-Encoding` and `ApiRequest.compression` **must** agree. The envelope
field is authoritative; the HTTP header is informational and exists to play
nicely with reverse proxies.

### 2.2 Response headers

```
HTTP/1.1 <status>
Content-Type:     application/x-protobuf
Content-Encoding: dkc-lz4 | gzip | identity
```

The HTTP status code mirrors the `ErrorCode` for transport-aware clients
(see § 5). The structured `ApiError` is still placed in the envelope body
even when the status is non-2xx, so clients only need one error-handling
path.

### 2.3 Ping / health

The single non-protobuf endpoint:

```
GET /api.php/health
→ 200 OK
   Content-Type: text/plain
   ok
```

Used by `ConnectivityService` to decide whether the server is reachable
before invoking protobuf actions.

---

## 3. Compression

The envelope's `compression` field applies to `payload` only – the envelope
itself is never compressed.

| Encoding   | Token       | When used                                |
|------------|-------------|------------------------------------------|
| LZ4 (block + 4-byte size prefix) | `dkc-lz4`   | Preferred for payloads ≥ 256 bytes |
| Gzip       | `gzip`      | Fallback if peer rejects `dkc-lz4`       |
| None       | `identity`  | Payloads < 256 bytes; diagnostics        |

### 3.1 `dkc-lz4` framing

```
+---------------------+----------------------------+
|  uint32 LE original | LZ4 block-compressed bytes |
|  payload size       |                            |
+---------------------+----------------------------+
```

The original size is duplicated in the header so the receiver can allocate
the destination buffer in a single pass without parsing the LZ4 stream.

PHP-side implementation reference (using the `lz4` PECL extension):

```php
$originalSize = unpack('V', substr($buffer, 0, 4))[1];
if ($originalSize > 64 * 1024 * 1024) {
    throw new ApiError(UNSUPPORTED_COMPRESSION, 'payload too large');
}
$payload = lz4_uncompress(substr($buffer, 4));
```

### 3.2 Fallback rules

A client SHOULD send the highest-quality encoding it supports.
A server that cannot decode the request encoding MUST reply with
`ErrorCode.UNSUPPORTED_COMPRESSION` and HTTP 400. The DKC desktop client
automatically retries the same logical request with the next encoding in
the chain `dkc-lz4 → gzip → identity`. The `Accept-Encoding` header lists
the encodings the server may choose for its response.

### 3.3 Size limits / safety

| Limit                        | Default | Where enforced                |
|------------------------------|---------|-------------------------------|
| Max request size (post-decompress) | 16 MiB | Backend                  |
| Max inflated payload (zip-bomb cap) | 64 MiB | Backend & desktop client |

Both LZ4 and Gzip decoders abort with `UNSUPPORTED_COMPRESSION` if the cap
would be exceeded.

---

## 4. Actions

The `Action` enum lives in `proto/dkc/common.proto`. Every numeric value is
permanent – removed actions are marked `reserved` rather than renumbered.

| Group          | Actions |
|----------------|---------|
| **Auth & users**     | `AUTH_LOGIN`, `AUTH_LOGOUT`, `AUTH_STATUS`, `USER_INFO`, `USER_TOKENS_LIST`, `USER_TOKEN_DELETE` |
| **Mängelmeldungen**  | `MM_LIST`, `MM_DETAIL`, `MM_CREATE`, `MM_UPDATE`, `MM_UPDATE_STATUS`, `MM_ASSIGN_CONTRACTOR`, `MM_DELETE` |
| **NEA**              | `NEA_SYSTEMS`, `NEA_SYSTEM_CREATE`, `NEA_SYSTEM_UPDATE`, `NEA_SYSTEM_DELETE`, `NEA_INSPECTIONS`, `NEA_INSPECTION_DETAIL`, `NEA_INSPECTION_CREATE`, `NEA_INSPECTION_UPDATE`, `NEA_INSPECTION_COMPLETE`, `NEA_CHECKLIST_UPDATE`, `NEA_DASHBOARD` |
| **Gebäudebegehungen**| `BUILDING_LIST`, `BUILDING_CREATE`, `BUILDING_UPDATE`, `BUILDING_INSPECTIONS`, `BUILDING_INSPECTION_DETAIL`, `BUILDING_INSPECTION_CREATE`, `BUILDING_INSPECTION_UPDATE`, `BUILDING_INSPECTION_COMPLETE`, `BUILDING_CHECKPOINT_UPDATE`, `BUILDING_CHECKPOINTS_LIST` |
| **Klima / HVAC**     | `KLIMA_DEVICES`, `KLIMA_STATUS`, `KLIMA_REALTIME_STATUS`, `KLIMA_DEVICE_CONTROL`, `KLIMA_GROUP_CONTROL`, `KLIMA_GROUPS_LIST`, `KLIMA_DEVICE_UPDATE` |
| **Schlüsselverwaltung** | `KEYS_INVENTORY`, `KEYS_ISSUED`, `KEYS_CREATE`, `KEYS_UPDATE`, `KEYS_ISSUE`, `KEYS_RETURN`, `KEYS_DELETE` |
| **Dashboard & Projekte** | `DASHBOARD_DATA`, `PROJECTS_LIST`, `PROJECT_CREATE`, `PROJECT_UPDATE`, `PROJECT_SET_ACTIVE` |
| **Benutzerverwaltung (Admin)** | `USERS_LIST`, `USER_CREATE`, `USER_UPDATE`, `USER_DELETE` |
| **Benachrichtigungen** | `NOTIFICATIONS`, `NOTIFICATION_COUNT` |
| **WLS**              | `WLS_BUILDINGS_LIST`, `WLS_BUILDING_CREATE`, `WLS_BUILDING_UPDATE`, `WLS_BUILDING_DELETE`, `WLS_APARTMENTS_LIST`, `WLS_APARTMENTS_BY_BUILDING`, `WLS_APARTMENT_CREATE`, `WLS_APARTMENT_UPDATE`, `WLS_APARTMENT_DELETE`, `WLS_RECORDS_LIST`, `WLS_RECORD_CREATE`, `WLS_RECORD_UPDATE`, `WLS_RECORD_DELETE` |

Each action's request and response message live in the matching
`.proto` file, e.g. `MM_LIST` ↔ `MmListRequest` / `MmListResponse` in
[`proto/dkc/mm.proto`](../proto/dkc/mm.proto). Where there is no body
the request or response uses `Empty` and the success path uses `Ack`.

### 4.1 Example – Login

```proto
// proto/dkc/auth.proto
message AuthLoginRequest {
    string username   = 1;
    string password   = 2;
    string token_name = 3; // default "DKC Desktop"
    int32  ttl_days   = 4;
}

message AuthLoginResponse {
    string   token      = 1;  // dkc_<64-hex>
    string   token_type = 2;  // "Bearer"
    string   expires_at = 3;  // ISO-8601, optional
    UserInfo user       = 4;
}
```

Wire-level (illustrative):

```
ApiRequest {
  protocol_version: 1
  request_id:       "e8a6...c01"
  action:           AUTH_LOGIN
  auth:             {}              // no token yet
  compression:      COMPRESSION_LZ4
  payload:          <lz4(AuthLoginRequest{...})>
}
```

Response on success:

```
ApiResponse {
  protocol_version: 1
  request_id:       "e8a6...c01"
  success:          true
  server_time:      1742400000
  compression:      COMPRESSION_LZ4
  payload:          <lz4(AuthLoginResponse{token="dkc_...", user={...}})>
}
```

### 4.2 Example – Mängelmeldung Statuswechsel

```proto
// proto/dkc/mm.proto
message MmUpdateStatusRequest {
    string uid     = 1;
    int32  status  = 2; // 0..3
    string comment = 3;
}
```

Action = `MM_UPDATE_STATUS`; response payload = `Ack`.

---

## 5. Error codes

| `ErrorCode`                | HTTP | Meaning                                       |
|---------------------------|------|-----------------------------------------------|
| `INVALID_REQUEST`         | 400  | Malformed envelope or payload                 |
| `UNAUTHENTICATED`         | 401  | Missing/expired token                         |
| `FORBIDDEN`               | 403  | Authenticated but lacks permission            |
| `NOT_FOUND`               | 404  | Resource missing                              |
| `VALIDATION_ERROR`        | 422  | Field validation failed (`details` populated) |
| `CONFLICT`                | 409  | State conflict (e.g. duplicate UID)           |
| `RATE_LIMITED`            | 429  | Login or write rate-limit hit                 |
| `UNSUPPORTED_COMPRESSION` | 400  | Request used an encoding the server cannot decode |
| `INTERNAL_ERROR`          | 500  | Unhandled server error                        |
| `SERVICE_UNAVAILABLE`     | 503  | Maintenance / downstream outage               |
| `PROTOCOL_VERSION_MISMATCH` | 400 | `protocol_version` not supported              |

The desktop client maps these 1:1 to `DkcProtobufApiException`.

---

## 6. Versioning policy

- Protobuf package: `dkc.api.v1`.
- `ApiRequest.protocol_version` / `ApiResponse.protocol_version` start at
  `1`. The server SHOULD accept any version ≤ its current and reply with
  `PROTOCOL_VERSION_MISMATCH` for higher.
- **Field numbers are forever.** Adding fields: use the next free number.
  Removing fields: mark `reserved` (both the number and the name).
- New `Action` values may be added; clients must tolerate unknown
  actions/enum values gracefully (proto3 zero default).
- Breaking changes require a new package (`dkc.api.v2`) and a parallel
  endpoint period during migration.

---

## 7. Security

- The DKC login endpoint is rate-limited by IP and by username – this
  applies before the request envelope is even inspected.
- Tokens of the form `dkc_<64-hex>` are never logged. Only request id,
  action, HTTP status, payload byte length, and compression are
  log-safe metadata.
- Maximum request size after decompression is 16 MiB; both encoders abort
  inflation if the receiver-side cap (64 MiB) would be exceeded
  (protection against zip-bomb-style attacks).
- All actions enforce auth and per-resource permission centrally, in a
  router shim wrapped around the dispatcher.
- TLS is required in production; HTTP is only acceptable behind a
  trusted reverse proxy on localhost.

---

## 8. Client integration

The C# desktop client provides the
`DkcDesktopClient.Core.Protobuf.IDkcProtobufApi` interface with one method
per action plus a default implementation `DkcProtobufApi` over the shared
`DkcProtobufApiClient`:

```csharp
var http = new HttpClient { BaseAddress = new Uri(serverUrl) };
using var transport = new DkcProtobufApiClient(http, () => tokenStore.Token);
IDkcProtobufApi api = new DkcProtobufApi(transport, CompressionPreference.Lz4);

var login = await api.LoginAsync(new AuthLoginRequest {
    Username = "alice",
    Password = "...",
    TokenName = "DKC Desktop",
});
```

Errors arrive as `DkcProtobufApiException` carrying the `ErrorCode` and
the original `request_id` for log correlation.

Generated Protobuf C# classes live under the
`DkcDesktopClient.Core.Protocol` namespace and are produced from
[`proto/dkc/*.proto`](../proto/dkc) at build time via `Grpc.Tools`.

---

## 9. Roadmap & migration

See [`ROADMAP.md`](../ROADMAP.md) for the multi-phase rollout. The desktop
client side of the migration is staged so that Refit/REST and Protobuf
co-exist during the transition:

1. Protobuf contracts and client transport (this PR).
2. Backend: protobuf-aware router in `/api.php`.
3. Migrate read-only endpoints (auth status, dashboard, list/detail).
4. Migrate write endpoints (login/logout, MM/NEA/Building writes, Klima
   control, key issue/return).
5. Remove Refit/REST code paths from the desktop client once every
   ViewModel has been switched over.

The single HTTP health probe (`GET /api.php/health`) remains
non-protobuf for the whole lifetime of the API.
