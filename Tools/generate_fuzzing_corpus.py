#!/usr/bin/env python3
"""Generate seed corpus and PoCs for all solarSim native fuzzers."""

import os
import struct

BASE = "fuzz"
CORPUS_BASE = os.path.join(BASE, "corpus")
POC_BASE = os.path.join(BASE, "poc")


def ensure_dirs() -> None:
    for target in [
        "solar_module_catalog_fuzzer",
        "project_file_fuzzer",
        "roof_geometry_fuzzer",
        "wire_route_fuzzer",
    ]:
        os.makedirs(os.path.join(CORPUS_BASE, target), exist_ok=True)
    os.makedirs(POC_BASE, exist_ok=True)


# ---------------------------------------------------------------------------
# Solar Module Catalog (SMC)
# ---------------------------------------------------------------------------

def make_smc(name: bytes, electrical: tuple, width: int, height: int, tags: list[bytes] = None) -> bytes:
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


def generate_smc_corpus() -> None:
    out_dir = os.path.join(CORPUS_BASE, "solar_module_catalog_fuzzer")
    corpus = [
        ("seed1_boviet_270w.smc", b"Boviet 270 W", (270.0, 31.2, 8.65, 38.1, 9.20, -0.34), 1134, 1900, [b"mono", b"60cell"]),
        ("seed2_long_name.smc", b"A" * 200, (450.0, 41.0, 10.98, 49.5, 11.60, -0.30), 1134, 1900, []),
        ("seed3_boundary_name.smc", b"X" * 255, (300.0, 32.5, 9.23, 40.1, 9.85, -0.33), 1000, 1700, [b"boundary"]),
    ]
    for filename, name, elec, w, h, tags in corpus:
        with open(os.path.join(out_dir, filename), "wb") as f:
            f.write(make_smc(name, elec, w, h, tags))

    # PoC: name_len == 256 triggers off-by-one stack write.
    with open(os.path.join(POC_BASE, "poc_smc_name_overflow.smc"), "wb") as f:
        f.write(make_smc(b"A" * 256, (270.0, 31.2, 8.65, 38.1, 9.20, -0.34), 1134, 1900, [b"poc"]))


# ---------------------------------------------------------------------------
# Project file (SOLP)
# ---------------------------------------------------------------------------

def write_fixed_string(s: str, max_len: int) -> bytes:
    encoded = s.encode("utf-8")[:max_len]
    return struct.pack("<I", len(encoded)) + encoded


def raw_fixed_string(s: str, max_len: int) -> bytes:
    encoded = s.encode("utf-8")[:max_len]
    return encoded + b"\x00" * (max_len - len(encoded))


def make_solp(
    site_name: str = "Fuzz Site",
    location: str = "Test City",
    climate: str = "temperate",
    panel_count: int = 0,
    connection_count: int = 0,
    surface_count: int = 0,
    obstacle_count: int = 0,
) -> bytes:
    magic = b"SOLP"
    version = 1
    flags = 0
    header = (
        magic
        + struct.pack("<IIIIII", version, flags, panel_count, connection_count, surface_count, obstacle_count)
        + struct.pack("<I", 0)  # tag_count
        + struct.pack("<dddddddd", -33.0, 151.0, 25.0, 60.0, 4.5, 0.86, 22.0, 180.0)
        + raw_fixed_string(site_name, 128)
        + raw_fixed_string(location, 128)
        + raw_fixed_string(climate, 64)
    )
    # Header must match the C struct size (4 + 7*4 + 8*8 + 128 + 128 + 64 = 416 bytes).
    assert len(header) <= 416, f"header too big: {len(header)}"
    header = header + b"\x00" * (416 - len(header))

    # Definitions.
    def_id = (0, 0x111111110001)
    def_bytes = struct.pack("<I", 1)
    def_bytes += (
        struct.pack("<QQ", *def_id)
        + write_fixed_string("Boviet 270 W", 256)
        + write_fixed_string("Boviet", 64)
        + write_fixed_string("270 W", 64)
        + write_fixed_string("MC4-compatible", 32)
        + struct.pack("<dddddddddd", 270.0, 31.2, 8.65, 38.1, 9.20, -0.34, 992.0, 1640.0, 35.0, -0.28)
        + struct.pack("<dddI", -0.36, 1000.0, 1000.0, 0)
    )

    # Panels. Each panel has two ports: positive=base+0, negative=base+1.
    graph_bytes = struct.pack("<I", panel_count)
    for i in range(panel_count):
        panel_id = (0, 0x1000 + i)
        graph_bytes += struct.pack("<QQQQddI", *panel_id, *def_id, i * 1000.0, 0.0, 0)

    # Connections: connect panel i negative to panel i+1 positive (series).
    graph_bytes += struct.pack("<I", connection_count)
    for i in range(connection_count):
        start_port = (0, 0x1000 + 2 * i + 1)  # negative of panel i
        end_port = (0, 0x1000 + 2 * (i + 1))  # positive of panel i+1
        graph_bytes += struct.pack("<QQQQdI", *start_port, *end_port, 1500.0, 10)

    # Roof.
    roof_bytes = struct.pack("<I", surface_count)
    for i in range(surface_count):
        roof_bytes += write_fixed_string(f"Roof{i}", 64)
        roof_bytes += struct.pack("<d", 300.0)
        roof_bytes += struct.pack("<I", 4)
        corners = [(0.0, 0.0), (10000.0, 0.0), (10000.0, 8000.0), (0.0, 8000.0)]
        for x, y in corners:
            roof_bytes += struct.pack("<dd", x, y)

    roof_bytes += struct.pack("<I", obstacle_count)
    for i in range(obstacle_count):
        roof_bytes += write_fixed_string(f"Obstacle{i}", 64)
        roof_bytes += struct.pack("<I", 4)
        corners = [(2000.0, 2000.0), (3000.0, 2000.0), (3000.0, 3000.0), (2000.0, 3000.0)]
        for x, y in corners:
            roof_bytes += struct.pack("<dd", x, y)

    return header + def_bytes + graph_bytes + roof_bytes


def generate_project_file_corpus() -> None:
    out_dir = os.path.join(CORPUS_BASE, "project_file_fuzzer")
    samples = [
        ("seed1_empty_project.solp", 0, 0, 0, 0),
        ("seed2_two_panels.solp", 2, 1, 1, 0),
        ("seed3_with_obstacle.solp", 3, 2, 1, 1),
    ]
    for filename, panels, conns, surfaces, obstacles in samples:
        with open(os.path.join(out_dir, filename), "wb") as f:
            f.write(make_solp(panel_count=panels, connection_count=conns, surface_count=surfaces, obstacle_count=obstacles))


# ---------------------------------------------------------------------------
# Roof geometry
# ---------------------------------------------------------------------------

def make_roof_polygon(vertices: list[tuple[float, float]]) -> bytes:
    data = struct.pack("<I", len(vertices))
    data += b"\x00\x00\x00\x00"  # padding
    for x, y in vertices:
        data += struct.pack("<dd", x, y)
    return data


def generate_roof_geometry_corpus() -> None:
    out_dir = os.path.join(CORPUS_BASE, "roof_geometry_fuzzer")
    samples = [
        ("seed1_triangle.bin", [(0.0, 0.0), (5000.0, 0.0), (2500.0, 4000.0)]),
        ("seed2_rectangle.bin", [(0.0, 0.0), (10000.0, 0.0), (10000.0, 8000.0), (0.0, 8000.0)]),
        ("seed3_l_shape.bin", [(0.0, 0.0), (6000.0, 0.0), (6000.0, 3000.0), (10000.0, 3000.0), (10000.0, 8000.0), (0.0, 8000.0)]),
    ]
    for filename, vertices in samples:
        with open(os.path.join(out_dir, filename), "wb") as f:
            f.write(make_roof_polygon(vertices))


# ---------------------------------------------------------------------------
# Wire route
# ---------------------------------------------------------------------------

def make_wire_route(points: list[tuple[float, float]], gauge: int = 10, current: int = 8) -> bytes:
    data = struct.pack("<BB", gauge, current)
    data += b"\x00\x00"  # padding
    for x, y in points:
        data += struct.pack("<dd", x, y)
    return data


def generate_wire_route_corpus() -> None:
    out_dir = os.path.join(CORPUS_BASE, "wire_route_fuzzer")
    samples = [
        ("seed1_short.bin", [(0.0, 0.0), (1000.0, 0.0)]),
        ("seed2_ortho.bin", [(0.0, 0.0), (500.0, 0.0), (500.0, 300.0), (1000.0, 300.0)]),
        ("seed3_long.bin", [(0.0, 0.0), (5000.0, 0.0), (5000.0, 2000.0), (10000.0, 2000.0)]),
    ]
    for filename, points in samples:
        with open(os.path.join(out_dir, filename), "wb") as f:
            f.write(make_wire_route(points, gauge=10, current=8))


def main() -> None:
    ensure_dirs()
    generate_smc_corpus()
    generate_project_file_corpus()
    generate_roof_geometry_corpus()
    generate_wire_route_corpus()
    print("Generated seed corpora and PoCs in fuzz/corpus/ and fuzz/poc/")


if __name__ == "__main__":
    main()
