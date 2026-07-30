#!/usr/bin/env python3
"""Generate binary seed files for the new native fuzzers."""

import struct
import os

CORPUS_ROOT = os.path.join(os.path.dirname(__file__), "corpus")


def write_seed(name, index, data):
    d = os.path.join(CORPUS_ROOT, name)
    os.makedirs(d, exist_ok=True)
    # Pad to at least 512 bytes so the fuzzer sees enough data even if
    # the C struct is larger than our manual layout.
    if len(data) < 512:
        data = data + b"\x00" * (512 - len(data))
    with open(os.path.join(d, f"seed{index}.bin"), "wb") as f:
        f.write(data)


def panel_definition():
    """Return a raw byte layout matching solar_panel_definition_t."""
    id_high = 0x11111111
    id_low = 0x111111110002
    owner_high = 0
    owner_low = 0
    # Pad with zeros to reach a reasonable size; fuzzer fills in strings.
    data = struct.pack("<QQ", id_high, id_low)
    data += struct.pack("<QQ", owner_high, owner_low)
    data += b"Generic\x00" + b"\x00" * (64 - 8)
    data += b"400 W\x00" + b"\x00" * (64 - 6)
    data += struct.pack("<d", 400.0)  # pmax
    data += struct.pack("<d", 31.25)  # vmp
    data += struct.pack("<d", 12.80)  # imp
    data += struct.pack("<d", 37.1)   # voc
    data += struct.pack("<d", 13.50)  # isc
    data += struct.pack("<d", 1134.0) # width
    data += struct.pack("<d", 1722.0) # height
    data += struct.pack("<d", 35.0)   # depth
    data += struct.pack("<d", -0.28)  # voc temp coeff
    data += struct.pack("<d", -0.35) # pmax temp coeff
    data += b"MC4-compatible\x00" + b"\x00" * (32 - 15)
    data += struct.pack("<d", 1000.0) # positive lead
    data += struct.pack("<d", 1000.0) # negative lead
    data += struct.pack("<B", 0)      # is_custom
    # Pad to sizeof(struct)
    data += b"\x00" * (256 - len(data))
    return data


def site_conditions():
    data = b"Temperate\x00" + b"\x00" * (64 - 10)
    data += struct.pack("<d", -10.0)  # min ambient
    data += struct.pack("<d", 70.0)   # hot cell
    data += struct.pack("<d", 4.5)    # psh
    data += struct.pack("<d", 0.85)   # derate
    data += struct.pack("<d", 20.0)   # tilt
    data += struct.pack("<d", 180.0)  # azimuth
    data += b"\x00" * (256 - len(data))
    return data


def inverter_specs():
    data = struct.pack("<QQ", 0xa1111111, 0x0000000000000001)
    data += struct.pack("<d", 5000.0)  # ac rated
    data += struct.pack("<i", 2)       # mppt count
    data += struct.pack("<d", 80.0)    # min mppt
    data += struct.pack("<d", 480.0)   # max mppt
    data += struct.pack("<d", 600.0)   # max dc
    data += struct.pack("<d", 12.5)    # max current per mppt
    data += struct.pack("<d", 4000.0)  # max dc power per mppt
    data += b"\x00" * (256 - len(data))
    return data


def equipment_port():
    data = struct.pack("<QQ", 0, 0x1001)  # id
    data += struct.pack("<QQ", 0, 1)       # owner_id
    data += struct.pack("<I", 0)          # type
    data += struct.pack("<I", 0)          # polarity
    data += b"MC4-compatible\x00" + b"\x00" * (32 - 15)  # connector family
    data += struct.pack("<I", 0)          # interface_type
    data += struct.pack("<QQ", 0, 0)      # connection_id
    data += struct.pack("<B", 0)          # is_occupied
    data += b"BAT+\x00" + b"\x00" * (32 - 5)  # label
    data += struct.pack("<i", 10)          # port_type
    data += b"\x00" * (128 - len(data))
    return data


def racking_layout():
    data = struct.pack("<Q", 4)           # rail_count
    data += struct.pack("<d", 12000.0)    # total rail length
    data += struct.pack("<Q", 2)          # row_count
    data += struct.pack("<Q", 16)         # attachment_count
    data += struct.pack("<Q", 8)          # end_clamp_count
    data += struct.pack("<Q", 24)         # mid_clamp_count
    data += b"\x00" * (256 - len(data))
    return data


def main():
    # temperature_derating_fuzzer
    for i in range(1, 4):
        write_seed("temperature_derating_fuzzer", i, panel_definition() + site_conditions())

    # string_sizing_fuzzer
    for i in range(1, 4):
        write_seed("string_sizing_fuzzer", i, panel_definition() + inverter_specs() + site_conditions())

    # mppt_compatibility_fuzzer
    for i in range(1, 4):
        write_seed("mppt_compatibility_fuzzer", i, inverter_specs())

    # connection_validator_fuzzer
    for i in range(1, 4):
        write_seed("connection_validator_fuzzer", i, equipment_port() + equipment_port())

    # bom_schedule_fuzzer
    for i in range(1, 4):
        write_seed("bom_schedule_fuzzer", i, racking_layout())

    # wire_gauge_format_fuzzer
    for i in range(1, 4):
        data = struct.pack("<i", 10)        # gauge code
        data += struct.pack("<d", 8.0)       # current
        data += struct.pack("<d", 15000.0)   # length
        write_seed("wire_gauge_format_fuzzer", i, data)

    print("Seed corpus generated.")


if __name__ == "__main__":
    main()
