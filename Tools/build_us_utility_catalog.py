"""Build the bundled US utility catalog from EIA-861 Sales to Ultimate Customers.

EIA-861 covers the 50 states and DC only. Territories are appended from official
utility sites. This script does not scrape or invent FAC / LEAC numbers.
"""

from __future__ import annotations

import json
import re
import xml.etree.ElementTree as ET
import zipfile
from collections import defaultdict
from pathlib import Path

NS = {"m": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
ROOT = Path(__file__).resolve().parents[1]
XLSX = ROOT / "Tools" / "_eia861" / "extract" / "Sales_Ult_Cust_2024.xlsx"
OUT = ROOT / "src" / "SolarSim.Domain" / "Estimate" / "Data" / "us-utilities.json"

SKIP_OWNERSHIP = {"Behind the Meter", "Behind the Meter"}

# Official published-rate pages only. Never a made-up $/kWh table.
OFFICIAL_BY_NAME = [
    (("HI", "hawaiian electric"), "https://www.hawaiianelectric.com/billing-and-payment/rates-and-regulations"),
    (("HI", "hawaii electric light"), "https://www.hawaiianelectric.com/billing-and-payment/rates-and-regulations"),
    (("HI", "maui electric"), "https://www.hawaiianelectric.com/billing-and-payment/rates-and-regulations"),
    (("HI", "kauai island"), "https://kiuc.coop/rates"),
]

EXTRAS = [
    {
        "i": None,
        "n": "Commonwealth Utilities Corporation (CUC)",
        "s": ["MP"],
        "o": "Territorial",
        "r": "https://www.cucgov.org/rates-and-tariffs/",
        "t": "cuc",
    },
    {
        "i": None,
        "n": "Guam Power Authority (GPA)",
        "s": ["GU"],
        "o": "Territorial",
        "r": "https://www.guampowerauthority.com/rates",
        "t": None,
    },
    {
        "i": None,
        "n": "American Samoa Power Authority (ASPA)",
        "s": ["AS"],
        "o": "Territorial",
        "r": "https://www.aspower.com/rates.html",
        "t": None,
    },
    {
        "i": None,
        "n": "Virgin Islands Water and Power Authority (WAPA)",
        "s": ["VI"],
        "o": "Territorial",
        "r": "https://www.viwapa.vi/customer-service/rates/electric-rate",
        "t": None,
    },
    {
        "i": None,
        "n": "LUMA Energy",
        "s": ["PR"],
        "o": "Territorial",
        "r": "https://lumapr.com/",
        "t": None,
    },
    {
        "i": None,
        "n": "Puerto Rico Electric Power Authority (PREPA)",
        "s": ["PR"],
        "o": "Territorial",
        "r": "https://lumapr.com/",
        "t": None,
    },
]


def shared_strings(z: zipfile.ZipFile) -> list[str]:
    root = ET.fromstring(z.read("xl/sharedStrings.xml"))
    out: list[str] = []
    for si in root.findall("m:si", NS):
        out.append("".join(t.text or "" for t in si.findall(".//m:t", NS)))
    return out


def col_row(ref: str) -> tuple[int, int]:
    m = re.match(r"([A-Z]+)(\d+)", ref)
    if not m:
        raise ValueError(ref)
    col, row = m.group(1), int(m.group(2))
    n = 0
    for ch in col:
        n = n * 26 + (ord(ch) - 64)
    return n - 1, row


def iter_rows(path: Path):
    z = zipfile.ZipFile(path)
    ss = shared_strings(z)
    root = ET.fromstring(z.read("xl/worksheets/sheet1.xml"))
    rows: dict[int, dict[int, str]] = defaultdict(dict)
    for c in root.findall(".//m:c", NS):
        ref = c.get("r")
        if not ref:
            continue
        col, row = col_row(ref)
        v = c.find("m:v", NS)
        if v is None or v.text is None:
            val = ""
        elif c.get("t") == "s":
            val = ss[int(v.text)]
        else:
            val = v.text
        rows[row][col] = val
    z.close()
    for r in sorted(rows):
        yield r, rows[r]


def to_float(text: str) -> float:
    t = (text or "").strip().replace(",", "")
    if t in ("", ".", "NA"):
        return 0.0
    try:
        return float(t)
    except ValueError:
        return 0.0


def official_url(name: str, states: list[str]) -> str | None:
    lower = name.lower()
    for (st, needle), url in OFFICIAL_BY_NAME:
        if st in states and needle in lower:
            return url
    return None


def main() -> None:
    grouped: dict[tuple[int, str], dict] = {}
    for r, row in iter_rows(XLSX):
        if r < 4:
            continue
        try:
            eia_id = int(float(row.get(1) or 0))
        except ValueError:
            continue
        name = (row.get(2) or "").strip()
        state = (row.get(6) or "").strip().upper()
        ownership = (row.get(7) or "").strip()
        res_customers = to_float(row.get(11) or "0")
        if eia_id <= 0 or not name or len(state) != 2:
            continue
        if ownership in SKIP_OWNERSHIP:
            continue
        if res_customers <= 0:
            continue
        key = (eia_id, name)
        item = grouped.get(key)
        if item is None:
            grouped[key] = {
                "i": eia_id,
                "n": name,
                "s": [state],
                "o": ownership or None,
            }
        elif state not in item["s"]:
            item["s"].append(state)

    utilities = []
    for item in grouped.values():
        item["s"] = sorted(set(item["s"]))
        url = official_url(item["n"], item["s"])
        if url:
            item["r"] = url
        if not item.get("o"):
            item.pop("o", None)
        utilities.append(item)

    for extra in EXTRAS:
        rec = {k: v for k, v in extra.items() if v is not None}
        utilities.append(rec)

    utilities.sort(key=lambda u: (u["n"].lower(), u.get("i") or 0))

    payload = {
        "y": 2024,
        "src": "U.S. EIA Form EIA-861 2024 Sales to Ultimate Customers (50 states + DC). Territories are not in EIA-861; listed from official utility rate pages. No FAC/LEAC values are stored except the separate CUC tariff.",
        "u": utilities,
    }

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(payload, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")

    states = set()
    for u in utilities:
        states.update(u["s"])
    print(f"wrote {OUT}")
    print(f"utilities {len(utilities)} states {len(states)} bytes {OUT.stat().st_size}")
    print("missing", sorted({"AL","AK","AZ","AR","CA","CO","CT","DE","DC","FL","GA","HI","ID","IL","IN","IA","KS","KY","LA","ME","MD","MA","MI","MN","MS","MO","MT","NE","NV","NH","NJ","NM","NY","NC","ND","OH","OK","OR","PA","RI","SC","SD","TN","TX","UT","VT","VA","WA","WV","WI","WY","AS","GU","MP","PR","VI"} - states))
    print("HI", [u["n"] for u in utilities if "HI" in u["s"]])
    print("MP", [u["n"] for u in utilities if "MP" in u["s"]])


if __name__ == "__main__":
    main()
