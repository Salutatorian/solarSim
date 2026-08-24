# solarSim — Changelog / development history

Project history for review and submission.  
**Primary product UI:** WPF on Windows (`Launch-solarSim.bat`). Avalonia macOS preview (`Launch-Mac.sh`). Unity is a secondary Panel Lab shell.  
**Disclaimer throughout:** design / simulation aid — not stamped electrical, structural, or bankable-yield approval.

Current `.solarproj` schema: **10** · Domain tests: run `dotnet test` (expect all passing).

### Update toast (`1.5.11`)
- Update reminder sits bottom-right (Ignore / Update) — it no longer covers Save in the title bar
- Settings only says an update is available; release-note markdown is gone
- Checkbox lines up with its label

### Wrap + Home updates (`1.5.1`)
- Suggestion **Lay on roof** wraps into extra rows when a single line would be too long (10 modules → 2×5 on a short roof)
- Update check, install, and What’s new run on the Home screen — you do not need to open a project first

### One window (`1.5.0`)
- Home, kWh, Trace roof, Settings, and confirms are overlays on the editor window so window-capture recorders keep running
- Map Import goes straight to the canvas (WebView2 was hiding the confirm card)
- Home: light labels, compact cards, Recent fills the rest of the panel
- Suggestion is its own left-rail icon. X returns to Select
- The estimate is not laid on the roof until **Lay on roof** / **Replace array**
- Same-polarity cables: red +/+, black −/−. Parallel batteries may join BAT+↔BAT+ and BAT−↔BAT−

### Side rail close + polarity cables (`0.1.40`)
- Suggestion is its own left-rail icon (after a roof exists). X on any side panel returns to Select with the canvas clear
- Create Project, kWh prompt, Trace roof, and in-app confirms stay in the same window so window-capture recorders keep running
- The estimate is not laid on the roof until you click **Lay on roof** / **Replace array**. Replace asks OK or Cancel if modules are already there
- Equipment library no longer stays glued open; click Equipment to place, X to wire with a full canvas
- Dropping a wire on an equipment body snaps to the nearest legal terminal (combiner prefers MPPT, batteries prefer BAT)
- Parallel batteries can join BAT+ to BAT+ and BAT− to BAT−
- Same-polarity cables take the conductor color: red for +/+, black for −/−

### kWh start (`0.1.39`)
- Create Project asks for kWh per month or per year, then opens the editor
- Location, rooms, appliances, roof quiz, budget, and the results page are gone from onboarding
- Skip still opens a blank project; enter kWh later from the sidebar

### License
- Relicensed to **Apache License 2.0** (see [LICENSE](LICENSE) and [NOTICE](NOTICE)); proprietary / no-fork terms removed.

### Module presets on the estimate (`0.1.38`)
- After usage is entered, Quick System Estimate shows 270 / 400 / 550 / 700 W cards: panel count, kW, footprint, and whether it fits the estimated roof
- Same kWh target — smaller watts need more modules and more roof; larger watts need fewer modules and less roof
- Tap a card to use that size as the starting design, then change anything on the canvas
- Generic 700 W added to the panel library
- Billing calendar shows two months at once and paints the full start→end span
- Trace tutorial actually closes the outline; the first point turns green when it is ready to finish (tutorial only)

### US utilities directory (`0.1.37`)
- Quick System Estimate lists retail electric utilities for all 50 states, DC, and US territories (AS, GU, MP, PR, VI)
- Source: EIA Form 861 2024 sales-to-ultimate-customers (states + DC); territories are not in EIA-861 and are listed from official utility sites
- **View published rates / FAC** opens the utility's rate page when we have it, otherwise OpenEI search — solarSim does not invent monthly fuel clauses except CUC (CNMI)
- Prefer kWh from the bill; other utilities still use a manual $/kWh if you reverse-estimate from dollars
- Billing dates use an in-app calendar (tap start, then end). Room counts start blank. AC is optional (mini-split or window/boxed) with wattage math behind the scenes
- Blank numeric fields in Quick System Estimate count as 0 (not a hidden default or an appliance guess). Pick **I don't know** if you want a household-appliance estimate instead
- Equipment is its own left-rail tool (no longer inside Layers). Roof / Equipment plan switch stays a fixed size so the canvas does not jump

### Quick System Estimate (`0.1.36`)
- Optional first step after **Create Project**: location/utility → usage → home profile → appliances → roof → budget/battery → recommended system
- Combines bill kWh, household appliances, roof capacity, and budget into a preliminary array / inverter / battery target — **does not auto-place equipment**
- Prefers kWh on the bill; CUC residential tariff uses dated FAC (Jul 2026 $0.32505, Aug 2026 $0.34129) with day-proration; dollars reverse-estimate at lower confidence
- Always labeled: *Preliminary estimate. Your recommendation will become more accurate after tracing the roof, selecting equipment, and adding detailed usage.*
- Saved as `initialDesignTarget` on the project (schema 10 extra JSON). After the roof is traced, the inspector compares estimate vs fit.

### Architecture docs + per-roof pitch/azimuth (`0.1.35`)
- Rewrote [ARCHITECTURE.md](ARCHITECTURE.md) to match the real product (WPF primary, Unity secondary, schema 10)
- README lists electrical, production, and equipment features that were already in the code
- Each `RoofSurface` stores optional pitch / azimuth (Google Solar import no longer stuffs them into the layer name)
- Monthly production area-weights roof orientations; inspector can edit the active roof (blank = inherit site tilt/az)
- **macOS preview** (`src/SolarSim.Desktop`, Avalonia): open/save the same `.solarproj`, place/move panels; CI publishes Apple Silicon and Intel **.dmg** installers

### Tips / donations (`0.1.34`)
- Settings → **Support solarSim** with Donate $1 / $3 / $5 (USD Stripe Payment Links; browser checkout)

### MC4 Unity connect asset (GrabCAD)
- Exported matching male/female MC4 + connect clip into `UnityProject/Assets/SolarSim/Art/MC4/`
- `Mc4ConnectionPresenter` + `WireView` spawn/play clip on new series joins
- One-time Editor setup: **solarSim → Setup MC4 Connection Prefab**

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
| 2.4 | Satellite house picker (WPF) | done | — |

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

### 2.4 — Satellite house picker (WPF)
- **Trace roof on map…** (Roof rail + empty state): **WebView2 + Leaflet** — sharp Google satellite by default (Esri fallback toggle)
- Search via **OpenStreetMap Nominatim** (no key) or paste lat,lon
- Click roof corners → drag handles to adjust → live edge lengths (m) + area on map
- Undo / Ctrl+Z; **New section** for L/T multi-wing roofs (imports as multiple roof layers)
- After import: drag roof to move; Canva-style ↻ rotate (snaps to 90°); edges auto-straightened; corner drag snaps H/V
- Measure tool: click points on canvas for live edge lengths (same style as Draw roof)

---

## UX / stability notes (not separate phases)
- PV series wires: short physical Bézier jumpers (under modules), neutral cable color, polarity at terminals; no mid-wire MC4 node
- Panel terminals: simple bottom-edge −/+ pair with short leads (`PanelPortLayoutService`); no MC4 glyphs
- Polarity wiring UX: dual-tone leads, series arcs, plug feedback
- Startup NullReference from racking checkbox during XAML load — fixed (checkbox off by default; no `IsChecked` fire before canvas exists)
- Stale equipment selection / status refresh hardening + crash log at `%LOCALAPPDATA%\solarSim\last-error.log`
- **Roof / Interior / Combined:** focused plans stay clean; Combined is for cross-wiring
- **Autosave:** every project change debounced-saves to `%LOCALAPPDATA%\solarSim\projects\` (or the open `.solarproj` path)
- **Canvas-first shell (UX refactor):** narrow tool rail, contextual Roof/Panel/Wire bars, Add palette, Layers drawer, Roof / Equipment / System tabs, soft empty state, dot grid — electrical engine unchanged
- **Canva-style layout:** icon+label left rail, expandable left tool panel, canvas as hero on dark stage, Properties inspector on demand, top File/workspace tabs + bottom zoom bar
- **UI identity pass:** drop Canva naming; neutral charcoal + amber theme; WindowChrome title bar; 48px icon-only rail; row-based inspector; thinner wires; no blue selection glow; structured bottom HUD; contextual site/racking panels; semantic zoom on modules
- **MC4 / Add / selection chrome:** distinctive male/female MC4 glyphs + mated wire nodes; searchable Add tile palette with Recent; floating rotate/duplicate/string toolbar above selection
- **System string identity:** color-code modules and series jumpers by string; System inspector shows SLD-style array/strings/path summary

---

## Remaining roadmap (not done)

| Item | Notes |
|------|--------|
| Cesium for Unity 3D site | Later; WPF satellite picker (2.4) covers mark-house now |
| Unity roof/equipment parity | WPF is currently ahead |
| Full TMY / bankable pvlib | Optional bridge is clearsky-only |

### R2 / R3 — Windows Release packaging
- `Tools/Publish-Windows.ps1` — self-contained `win-x64` folder + zip under `artifacts/publish/`
- `.github/workflows/release.yml` — on `v*` tag: test → publish → GitHub Release asset
- App version `0.1.0` in `SolarSim.Preview.csproj`; see `RELEASE.md`

### R1 / R4 — Public beta polish (`0.1.1`)
- Title bar version + **design aid**; empty-state disclaimer; ⋯ → **About solarSim…**
- WebView2 missing → prompt with download page
- Imported roofs straighten + lock; Unlock / Alt+drag for geometry edits
- `SMOKE.md` checklist; known issues in `RELEASE.md`
- App version `0.1.1`

### Home + updates + ownership (`0.1.2`)
- Home screen: name project, choose born path on disk, or open recent (local only)
- Autosave only to the chosen `.solarproj` path — no silent LocalAppData project files; no cloud sync
- Settings gear (red badge when update available); blue download % bar; bottom-right Update/Later toast
- Background GitHub Releases check/download; apply on close or Update now; What's New notes after install
- Title chrome shows the project name (not “File”); drag-rotate roof anytime with 90° / 45° / 15° magnets
- Brand logo on home, title bars, empty state, dialogs, About, and Windows exe/taskbar icon
- Update installer hardening (fail-safe apply, zip host allowlist, progress when size unknown)
- Home Create uses the typed project name; lock status text clarifies rotate-while-locked
- Proprietary [LICENSE](LICENSE) + [OWNERSHIP.md](OWNERSHIP.md) — public use OK, please do not fork
- External URL allowlist for browser opens

### UX polish (`0.1.3`)
- Logo asset is transparent (no black square behind the mark)
- Paste / multi-duplicate undo as one step (`CompositeCommand`)
- Home Save location text vertically centered (no clipped placeholder)
- Panel drag no longer hard-stops at the yellow setback — free move on the map

### Feedback + equipment faces (`0.1.4`)
- Settings → GitHub repo, Releases, License, Report a bug, Suggest an idea
- GitHub issue templates for bugs (how found / steps / cause) and suggestions
- Photoreal equipment: 6-string combiner, ANENJI 4.2 / 6.5 / 12 kW hybrids, ANENJI 16 kWh battery, PV DC isolator, battery disconnect
- All string-inverter terminals (MPPT / AC / BAT) sit on the bottom edge — not left/right side columns
- Port layouts match the photos (MC4 / lugs / BAT±); 6.5 kW: AC left, BAT middle, PV1/PV2 right; battery cables 1/0–4/0; disconnect Amp ratings with recommended (not forced) wire sizes

### Smart Wiring (`0.1.5`)
- Default cable routing is rounded orthogonal / Manhattan (H/V only) — no soft Bezier sag
- Parallel lane spacing for bundled auto-routes between the same equipment pair
- Draggable bend points; drag a selected wire segment to move a whole run
- Double-click bakes the auto route into editable waypoints; rubber-band preview is ortho while connecting
- Moving equipment with unedited wires re-routes cleanly
- ANENJI 12.8V 300Ah (~3.84 kWh) prismatic battery — BAT− left / BAT+ right on the top edge
- ANENJI 10 kW wall + 5.1 kWh rack (stackable) batteries; large packs (16 kWh / 10 kW / rack) expose dual BAT1± and BAT2±; golf-cart 12.8V stays single ±

### Update UX (`0.1.6`)
- Settings and bottom-right mini toast both show Update / Cancel when a release is available
- Clicking Update downloads (if needed) and auto-installs at 100% — no need to leave Settings for the toast
- Cancel dismisses the notice (and stops an in-progress download)
- After install, a What's new dialog box lists updates / fixes with release date & local time (24-hour)
- Electrical Add catalog uses real equipment photos (ANENJI inverters & batteries, combiner, isolators)
- Equipment workspace hides roof / panel / obstacle tools so the canvas stays equipment-focused

### Equipment face cleanup (`0.1.7`)
- Stripped black matte / floor-shadow fringe from photoreal equipment PNGs
- Photo faces render without a dark rounded chrome border; alpha composites cleanly (no black halo)

### Add catalog labels (`0.1.8`)
- Electrical (and Solar) Add tiles lead with type — Inverter, Battery, Module, etc. — brand and size on the second line
- Subtitles wrap inside the tile so you can tell models apart without guessing

### Equipment snap guides (`0.1.9`)
- Equipment drag snaps to other equipment (edges / centers / spaced seats), same idea as panels
- Magenta dotted alignment guides while dragging; Alt = free drag

### Update toast polish (`0.1.10`)
- Mini toast is Update / Cancel only — no download progress there (bar stays in Settings)
- Clicking Update once downloads and auto-installs at 100% with no second toast click

### What's new after Home (`0.1.11`)
- What's new dialog waits until you leave Home and open/create a project (shows over the editor)
- Uses the shipped changelog (not GitHub zip/install boilerplate)

### Toast jitter fix (`0.1.12`)
- Mini toast no longer shows a live download % (that caused layout vibrate)
- Progress stays in Settings with a fixed-width percent label; download UI updates are throttled
- What's new shows once per version after Got it — not again until the next update

### Equipment resize + Solar disconnect (`0.1.13`)
- Selected equipment shows a corner resize handle (Shift = keep aspect); size persists in `.solarproj`
- Catalog label **Solar disconnect** (was “DC isolator”); smaller default sizes for solar / battery disconnects

### Equipment plan tool rail (`0.1.14`)
- Equipment plan hides Select / Wire / Measure (placement planner only); Roof keeps measure and wiring tools

### Smart wiring + battery ports (`0.1.15`)
- Obstacle-aware ortho routing around equipment bounds (no cables through gear)
- Selected wires show Canva-style white segment handles for H/V run dragging
- Tall battery terminals on top; battery↔disconnect allowed on IN± or OUT±

### Electrical catalog order (`0.1.16`)
- Photo equipment tiles list first; generic icon tiles (MC4 Y±, string inverters, AC) at the bottom

### Wire stability + port labels (`0.1.17`)
- Equipment wires stay attached across zoom and moves (no more broken floating segments)
- Resize keeps aspect; free 360° rotation by default
- Visible inverter labels (PV1/PV2, BAT, AC L/N/G); battery↔solar disconnect allowed; 16 kWh ports on bottom

### Panel string wires on zoom (`0.1.18`)
- Neighbor panel cables stay local when zoomed out (no mega-box detour / floating mid-runs)
- Zoom rebuilds routes from live ports

### Spawn in viewport (`0.1.19`)
- Adding panels or equipment places them in the center of the current screen view (not at world 0,0)

### UI chrome polish (`0.1.20`)
- App-wide slim overlay scrollbars (theme-aware thumbs)
- Dialogs (Settings / What's new / About / Custom panel / Satellite map) use borderless dark chrome instead of the OS white title bar
- ComboBox + dropdown items restyled to match the CAD HUD

### Startup resource fix (`0.1.21`)
- Fix launch crash: missing `TitleBarButton` style (chrome styles now load before ScrollBar/ComboBox overrides)
- Startup XAML failures shut down cleanly instead of a fake "recovered" dialog

### App.Resources load fix (`0.1.22`)
- Root cause: custom ScrollBar/ScrollViewer/ComboBox ControlTemplates aborted Application.Resources mid-load, so later styles (`FieldLabel`, etc.) never registered — home screen crashed
- Removed those templates; keep simple ScrollBar/ComboBox setters; move FieldLabel/SectionLabel with core chrome styles

### Duplicate ComboBox crash (`0.1.23`)
- Remove second implicit `ComboBox` style that hard-crashed startup (`Item has already been added`)

### XAML resource guards (`0.1.24`)
- Unit tests + `Tools/Validate-XamlResources.ps1` (wired into Release CI) fail the build on duplicate App.xaml keys/implicit styles or missing StaticResource references

### Battery ports + fixed wires (`0.1.25`)
- 16 kWh battery BAT± on top edge (not bottom)
- Wire segment drag on first click; equipment cables sit above gear for hit-testing
- Zoom/fit keep manual waypoints (no auto wipe)
- Equipment corner resize scales uniformly with min/max scale clamp (no square squash)

### Battery snap + sticky zoom wires (`0.1.26`)
- Battery disconnect → inverter must land on BAT± (MPPT/PV rejected)
- Snap prefers BAT when wiring from battery / battery disconnect / solar disconnect already on a battery
- Hybrid inverter layout spaces BAT away from PV1/PV2
- Auto-routed cables bake to world-mm waypoints so zoom no longer reshuffles paths

### Inspector scrollbar gutter (`0.1.27`)
- Properties / side-panel scroll areas keep a right gutter so overlay scrollbars no longer cover values
- Slimmer, quieter scrollbar chrome
- Update check explains when a GitHub release exists but the Windows zip is still uploading

### Short clean equipment wires (`0.1.28`)
- Auto-route prefers the short gap corridor between facing ports (battery↔disconnect) instead of looping around the outside of both boxes
- Still avoids crossing other component bodies; outer gutters remain as fallback

### Uniform Add catalog tiles (`0.1.29`)
- Electrical / Add palette tiles share one fixed 108×118 size (photo and glyph cards alike)

### Remove System tab (`0.1.30`)
- Dropped the **System** (combined) workspace tab — plans are **Roof** and **Equipment** only for now
- Combined roof+equipment canvas deferred to a later feature

### Restore panel string wires after open (`0.1.31`)
- Opening a project no longer leaves ports “occupied” with invisible wires (port links are cleared before reconnect)
- Panel↔panel jumpers draw above module faces again; stale baked midpoints are ignored for live routing

### Open copies site/racking/canvas + save/update hardening (`0.1.32`)
- `ReplaceProject` now copies Site, Racking, Canvas, and SchemaVersion from the loaded file (was silently keeping the previous project’s values — data-loss risk on save)
- `HealWiringVisualState` no longer clears panel-jumper waypoints
- Atomic `.solarproj` save (temp + replace)
- Update extract scripts reject zip-slip paths
- Update check prefers `github.com/.../releases/latest` redirect so Settings spam does not hit the API 60/hour rate limit
- Surface a warning when some wires fail to restore on open

### Modeless Settings (`0.1.33`)
- Settings opens with `Show()` instead of `ShowDialog()` so the main window stays usable (minimize, taskbar, canvas)

### Architecture docs + per-roof pitch/azimuth (`0.1.35`)
- Rewrote architecture + README to match WPF as the shipping product
- Optional pitch/azimuth on each roof; Google Solar import stores them as properties; production area-weights planes
- **macOS preview** via Avalonia (`src/SolarSim.Desktop`) — same `.solarproj` engine; CI publishes `osx-arm64` / `osx-x64` app bundles

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
| Last phase recorded | R1/R4 public beta polish (`0.1.1`) |
| Maintainer note | Keep this file updated when shipping a new numbered phase |
