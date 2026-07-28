# solarSim Architecture

## Goals

1. Feel like CAD / Figma / a circuit simulator — not a Unity game.
2. Keep electrical truth in a pure domain graph, independent of rendering.
3. Ship Phase 1 (Solar Panel Lab) without blocking future roof, Cesium, Google Solar API, or pvlib work.

## Layers

```
┌─────────────────────────────────────────────┐
│ Presentation (Unity)                        │
│  UI Toolkit chrome + orthographic canvas    │
└────────────────────┬────────────────────────┘
                     │ events / commands
┌────────────────────▼────────────────────────┐
│ Application                                 │
│  SolarProject, CommandHistory, Selection,   │
│  Serialization, Units                       │
└────────────────────┬────────────────────────┘
                     │
┌────────────────────▼────────────────────────┐
│ Domain                                      │
│  Definitions, Instances, Ports, Graph,      │
│  Calculations, Validation                   │
└─────────────────────────────────────────────┘
```

Domain references **no** `UnityEngine` types. Unit tests construct panels, connect ports, and assert watts/volts/amps without a scene.

## Canvas decision

**Orthographic Unity camera for the interactive canvas + UI Toolkit for application chrome.**

Reasons:

- Zoom/pan map cleanly to camera orthographic size / position.
- Panel meshes, snap guides, and MC4 animation live in world space.
- Future Cesium / 3D roof modes can coexist without rewriting UI chrome.
- UI Toolkit remains the right tool for sidebars, inspector, dialogs, status bar.

Alternative considered: pure UI Toolkit canvas. Rejected for Phase 1 because long-term 3D/geospatial integration would force a second interaction stack.

## Core domain types

### SolarPanelDefinition

Immutable catalog entry: manufacturer, model, Pmax/Vmp/Imp/Voc/Isc, width/height/depth mm, connector family, lead lengths, optional visual asset ref.

Built-ins ship with stable GUIDs. Custom definitions set `IsCustom = true`.

### SolarPanelInstance

Placed object: `definitionId`, position (mm), rotation (90° steps in Phase 1), visual mode, `PositivePort`, `NegativePort`.

### ElectricalPort

- `PortType` / `Polarity` (electrical)
- `ConnectorFamily` / `ConnectorInterface` (mechanical) — **not** hardcoded as + = male
- `ConnectionId` when occupied

### ElectricalConnection + PVWire

Undirected electrical link between two ports, plus gauge/type/length metadata. Route control points come later; Phase 1 stores geometric one-way length.

### ElectricalGraph

Owns panels, ports, connections. `TryConnect` validates then mutates. `RebuildStrings` discovers series strings from topology (never from proximity).

### PVString

`Guid` identity + display name (`String 1`…) + ordered panel id list.

### Calculations

`ElectricalCalculationService`:

- Identical series: ΣPmax, ΣVmp, ΣVoc, Imp≈module Imp, Isc≈module Isc
- Mixed modules: warning `MIXED_MODULE_STRING`, conservative min(Imp)/min(Isc), flagged simplified
- Project totals: sum of placed panel Pmax, string count, unconnected info

## Visual vs electrical

| Concern | Owner |
|---------|--------|
| Watts, volts, amps, topology | Domain definitions + graph |
| Rectangle size on canvas | Instance definition mm → world scale |
| Product photo / blueprint skin | Visual layer only |
| Selection outline, MC4 mesh | Presentation only |

Deleting a texture must not change calculations.

## Undo / redo

`ICommand` + `CommandHistory`.

- Drag → one `MovePanelCommand` on mouse-up
- Connect / disconnect / add / delete / rotate / duplicate are discrete commands
- Descriptions exist for a future visible history timeline

## Save format

`.solarproj` — indented JSON, `schemaVersion: 5` (panels + multi-roof layers + equipment/inverters/AC + length unit).

Contains project metadata, definitions used (plus customs), panel instances with port GUIDs, connections/wires, canvas settings.

Load path migrates by schema version; corrupt files raise `ProjectSerializationException` with a human message.

## Events

Application raises:

- `ProjectChanged`
- `CalculationsUpdated`
- `SelectionChanged`
- `HistoryChanged`

UI binds to these; it does not poll topology every frame. Moving a panel does not recompute string electrics unless topology/properties change.

## Folder map

```
src/SolarSim.Domain/
  Electrical/   Enums, Port, Connection, Graph, Validation, Calculations
  Equipment/    SolarPanelDefinition, SolarPanelInstance

src/SolarSim.Application/
  Commands/     History + project commands
  Project/      SolarProject, Selection, CanvasSettings
  Serialization/
  Equipment/    CustomPanelFactory
  Units/
  Events/

UnityProject/Assets/SolarSim/
  UI/           UI Toolkit UXML/USS + controllers
  Canvas/       Camera, interaction, views, snapping, wiring visuals
  Application/  Unity bootstrap wiring into SolarProject
  Persistence/  File dialogs, autosave recovery
```

## Future extension hooks

| Future feature | Hook |
|----------------|------|
| Combiner / inverter / battery | `IElectricalComponent` with N ports |
| Parallel | Branch-connector component — never multi-attach to a single panel port |
| Roof surfaces | Panel position remains mm; later parent to `RoofSurface` |
| Google Solar / Cesium | `IRoofDataProvider` outside roof domain types |
| pvlib | `IAdvancedSimulationProvider` |
| Voltage drop / cold Voc / MPPT checks | Calculation modules consuming the same graph |
| SYSTEM single-line view | Generated from `ElectricalGraph` |

## Performance rules

- No `FindObjectsOfType` architecture
- No electrical recalc on pure translation unless wire length display depends on it (wire length may update; string V/I/W does not)
- Centralized systems over per-object `Update` spam

## Testing contract

A headless test must be able to:

1. Create three Boviet 270 W instances  
2. Connect A+→B−, B+→C−  
3. Assert ~810 W, 93.6 Vmp, 114.3 Voc, 8.65 Imp, 9.2 Isc  

If that ever requires opening a Unity scene, the architecture has regressed.
