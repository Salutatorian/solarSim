# Releasing solarSim

## Local publish (zip)

```powershell
cd C:\Users\JW\Desktop\projects\solarSim
.\Tools\Publish-Windows.ps1
```

Output:

- `artifacts/publish/solarSim-<version>-win-x64/` — runnable folder (`solarSim.exe`)
- `artifacts/publish/solarSim-<version>-win-x64.zip` — uploadable archive

## GitHub Release

1. Bump `<Version>` in `src/SolarSim.Preview/SolarSim.Preview.csproj` if needed.
2. Run smoke checks in [SMOKE.md](SMOKE.md).
3. Commit and push to `main`.
4. Create and push a version tag:

```powershell
git tag v0.1.1
git push origin v0.1.1
```

The **Release** workflow builds, tests, zips, and publishes a GitHub Release with the zip attached.

Manual run: Actions → **Release** → **Run workflow** (builds artifact; tag push creates the Release page).

## Requirements for end users

- Windows 10/11 x64
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (needed for **Trace roof on map**)

## Known issues (public beta)

| Issue | Workaround |
|-------|------------|
| Map blank / WebView2 error | Install Evergreen WebView2, restart app |
| Map needs network | Offline: use **Draw roof** instead |
| Diagonal house after trace | Import auto-straightens; Unlock only to tweak |
| Accidental roof move while selecting | Import locks roof; plain drag = marquee; Alt+drag moves when unlocked |
| Antivirus flags self-contained zip | Unblock zip / allow folder; report false positive if needed |
| Not stamped engineering | Use as design aid only — see in-app **About** |

## Versioning

- `0.1.x` — public beta Windows WPF lab
- Bump patch for fixes; minor when UX/features shift meaningfully
