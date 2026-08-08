# solarSim

A visual **solar design lab** for Windows.

Trace a roof, place panels, wire strings, and sketch equipment — on your computer.  
No accounts. No cloud sync. Public beta — still improving.

This is a **design aid**, not stamped electrical, structural, or bankable-yield approval.

## Download

**[Download the latest release](https://github.com/Salutatorian/solarSim/releases/latest)**

| Platform | Get |
| --- | --- |
| **Windows** | `solarSim-*-win-x64.zip` → unzip → run `solarSim.exe` |

Needs Windows 10/11 x64. For **Trace roof on map**, install [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/).

Windows may warn (SmartScreen) because builds aren’t code-signed yet — **More info → Run anyway** is normal for this beta.

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

## Privacy

Projects stay on your PC (`.solarproj`). Updates only check GitHub Releases. Optional tips in Settings open Stripe in your browser — card details never touch the app.

## License

[Apache License 2.0](LICENSE). See [NOTICE](NOTICE).

## For developers

```powershell
Launch-solarSim.bat
# or
dotnet run --project src\SolarSim.Preview\SolarSim.Preview.csproj -c Release
```

```powershell
dotnet test
```

More detail: [CHANGELOG.md](CHANGELOG.md) · [RELEASE.md](RELEASE.md) · [SMOKE.md](SMOKE.md) · [ARCHITECTURE.md](ARCHITECTURE.md)
