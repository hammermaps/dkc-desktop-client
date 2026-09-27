# Changelog

Alle bemerkenswerten Änderungen an diesem Projekt werden in dieser Datei dokumentiert.  
Format basiert auf [Keep a Changelog](https://keepachangelog.com/de/1.1.0/) und dieses Projekt hält sich an [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Neu
- Automatischer Updater erkennt jetzt zusätzlich zum bestehenden GitHub-Tag-Kanal den
  kontinuierlichen DKC-Build-Kanal (jeder Push auf `main`, nur Linux/Windows — macOS bleibt
  ausschließlich beim GitHub-Pfad). `UpdateService.CheckForUpdateAsync()` prüft beide Quellen
  und übernimmt die jeweils neuere Version; der Download läuft für den DKC-Kanal über den neu
  authentifizierten Server-Endpunkt `desktop_app_download_binary` (`Authorization: Bearer
  <persönlicher Token>`), silent Self-Update wie beim bisherigen GitHub-Pfad.

### CI/CD
- Kontinuierlicher Auto-Build für Linux/Windows: `.github/workflows/build.yml` lädt bei
  jedem Push auf `main` die frisch gebauten `linux-x64`/`win-x64`-Binaries zusätzlich zum
  bestehenden Tag-basierten GitHub-Release-Pfad direkt zur DKC-Website hoch (analog zum
  TWS-App-Android-Auto-Deploy). Download für Staff: `?page=admin&action=desktop_app` im DKC.
  Benötigt das Repository-Secret `DESKTOP_APP_DEPLOY_TOKEN` (siehe Server-CHANGES.textile).
- Fix: der „Set version“-Schritt setzte für Nicht-Tag-Pushes hart `-p:Version=1.0.0`, während
  der Deploy-Schritt dem Server eine fortlaufende Version (`YYYY.MM.DD.<run_number>`) meldete —
  jede frisch aktualisierte Binärdatei hätte sich dadurch selbst immer als veraltet ausgegeben.
  Beide Stellen berechnen die Version jetzt einmal pro Workflow-Lauf gemeinsam.

### Fix
- **Kritisch:** Schreibende Aktionen für Gebäudebegehungen (anlegen/bearbeiten/abschließen/
  Prüfpunkte), Schlüsselverwaltung (anlegen/bearbeiten/löschen/ausgeben/zurückgeben), Klima-
  Steuerung (Einzelgerät/Gruppe/Alle-Ein/Alle-Aus/Letzten-Zustand-wiederherstellen), NEA
  (Systeme anlegen/bearbeiten/löschen, Prüfungen anlegen/bearbeiten/abschließen, Checkliste)
  und Projektverwaltung (anlegen/bearbeiten) waren in `api.php` nie als REST-Route
  registriert — jeder Speichern-/Löschen-/Steuern-Klick schlug mit 404 fehl. Nur die
  bereits vollständig implementierten Protobuf-Actions funktionierten. Alle betroffenen
  ViewModels nutzen jetzt durchgängig Protobuf für diese Operationen (Lesezugriffe bleiben
  dort REST, wo sie bereits funktionieren). Derselbe Root Cause wie beim vorherigen
  Mängelmeldungs-Fix.
- Schlüsselverwaltung: `KeysSaveHandler` verlangt zusätzlich `number` und `type_id`, die das
  bisherige Formular gar nicht erhob — Formular um Pflichtfelder "Nummer" und "Typ" (Dropdown,
  neu: `GetKeysTypesListAsync`/`GetKeysCabinetsListAsync`) sowie optional "Schrank" ergänzt;
  die Inventarliste lädt jetzt ebenfalls über Protobuf, da die REST-Liste diese Felder nicht
  liefert. Der frühere "Löschen"-Button auf dem Ausgabe-Tab rief `keys_delete` (löscht einen
  Inventartyp) fälschlich mit der ID eines Ausgabe-Datensatzes auf — kein entsprechendes
  Web-Feature existiert; Button entfernt und durch einen korrekten "Löschen"-Button für
  Inventartypen (mit Bestätigungsdialog, Web-Pendant: `KeysInventoryTrait::deleteInventory()`)
  auf dem Inventar-Tab ersetzt.

- Admin-Benutzerverwaltung (Benutzer anlegen/bearbeiten/löschen, `SettingsViewModel`): die
  Protobuf-Actions `USER_CREATE`/`USER_UPDATE`/`USER_DELETE` waren serverseitig nur als Stub
  registriert (503 „not yet implemented“), obwohl der Vertrag bereits vollständig war und der
  Client bereits fertige Aufrufe dafür hatte. Server-seitig jetzt implementiert (neue
  `UserSaveHandler`/`UserDeleteHandler`, Admin-Grundfall: Benutzername/Passwort/Name/E-Mail/
  Admin-Flag) und `SettingsViewModel` auf Protobuf umgestellt. Bewusst nicht Teil dieses
  minimalen Vertrags (wie im Web-Formular vorhanden, aber hier ausgeklammert): Manager-Modus,
  Mieter-Accounts, Projekt-/OIDC-/Geräte-Zuweisung.

### Bekannt, nicht behoben
- Benutzerliste (`USERS_LIST`) läuft weiterhin über die bereits funktionierende REST-Action
  `users_list`; die Protobuf-Action bleibt bewusst Stub (kein aktueller Bedarf).

### Hinzugefügt
- Phase-5-Abschluss: vollständige Test-Coverage, Dokumentation, CI/CD-Erweiterung
- Alle Listen-Ansichten (Mängelmeldungen, NEA-Systeme/-Prüfungen, Gebäude, Klimageräte,
  Schlüssel-Inventar, WLS-Gebäude, Benachrichtigungen) laden jetzt automatisch beim
  Öffnen (`INavigationTarget.OnNavigatedToAsync`) statt nur per manuellem "Laden"-Klick,
  und ziehen Änderungen periodisch im Hintergrund über den bestehenden
  `BackgroundRefreshService` nach (neue Cache-Keys `NeaSystems`, `BuildingList`,
  `KlimaDevices`, `Notifications`, `WlsBuildings`), ohne die aktuelle Auswahl oder
  sichtbare Fehlermeldungen zu stören.
- Dashboard an das aktuelle Web-Dashboard angeglichen: neue Karten für Sammelstörung
  (Klima/SiBe/KinCony/RCO), ausgegebene Schlüssel, offene Freigaben, überfällige
  Mängelmeldungen und WLS-Spülfälligkeiten, geladen über die erweiterte Protobuf-Aktion
  `DASHBOARD_WIDGETS` (`GetDashboardWidgetsAsync`, erstmals genutzter Protobuf-Client).
  Durchschnittstemperatur inkl. 14-Tage-Verlauf als Balkendiagramm über die neu
  eingebundene Bibliothek `LiveChartsCore.SkiaSharpView.Avalonia`.
- Mängelmeldungen: Volltextsuche, Dringlichkeits-/Jahr-Filter, Sortierung (Neueste/
  Älteste/Betreff/Dringlichkeit/Status) sowie serverseitig aufgelöste Objekt-/
  Nachunternehmer-Namen und ein Folgemeldungs-Zähler in Liste und Detail; echte
  Seiten-Pagination (Vorherige/Nächste Seite) statt fester Begrenzung auf die ersten
  50 Einträge. Kommentarfeld beim Statuswechsel und ein Lösch-Bestätigungsdialog
  (`IDialogService`, bislang ungenutzt) ergänzt.
- **Bugfix:** Die Dringlichkeits-Badges/-Filter verwendeten die erfundenen Werte
  "dringend"/"notfall" statt der tatsächlichen DB-Enum-Werte
  (`niedrig|normal|hoch|kritisch`) — Badges zeigten dadurch fast immer "Normal" an.
  Auf die echten Werte korrigiert (Web-Pendant: `mm_list.tpl` `getPriorityBadge()`).
- Freigabe-/Prüfungs-Workflow im Status-Panel der Mängelmeldungen: EK-Preis,
  Planon-Arbeitsauftragsgruppe, Freigabe-Akt-Nr. und Pflicht-Folgemaßnahmen-Text
  (sichtbar bei Status "Freigabe"/"Freigegeben"), 12 Zusatzanweisungs-Checkboxen
  (Labels jetzt vom Server geladen statt hartkodiert) sowie Auftragsnummer
  (sichtbar bei Status "Erledigt", serverseitig formatgeprüft). **Bewusst nicht
  enthalten:** E-Mail-Benachrichtigungen an Verwaltung/Nachunternehmer/Mieter und
  der Arbeitsnachweis-PDF-Upload nach Nextcloud — ein Statuswechsel über den
  Desktop-Client aktualisiert die Datenbank korrekt, löst aber keine E-Mails aus
  (siehe `docs/reports/MM_LISTE_DESKTOP_CLIENT_LUECKENANALYSE.md` im Hauptrepo).

### Geändert
- Mängelmeldungen (Liste, Detail, Anlegen, Bearbeiten, Statuswechsel, NU-Zuweisung,
  Löschen) laufen jetzt vollständig über Protobuf statt REST. Grund: die REST-
  Schreib-Actions (`mm_create`, `mm_update`, `mm_update_status`,
  `mm_assign_contractor`, `mm_delete`) waren in `api.php` nie registriert — diese
  Funktionen liefen bislang real ins Leere (404). Die Protobuf-Actions
  (`system\protobuf\Actions\ActionRegistry`) waren durchgehend vollständig
  implementiert und sind jetzt der einzige Pfad für MM-Operationen.

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
