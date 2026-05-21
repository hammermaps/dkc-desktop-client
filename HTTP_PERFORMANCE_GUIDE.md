# HTTP Performance-Optimierungen – Desktop-Client

## Durchgeführte Verbesserungen

### 1. **HTTP-Client Konfiguration**
- **Connection Pooling**: Aktiviert für Wiederverwendung von TCP-Verbindungen
- **Keep-Alive**: Verbindungen bleiben bis zu 5 Sekunden offen (100 Requests max)
- **Timeout-Optimierung**: 
  - Request Timeout: 30 Sekunden (konfigurierbar)
  - Connect Timeout: 10 Sekunden
  - Verhindert Hängen bei langsamen Verbindungen

### 2. **HTTP/2 & Kompression**
- HTTP/2 automatisch wenn unterstützt (Multiplexing mehrerer Anfragen)
- Gzip/Deflate Kompression für Request/Response
- Reduziert Bandbreite und verbessert Durchsatz

### 3. **Logging-Optimierung**
- Request/Response Body wird nur im Debug-Level geloggt
- Performance-Metriken (Response Time in ms) werden auf Info-Level geloggt
- Verhindert I/O-Overhead durch exzessives Logging

### 4. **Background Refresh Service**
- Hochfrequente Daten (Klima, Dashboard) werden sequenziell geladen
- Seltener aktualisierte Daten (MM, NEA, Keys) werden parallel geladen
- Verhindert Ressourcen-Überlastung durch zu viele gleichzeitige Requests

### 5. **DNS-Caching** (über HttpClientHandler)
- Verbesserte DNS-Auflösung
- Reduziert Latenz bei wiederholten Anfragen

---

## Konfigurierbare Einstellungen

Alle Performance-Parameter sind über Umgebungsvariablen oder die `HttpPerformanceConfig` anpassbar:

### Umgebungsvariablen

```bash
# Request-Timeout in Sekunden (default: 30)
DKC_HTTP_REQUEST_TIMEOUT=60

# Connection-Timeout in Sekunden (default: 10)
DKC_HTTP_CONNECT_TIMEOUT=15

# Max. gleichzeitige Verbindungen (default: 10)
DKC_HTTP_MAX_CONNECTIONS=20
```

### Beispiel-Konfiguration für verschiedene Szenarien

#### Langsame/unstabile Verbindung
```bash
DKC_HTTP_REQUEST_TIMEOUT=60
DKC_HTTP_CONNECT_TIMEOUT=20
DKC_HTTP_MAX_CONNECTIONS=5
```

#### Schnelle Verbindung (LAN)
```bash
DKC_HTTP_REQUEST_TIMEOUT=15
DKC_HTTP_CONNECT_TIMEOUT=5
DKC_HTTP_MAX_CONNECTIONS=20
```

---

## Performance-Tipps

### Auf Client-Seite
1. **Browser vs. Desktop**: Im Browser ist api.php schneller, weil:
   - PHP-Session Cookie ist bereits vorhanden
   - Keine Authentifizierung nötig bei jeder Anfrage
   - Browser-Caching optimierter

2. **Desktop-Client Optimierungen**:
   - Cache wird automatisch verwendet (TTL-basiert)
   - Hintergrund-Refresh lädt Daten automatisch nach (nicht blockierend)
   - Paralleles Laden von mehreren Datensätzen gleichzeitig

3. **Netzwerk-Optimierungen**:
   - HTTP/2 Multiplexing nutzen (mehrere Anfragen über eine Verbindung)
   - Connection Pooling reduziert TCP-Handshakes
   - Keep-Alive verhindert häufiges Neu-Öffnen von Verbindungen

### Auf Server-Seite (Backend)

#### PHP Backend-Optimierungen
1. **Query-Optimierungen**
   ```php
   // In ContMm.php, ContNea.php etc.
   // Verwende Pagination/Limits bei großen Datenmengen
   // Beispiel: ?limit=50&offset=0
   ```

2. **Caching auf Backend-Seite**
   ```php
   // Verwende Redis/Memcached für häufig abgerufene Daten
   // Z.B. Dashboard-Statistiken, Systemlisten
   ```

3. **Datenbank-Indizes**
   - Sicherstelle Indizes auf häufig filterten Spalten
   - Z.B. status, building_id, system_id in MM-Tabelle

4. **API-Response-Größe minimieren**
   ```php
   // Filtere nur notwendige Felder
   // Z.B. in mm_list: uid, status, title, priority
   // Nicht alle 50 Spalten
   ```

5. **Bild/Datei-Kompression**
   - Verwende WebP statt JPEG/PNG
   - Skaliere große Bilder herunter

---

## Monitoring & Debugging

### Performance-Metriken in Logs

Die Logs zeigen Response-Zeit für jede API-Anfrage:
```
2026-05-19 14:32:15 [INFO] API response GET https://... => 200 (234 ms)
```

### Debug-Logging aktivieren

```bash
# Vollständiges API-Logging mit Request/Response Body
DKC_LOG_LEVEL=Debug
```

Oder in `~/.config/DkcDesktopClient/logging.json`:
```json
{
  "logLevel": "Debug"
}
```

---

## Häufige Probleme & Lösungen

### ❌ Problem: Langsame API-Anfragen im Desktop-Client
**Ursachen:**
- Schlechte Netzwerkverbindung
- Server antwortet zu langsam
- Zu viele gleichzeitige Anfragen

**Lösungen:**
1. Response-Zeit prüfen: `tail -f /logs/dkc-.log | grep "API response"`
2. Server-Last prüfen: `top` / `htop` auf Server
3. Netzwerk-Latenz: `ping` / `mtr <server-ip>`

### ❌ Problem: Timeout-Fehler
**Lösungen:**
```bash
# Erhöhe Timeouts
DKC_HTTP_REQUEST_TIMEOUT=60
DKC_HTTP_CONNECT_TIMEOUT=20
```

### ❌ Problem: Zu viele Verbindungen
**Lösungen:**
```bash
# Reduziere Max-Connections
DKC_HTTP_MAX_CONNECTIONS=5
```

---

## Weitere Performance-Verbesserungen (Zukunft)

- [ ] HTTP/2 Server Push für häufig abgerufene Daten
- [ ] WebSocket für Echtzeit-Updates statt Polling
- [ ] Incremental Sync statt vollständiger Datensätze
- [ ] Lokale SQLite DB für Offline-Funktionalität
- [ ] GraphQL API für granulare Feldauswahl

---

*Dokumentation: 2026-05-19*

