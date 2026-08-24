# Smoke checklist (before tagging a Release)

Run through this on a clean Windows machine (or your own) after `.\Tools\Publish-Windows.ps1` or a GitHub Release zip.

## Launch
- [ ] Download `solarSim-*-win-x64.exe` and run it (no unzip, no .NET SDK)
- [ ] Title bar shows version + **design aid**
- [ ] ⋯ → **About solarSim…** shows disclaimer + WebView2 note

## Roof
- [ ] **Trace roof on map…** opens (needs WebView2 + network)
- [ ] Search / paste lat,lon → click corners → Import
- [ ] Outline arrives **straightened + locked**
- [ ] Dragging over the house **box-selects** (does not move roof)
- [ ] **Unlock roof** → corner drag / ↻ / straighten work; **Lock** again

## Panels & wiring
- [ ] Add modules, snap edges, rotate (R / ↻)
- [ ] Wire PV+ → PV−; string appears in bottom-right **Strings** list with color
- [ ] Panel string borders readable; click string row selects modules

## Project
- [ ] Save / Open `.solarproj`
- [ ] Ctrl+Z / Ctrl+Shift+Z
- [ ] Export report (HTML)
- [ ] Dark / Light theme toggle

## Expected limitations (OK for beta)
- Windows only; map needs internet + WebView2
- Yield estimates are design-aid (not bankable TMY)
- Unity / Cesium not in this build
