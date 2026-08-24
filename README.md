# solarSim

[![Download](https://img.shields.io/badge/Download-v1.5.11-4C9AFF?labelColor=333333)](https://github.com/Salutatorian/solarSim/releases/latest)
[![Platform](https://img.shields.io/badge/Windows-full_editor-0B1220?labelColor=333333)](https://github.com/Salutatorian/solarSim/releases/latest)
[![macOS](https://img.shields.io/badge/macOS-preview-0B1220?labelColor=333333)](https://github.com/Salutatorian/solarSim/releases/latest)
[![Privacy](https://img.shields.io/badge/Privacy-no_cloud_sync-22C55E?labelColor=333333)](https://github.com/Salutatorian/solarSim#what-it-does)
[![License](https://img.shields.io/badge/License-Apache_2.0-3B82F6?labelColor=333333)](LICENSE)

A visual **solar design lab**. Windows has the full editor; macOS has a preview that opens the same `.solarproj` files.

Public beta — still improving. Design aid only — not stamped electrical, structural, or bankable-yield approval.

## Download

**[Get the latest zip](https://github.com/Salutatorian/solarSim/releases/latest)**

- **Windows:** unzip → run `solarSim.exe` (full editor). Map tracer needs [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/). SmartScreen: **More info → Run anyway**.
- **macOS preview:** open the DMG and drag `solarSim` into Applications.
  - Apple Silicon: `solarSim-*-osx-arm64.dmg`
  - Intel: `solarSim-*-osx-x64.dmg`
  - First launch: right-click → **Open** (unsigned). Same project files; no satellite map yet.

## What it does

### Trace your roof

Outline your house on satellite imagery and import a near–real-world footprint.

![Trace roof on satellite map](docs/screenshots/01-trace-roof-on-map.png)

### Place panels & strings

Drop modules at real size, connect PV+ / PV−, and see strings light up with live power and voltage.

![Roof plan with panels and strings](docs/screenshots/02-roof-panels-and-strings.png)

### Design equipment

Lay out inverters, batteries, disconnects, and combiners on an equipment plan.

![Equipment plan](docs/screenshots/03-equipment-electrical.png)

### Also in the app

- Optional **kWh** prompt after Create Project (month or year) — sizes the array, then you jump straight to the roof
- Multi-roof layers, setbacks, and edge measurements
- Live string math (Pmax, Vmp, Voc, Imp, Isc), cold Voc / hot Vmp, MPPT range checks
- Smart orthogonal wiring, voltage-drop estimates, BOM, racking helpers
- Site / climate assumptions; per-roof pitch and azimuth (Google Solar import fills these)
- Monthly production estimate (C# design aid); optional pvlib; HTML report
- Local `.solarproj` files, autosave, undo/redo (panels/roofs), in-app updater

Windows WPF is the full editor. macOS uses the Avalonia preview (`Launch-Mac.sh`). Unity is a secondary Panel Lab.

## For developers

```powershell
Launch-solarSim.bat
# or
dotnet run --project src\SolarSim.Preview\SolarSim.Preview.csproj -c Release
dotnet test
```

macOS / Linux preview:

```bash
./Launch-Mac.sh
# or
dotnet run --project src/SolarSim.Desktop/SolarSim.Desktop.csproj -c Release
```

More: [CHANGELOG.md](CHANGELOG.md) · [RELEASE.md](RELEASE.md) · [SMOKE.md](SMOKE.md) · [ARCHITECTURE.md](ARCHITECTURE.md) · [NOTICE](NOTICE)
