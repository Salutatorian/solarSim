#include "string_sizing.h"

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

static void add_issue(
    solar_string_sizing_advice_t *advice,
    solar_string_sizing_severity_t severity,
    const char *code,
    const char *message,
    const char *detail) {
    if (!advice || !code || !message || !detail) return;
    if (advice->issue_count >= SOLAR_STRING_SIZING_MAX_ISSUES) return;
    solar_string_sizing_issue_t *issue = &advice->issues[advice->issue_count];
    issue->severity = severity;
    std::strncpy(issue->code, code, sizeof(issue->code) - 1);
    issue->code[sizeof(issue->code) - 1] = '\0';
    std::strncpy(issue->message, message, sizeof(issue->message) - 1);
    issue->message[sizeof(issue->message) - 1] = '\0';
    std::strncpy(issue->detail, detail, sizeof(issue->detail) - 1);
    issue->detail[sizeof(issue->detail) - 1] = '\0';
    advice->issue_count++;
}

static void guid_from_u64_pair(solar_guid_t *guid, uint64_t high, uint64_t low) {
    if (!guid) return;
    guid->id_high = high;
    guid->id_low = low;
}

void solar_inverter_specs_generic_5kw_2mppt(solar_inverter_electrical_specs_t *specs) {
    if (!specs) return;
    std::memset(specs, 0, sizeof(*specs));
    guid_from_u64_pair(&specs->definition_id, 0xa1111111, 0x0000000000000001ULL);
    specs->ac_rated_watts = 5000.0;
    specs->mppt_count = 2;
    specs->min_mppt_volts = 80.0;
    specs->max_mppt_volts = 480.0;
    specs->max_dc_volts = 600.0;
    specs->max_current_per_mppt_amps = 12.5;
    specs->max_dc_power_per_mppt_watts = 4000.0;
}

void solar_inverter_specs_generic_7_6kw_3mppt(solar_inverter_electrical_specs_t *specs) {
    if (!specs) return;
    std::memset(specs, 0, sizeof(*specs));
    guid_from_u64_pair(&specs->definition_id, 0xa1111111, 0x0000000000000002ULL);
    specs->ac_rated_watts = 7600.0;
    specs->mppt_count = 3;
    specs->min_mppt_volts = 100.0;
    specs->max_mppt_volts = 500.0;
    specs->max_dc_volts = 600.0;
    specs->max_current_per_mppt_amps = 13.0;
    specs->max_dc_power_per_mppt_watts = 4500.0;
}

void solar_inverter_specs_anenji_12kw_2mppt(solar_inverter_electrical_specs_t *specs) {
    if (!specs) return;
    std::memset(specs, 0, sizeof(*specs));
    guid_from_u64_pair(&specs->definition_id, 0xa1111111, 0x0000000000000003ULL);
    specs->ac_rated_watts = 12000.0;
    specs->mppt_count = 2;
    specs->min_mppt_volts = 90.0;
    specs->max_mppt_volts = 500.0;
    specs->max_dc_volts = 500.0;
    specs->max_current_per_mppt_amps = 22.0;
    specs->max_dc_power_per_mppt_watts = 7500.0;
}

void solar_inverter_specs_anenji_4_2kw_1mppt(solar_inverter_electrical_specs_t *specs) {
    if (!specs) return;
    std::memset(specs, 0, sizeof(*specs));
    guid_from_u64_pair(&specs->definition_id, 0xa1111111, 0x0000000000000004ULL);
    specs->ac_rated_watts = 4200.0;
    specs->mppt_count = 1;
    specs->min_mppt_volts = 60.0;
    specs->max_mppt_volts = 450.0;
    specs->max_dc_volts = 500.0;
    specs->max_current_per_mppt_amps = 18.0;
    specs->max_dc_power_per_mppt_watts = 4500.0;
}

void solar_inverter_specs_anenji_6_5kw_2mppt(solar_inverter_electrical_specs_t *specs) {
    if (!specs) return;
    std::memset(specs, 0, sizeof(*specs));
    guid_from_u64_pair(&specs->definition_id, 0xa1111111, 0x0000000000000005ULL);
    specs->ac_rated_watts = 6500.0;
    specs->mppt_count = 2;
    specs->min_mppt_volts = 90.0;
    specs->max_mppt_volts = 500.0;
    specs->max_dc_volts = 500.0;
    specs->max_current_per_mppt_amps = 18.0;
    specs->max_dc_power_per_mppt_watts = 4000.0;
}

bool solar_inverter_specs_is_valid(const solar_inverter_electrical_specs_t *specs) {
    if (!specs) return false;
    if (specs->mppt_count < 1 || specs->mppt_count > 8) return false;
    if (specs->min_mppt_volts <= 0.0) return false;
    if (specs->max_mppt_volts <= specs->min_mppt_volts) return false;
    if (specs->max_dc_volts < specs->max_mppt_volts) return false;
    if (specs->max_current_per_mppt_amps <= 0.0) return false;
    if (specs->max_dc_power_per_mppt_watts <= 0.0) return false;
    if (specs->ac_rated_watts <= 0.0) return false;
    return true;
}

static int floor_to_int(double value) {
    if (!std::isfinite(value)) return 0;
    if (value < 0.0) return 0;
    return static_cast<int>(std::floor(value));
}

static int ceil_to_int(double value) {
    if (!std::isfinite(value)) return 0;
    if (value < 0.0) return 0;
    return static_cast<int>(std::ceil(value));
}

static void init_advice(solar_string_sizing_advice_t *advice) {
    if (!advice) return;
    std::memset(advice, 0, sizeof(*advice));
    advice->max_modules_in_series = 0;
    advice->min_modules_in_series = 0;
    advice->max_modules_for_mppt_window = 0;
    advice->issue_count = 0;
}

void solar_string_sizing_advise(
    const solar_panel_definition_t *panel,
    const solar_inverter_electrical_specs_t *inverter,
    const solar_site_design_conditions_t *site,
    solar_string_sizing_advice_t *out_advice) {
    init_advice(out_advice);
    if (!out_advice || !panel || !inverter || !site) return;

    if (!solar_panel_definition_is_valid(panel)) {
        add_issue(out_advice, SOLAR_STRING_SIZING_SEVERITY_ERROR,
            "INVALID_PANEL", "Invalid panel definition",
            "Panel definition failed basic validation; cannot size string.");
        return;
    }

    if (!solar_inverter_specs_is_valid(inverter)) {
        add_issue(out_advice, SOLAR_STRING_SIZING_SEVERITY_ERROR,
            "INVALID_INVERTER", "Invalid inverter specs",
            "Inverter electrical specs are inconsistent; cannot size string.");
        return;
    }

    double beta = solar_resolve_voc_temp_coeff_pct_per_c(panel);
    double cold_voc = solar_cold_voc_volts_for_module(panel, site);
    double hot_vmp = solar_hot_vmp_volts_for_module(panel, site);
    bool used_default = solar_uses_default_voc_coeff(panel);

    out_advice->panel_definition_id = panel->id;
    copy_string(out_advice->panel_name, sizeof(out_advice->panel_name), panel->model);
    out_advice->stc_voc_volts = panel->voc_volts;
    out_advice->cold_voc_volts = cold_voc;
    out_advice->hot_vmp_volts = hot_vmp;
    out_advice->min_ambient_celsius = site->min_ambient_celsius;
    out_advice->hot_cell_celsius = site->hot_cell_celsius;
    out_advice->voc_temp_coeff_pct_per_c = beta;
    out_advice->used_default_voc_coeff = used_default;
    out_advice->inverter_max_dc_volts = inverter->max_dc_volts;
    out_advice->inverter_min_mppt_volts = inverter->min_mppt_volts;
    out_advice->inverter_max_mppt_volts = inverter->max_mppt_volts;

    if (used_default) {
        char detail[256];
        std::snprintf(detail, sizeof(detail),
            "%s: datasheet Voc coeff missing — using %.2f %%/°C.",
            panel->model, SOLAR_DEFAULT_VOC_TEMP_COEFF_PCT_PER_C);
        add_issue(out_advice, SOLAR_STRING_SIZING_SEVERITY_INFO,
            "TEMP_COEFF_DEFAULT", "Using default Voc temp coeff", detail);
    }

    int max_by_cold_voc = 0;
    if (cold_voc > 0.0) {
        max_by_cold_voc = floor_to_int(inverter->max_dc_volts / cold_voc);
    }
    if (max_by_cold_voc < 1) max_by_cold_voc = 0;

    int min_by_hot_vmp = 0;
    if (hot_vmp > 0.0) {
        min_by_hot_vmp = ceil_to_int(inverter->min_mppt_volts / hot_vmp);
    }

    int max_by_hot_vmp = 0;
    if (hot_vmp > 0.0) {
        max_by_hot_vmp = floor_to_int(inverter->max_mppt_volts / hot_vmp);
    }
    if (max_by_hot_vmp < 1) max_by_hot_vmp = 0;

    out_advice->max_modules_in_series = max_by_cold_voc;
    out_advice->min_modules_in_series = min_by_hot_vmp;
    out_advice->max_modules_for_mppt_window = max_by_hot_vmp;

    if (max_by_cold_voc == 0) {
        char detail[256];
        std::snprintf(detail, sizeof(detail),
            "One module cold Voc %.1f V exceeds max DC %.1f V at %.1f °C.",
            cold_voc, inverter->max_dc_volts, site->min_ambient_celsius);
        add_issue(out_advice, SOLAR_STRING_SIZING_SEVERITY_ERROR,
            "STRING_SIZE_IMPOSSIBLE", "Cold Voc too high for inverter", detail);
    } else if (min_by_hot_vmp > max_by_cold_voc) {
        char detail[256];
        std::snprintf(detail, sizeof(detail),
            "Need ≥%d modules for hot Vmp, but cold Voc allows ≤%d.",
            min_by_hot_vmp, max_by_cold_voc);
        add_issue(out_advice, SOLAR_STRING_SIZING_SEVERITY_WARNING,
            "STRING_SIZE_NO_OVERLAP", "No series length satisfies both limits", detail);
    }
}

void solar_string_sizing_advise_count(
    const solar_panel_definition_t *panel,
    const solar_inverter_electrical_specs_t *inverter,
    const solar_site_design_conditions_t *site,
    int module_count,
    solar_string_sizing_advice_t *out_advice) {
    solar_string_sizing_advise(panel, inverter, site, out_advice);
    if (!out_advice || module_count <= 0) return;

    if (module_count > out_advice->max_modules_in_series && out_advice->max_modules_in_series > 0) {
        char detail[256];
        std::snprintf(detail, sizeof(detail),
            "%d modules in series exceeds max %d at %.1f °C (cold Voc).",
            module_count, out_advice->max_modules_in_series, site->min_ambient_celsius);
        add_issue(out_advice, SOLAR_STRING_SIZING_SEVERITY_ERROR,
            "STRING_TOO_LONG_COLD_VOC", "String too long for cold Voc", detail);
    } else if (module_count < out_advice->min_modules_in_series && out_advice->min_modules_in_series > 0) {
        char detail[256];
        std::snprintf(detail, sizeof(detail),
            "%d modules is below min %d so hot Vmp stays in MPPT window.",
            module_count, out_advice->min_modules_in_series);
        add_issue(out_advice, SOLAR_STRING_SIZING_SEVERITY_WARNING,
            "STRING_TOO_SHORT_HOT_VMP", "String short for hot Vmp", detail);
    } else if (module_count > out_advice->max_modules_for_mppt_window && out_advice->max_modules_for_mppt_window > 0) {
        char detail[256];
        std::snprintf(detail, sizeof(detail),
            "%d modules exceeds hot Vmp window max %d.",
            module_count, out_advice->max_modules_for_mppt_window);
        add_issue(out_advice, SOLAR_STRING_SIZING_SEVERITY_WARNING,
            "STRING_TOO_LONG_HOT_VMP", "String too long for hot Vmp", detail);
    }
}

int solar_string_sizing_clamp_module_count(int count, const solar_string_sizing_advice_t *advice) {
    if (!advice) return count;
    if (count < 1) count = 1;
    if (advice->max_modules_in_series > 0 && count > advice->max_modules_in_series) {
        count = advice->max_modules_in_series;
    }
    if (advice->min_modules_in_series > 0 && count < advice->min_modules_in_series) {
        count = advice->min_modules_in_series;
    }
    if (advice->max_modules_for_mppt_window > 0 && count > advice->max_modules_for_mppt_window) {
        count = advice->max_modules_for_mppt_window;
    }
    return count;
}

int solar_string_sizing_recommended_parallel_strings(
    const solar_panel_definition_t *panel,
    const solar_inverter_electrical_specs_t *inverter,
    int modules_per_string) {
    if (!panel || !inverter || modules_per_string <= 0) return 0;
    double string_pmax = modules_per_string * panel->pmax_watts;
    if (string_pmax <= 0.0 || inverter->max_dc_power_per_mppt_watts <= 0.0) return 0;
    int parallel = static_cast<int>(std::floor(inverter->max_dc_power_per_mppt_watts / string_pmax));
    if (parallel < 1) parallel = 1;
    if (parallel > 8) parallel = 8; /* Practical limit for design aid. */
    return parallel;
}

double solar_string_sizing_dc_ac_ratio(
    const solar_panel_definition_t *panel,
    int modules_per_string,
    int parallel_strings,
    const solar_inverter_electrical_specs_t *inverter) {
    if (!panel || !inverter || inverter->ac_rated_watts <= 0.0) return 0.0;
    double total_dc = modules_per_string * parallel_strings * panel->pmax_watts;
    return total_dc / inverter->ac_rated_watts;
}

void solar_string_sizing_advise_parallel(
    const solar_panel_definition_t *panel,
    const solar_inverter_electrical_specs_t *inverter,
    const solar_site_design_conditions_t *site,
    int modules_per_string,
    int parallel_strings,
    solar_string_sizing_advice_t *out_advice) {
    solar_string_sizing_advise(panel, inverter, site, out_advice);
    if (!out_advice || !panel || !inverter || !site) return;
    if (modules_per_string <= 0 || parallel_strings <= 0) return;

    double imp_total = panel->imp_amps * parallel_strings;
    if (imp_total > inverter->max_current_per_mppt_amps) {
        char detail[256];
        std::snprintf(detail, sizeof(detail),
            "Parallel Imp %.2f A exceeds MPPT max %.2f A.",
            imp_total, inverter->max_current_per_mppt_amps);
        add_issue(out_advice, SOLAR_STRING_SIZING_SEVERITY_ERROR,
            "PARALLEL_IMP_EXCEEDED", "Parallel current too high", detail);
    }

    double ratio = solar_string_sizing_dc_ac_ratio(panel, modules_per_string, parallel_strings, inverter);
    if (ratio > 1.5) {
        char detail[256];
        std::snprintf(detail, sizeof(detail),
            "DC/AC ratio %.2f is high (>%d strings may clip).", ratio, parallel_strings);
        add_issue(out_advice, SOLAR_STRING_SIZING_SEVERITY_WARNING,
            "DC_AC_RATIO_HIGH", "High DC/AC ratio", detail);
    } else if (ratio < 0.8) {
        char detail[256];
        std::snprintf(detail, sizeof(detail),
            "DC/AC ratio %.2f is low; inverter may run below optimum.", ratio);
        add_issue(out_advice, SOLAR_STRING_SIZING_SEVERITY_INFO,
            "DC_AC_RATIO_LOW", "Low DC/AC ratio", detail);
    }
}

int solar_string_sizing_mppt_count_recommendation(
    const solar_panel_definition_t *panel,
    int total_modules,
    int modules_per_string,
    const solar_inverter_electrical_specs_t *inverter) {
    if (!panel || !inverter || modules_per_string <= 0 || total_modules <= 0) return 0;
    int strings_needed = (total_modules + modules_per_string - 1) / modules_per_string;
    if (strings_needed <= inverter->mppt_count) return inverter->mppt_count;
    /* If more strings than MPPTs, suggest the next inverter with more MPPTs. */
    return strings_needed + (strings_needed % 2);
}

