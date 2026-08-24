#!/usr/bin/env bash
set -euo pipefail
# Run the cross-platform Avalonia client (macOS / Linux / Windows).
cd "$(dirname "$0")"
dotnet run --project src/SolarSim.Desktop/SolarSim.Desktop.csproj -c Release -- "$@"
