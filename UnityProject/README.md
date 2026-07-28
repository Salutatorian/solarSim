# Unity client for solarSim

Phase **0.9** — Unity shell parity for the Solar Panel Lab (panels, snap, PV+/PV− wiring, live status, undo, save/open).

## Quick start

From repo root, double-click **`Launch-Unity.bat`** (syncs domain, opens the Editor).

Or manually:

1. Install Unity Hub + **Unity 6 LTS** (Windows Build Support). Editor **6000.5.5f1** is fine.
2. Open this `UnityProject` folder.
3. Let Unity import packages / create missing ProjectSettings on first open.
4. **Edit → Project Settings → Player → Active Input Handling → Both** (camera pan uses legacy Input).
5. Menu **solarSim → Setup Main Scene** (once) — creates `Assets/SolarSim/Scenes/Main.unity`.
6. Press **Play**.

### Lab loop

1. **Boviet 270 W** (or empty-state **Add Solar Panel**)
2. Drag modules — edges magnetically snap (hold **Alt** to free-move)
3. Select a panel → red **PV+** / black **PV−** appear
4. Drag **PV+** onto another panel’s **PV−**
5. Status bar shows **540 W / Vmp / Voc** for a 2-module string
6. **Ctrl+Z** / **Ctrl+Y** undo/redo · **Ctrl+D** duplicate · **R** rotate · **Del** delete
7. **Save** / **Open** `.solarproj` (Editor file dialogs; standalone writes under Documents\solarSim)

## Sync domain after C# changes

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\SyncDomainToUnity.ps1
```

Authoritative electrical code lives in `src/`. Unity consumes a synced copy under `Assets/SolarSim/Runtime/`.

## Batch scene setup

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe" `
  -batchmode -quit -projectPath "$PWD\UnityProject" `
  -executeMethod SolarSim.Unity.Editor.SceneBootstrap.EnsureMainSceneBatch
```
