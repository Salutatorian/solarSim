#Requires -Version 5.1
<#
.SYNOPSIS
  Fail CI/local builds if App.xaml has duplicate resources or missing StaticResource keys.
#>
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$appXaml = Join-Path $repoRoot "src\SolarSim.Preview\App.xaml"
if (-not (Test-Path $appXaml)) { throw "Missing $appXaml" }

$text = Get-Content $appXaml -Raw

$keys = [regex]::Matches($text, 'x:Key="([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
$keyDupes = $keys | Group-Object | Where-Object Count -gt 1
if ($keyDupes) {
    throw ("Duplicate x:Key in App.xaml: " + (($keyDupes | ForEach-Object Name) -join ", "))
}

$implicit = @()
foreach ($m in [regex]::Matches($text, '<Style\b[^>]*>')) {
    $tag = $m.Value
    if ($tag -match 'x:Key=') { continue }
    if ($tag -match 'TargetType="([^"]+)"') { $implicit += $Matches[1] }
}
$styleDupes = $implicit | Group-Object | Where-Object Count -gt 1
if ($styleDupes) {
    throw ("Duplicate implicit Style TargetType in App.xaml: " + (($styleDupes | ForEach-Object Name) -join ", "))
}

$keySet = [System.Collections.Generic.HashSet[string]]::new([string[]]$keys)
$missing = @()
Get-ChildItem (Join-Path $repoRoot "src\SolarSim.Preview") -Recurse -Filter *.xaml | ForEach-Object {
    $fileText = Get-Content $_.FullName -Raw
    foreach ($m in [regex]::Matches($fileText, '\{StaticResource\s+([A-Za-z_][A-Za-z0-9_]*)\}')) {
        $k = $m.Groups[1].Value
        if (-not $keySet.Contains($k)) {
            $missing += "$($_.Name) → $k"
        }
    }
}
$missing = $missing | Sort-Object -Unique
if ($missing.Count -gt 0) {
    throw ("Missing StaticResource keys:`n" + ($missing -join "`n"))
}

Write-Host "Validate-XamlResources: OK ($($keys.Count) keys, no duplicates)" -ForegroundColor Green
