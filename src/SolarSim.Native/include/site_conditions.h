#ifndef SITE_CONDITIONS_H
#define SITE_CONDITIONS_H

#include <stdbool.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Site-level design conditions and climate presets.
 * Mirrors SolarSim.Domain.Electrical.SiteDesignConditions.
 */

#define SOLAR_SITE_LOCATION_NAME_LEN 128
#define SOLAR_SITE_PRESET_ID_LEN 32
#define SOLAR_SITE_PRESET_NAME_LEN 64
#define SOLAR_SITE_PRESET_COUNT 6

/* Standard test conditions and shared defaults. */
double solar_site_standard_test_celsius(void);
double solar_site_default_voc_temp_coeff_percent_per_c(void);
double solar_site_default_pmax_temp_coeff_percent_per_c(void);
double solar_site_default_peak_sun_hours_per_day(void);
double solar_site_default_system_derate_factor(void);

/* Named climate starter values. */
typedef struct {
    char id[SOLAR_SITE_PRESET_ID_LEN];
    char display_name[SOLAR_SITE_PRESET_NAME_LEN];
    bool has_latitude;
    double latitude_degrees;
    bool has_longitude;
    double longitude_degrees;
    double min_ambient_celsius;
    double hot_cell_celsius;
    double peak_sun_hours_per_day;
    double system_derate_factor;
} solar_site_climate_preset_t;

/* Project-level design conditions. */
typedef struct {
    char location_name[SOLAR_SITE_LOCATION_NAME_LEN];
    bool has_latitude;
    double latitude_degrees;
    bool has_longitude;
    double longitude_degrees;
    double min_ambient_celsius;
    double hot_cell_celsius;
    double peak_sun_hours_per_day;
    double system_derate_factor;
    double array_tilt_degrees;
    double array_azimuth_degrees;
} solar_site_design_conditions_t;

/* Lifecycle and mutation. */
void solar_site_conditions_init(solar_site_design_conditions_t *conditions);
void solar_site_conditions_clone(
    const solar_site_design_conditions_t *source,
    solar_site_design_conditions_t *dest);

/* Apply a climate preset, deriving tilt/azimuth from latitude when available. */
bool solar_site_conditions_apply_preset(
    solar_site_design_conditions_t *conditions,
    const solar_site_climate_preset_t *preset);

/* Preset library. */
const solar_site_climate_preset_t *solar_site_climate_preset_by_id(const char *id);
const solar_site_climate_preset_t *solar_site_climate_preset_at(size_t index);
size_t solar_site_climate_preset_count(void);

/* Quick annual energy estimate using STC kW, peak sun hours, and derate. */
double solar_site_estimate_annual_kwh(double stc_kw, double peak_sun_hours_per_day, double derate_factor);

/* Clamp a tilt/azimuth to sensible physical ranges. */
double solar_site_normalize_tilt(double degrees);
double solar_site_normalize_azimuth(double degrees);

#ifdef __cplusplus
}
#endif

#endif
