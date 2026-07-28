@echo off
title solarSim
cd /d "%~dp0"
echo Starting solarSim Panel Lab preview...
echo.
dotnet run --project "%~dp0src\SolarSim.Preview\SolarSim.Preview.csproj" -c Release
if errorlevel 1 (
  echo.
  echo Failed to launch. Make sure .NET 8 SDK is installed.
  pause
)
