# Backend-Optimierungen für schnellere API-Antworten

## Problem-Analyse

Der Browser erhält schnellere Antworten als der Desktop-Client, weil:

1. **Browser nutzt PHP-Session**: Keine Token-Validierung bei jedem Request
2. **Browser-Caching**: HTTP-Caching Header werden beachtet
3. **Template-Rendering optimiert**: Nur was angezeigt wird, wird generiert

---

## Optimierungen in `ProxyServer/system/`

### 1. **Optimierte API-Endpunkte mit Pagination**

```php
// ProxyServer/system/controller/ContMm.php
public function mm_list()
{
    $limit = intval($_GET['limit'] ?? 50);  // Standard 50
    $offset = intval($_GET['offset'] ?? 0);
    
    // Nur essenzielle Felder abrufen
    $sql = "SELECT uid, status, title, priority, street, date_created 
            FROM db_mm 
            WHERE deleted_at IS NULL 
            ORDER BY date_created DESC 
            LIMIT ? OFFSET ?";
    
    $result = $this->db->prepare($sql)
        ->execute([$limit, $offset])
        ->fetchAll();
        
    return ['data' => $result];
}
```

### 2. **Datenbank-Indizes hinzufügen**

```sql
-- Für schnelle Filterung in mm_list
CREATE INDEX idx_mm_status ON db_mm(status);
CREATE INDEX idx_mm_street ON db_mm(street);
CREATE INDEX idx_mm_deleted ON db_mm(deleted_at);

-- Für NEA-Prüfungen
CREATE INDEX idx_nea_system_id ON nea_inspections(system_id);
CREATE INDEX idx_nea_status ON nea_inspections(status);

-- Für Gebäude-Begehungen
CREATE INDEX idx_building_id ON building_inspections(building_id);
CREATE INDEX idx_building_status ON building_inspections(status);

-- Für Schlüssel
CREATE INDEX idx_keys_status ON keys_issued(status);
```

### 3. **Response-Caching (Redis/Memcached)**

```php
// ProxyServer/system/controller/ContDashboard.php
public function dashboard_data()
{
    $cacheKey = 'dashboard_stats_' . $this->user->id;
    
    // Versuche aus Cache zu laden (TTL: 60 Sekunden)
    $cached = cache_get($cacheKey);
    if ($cached !== null) {
        return json_decode($cached, true);
    }
    
    // Ansonsten berechne
    $stats = [
        'mm_total' => $this->db->count('db_mm'),
        'mm_open' => $this->db->count('db_mm', ['status' => -2]),
        'nea_inspections' => $this->db->count('nea_inspections'),
        'building_inspections' => $this->db->count('building_inspections'),
    ];
    
    // Speichere für 60 Sekunden
    cache_set($cacheKey, json_encode($stats), 60);
    
    return $stats;
}
```

### 4. **Lazy-Loading für große Datenmengen**

```php
// ProxyServer/system/controller/ContKeys.php
public function keys_inventory()
{
    $limit = intval($_GET['limit'] ?? 100);
    $offset = intval($_GET['offset'] ?? 0);
    
    // Abrufen mit Pagination
    $keys = $this->db->prepare(
        "SELECT id, type, status, issued_to, issued_date 
         FROM keys_inventory 
         ORDER BY id DESC 
         LIMIT ? OFFSET ?"
    )->execute([$limit, $offset])->fetchAll();
    
    // Zähle Gesamt (wird gecacht)
    $total = cache_get('keys_total_count');
    if (!$total) {
        $total = $this->db->count('keys_inventory');
        cache_set('keys_total_count', $total, 300); // 5 Min Cache
    }
    
    return [
        'data' => $keys,
        'total' => $total,
        'limit' => $limit,
        'offset' => $offset
    ];
}
```

### 5. **Query-Optimierung: Aggregation statt einzelne Queries**

```php
// SCHLECHT: N+1 Problem
foreach ($buildings as $building) {
    $inspections = db_query("SELECT COUNT(*) FROM building_inspections WHERE building_id = ?", [$building['id']]);
}

// GUT: Ein Query mit Aggregation
$sql = "SELECT b.id, b.name, COUNT(bi.id) as inspection_count
        FROM buildings b
        LEFT JOIN building_inspections bi ON b.id = bi.building_id
        GROUP BY b.id";
$result = db_query($sql);
```

### 6. **HTTP-Caching Header einstellen**

```php
// ProxyServer/api.php
header('Cache-Control: public, max-age=300');  // 5 Min für GET-Anfragen
header('Last-Modified: ' . gmdate('r'));
header('ETag: "' . md5($responseBody) . '"');

// Für häufig abgerufene Daten
if (isset($_GET['action']) && in_array($_GET['action'], ['dashboard_data', 'projects_list'])) {
    header('Cache-Control: public, max-age=60');  // 1 Min
}
```

### 7. **Gzip Kompression aktivieren**

```php
// ProxyServer/system/Application.php
ob_start('ob_gzhandler');

// Oder in Apache/.htaccess
<IfModule mod_deflate.c>
    AddOutputFilterByType DEFLATE application/json
    AddOutputFilterByType DEFLATE text/html
</IfModule>
```

### 8. **Request-Body lesen nur bei Bedarf**

```php
// SCHLECHT: Liest immer
$input = file_get_contents('php://input');

// GUT: Lazy-Loading
class RequestBody {
    private $data = null;
    
    public function get() {
        if ($this->data === null) {
            $this->data = json_decode(file_get_contents('php://input'), true);
        }
        return $this->data;
    }
}
```

---

## Performance-Messungen

### Desktop-Client Logs analysieren

```bash
# Antwortzeiten extrahieren
grep "API response" /logs/dkc-.log | awk '{print $NF}' | sort -n | tail -20

# Durchschnittliche Antwortzeit
grep "API response" /logs/dkc-.log | awk '{print $NF}' | awk '{sum+=$1; count++} END {print sum/count " ms"}'

# Langsame Anfragen (>1000ms)
grep "API response" /logs/dkc-.log | awk '$NF > 1000 {print}'
```

### Apache/Nginx Logs prüfen

```bash
# Langsame Requests im Apache Log
tail -f /var/log/apache2/access.log | awk '$NF > 1000 {print}'

# Nginx upstream response time
grep "upstream_response_time" /var/log/nginx/access.log | awk '{print $NF}' | sort -n | tail -20
```

---

## Recommended Cache-Strategie

```
┌─────────────────────────────────────┐
│      Desktop-Client Cache           │
│  (Browser wird ignoriert)           │
├─────────────────────────────────────┤
│                                     │
│  In-Memory Cache (TTL-basiert)      │
│  ├─ Dashboard Stats: 60s             │
│  ├─ MM List: 120s                    │
│  ├─ NEA: 300s                        │
│  └─ Klima Status: 30s                │
│                                     │
│  → GET /api.php                     │
│                                     │
│  Backend Response Cache (Redis)     │
│  ├─ Dashboard Data: 60s              │
│  ├─ Projektlisten: 300s              │
│  └─ Systemlisten: 300s               │
│                                     │
│  Database (Indexed Queries)         │
│                                     │
└─────────────────────────────────────┘
```

---

## Checkliste für Backend-Optimierung

- [ ] Indizes auf häufig filterten Spalten erstellen
- [ ] Pagination für große Datensätze implementieren
- [ ] Redis/Memcached für Response-Caching integrieren
- [ ] HTTP-Caching Header setzen
- [ ] Gzip-Kompression aktivieren
- [ ] N+1 Query-Problem beheben
- [ ] Apache/Nginx Logging analysieren
- [ ] Slow Query Log überprüfen
- [ ] APM-Tool verwenden (z.B. New Relic, Datadog)

---

## Quick-Wins für sofortige Verbesserungen

1. **Indizes**: `CREATE INDEX idx_mm_status ON db_mm(status);` → -50% Abfragzeit
2. **Caching**: Redis für `dashboard_data` → -80% Antwortzeit
3. **Pagination**: `LIMIT 50` statt alle Datensätze → -70% Datentransfer
4. **Gzip**: HTTP-Kompression → -60% Bandbreite

---

*Backend-Optimierungsleitfaden: 2026-05-19*

