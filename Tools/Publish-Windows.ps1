#Requires -Version 5.1
<#
.SYNOPSIS
  Publish solarSim (WPF) as one self-contained Windows x64 .exe (plus a zip for in-app updates).

.EXAMPLE
  .\Tools\Publish-Windows.ps1
  .\Tools\Publish-Windows.ps1 -Version 0.1.0
#>
param(
    [string]$Version = "",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repoRoot "artifacts\publish"
}

$csproj = Join-Path $repoRoot "src\SolarSim.Preview\SolarSim.Preview.csproj"
if (-not (Test-Path $csproj)) {
    throw "Project not found: $csproj"
}

if (-not $Version) {
    [xml]$proj = Get-Content $csproj
    $Version = $proj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { $Version = "0.0.0-dev" }
}

$publishDir = Join-Path $OutputRoot "solarSim-$Version-$Runtime"
$exePath = Join-Path $OutputRoot "solarSim-$Version-$Runtime.exe"
$zipPath = Join-Path $OutputRoot "solarSim-$Version-$Runtime.zip"

Write-Host "Publishing solarSim $Version ($Configuration / $Runtime) as a single file..." -ForegroundColor Cyan

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

dotnet publish $csproj `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$builtExe = Join-Path $publishDir "solarSim.exe"
if (-not (Test-Path $builtExe)) {
    throw "Publish did not produce solarSim.exe"
}

Copy-Item -Path $builtExe -Destination $exePath -Force

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

# In-app updater still downloads a zip and copies solarSim.exe into place.
Compress-Archive -Path $builtExe -DestinationPath $zipPath -Force

Write-Host ""
Write-Host "Windows exe: $exePath" -ForegroundColor Green
Write-Host "Zip (updates): $zipPath" -ForegroundColor Green
Get-Item $exePath, $zipPath | Select-Object FullName, @{N = "SizeMB"; E = { [math]::Round($_.Length / 1MB, 2) } }
Get-ChildItem $publishDir | Select-Object Name, @{N = "SizeMB"; E = { [math]::Round($_.Length / 1MB, 2) } }
