# solarSim

[![Download](https://img.shields.io/badge/Download-v0.1.34-4C9AFF?labelColor=333333)](https://github.com/Salutatorian/solarSim/releases/latest)
[![Platform](https://img.shields.io/badge/Windows-native-0B1220?labelColor=333333)](https://github.com/Salutatorian/solarSim/releases/latest)
[![Privacy](https://img.shields.io/badge/Privacy-no_cloud_sync-22C55E?labelColor=333333)](https://github.com/Salutatorian/solarSim#what-it-does)
[![License](https://img.shields.io/badge/License-Apache_2.0-3B82F6?labelColor=333333)](LICENSE)

A visual **solar design lab**. Trace a roof, place panels, wire strings, and sketch equipment.

Public beta — still improving. Design aid only — not stamped electrical, structural, or bankable-yield approval.

## Download

**[Get the latest zip](https://github.com/Salutatorian/solarSim/releases/latest)** → unzip → run `solarSim.exe`

- Windows 10/11 x64
- Map tracer needs [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/)
- SmartScreen may warn (unsigned beta) — **More info → Run anyway**

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

## For developers

```powershell
Launch-solarSim.bat
# or
dotnet run --project src\SolarSim.Preview\SolarSim.Preview.csproj -c Release
dotnet test
```

More: [CHANGELOG.md](CHANGELOG.md) · [RELEASE.md](RELEASE.md) · [SMOKE.md](SMOKE.md) · [ARCHITECTURE.md](ARCHITECTURE.md) · [NOTICE](NOTICE)
