# Releasing solarSim

## Local publish (zip)

```powershell
cd C:\Users\JW\Desktop\projects\solarSim
.\Tools\Publish-Windows.ps1
```

Output:

- `artifacts/publish/solarSim-<version>-win-x64/` — runnable folder (`solarSim.exe`)
- `artifacts/publish/solarSim-<version>-win-x64.zip` — uploadable archive

macOS (run on a Mac or in GitHub Actions `macos-latest`):

```powershell
./Tools/Publish-Mac.ps1              # Apple Silicon
./Tools/Publish-Mac.ps1 -Runtime osx-x64
```

Output: `solarSim-<version>-osx-arm64.dmg` (Apple Silicon) and `solarSim-<version>-osx-x64.dmg` (Intel).
Each disk image has `solarSim.app` plus an Applications shortcut — drag to install.

## GitHub Release

Prefer **milestone releases** (~every 10 patches, or when something big ships) so the Releases page stays short. See [docs/RELEASE-MILESTONES.md](docs/RELEASE-MILESTONES.md). You can still bump the app version every patch; only push a `v*` tag when you want a public download page.

1. Bump `<Version>` in `src/SolarSim.Preview/SolarSim.Preview.csproj` if needed.
2. Run smoke checks in [SMOKE.md](SMOKE.md).
3. Commit and push to `main`.
4. When publishing a milestone, create and push a version tag:

```powershell
git tag v1.5.11
git push origin v1.5.11
```

The **Release** workflow builds, tests, zips, and publishes a GitHub Release with the zip attached.

Manual run: Actions → **Release** → **Run workflow** (builds artifact; tag push creates the Release page).

After the zip is up, edit the Release notes to summarize everything since the last milestone (not just the last commit).

## Requirements for end users

- Windows 10/11 x64 (full WPF editor)
- macOS 12+ preview: Apple Silicon or Intel **.dmg** (unsigned)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (Windows **Trace roof on map** only)

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

- `1.5.x` — public beta (one-window Windows editor; macOS preview)
- `0.1.x` — earlier public beta
- Bump patch for fixes; minor when UX/features shift meaningfully
