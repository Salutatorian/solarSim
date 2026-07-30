#ifndef TEMPERATURE_DERATING_H
#define TEMPERATURE_DERATING_H

#include <stdbool.h>
#include <stddef.h>

#include "solar_panel.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Site-level design temperatures and production assumptions.
 * Mirrors SolarSim.Domain.Electrical.SiteDesignConditions.
 */

#define SOLAR_LOCATION_NAME_LEN 64

typedef struct {
    char location_name[SOLAR_LOCATION_NAME_LEN];
    double min_ambient_celsius;
    double hot_cell_celsius;
    double peak_sun_hours_per_day;
    double system_derate_factor;
    double array_tilt_degrees;
    double array_azimuth_degrees;
} solar_site_design_conditions_t;

#define SOLAR_STANDARD_TEST_CELSIUS 25.0
#define SOLAR_DEFAULT_VOC_TEMP_COEFF_PCT_PER_C -0.30
#define SOLAR_DEFAULT_PMAX_TEMP_COEFF_PCT_PER_C -0.35
#define SOLAR_DEFAULT_PEAK_SUN_HOURS_PER_DAY 4.5
#define SOLAR_DEFAULT_SYSTEM_DERATE_FACTOR 0.85

/* Preset starter values. Not weather-station data. */
typedef struct {
    const char *id;
    const char *display_name;
    double latitude_degrees;
    double longitude_degrees;
    double min_ambient_celsius;
    double hot_cell_celsius;
    double peak_sun_hours_per_day;
    double system_derate_factor;
} solar_site_climate_preset_t;

void solar_site_design_conditions_init_default(solar_site_design_conditions_t *site);
void solar_site_design_conditions_apply_preset(solar_site_design_conditions_t *site, const solar_site_climate_preset_t *preset);

const solar_site_climate_preset_t *solar_site_climate_preset_by_id(const char *id);
const solar_site_climate_preset_t *solar_site_climate_presets(size_t *out_count);

/* Temperature adjustment helpers. */
double solar_adjust_voltage(double stc_volts, double temperature_celsius, double temp_coeff_percent_per_c);
double solar_adjust_power(double stc_watts, double temperature_celsius, double temp_coeff_percent_per_c);

/* Coefficient resolution with defaults. */
double solar_resolve_voc_temp_coeff_pct_per_c(const solar_panel_definition_t *def);
double solar_resolve_pmax_temp_coeff_pct_per_c(const solar_panel_definition_t *def);
bool solar_uses_default_voc_coeff(const solar_panel_definition_t *def);

/* Module-level temperature-adjusted electricals. */
double solar_cold_voc_volts_for_module(const solar_panel_definition_t *def, const solar_site_design_conditions_t *site);
double solar_hot_vmp_volts_for_module(const solar_panel_definition_t *def, const solar_site_design_conditions_t *site);
double solar_hot_pmax_watts_for_module(const solar_panel_definition_t *def, const solar_site_design_conditions_t *site);

/* Series string sums. */
double solar_cold_voc_for_series(const solar_panel_definition_t * const *modules, size_t count, const solar_site_design_conditions_t *site);
double solar_hot_vmp_for_series(const solar_panel_definition_t * const *modules, size_t count, const solar_site_design_conditions_t *site);
double solar_hot_pmax_for_series(const solar_panel_definition_t * const *modules, size_t count, const solar_site_design_conditions_t *site);

/* Comprehensive module temperature report. */
typedef struct {
    bool used_default_voc_coeff;
    double stc_voc_volts;
    double stc_vmp_volts;
    double stc_pmax_watts;
    double voc_temp_coeff_pct_per_c;
    double pmax_temp_coeff_pct_per_c;
    double min_ambient_celsius;
    double hot_cell_celsius;
    double cold_voc_volts;
    double hot_vmp_volts;
    double hot_pmax_watts;
    double voc_delta_c;
    double vmp_delta_c;
    double pmax_delta_c;
} solar_module_temp_report_t;

void solar_derate_module(const solar_panel_definition_t *def, const solar_site_design_conditions_t *site, solar_module_temp_report_t *out_report);

/* String-level temperature report. */
typedef struct {
    size_t module_count;
    double stc_voc_volts;
    double stc_vmp_volts;
    double stc_pmax_watts;
    double cold_voc_volts;
    double hot_vmp_volts;
    double hot_pmax_watts;
    bool any_default_voc_coeff;
    bool is_mixed_module;
} solar_string_temp_report_t;

void solar_derate_string(const solar_panel_definition_t * const *modules, size_t count, const solar_site_design_conditions_t *site, solar_string_temp_report_t *out_report);

/* Rough annual energy estimate from array STC kW and site conditions. */
double solar_annual_energy_estimate_kwh(double stc_kw, const solar_site_design_conditions_t *site);

/* Temperature at which a module's STC Voc would reach a target voltage. */
double solar_voltage_crossing_temperature(double stc_volts, double target_volts, double temp_coeff_percent_per_c);

/* Pmax derating factor from hot cell temperature. */
double solar_pmax_derating_factor(double hot_cell_celsius, double temp_coeff_percent_per_c);

/* Cold Voc for an array of parallel strings of a given series length. */
double solar_cold_voc_for_array(
    const solar_panel_definition_t * const *modules,
    size_t count,
    const solar_site_design_conditions_t *site,
    int modules_in_series);

/* Rough monthly energy estimate with a seasonal PSH factor. */
double solar_monthly_energy_estimate_kwh(
    double stc_kw,
    const solar_site_design_conditions_t *site,
    int month,
    double monthly_psh_factor);

#ifdef __cplusplus
}
#endif

#endif
