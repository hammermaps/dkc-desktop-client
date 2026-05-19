# Changelog

Alle bemerkenswerten Änderungen an diesem Projekt werden in dieser Datei dokumentiert.  
Format basiert auf [Keep a Changelog](https://keepachangelog.com/de/1.1.0/) und dieses Projekt hält sich an [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Hinzugefügt
- Phase-5-Abschluss: vollständige Test-Coverage, Dokumentation, CI/CD-Erweiterung

---

## [1.0.0] – Phase 5 – Qualität & Abschluss

### Hinzugefügt
- **Test-Coverage** (Phase 5.1)
  - `BackgroundRefreshServiceTests`: Pause bei Logout, DataRefreshed-Event, NotifyUserActivity-Deferral (5 Tests)
  - `DtoTests`: MmMessage computed properties – StatusText, DringlichkeitText, StatusColorHex, DringlichkeitColorHex (13 Tests)
  - `ViewModelTests`: alle 10 ViewModels (Dashboard, Notifications, Mm, Building, Nea, Klima, Keys, Login, Settings, Wls) – state, derived properties, command guards, events (57 Tests)
  - Gesamt: **151 Tests** (zuvor 68)
- **Dokumentation** (Phase 5.2)
  - `README.md`: Setup-Anleitung, Build-Befehle, Feature-Übersicht, Projektstruktur
  - `CHANGELOG.md`: initiale Versionshistorie
- **CI/CD** (Phase 5.3)
  - macOS-Build (osx-x64 und osx-arm64) in GitHub Actions
  - Automatische GitHub-Release-Erstellung bei Git-Tag (bereits vorhanden)

### Geändert
- `DkcApiFactory.Create()` ist jetzt `virtual` → ermöglicht sauberes Mocking in Tests
- `BackgroundRefreshService.TickInterval` ist jetzt ein `protected virtual` Property → übersteuerbar in Tests für kurze Wartezeiten

---

## [0.9.0] – Phase 4 – Desktop-Optimierungen

### Hinzugefügt
- `ConnectivityService` mit automatischer Wiederverbindung (exponentielles Backoff)
- Offline-Anzeige in der Statusleiste (`IsOnline`-Property)
- `UpdateService` mit GitHub-Release-Check und Versions-Vergleich
- `CsvExportService` für MM-, NEA- und Schlüssel-Listen
- Keyboard-Shortcuts: `Ctrl+R` für Refresh, `Escape` schließt Formulare
- `NavigationService` mit Back-Stack und Breadcrumb-Anzeige

---

## [0.8.0] – Phase 3 – Feature-Parität zur HTML-UI

### Hinzugefügt
- **WLS-Modul**: `WlsViewModel` + `WlsView` mit Tabs für Gebäude, Wohnungen, Erfassungen
- **Benachrichtigungen-Modul**: `NotificationsViewModel` + `NotificationsView`, Sidebar-Badge
- **NEA Checklist-Editor**: interaktive Checklisten-Einträge per `nea_checklist_update`
- **Building Checkpoint-Editor**: interaktive Prüfpunkte per `building_checkpoint_update`
- MM: Dringlichkeits- und Status-Farbkodierung, Schnellfilter-Chips
- Schlüssel: Rückgabe-Formular direkt in der Ausgaben-Liste
- Dashboard: Projekt-Schnellwechsel, Quick-Action-Buttons, überfällige NEA-Prüfungen

---

## [0.7.0] – Phase 2 – UI/UX-Modernisierung

### Hinzugefügt
- Design-System: dunkle Sidebar, heller Content-Bereich, Akzentfarbe Blau, Inter Font
- Wiederverwendbare Controls: `StatCard`, `StatusBadge`, `LoadingOverlay`, `SearchFilterBar`
- Sidebar: Icons, NotificationBadge, aktive Hervorhebung, User-Avatar-Platzhalter
- DataGrid: Zebra-Striping, hover-Highlighting, selektierbare Zeilen, Sortierung
- `PaginationControl` für alle Listen-Views

---

## [0.6.0] – Phase 1 – Infrastruktur & Fundament

### Hinzugefügt
- `DataCacheService`: TTL-basierter In-Memory-Cache, Thread-sicher (SemaphoreSlim)
- `BackgroundRefreshService`: periodische Datenaktualisierung, Pause bei Logout
- `NotificationPollingService`: 60-s-Polling, Unread-Count, lokales Markieren als gelesen
- `NavigationService`: Back-Stack, Breadcrumbs, INavigationTarget-Interface
- `DialogService`: ConfirmAsync, AlertAsync, ShowDetailPanelAsync, ShowFormDialogAsync
- Refit-API-Client mit vollständiger IDkcApi-Definition
- Token-basierte Authentifizierung (dkc_…-Token) mit DPAPI-Verschlüsselung
- GitHub Actions Build-Pipeline (Windows + Linux, Self-Contained Single-File)

[Unreleased]: https://github.com/hammermaps/dkc-desktop-client/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/hammermaps/dkc-desktop-client/compare/v0.9.0...v1.0.0
[0.9.0]: https://github.com/hammermaps/dkc-desktop-client/compare/v0.8.0...v0.9.0
[0.8.0]: https://github.com/hammermaps/dkc-desktop-client/compare/v0.7.0...v0.8.0
[0.7.0]: https://github.com/hammermaps/dkc-desktop-client/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/hammermaps/dkc-desktop-client/releases/tag/v0.6.0
