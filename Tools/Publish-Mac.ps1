#Requires -Version 5.1
<#
.SYNOPSIS
  Publish solarSim Desktop (Avalonia) as a macOS .app and a compressed .dmg.
  Must run on macOS (hdiutil). CI uses macos-latest.

  osx-arm64 = Apple Silicon
  osx-x64   = Intel

.EXAMPLE
  ./Tools/Publish-Mac.ps1
  ./Tools/Publish-Mac.ps1 -Runtime osx-arm64
  ./Tools/Publish-Mac.ps1 -Runtime osx-x64
#>
param(
    [string]$Version = "",
    [string]$Configuration = "Release",
    [ValidateSet("osx-arm64", "osx-x64")]
    [string]$Runtime = "osx-arm64",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repoRoot "artifacts/publish"
}

$csproj = Join-Path $repoRoot "src/SolarSim.Desktop/SolarSim.Desktop.csproj"
if (-not (Test-Path $csproj)) { throw "Project not found: $csproj" }

if (-not $Version) {
    [xml]$proj = Get-Content $csproj
    $Version = $proj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { $Version = "0.0.0-dev" }
}

$chip = if ($Runtime -eq "osx-arm64") { "Apple Silicon" } else { "Intel" }
$publishDir = Join-Path $OutputRoot "solarSim-$Version-$Runtime"
$appDir = Join-Path $publishDir "solarSim.app"
$dmgPath = Join-Path $OutputRoot "solarSim-$Version-$Runtime.dmg"
$zipPath = Join-Path $OutputRoot "solarSim-$Version-$Runtime.zip"

Write-Host "Publishing solarSim $Version ($Configuration / $Runtime / $chip)..." -ForegroundColor Cyan

if (-not (Get-Command hdiutil -ErrorAction SilentlyContinue)) {
    throw "hdiutil not found. DMG files must be built on macOS (GitHub Actions macos-latest, or a Mac)."
}

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir | Out-Null

$staging = Join-Path $OutputRoot "_mac-stage-$Runtime"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }

dotnet publish $csproj -c $Configuration -r $Runtime --self-contained true -o $staging --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$macos = Join-Path $appDir "Contents/MacOS"
$resources = Join-Path $appDir "Contents/Resources"
New-Item -ItemType Directory -Path $macos -Force | Out-Null
New-Item -ItemType Directory -Path $resources -Force | Out-Null
Copy-Item (Join-Path $staging "*") $macos -Recurse -Force
Copy-Item (Join-Path $repoRoot "src/SolarSim.Desktop/Info.plist") (Join-Path $appDir "Contents/Info.plist") -Force

$exe = Join-Path $macos "solarSim"
if (Test-Path $exe) {
    & chmod +x $exe
}

# DMG root: app + Applications shortcut so users can drag-install.
$dmgStage = Join-Path $OutputRoot "_dmg-stage-$Runtime"
if (Test-Path $dmgStage) { Remove-Item $dmgStage -Recurse -Force }
New-Item -ItemType Directory -Path $dmgStage | Out-Null
Copy-Item $appDir (Join-Path $dmgStage "solarSim.app") -Recurse -Force
& ln -s /Applications (Join-Path $dmgStage "Applications")

if (Test-Path $dmgPath) { Remove-Item $dmgPath -Force }
$volName = "solarSim $Version $chip"
& hdiutil create -volname $volName -srcfolder $dmgStage -ov -format UDZO $dmgPath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $dmgPath)) {
    throw "hdiutil failed to create $dmgPath"
}

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Push-Location $publishDir
try {
    if (Get-Command zip -ErrorAction SilentlyContinue) {
        & zip -r -q $zipPath "solarSim.app"
    }
} finally {
    Pop-Location
}

Remove-Item $staging -Recurse -Force
Remove-Item $dmgStage -Recurse -Force
Write-Host "App: $appDir"
Write-Host "DMG: $dmgPath  ($chip)"
if (Test-Path $zipPath) { Write-Host "Zip: $zipPath" }
