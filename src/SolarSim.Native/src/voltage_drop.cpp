#include "voltage_drop.h"

#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <cmath>

static bool is_aluminum(const char *material) {
    if (!material) return false;
    return std::strstr(material, "Aluminum") != NULL ||
           std::strstr(material, "aluminum") != NULL ||
           std::strstr(material, "ALUMINUM") != NULL ||
           std::strstr(material, "Al") != NULL;
}

static void copy_material(char *dest, size_t dest_size, const char *src) {
    if (!src || dest_size == 0) return;
    size_t len = std::strlen(src);
    if (len >= dest_size) len = dest_size - 1;
    std::memcpy(dest, src, len);
    dest[len] = '\0';
}

double solar_voltage_drop_resistance_per_1000ft(wire_awg_t gauge, const char *material) {
    double copper = 0.0;
    switch (gauge) {
        case WIRE_AWG_4_0: copper = 0.0490; break;
        case WIRE_AWG_3_0: copper = 0.0618; break;
        case WIRE_AWG_2_0: copper = 0.0779; break;
        case WIRE_AWG_1_0: copper = 0.0983; break;
        case WIRE_AWG_6: copper = 0.491; break;
        case WIRE_AWG_8: copper = 0.778; break;
        case WIRE_AWG_10: copper = 1.24; break;
        case WIRE_AWG_12: copper = 1.98; break;
        default: copper = 1.24; break;
    }
    if (is_aluminum(material)) {
        return copper * 1.6;
    }
    return copper;
}

void solar_voltage_drop_calculate(
    wire_awg_t gauge,
    const char *material,
    double one_way_length_mm,
    double current_amps,
    double system_voltage_volts,
    solar_voltage_drop_result_t *out_result) {
    if (!out_result) return;
    std::memset(out_result, 0, sizeof(*out_result));

    if (one_way_length_mm < 0.0) one_way_length_mm = 0.0;
    if (current_amps < 0.0) current_amps = 0.0;

    out_result->gauge = gauge;
    out_result->is_estimate = true;
    copy_material(out_result->material, sizeof(out_result->material),
        is_aluminum(material) ? "Aluminum" : "Copper");
    out_result->current_amps = current_amps;
    out_result->one_way_length_mm = one_way_length_mm;
    out_result->circuit_length_mm = one_way_length_mm * 2.0;

    double ohms_per_1000ft = solar_voltage_drop_resistance_per_1000ft(gauge, material);
    double one_way_feet = one_way_length_mm / 25.4 / 12.0;
    double circuit_feet = one_way_feet * 2.0;
    out_result->resistance_ohms = ohms_per_1000ft * (circuit_feet / 1000.0);
    out_result->voltage_drop_volts = current_amps * out_result->resistance_ohms;
    out_result->power_loss_watts = current_amps * current_amps * out_result->resistance_ohms;

    if (system_voltage_volts > 0.0) {
        out_result->percent_drop = (out_result->voltage_drop_volts / system_voltage_volts) * 100.0;
        out_result->has_percent_drop = true;
    } else {
        out_result->has_percent_drop = false;
    }
}

void solar_voltage_drop_calculate_from_route(
    const wire_route_t *route,
    double current_amps,
    double system_voltage_volts,
    solar_voltage_drop_result_t *out_result) {
    if (!route || !out_result) return;
    const char *material = (route->material == WIRE_MATERIAL_ALUMINUM) ? "Aluminum" : "Copper";
    solar_voltage_drop_calculate(
        route->gauge_awg,
        material,
        wire_route_length_mm(route),
        current_amps,
        system_voltage_volts,
        out_result);
}

bool solar_voltage_drop_suggest_gauge(
    double one_way_length_mm,
    double current_amps,
    double system_voltage_volts,
    double max_percent_drop,
    wire_material_t material,
    wire_awg_t *out_gauge,
    double *out_actual_percent) {
    if (!out_gauge || !out_actual_percent) return false;
    if (one_way_length_mm < 0.0 || current_amps < 0.0 || system_voltage_volts <= 0.0 || max_percent_drop <= 0.0) {
        return false;
    }

    const char *mat_str = (material == WIRE_MATERIAL_ALUMINUM) ? "Aluminum" : "Copper";
    wire_awg_t candidates[] = {
        WIRE_AWG_12,
        WIRE_AWG_10,
        WIRE_AWG_8,
        WIRE_AWG_6,
        WIRE_AWG_1_0,
        WIRE_AWG_2_0,
        WIRE_AWG_3_0,
        WIRE_AWG_4_0
    };

    for (size_t i = 0; i < sizeof(candidates) / sizeof(candidates[0]); i++) {
        solar_voltage_drop_result_t result;
        solar_voltage_drop_calculate(
            candidates[i], mat_str, one_way_length_mm, current_amps, system_voltage_volts, &result);
        if (result.has_percent_drop && result.percent_drop <= max_percent_drop) {
            *out_gauge = candidates[i];
            *out_actual_percent = result.percent_drop;
            return true;
        }
    }
    return false;
}

bool solar_voltage_drop_check_ampacity(
    wire_awg_t gauge,
    wire_material_t material,
    double current_amps,
    double ambient_temp_c) {
    if (current_amps < 0.0) return false;
    double base_ampacity = wire_ampacity_amps(gauge, material);
    if (base_ampacity <= 0.0) return false;
    double temp_factor = 1.0;
    if (ambient_temp_c > 30.0) {
        temp_factor = 1.0 - (ambient_temp_c - 30.0) * 0.004;
        if (temp_factor < 0.5) temp_factor = 0.5;
    }
    double adjusted = base_ampacity * temp_factor;
    return current_amps <= adjusted;
}
