# solarSim

[![Download](https://img.shields.io/badge/Download-v1.5.11-4C9AFF?labelColor=333333)](#download-the-app)
[![Windows](https://img.shields.io/badge/Windows-solarSim.exe-0B1220?labelColor=333333)](https://github.com/Salutatorian/solarSim/releases/download/v1.5.11/solarSim-1.5.11-win-x64.zip)
[![Mac Silicon](https://img.shields.io/badge/Mac-Apple_Silicon-0B1220?labelColor=333333)](https://github.com/Salutatorian/solarSim/releases/download/v1.5.11/solarSim-1.5.11-osx-arm64.dmg)
[![Mac Intel](https://img.shields.io/badge/Mac-Intel-0B1220?labelColor=333333)](https://github.com/Salutatorian/solarSim/releases/download/v1.5.11/solarSim-1.5.11-osx-x64.dmg)
[![Privacy](https://img.shields.io/badge/Privacy-no_cloud_sync-22C55E?labelColor=333333)](#what-it-does)
[![License](https://img.shields.io/badge/License-Apache_2.0-3B82F6?labelColor=333333)](LICENSE)

A visual **solar design lab**. Windows has the full editor; macOS has a preview that opens the same `.solarproj` files.

Public beta — still improving. Design aid only — not stamped electrical, structural, or bankable-yield approval.

## Download the app

You do **not** need a GitHub account. Click the button for your computer. That starts the download — you never have to find a “Releases” page.

### Windows (full editor)

**[Download for Windows](https://github.com/Salutatorian/solarSim/releases/download/v1.5.11/solarSim-1.5.11-win-x64.zip)** — this is the app (`solarSim.exe` inside a zip).

1. Click the link. Your browser saves `solarSim-1.5.11-win-x64.zip`.
2. Right-click the zip → **Extract All**.
3. Open the new folder and double-click **solarSim.exe**.

Windows may say “Windows protected your PC”. Click **More info** → **Run anyway**. That is normal for this unsigned beta.

To trace a roof on the map, Windows needs [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) (most PCs already have it).

### Mac — Apple Silicon (M1, M2, M3, M4)

**[Download for Mac (Apple Silicon)](https://github.com/Salutatorian/solarSim/releases/download/v1.5.11/solarSim-1.5.11-osx-arm64.dmg)**

Not sure which Mac? Apple menu (top-left apple) → **About This Mac**. If **Chip** says Apple M1 / M2 / M3 / M4, use this one.

1. Open the `.dmg` file.
2. Drag **solarSim** into **Applications**.
3. First time: right-click the app → **Open** (macOS blocks unsigned apps until you do this).

Mac is a preview: same project files as Windows, no satellite map yet.

### Mac — Intel

**[Download for Mac (Intel)](https://github.com/Salutatorian/solarSim/releases/download/v1.5.11/solarSim-1.5.11-osx-x64.dmg)**

**About This Mac** says **Processor: Intel**? Use this one. Same install steps as Apple Silicon.

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
