# Fehlende & benötigte API-Endpunkte — ProxyServer

> **Aufgabe für Backend-Agent:**  
> Implementiere die unten aufgeführten Endpunkte in `api.php` des ProxyServer-Projekts.  
> Alle Endpunkte folgen dem bestehenden Schema:
> - Aufruf via `GET/POST/PUT/DELETE /api.php?action=<action_name>`
> - Auth-Prüfung per Bearer-Token (Header `Authorization: Bearer <token>`)
> - JSON-Response mit `{ "success": bool, "error": string|null, ... }`
> - Fehler: HTTP 400/401/403/404 mit `{ "success": false, "error": "..." }`
>
> ⚠️ **Hinweis:** Diese Liste basiert auf dem aktuellen Read-Only-Desktop-Client.  
> Sie muss nach Analyse von `template/default/*` im ProxyServer-Projekt ergänzt/korrigiert werden —  
> alle dort vorhandenen Formular-Controls und Aktions-Buttons müssen durch einen Endpunkt abgebildet sein.

---

## 1. NEA (Notfall-/Evakuierungsanlagen)

### 1.1 `nea_system_create` — NEA-Anlage anlegen
**Methode:** `POST`  
**Request-Body (JSON):**
```json
{
  "name": "string (required)",
  "description": "string|null",
  "location": "string|null",
  "manufacturer": "string|null",
  "model": "string|null",
  "serial_number": "string|null",
  "installation_date": "YYYY-MM-DD|null",
  "enabled": true,
  "project_id": 1
}
```
**Response:**
```json
{ "success": true, "id": 42 }
```

---

### 1.2 `nea_system_update` — NEA-Anlage bearbeiten
**Methode:** `PUT`  
**Query-Parameter:** `id` (int, required)  
**Request-Body:** identisch mit `nea_system_create` (alle Felder optional außer `id`)  
**Response:**
```json
{ "success": true }
```

---

### 1.3 `nea_system_delete` — NEA-Anlage löschen
**Methode:** `DELETE`  
**Query-Parameter:** `id` (int, required)  
**Response:**
```json
{ "success": true }
```

---

### 1.4 `nea_inspection_create` — NEA-Prüfung anlegen
**Methode:** `POST`  
**Request-Body (JSON):**
```json
{
  "nea_system_id": 1,
  "inspection_type": "string (z.B. 'annual', 'monthly')",
  "inspection_date": "YYYY-MM-DD",
  "inspector_id": 1,
  "status": "string (z.B. 'open', 'in_progress', 'completed')",
  "overall_result": "string (z.B. 'ok', 'defects_found', 'failed')",
  "runtime_hours": 1234,
  "runtime_hours_after": 1240,
  "notes": "string|null",
  "defects_found": "string|null",
  "corrective_actions": "string|null",
  "checklist_data": {}
}
```
**Response:**
```json
{ "success": true, "id": 99 }
```

---

### 1.5 `nea_inspection_update` — NEA-Prüfung bearbeiten
**Methode:** `PUT`  
**Query-Parameter:** `id` (int, required)  
**Request-Body:** identisch mit `nea_inspection_create` (alle Felder optional)  
**Response:**
```json
{ "success": true }
```

---

### 1.6 `nea_inspection_complete` — NEA-Prüfung abschließen
**Methode:** `POST`  
**Query-Parameter:** `id` (int, required)  
**Request-Body:**
```json
{
  "overall_result": "string",
  "notes": "string|null"
}
```
**Response:**
```json
{ "success": true }
```

---

### 1.7 `nea_checklist_update` — Checklisten-Einträge einer Prüfung speichern
**Methode:** `POST`  
**Query-Parameter:** `inspection_id` (int, required)  
**Request-Body:**
```json
{
  "items": [
    {
      "checkpoint_id": 1,
      "status": "string (z.B. 'ok', 'nok', 'n/a')",
      "note": "string|null",
      "comment": "string|null"
    }
  ]
}
```
**Response:**
```json
{ "success": true }
```

---

## 2. MM (Mängelmeldungen)

### 2.1 `mm_create` — Neue Mängelmeldung anlegen
**Methode:** `POST`  
**Request-Body (JSON):**
```json
{
  "betreff": "string (required)",
  "meldung_massage": "string|null",
  "street": "string|null",
  "whg": "string|null",
  "melder": "string|null",
  "tel": "string|null",
  "email": "string|null",
  "dringlichkeit": "string (z.B. 'normal', 'dringend', 'notfall')",
  "nachunternehmer": "string|null",
  "zugeh": "string|null"
}
```
**Response:**
```json
{ "success": true, "uid": "MM-2024-001" }
```

---

### 2.2 `mm_update` — Mängelmeldung bearbeiten
**Methode:** `PUT`  
**Query-Parameter:** `uid` (string, required)  
**Request-Body:** identisch mit `mm_create` (alle Felder optional)  
**Response:**
```json
{ "success": true }
```

---

### 2.3 `mm_update_status` — Status einer Mängelmeldung ändern
**Methode:** `POST`  
**Query-Parameter:** `uid` (string, required)  
**Request-Body:**
```json
{
  "status": 0,
  "comment": "string|null"
}
```
*Status-Werte: 0 = offen, 1 = in Bearbeitung, 2 = geschlossen, 3 = abgebrochen*  
**Response:**
```json
{ "success": true }
```

---

### 2.4 `mm_assign_contractor` — Nachunternehmer zuweisen
**Methode:** `POST`  
**Query-Parameter:** `uid` (string, required)  
**Request-Body:**
```json
{
  "nachunternehmer": "string"
}
```
**Response:**
```json
{ "success": true }
```

---

### 2.5 `mm_delete` — Mängelmeldung löschen
**Methode:** `DELETE`  
**Query-Parameter:** `uid` (string, required)  
**Response:**
```json
{ "success": true }
```

---

## 3. Buildings (Gebäude-Begehungen)

### 3.1 `building_create` — Gebäude anlegen
**Methode:** `POST`  
**Request-Body (JSON):**
```json
{
  "name": "string (required)",
  "address": "string|null",
  "description": "string|null",
  "enabled": true,
  "project_id": 1
}
```
**Response:**
```json
{ "success": true, "id": 10 }
```

---

### 3.2 `building_update` — Gebäude bearbeiten
**Methode:** `PUT`  
**Query-Parameter:** `id` (int, required)  
**Request-Body:** identisch mit `building_create` (alle Felder optional)  
**Response:**
```json
{ "success": true }
```

---

### 3.3 `building_inspection_create` — Gebäude-Begehung anlegen
**Methode:** `POST`  
**Request-Body (JSON):**
```json
{
  "building_id": 1,
  "title": "string|null",
  "inspection_date": "YYYY-MM-DD|null",
  "status": "string (z.B. 'open', 'in_progress', 'completed')",
  "weather": "string|null",
  "attendees": "string|null",
  "general_notes": "string|null"
}
```
**Response:**
```json
{ "success": true, "id": 55 }
```

---

### 3.4 `building_inspection_update` — Gebäude-Begehung bearbeiten
**Methode:** `PUT`  
**Query-Parameter:** `id` (int, required)  
**Request-Body:** identisch mit `building_inspection_create` (alle Felder optional)  
**Response:**
```json
{ "success": true }
```

---

### 3.5 `building_inspection_complete` — Begehung abschließen
**Methode:** `POST`  
**Query-Parameter:** `id` (int, required)  
**Request-Body:**
```json
{
  "overall_result": "string (z.B. 'ok', 'defects_found')",
  "general_notes": "string|null"
}
```
**Response:**
```json
{ "success": true }
```

---

### 3.6 `building_checkpoint_update` — Prüfpunkt-Ergebnis eintragen
**Methode:** `POST`  
**Query-Parameter:** `inspection_id` (int, required)  
**Request-Body:**
```json
{
  "checkpoint_id": 1,
  "status": "string (z.B. 'ok', 'nok', 'n/a')",
  "note": "string|null",
  "comment": "string|null"
}
```
**Response:**
```json
{ "success": true }
```

---

### 3.7 `building_checkpoints_list` — Prüfpunkte für ein Gebäude/Begehungsvorlage
**Methode:** `GET`  
**Query-Parameter:** `building_id` (int, optional), `inspection_id` (int, optional)  
**Response:**
```json
{
  "success": true,
  "checkpoints": [
    { "id": 1, "name": "string", "category": "string|null", "sort": 0 }
  ]
}
```

---

## 4. Klima (Klimasteuerung)

### 4.1 `klima_status` — Erweitert: Echtzeit-Status aller Geräte
**Methode:** `GET` *(bereits vorhanden, aber vermutlich unstrukturiert)*  
**Erwartete Response-Erweiterung:**
```json
{
  "success": true,
  "timestamp": "2024-01-15T10:30:00Z",
  "devices": [
    {
      "address": 1,
      "name": "string",
      "online": true,
      "power": true,
      "mode": "string (cooling|heating|fan|auto|dry)",
      "setpoint": 22.0,
      "current_temp": 23.5,
      "fan_speed": "string (auto|low|medium|high)",
      "error_code": "string|null"
    }
  ]
}
```

---

### 4.2 `klima_device_control` — Einzelgerät steuern
**Methode:** `POST`  
**Request-Body (JSON):**
```json
{
  "address": 1,
  "power": true,
  "mode": "string (cooling|heating|fan|auto|dry)|null",
  "setpoint": 22.0,
  "fan_speed": "string (auto|low|medium|high)|null"
}
```
**Response:**
```json
{ "success": true }
```

---

### 4.3 `klima_group_control` — Gerätegruppe steuern
**Methode:** `POST`  
**Request-Body (JSON):**
```json
{
  "group_id": 1,
  "power": true,
  "mode": "string|null",
  "setpoint": 22.0,
  "fan_speed": "string|null"
}
```
**Response:**
```json
{ "success": true, "affected_devices": 4 }
```

---

### 4.4 `klima_groups_list` — Gerätegruppen auflisten
**Methode:** `GET`  
**Response:**
```json
{
  "success": true,
  "groups": [
    { "id": 1, "name": "string", "device_count": 4 }
  ]
}
```

---

### 4.5 `klima_device_update` — Gerätekonfiguration bearbeiten (Name, Gruppe, Sortierung)
**Methode:** `PUT`  
**Query-Parameter:** `address` (int, required)  
**Request-Body:**
```json
{
  "name": "string|null",
  "group_id": 1,
  "enabled": true,
  "sort": 0
}
```
**Response:**
```json
{ "success": true }
```

---

## 5. Keys (Schlüsselverwaltung)

### 5.1 `keys_create` — Schlüssel/Schlüsseltyp anlegen
**Methode:** `POST`  
**Request-Body (JSON):**
```json
{
  "name": "string (required)",
  "description": "string|null",
  "total": 1
}
```
**Response:**
```json
{ "success": true, "id": 7 }
```

---

### 5.2 `keys_update` — Schlüssel/Schlüsseltyp bearbeiten
**Methode:** `PUT`  
**Query-Parameter:** `id` (int, required)  
**Request-Body:** identisch mit `keys_create` (alle Felder optional)  
**Response:**
```json
{ "success": true }
```

---

### 5.3 `keys_issue` — Schlüssel ausgeben
**Methode:** `POST`  
**Request-Body (JSON):**
```json
{
  "key_id": 1,
  "issued_to": "string (required)",
  "issued_at": "YYYY-MM-DD (required)",
  "notes": "string|null"
}
```
**Response:**
```json
{ "success": true, "id": 23 }
```

---

### 5.4 `keys_return` — Schlüssel zurückgeben
**Methode:** `POST`  
**Query-Parameter:** `id` (int, required — ID des Ausgabe-Eintrags)  
**Request-Body:**
```json
{
  "returned_at": "YYYY-MM-DD",
  "notes": "string|null"
}
```
**Response:**
```json
{ "success": true }
```

---

### 5.5 `keys_delete` — Schlüssel-Ausgabe-Eintrag löschen
**Methode:** `DELETE`  
**Query-Parameter:** `id` (int, required)  
**Response:**
```json
{ "success": true }
```

---

## 6. Projekte (Admin)

### 6.1 `project_create` — Projekt anlegen
**Methode:** `POST`  
**Request-Body (JSON):**
```json
{
  "name": "string (required)",
  "description": "string|null"
}
```
**Response:**
```json
{ "success": true, "id": 3 }
```

---

### 6.2 `project_update` — Projekt bearbeiten
**Methode:** `PUT`  
**Query-Parameter:** `id` (int, required)  
**Request-Body:** identisch mit `project_create`  
**Response:**
```json
{ "success": true }
```

---

### 6.3 `project_set_active` — Aktives Projekt wechseln
**Methode:** `POST`  
**Request-Body (JSON):**
```json
{ "project_id": 2 }
```
**Response:**
```json
{ "success": true }
```

---

## 7. Benutzerverwaltung (Admin)

### 7.1 `users_list` — Benutzerliste (Admin)
**Methode:** `GET`  
**Response:**
```json
{
  "success": true,
  "users": [
    {
      "id": 1,
      "username": "string",
      "vname": "string",
      "nname": "string",
      "email": "string",
      "is_admin": false,
      "active_project_id": 1
    }
  ]
}
```

---

### 7.2 `user_create` — Neuen Benutzer anlegen (Admin)
**Methode:** `POST`  
**Request-Body (JSON):**
```json
{
  "username": "string (required)",
  "password": "string (required)",
  "vname": "string",
  "nname": "string",
  "email": "string",
  "is_admin": false
}
```
**Response:**
```json
{ "success": true, "id": 5 }
```

---

### 7.3 `user_update` — Benutzer bearbeiten (Admin)
**Methode:** `PUT`  
**Query-Parameter:** `id` (int, required)  
**Request-Body:** identisch mit `user_create` (alle Felder optional außer Passwort)  
**Response:**
```json
{ "success": true }
```

---

### 7.4 `user_delete` — Benutzer löschen (Admin)
**Methode:** `DELETE`  
**Query-Parameter:** `id` (int, required)  
**Response:**
```json
{ "success": true }
```

---

## Implementierungshinweise für den Backend-Agent

1. **Konsistenz:** Alle Endpunkte geben `{ "success": false, "error": "..." }` zurück bei Fehler
2. **Auth:** Jeder Endpunkt prüft zuerst den Bearer-Token über die bestehende Auth-Middleware
3. **Admin-Prüfung:** Endpunkte für Benutzerverwaltung und Projektverwaltung prüfen zusätzlich `is_admin === true`
4. **Validierung:** Required-Felder im Request-Body validieren, bei fehlendem Pflichtfeld HTTP 400 zurückgeben
5. **Atomarität bei Checklisten:** `nea_checklist_update` und `building_checkpoint_update` in einer Datenbank-Transaktion ausführen

---

## Template-Analyse noch ausstehend

> ⚠️ Diese Liste ist vorläufig und basiert auf dem aktuellen Desktop-Client.  
> Nach Zugriff auf `hammermaps/ProxyServer` müssen die Dateien unter `template/default/*`  
> analysiert werden und diese Liste entsprechend ergänzt/angepasst werden.  
> Insbesondere für den Klima-Bereich (`klima_*`) hängen die konkreten Steuer-Endpunkte  
> stark von der verwendeten Hardware/Middleware ab.
