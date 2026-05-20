# Agent-Anweisung – PHP-Backend für die DKC-Protobuf-API

> **Zielgruppe:** Coding-Agent oder Entwickler:in im **Backend-Repository**.
> **Ziel:** Vollständige Implementierung des Protobuf-Routers in `/api.php`
> auf Basis der in diesem Repository definierten Contracts. Nach Abschluss
> spricht das Backend ausschließlich Protobuf (außer Health), die alten
> JSON/REST-Pfade bleiben übergangsweise als Legacy-Fallback erhalten.
>
> Diese Datei ist die maßgebliche Anweisung. Sie ergänzt – und hat im
> Konfliktfall Vorrang vor – allgemeinen Hinweisen in `agent.md` /
> `ProxyServer/Backend.md`, soweit es um die Protobuf-Schnittstelle geht.

---

## 0. Voraussetzungen & Quellen der Wahrheit

| Artefakt | Pfad (Client-Repo) | Rolle |
|---|---|---|
| Contracts | [`proto/dkc/*.proto`](../proto/dkc) | **Single source of truth** für Wire-Format und Action-Enum. |
| Wire-Doku | [`docs/PROTOBUF_API.md`](./PROTOBUF_API.md) | Schritt-für-Schritt-Erklärung des Envelopes, der Kompression und der Fehlercodes. |
| C#-Referenz-Implementierung | [`DkcDesktopClient.Core/Services/EnvelopeCodec.cs`](../DkcDesktopClient.Core/Services/EnvelopeCodec.cs), [`DkcProtobufApiClient.Envelope.cs`](../DkcDesktopClient.Core/Services/DkcProtobufApiClient.Envelope.cs) | Verhalten, das das Backend bit-genau spiegeln muss. |
| Legacy-Endpunkte | `agent.md` Abschnitte 5 / 6 | Verhaltens-Spezifikation der JSON-Endpunkte, die hinter den Protobuf-Actions weiterleben. |

Vor jeder Änderung im Backend zuerst diese vier Quellen lesen. Bei Abweichungen
zwischen `agent.md` und einer `.proto`-Datei **gewinnt die `.proto`-Datei**.

---

## 1. Scope (Definition of Done)

Die Backend-Aufgabe gilt als abgeschlossen, wenn alle folgenden Punkte
zutreffen:

1. **Codegen**: Aus `proto/dkc/*.proto` werden PHP-Klassen erzeugt und in das
   Backend eingecheckt (siehe § 3).
2. **Router**: `POST /api.php` erkennt Protobuf-Requests anhand der Header
   und dispatcht in einen zentralen Action-Dispatcher (siehe § 4).
3. **Auth**: `bearer_token` / `api_key` werden aus `AuthContext` gelesen und
   gegen die bestehende User-API-Token- bzw. API-Key-Tabelle validiert.
4. **Kompression**: `dkc-lz4` und `gzip` sind sowohl als Request- als auch
   als Response-Encoding implementiert, mit Fallback und Zip-Bomb-Schutz
   (siehe § 5).
5. **Health**: `GET /api.php/health` antwortet `200 OK text/plain "ok"`,
   ohne Protobuf, ohne Auth.
6. **Actions**: Mindestens die in der Migration definierten Read-only-Actions
   (Auth-Status, Dashboard, MM/NEA/Building list/detail) sind funktionsfähig.
   Die übrigen Actions sind als Handler-Stubs angelegt, geben aber bis zur
   Migration `ErrorCode.SERVICE_UNAVAILABLE` zurück (nie unhandled).
7. **Tests**: Unit- und Integrationstests sichern Envelope-Encoding,
   Kompressions-Roundtrips, Fehlerantworten und Auth-Pfad ab (siehe § 8).
8. **Doku**: Eine `docs/PROTOBUF_BACKEND.md` (im Backend-Repo) erklärt
   Inbetriebnahme, Abhängigkeiten und Betriebshinweise.

**Was nicht Teil dieser Aufgabe ist:**
- Abschalten der bestehenden JSON-Endpunkte – die laufen parallel weiter,
  bis der Client vollständig migriert ist (siehe `ROADMAP.md` Phase 6.3).
- Schema-Änderungen an der Datenbank.

---

## 2. Erwartete Verzeichnisstruktur (Backend-Repo)

```
/api.php                         # Eintrittspunkt (existiert bereits)
/proto/dkc/                      # 1:1 Kopie von proto/dkc aus dem Client-Repo
/src/Protobuf/
  ApiRouter.php                  # § 4
  EnvelopeCodec.php              # § 5
  Compression/
    Lz4Codec.php
    GzipCodec.php
  Auth/
    AuthResolver.php             # § 6
  Actions/
    AuthLoginHandler.php
    AuthStatusHandler.php
    DashboardHandler.php
    MmListHandler.php
    ...                          # ein Handler pro Action
    ActionRegistry.php
/generated/Dkc/Api/V1/           # Output von `protoc` (committed)
/tests/Protobuf/
/docs/PROTOBUF_BACKEND.md
```

Die `/proto`-Kopie wird per Skript synchron gehalten (siehe § 9). Die
generierten Klassen werden eingecheckt, damit das Backend ohne `protoc` auf
dem Server gebaut/deployed werden kann.

---

## 3. Codegenerierung

### 3.1 Abhängigkeiten

`composer.json` ergänzen:

```json
{
  "require": {
    "google/protobuf": "^3.25"
  },
  "require-dev": {
    "phpunit/phpunit": "^11.0"
  },
  "autoload": {
    "psr-4": {
      "Dkc\\Api\\V1\\": "generated/Dkc/Api/V1/",
      "App\\Protobuf\\": "src/Protobuf/"
    }
  }
}
```

PHP-Erweiterungen auf dem Server:

- `protobuf` (PECL `protobuf` – C-Implementierung, deutlich schneller als
  Pure-PHP) oder als Fallback nur `google/protobuf` ohne Native-Extension.
- `lz4` (PECL) für LZ4. Ist sie nicht verfügbar, darf das Backend LZ4
  ablehnen und ausschließlich `gzip`/`identity` antworten – siehe § 5.4.
- `zlib` ist Teil von Standard-PHP, kein zusätzliches Paket nötig.

### 3.2 Generieren

`tools/regenerate-protobuf.sh` (Backend-Repo):

```bash
#!/usr/bin/env bash
set -euo pipefail
rm -rf generated/Dkc
protoc \
  --proto_path=proto \
  --php_out=generated \
  proto/dkc/*.proto
```

Der Generator-Output landet unter `generated/Dkc/Api/V1/` (Namespace
`Dkc\Api\V1`). Diesen Pfad bitte in `composer.json` autoloaden.

> **Wichtig:** Keine manuellen Edits an `generated/`. Änderungen immer in
> den `.proto`-Dateien (im Client-Repo) und anschließend Sync + Regen.

---

## 4. `/api.php` – Router

### 4.1 Erkennung

Ein Request ist ein Protobuf-Request **genau dann**, wenn **alle** der
folgenden Bedingungen zutreffen:

- HTTP-Methode `POST` auf `/api.php` (Pfad ohne `/health`).
- Header `X-DKC-Protocol: protobuf` (case-insensitive).
- Header `Content-Type` beginnt mit `application/x-protobuf`.

Andernfalls läuft die bestehende JSON/Action-Routing-Logik unverändert
weiter (Legacy-Pfad, vorerst nicht entfernen).

### 4.2 Ablauf

```
1. Headers lesen → Protobuf-Modus?
2. Body lesen (raw: php://input). Maximalgröße prüfen (§ 5.5).
3. ApiRequest::parseFromString($body) – decoderror → INVALID_REQUEST
4. protocol_version prüfen (== 1) – sonst PROTOCOL_VERSION_MISMATCH
5. Inneres payload-Bytes anhand request.compression dekomprimieren (§ 5).
6. AuthContext auflösen (§ 6).
7. Action im Dispatcher nachschlagen (§ 7). Unbekannt → INVALID_REQUEST.
8. Handler ausführen → strongly-typed Response-Message ODER ApiError.
9. Response-Payload serialisieren, Kompression nach client-preference + size,
   ApiResponse-Envelope befüllen, Header setzen, Body senden.
10. Fehlerfälle (Exception, Auth, Permission) zentral abfangen und zu einem
    konsistenten ApiResponse{success=false, error=ApiError{…}} machen.
```

### 4.3 Pseudocode

```php
$router = new App\Protobuf\ApiRouter(/* deps */);

if ($router->matches($_SERVER)) {
    $router->handle();   // schreibt Header + Body, ruft exit
}

// Legacy-JSON-Routing wie bisher
require __DIR__ . '/legacy_router.php';
```

`ApiRouter::handle()` muss garantieren, dass jeder Pfad – auch
Exceptions – mit einem gültig serialisierten `ApiResponse` und passendem
HTTP-Status endet. Niemals HTML-Fehlerseiten, niemals leere Bodies.

### 4.4 HTTP-Statuscodes

| Bedingung | HTTP | `ApiError.code` |
|---|---|---|
| OK | 200 | – |
| Validation / Parse / Version | 400 | `INVALID_REQUEST`, `VALIDATION_ERROR`, `PROTOCOL_VERSION_MISMATCH` |
| Auth fehlt/ungültig | 401 | `UNAUTHENTICATED` |
| Berechtigung fehlt | 403 | `FORBIDDEN` |
| Ressource fehlt | 404 | `NOT_FOUND` |
| Konflikt | 409 | `CONFLICT` |
| Rate-Limit | 429 | `RATE_LIMITED` (zusätzlich `Retry-After`-Header setzen) |
| Unbekannte Compression | 400 | `UNSUPPORTED_COMPRESSION` |
| Server-Fehler | 500 | `INTERNAL_ERROR` |
| Wartung / Downstream | 503 | `SERVICE_UNAVAILABLE` |

Selbst bei nicht-2xx muss der Body ein gültig serialisierter `ApiResponse`
sein (`success=false`, `error` befüllt). Der Client liest beides.

---

## 5. Kompression

### 5.1 Verbindliche Tokens

| Encoding | HTTP-Token | `Compression` Enum |
|---|---|---|
| LZ4 (Block + 4-Byte LE Size-Header) | `dkc-lz4` | `COMPRESSION_LZ4` |
| Gzip (RFC 1952) | `gzip` | `COMPRESSION_GZIP` |
| Keine | `identity` | `COMPRESSION_IDENTITY` |

`Content-Encoding` und `ApiRequest.compression` **müssen übereinstimmen**.
Bei Diskrepanz → `INVALID_REQUEST`. Authoritative ist `compression`.

### 5.2 `dkc-lz4` – Framing

```
+---------------------+----------------------------+
|  uint32 LE original | LZ4 block-compressed bytes |
|  payload size       |                            |
+---------------------+----------------------------+
```

PHP-Encoder:

```php
$inner    = $message->serializeToString();
$compressed = lz4_compress($inner, /*level*/ 0);
$wire     = pack('V', strlen($inner)) . $compressed;
```

PHP-Decoder:

```php
if (strlen($wire) < 4) throw new ApiError(INVALID_REQUEST, 'lz4 header missing');
$origSize = unpack('V', substr($wire, 0, 4))[1];
if ($origSize > self::MAX_INFLATED_BYTES) {        // 64 MiB
    throw new ApiError(UNSUPPORTED_COMPRESSION, 'inflated size exceeds cap');
}
$inner = lz4_uncompress(substr($wire, 4));
if ($inner === false || strlen($inner) !== $origSize) {
    throw new ApiError(INVALID_REQUEST, 'lz4 decompress failed');
}
```

> Die `Size`-Angabe im Header wird **immer** geprüft, **bevor** dekomprimiert
> wird. Das ist der erste und wichtigste Zip-Bomb-Schutz.

### 5.3 Gzip

`gzencode()` zum Schreiben, `gzdecode()` zum Lesen. Inflated-Size-Cap wie
LZ4 (64 MiB). Da `gzdecode` keine streamende Größenbegrenzung erlaubt,
**erst** die rohe Eingabegröße prüfen (max 16 MiB pre-decompress), **dann**
nach dem Decompress die Output-Größe noch einmal prüfen.

### 5.4 Auswahl der Response-Encoding

Reihenfolge:

1. Wenn `Accept-Encoding` `dkc-lz4` enthält **und** das Backend LZ4
   unterstützt **und** der Response-Payload ≥ 256 Bytes ist → `dkc-lz4`.
2. Sonst, wenn `Accept-Encoding` `gzip` enthält **und** Payload ≥ 256 Bytes
   → `gzip`.
3. Sonst → `identity`.

Server-Antwort setzt sowohl `Content-Encoding`-Header als auch
`ApiResponse.compression` (beide müssen übereinstimmen).

### 5.5 Größenlimits & Zip-Bomb-Schutz

| Limit | Default | Wirkung bei Überschreitung |
|---|---|---|
| HTTP `Content-Length` (raw) | 16 MiB | `413` + `ApiError{INVALID_REQUEST}` |
| Inflated Payload | 64 MiB | `400` + `ApiError{UNSUPPORTED_COMPRESSION}` |
| Compression-Ratio-Tripwire | wenn `origSize / compressedSize > 100` | nur loggen, nicht abbrechen |

Limits gehören in eine zentrale Config (`config/protobuf.php`), nicht
hartcodiert in die Codecs.

### 5.6 Fehlerfall „unbekannte Compression"

Akzeptiert das Backend ein Encoding nicht (z. B. fehlt PECL `lz4`), muss es
mit `ApiError.code = UNSUPPORTED_COMPRESSION` antworten **und im
`ApiError.details["accept-encoding"]`** die tatsächlich unterstützte Liste
zurückgeben (z. B. `"gzip, identity"`). Der C#-Client retried dann mit dem
nächsten Encoding (siehe Referenz-Implementierung).

---

## 6. Authentifizierung

### 6.1 Token-Auflösung

Reihenfolge der Quellen für den Bearer-Token:

1. `ApiRequest.auth.bearer_token` (Protobuf, primär).
2. `Authorization: Bearer <token>` (HTTP-Header, fallback/sanity check;
   bei Diskrepanz → `UNAUTHENTICATED`).

Tokens haben das Format `dkc_<64 hex>` und werden gegen die bestehende
User-API-Token-Tabelle aufgelöst (gleiche Logik wie heute im JSON-Pfad –
unbedingt **wiederverwenden**, keinen Parallel-Pfad).

### 6.2 API-Key

`ApiRequest.auth.api_key` ist eine UUID-v4 für System-Integrationen. Hat
Vorrang vor `bearer_token`, wenn beide gesetzt sind, und wird gegen die
API-Key-Tabelle validiert.

### 6.3 Anonyme Actions

Nur diese Actions dürfen ohne Auth durchgelassen werden:

- `AUTH_LOGIN`
- `AUTH_STATUS` (gibt `{authenticated=false}` zurück, kein Fehler)

Alle anderen Actions liefern `UNAUTHENTICATED`, wenn kein gültiger Principal
aufgelöst werden kann.

### 6.4 Permissions

Pro Action ist in `ActionRegistry` (§ 7) eine Permission-Liste hinterlegt.
Der Router prüft sie zentral, **bevor** der Handler aufgerufen wird. Fehlt
eine Permission → `FORBIDDEN`. Handler dürfen davon ausgehen, dass die
Permission-Prüfung bereits erfolgt ist.

### 6.5 Logging

- Tokens (`dkc_...`) niemals in Logs schreiben. Hash (`sha256`) ist erlaubt
  für Korrelation; bevorzugt aber `auth.user_id`.
- Pro Request loggen: `request_id`, `action`, `auth.user_id`, HTTP-Status,
  `compression_in/out`, `payload_bytes_in/out`, Latenz. **Niemals** der
  Payload-Inhalt.

---

## 7. Action-Dispatch

### 7.1 Registry

```php
final class ActionRegistry
{
    /** @var array<int, ActionDefinition> */
    private array $byNumber = [];

    public function register(ActionDefinition $def): void { /* … */ }
    public function lookup(int $action): ?ActionDefinition { /* … */ }
}

final class ActionDefinition
{
    public function __construct(
        public readonly int $action,                    // Dkc\Api\V1\Action::* (Enum-Nummer)
        public readonly string $requestClass,           // FQN, z.B. Dkc\Api\V1\MmListRequest
        public readonly string $responseClass,          // FQN
        public readonly bool $anonymous,                // true für AUTH_LOGIN/AUTH_STATUS
        public readonly array $permissions,             // string[]; leer = nur eingeloggt
        public readonly ActionHandler $handler,
    ) {}
}

interface ActionHandler
{
    public function handle(Principal $principal, \Google\Protobuf\Internal\Message $request): \Google\Protobuf\Internal\Message;
}
```

Eine Action ist „bekannt", wenn sie in der Registry steht. Alle Actions aus
`Action`-Enum müssen registriert sein (auch wenn der Handler vorerst
`SERVICE_UNAVAILABLE` wirft) – sonst kann der Client nicht zuverlässig
fallback-feature-detecten.

### 7.2 Migrationsreihenfolge

In dieser Reihenfolge implementieren (Read-only zuerst, dann Write):

1. `AUTH_LOGIN`, `AUTH_LOGOUT`, `AUTH_STATUS`, `USER_INFO`
2. `DASHBOARD_DATA`, `PROJECTS_LIST`, `PROJECT_SET_ACTIVE`
3. `MM_LIST`, `MM_DETAIL`
4. `NEA_SYSTEMS`, `NEA_INSPECTIONS`, `NEA_INSPECTION_DETAIL`, `NEA_DASHBOARD`
5. `BUILDING_LIST`, `BUILDING_INSPECTIONS`, `BUILDING_INSPECTION_DETAIL`,
   `BUILDING_CHECKPOINTS_LIST`
6. Restliche Read-only Actions (KLIMA_*, KEYS_*, NOTIFICATIONS, WLS_*-LIST/DETAIL)
7. Write-Actions: `AUTH_LOGIN` (falls noch nicht), MM/NEA/Building create/update,
   KLIMA_*_CONTROL, KEYS_ISSUE/RETURN
8. Admin-Actions (`USERS_*`, `USER_TOKEN_DELETE`)

Jede Action ist erst dann „grün", wenn:

- Handler implementiert,
- Unit-Test deckt mindestens Happy-Path + 1 Fehlerfall ab,
- Integrationstest gegen den C#-Referenz-Client (siehe § 8.3) grün ist.

### 7.3 DTO-Mapping

Bestehende Domain-Models (PHP) werden in den Handlern auf die generierten
Protobuf-Messages gemappt – nicht umgekehrt. Konventionen:

- DB-`int` ↔ proto `int32` (Bereiche prüfen!).
- DB-`datetime` ↔ proto `string` als ISO-8601 in UTC.
- Optionale Felder in proto3: ein nicht gesetzter `string` ist `""`.
  Für „echte Tristate"-Booleans nutzen die Schemata zusätzliche `*_set`-Flags
  (siehe `KlimaDeviceUpdateRequest`).

---

## 8. Tests

### 8.1 Unit (PHPUnit, Backend-Repo)

Mindestens folgende Tests sind verpflichtend:

- `EnvelopeCodecTest`
  - `dkc-lz4` round-trip mit 256 B / 8 KB / 1 MB Payload.
  - `gzip` round-trip.
  - Identity bei `< 256` Bytes.
  - LZ4-Header fehlt → `UNSUPPORTED_COMPRESSION`.
  - Originalsize-Header > 64 MiB → `UNSUPPORTED_COMPRESSION`, **ohne**
    Aufruf des Decoders.
- `ApiRouterTest`
  - Unbekannte Action → `INVALID_REQUEST`.
  - Anonymous Action → kein Auth nötig.
  - Auth-Mismatch (HTTP-Header vs. Envelope) → `UNAUTHENTICATED`.
  - `protocol_version != 1` → `PROTOCOL_VERSION_MISMATCH`.
  - Body > Limit → `INVALID_REQUEST` (413 HTTP).
  - Erfolgreicher Login setzt `Content-Encoding` und Envelope-Compression
    konsistent.
- `AuthResolverTest`
  - Bearer aus Envelope.
  - API-Key hat Vorrang.
  - Ungültiger Token → `UNAUTHENTICATED`.

### 8.2 Action-Tests

Pro Handler ein Test, der mit einer Fixtures-DB ein realistisches Szenario
abbildet (mindestens Happy-Path + Permission-Denied + Validation-Error).

### 8.3 Integrationstest gegen den C#-Client

Das Backend soll einen Test-Modus haben (`APP_ENV=test`), der lokal über
PHP-Built-in-Server (`php -S 127.0.0.1:8080 -t .`) lauffähig ist. Der
C#-Referenz-Client läuft als HTTP-Client im selben Test-Harness. Mindest-Suite:

- Login → Token erhalten.
- `AUTH_STATUS` mit Token → `authenticated=true`.
- `DASHBOARD_DATA` mit großem Response → tatsächlich LZ4-komprimiert
  (anhand `Content-Encoding`).
- `MM_LIST` mit Filter → erwartete Items.
- Server lehnt `dkc-lz4` ab (Test-Hook): Client retried mit `gzip`, beide
  Aufrufe loggen denselben `request_id`-Wert.
- `UNAUTHENTICATED` (kein Token) → strukturierter Fehler mit korrektem
  HTTP-Status 401.

### 8.4 Regression

Solange JSON-Pfade leben, müssen die bestehenden HTTP-Tests weiter laufen.

---

## 9. Sync mit dem Client-Repo

Die `.proto`-Dateien dürfen nur **im Client-Repo** geändert werden. Im
Backend-Repo läuft ein Sync-Skript, das die Dateien aus dem Client-Repo
in `/proto/` spiegelt und Regen ausführt. Vorlage:

```bash
# tools/sync-proto.sh (im Backend-Repo)
set -euo pipefail
CLIENT_REPO="${CLIENT_REPO:-../dkc-desktop-client}"
rsync -av --delete "$CLIENT_REPO/proto/dkc/" proto/dkc/
./tools/regenerate-protobuf.sh
git add proto/ generated/
git status --short
```

PRs, die `proto/` oder `generated/` ohne Sync ändern, sind abzulehnen.

---

## 10. Sicherheit (Pflichtliste)

- Login-Rate-Limit: bestehende Logik (IP + Username) wiederverwenden;
  wirkt **vor** Envelope-Parsing.
- TLS in Produktion verpflichtend.
- `Content-Length`-Pflicht (kein chunked-only).
- Maximale `Content-Length` 16 MiB, maximale Inflated-Size 64 MiB
  (siehe § 5.5). Beides via Config setzbar.
- Niemals Payloads in Logs; nur Metadaten (§ 6.5).
- Kein `error_reporting`-Output im Body. Globaler `set_exception_handler`
  fängt alles, übersetzt zu `ApiError{INTERNAL_ERROR}` und loggt den
  Stacktrace serverseitig.
- CSRF spielt für Protobuf keine Rolle (kein Cookie-Auth bei
  `Authorization`-Header). Falls `session_id` doch genutzt wird, muss der
  CSRF-Token nach bestehendem Muster geprüft werden.

---

## 11. Beispiel – Minimal-Handler

```php
namespace App\Protobuf\Actions;

use App\Protobuf\ActionHandler;
use App\Protobuf\Principal;
use Dkc\Api\V1\AuthStatusResponse;
use Dkc\Api\V1\Empty as PbEmpty;
use Dkc\Api\V1\UserInfo;
use Google\Protobuf\Internal\Message;

final class AuthStatusHandler implements ActionHandler
{
    public function handle(Principal $principal, Message $request): Message
    {
        // $request ist hier PbEmpty.
        $resp = new AuthStatusResponse();
        $resp->setAuthenticated($principal->isAuthenticated());

        if ($principal->isAuthenticated()) {
            $user = new UserInfo();
            $user->setId($principal->userId());
            $user->setUsername($principal->username());
            $user->setIsAdmin($principal->isAdmin());
            $user->setActiveProjectId($principal->activeProjectId());
            $resp->setUser($user);
        }
        return $resp;
    }
}
```

Registrierung:

```php
$registry->register(new ActionDefinition(
    action:        Dkc\Api\V1\Action::AUTH_STATUS,
    requestClass:  Dkc\Api\V1\Empty::class,
    responseClass: AuthStatusResponse::class,
    anonymous:     true,
    permissions:   [],
    handler:       new AuthStatusHandler(),
));
```

---

## 12. Abgrenzung & nächste Schritte

- Der C#-Client erwartet das Verhalten dieser Spec **bit-genau**. Bei
  Abweichungen ist die Spec verbindlich, und der Backend-Patch muss
  angepasst werden – nicht der Client.
- Sobald Read-only- und Auth-Actions live sind, kann der Client mit
  Phase 6.3 (Refit-Entfernung) beginnen. Bis dahin laufen JSON und
  Protobuf nebeneinander.
- Health-Endpoint bleibt für alle Zeit ohne Protobuf. Er ist der einzige
  Endpoint, den das Backend ohne `protobuf`-/`lz4`-Extensions ausliefern
  können muss.

---

## 13. Checkliste für den Backend-Agent

Vor dem Öffnen eines Backend-PRs:

- [ ] `.proto`-Dateien sind frischer Sync aus dem Client-Repo.
- [ ] `generated/` ist neu erzeugt und committed.
- [ ] `composer install` und `composer dump-autoload` laufen.
- [ ] `phpunit` ist grün (mind. § 8.1, § 8.2 betroffener Handler).
- [ ] Integrationstest gegen den C#-Client läuft (§ 8.3).
- [ ] `docs/PROTOBUF_BACKEND.md` ist aktualisiert (Setup, Limits, Env-Vars).
- [ ] Keine Tokens in Logs (`grep -R "dkc_" logs/` → leer).
- [ ] Health-Endpoint funktioniert ohne `protobuf`/`lz4`-Extensions.
- [ ] Alle Action-Enum-Werte sind in `ActionRegistry` registriert
      (auch Stubs mit `SERVICE_UNAVAILABLE`).
