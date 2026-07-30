#!/usr/bin/env python3
"""Generate seed corpus and PoC for the solar_module_catalog_fuzzer."""

import os
import struct

CORPUS_DIR = "fuzz/corpus/solar_module_catalog_fuzzer"
POC_DIR = "fuzz/poc"


def make_smc(name: bytes, electrical: tuple, width: int, height: int, tags: list[bytes] = None) -> bytes:
    """Build a valid SMC file containing one module."""
    if tags is None:
        tags = []
    magic = b"SMC\x00"
    version = 1
    module_count = 1
    reserved = 0
    header = magic + struct.pack("<III", version, module_count, reserved)

    pmax, vmp, imp, voc, isc, vmp_temp = electrical
    body = (
        struct.pack("<I", len(name))
        + name
        + struct.pack("<dddddd", pmax, vmp, imp, voc, isc, vmp_temp)
        + struct.pack("<II", width, height)
        + struct.pack("<I", len(tags))
    )
    for tag in tags:
        body += struct.pack("<I", len(tag)) + tag

    return header + body


def main() -> None:
    os.makedirs(CORPUS_DIR, exist_ok=True)
    os.makedirs(POC_DIR, exist_ok=True)

    # Seed 1: minimal valid module (Boviet 270 W values).
    seed1 = make_smc(
        name=b"Boviet 270 W",
        electrical=(270.0, 31.2, 8.65, 38.1, 9.20, -0.34),
        width=1134,
        height=1900,
        tags=[b"mono", b"60cell"],
    )
    with open(os.path.join(CORPUS_DIR, "seed1_boviet_270w.smc"), "wb") as f:
        f.write(seed1)

    # Seed 2: custom panel with long name (still < 256).
    seed2 = make_smc(
        name=b"A" * 200,
        electrical=(450.0, 41.0, 10.98, 49.5, 11.60, -0.30),
        width=1134,
        height=1900,
        tags=[],
    )
    with open(os.path.join(CORPUS_DIR, "seed2_long_name.smc"), "wb") as f:
        f.write(seed2)

    # Seed 3: module at the boundary (name_len = 255, valid).
    seed3 = make_smc(
        name=b"X" * 255,
        electrical=(300.0, 32.5, 9.23, 40.1, 9.85, -0.33),
        width=1000,
        height=1700,
        tags=[b"boundary"],
    )
    with open(os.path.join(CORPUS_DIR, "seed3_boundary_name.smc"), "wb") as f:
        f.write(seed3)

    # PoC: name_len == 256 triggers the off-by-one stack write.
    poc = make_smc(
        name=b"A" * 256,
        electrical=(270.0, 31.2, 8.65, 38.1, 9.20, -0.34),
        width=1134,
        height=1900,
        tags=[b"poc"],
    )
    with open(os.path.join(POC_DIR, "poc_name_overflow.smc"), "wb") as f:
        f.write(poc)

    print(f"Wrote {len(seed1)} bytes to {CORPUS_DIR}/seed1_boviet_270w.smc")
    print(f"Wrote {len(seed2)} bytes to {CORPUS_DIR}/seed2_long_name.smc")
    print(f"Wrote {len(seed3)} bytes to {CORPUS_DIR}/seed3_boundary_name.smc")
    print(f"Wrote {len(poc)} bytes to {POC_DIR}/poc_name_overflow.smc")


if __name__ == "__main__":
    main()
