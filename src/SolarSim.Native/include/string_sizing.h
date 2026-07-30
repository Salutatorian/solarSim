#ifndef STRING_SIZING_H
#define STRING_SIZING_H

#include <stdbool.h>
#include <stddef.h>

#include "solar_panel.h"
#include "temperature_derating.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Inverter electrical limits. Mirrors SolarSim.Domain.Equipment.InverterElectricalSpecs. */

typedef struct {
    solar_guid_t definition_id;
    double ac_rated_watts;
    int mppt_count;
    double min_mppt_volts;
    double max_mppt_volts;
    double max_dc_volts;
    double max_current_per_mppt_amps;
    double max_dc_power_per_mppt_watts;
} solar_inverter_electrical_specs_t;

#define SOLAR_STRING_SIZING_MAX_ISSUES 8
#define SOLAR_STRING_SIZING_MSG_LEN 256
#define SOLAR_STRING_SIZING_CODE_LEN 32

typedef enum {
    SOLAR_STRING_SIZING_SEVERITY_INFO = 0,
    SOLAR_STRING_SIZING_SEVERITY_WARNING,
    SOLAR_STRING_SIZING_SEVERITY_ERROR
} solar_string_sizing_severity_t;

typedef struct {
    solar_string_sizing_severity_t severity;
    char code[SOLAR_STRING_SIZING_CODE_LEN];
    char message[SOLAR_STRING_SIZING_MSG_LEN];
    char detail[SOLAR_STRING_SIZING_MSG_LEN];
} solar_string_sizing_issue_t;

typedef struct {
    solar_guid_t panel_definition_id;
    char panel_name[SOLAR_MODEL_LEN];
    double stc_voc_volts;
    double cold_voc_volts;
    double hot_vmp_volts;
    double min_ambient_celsius;
    double hot_cell_celsius;
    double voc_temp_coeff_pct_per_c;
    bool used_default_voc_coeff;
    double inverter_max_dc_volts;
    double inverter_min_mppt_volts;
    double inverter_max_mppt_volts;
    int max_modules_in_series;
    int min_modules_in_series;
    int max_modules_for_mppt_window;
    solar_string_sizing_issue_t issues[SOLAR_STRING_SIZING_MAX_ISSUES];
    size_t issue_count;
} solar_string_sizing_advice_t;

/* Built-in inverter spec factories. */
void solar_inverter_specs_generic_5kw_2mppt(solar_inverter_electrical_specs_t *specs);
void solar_inverter_specs_generic_7_6kw_3mppt(solar_inverter_electrical_specs_t *specs);
void solar_inverter_specs_anenji_12kw_2mppt(solar_inverter_electrical_specs_t *specs);
void solar_inverter_specs_anenji_4_2kw_1mppt(solar_inverter_electrical_specs_t *specs);
void solar_inverter_specs_anenji_6_5kw_2mppt(solar_inverter_electrical_specs_t *specs);

bool solar_inverter_specs_is_valid(const solar_inverter_electrical_specs_t *specs);

/* Main string sizing advice. */
void solar_string_sizing_advise(
    const solar_panel_definition_t *panel,
    const solar_inverter_electrical_specs_t *inverter,
    const solar_site_design_conditions_t *site,
    solar_string_sizing_advice_t *out_advice);

/* Convenience: directly advise for a known number of modules. */
void solar_string_sizing_advise_count(
    const solar_panel_definition_t *panel,
    const solar_inverter_electrical_specs_t *inverter,
    const solar_site_design_conditions_t *site,
    int module_count,
    solar_string_sizing_advice_t *out_advice);

/* Helper: bounds clamp for integer module counts. */
int solar_string_sizing_clamp_module_count(int count, const solar_string_sizing_advice_t *advice);

/* Parallel string advice. */
int solar_string_sizing_recommended_parallel_strings(
    const solar_panel_definition_t *panel,
    const solar_inverter_electrical_specs_t *inverter,
    int modules_per_string);

double solar_string_sizing_dc_ac_ratio(
    const solar_panel_definition_t *panel,
    int modules_per_string,
    int parallel_strings,
    const solar_inverter_electrical_specs_t *inverter);

void solar_string_sizing_advise_parallel(
    const solar_panel_definition_t *panel,
    const solar_inverter_electrical_specs_t *inverter,
    const solar_site_design_conditions_t *site,
    int modules_per_string,
    int parallel_strings,
    solar_string_sizing_advice_t *out_advice);

/* Recommend MPPT count given total module target and chosen string length. */
int solar_string_sizing_mppt_count_recommendation(
    const solar_panel_definition_t *panel,
    int total_modules,
    int modules_per_string,
    const solar_inverter_electrical_specs_t *inverter);

#ifdef __cplusplus
}
#endif

#endif
