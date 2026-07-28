# Syncs pure C# domain/application sources into the Unity Assets tree.
# Run after changing files under src/SolarSim.Domain or src/SolarSim.Application.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root "UnityProject\Assets\SolarSim\Runtime"

if (Test-Path $dest) {
    Remove-Item $dest -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $dest | Out-Null

Copy-Item -Recurse (Join-Path $root "src\SolarSim.Domain") (Join-Path $dest "Domain")
Copy-Item -Recurse (Join-Path $root "src\SolarSim.Application") (Join-Path $dest "Application")

# Remove non-Unity project files from the copy
Get-ChildItem $dest -Recurse -Include *.csproj,*.md | Remove-Item -Force
Get-ChildItem $dest -Recurse -Directory -Filter obj | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem $dest -Recurse -Directory -Filter bin | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

$asmdef = @'
{
    "name": "SolarSim.Runtime",
    "rootNamespace": "SolarSim",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true,
    "languageVersion": "12"
}
'@
Set-Content -Path (Join-Path $dest "SolarSim.Runtime.asmdef") -Value $asmdef -Encoding UTF8

# Force modern C# for file-scoped namespaces / records / collection expressions.
Set-Content -Path (Join-Path $dest "csc.rsp") -Value "-langversion:12" -Encoding UTF8

# Unity's netstandard profile lacks IsExternalInit (needed for init-only setters / records).
$polyfill = @'
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
'@
Set-Content -Path (Join-Path $dest "IsExternalInit.cs") -Value $polyfill -Encoding UTF8

Write-Output "Synced domain sources to $dest"
Write-Output "Open UnityProject in Unity Hub after sync."
