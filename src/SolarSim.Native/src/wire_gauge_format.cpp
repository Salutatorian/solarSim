#include "wire_gauge_format.h"

#include <cstddef>
#include <cstdio>
#include <cstring>
#include <cmath>

static void copy_string(char *dest, size_t dest_size, const char *src) {
    if (!src || dest_size == 0) return;
    size_t len = std::strlen(src);
    if (len >= dest_size) len = dest_size - 1;
    std::memcpy(dest, src, len);
    dest[len] = '\0';
}

static int case_insensitive_contains(const char *haystack, const char *needle) {
    if (!haystack || !needle) return 0;
    size_t needle_len = std::strlen(needle);
    if (needle_len == 0) return 1;
    for (size_t i = 0; haystack[i]; i++) {
        size_t j = 0;
        for (; j < needle_len; j++) {
            char a = haystack[i + j];
            char b = needle[j];
            if (a >= 'A' && a <= 'Z') a += 'a' - 'A';
            if (b >= 'A' && b <= 'Z') b += 'a' - 'A';
            if (a != b) break;
        }
        if (j == needle_len) return 1;
    }
    return 0;
}

static const solar_wire_gauge_awg_t k_battery_gauges[] = {
    SOLAR_AWG_1_0,
    SOLAR_AWG_2_0,
    SOLAR_AWG_3_0,
    SOLAR_AWG_4_0,
};

static const solar_wire_gauge_awg_t k_pv_string_gauges[] = {
    SOLAR_AWG_6,
    SOLAR_AWG_8,
    SOLAR_AWG_10,
    SOLAR_AWG_12,
};

static const solar_wire_gauge_awg_t k_all_gauges[] = {
    SOLAR_AWG_4_0,
    SOLAR_AWG_3_0,
    SOLAR_AWG_2_0,
    SOLAR_AWG_1_0,
    SOLAR_AWG_6,
    SOLAR_AWG_8,
    SOLAR_AWG_10,
    SOLAR_AWG_12,
};

const solar_wire_gauge_awg_t *solar_wire_battery_cable_gauges(size_t *out_count) {
    if (out_count) *out_count = sizeof(k_battery_gauges) / sizeof(k_battery_gauges[0]);
    return k_battery_gauges;
}

const solar_wire_gauge_awg_t *solar_wire_pv_string_gauges(size_t *out_count) {
    if (out_count) *out_count = sizeof(k_pv_string_gauges) / sizeof(k_pv_string_gauges[0]);
    return k_pv_string_gauges;
}

const solar_wire_gauge_awg_t *solar_wire_all_gauges(size_t *out_count) {
    if (out_count) *out_count = sizeof(k_all_gauges) / sizeof(k_all_gauges[0]);
    return k_all_gauges;
}

const char *solar_wire_gauge_to_display(solar_wire_gauge_awg_t gauge, char *buffer, size_t buffer_size) {
    if (!buffer || buffer_size == 0) return "";
    switch (gauge) {
        case SOLAR_AWG_4_0:
            copy_string(buffer, buffer_size, "4/0");
            break;
        case SOLAR_AWG_3_0:
            copy_string(buffer, buffer_size, "3/0");
            break;
        case SOLAR_AWG_2_0:
            copy_string(buffer, buffer_size, "2/0");
            break;
        case SOLAR_AWG_1_0:
            copy_string(buffer, buffer_size, "1/0");
            break;
        case SOLAR_AWG_6:
        case SOLAR_AWG_8:
        case SOLAR_AWG_10:
        case SOLAR_AWG_12:
            std::snprintf(buffer, buffer_size, "%d AWG", static_cast<int>(gauge));
            break;
        default:
            copy_string(buffer, buffer_size, "Unknown");
            break;
    }
    return buffer;
}

bool solar_wire_gauge_from_display(const char *text, solar_wire_gauge_awg_t *out_gauge) {
    if (!text || !out_gauge) return false;
    if (case_insensitive_contains(text, "4/0") || case_insensitive_contains(text, "0000")) {
        *out_gauge = SOLAR_AWG_4_0;
        return true;
    }
    if (case_insensitive_contains(text, "3/0") || case_insensitive_contains(text, "000")) {
        *out_gauge = SOLAR_AWG_3_0;
        return true;
    }
    if (case_insensitive_contains(text, "2/0") || case_insensitive_contains(text, "00")) {
        *out_gauge = SOLAR_AWG_2_0;
        return true;
    }
    if (case_insensitive_contains(text, "1/0") || case_insensitive_contains(text, "0")) {
        *out_gauge = SOLAR_AWG_1_0;
        return true;
    }

    int code = 0;
    if (std::sscanf(text, "%d", &code) == 1) {
        solar_wire_gauge_awg_t gauge = solar_wire_gauge_from_int(code);
        if (solar_wire_gauge_is_valid(gauge)) {
            *out_gauge = gauge;
            return true;
        }
    }
    return false;
}

solar_wire_gauge_awg_t solar_wire_gauge_from_int(int code) {
    switch (code) {
        case -40: return SOLAR_AWG_4_0;
        case -30: return SOLAR_AWG_3_0;
        case -20: return SOLAR_AWG_2_0;
        case -10: return SOLAR_AWG_1_0;
        case 6: return SOLAR_AWG_6;
        case 8: return SOLAR_AWG_8;
        case 10: return SOLAR_AWG_10;
        case 12: return SOLAR_AWG_12;
        default: return SOLAR_AWG_INVALID;
    }
}

bool solar_wire_gauge_is_valid(solar_wire_gauge_awg_t gauge) {
    for (size_t i = 0; i < sizeof(k_all_gauges) / sizeof(k_all_gauges[0]); i++) {
        if (k_all_gauges[i] == gauge) return true;
    }
    return false;
}

int solar_wire_gauge_compare(solar_wire_gauge_awg_t a, solar_wire_gauge_awg_t b) {
    /* Smaller AWG number = larger conductor. For kcmil-style negatives, -40 (4/0) is largest. */
    int ia = static_cast<int>(a);
    int ib = static_cast<int>(b);
    if (ia < 0) ia = -ia + 100;
    if (ib < 0) ib = -ib + 100;
    if (ia < ib) return 1;
    if (ia > ib) return -1;
    return 0;
}

double solar_wire_copper_ohms_per_1000ft(solar_wire_gauge_awg_t gauge) {
    switch (gauge) {
        case SOLAR_AWG_4_0: return 0.0490;
        case SOLAR_AWG_3_0: return 0.0618;
        case SOLAR_AWG_2_0: return 0.0779;
        case SOLAR_AWG_1_0: return 0.0983;
        case SOLAR_AWG_6: return 0.491;
        case SOLAR_AWG_8: return 0.778;
        case SOLAR_AWG_10: return 1.24;
        case SOLAR_AWG_12: return 1.98;
        default: return 0.0;
    }
}

double solar_wire_aluminum_ohms_per_1000ft(solar_wire_gauge_awg_t gauge) {
    double copper = solar_wire_copper_ohms_per_1000ft(gauge);
    if (copper <= 0.0) return 0.0;
    return copper * 1.6;
}

double solar_wire_copper_ampacity_amps(solar_wire_gauge_awg_t gauge) {
    /* Approximate 75°C copper ampacities, rounded down for design-aid use. */
    switch (gauge) {
        case SOLAR_AWG_4_0: return 230.0;
        case SOLAR_AWG_3_0: return 200.0;
        case SOLAR_AWG_2_0: return 175.0;
        case SOLAR_AWG_1_0: return 150.0;
        case SOLAR_AWG_6: return 65.0;
        case SOLAR_AWG_8: return 40.0;
        case SOLAR_AWG_10: return 30.0;
        case SOLAR_AWG_12: return 20.0;
        default: return 0.0;
    }
}

solar_wire_gauge_awg_t solar_wire_recommend_pv_string_gauge(double current_amps, double one_way_length_mm) {
    if (!std::isfinite(current_amps) || current_amps < 0.0) current_amps = 0.0;
    if (!std::isfinite(one_way_length_mm) || one_way_length_mm < 0.0) one_way_length_mm = 0.0;

    /* Long runs and higher currents push toward larger conductors. */
    size_t count = 0;
    const solar_wire_gauge_awg_t *gauges = solar_wire_pv_string_gauges(&count);
    if (count == 0) return SOLAR_AWG_10;

    solar_wire_gauge_awg_t chosen = gauges[count - 1]; /* smallest default */
    for (size_t i = 0; i < count; i++) {
        double ampacity = solar_wire_copper_ampacity_amps(gauges[i]);
        if (ampacity <= 0.0) continue;
        if (current_amps <= ampacity * 0.8) {
            chosen = gauges[i];
            break;
        }
    }

    /* Boost one size if the run is long and current is meaningful. */
    if (one_way_length_mm > 30000.0 && current_amps > 5.0) {
        for (size_t i = 0; i < count; i++) {
            if (gauges[i] == chosen && i > 0) {
                chosen = gauges[i - 1];
                break;
            }
        }
    }
    return chosen;
}

solar_wire_gauge_awg_t solar_wire_recommend_battery_gauge(double continuous_amps, double peak_amps) {
    if (!std::isfinite(continuous_amps) || continuous_amps < 0.0) continuous_amps = 0.0;
    if (!std::isfinite(peak_amps) || peak_amps < continuous_amps) peak_amps = continuous_amps;

    size_t count = 0;
    const solar_wire_gauge_awg_t *gauges = solar_wire_battery_cable_gauges(&count);
    if (count == 0) return SOLAR_AWG_1_0;

    solar_wire_gauge_awg_t chosen = gauges[count - 1];
    for (size_t i = 0; i < count; i++) {
        double ampacity = solar_wire_copper_ampacity_amps(gauges[i]);
        if (ampacity <= 0.0) continue;
        if (peak_amps <= ampacity * 0.8) {
            chosen = gauges[i];
            break;
        }
    }
    return chosen;
}

const char *solar_wire_properties_to_display(
    const solar_wire_properties_t *props,
    char *buffer,
    size_t buffer_size) {
    if (!props || !buffer || buffer_size == 0) return "";
    char gauge_str[32];
    solar_wire_gauge_to_display(props->gauge, gauge_str, sizeof(gauge_str));
    const char *material = props->material[0] ? props->material : "Copper";
    const char *type = props->wire_type[0] ? props->wire_type : "PV wire";
    const char *color = props->color[0] ? props->color : "Black";
    std::snprintf(buffer, buffer_size, "%s %s %s (%s)", gauge_str, material, type, color);
    return buffer;
}

double solar_wire_circular_mils(solar_wire_gauge_awg_t gauge) {
    /* Approximate circular-mil area (CM). Values are rounded design aids. */
    switch (gauge) {
        case SOLAR_AWG_4_0: return 211600.0;
        case SOLAR_AWG_3_0: return 167800.0;
        case SOLAR_AWG_2_0: return 133100.0;
        case SOLAR_AWG_1_0: return 105600.0;
        case SOLAR_AWG_6: return 26240.0;
        case SOLAR_AWG_8: return 16510.0;
        case SOLAR_AWG_10: return 10380.0;
        case SOLAR_AWG_12: return 6530.0;
        default: return 0.0;
    }
}

double solar_wire_voltage_drop(
    solar_wire_gauge_awg_t gauge,
    const char *material,
    double one_way_length_mm,
    double current_amps,
    double *out_resistance_ohms) {
    if (!std::isfinite(one_way_length_mm) || one_way_length_mm < 0.0) one_way_length_mm = 0.0;
    if (!std::isfinite(current_amps) || current_amps < 0.0) current_amps = 0.0;

    double ohms_per_1000ft = solar_wire_copper_ohms_per_1000ft(gauge);
    if (ohms_per_1000ft <= 0.0) return 0.0;
    if (material && case_insensitive_contains(material, "alum")) {
        ohms_per_1000ft = solar_wire_aluminum_ohms_per_1000ft(gauge);
    }

    double one_way_feet = one_way_length_mm / 25.4 / 12.0;
    double circuit_feet = one_way_feet * 2.0;
    double resistance = ohms_per_1000ft * (circuit_feet / 1000.0);
    if (out_resistance_ohms) *out_resistance_ohms = resistance;
    return current_amps * resistance;
}

double solar_wire_temperature_factor(double ambient_celsius) {
    /* Simplified NEC-style temperature correction factor for 75°C rated conductors. */
    if (ambient_celsius <= 30.0) return 1.0;
    if (ambient_celsius <= 40.0) return 0.91;
    if (ambient_celsius <= 45.0) return 0.87;
    if (ambient_celsius <= 50.0) return 0.82;
    if (ambient_celsius <= 55.0) return 0.76;
    if (ambient_celsius <= 60.0) return 0.71;
    return 0.65;
}

double solar_wire_adjusted_ampacity(solar_wire_gauge_awg_t gauge, double ambient_celsius, const char *material) {
    double base = solar_wire_copper_ampacity_amps(gauge);
    if (material && case_insensitive_contains(material, "alum")) {
        base = base * 0.8; /* Rough aluminum adjustment. */
    }
    return base * solar_wire_temperature_factor(ambient_celsius);
}

solar_wire_gauge_awg_t solar_wire_recommend_ac_gauge(double current_amps, double one_way_length_mm, double max_drop_pct) {
    if (!std::isfinite(max_drop_pct) || max_drop_pct <= 0.0) max_drop_pct = 3.0;
    size_t count = 0;
    const solar_wire_gauge_awg_t *gauges = solar_wire_all_gauges(&count);
    if (count == 0) return SOLAR_AWG_10;

    solar_wire_gauge_awg_t chosen = gauges[count - 1];
    for (size_t i = 0; i < count; i++) {
        double drop = solar_wire_voltage_drop(gauges[i], "Copper", one_way_length_mm, current_amps, NULL);
        double nominal_voltage = 240.0;
        double pct = (drop / nominal_voltage) * 100.0;
        if (pct <= max_drop_pct) {
            chosen = gauges[i];
            break;
        }
    }
    return chosen;
}

const char *solar_wire_recommend_conduit_size(
    solar_wire_gauge_awg_t gauge,
    size_t conductor_count,
    char *buffer,
    size_t buffer_size) {
    if (!buffer || buffer_size == 0) return "";
    if (conductor_count == 0) {
        copy_string(buffer, buffer_size, "—");
        return buffer;
    }
    double area = solar_wire_circular_mils(gauge) * static_cast<double>(conductor_count) / 1000.0;
    if (area < 100.0) {
        std::snprintf(buffer, buffer_size, "3/4\"");
    } else if (area < 200.0) {
        std::snprintf(buffer, buffer_size, "1\"");
    } else if (area < 350.0) {
        std::snprintf(buffer, buffer_size, "1-1/4\"");
    } else if (area < 600.0) {
        std::snprintf(buffer, buffer_size, "1-1/2\"");
    } else {
        std::snprintf(buffer, buffer_size, "2\" or larger");
    }
    return buffer;
}

