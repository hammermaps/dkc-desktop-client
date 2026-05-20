# DKC Desktop Client – API-Referenz (`api.php`)

> **Aktueller Status (Protobuf-Migration):** Die produktive Schnittstelle wird
> auf eine einzige, Protobuf-basierte API umgestellt. Die vollständige
> Schnittstellen-Dokumentation befindet sich in
> [`docs/PROTOBUF_API.md`](./docs/PROTOBUF_API.md), die Contracts unter
> [`proto/dkc/*.proto`](./proto/dkc). Die unten beschriebene JSON/REST-API
> bleibt während der Migration als Legacy-Pfad verfügbar (siehe
> [`ROADMAP.md`](./ROADMAP.md)).

> **Backend-Dokumentation:** Architektur, Controller, Models, Templates und Template↔C#-Mapping befinden sich in  
> [`/ProxyServer/Backend.md`](./ProxyServer/Backend.md)

> **Basis-URL:** `https://<host>/api.php`  
> **Zeitzone:** `Europe/Berlin` (konfigurierbar via `APP_TIMEZONE`)  
> **Encoding:** UTF-8 / JSON  

---

## Inhaltsverzeichnis

1. [Authentifizierung](#1-authentifizierung)
2. [Routing-Modi](#2-routing-modi)
3. [Fehler-Format](#3-fehler-format)
4. [Rate Limiting & Brute-Force-Schutz](#4-rate-limiting--brute-force-schutz)
5. [Action-basierte Endpunkte (Legacy-API)](#5-action-basierte-endpunkte-legacy-api)
   - 5.1 [Auth / Login](#51-auth--login)
   - 5.2 [NEA – Netzersatzanlagen (Read-Only)](#52-nea--netzersatzanlagen-read-only)
   - 5.3 [MM – Mängelmeldungen (Read-Only)](#53-mm--mängelmeldungen-read-only)
   - 5.4 [Gebäudebegehungen (Read-Only)](#54-gebäudebegehungen-read-only)
   - 5.5 [Klima – Klimaanlage / HVAC (Read-Only)](#55-klima--klimaanlage--hvac-read-only)
   - 5.6 [Schlüsselverwaltung (Read-Only)](#56-schlüsselverwaltung-read-only)
   - 5.7 [Dashboard & Projekte](#57-dashboard--projekte)
   - 5.8 [Benutzer-API-Tokens](#58-benutzer-api-tokens)
   - 5.9 [Zählererfassung (PWA / Meter)](#59-zählererfassung-pwa--meter)
   - 5.10 [Benachrichtigungen](#510-benachrichtigungen)
   - 5.11 [System-Funktionen (API-Key erforderlich)](#511-system-funktionen-api-key-erforderlich)
6. [TWS REST API (Path-basiert)](#6-tws-rest-api-path-basiert)
   - 6.1 [Health](#61-get-health)
   - 6.2 [User (Benutzerverwaltung)](#62-user-endpunkte)
   - 6.3 [Buildings (WLS-Gebäude)](#63-buildings-wls-gebäude)
   - 6.4 [Apartments (Leerstandseinheiten)](#64-apartments-leerstandseinheiten)
   - 6.5 [Records (WLS-Datensätze)](#65-records-wls-datensätze)
7. [Berechtigungs-Übersicht](#7-berechtigungs-übersicht)
8. [Fehlende / geplante Endpunkte](#8-fehlende--geplante-endpunkte)

---

## 1. Authentifizierung

Die API unterstützt drei Authentifizierungsverfahren:

### 1a. API-Key (UUID v4) — für System-Integrationen
```
POST  /api.php?action=<action>
Body: apikey=<uuid-v4>
```
oder via HTTP-Header:
```
Authorization: Bearer <uuid-v4>
```
API-Keys werden in der Tabelle `api` verwaltet. Ein Key muss `enabled = 1` sein.  
Zugriff ist zusätzlich per IP-Whitelist (`config/ips.txt`, CIDR-Notation) und per Key-spezifischer `allowed_ips`-Liste absicherbar.

**Freigeschaltete Funktionen** je Key: `sync`, `sms`, `email`, `rmi`, `gotify`, `webhook`.

### 1b. User-API-Token (dkc_…) — für den Desktop-Client und externe Apps
```
Authorization: Bearer dkc_<64-hex-Zeichen>
```
Token wird über `auth_login` erzeugt und in der Tabelle `user_api_tokens` als SHA-256-Hash gespeichert.  
Token-Format: `dkc_` + 64 Hex-Zeichen (256 Bit Entropie).  
Ablauf: konfigurierbar (Standard 30 Tage), `expires_at = NULL` = unbegrenzt.

### 1c. PHP-Session — für Browser-basierte Aufrufe
Aktive `$_SESSION['id']` nach klassischem Browser-Login.

---

## 2. Routing-Modi

### Modus A: Query-Parameter-Routing (Legacy)
```
GET/POST  /api.php?action=<action_name>[&weitere_parameter]
```
Alle Endpunkte in Abschnitt 5 folgen diesem Schema.

### Modus B: Path-Info-Routing (TWS REST API)
```
<METHODE>  /api.php/<resource>/[<segment1>/[<segment2>]]
```
Wird erkannt wenn `PATH_INFO` oder `REQUEST_URI` einen Pfad nach `/api.php` enthält.  
Beispiel: `POST /api.php/user/login`  
Alle Endpunkte in Abschnitt 6 folgen diesem Schema.

---

## 3. Fehler-Format

### Action-API (Modus A)
```json
{ "success": false, "error": "Fehlermeldung" }
```

### TWS REST API (Modus B)
```json
{ "success": false, "error": "Fehlermeldung", "server_time": 1700000000 }
```
Alle TWS-Antworten enthalten `"server_time"` (Unix-Timestamp).

### HTTP-Status-Codes
| Code | Bedeutung |
|------|-----------|
| 200  | Erfolg |
| 400  | Ungültige Anfrage / fehlende Parameter |
| 401  | Nicht authentifiziert |
| 403  | Keine Berechtigung |
| 404  | Ressource nicht gefunden / unbekannte Action |
| 405  | HTTP-Methode nicht erlaubt |
| 409  | Konflikt (z. B. Benutzername bereits vergeben) |
| 429  | Zu viele Anfragen (Rate Limit) |
| 500  | Interner Fehler |
| 503  | Datenbank nicht erreichbar |

---

## 4. Rate Limiting & Brute-Force-Schutz

- Alle API-Anfragen unterliegen einem globalen Rate Limit pro IP.
- Login-Endpunkte (`auth_login`, `/user/login`) haben separaten Brute-Force-Schutz.
- Bei Überschreitung: HTTP 429 + `Retry-After: 60`.
- XSS-Erkennung auf `$_GET` und `$_POST`: bei Angriff sofort HTTP 400.

---

## 5. Action-basierte Endpunkte (Legacy-API)

### Auth-Typen pro Action

| Auth-Typ | Actions |
|----------|---------|
| Öffentlich (kein Auth) | `auth_login` |
| User-Token **oder** Session | `auth_logout`, `auth_status`, `user_info`, `nea_systems`, `nea_inspections`, `nea_inspection_detail`, `nea_dashboard`, `mm_list`, `mm_detail`, `building_list`, `building_inspections`, `building_inspection_detail`, `klima_devices`, `klima_status`, `keys_inventory`, `keys_issued`, `dashboard_data`, `projects_list`, `user_tokens_list`, `user_token_delete` |
| Session-basiert | `notifications`, `get_notification_count`, `ckeditor_draft`, `client_cache_version`, `meter_*`, `dropdown_data` |
| API-Key | `sync`, `sync_download`, `sms`, `email`, `gotify`, `rmi`, `webhook` |

---

### 5.1 Auth / Login

#### `auth_login` — Persönlichen User-API-Token erstellen
```
POST /api.php?action=auth_login
Content-Type: application/json
```
**Body:**
```json
{
  "username":   "string (required)",
  "password":   "string (required)",
  "token_name": "string (optional, default: 'API Token', max 255 Zeichen)",
  "ttl_days":   30
}
```
**Response:**
```json
{
  "success":    true,
  "token":      "dkc_...",
  "token_type": "Bearer",
  "expires_at": "2024-12-31 00:00:00",
  "user": {
    "id": 1, "username": "string", "vname": "string",
    "nname": "string", "email": "string", "is_admin": false
  }
}
```
> `ttl_days = 0` erzeugt ein Token ohne Ablauf.  
> Vorhandene Tokens mit gleichem `token_name` für den Benutzer werden vorher gelöscht.

---

#### `auth_logout` — Token invalidieren
```
POST /api.php?action=auth_logout
Authorization: Bearer dkc_...
```
**Response:** `{ "success": true, "message": "Erfolgreich abgemeldet" }`

---

#### `auth_status` — Token/Session prüfen
```
GET /api.php?action=auth_status
Authorization: Bearer dkc_...
```
**Response:**
```json
{
  "success": true, "authenticated": true,
  "user": { "id": 1, "username": "...", "vname": "...", "nname": "...", "email": "...", "is_admin": false }
}
```

---

#### `user_info` — Eigene Benutzer-Infos
```
GET /api.php?action=user_info
Authorization: Bearer dkc_...
```
**Response:**
```json
{
  "success": true,
  "user": { "id": 1, "username": "...", "vname": "...", "nname": "...", "email": "...", "is_admin": false },
  "permissions": { "admin": false, "nea_view": true, ... },
  "active_project_id": 1
}
```

---

### 5.2 NEA – Netzersatzanlagen (Read-Only)

> Berechtigung: `nea_view` oder `admin`

#### `nea_systems` — Alle NEA-Anlagen des aktiven Projekts
```
GET /api.php?action=nea_systems
Authorization: Bearer dkc_...
```
**Response:**
```json
{
  "success": true,
  "project_id": 1,
  "systems": [
    {
      "id": 1, "name": "string", "description": "string|null",
      "location": "string|null", "manufacturer": "string|null",
      "model": "string|null", "serial_number": "string|null",
      "installation_date": "YYYY-MM-DD|null", "enabled": true,
      "project_id": 1,
      "last_inspection_date": "YYYY-MM-DD|null",
      "last_inspection_result": "ok|defects_found|failed|null"
    }
  ]
}
```

---

#### `nea_inspections` — NEA-Prüfungsliste
```
GET /api.php?action=nea_inspections[&system_id=X][&year=YYYY][&status=S][&limit=50][&offset=0]
Authorization: Bearer dkc_...
```
**Filter-Parameter:**

| Parameter | Typ | Beschreibung |
|-----------|-----|--------------|
| `system_id` | int | Filter auf NEA-Anlage |
| `year` | int | Filter auf Jahr |
| `status` | string | `in_progress`, `completed`, `failed`, `cancelled` |
| `limit` | int | Max 200, Standard 50 |
| `offset` | int | Paginierung |

**Response:**
```json
{
  "success": true, "project_id": 1,
  "total": 42, "limit": 50, "offset": 0,
  "inspections": [
    {
      "id": 1, "nea_system_id": 1, "system_name": "string",
      "inspection_type": "annual|monthly",
      "inspection_date": "YYYY-MM-DD",
      "inspector_name": "string", "status": "completed",
      "overall_result": "ok|defects_found|failed",
      "runtime_hours": 1234, "notes": "string|null", "created_at": "..."
    }
  ]
}
```

---

#### `nea_inspection_detail` — Detailansicht einer Prüfung
```
GET /api.php?action=nea_inspection_detail&id=<int>
Authorization: Bearer dkc_...
```
**Response:**
```json
{
  "success": true,
  "inspection": {
    "id": 1, "nea_system_id": 1,
    "system": { "id": 1, "name": "string" },
    "inspection_type": "string", "inspection_date": "YYYY-MM-DD",
    "inspector_id": 1, "inspector_name": "string",
    "status": "string", "overall_result": "string",
    "runtime_hours": 1234, "runtime_hours_after": 1240,
    "defects_found": "string|null", "corrective_actions": "string|null",
    "notes": "string|null",
    "checklist_data": {},
    "defect_notes": {},
    "photos": [],
    "created_at": "..."
  }
}
```

---

#### `nea_dashboard` — NEA-Dashboard-Statistiken
```
GET /api.php?action=nea_dashboard
Authorization: Bearer dkc_...
```
**Response:**
```json
{
  "success": true, "project_id": 1,
  "stats": {
    "total_systems": 5,
    "inspections_this_week": 2,
    "inspections_this_month": 8,
    "failed_last_30_days": 1
  },
  "due_tests": [
    { "system_id": 2, "system_name": "string", "days_overdue": 3, "last_inspection": "YYYY-MM-DD" }
  ],
  "recent_inspections": [
    { "id": 1, "nea_system_id": 1, "inspection_date": "...", "inspector_name": "...", "status": "...", "overall_result": "..." }
  ]
}
```

---

### 5.3 MM – Mängelmeldungen (Read-Only)

> Berechtigung: `view_mm_list` (Liste) / `view_mm` (Detail)

#### `mm_list` — Mängelmeldungen auflisten
```
GET /api.php?action=mm_list[&status=0][&street=X][&limit=50][&offset=0]
Authorization: Bearer dkc_...
```
**Status-Werte:** `0` = offen, `1` = in Bearbeitung, `2` = geschlossen, `3` = abgebrochen

**Response:**
```json
{
  "success": true, "total": 10, "limit": 50, "offset": 0,
  "messages": [
    {
      "uid": "MM-2024-001", "status": 0, "betreff": "string",
      "street": "string|null", "whg": "string|null",
      "melder": "string|null", "dringlichkeit": "normal|dringend|notfall",
      "nachunternehmer": "string|null", "datetime": "YYYY-MM-DD HH:MM:SS"
    }
  ]
}
```

---

#### `mm_detail` — Detailansicht einer Mängelmeldung
```
GET /api.php?action=mm_detail&uid=MM-2024-001
Authorization: Bearer dkc_...
```
**Response:**
```json
{
  "success": true,
  "message": {
    "uid": "MM-2024-001", "status": 0,
    "betreff": "string", "meldung_massage": "string|null",
    "apleona": "string|null", "folge": "string|null",
    "street": "string|null", "whg": "string|null",
    "melder": "string|null", "tel": "string|null", "email": "string|null",
    "datetime": "YYYY-MM-DD HH:MM:SS",
    "dringlichkeit": "normal|dringend|notfall",
    "nachunternehmer": "string|null", "ekpreis": "string|null",
    "klausel": false, "zugeh": "string|null",
    "scanned": false, "zeit": "string|null",
    "planon": "string|null", "instructions": []
  }
}
```

---

### 5.4 Gebäudebegehungen (Read-Only)

> Berechtigung: `building_view` oder `admin`

#### `building_list` — Gebäudeliste
```
GET /api.php?action=building_list
Authorization: Bearer dkc_...
```
**Response:**
```json
{
  "success": true, "project_id": 1,
  "buildings": [
    { "id": 1, "name": "string", "address": "string|null", "description": "string|null", "enabled": true, "project_id": 1 }
  ]
}
```

---

#### `building_inspections` — Begehungsliste
```
GET /api.php?action=building_inspections[&building_id=X][&status=S][&year=Y][&limit=50][&offset=0]
Authorization: Bearer dkc_...
```
**Status-Werte:** `open`, `in_progress`, `completed`

**Response:**
```json
{
  "success": true, "project_id": 1,
  "total": 5, "limit": 50, "offset": 0,
  "inspections": [
    {
      "id": 1, "building_id": 1, "building_name": "string",
      "title": "string|null", "inspection_date": "YYYY-MM-DD|null",
      "status": "open|in_progress|completed",
      "overall_result": "string|null",
      "weather": "string|null", "attendees": "string|null",
      "general_notes": "string|null", "created_at": "..."
    }
  ]
}
```

---

#### `building_inspection_detail` — Begehungs-Detailansicht
```
GET /api.php?action=building_inspection_detail&id=<int>
Authorization: Bearer dkc_...
```
**Response:**
```json
{
  "success": true,
  "inspection": {
    "id": 1, "building_id": 1,
    "building": { "id": 1, "name": "string" },
    "title": "string|null", "inspection_date": "YYYY-MM-DD|null",
    "status": "string", "overall_result": "string|null",
    "weather": "string|null", "attendees": "string|null",
    "general_notes": "string|null",
    "checkpoints": [
      { "id": 1, "name": "string", "category": "string|null", "status": "ok|nok|n/a|null", "note": "string|null" }
    ],
    "created_at": "..."
  }
}
```

---

### 5.5 Klima – Klimaanlage / HVAC (Read-Only)

> Berechtigung: `view_groups` oder `admin`  
> ⚠️ Echtzeit-Temperaturen erfordern eine direkte RMI-Hardwareverbindung und sind über diese API **nicht** verfügbar. Es werden nur Datenbankwerte zurückgegeben.

#### `klima_devices` — Geräteliste
```
GET /api.php?action=klima_devices
Authorization: Bearer dkc_...
```
**Response:**
```json
{
  "success": true,
  "devices": [
    { "address": 1, "name": "string", "group_id": 1, "enabled": true, "sort": 0 }
  ]
}
```

---

#### `klima_status` — Betriebsstatus (aus DB)
```
GET /api.php?action=klima_status[&address=<int>]
Authorization: Bearer dkc_...
```
**Response:**
```json
{
  "success": true,
  "devices": [
    {
      "address": 1, "name": "string", "enabled": true,
      "group_id": 1, "operating_mode": "string|null",
      "note": "Echtzeit-Status erfordert direkte RMI-Verbindung"
    }
  ]
}
```

---

### 5.6 Schlüsselverwaltung (Read-Only)

> Berechtigung: `keys_view` oder `admin`

#### `keys_inventory` — Schlüssel-Inventar
```
GET /api.php?action=keys_inventory[&status=active|inactive][&limit=50][&offset=0]
Authorization: Bearer dkc_...
```
**Response:**
```json
{
  "success": true, "total": 20, "limit": 50, "offset": 0,
  "keys": [
    {
      "id": 1, "number": "string", "name": "string",
      "description": "string|null", "type_id": 1, "cabinet_id": 1,
      "total_count": 3, "enabled": true
    }
  ]
}
```

---

#### `keys_issued` — Aktuell ausgegebene Schlüssel
```
GET /api.php?action=keys_issued[&limit=50][&offset=0]
Authorization: Bearer dkc_...
```
**Response:**
```json
{
  "success": true, "total": 5, "limit": 50, "offset": 0,
  "issued": [
    {
      "id": 1, "key_id": 1, "key_name": "string",
      "issued_to": "string", "issued_at": "YYYY-MM-DD",
      "returned_at": null, "notes": "string|null"
    }
  ]
}
```

---

### 5.7 Dashboard & Projekte

#### `dashboard_data` — Aggregierte Dashboard-Statistiken
```
GET /api.php?action=dashboard_data
Authorization: Bearer dkc_...
```
**Response** (enthält nur Bereiche für die der User berechtigt ist):
```json
{
  "success": true, "project_id": 1,
  "mm":       { "total": 10, "pending": 3, "approved": 5, "completed": 2 },
  "nea":      { "total_systems": 4, "inspections_this_month": 6 },
  "building": { "open": 2, "in_progress": 1, "completed": 8 },
  "keys":     { "total_inventory": 50, "currently_issued": 12 },
  "notifications": { "unread": 3 }
}
```

---

#### `projects_list` — Verfügbare Projekte
```
GET /api.php?action=projects_list
Authorization: Bearer dkc_...
```
**Response:**
```json
{
  "success": true,
  "active_project_id": 1,
  "projects": [
    { "id": 1, "name": "string", "description": "string|null", "status": "string", "created_at": "..." }
  ]
}
```

---

### 5.8 Benutzer-API-Tokens

#### `user_tokens_list` — Eigene Tokens auflisten
```
GET /api.php?action=user_tokens_list
Authorization: Bearer dkc_...
```
**Response:**
```json
{
  "success": true,
  "tokens": [
    { "id": 1, "name": "string", "created_at": "...", "expires_at": "...|null", "last_used_at": "...|null", "last_ip": "string|null" }
  ]
}
```

---

#### `user_token_delete` — Token löschen
```
DELETE /api.php?action=user_token_delete&id=<int>
Authorization: Bearer dkc_...
```
**Response:** `{ "success": true }`

---

### 5.9 Zählererfassung (PWA / Meter)

> Berechtigung: Session-basiert (kein API-Key, kein User-Token).

| Action | Methode | Beschreibung |
|--------|---------|--------------|
| `meter_list` | GET | Zähler-Übersicht (mit Filter: `building_id`, `type`) |
| `meter_submit` | POST | Einzelnen Zählerstand einreichen |
| `meter_batch_sync` | POST | Mehrere Ablesungen auf einmal synchronisieren |
| `meter_readings` | GET | Ablesungen abfragen (Filter: `meter_id`, `from`, `to`) |
| `meter_qr_list` | GET | Zähler mit QR-Codes auflisten |
| `meter_deactivate` | POST | Zähler deaktivieren (`id` erforderlich) |
| `meter_activate` | POST | Zähler aktivieren (`id` erforderlich) |
| `meter_buildings` | GET | Gebäude für Zähler-Auswahl |
| `meter_whg` | GET | Wohnungen für Zähler-Auswahl (`building_id` optional) |
| `meter_users` | GET | Benutzer für Zähler-Aufgaben |
| `meter_topology` | GET | Topologie aller Zähler (hierarchisch: Gebäude → Wohnung → Zähler) |
| `dropdown_data` | GET | Dropdown-Daten (Gebäude, Wohnungen, Typen etc.) |

---

### 5.10 Benachrichtigungen

> Berechtigung: Session-basiert.

#### `notifications` — Benachrichtigungen abrufen
```
GET /api.php?action=notifications
```
**Response:**
```json
{ "authenticated": true, "count": 3, "notifications": [...], "version": "..." }
```

---

#### `get_notification_count` — Ungelesene Benachrichtigungen zählen
```
GET /api.php?action=get_notification_count
```
**Response:** `{ "count": 3 }`

---

#### `client_cache_version` — Client-Cache-Version
```
GET /api.php?action=client_cache_version
```
**Response:** `{ "version": "...", "timestamp": ... }`

---

### 5.11 System-Funktionen (API-Key erforderlich)

> Alle folgenden Actions benötigen einen gültigen UUID-v4-API-Key mit der jeweiligen Funktion aktiviert.

#### `sync` — Datei-Sync-Index
```
GET /api.php?action=sync[&days=N][&since=YYYY-MM-DD_HH:MM]
Authorization: Bearer <api-key>
```
**Response:**
```json
[
  { "name": "relative/pfad.pdf", "url": "https://.../api.php?action=sync_download&file=<hash>", "hash": "<md5>" }
]
```

---

#### `sync_download` — Datei herunterladen
```
GET /api.php?action=sync_download&file=<file-id>
Authorization: Bearer <api-key>
```
**Response:** Binary-Datei (`Content-Type: application/pdf`)

---

#### `sms` — SMS senden
```
GET /api.php?action=sms&nummer=<tel>&text=<text>[&sender=<name>][&multi=1]
Authorization: Bearer <api-key>
```
Mehrere Nummern (bei `multi=1`): kommagetrennt in `nummer`.  
**Unifi Protect Integration:** via Custom-Headers `X-SMS-NUMBER`, `X-SMS-TEXT`, `X-SMS-SENDER`.

---

#### `email` — E-Mail senden
```
POST /api.php?action=email
Authorization: Bearer <api-key>
Body: to=<email>&subject=<betreff>&body=<text>[&cc=<email>]
```
**Unifi Protect Integration:** via Custom-Headers `X-EMAIL-ADD`, `X-EMAIL-CC`, `X-EMAIL-SUBJECT`, `X-EMAIL-TEMPLATE`, `X-EMAIL-TEXT`.

---

#### `gotify` — Gotify-Nachricht senden
```
POST /api.php?action=gotify
Authorization: Bearer <api-key>
Body: { "title": "...", "text": "...", "priority": 5, "token": "CK|PROTECT" }
```
**Unifi Protect Integration:** via Custom-Headers `X-GOTIFY-TITLE`, `X-GOTIFY-TEXT`, `X-GOTIFY-PRIO`, `X-GOTIFY-SYSTEM`.

---

#### `rmi` — Remote Method Invocation (Klimasteuerung)
```
POST /api.php?action=rmi
Authorization: Bearer <api-key>
```
Direkter Aufruf des RMI-Subsystems für Hardware-Steuerung (Klimaanlagen).

---

#### `webhook` — Eingehende Webhooks verarbeiten
```
POST /api.php?action=webhook
Authorization: Bearer <api-key>
```
Empfängt Webhook-Payloads von externen Systemen (z. B. Monitoring, Alarm-Manager).

---

## 6. TWS REST API (Path-basiert)

> **Basis-URL:** `https://<host>/api.php/<resource>/...`  
> **Auth:** `Authorization: Bearer dkc_...` oder aktive PHP-Session  
> **Response-Format:** `{ "success": bool, "data": mixed, "error": string, "server_time": <unix-ts> }`  
> **CORS:** vollständig konfiguriert (alle Methoden, alle Origins, 24h Preflight-Cache)

---

### 6.1 `GET /health`

```
GET /api.php/health
```
**Response:** `{ "success": true, "status": "ok", "version": "2.0.0-integrated", "server_time": ... }`  
Kein Auth erforderlich.

---

### 6.2 User-Endpunkte

#### `POST /user/login` — Einloggen (öffentlich)
```json
{ "username": "string", "password": "string" }
```
**Response:**
```json
{
  "success": true,
  "data": { "token": "dkc_...", "user": { /* UserItem */ } },
  "server_time": ...
}
```

---

#### `POST /user/register` — Benutzer anlegen *(Admin only)*
```json
{ "username": "string", "password": "string", "name": "Vorname Nachname", "email": "string" }
```
Neuer Benutzer erhält Berechtigungen `wls_view`, `wls_create`.  
**Response:** `{ "success": true, "data": { /* UserItem */ } }`

---

#### `POST /user/logout`
Invalidiert den verwendeten Bearer-Token.  
**Response:** `{ "success": true, "message": "Abgemeldet" }`

---

#### `GET /user/check-token` oder `POST /user/check-token`
**Response:**
```json
{ "success": true, "data": { "valid": true, "session_time": 0, "user_data": { /* UserItem */ } } }
```

---

#### `GET /user/get[/{id}]`
Eigenes Profil oder (Admin) fremdes Profil.  
**Response:** `{ "success": true, "data": { /* UserItem */ } }`

---

#### `GET /user/list` *(wls_view erforderlich)*
**Response:** `{ "success": true, "data": [ /* UserItem[] */ ] }`

---

#### `POST /user/update/{id}`
Eigenes Profil (oder Admin: fremdes).

**Body (alle Felder optional):**
```json
{ "username": "string", "email": "string", "enabled": 1, "name": "Vorname Nachname", "role": "user|technician|admin" }
```
`role` nur von Admins änderbar.  
**Response:** `{ "success": true, "data": { /* UserItem */ } }`

---

#### `GET /user/role`
**Response:** `{ "success": true, "data": { "role": "user|technician|admin", "enabled": true } }`

---

#### `POST /user/setrole` *(wls_admin erforderlich)*
```json
{ "id": 1, "role": "user|technician|admin" }
```
**Response:** `{ "success": true }`

---

#### `DELETE /user/remove/{id}` *(wls_admin erforderlich)*
Soft-Delete: setzt `enabled = 0`.  
**Response:** `{ "success": true, "message": "Benutzer deaktiviert" }`

---

#### `POST /user/changepw`
```json
{ "oldPassword": "string", "newPassword": "string (min. 8 Zeichen)" }
```
**Response:** `{ "success": true, "message": "Passwort geändert" }`

---

#### `GET /user/photo/{id}` *(wls_view erforderlich)*
Profilfotos werden aktuell nicht unterstützt.  
**Response:** `{ "success": true, "data": null }`

---

**UserItem-Format:**
```json
{
  "id": 1, "username": "string", "name": "Vorname Nachname",
  "email": "string", "role": "user|technician|admin",
  "enabled": true, "indent": "1",
  "last_login": "...", "last_logout": null,
  "created_at": "...", "updated_at": null,
  "logins_total": 0, "logins_failed": 0, "session_time": 0
}
```

**Rollen-Zuordnung:**
| Rolle | Berechtigungen |
|-------|----------------|
| `admin` | `admin` oder `wls_admin` |
| `technician` | `wls_edit` |
| `user` | `wls_view`, `wls_create` |

---

### 6.3 Buildings (WLS-Gebäude)

> Berechtigung: `wls_view` (lesen), `wls_edit` (schreiben)

#### `GET /buildings/list`
**Response:** `{ "success": true, "data": [ /* BuildingItem[] */ ] }`

---

#### `GET /buildings/{id}`
**Response:** `{ "success": true, "data": { /* BuildingItem */ } }`

---

#### `POST /buildings/create` *(wls_edit)*
```json
{ "name": "string (required)", "hidden": false, "sorted": 0 }
```
**Response:** `{ "success": true, "data": { /* BuildingItem */ } }`

---

#### `POST /buildings/{id}` *(wls_edit)*
```json
{ "name": "string", "hidden": false, "sorted": 0 }
```
**Response:** `{ "success": true, "data": { /* BuildingItem */ } }`

---

#### `DELETE /buildings/{id}` *(wls_edit)*
**Response:** `{ "success": true }`

---

#### `POST /buildings/sync` *(wls_edit)*
Upsert mehrerer Gebäude.
```json
{
  "buildings": [
    { "id": 1, "name": "string", "hidden": false, "sorted": 0 }
  ]
}
```
**Response:** `{ "success": true, "data": [ /* BuildingItem[] */ ] }`

---

**BuildingItem-Format:**
```json
{
  "id": 1, "name": "string", "hidden": false, "sorted": 0,
  "created": "...", "updated": "...",
  "apartments_count": 5
}
```

---

### 6.4 Apartments (Leerstandseinheiten)

> Berechtigung: `wls_view` (lesen), `wls_create`/`wls_edit`/`wls_delete` (schreiben)  
> Tabelle: `mm_whg` (Zeilen mit `empty = 1`)

#### `GET /apartments/list` oder `GET /apartments/list/{building_id}`
**Response:** `{ "success": true, "data": [ /* ApartmentItem[] */ ] }`

---

#### `GET /apartments/{id}`
**Response:** `{ "success": true, "data": { /* ApartmentItem */ } }`

---

#### `POST /apartments/create` *(wls_create)*
```json
{ "building_id": 1, "value": "string (required)", "name": "string|null", "sorted": 0 }
```
**Response:** `{ "success": true, "data": { /* ApartmentItem */ } }`

---

#### `POST /apartments/{id}` *(wls_edit)*
```json
{ "value": "string", "name": "string", "sorted": 0, "empty": 1, "building_id": 1 }
```
**Response:** `{ "success": true, "data": { /* ApartmentItem */ } }`

---

#### `DELETE /apartments/{id}` *(wls_delete)*
Soft-Delete: setzt `empty = 0`.  
**Response:** `{ "success": true }`

---

**ApartmentItem-Format:**
```json
{
  "id": 1, "building_id": 1,
  "number": "1a – Dachgeschoss",
  "value": "1a", "name": "Dachgeschoss",
  "sorted": 0, "sonder": false,
  "keller": null, "empty": true
}
```

---

### 6.5 Records (WLS-Datensätze)

> Berechtigung: `wls_view` (lesen), `wls_create`/`wls_edit`/`wls_delete` (schreiben)  
> Tabelle: `wls_records`

#### `POST /records/list`
**Body (alle Filter optional):**
```json
{
  "apartment_id": 1, "building_id": 1, "user_id": 1,
  "start_date": "YYYY-MM-DD", "end_date": "YYYY-MM-DD",
  "order_by": "start_time|end_time|created_at|id",
  "order": "ASC|DESC",
  "limit": 50, "offset": 0
}
```
**Response:** `{ "success": true, "data": [ /* RecordItem[] */ ] }`

---

#### `GET /records/get/{id}`
**Response:** `{ "success": true, "data": { /* RecordItem */ } }`

---

#### `POST /records/create` *(wls_create)*
```json
{
  "apartment_id": 1, "building_id": 1,
  "start_time": "YYYY-MM-DD HH:MM:SS",
  "end_time": "YYYY-MM-DD HH:MM:SS",
  "user_id": 1,
  "latitude": 52.123, "longitude": 13.456,
  "location_accuracy": 10.5
}
```
**Response:** `{ "success": true, "data": { /* RecordItem */ } }`

---

#### `POST /records/update/{id}` *(wls_edit)*
```json
{
  "start_time": "...", "end_time": "...",
  "latitude": 52.123, "longitude": 13.456,
  "location_accuracy": 10.5
}
```
**Response:** `{ "success": true, "data": { /* RecordItem */ } }`

---

#### `DELETE /records/remove/{id}` *(wls_delete)*
Endgültiges Löschen.  
**Response:** `{ "success": true }`

---

**RecordItem-Format:**
```json
{
  "id": 1, "apartment_id": 1, "building_id": 1,
  "user_id": 1, "start_time": "...", "end_time": "...",
  "duration": 3600,
  "latitude": 52.123, "longitude": 13.456, "location_accuracy": 10.5,
  "created_at": "...", "updated_at": "...",
  "user_name": "Max Muster", "user_email": "max@example.com",
  "user_firstname": "Max", "user_lastname": "Muster"
}
```

---

## 7. Berechtigungs-Übersicht

| Permission-Key | Zugriff auf |
|----------------|-------------|
| `admin` | Alle Endpunkte (vollständig) |
| `wls_admin` | TWS REST API Adminaktionen (Benutzerverwaltung, Rollen) |
| `wls_view` | TWS REST API: Lesezugriff |
| `wls_create` | TWS REST API: Anlegen (Apartments, Records) |
| `wls_edit` | TWS REST API: Bearbeiten (Buildings, Apartments, Records) |
| `wls_delete` | TWS REST API: Löschen (Apartments, Records) |
| `nea_view` | Lesen: `nea_systems`, `nea_inspections`, `nea_inspection_detail`, `nea_dashboard` |
| `view_mm_list` | Lesen: `mm_list` |
| `view_mm` | Lesen: `mm_detail` |
| `building_view` | Lesen: `building_list`, `building_inspections`, `building_inspection_detail` |
| `view_groups` | Lesen: `klima_devices`, `klima_status` |
| `keys_view` | Lesen: `keys_inventory`, `keys_issued` |

Berechtigungen werden als JSON-Objekt in `users.permissions` gespeichert:
```json
{ "admin": true, "nea_view": true, "wls_view": true, ... }
```

---

## 8. Fehlende / geplante Endpunkte

Die folgenden Endpunkte sind laut `missing-api-endpoints.md` noch nicht implementiert und werden für zukünftige Client-Funktionen benötigt:

| Bereich | Action / Methode | Beschreibung |
|---------|-----------------|--------------|
| NEA | `nea_system_create` (POST) | NEA-Anlage anlegen |
| NEA | `nea_system_update` (PUT) | NEA-Anlage bearbeiten |
| NEA | `nea_system_delete` (DELETE) | NEA-Anlage löschen |
| NEA | `nea_inspection_create` (POST) | Prüfung anlegen |
| NEA | `nea_inspection_update` (PUT) | Prüfung bearbeiten |
| NEA | `nea_inspection_complete` (POST) | Prüfung abschließen |
| NEA | `nea_checklist_update` (POST) | Checklisten-Einträge speichern |
| MM | `mm_create` (POST) | Neue Mängelmeldung |
| MM | `mm_update` (PUT) | Mängelmeldung bearbeiten |
| MM | `mm_update_status` (POST) | Status ändern |
| MM | `mm_assign_contractor` (POST) | Nachunternehmer zuweisen |
| MM | `mm_delete` (DELETE) | Mängelmeldung löschen |
| Gebäude | `building_create` (POST) | Gebäude (Begehung) anlegen |
| Gebäude | `building_update` (PUT) | Gebäude bearbeiten |
| Gebäude | `building_inspection_create` (POST) | Begehung anlegen |
| Gebäude | `building_inspection_update` (PUT) | Begehung bearbeiten |
| Gebäude | `building_inspection_complete` (POST) | Begehung abschließen |
| Gebäude | `building_checkpoint_update` (POST) | Prüfpunkt-Ergebnis eintragen |
| Gebäude | `building_checkpoints_list` (GET) | Prüfpunkte auflisten |
| Klima | `klima_device_control` (POST) | Einzelgerät steuern |
| Klima | `klima_group_control` (POST) | Gerätegruppe steuern |
| Klima | `klima_groups_list` (GET) | Gerätegruppen auflisten |
| Klima | `klima_device_update` (PUT) | Gerätekonfiguration bearbeiten |
| Schlüssel | `keys_create` (POST) | Schlüssel anlegen |
| Schlüssel | `keys_update` (PUT) | Schlüssel bearbeiten |
| Schlüssel | `keys_issue` (POST) | Schlüssel ausgeben |
| Schlüssel | `keys_return` (POST) | Schlüssel zurückgeben |
| Schlüssel | `keys_delete` (DELETE) | Ausgabe-Eintrag löschen |
| Projekte | `project_create` (POST) | Projekt anlegen |
| Projekte | `project_update` (PUT) | Projekt bearbeiten |
| Projekte | `project_set_active` (POST) | Aktives Projekt wechseln |
| Benutzer | `users_list` (GET) | Benutzerliste (Admin) |
| Benutzer | `user_create` (POST) | Benutzer anlegen (Admin) |
| Benutzer | `user_update` (PUT) | Benutzer bearbeiten (Admin) |
| Benutzer | `user_delete` (DELETE) | Benutzer löschen (Admin) |

> ⚠️ Diese Liste basiert auf `missing-api-endpoints.md`. Eine vollständige Analyse der `template/default/*`-Dateien im ProxyServer-Projekt ist noch ausstehend.

---

## 9. C# Desktop Client – Architektur & Implementierungsstatus

### 9.1 Technologie-Stack

| Komponente | Technologie |
|------------|-------------|
| UI-Framework | Avalonia UI 12 (Cross-Platform: Windows, Linux, macOS) |
| Sprache | C# / .NET 8.0 |
| MVVM | CommunityToolkit.Mvvm 8.4 |
| API-Client | Refit (typisierter HTTP-Client über `IDkcApi`) |
| Dependency Injection | Microsoft.Extensions.Hosting + Microsoft.Extensions.DependencyInjection |
| Token-Persistenz | Microsoft.AspNetCore.DataProtection (verschlüsselt, OS-spezifisch) |
| Logging | Serilog (File-Sink) |
| Theme | Avalonia Fluent Theme + Inter Font |

**Build:** `dotnet build DkcDesktopClient.slnx --configuration Release`  
**Test:** `dotnet test DkcDesktopClient.Tests/DkcDesktopClient.Tests.csproj --configuration Release --no-build`

---

### 9.2 Solution-Struktur

```
DkcDesktopClient.slnx
├── DkcDesktopClient.App/          # Avalonia UI – Views, ViewModels, App-Einstiegspunkt
│   ├── Views/                     # AXAML Views (UserControls + MainWindow)
│   ├── ViewModels/                # MVVM ViewModels (CommunityToolkit.Mvvm)
│   ├── Assets/                    # Statische Ressourcen (Icons, Bilder)
│   ├── App.axaml / App.axaml.cs   # Application-Root, Theme-Konfiguration
│   ├── Program.cs                 # DI-Container, Host-Konfiguration, Serilog
│   └── ViewLocator.cs             # Automatisches View→ViewModel-Mapping
│
├── DkcDesktopClient.Core/         # Kern-Bibliothek (API, Services)
│   ├── Api/
│   │   ├── IDkcApi.cs             # Refit-Interface: alle API-Endpunkte typisiert
│   │   └── DTOs.cs                # Request/Response-Records (System.Text.Json)
│   └── Services/
│       ├── AuthService.cs         # Login/Logout/AutoLogin, Berechtigungsverwaltung
│       ├── DkcApiFactory.cs       # Refit-Client-Factory (Token + Server-URL)
│       ├── TokenStore.cs          # Verschlüsselte Token-/URL-Persistenz (AppData)
│       └── UpdateService.cs       # GitHub-Release-Checker + Download + Self-Update
│
└── DkcDesktopClient.Tests/        # xUnit-Tests für Core-Services
```

---

### 9.3 Aktuelle Implementierungsstatus der Views

| View / ViewModel | Lesend | Schreibend | Hintergrund-Refresh | Caching | Status |
|------------------|--------|-----------|---------------------|---------|--------|
| `LoginView` | ✅ | ✅ | — | — | ✅ Vollständig |
| `DashboardView` | ✅ (NEA) | — | ❌ | ❌ | ⚠️ Unvollständig (nur NEA-Stats) |
| `NeaView` | ✅ | ✅ (CRUD) | ❌ | ❌ | ✅ Grundfunktionen implementiert |
| `MmView` | ✅ | ✅ (CRUD) | ❌ | ❌ | ⚠️ Teilweise (kein Anhang/Photo) |
| `BuildingView` | ✅ | ✅ (CRUD) | ❌ | ❌ | ⚠️ Teilweise (keine Checkpoints-UI) |
| `KlimaView` | ✅ | ✅ (Steuerung) | ❌ | ❌ | ⚠️ Teilweise |
| `KeysView` | ✅ | ✅ (CRUD) | ❌ | ❌ | ⚠️ Teilweise |
| `SettingsView` | ✅ | ✅ | — | — | ✅ Token, Projekte, User, Update |
| WLS (Buildings/Apartments/Records) | ❌ | ❌ | ❌ | ❌ | ❌ Nicht implementiert |
| Benachrichtigungen | ❌ | — | ❌ | ❌ | ❌ Nicht implementiert |
| Meter/PWA | ❌ | ❌ | ❌ | ❌ | ❌ Nicht implementiert |
| Admin-Benutzerverwaltung | ✅ (in Settings) | ✅ (in Settings) | — | — | ⚠️ In Settings eingebettet |

---

### 9.4 Fehlende Infrastruktur-Komponenten

| Komponente | Beschreibung | Priorität |
|------------|-------------|-----------|
| `DataCacheService` | In-Memory + Disk-Cache für API-Responses (TTL-basiert) | Hoch |
| `BackgroundRefreshService` | Periodische Hintergrundaktualisierung von Daten per Timer | Hoch |
| `NotificationService` | Polling / SSE für Benachrichtigungen, lokale Toast-Anzeige | Mittel |
| `NavigationService` | Zentraler Navigation-Service mit History/Back-Stack | Mittel |
| `DialogService` | Zentrale Dialoge (Confirm, Alert, Detail-Panels) | Mittel |
| `ThemeService` | Light/Dark-Mode-Umschaltung, Akzentfarbe | Niedrig |

---

### 9.5 Navigationsmodell (aktuell)

Die Navigation erfolgt über eine `SplitView` mit einer Sidebar-`ListBox` (NavItems). Der `MainWindowViewModel` verwaltet `CurrentView` und tauscht das aktive ViewModel aus. Es gibt kein Back-Stack oder Deep-Linking.

**Aktuell vorhandene Nav-Items (nach Login):**
1. Dashboard
2. NEA (Netzersatzanlagen)
3. Mängelmeldungen
4. Buildings (Gebäudebegehungen)
5. Climate (Klimaanlagen)
6. Keys (Schlüsselverwaltung)
7. Settings

**Fehlende Nav-Items:**
- WLS (Wohnungsleerstandserfassung)
- Benachrichtigungen / Notifications
- Meter / Zählererfassung *(Session-basiert, ggf. eingeschränkt)*

---

### 9.6 PHP/Smarty-Template → C#-View-Mapping

| PHP/Smarty Template | C# View | Status |
|---------------------|---------|--------|
| `dashboard.tpl` | `DashboardView` | ⚠️ Unvollständig (nur NEA) |
| `nea.tpl` / `nea_detail.tpl` | `NeaView` | ✅ Grundfunktionen |
| `mm.tpl` / `mm_detail.tpl` | `MmView` | ⚠️ Kein Photo-Upload |
| `building.tpl` / `building_detail.tpl` | `BuildingView` | ⚠️ Checkpoints fehlen |
| `klima.tpl` | `KlimaView` | ⚠️ Teilweise |
| `keys.tpl` | `KeysView` | ⚠️ Teilweise |
| `settings.tpl` | `SettingsView` | ✅ |
| `wls.tpl` / `apartments.tpl` / `records.tpl` | — | ❌ Fehlt |
| `notifications.tpl` | — | ❌ Fehlt |
| `meter.tpl` | — | ❌ Fehlt (Session-abhängig) |
| `admin/users.tpl` | In `SettingsView` | ⚠️ Eingebettet |

---

## 10. Phase 5 – Qualität & Abschluss (abgeschlossen)

### 10.1 Test-Coverage

Die Test-Suite wurde von 68 auf **151 Tests** ausgebaut. Alle Tests liegen in `DkcDesktopClient.Tests/`.

| Test-Datei | Beschreibung | Anzahl Tests |
|---|---|---|
| `AuthServiceTests` | IsAuthenticated, TryAutoLogin, Logout, Permissions, AuthStateChanged | 5 |
| `ConnectivityServiceTests` | Online/Offline-Erkennung | 2 |
| `CsvExportServiceTests` | CSV-Formatierung, Sonderzeichen, Encoding | — |
| `DataCacheServiceTests` | TTL, Invalidierung, Parallelität, Null-Werte | 11 |
| `NavigationServiceTests` | Back-Stack, Breadcrumbs, Events, Typ-Navigation | 15 |
| `TokenStoreTests` | Save/Load/Delete Token + ServerURL | — |
| `UpdateServiceTests` | Version-Vergleich, Asset-Auswahl, Fehlerbehandlung | — |
| `BackgroundRefreshServiceTests` | Pause bei Logout, DataRefreshed-Event, NotifyUserActivity | 5 |
| `DtoTests` | MmMessage computed properties (StatusText, Farben) | 13 |
| `ViewModelTests` | Dashboard, Notifications, Mm, Building, Nea, Klima, Keys, Login, Settings, Wls | 57 |

**Produktions-Änderungen für Testbarkeit:**
- `DkcApiFactory.Create()` ist jetzt `virtual` → Subclassing mit Mock-API in Tests
- `BackgroundRefreshService.TickInterval` ist jetzt `protected virtual` → überschreibbar für kurze Test-Zyklen

**Test-Hilfsklassen (nur in Tests):**
- `FakeDkcApiFactory` – gibt vorkonfigurierten `IDkcApi`-Mock zurück
- `FastBackgroundRefreshService` – überschreibt `TickInterval` für schnelle Test-Loops

### 10.2 Dokumentation

- **`README.md`** komplett überarbeitet: Setup-Anleitung, Feature-Tabelle, Build-Befehle für alle Plattformen, Projektstruktur
- **`CHANGELOG.md`** hinzugefügt: Versionshistorie aller 5 Phasen nach Keep-a-Changelog-Format
- **`ROADMAP.md`** Phase-5-Einträge als erledigt markiert

### 10.3 CI/CD

- `build.yml` erweitert: macOS `osx-x64` und `osx-arm64` Builds zur Build-Matrix hinzugefügt
- Rename-Schritt für Linux/macOS konsolidiert (`matrix.rid == 'linux-x64' || startsWith(matrix.rid, 'osx-')`)
- GitHub Release bei Git-Tag `v*` bereits vorhanden (seit Phase 1)

### 10.4 Aktueller Projektstatus

| Bereich | Status |
|---|---|
| Plattform-Support | Windows (win-x64), Linux (linux-x64), macOS (osx-x64, osx-arm64) |
| Build | `dotnet build DkcDesktopClient.slnx --configuration Release` |
| Test | `dotnet test DkcDesktopClient.Tests/DkcDesktopClient.Tests.csproj --configuration Release` |
| Test-Anzahl | 151 Tests |
| CI | GitHub Actions (Build + Test + Release auf Tag) |
