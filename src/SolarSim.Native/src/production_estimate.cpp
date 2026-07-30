#include "production_estimate.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <cctype>
#include <string>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

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

static constexpr double STANDARD_TEST_CELSIUS = 25.0;
static constexpr double DEFAULT_VOC_TEMP_COEFF_PERCENT_PER_C = -0.30;
static constexpr double DEFAULT_PMAX_TEMP_COEFF_PERCENT_PER_C = -0.35;
static constexpr double DEFAULT_PEAK_SUN_HOURS_PER_DAY = 4.5;
static constexpr double DEFAULT_SYSTEM_DERATE_FACTOR = 0.85;

static constexpr int DAYS_IN_MONTH[12] = {
    31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31
};

static constexpr double NORTHERN_SEASON[12] = {
    0.55, 0.70, 0.90, 1.10, 1.25, 1.30,
    1.28, 1.18, 1.00, 0.80, 0.60, 0.50
};

static const char *MONTH_NAMES[12] = {
    "Jan", "Feb", "Mar", "Apr", "May", "Jun",
    "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
};

static const solar_site_climate_preset_t CLIMATE_PRESETS[] = {
    {
        "sydney", "Sydney, AU",
        -33.87, 151.21, true, true,
        2.0, 70.0, 4.5, DEFAULT_SYSTEM_DERATE_FACTOR
    },
    {
        "melbourne", "Melbourne, AU",
        -37.81, 144.96, true, true,
        -2.0, 68.0, 4.1, DEFAULT_SYSTEM_DERATE_FACTOR
    },
    {
        "brisbane", "Brisbane, AU",
        -27.47, 153.03, true, true,
        5.0, 75.0, 5.0, DEFAULT_SYSTEM_DERATE_FACTOR
    },
    {
        "phoenix", "Phoenix, AZ",
        33.45, -112.07, true, true,
        -5.0, 85.0, 6.5, DEFAULT_SYSTEM_DERATE_FACTOR
    },
    {
        "minneapolis", "Minneapolis, MN",
        44.98, -93.27, true, true,
        -30.0, 65.0, 4.2, DEFAULT_SYSTEM_DERATE_FACTOR
    },
    {
        "temperate", "Temperate default",
        0.0, 0.0, false, false,
        -10.0, 70.0, 4.5, DEFAULT_SYSTEM_DERATE_FACTOR
    }
};

static constexpr size_t PRESET_COUNT =
    sizeof(CLIMATE_PRESETS) / sizeof(CLIMATE_PRESETS[0]);

static double clamp_double(double value, double low, double high) {
    if (value < low) return low;
    if (value > high) return high;
    return value;
}

static double normalize_azimuth(double azimuth_degrees) {
    double n = std::fmod(azimuth_degrees, 360.0);
    if (n < 0.0) n += 360.0;
    return n;
}

static double seasonal_factor(int month_index_0, bool has_latitude, double latitude) {
    double northern = NORTHERN_SEASON[month_index_0];
    if (!has_latitude) return northern;
    if (latitude < 0.0) {
        return NORTHERN_SEASON[(month_index_0 + 6) % 12];
    }
    if (std::fabs(latitude) < 15.0) {
        return 0.85 + 0.15 * northern;
    }
    return northern;
}

static double tilt_factor(double tilt_degrees, bool has_latitude, double latitude) {
    double target = has_latitude
        ? clamp_double(std::fabs(latitude), 0.0, 45.0)
        : 20.0;
    double delta = std::fabs(tilt_degrees - target);
    double factor = std::cos(delta * M_PI / 180.0);
    return clamp_double(factor, 0.75, 1.05);
}

static double azimuth_factor(double azimuth_degrees, bool has_latitude, double latitude) {
    double ideal = (has_latitude && latitude < 0.0) ? 0.0 : 180.0;
    double delta = std::fabs(azimuth_degrees - ideal);
    if (delta > 180.0) delta = 360.0 - delta;
    double factor = std::cos(delta * M_PI / 180.0);
    return clamp_double(0.70 + 0.30 * std::max(0.0, factor), 0.70, 1.0);
}

static double temperature_factor(double hot_cell_celsius) {
    double loss = std::max(0.0, (hot_cell_celsius - 45.0) * 0.002);
    return clamp_double(1.0 - loss, 0.85, 1.0);
}

static double production_per_month(
    double kw,
    double peak_sun_hours,
    double derate,
    int days_in_month,
    double tilt_factor,
    double azimuth_factor,
    double temperature_factor) {
    return kw * peak_sun_hours * days_in_month * derate *
           tilt_factor * azimuth_factor * temperature_factor;
}

static bool equal_case_insensitive(const char *a, const char *b) {
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

void solar_site_conditions_init(solar_site_conditions_t *site) {
    if (!site) return;
    site->location_name[0] = '\0';
    std::strncpy(site->location_name, "Unspecified", sizeof(site->location_name) - 1);
    site->location_name[sizeof(site->location_name) - 1] = '\0';
    site->latitude_degrees = 0.0;
    site->longitude_degrees = 0.0;
    site->has_latitude = false;
    site->has_longitude = false;
    site->min_ambient_celsius = -10.0;
    site->hot_cell_celsius = 70.0;
    site->peak_sun_hours_per_day = DEFAULT_PEAK_SUN_HOURS_PER_DAY;
    site->system_derate_factor = DEFAULT_SYSTEM_DERATE_FACTOR;
    site->array_tilt_degrees = 20.0;
    site->array_azimuth_degrees = 180.0;
}

bool solar_site_conditions_apply_preset(
    solar_site_conditions_t *site,
    const char *preset_id) {
    if (!site || !preset_id) return false;
    for (size_t i = 0; i < PRESET_COUNT; ++i) {
        if (equal_case_insensitive(CLIMATE_PRESETS[i].id, preset_id)) {
            const solar_site_climate_preset_t *p = &CLIMATE_PRESETS[i];
            std::strncpy(site->location_name, p->display_name, sizeof(site->location_name) - 1);
            site->location_name[sizeof(site->location_name) - 1] = '\0';
            site->has_latitude = p->has_latitude;
            site->has_longitude = p->has_longitude;
            if (p->has_latitude) {
                site->latitude_degrees = p->latitude_degrees;
                site->array_azimuth_degrees = (p->latitude_degrees < 0.0) ? 0.0 : 180.0;
                site->array_tilt_degrees = clamp_double(std::fabs(p->latitude_degrees), 5.0, 40.0);
            } else {
                site->array_azimuth_degrees = 180.0;
                site->array_tilt_degrees = 20.0;
            }
            if (p->has_longitude) {
                site->longitude_degrees = p->longitude_degrees;
            }
            site->min_ambient_celsius = p->min_ambient_celsius;
            site->hot_cell_celsius = p->hot_cell_celsius;
            site->peak_sun_hours_per_day = p->peak_sun_hours_per_day;
            site->system_derate_factor = p->system_derate_factor;
            return true;
        }
    }
    return false;
}

void solar_energy_estimate_simple(
    double total_dc_watts,
    const solar_site_conditions_t *site,
    solar_energy_estimate_t *out) {
    if (!out) return;
    if (!site) {
        solar_site_conditions_t default_site;
        solar_site_conditions_init(&default_site);
        solar_energy_estimate_simple(total_dc_watts, &default_site, out);
        return;
    }

    double kw = std::max(0.0, total_dc_watts) / 1000.0;
    double psh = clamp_double(site->peak_sun_hours_per_day, 0.0, 12.0);
    double derate = clamp_double(site->system_derate_factor, 0.1, 1.0);
    double daily = kw * psh * derate;
    double annual = daily * 365.0;

    out->array_kw_dc = kw;
    out->peak_sun_hours_per_day = psh;
    out->system_derate_factor = derate;
    out->estimated_daily_kwh = daily;
    out->estimated_annual_kwh = annual;
    out->method_note =
        "STC kW x peak-sun-hours/day x derate x 365 - approximate design aid, not a weather simulation.";
}

void solar_detailed_production_estimate(
    double total_dc_watts,
    const solar_site_conditions_t *site,
    solar_detailed_production_estimate_t *out) {
    if (!out) return;
    if (!site) {
        solar_site_conditions_t default_site;
        solar_site_conditions_init(&default_site);
        solar_detailed_production_estimate(total_dc_watts, &default_site, out);
        return;
    }

    double kw = std::max(0.0, total_dc_watts) / 1000.0;
    double base_psh = clamp_double(site->peak_sun_hours_per_day, 0.0, 12.0);
    double derate = clamp_double(site->system_derate_factor, 0.1, 1.0);
    double tilt = clamp_double(site->array_tilt_degrees, 0.0, 60.0);
    double az = normalize_azimuth(site->array_azimuth_degrees);
    bool has_lat = site->has_latitude && std::isfinite(site->latitude_degrees);
    double lat = has_lat ? site->latitude_degrees : 0.0;

    double t_factor = tilt_factor(tilt, has_lat, lat);
    double a_factor = azimuth_factor(az, has_lat, lat);
    double temp_factor = temperature_factor(site->hot_cell_celsius);

    double annual = 0.0;
    for (int m = 0; m < 12; ++m) {
        double season = seasonal_factor(m, has_lat, lat);
        double month_psh = base_psh * season;
        double kwh = production_per_month(
            kw, month_psh, derate, DAYS_IN_MONTH[m], t_factor, a_factor, temp_factor);
        annual += kwh;
        out->months[m].month = m + 1;
        out->months[m].month_name = MONTH_NAMES[m];
        out->months[m].peak_sun_hours_per_day = month_psh;
        out->months[m].estimated_kwh = kwh;
    }

    out->array_kw_dc = kw;
    out->array_tilt_degrees = tilt;
    out->array_azimuth_degrees = az;
    out->system_derate_factor = derate;
    out->latitude_degrees = lat;
    out->has_latitude = has_lat;
    out->estimated_annual_kwh = annual;
    out->estimated_daily_kwh = annual / 365.0;
    out->method_note =
        "Monthly STCxPSHxseasonxtiltxazimuthxtempxderate - C++ design aid (pvlib-ready shape), not TMY yield.";
}

void solar_site_conditions_clone(
    const solar_site_conditions_t *src,
    solar_site_conditions_t *dst) {
    if (!dst) return;
    if (!src) {
        solar_site_conditions_init(dst);
        return;
    }
    std::memcpy(dst->location_name, src->location_name, sizeof(dst->location_name));
    dst->latitude_degrees = src->latitude_degrees;
    dst->longitude_degrees = src->longitude_degrees;
    dst->has_latitude = src->has_latitude;
    dst->has_longitude = src->has_longitude;
    dst->min_ambient_celsius = src->min_ambient_celsius;
    dst->hot_cell_celsius = src->hot_cell_celsius;
    dst->peak_sun_hours_per_day = src->peak_sun_hours_per_day;
    dst->system_derate_factor = src->system_derate_factor;
    dst->array_tilt_degrees = src->array_tilt_degrees;
    dst->array_azimuth_degrees = src->array_azimuth_degrees;
}

bool solar_site_conditions_equal(
    const solar_site_conditions_t *a,
    const solar_site_conditions_t *b) {
    if (!a || !b) return a == b;
    if (std::strcmp(a->location_name, b->location_name) != 0) return false;
    if (a->has_latitude != b->has_latitude || a->has_longitude != b->has_longitude) return false;
    if (a->has_latitude && a->latitude_degrees != b->latitude_degrees) return false;
    if (a->has_longitude && a->longitude_degrees != b->longitude_degrees) return false;
    if (a->min_ambient_celsius != b->min_ambient_celsius) return false;
    if (a->hot_cell_celsius != b->hot_cell_celsius) return false;
    if (a->peak_sun_hours_per_day != b->peak_sun_hours_per_day) return false;
    if (a->system_derate_factor != b->system_derate_factor) return false;
    if (a->array_tilt_degrees != b->array_tilt_degrees) return false;
    if (a->array_azimuth_degrees != b->array_azimuth_degrees) return false;
    return true;
}

void solar_site_conditions_to_string(
    const solar_site_conditions_t *site,
    char *out,
    size_t out_size) {
    if (!out || out_size == 0) return;
    if (!site) {
        copy_string(out, out_size, "(null)");
        return;
    }
    std::snprintf(out, out_size,
        "Location: %s\n"
        "Lat/Lon: %s%.3f, %s%.3f\n"
        "Cold Voc ambient: %.1f C\n"
        "Hot cell: %.1f C\n"
        "Peak sun hours: %.1f h/day\n"
        "System derate: %.2f\n"
        "Array tilt / azimuth: %.1f / %.1f",
        site->location_name,
        site->has_latitude ? "" : "?", site->latitude_degrees,
        site->has_longitude ? "" : "?", site->longitude_degrees,
        site->min_ambient_celsius,
        site->hot_cell_celsius,
        site->peak_sun_hours_per_day,
        site->system_derate_factor,
        site->array_tilt_degrees,
        site->array_azimuth_degrees);
}

void solar_energy_estimate_to_string(
    const solar_energy_estimate_t *est,
    char *out,
    size_t out_size) {
    if (!out || out_size == 0) return;
    if (!est) {
        copy_string(out, out_size, "(null)");
        return;
    }
    std::snprintf(out, out_size,
        "Array: %.2f kW DC\n"
        "Peak sun hours: %.1f h/day\n"
        "System derate: %.2f\n"
        "Estimated: %.2f kWh/day, %.0f kWh/year\n"
        "Method: %s",
        est->array_kw_dc,
        est->peak_sun_hours_per_day,
        est->system_derate_factor,
        est->estimated_daily_kwh,
        est->estimated_annual_kwh,
        est->method_note);
}

void solar_detailed_production_estimate_to_string(
    const solar_detailed_production_estimate_t *est,
    char *out,
    size_t out_size) {
    if (!out || out_size == 0) return;
    if (!est) {
        copy_string(out, out_size, "(null)");
        return;
    }
    std::string buffer;
    char line[128];
    std::snprintf(line, sizeof(line),
        "Array: %.2f kW DC, tilt %.1f, azimuth %.1f\n",
        est->array_kw_dc, est->array_tilt_degrees, est->array_azimuth_degrees);
    buffer += line;
    std::snprintf(line, sizeof(line),
        "System derate: %.2f, latitude %s%.3f\n",
        est->system_derate_factor,
        est->has_latitude ? "" : "?", est->latitude_degrees);
    buffer += line;
    std::snprintf(line, sizeof(line),
        "Estimated: %.2f kWh/day, %.0f kWh/year\n\n",
        est->estimated_daily_kwh, est->estimated_annual_kwh);
    buffer += line;
    buffer += "Monthly (kWh):\n";
    for (int i = 0; i < 12; ++i) {
        std::snprintf(line, sizeof(line), "  %s: %.0f\n",
            est->months[i].month_name, est->months[i].estimated_kwh);
        buffer += line;
    }
    buffer += "Method: ";
    buffer += est->method_note ? est->method_note : "";
    copy_string(out, out_size, buffer.c_str());
}

size_t solar_site_conditions_preset_count(void) {
    return PRESET_COUNT;
}

const solar_site_climate_preset_t *solar_site_conditions_preset_get(size_t index) {
    if (index >= PRESET_COUNT) return NULL;
    return &CLIMATE_PRESETS[index];
}
