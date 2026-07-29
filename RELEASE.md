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
2. Commit and push to `main`.
3. Create and push a version tag:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

The **Release** workflow builds, tests, zips, and publishes a GitHub Release with the zip attached.

Manual run: Actions → **Release** → **Run workflow** (builds artifact; tag push creates the Release page).

## Requirements for end users

- Windows 10/11 x64
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (needed for **Trace roof on map**)
