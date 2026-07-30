#!/usr/bin/env python3
"""Verify the structure of generated SMC seed and PoC files."""

import struct
import sys

FILES = [
    "fuzz/corpus/solar_module_catalog_fuzzer/seed1_boviet_270w.smc",
    "fuzz/corpus/solar_module_catalog_fuzzer/seed2_long_name.smc",
    "fuzz/corpus/solar_module_catalog_fuzzer/seed3_boundary_name.smc",
    "fuzz/poc/poc_name_overflow.smc",
]


def check(path: str) -> bool:
    data = open(path, "rb").read()
    print(f"{path}: {len(data)} bytes")
    if len(data) < 16:
        print("  ERROR: too short for header")
        return False

    magic = data[:4]
    version, module_count, reserved = struct.unpack("<III", data[4:16])
    print(f"  magic={magic!r} version={version} count={module_count} reserved={reserved}")
    if magic != b"SMC\x00":
        print("  ERROR: bad magic")
        return False
    if version != 1 or module_count != 1 or reserved != 0:
        print("  ERROR: unexpected header values")
        return False

    off = 16
    name_len = struct.unpack("<I", data[off : off + 4])[0]
    off += 4
    if off + name_len > len(data):
        print("  ERROR: name exceeds file")
        return False
    name = data[off : off + name_len]
    off += name_len

    if off + 48 + 8 + 4 > len(data):
        print("  ERROR: truncated electrical/tag header")
        return False
    elec = struct.unpack("<dddddd", data[off : off + 48])
    off += 48
    w, h = struct.unpack("<II", data[off : off + 8])
    off += 8
    tag_count = struct.unpack("<I", data[off : off + 4])[0]
    off += 4
    print(f"  name_len={name_len} name={name[:20]!r}... elec={elec} wh={w}x{h} tags={tag_count}")

    for _ in range(tag_count):
        if off + 4 > len(data):
            print("  ERROR: truncated tag length")
            return False
        tag_len = struct.unpack("<I", data[off : off + 4])[0]
        off += 4
        if off + tag_len > len(data):
            print("  ERROR: tag exceeds file")
            return False
        off += tag_len

    if len(data) != off:
        print(f"  ERROR: trailing bytes at offset {off} (file size {len(data)})")
        return False

    print(f"  OK")
    return True


def main() -> int:
    ok = True
    for path in FILES:
        if not check(path):
            ok = False
    if ok:
        print("\nAll files are structurally valid.")
    else:
        print("\nSome files failed validation.")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
