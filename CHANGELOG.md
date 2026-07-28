# solarSim — Changelog / development history

Project history for review and submission.  
**Primary product UI:** WPF (`Launch-solarSim.bat`). Unity is a secondary Panel Lab shell.  
**Disclaimer throughout:** design / simulation aid — not stamped electrical, structural, or bankable-yield approval.

Current `.solarproj` schema: **10** · Domain tests: run `dotnet test` (expect all passing).

---

## Summary table

| Phase | Title | Status | Schema |
|-------|--------|--------|--------|
| 0.1 | Solar Panel Lab | done | — |
| 0.2 | Manual roofs + layers | done | 5* |
| 0.3 | Combiners / strings / voltage drop | done | — |
| 0.4 | Inverters / MPPT | done | — |
| 0.5 | AC side / single-line | done | 5 |
| 0.6 | Cold Voc / temp derating / string sizing | done | 6 |
| 0.7 | Wire routing + BOM | done | 7 |
| 0.8 | Racking / attachment helpers | done | 8 |
| 0.9 | Unity Panel Lab shell | done | — |
| 1.0 | Reports / HTML→PDF | done | — |
| 1.1 | Batteries + DC disconnects | done | — |
| 1.2 | Site / weather assumptions | done | 9 |
| 2.0 | Google Solar API roof import | done | — |
| 2.1 | Detailed monthly production (C#) | done | 10 |
| 2.2 | Optional Python/pvlib bridge | done | — |
| 2.3 | Combined plan + dark CAD HUD | done | — |

\*Multi-roof + units landed with / around schema 5.

---

## Phase details

### 0.1 — Solar Panel Lab
- Place real-sized modules, magnetic edge snap, PV+/PV− wiring
- Live string math (Pmax, Vmp, Voc, Imp, Isc) — no Calculate button
- Select / delete / undo / save-load `.solarproj`
- Pure C# domain + WPF preview

### 0.2 — Manual roofs
- Polygon draw with 90°/180° ortho snap (Alt = free)
- Live edge measurement; units mm / m / ft / ft-in / yd / in
- Multi-roof layers (L/T shapes), Photoshop-style Layers panel
- Setbacks, obstacles, panel containment

### 0.3 — Combiners / strings / voltage drop
- Combiner boxes, string inputs, home-run voltage drop estimates

### 0.4 — Inverters / MPPT mapping
- String inverters with MPPT channels and compatibility checks

### 0.5 — AC side / single-line
- AC Disconnect, AC Load Center
- Text single-line summary in inspector
- Schema **5**

### 0.6 — Cold Voc / temp derating / string sizing
- Site cold Voc / hot cell temps
- Module temp coeffs; MPPT cold Voc / hot Vmp checks
- Series length advice on inverter select
- Schema **6**

### 0.7 — Wire routing + BOM
- Wire waypoints (double-click / drag bends)
- BOM / wire schedule by gauge
- Schema **7**

### 0.8 — Racking / attachment helpers
- Estimated rails, attachments, end/mid clamps
- Racking params in sidebar; BOM racking lines
- Attachment overlay is **optional, off by default** (checkbox under RACKING)
- Schema **8**

### 0.9 — Unity shell (Panel Lab)
- Orthographic canvas + UI Toolkit chrome
- Same electrical engine; WPF remains full-featured primary UI
- `Launch-Unity.bat` + domain sync script

### 1.0 — Reports / PDF
- Export HTML report: single-line, SVG array layout, module schedule, racking, BOM
- Browser Ctrl+P → Save as PDF

### 1.1 — Batteries + DC disconnects
- Battery (BAT±), Battery Disconnect
- UI label **DC / PV Disconnect** for array-side disconnect
- Single-line Storage line; BOM auto-includes equipment

### 1.2 — Site / weather assumptions
- Location, lat/lon, PSH, derate, climate presets
- Rough annual energy on SLD / report
- Schema **9**

### 2.0 — Google Solar API roof import
- Address or `lat,lon` → buildingInsights roof-segment layers
- API key via UI file or `SOLARSIM_GOOGLE_API_KEY`
- Fills site lat/lon + PSH from max sunshine hours

### 2.1 — Detailed monthly production (C#)
- Tilt / azimuth + NH/SH seasonality
- Monthly kWh on Single-Line + HTML report
- Schema **10**

### 2.2 — Optional Python / pvlib bridge
- `Tools/pvlib_estimate.py` + WPF **Run pvlib yield** / **pvlib status**
- Requires `pip install pvlib pandas numpy` when used
- C# estimate remains default for SLD / reports

### 2.3 — Combined plan + dark CAD HUD
- Workspace tabs: **Roof** · **Interior** · **Combined**
- Combined shows roof geometry + modules + equipment on one canvas
- Combined shows **all** wires including module↔equipment home-runs
- Dark CAD HUD theme by default; **Light** / **Dark** toggle in the top bar
- Design aid only — not a stamped permit package

---

## UX / stability notes (not separate phases)
- Polarity wiring UX: dual-tone leads, series arcs, plug feedback
- Startup NullReference from racking checkbox during XAML load — fixed (checkbox off by default; no `IsChecked` fire before canvas exists)
- Stale equipment selection / status refresh hardening + crash log at `%LOCALAPPDATA%\solarSim\last-error.log`
- **Roof / Interior / Combined:** focused plans stay clean; Combined is for cross-wiring
- **Autosave:** every project change debounced-saves to `%LOCALAPPDATA%\solarSim\projects\` (or the open `.solarproj` path)
- **Canvas-first shell (UX refactor):** narrow tool rail, contextual Roof/Panel/Wire bars, Add palette, Layers drawer, Roof / Equipment / System tabs, soft empty state, dot grid — electrical engine unchanged
- **Canva-style layout:** icon+label left rail, expandable left tool panel, canvas as hero on dark stage, Properties inspector on demand, top File/workspace tabs + bottom zoom bar

---

## Remaining roadmap (not done)

| Item | Notes |
|------|--------|
| Cesium site context | Likely Unity-oriented |
| Unity roof/equipment parity | WPF is currently ahead |
| Full TMY / bankable pvlib | Optional bridge is clearsky-only |

---

## How to verify for submission

```powershell
cd C:\Users\JW\Desktop\projects\solarSim
dotnet test
Launch-solarSim.bat
```

Launch entry points: `Launch-solarSim.bat` (WPF), `Launch-Unity.bat` (Unity lab).

---

## Document control

| Field | Value |
|-------|--------|
| Changelog created | 2026-07-29 |
| Last phase recorded | 2.3 Combined plan + dark CAD HUD |
| Maintainer note | Keep this file updated when shipping a new numbered phase |
