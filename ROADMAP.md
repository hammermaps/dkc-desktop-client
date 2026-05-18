# DKC Desktop Client – Entwicklungs-Roadmap

> **Ziel:** Vollständige Abbildung der PHP/Smarty-HTML-UI als native Desktop-Anwendung (Avalonia UI / C# / .NET 8) mit modernem, flüssigem UI-Design, Hintergrundaktualisierungen und effizientem Datencaching.

---

## Übersicht

```
Phase 1 – Infrastruktur & Fundament       [Voraussetzung für alles weitere]
Phase 2 – UI/UX-Modernisierung            [Modernes, einheitliches Design]
Phase 3 – Feature-Parität zur HTML-UI     [Vollständige Funktionsabdeckung]
Phase 4 – Desktop-Optimierungen           [Native Stärken ausspielen]
Phase 5 – Qualität & Abschluss            [Tests, Dokumentation, Release]
```

---

## Phase 1 – Infrastruktur & Fundament

### 1.1 `DataCacheService` (Core)

Zentraler In-Memory-Cache mit optionaler Disk-Persistenz für API-Responses.

**Anforderungen:**
- TTL-basiert (konfigurierbar pro Datentyp, z. B. Dashboard 60 s, Geräteliste 5 min)
- Generischer Cache-Key (`string` oder typisiert via `CacheKey<T>`)
- `GetOrFetchAsync<T>(key, fetcher, ttl)` – gibt gecachten Wert oder ruft API auf
- `Invalidate(key)` / `InvalidateAll()` für manuelle Cache-Invalidierung nach Schreiboperationen
- Optional: JSON-Serialisierung in `AppData/cache/` für Offline-Vorschau beim Start
- Thread-sicher (`ConcurrentDictionary` + `SemaphoreSlim` pro Key)

**Betroffene Daten (Standardwerte):**
| Daten | TTL |
|-------|-----|
| Dashboard-Statistiken | 60 s |
| NEA-Anlagen-Liste | 5 min |
| NEA-Prüfungsliste | 2 min |
| MM-Liste | 2 min |
| Gebäudeliste | 10 min |
| Klima-Gerätekonfiguration | 10 min |
| Klima-Betriebsstatus | 30 s |
| Schlüssel-Inventar | 5 min |
| Projekteliste | 10 min |
| Benutzerliste (Admin) | 5 min |
| WLS-Gebäude / Apartments | 10 min |

---

### 1.2 `BackgroundRefreshService` (Core)

Hintergrundservice für periodische Datenpflege ohne Benutzerinteraktion.

**Anforderungen:**
- Implementiert `IHostedService` / `BackgroundService`
- Konfigurierbare Refresh-Intervalle pro Datentyp (via `IOptions<RefreshConfig>`)
- Verwendet `DataCacheService.Invalidate()` + lädt Daten neu per `IDkcApi`
- Benutzeraktion setzt Timer zurück (verhindert unnötige Hintergrundcalls während aktiver Nutzung)
- Pausiert, wenn kein Token vorhanden (nicht eingeloggt)
- Löst `DataRefreshed`-Event aus → ViewModels binden sich daran und aktualisieren UI automatisch

**Priorisierte Refresh-Typen:**
1. Klima-Betriebsstatus (30 s – für Live-Steuerung)
2. Dashboard-Statistiken (60 s)
3. Benachrichtigungs-Zähler (60 s – als Polling-Ersatz für SSE)
4. MM-Statusänderungen (2 min)
5. NEA-Prüfungsstatus (5 min)

---

### 1.3 `NotificationPollingService` (Core)

Polling-basierter Benachrichtigungsservice als Ersatz für Server-Sent Events.

**Anforderungen:**
- Pollt `GET /api.php?action=notifications` (oder `get_notification_count`) alle 60 s
- Speichert ungelesene Benachrichtigungen in einer `ObservableCollection`
- Löst lokale Desktop-Benachrichtigung aus (Windows: Toast via WinRT, Linux/macOS: System-Notification)
- Zähler wird im Sidebar-Badge angezeigt
- Markiert Benachrichtigungen als gelesen nach Anzeige

---

### 1.4 `NavigationService` (App)

Zentraler Navigation-Service mit Verlauf.

**Anforderungen:**
- Ersetzt direkte `CurrentView`-Zuweisung im `MainWindowViewModel`
- Back-Stack (`Stack<ViewModelBase>`) für Breadcrumb-Navigation
- `NavigateTo<TViewModel>()` mit optionalen Parametern (z. B. `NavigateTo<NeaView>(systemId: 5)`)
- `NavigateBack()` 
- Unterstützt modale Overlay-Ansichten (Detail-Panels) ohne Sidebar-Wechsel
- Breadcrumb-Anzeige in der Header-Leiste (z. B. „NEA > Anlage X > Prüfung 123")

---

### 1.5 `DialogService` (App)

Einheitlicher Service für alle modalen Dialoge.

**Anforderungen:**
- `ConfirmAsync(title, message)` → `bool`
- `AlertAsync(title, message)`
- `ShowDetailPanelAsync<TViewModel>()` → Slide-In Panel von rechts
- `ShowFormDialogAsync<TViewModel>()` → Zentriertes Modal-Fenster
- Alle Dialoge verwenden das neue Design-System (Phase 2)

---

## Phase 2 – UI/UX-Modernisierung

### 2.1 Design-System & Theme

Einheitliches, modernes Design auf Basis von Avalonia FluentTheme.

**Anforderungen:**
- **Farbpalette:** Dunkle Sidebar (#1E2235), heller Content-Bereich (#F8FAFC), Akzentfarbe (#3B82F6 Blau)
- **Typografie:** Inter Font, definierte Größen-Skala (xs/sm/base/lg/xl/2xl)
- **Abstands-System:** 4 px Grid (4, 8, 12, 16, 24, 32, 48)
- **Schatten:** Konsistente Box-Shadow-Stufen (sm/md/lg) für Cards und Modals
- **Globale Resource-Dictionary** für alle Farben, Brushes, Styles
- **Light-/Dark-Mode:** Umschaltbar in den Settings, System-Default erkennen
- Alle bestehenden Views auf das neue Design-System migrieren

**Neue UI-Komponenten (als wiederverwendbare `UserControl`s):**

| Komponente | Beschreibung |
|------------|-------------|
| `StatCard` | KPI-Karte mit Titel, Wert, optionalem Icon und Trendpfeil |
| `StatusBadge` | Farbige Status-Chips (ok, warning, error, info) |
| `LoadingOverlay` | Halbtransparentes Lade-Overlay über Content |
| `EmptyStateView` | Leerer Zustand mit Icon, Titel und optionalem Aktionsbutton |
| `SearchFilterBar` | Einheitliche Suchleiste + Filter-Buttons |
| `DetailSidePanel` | Slide-In Panel von rechts für Detailansichten |
| `ConfirmDialog` | Modaler Bestätigungsdialog |
| `NotificationBadge` | Zähler-Badge für Sidebar-Einträge |
| `InlineFormPanel` | Zusammenklappbares Formular-Panel (ersetzt Inline-Borders) |
| `PaginationControl` | Seitennavigation für Listen |
| `RefreshIndicator` | Subtiler Refresh-Spinner in der Ecke (Hintergrundaktualisierung) |

---

### 2.2 Sidebar-Modernisierung

**Änderungen am `MainWindow`:**
- Sidebar-Einträge mit Icons (Fluent Icons oder Material Icons)
- `NotificationBadge` neben „Benachrichtigungen" mit Unread-Zähler
- Aktiver Eintrag mit Akzentfarbe hervorheben (nicht nur Background)
- User-Avatar-Platzhalter mit Initialen oben in der Sidebar
- Projekt-Selektor als Dropdown direkt in der Sidebar (Schnellwechsel)
- Trennlinien zwischen Gruppen (Hauptmodule / Admin / Settings)
- Kollapsierbare Sidebar animiert (Slide-Animation statt abruptem Toggle)

---

### 2.3 Dashboard-Modernisierung (`DashboardView`)

**Änderungen:**
- Vollständige Dashboard-Daten (`dashboard_data`) nutzen – alle Bereiche (MM, NEA, Building, Keys, Notifications)
- `StatCard`-Grid: MM-Statistiken, NEA-Statistiken, Gebäude-Status, Schlüssel-Status, Benachrichtigungen
- Hintergrundaktualisierung alle 60 s (via `BackgroundRefreshService`)
- „Letzte Aktivitäten" Feed: kombinierte Chronologie aller aktuellen Ereignisse
- Quick-Action-Buttons: „Neue Mängelmeldung", „NEA-Prüfung starten"
- Überfällige NEA-Prüfungen als Warn-Cards oben

---

### 2.4 Listen-Views modernisieren (alle Module)

**Einheitliches List-Pattern für alle Module:**
- `SearchFilterBar` oben (Suche + Status-Filter + Datumsfilter)
- `DataGrid` mit Zebra-Striping, hover-Highlighting, selektierbaren Zeilen
- Sortierung per Spaltenklick
- Paginierung via `PaginationControl`
- Rechts: `DetailSidePanel` öffnet sich beim Klick auf eine Zeile
- Hintergrundaktualisierung mit subtilen `RefreshIndicator`

---

## Phase 3 – Feature-Parität zur PHP/Smarty-HTML-UI

### 3.1 Dashboard – Vollständige Abbildung

- [x] NEA-Statistiken (vorhanden)
- [ ] `dashboard_data` vollständig auswerten (MM, Building, Keys, Notifications)
- [ ] Projekt-Schnellwechsel direkt im Dashboard
- [ ] Letzte-Aktivitäten-Feed
- [ ] Quick-Action-Shortcuts

---

### 3.2 NEA – Netzersatzanlagen

**Bereits vorhanden:**
- Anlage erstellen/bearbeiten/löschen
- Prüfung erstellen/bearbeiten/abschließen
- Prüfungsliste mit Filter

**Fehlend / zu verbessern:**
- [ ] Checklist-Editor (`nea_checklist_update`): interaktive Checklisten-Einträge pro Prüfung abhaken
- [ ] Foto-Galerie pro Prüfung (Photos aus `nea_inspection_detail.photos`)
- [ ] Defect-Notes-Anzeige (strukturiert, nicht nur als Text)
- [ ] Status-Badge statt reiner Text-Darstellung
- [ ] Export-Funktion (PDF-Bericht der Prüfung – via Browser-Print oder lokale PDF-Bibliothek)
- [ ] NEA-Detail-Seite als eigener View (nicht nur Inline-Panel)

---

### 3.3 Mängelmeldungen (MM)

**Bereits vorhanden:**
- Liste + Detail + CRUD + Status-Änderung + Contractor-Zuweisung

**Fehlend / zu verbessern:**
- [ ] Dringlichkeits-Farbkodierung in der Liste (normal = grün, dringend = orange, notfall = rot)
- [ ] `instructions`-Liste in der Detailansicht anzeigen
- [ ] MM-Status-Workflow-Visualisierung (Fortschrittsbalken: Offen → In Bearbeitung → Erledigt)
- [ ] Schnellfilter-Chips für Status oben in der Liste
- [ ] Drucken/Export-Funktion für Mängelmeldungs-PDF

---

### 3.4 Gebäudebegehungen (Building)

**Bereits vorhanden:**
- Gebäude + Begehungen CRUD, Status, Abschluss

**Fehlend / zu verbessern:**
- [ ] **Checkpoint-Editor:** interaktive Checkbox-Liste aller Prüfpunkte einer Begehung (`building_checkpoint_update`)
- [ ] Prüfpunkte nach Kategorie gruppiert anzeigen
- [ ] Checkpoint-Status-Badge (ok/nok/n/a)
- [ ] Begehungs-Übersicht mit Fortschrittsbalken (X von Y Checkpoints erledigt)
- [ ] `building_checkpoints_list` laden und anzeigen
- [ ] Allgemeine Notizen + Witterungsbedingungen + Teilnehmer in Detailansicht prominent

---

### 3.5 Klimaanlage / HVAC (Klima)

**Bereits vorhanden:**
- Gerätestatus, Gruppen, Steuerung

**Fehlend / zu verbessern:**
- [ ] Gruppen-Steuerung als prominente UI (Gruppe EIN/AUS mit einem Klick)
- [ ] Geräteliste gruppiert nach Gruppe anzeigen
- [ ] Betriebsmodus-Anzeige mit Icons (Heizen, Kühlen, Lüften, Auto)
- [ ] Hintergrundaktualisierung Klima-Status alle 30 s (höchste Refresh-Prio)
- [ ] Gerätekonfiguration bearbeiten (`klima_device_update`)
- [ ] „Alle EIN" / „Alle AUS" Schnellaktionen
- [ ] Historischer Verlauf (falls API vorhanden)

---

### 3.6 Schlüsselverwaltung (Keys)

**Bereits vorhanden:**
- Inventar + Ausgaben CRUD, Ausgabe + Rückgabe

**Fehlend / zu verbessern:**
- [ ] Visueller Status je Schlüssel: verfügbar/ausgegeben (farbkodiert)
- [ ] Schlüssel-Ausgabe-Workflow: Step-by-Step (Schlüssel wählen → Empfänger eingeben → Bestätigen)
- [ ] Rückgabe-Schnellaktion direkt in der Ausgaben-Liste
- [ ] Filter nach Schlüsseltyp + Schrank
- [ ] Ausgabehistorie pro Schlüssel in der Detailansicht
- [ ] Mahnung/Warnung bei überfälligen Schlüsselrückgaben

---

### 3.7 WLS – Wohnungsleerstandserfassung *(neu)*

Vollständig neues Modul (kein C#-View vorhanden), bildet die PHP/Smarty WLS-Templates ab.

**Zu implementieren:**
- [ ] `WlsViewModel` + `WlsView` (Hauptansicht mit 3 Tabs: Gebäude / Wohnungen / Erfassungen)
- [ ] **Tab Gebäude:** Liste aller WLS-Gebäude (`GET /buildings/list`), CRUD, aktivieren/deaktivieren
- [ ] **Tab Wohnungen:** Wohnungen pro Gebäude (`GET /apartments/list/{building_id}`), CRUD, Leerstandstatus
- [ ] **Tab Erfassungen:** Datensätze (`POST /records/list`) mit Filter (Datum, Gebäude, Wohnung, Benutzer)
- [ ] Neue Erfassung anlegen (`POST /records/create`) mit Datum/Uhrzeit-Picker
- [ ] Geografische Koordinaten (Latitude/Longitude) in Erfassungsformular (optional: Karte)
- [ ] Auswertungsansicht: Leerstandsquote pro Gebäude
- [ ] Dauer-Berechnung aus `start_time` / `end_time`
- [ ] Sidebar-Eintrag: „WLS" (nur bei Berechtigung `wls_view`)

---

### 3.8 Benachrichtigungen *(neu)*

Vollständig neues Modul.

**Zu implementieren:**
- [ ] `NotificationsViewModel` + `NotificationsView`
- [ ] Benachrichtigungsliste (`GET /api.php?action=notifications`)
- [ ] Ungelesene-Zähler in Sidebar-Badge
- [ ] Benachrichtigung als gelesen markieren
- [ ] Desktop-Toast-Notification bei neuer Benachrichtigung (Hintergrundpolling)
- [ ] Notifications-Filterung (gelesen / ungelesen)
- [ ] Sidebar-Eintrag mit Badge

---

### 3.9 Meter / Zählererfassung *(optional, Session-abhängig)*

> ⚠️ Die Meter-API ist **Session-basiert** (kein User-Token). Umsetzbarkeit im Desktop-Client prüfen.

**Möglicher Ansatz:**
- [ ] Prüfen ob `meter_*` Actions über User-Token erreichbar gemacht werden können (Backend-Änderung)
- [ ] Falls ja: `MeterViewModel` + `MeterView` mit:
  - Zähler-Topologie (Gebäude → Wohnung → Zähler)
  - Zählerstand-Erfassung
  - Ablese-Verlauf
- [ ] Falls nein: Eingebetteter Webview als Fallback

---

### 3.10 Einstellungen – Erweiterungen

**Bereits vorhanden:**
- Server-URL, Token-Verwaltung, Projekte-CRUD, User-Admin (CRUD), Update

**Fehlend / zu verbessern:**
- [ ] Admin-Benutzerverwaltung als eigener Tab (aus Settings auslagern → `AdminView`)
- [ ] Passwort-Änderung für den aktuellen Benutzer (`POST /user/changepw` oder `auth_changepw`)
- [ ] Benutzer-Profil-Seite (Name, E-Mail, eigene Berechtigungen anzeigen)
- [ ] Theme-Umschaltung (Light/Dark/System) in den Settings
- [ ] Cache leeren Button
- [ ] Log-Datei anzeigen / öffnen
- [ ] Verbindungstest (Health-Check: `GET /api.php/health`)

---

## Phase 4 – Desktop-Optimierungen

### 4.1 Offline-Modus

- [ ] Beim Start: gecachte Daten sofort anzeigen, API-Daten im Hintergrund nachladen
- [ ] Klare "Offline"-Anzeige in der Statusleiste, wenn API nicht erreichbar
- [ ] Schreiboperationen queuen wenn offline → bei Verbindung automatisch abspielen (optional)
- [ ] Automatische Wiederverbindung im Hintergrund (exponentielles Backoff)

---

### 4.2 Performance

- [ ] Virtualisierung für alle `DataGrid`-Instanzen (bereits durch Avalonia gegeben, sicherstellen)
- [ ] Lazy Loading von Detailansichten (erst laden wenn sichtbar)
- [ ] API-Aufrufe mit `CancellationToken` abbrechen bei View-Wechsel
- [ ] Parallele Ladevorgänge (`Task.WhenAll`) für unabhängige Daten (bereits teilweise vorhanden)
- [ ] Splash-Screen beim Start mit Auto-Login-Fortschritt

---

### 4.3 Keyboard-Navigation & Shortcuts

- [ ] Globale Keyboard-Shortcuts: `Ctrl+1`…`Ctrl+7` für Navigation zwischen Modulen
- [ ] `Ctrl+R` für manuelles Refresh des aktuellen Moduls
- [ ] `Escape` schließt offene Formulare/Dialoge
- [ ] `Enter` bestätigt Formulare
- [ ] Vollständige Tab-Navigation in allen Formularen

---

### 4.4 Systemtray-Integration (Windows/macOS/Linux)

- [ ] App läuft im Hintergrund (optional) im Systemtray
- [ ] Tray-Icon mit Unread-Benachrichtigungs-Badge
- [ ] Kontext-Menü: „Öffnen", „Benachrichtigungen", „Beenden"
- [ ] Desktop-Toast-Notifications bei neuen MM / Benachrichtigungen

---

### 4.5 Datenexport

- [ ] CSV-Export für alle Listen (MM, NEA, Schlüssel, WLS)
- [ ] Drucken-Funktion für Detail-Ansichten (via System-Druckdialog)
- [ ] Optional: PDF-Generierung für Prüfberichte

---

## Phase 5 – Qualität & Abschluss

### 5.1 Test-Coverage

- [ ] Unit-Tests für `DataCacheService` (TTL, Invalidierung, Parallelität)
- [ ] Unit-Tests für `BackgroundRefreshService` (Timer, Pause bei Logout)
- [ ] Unit-Tests für `NavigationService` (Back-Stack)
- [ ] Unit-Tests für alle ViewModels (Mocks via `IDkcApi`)
- [ ] Integration-Tests für API-Mapping (Refit + WireMock)

---

### 5.2 Dokumentation

- [ ] `agent.md` aktuell halten (nach jeder Phase)
- [ ] Inline-Kommentare für komplexe Service-Logik
- [ ] `README.md` mit Setup-Anleitung, Build-Befehlen, Screenshot
- [ ] Changelog (`CHANGELOG.md`) für Releases

---

### 5.3 CI/CD & Release

- [ ] GitHub Actions Build-Pipeline bereits vorhanden (`build.yml`) → erweitern
- [ ] Automatische Release-Erstellung bei Git-Tag
- [ ] Plattform-Builds: Windows (`.exe`, Self-Contained), Linux (`.AppImage` oder Binary), macOS (`.dmg`)
- [ ] Code-Signing für Windows (optional)

---

## Priorisierungsmatrix

| # | Feature | Nutzen | Aufwand | Priorität |
|---|---------|--------|---------|-----------|
| 1 | `DataCacheService` + `BackgroundRefreshService` | ⭐⭐⭐⭐⭐ | M | 🔴 Hoch |
| 2 | Dashboard vollständig (`dashboard_data`) | ⭐⭐⭐⭐⭐ | S | 🔴 Hoch |
| 3 | Design-System + `StatCard` + `StatusBadge` | ⭐⭐⭐⭐⭐ | L | 🔴 Hoch |
| 4 | Klima Hintergrundaktualisierung (30 s) | ⭐⭐⭐⭐⭐ | S | 🔴 Hoch |
| 5 | WLS-Modul (Buildings/Apartments/Records) | ⭐⭐⭐⭐ | L | 🟠 Mittel |
| 6 | Benachrichtigungen-Modul + Sidebar-Badge | ⭐⭐⭐⭐ | M | 🟠 Mittel |
| 7 | NEA Checklist-Editor | ⭐⭐⭐⭐ | M | 🟠 Mittel |
| 8 | Building Checkpoint-Editor | ⭐⭐⭐⭐ | M | 🟠 Mittel |
| 9 | `NavigationService` + Breadcrumb | ⭐⭐⭐ | M | 🟠 Mittel |
| 10 | `DialogService` + `DetailSidePanel` | ⭐⭐⭐ | M | 🟠 Mittel |
| 11 | Schlüssel-Ausgabe-Workflow | ⭐⭐⭐ | S | 🟡 Niedrig |
| 12 | Light/Dark-Mode + ThemeService | ⭐⭐⭐ | M | 🟡 Niedrig |
| 13 | Systemtray-Integration | ⭐⭐ | M | 🟡 Niedrig |
| 14 | CSV/PDF-Export | ⭐⭐ | M | 🟡 Niedrig |
| 15 | Meter-Modul | ⭐⭐ | L | 🟡 Niedrig (Backend-Abhängigkeit) |

**Legende:** S = Klein (1–2 Tage), M = Mittel (3–5 Tage), L = Groß (1–2 Wochen)

---

## Nächste Schritte (Start-Sequenz)

1. **`DataCacheService`** in `DkcDesktopClient.Core/Services/` implementieren + Unit-Tests
2. **`BackgroundRefreshService`** in `DkcDesktopClient.Core/Services/` implementieren + DI registrieren
3. **Design-System** in `DkcDesktopClient.App/Styles/` anlegen (ResourceDictionary, Farben, Controls)
4. **`DashboardView`** auf `dashboard_data` migrieren + neue `StatCard`-Komponenten
5. **`KlimaView`** mit 30-s-Hintergrundaktualisierung
6. **`WlsView` + `WlsViewModel`** als neues Modul implementieren
7. **`NotificationsView` + `NotificationPollingService`** + Sidebar-Badge
8. **NEA Checklist-Editor** + **Building Checkpoint-Editor**
9. **`NavigationService`** + **`DialogService`** einführen, alle Views migrieren
10. **Theme-System** (Light/Dark), **Keyboard-Shortcuts**, **Offline-Modus**
