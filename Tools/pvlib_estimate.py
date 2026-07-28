#!/usr/bin/env python3
"""solarSim optional pvlib production estimate.

Reads JSON on stdin, writes JSON on stdout.
Requires: Python 3.10+, pvlib, pandas, numpy

  pip install pvlib pandas numpy

Design aid only — clearsky-based, not a TMY / bankable yield study.
"""

from __future__ import annotations

import json
import sys
from datetime import datetime


def fail(code: int, message: str) -> None:
    json.dump({"ok": False, "error": message}, sys.stdout)
    sys.exit(code)


def main() -> None:
    try:
        import numpy as np
        import pandas as pd
        from pvlib import irradiance, location, temperature
    except ImportError as ex:
        fail(2, f"Missing dependency: {ex}. Run: pip install pvlib pandas numpy")

    try:
        req = json.load(sys.stdin)
    except json.JSONDecodeError as ex:
        fail(1, f"Invalid JSON input: {ex}")

    try:
        lat = float(req["latitude"])
        lon = float(req["longitude"])
        kw = max(0.0, float(req.get("arrayKwDc", 0)))
        tilt = float(req.get("tiltDegrees", 20))
        azimuth = float(req.get("azimuthDegrees", 180))
        derate = float(req.get("derate", 0.85))
        gamma = float(req.get("pmaxTempCoeffPercentPerC", -0.35)) / 100.0
        year = int(req.get("year", datetime.utcnow().year))
    except (KeyError, TypeError, ValueError) as ex:
        fail(1, f"Bad request fields: {ex}")

    if kw <= 0:
        fail(1, "arrayKwDc must be > 0")

    site = location.Location(latitude=lat, longitude=lon, tz="UTC")
    times = pd.date_range(
        f"{year}-01-01",
        f"{year}-12-31 23:00",
        freq="h",
        tz="UTC",
    )
    # Sample every 3 hours to keep runtime low while covering the year.
    times = times[::3]
    clear = site.get_clearsky(times, model="ineichen")
    solar = site.get_solarposition(times)
    poa = irradiance.get_total_irradiance(
        surface_tilt=tilt,
        surface_azimuth=azimuth,
        dni=clear["dni"],
        ghi=clear["ghi"],
        dhi=clear["dhi"],
        solar_zenith=solar["apparent_zenith"],
        solar_azimuth=solar["azimuth"],
    )["poa_global"].fillna(0.0)

    # Cell temperature (simple open-rack glass/polymer) then STC power with temp coeff.
    t_cell = temperature.sapm_cell(
        poa_global=poa,
        temp_air=25.0,
        wind_speed=1.0,
        a=-3.47,
        b=-0.0594,
        deltaT=3.0,
    )
    # Scale clearsky down ~0.55 so results are closer to real-sky annuals (clearsky is optimistic).
    clearsky_scale = float(req.get("clearskyScale", 0.55))
    p_kw = (
        kw
        * (poa * clearsky_scale / 1000.0)
        * (1.0 + gamma * (t_cell - 25.0))
        * derate
    )
    # Each sample represents 3 hours.
    hours_per_sample = 3.0
    energy_kwh = p_kw * hours_per_sample

    monthly = []
    for month in range(1, 13):
        mask = times.month == month
        kwh = float(energy_kwh[mask].sum())
        monthly.append(
            {
                "month": month,
                "monthName": datetime(2000, month, 1).strftime("%b"),
                "estimatedKwh": round(kwh, 2),
            }
        )

    annual = float(sum(m["estimatedKwh"] for m in monthly))
    json.dump(
        {
            "ok": True,
            "engine": "pvlib-clearsky",
            "arrayKwDc": kw,
            "tiltDegrees": tilt,
            "azimuthDegrees": azimuth,
            "derate": derate,
            "latitude": lat,
            "longitude": lon,
            "estimatedAnnualKwh": round(annual, 2),
            "estimatedDailyKwh": round(annual / 365.0, 3),
            "months": monthly,
            "methodNote": (
                "pvlib Ineichen clearsky × POA × temp × derate × clearskyScale "
                f"{clearsky_scale} — design aid, not TMY / bankable yield."
            ),
        },
        sys.stdout,
    )


if __name__ == "__main__":
    main()
