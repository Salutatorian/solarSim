@echo off
title solarSim Unity
cd /d "%~dp0"

set "UNITY=C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe"
if not exist "%UNITY%" set "UNITY=C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Unity.exe"
if not exist "%UNITY%" (
  echo Unity Editor not found under Hub\Editor.
  echo Install Unity 6 LTS, then open UnityProject\ in Unity Hub.
  pause
  exit /b 1
)

echo Syncing domain sources into Unity Assets...
powershell -ExecutionPolicy Bypass -File "%~dp0Tools\SyncDomainToUnity.ps1"
if errorlevel 1 (
  echo Sync failed.
  pause
  exit /b 1
)

echo Opening Unity project...
start "" "%UNITY%" -projectPath "%~dp0UnityProject"
echo.
echo In Unity: menu solarSim - Setup Main Scene  (once), then Press Play.
echo  Add Boviet - drag panels - drag PV+ to PV- - watch status bar.
