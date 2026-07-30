#include "temperature_derating.h"

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

static int case_insensitive_equals(const char *a, const char *b) {
    if (!a || !b) return a == b ? 1 : 0;
    while (*a && *b) {
        char ca = *a;
        char cb = *b;
        if (ca >= 'A' && ca <= 'Z') ca += 'a' - 'A';
        if (cb >= 'A' && cb <= 'Z') cb += 'a' - 'A';
        if (ca != cb) return 0;
        a++;
        b++;
    }
    return *a == '\0' && *b == '\0' ? 1 : 0;
}

static const solar_site_climate_preset_t k_presets[] = {
    {
        "sydney",
        "Sydney, AU",
        -33.87,
        151.21,
        2.0,
        70.0,
        4.5,
        SOLAR_DEFAULT_SYSTEM_DERATE_FACTOR,
    },
    {
        "melbourne",
        "Melbourne, AU",
        -37.81,
        144.96,
        -2.0,
        68.0,
        4.1,
        SOLAR_DEFAULT_SYSTEM_DERATE_FACTOR,
    },
    {
        "brisbane",
        "Brisbane, AU",
        -27.47,
        153.03,
        5.0,
        75.0,
        5.0,
        SOLAR_DEFAULT_SYSTEM_DERATE_FACTOR,
    },
    {
        "phoenix",
        "Phoenix, AZ",
        33.45,
        -112.07,
        -5.0,
        85.0,
        6.5,
        SOLAR_DEFAULT_SYSTEM_DERATE_FACTOR,
    },
    {
        "minneapolis",
        "Minneapolis, MN",
        44.98,
        -93.27,
        -30.0,
        65.0,
        4.2,
        SOLAR_DEFAULT_SYSTEM_DERATE_FACTOR,
    },
    {
        "temperate",
        "Temperate default",
        0.0,
        0.0,
        -10.0,
        70.0,
        4.5,
        SOLAR_DEFAULT_SYSTEM_DERATE_FACTOR,
    },
};

static const size_t k_preset_count = sizeof(k_presets) / sizeof(k_presets[0]);

const solar_site_climate_preset_t *solar_site_climate_presets(size_t *out_count) {
    if (out_count) *out_count = k_preset_count;
    return k_presets;
}

const solar_site_climate_preset_t *solar_site_climate_preset_by_id(const char *id) {
    if (!id) return NULL;
    for (size_t i = 0; i < k_preset_count; i++) {
        if (case_insensitive_equals(k_presets[i].id, id)) {
            return &k_presets[i];
        }
    }
    return NULL;
}

void solar_site_design_conditions_init_default(solar_site_design_conditions_t *site) {
    if (!site) return;
    std::memset(site, 0, sizeof(*site));
    copy_string(site->location_name, SOLAR_LOCATION_NAME_LEN, "Unspecified");
    site->min_ambient_celsius = -10.0;
    site->hot_cell_celsius = 70.0;
    site->peak_sun_hours_per_day = SOLAR_DEFAULT_PEAK_SUN_HOURS_PER_DAY;
    site->system_derate_factor = SOLAR_DEFAULT_SYSTEM_DERATE_FACTOR;
    site->array_tilt_degrees = 20.0;
    site->array_azimuth_degrees = 180.0;
}

void solar_site_design_conditions_apply_preset(solar_site_design_conditions_t *site, const solar_site_climate_preset_t *preset) {
    if (!site || !preset) return;
    copy_string(site->location_name, SOLAR_LOCATION_NAME_LEN, preset->display_name);
    site->min_ambient_celsius = preset->min_ambient_celsius;
    site->hot_cell_celsius = preset->hot_cell_celsius;
    site->peak_sun_hours_per_day = preset->peak_sun_hours_per_day;
    site->system_derate_factor = preset->system_derate_factor;

    if (preset->latitude_degrees < 0.0) {
        site->array_azimuth_degrees = 0.0;
    } else {
        site->array_azimuth_degrees = 180.0;
    }

    double tilt = std::fabs(preset->latitude_degrees);
    if (tilt < 5.0) tilt = 5.0;
    if (tilt > 40.0) tilt = 40.0;
    site->array_tilt_degrees = tilt;
}

static double clamp_finite(double value, double min_val, double max_val) {
    if (!std::isfinite(value)) return min_val;
    if (value < min_val) return min_val;
    if (value > max_val) return max_val;
    return value;
}

double solar_adjust_voltage(double stc_volts, double temperature_celsius, double temp_coeff_percent_per_c) {
    if (!std::isfinite(stc_volts) || !std::isfinite(temperature_celsius) || !std::isfinite(temp_coeff_percent_per_c)) {
        return 0.0;
    }
    double delta = temperature_celsius - SOLAR_STANDARD_TEST_CELSIUS;
    double factor = 1.0 + (temp_coeff_percent_per_c / 100.0) * delta;
    return stc_volts * factor;
}

double solar_adjust_power(double stc_watts, double temperature_celsius, double temp_coeff_percent_per_c) {
    return solar_adjust_voltage(stc_watts, temperature_celsius, temp_coeff_percent_per_c);
}

double solar_resolve_voc_temp_coeff_pct_per_c(const solar_panel_definition_t *def) {
    if (!def) return SOLAR_DEFAULT_VOC_TEMP_COEFF_PCT_PER_C;
    /* The native panel definition stores the coefficient directly. Zero is treated as missing
     * because a real datasheet coefficient is always non-zero. */
    if (def->temp_coeff_voc_pct_per_c == 0.0) {
        return SOLAR_DEFAULT_VOC_TEMP_COEFF_PCT_PER_C;
    }
    return def->temp_coeff_voc_pct_per_c;
}

double solar_resolve_pmax_temp_coeff_pct_per_c(const solar_panel_definition_t *def) {
    if (!def) return SOLAR_DEFAULT_PMAX_TEMP_COEFF_PCT_PER_C;
    if (def->temp_coeff_pmax_pct_per_c == 0.0) {
        return SOLAR_DEFAULT_PMAX_TEMP_COEFF_PCT_PER_C;
    }
    return def->temp_coeff_pmax_pct_per_c;
}

bool solar_uses_default_voc_coeff(const solar_panel_definition_t *def) {
    if (!def) return true;
    return def->temp_coeff_voc_pct_per_c == 0.0;
}

double solar_cold_voc_volts_for_module(const solar_panel_definition_t *def, const solar_site_design_conditions_t *site) {
    if (!def || !site) return 0.0;
    double beta = solar_resolve_voc_temp_coeff_pct_per_c(def);
    return solar_adjust_voltage(def->voc_volts, site->min_ambient_celsius, beta);
}

double solar_hot_vmp_volts_for_module(const solar_panel_definition_t *def, const solar_site_design_conditions_t *site) {
    if (!def || !site) return 0.0;
    /* Uses the Voc coefficient as a Vmp proxy when no separate Vmp coefficient is recorded. */
    double beta = solar_resolve_voc_temp_coeff_pct_per_c(def);
    return solar_adjust_voltage(def->vmp_volts, site->hot_cell_celsius, beta);
}

double solar_hot_pmax_watts_for_module(const solar_panel_definition_t *def, const solar_site_design_conditions_t *site) {
    if (!def || !site) return 0.0;
    double gamma = solar_resolve_pmax_temp_coeff_pct_per_c(def);
    return solar_adjust_power(def->pmax_watts, site->hot_cell_celsius, gamma);
}

double solar_cold_voc_for_series(const solar_panel_definition_t * const *modules, size_t count, const solar_site_design_conditions_t *site) {
    if (!modules || !site || count == 0) return 0.0;
    double sum = 0.0;
    for (size_t i = 0; i < count; i++) {
        if (!modules[i]) continue;
        sum += solar_cold_voc_volts_for_module(modules[i], site);
    }
    return sum;
}

double solar_hot_vmp_for_series(const solar_panel_definition_t * const *modules, size_t count, const solar_site_design_conditions_t *site) {
    if (!modules || !site || count == 0) return 0.0;
    double sum = 0.0;
    for (size_t i = 0; i < count; i++) {
        if (!modules[i]) continue;
        sum += solar_hot_vmp_volts_for_module(modules[i], site);
    }
    return sum;
}

double solar_hot_pmax_for_series(const solar_panel_definition_t * const *modules, size_t count, const solar_site_design_conditions_t *site) {
    if (!modules || !site || count == 0) return 0.0;
    double sum = 0.0;
    for (size_t i = 0; i < count; i++) {
        if (!modules[i]) continue;
        sum += solar_hot_pmax_watts_for_module(modules[i], site);
    }
    return sum;
}

void solar_derate_module(const solar_panel_definition_t *def, const solar_site_design_conditions_t *site, solar_module_temp_report_t *out_report) {
    if (!out_report) return;
    std::memset(out_report, 0, sizeof(*out_report));
    if (!def || !site) return;

    out_report->used_default_voc_coeff = solar_uses_default_voc_coeff(def);
    out_report->stc_voc_volts = def->voc_volts;
    out_report->stc_vmp_volts = def->vmp_volts;
    out_report->stc_pmax_watts = def->pmax_watts;
    out_report->voc_temp_coeff_pct_per_c = solar_resolve_voc_temp_coeff_pct_per_c(def);
    out_report->pmax_temp_coeff_pct_per_c = solar_resolve_pmax_temp_coeff_pct_per_c(def);
    out_report->min_ambient_celsius = site->min_ambient_celsius;
    out_report->hot_cell_celsius = site->hot_cell_celsius;
    out_report->voc_delta_c = site->min_ambient_celsius - SOLAR_STANDARD_TEST_CELSIUS;
    out_report->vmp_delta_c = site->hot_cell_celsius - SOLAR_STANDARD_TEST_CELSIUS;
    out_report->pmax_delta_c = site->hot_cell_celsius - SOLAR_STANDARD_TEST_CELSIUS;
    out_report->cold_voc_volts = solar_cold_voc_volts_for_module(def, site);
    out_report->hot_vmp_volts = solar_hot_vmp_volts_for_module(def, site);
    out_report->hot_pmax_watts = solar_hot_pmax_watts_for_module(def, site);
}

static bool is_mixed_definition_set(const solar_panel_definition_t * const *modules, size_t count) {
    if (count <= 1) return false;
    const solar_guid_t first_id = modules[0]->id;
    for (size_t i = 1; i < count; i++) {
        if (!modules[i]) continue;
        if (!solar_panel_guid_equals(&modules[i]->id, &first_id)) {
            return true;
        }
    }
    return false;
}

void solar_derate_string(const solar_panel_definition_t * const *modules, size_t count, const solar_site_design_conditions_t *site, solar_string_temp_report_t *out_report) {
    if (!out_report) return;
    std::memset(out_report, 0, sizeof(*out_report));
    if (!modules || !site || count == 0) return;

    double stc_voc = 0.0;
    double stc_vmp = 0.0;
    double stc_pmax = 0.0;
    double cold_voc = 0.0;
    double hot_vmp = 0.0;
    double hot_pmax = 0.0;
    bool any_default = false;
    size_t valid_count = 0;

    for (size_t i = 0; i < count; i++) {
        if (!modules[i]) continue;
        valid_count++;
        stc_voc += modules[i]->voc_volts;
        stc_vmp += modules[i]->vmp_volts;
        stc_pmax += modules[i]->pmax_watts;
        cold_voc += solar_cold_voc_volts_for_module(modules[i], site);
        hot_vmp += solar_hot_vmp_volts_for_module(modules[i], site);
        hot_pmax += solar_hot_pmax_watts_for_module(modules[i], site);
        if (solar_uses_default_voc_coeff(modules[i])) {
            any_default = true;
        }
    }

    out_report->module_count = valid_count;
    out_report->stc_voc_volts = stc_voc;
    out_report->stc_vmp_volts = stc_vmp;
    out_report->stc_pmax_watts = stc_pmax;
    out_report->cold_voc_volts = cold_voc;
    out_report->hot_vmp_volts = hot_vmp;
    out_report->hot_pmax_watts = hot_pmax;
    out_report->any_default_voc_coeff = any_default;
    out_report->is_mixed_module = is_mixed_definition_set(modules, count);
}

double solar_annual_energy_estimate_kwh(double stc_kw, const solar_site_design_conditions_t *site) {
    if (!site || !std::isfinite(stc_kw) || stc_kw < 0.0) return 0.0;
    double derate = clamp_finite(site->system_derate_factor, 0.0, 1.0);
    double psh = clamp_finite(site->peak_sun_hours_per_day, 0.0, 24.0);
    return stc_kw * psh * 365.0 * derate;
}

double solar_voltage_crossing_temperature(double stc_volts, double target_volts, double temp_coeff_percent_per_c) {
    if (!std::isfinite(stc_volts) || !std::isfinite(target_volts) || !std::isfinite(temp_coeff_percent_per_c)) {
        return 0.0;
    }
    if (stc_volts <= 0.0 || temp_coeff_percent_per_c == 0.0) {
        return SOLAR_STANDARD_TEST_CELSIUS;
    }
    double ratio = target_volts / stc_volts;
    double temperature = SOLAR_STANDARD_TEST_CELSIUS + (ratio - 1.0) * (100.0 / temp_coeff_percent_per_c);
    return temperature;
}

double solar_pmax_derating_factor(double hot_cell_celsius, double temp_coeff_percent_per_c) {
    if (!std::isfinite(hot_cell_celsius) || !std::isfinite(temp_coeff_percent_per_c)) return 0.0;
    double delta = hot_cell_celsius - SOLAR_STANDARD_TEST_CELSIUS;
    double factor = 1.0 + (temp_coeff_percent_per_c / 100.0) * delta;
    if (factor < 0.0) factor = 0.0;
    if (factor > 1.0) factor = 1.0;
    return factor;
}

double solar_cold_voc_for_array(
    const solar_panel_definition_t * const *modules,
    size_t count,
    const solar_site_design_conditions_t *site,
    int modules_in_series) {
    if (!modules || !site || count == 0 || modules_in_series <= 0) return 0.0;
    double string_cold_voc = 0.0;
    size_t strings = 0;
    for (size_t i = 0; i < count; i += modules_in_series) {
        size_t end = i + modules_in_series;
        if (end > count) end = count;
        double voc = solar_cold_voc_for_series(&modules[i], end - i, site);
        if (voc > string_cold_voc) string_cold_voc = voc;
        strings++;
    }
    (void)strings;
    return string_cold_voc;
}

double solar_monthly_energy_estimate_kwh(
    double stc_kw,
    const solar_site_design_conditions_t *site,
    int month,
    double monthly_psh_factor) {
    if (!site || month < 1 || month > 12) return 0.0;
    double annual = solar_annual_energy_estimate_kwh(stc_kw, site);
    double days = 30.4375;
    if (month == 2) days = 28.25;
    if (month == 4 || month == 6 || month == 9 || month == 11) days = 30.0;
    double year_days = 365.25;
    return annual * (days / year_days) * monthly_psh_factor;
}

