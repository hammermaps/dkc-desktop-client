# Implementierungsplan: Web-Template → C#/Avalonia Desktop Client

Analyse aller Elemente aus `/ProxyServer/template/default/*` und Abbildung auf das C#-Projekt.

---

## Schritt 1: MM Status-Mapping (✅ Erledigt)

**Problemstellung**: API liefert `status` als Integer (-2 bis 3).

| Wert | Bedeutung                  | Badge-Farbe  |
|------|----------------------------|--------------|
| -2   | Zur Prüfung                | Grau         |
| -1   | Abgelehnt                  | Rot          |
|  0   | Freigabe erforderlich      | Orange       |
|  1   | Freigegeben                | Blau         |
|  2   | Nachunternehmer beauftragt | Lila         |
|  3   | Erledigt                   | Grün         |

**Geänderte Dateien**:
- `DkcDesktopClient.Core/Api/DTOs.cs`: `MmMessage.Street`, `MmMessage.Nachunternehmer`, `MmDetail.Street`, `MmDetail.Nachunternehmer` → `int?` (API liefert Integer)
- `DkcDesktopClient.App/ViewModels/MmViewModel.cs`: `StatusFilterOptions`, `StatusToLabel()`, `.ToString()`-Konvertierungen

---

## Schritt 2: MM Filter-ComboBoxen (✅ Erledigt)

**Problemstellung**: `mm.tpl` hat Status-Dropdown und Straßen-Filter.

**Geänderte Dateien**:
- `MmView.axaml`: `FilterStatus` → `ComboBox` mit `StatusFilterOptions` (Value/Label-Binding), `FilterStreet` → `TextBox`
- `MmViewModel.cs`: `FilterStatus` als `string?`, `StatusFilterOptions` als statische Liste

---

## Schritt 3: MM Detail-/Formularfelder (✅ Erledigt)

**Problemstellung**: `mm.tpl` hat Dringlichkeit `["normal","hoch","kritisch"]` und Zugeh `haus/gw/all`.

**Geänderte Dateien**:
- `MmViewModel.cs`: `DringlichkeitOptions = ["normal","hoch","kritisch"]`
- `MmView.axaml`:
  - `FormZugeh` → `ComboBox` mit `haus`/`gw`/`all`
  - Detailpanel mit Status-ComboBox und Contractor-Quickedit
  - Status-Badge-Spalte im DataGrid

---

## Schritt 4: NEA DTO-Erweiterung (✅ Erledigt)

**Problemstellung**: `nea.tpl` zeigt `rated_power` (kW) und `fuel_type` (Kraftstoffart).

**Geänderte Dateien**:
- `DTOs.cs`: `NeaSystem` und `NeaSystemSaveRequest` um `rated_power` (`double?`) und `fuel_type` (`string?`) erweitert
- `NeaViewModel.cs`: `FormSystemRatedPower`, `FormSystemFuelType`, `FuelTypeOptions = ["Diesel","Gas","Hybrid","Benzin","Erdgas"]`
- `NeaView.axaml`: DataGrid-Spalten „kW" und „Kraftstoff", Formular-Zeilen mit NumericUpDown + ComboBox

---

## Schritt 5: Klima-Globalsteuerung (✅ Erledigt)

**Problemstellung**: `klima.tpl` hat Buttons „Alle AN", „Alle AUS", Status speichern, Letzten Status wiederherstellen.

**Geänderte Dateien**:
- `KlimaViewModel.cs`:
  - `GlobalControlResult` Property
  - `_savedState` (in-memory Snapshot)
  - RelayCommands: `AllDevicesOnAsync`, `AllDevicesOffAsync`, `SaveState`, `RestoreLastStateAsync`
- `KlimaView.axaml`: Global-Control-Border in Row 1 mit farbigen Buttons und Ergebnis-TextBlock

---

## Schritt 6: Keys Stats-Kacheln (✅ Erledigt)

**Problemstellung**: `keys.tpl` zeigt 4 Statistik-Kacheln.

| Kachel         | Berechnung                        |
|----------------|-----------------------------------|
| Schlüsseltypen | `Inventory.Count`                 |
| Gesamt         | `Sum(Total)`                      |
| Ausgegeben     | `IssuedKeys.Count(k => k.ReturnedAt == null)` |
| Verfügbar      | `Sum(Available)`                  |

**Geänderte Dateien**:
- `KeysViewModel.cs`: `StatTotalKeyTypes`, `StatTotalKeys`, `StatIssuedKeys`, `StatAvailableKeys`
- `KeysView.axaml`: `UniformGrid Columns="4"` mit farbigen Kacheln

---

## Schritt 7: Building Stats-Kacheln (✅ Erledigt)

**Problemstellung**: `building.tpl` zeigt 4 Statistik-Kacheln.

| Kachel               | Berechnung                                   |
|----------------------|----------------------------------------------|
| Gebäude gesamt       | `Buildings.Count`                            |
| Begehungen gesamt    | `TotalInspections` (aus API)                 |
| Offen                | `Count(open + in_progress)`                  |
| Abgeschlossen        | `Count(completed)`                           |

**Geänderte Dateien**:
- `BuildingViewModel.cs`: `StatTotalBuildings`, `StatTotalInspections`, `StatOpenInspections`, `StatCompletedInspections`
- `BuildingView.axaml`: `UniformGrid Columns="4"` mit farbigen Kacheln

---

## Schritt 8: Nachunternehmer-API (⏳ Offen / Server-seitig)

**Problemstellung**: `mm.tpl` referenziert Nachunternehmer als Integer-ID (Lookup-Tabelle).

**Status**: API-Endpunkt `mm_contractors` existiert serverseitig noch nicht.  
**Workaround**: Freitext-Eingabe (ID) im Detail-Panel belassen.  
**Zukünftig**: Neuer PHP-Handler `mm_contractors` + `ComboBox` mit Lookup-Daten im Client.

---

## Abhängigkeiten / Pakete

- `CommunityToolkit.Mvvm` – MVVM-Framework (Source Generator)
- `Avalonia` – UI-Framework
- `Avalonia.Desktop` – Desktop-Laufzeit
- `Microsoft.Extensions.Http` – HTTP-Client Factory
- `Refit` – HTTP API Interface

---

*Erstellt: 2026-04-28*

