# solarSim Architecture

## Goals

1. Feel like CAD / a circuit lab — not a game.
2. Keep electrical truth in a pure C# domain graph, independent of rendering.
3. WPF is the product. Unity is a secondary 3D / Panel Lab shell on the same domain.

## Layers

```
SolarSim.Domain          electrical graph, roofs, calculations
     ↓
SolarSim.Application     SolarProject, commands, serialization, integrations
     ↓
┌──────────────────┬──────────────────┬──────────────────┐
│ WPF (Preview)    │ Avalonia Desktop │ Unity Panel Lab  │
│ Windows full app │ macOS preview    │ secondary / 3D   │
└──────────────────┴──────────────────┴──────────────────┘
```

Domain references **no** `UnityEngine` or WPF types. Unit tests construct panels, connect ports, and assert watts/volts/amps without a window or a scene.

## What actually ships

**Primary UI:** Windows WPF (`Launch-solarSim.bat`, `src/SolarSim.Preview`). Target `net8.0-windows`. Map tracing uses WebView2.

**macOS / Linux preview:** Avalonia (`Launch-Mac.sh`, `src/SolarSim.Desktop`). Same Domain + Application + `.solarproj`. Not feature-parity (no WebView2 map, thinner chrome).

**Secondary:** Unity Panel Lab (`Launch-Unity.bat`). Orthographic canvas + UI Toolkit. Not feature-parity with WPF.

**Document:** `.solarproj` JSON. Current `schemaVersion` is **10**. Roof pitch / azimuth are optional fields on each roof (older builds ignore them).

## Canvas decision (current)

The interactive design canvas is **WPF** (orthographic mm world, Roof / Equipment plans). Unity remains available for 3D experiments and the Panel Lab; it is not the shipping editor.

## Core domain

### SolarPanelDefinition

Catalog entry: manufacturer, model, Pmax/Vmp/Imp/Voc/Isc, width/height/depth mm, connector family.

### SolarPanelInstance

Placed module: definition id, world X/Y mm, rotation, `PositivePort` / `NegativePort`. Panels are **not** yet parented to a roof plane (`RoofSurfaceId` is a planned 3D step).

### ElectricalGraph

Owns panels, equipment, ports, connections. `TryConnect` validates then mutates. `RebuildStrings` discovers series strings from topology, never from proximity.

### Equipment

Combiners, string inverters (MPPT), PV / battery disconnects, batteries, AC bits, branch connectors. Same graph as panels.

### RoofSurface

2D polygon in mm (vertices, setbacks, obstacles, lock/visibility) **plus** optional `PitchDegrees` and `AzimuthDegrees` (null = inherit project site tilt/azimuth). Multiple roofs compose L/T plans.

Google Solar import writes pitch/azimuth onto each segment. Production estimates area-weight roofs that have orientation; otherwise they use site `ArrayTiltDegrees` / `ArrayAzimuthDegrees`.

### Calculations (already in Domain)

- String Pmax / Vmp / Voc / Imp / Isc (mixed-module warnings)
- Cold Voc / hot Vmp derating, string sizing vs MPPT windows
- Voltage-drop estimates, BOM, racking attachment helpers
- Monthly production (C# design aid), optional pvlib bridge
- HTML design report

## Visual vs electrical

| Concern | Owner |
|---------|--------|
| Watts, volts, amps, topology | Domain graph + definitions |
| Rectangle size on canvas | Definition mm → world scale |
| Product photo / HUD chrome | Presentation only |

Deleting a texture must not change calculations.

## Undo / redo

`ICommand` + `CommandHistory` for panels and roofs (add / move / rotate / connect / …).

Equipment add/move/resize still often mutates `Graph` directly — Ctrl+Z is **not** consistent there yet.

## Events (honest)

`SolarProject` raises `ProjectChanged` and `CalculationsUpdated` together via `NotifyChanged()`. Fine-grained events (`GeometryChanged` vs `ElectricalTopologyChanged`) are a planned split, not current behavior.

Also: `SelectionChanged`, `HistoryChanged`.

## Folder map

```
src/SolarSim.Domain/
  Electrical/   graph, ports, strings, sizing, production, BOM, reports
  Equipment/    panel + equipment instances and definitions
  Roof/         RoofSurface, RoofDocument, geometry, racking
  Geo/          local tangent projection (map / Solar API)

src/SolarSim.Application/
  Project/      SolarProject
  Commands/     undo history
  Serialization/
  Integrations/ Google Solar, Nominatim / map trace, pvlib
  Reports/      HTML exporter
  Units/

src/SolarSim.Preview/     WPF app (MainWindow is still a large shell — split planned)
src/SolarSim.Desktop/     Avalonia macOS / Windows / Linux preview

UnityProject/Assets/SolarSim/   Panel Lab; Runtime/ is a synced copy of Domain/Application
```

## Performance

- No electrical recalc required for pure translation except wire length display
- Keep domain work off per-frame Unity `Update` spam

## Testing contract

A headless test must be able to:

1. Create three Boviet 270 W instances
2. Connect A+→B−, B+→C−
3. Assert ~810 W, 93.6 Vmp, 114.3 Voc, 8.65 Imp, 9.2 Isc

If that ever requires opening WPF or Unity, the architecture has regressed.

## Roadmap (not pretending these are done)

| Next | Why |
|------|-----|
| Split `MainWindow.xaml.cs` into interaction controllers | ~9k-line god window |
| Equipment commands for undo | Ctrl+Z parity with panels |
| `RoofSurfaceId` + local panel coords | Required for honest 3D / per-plane production |
| Auto layout / auto stringing | Highest user-value features on existing geometry |
| Avalonia Mac client | Preview exists (`src/SolarSim.Desktop`); deepen toward WPF parity |
| Unity as 3D viewer of the same `.solarproj` | After roof planes are first-class |
