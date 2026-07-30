#include <cstddef>
#include <cstdint>
#include <cstring>

#include "voltage_drop.h"

static wire_awg_t pick_gauge(uint8_t v) {
    switch (v % 8) {
        case 0: return WIRE_AWG_4_0;
        case 1: return WIRE_AWG_3_0;
        case 2: return WIRE_AWG_2_0;
        case 3: return WIRE_AWG_1_0;
        case 4: return WIRE_AWG_6;
        case 5: return WIRE_AWG_8;
        case 6: return WIRE_AWG_10;
        default: return WIRE_AWG_12;
    }
}

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size < 32) return 0;

    double system_voltage = 400.0;
    double current = 10.0;
    double length_mm = 20000.0;
    wire_awg_t gauge = WIRE_AWG_10;
    std::memcpy(&system_voltage, data, sizeof(double));
    std::memcpy(&current, data + 8, sizeof(double));
    std::memcpy(&length_mm, data + 16, sizeof(double));
    if (size >= 24) gauge = pick_gauge(data[24]);
    if (system_voltage <= 0.0) system_voltage = 400.0;
    if (current <= 0.0) current = 10.0;
    if (length_mm <= 0.0) length_mm = 20000.0;

    solar_voltage_drop_result_t result;
    solar_voltage_drop_calculate(gauge, "CU", length_mm, current, system_voltage, &result);

    wire_awg_t suggested;
    double actual_percent;
    solar_voltage_drop_suggest_gauge(length_mm, current, system_voltage, 3.0, WIRE_MATERIAL_COPPER, &suggested, &actual_percent);
    return 0;
}
