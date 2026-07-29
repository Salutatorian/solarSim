#Requires -Version 5.1
<#
.SYNOPSIS
  Publish solarSim (WPF) as a self-contained Windows x64 folder + zip.

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
$zipPath = Join-Path $OutputRoot "solarSim-$Version-$Runtime.zip"

Write-Host "Publishing solarSim $Version ($Configuration / $Runtime)..." -ForegroundColor Cyan

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

dotnet publish $csproj `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$readme = @"
solarSim $Version (Windows x64)
===============================

Run:  solarSim.exe

Requirements
------------
- Windows 10/11 x64
- Microsoft Edge WebView2 Runtime (for map tracer)
  Download: https://developer.microsoft.com/microsoft-edge/webview2/

Notes
-----
- Design / simulation aid only — not stamped electrical approval.
- First launch may create files under %LOCALAPPDATA%\solarSim\
"@
Set-Content -Path (Join-Path $publishDir "README.txt") -Value $readme -Encoding UTF8

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

Write-Host ""
Write-Host "Published folder: $publishDir" -ForegroundColor Green
Write-Host "Zip archive:      $zipPath" -ForegroundColor Green
Get-Item $zipPath | Select-Object FullName, @{N = "SizeMB"; E = { [math]::Round($_.Length / 1MB, 2) } }
