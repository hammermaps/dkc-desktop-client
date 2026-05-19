# DKC Desktop Client

Eine native Desktop-Anwendung für das **DKC Facility-Management-System**, implementiert mit [Avalonia UI](https://avaloniaui.net/), C# und .NET 8. Sie bildet die bestehende PHP/Smarty-HTML-UI vollständig als plattformübergreifenden nativen Client ab.

---

## Features

| Modul | Beschreibung |
|---|---|
| **Dashboard** | Überblick über MM-, NEA-, Schlüssel-KPIs; Projektwechsel; überfällige Prüfungen |
| **Mängelmeldungen (MM)** | Liste, Detail, CRUD, Status-Workflow, Dringlichkeits-Farbkodierung, CSV-Export |
| **NEA – Netzersatzanlagen** | Anlage/Prüfung CRUD, interaktiver Checklist-Editor |
| **Gebäudebegehungen** | Gebäude/Begehung CRUD, interaktiver Checkpoint-Editor |
| **Klimaanlage (HVAC)** | Gerätestatus, Gruppen, EIN/AUS-Steuerung, 30-s-Hintergrundaktualisierung |
| **Schlüsselverwaltung** | Inventar, Ausgabe/Rückgabe-Workflow |
| **WLS – Wohnungsleerstand** | Gebäude, Wohnungen, Erfassungen (Datum, Koordinaten, Dauer) |
| **Benachrichtigungen** | Polling-Service, Ungelesen-Badge in der Sidebar |
| **Einstellungen** | Server-URL, Token-Verwaltung, Projekt-CRUD, Benutzer-Admin, Update-Check |

### Architektur-Highlights

- **DataCacheService** – TTL-basierter In-Memory-Cache mit Thread-Sicherheit (SemaphoreSlim pro Key)
- **BackgroundRefreshService** – Hintergrundaktualisierung je Datentyp, pausiert bei Logout
- **NotificationPollingService** – Polling alle 60 s als SSE-Ersatz mit lokalem Unread-Count
- **NavigationService** – Back-Stack, Breadcrumb-Anzeige, INavigationTarget-Interface
- Plattformübergreifend: **Windows**, **Linux**, **macOS**

---

## Voraussetzungen

| Komponente | Version |
|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 8.0 oder neuer |
| Betriebssystem | Windows 10+, Ubuntu 20.04+, macOS 12+ |
| DKC-Backend | Laufende Instanz mit API-Endpunkt |

---

## Setup & Build

### 1. Repository klonen

```bash
git clone https://github.com/hammermaps/dkc-desktop-client.git
cd dkc-desktop-client
```

### 2. Abhängigkeiten wiederherstellen

```bash
dotnet restore DkcDesktopClient.slnx
```

### 3. Anwendung bauen

```bash
dotnet build DkcDesktopClient.slnx --configuration Release
```

### 4. Anwendung starten

```bash
dotnet run --project DkcDesktopClient.App/DkcDesktopClient.App.csproj
```

---

## Tests ausführen

```bash
dotnet test DkcDesktopClient.Tests/DkcDesktopClient.Tests.csproj --configuration Release
```

Aktuell **115 Tests** für:
- `DataCacheService` (TTL, Invalidierung, Parallelität)
- `BackgroundRefreshService` (Pause bei Logout, DataRefreshed-Event)
- `NavigationService` (Back-Stack, Breadcrumbs, Events)
- ViewModel-Tests (Zustand, Derived Properties, Commands)
- DTO-Tests (MmMessage Berechnungen)
- `AuthService`, `TokenStore`, `ConnectivityService`, `UpdateService`, `CsvExportService`

---

## Self-Contained Publish

### Windows (x64)

```bash
dotnet publish DkcDesktopClient.App/DkcDesktopClient.App.csproj \
  --configuration Release --runtime win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  --output ./publish/win-x64
```

### Linux (x64)

```bash
dotnet publish DkcDesktopClient.App/DkcDesktopClient.App.csproj \
  --configuration Release --runtime linux-x64 \
  --self-contained true -p:PublishSingleFile=true \
  --output ./publish/linux-x64
```

### macOS (x64 / Apple Silicon)

```bash
# Intel Mac
dotnet publish DkcDesktopClient.App/DkcDesktopClient.App.csproj \
  --configuration Release --runtime osx-x64 \
  --self-contained true -p:PublishSingleFile=true \
  --output ./publish/osx-x64

# Apple Silicon (M1/M2/M3)
dotnet publish DkcDesktopClient.App/DkcDesktopClient.App.csproj \
  --configuration Release --runtime osx-arm64 \
  --self-contained true -p:PublishSingleFile=true \
  --output ./publish/osx-arm64
```

---

## CI/CD

GitHub Actions baut und testet automatisch bei jedem Push auf `main`/`develop` und bei Pull Requests.  
Bei einem Git-Tag (`v*`) wird automatisch ein **GitHub Release** mit Binaries für Windows, Linux und macOS erstellt.

Workflow: [`.github/workflows/build.yml`](.github/workflows/build.yml)

---

## Konfiguration

Beim ersten Start wird die Server-URL und der API-Token über den Login-Dialog eingegeben.  
Gespeicherte Zugangsdaten (Token, Server-URL) werden mit **DPAPI / Data Protection API** verschlüsselt im lokalen `AppData`-Verzeichnis abgelegt.

Log-Datei: `%LOCALAPPDATA%\DkcDesktopClient\debug.log` (Windows) bzw. `~/.local/share/DkcDesktopClient/debug.log` (Linux/macOS)

---

## Projektstruktur

```
DkcDesktopClient.App/        # Avalonia UI – Views, ViewModels, Services (App-Schicht)
DkcDesktopClient.Core/       # Business-Logik – API-Client (Refit), Services, DTOs
DkcDesktopClient.Tests/      # xUnit-Tests (115 Tests)
.github/workflows/build.yml  # CI/CD-Pipeline
ROADMAP.md                   # Entwicklungs-Roadmap
CHANGELOG.md                 # Versionshistorie
```

---

## Lizenz

Dieses Projekt steht unter der [MIT License](LICENSE).
