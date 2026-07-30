#include "site_conditions.h"

#include <cctype>
#include <cmath>
#include <cstring>

static double clamp_double(double value, double min_val, double max_val) {
    if (value < min_val) return min_val;
    if (value > max_val) return max_val;
    return value;
}

static bool str_equals_ignore_case(const char *a, const char *b) {
    if (!a || !b) return a == b;
    while (*a && *b) {
        if (std::tolower(static_cast<unsigned char>(*a)) !=
            std::tolower(static_cast<unsigned char>(*b))) {
            return false;
        }
        ++a;
        ++b;
    }
    return *a == '\0' && *b == '\0';
}

static void copy_string(char *dest, size_t dest_size, const char *src) {
    if (!dest || dest_size == 0) return;
    if (!src) {
        dest[0] = '\0';
        return;
    }
    size_t len = std::strlen(src);
    if (len >= dest_size) len = dest_size - 1;
    std::memcpy(dest, src, len);
    dest[len] = '\0';
}

static const solar_site_climate_preset_t s_presets[SOLAR_SITE_PRESET_COUNT] = {
    {
        "sydney",
        "Sydney, AU",
        true, -33.87,
        true, 151.21,
        2.0,
        70.0,
        4.5,
        0.85
    },
    {
        "melbourne",
        "Melbourne, AU",
        true, -37.81,
        true, 144.96,
        -2.0,
        68.0,
        4.1,
        0.85
    },
    {
        "brisbane",
        "Brisbane, AU",
        true, -27.47,
        true, 153.03,
        5.0,
        75.0,
        5.0,
        0.85
    },
    {
        "phoenix",
        "Phoenix, AZ",
        true, 33.45,
        true, -112.07,
        -5.0,
        85.0,
        6.5,
        0.85
    },
    {
        "minneapolis",
        "Minneapolis, MN",
        true, 44.98,
        true, -93.27,
        -30.0,
        65.0,
        4.2,
        0.85
    },
    {
        "temperate",
        "Temperate default",
        false, 0.0,
        false, 0.0,
        -10.0,
        70.0,
        4.5,
        0.85
    }
};

double solar_site_standard_test_celsius(void) { return 25.0; }

double solar_site_default_voc_temp_coeff_percent_per_c(void) { return -0.30; }

double solar_site_default_pmax_temp_coeff_percent_per_c(void) { return -0.35; }

double solar_site_default_peak_sun_hours_per_day(void) { return 4.5; }

double solar_site_default_system_derate_factor(void) { return 0.85; }

void solar_site_design_conditions_init(solar_site_design_conditions_t *conditions) {
    if (!conditions) return;
    std::memset(conditions, 0, sizeof(*conditions));
    copy_string(conditions->location_name, SOLAR_SITE_LOCATION_NAME_LEN, "Unspecified");
    conditions->has_latitude = false;
    conditions->has_longitude = false;
    conditions->min_ambient_celsius = -10.0;
    conditions->hot_cell_celsius = 70.0;
    conditions->peak_sun_hours_per_day = solar_site_default_peak_sun_hours_per_day();
    conditions->system_derate_factor = solar_site_default_system_derate_factor();
    conditions->array_tilt_degrees = 20.0;
    conditions->array_azimuth_degrees = 180.0;
}

void solar_site_design_conditions_clone(
    const solar_site_design_conditions_t *source,
    solar_site_design_conditions_t *dest) {
    if (!dest) return;
    if (!source) {
        solar_site_design_conditions_init(dest);
        return;
    }
    std::memcpy(dest, source, sizeof(*dest));
}

bool solar_site_conditions_apply_preset_to_design(
    solar_site_design_conditions_t *conditions,
    const solar_site_climate_preset_t *preset) {
    if (!conditions || !preset) return false;

    copy_string(conditions->location_name, SOLAR_SITE_LOCATION_NAME_LEN, preset->display_name);
    conditions->has_latitude = preset->has_latitude;
    conditions->latitude_degrees = preset->latitude_degrees;
    conditions->has_longitude = preset->has_longitude;
    conditions->longitude_degrees = preset->longitude_degrees;
    conditions->min_ambient_celsius = preset->min_ambient_celsius;
    conditions->hot_cell_celsius = preset->hot_cell_celsius;
    conditions->peak_sun_hours_per_day = preset->peak_sun_hours_per_day;
    conditions->system_derate_factor = preset->system_derate_factor;

    if (preset->has_latitude && preset->latitude_degrees < 0.0) {
        conditions->array_azimuth_degrees = 0.0;
    } else {
        conditions->array_azimuth_degrees = 180.0;
    }

    if (preset->has_latitude) {
        conditions->array_tilt_degrees = clamp_double(
            std::fabs(preset->latitude_degrees), 5.0, 40.0);
    }

    return true;
}

const solar_site_climate_preset_t *solar_site_conditions_climate_preset_by_id(const char *id) {
    if (!id) return NULL;
    for (size_t i = 0; i < SOLAR_SITE_PRESET_COUNT; i++) {
        if (str_equals_ignore_case(s_presets[i].id, id)) {
            return &s_presets[i];
        }
    }
    return NULL;
}

const solar_site_climate_preset_t *solar_site_climate_preset_at(size_t index) {
    if (index >= SOLAR_SITE_PRESET_COUNT) return NULL;
    return &s_presets[index];
}

size_t solar_site_climate_preset_count(void) {
    return SOLAR_SITE_PRESET_COUNT;
}

double solar_site_estimate_annual_kwh(double stc_kw, double peak_sun_hours_per_day, double derate_factor) {
    if (!std::isfinite(stc_kw) || stc_kw < 0.0) return 0.0;
    if (!std::isfinite(peak_sun_hours_per_day) || peak_sun_hours_per_day < 0.0) return 0.0;
    if (!std::isfinite(derate_factor) || derate_factor < 0.0) derate_factor = 0.0;
    if (derate_factor > 1.0) derate_factor = 1.0;
    return stc_kw * peak_sun_hours_per_day * 365.0 * derate_factor;
}

double solar_site_normalize_tilt(double degrees) {
    if (!std::isfinite(degrees)) return 0.0;
    double n = std::fmod(degrees, 360.0);
    if (n < -90.0) n = -90.0;
    if (n > 90.0) n = 90.0;
    return n;
}

double solar_site_normalize_azimuth(double degrees) {
    if (!std::isfinite(degrees)) return 0.0;
    double n = std::fmod(degrees, 360.0);
    if (n < 0.0) n += 360.0;
    return n;
}
