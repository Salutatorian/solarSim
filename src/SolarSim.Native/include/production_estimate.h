#ifndef PRODUCTION_ESTIMATE_H
#define PRODUCTION_ESTIMATE_H

#include <stdbool.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Site-level design assumptions for production estimates.
 * Mirrors SolarSim.Domain.Electrical.SiteDesignConditions.
 */
#define SOLAR_SITE_NAME_LEN 128
#define SOLAR_SITE_LOCATION_LEN 128

typedef struct {
    char location_name[SOLAR_SITE_NAME_LEN];
    double latitude_degrees;
    double longitude_degrees;
    bool has_latitude;
    bool has_longitude;
    double min_ambient_celsius;
    double hot_cell_celsius;
    double peak_sun_hours_per_day;
    double system_derate_factor;
    double array_tilt_degrees;
    double array_azimuth_degrees;
} solar_site_conditions_t;

typedef struct {
    int month; /* 1-12 */
    const char *month_name;
    double peak_sun_hours_per_day;
    double estimated_kwh;
} solar_monthly_production_row_t;

typedef struct {
    double array_kw_dc;
    double peak_sun_hours_per_day;
    double system_derate_factor;
    double estimated_annual_kwh;
    double estimated_daily_kwh;
    const char *method_note;
} solar_energy_estimate_t;

typedef struct {
    double array_kw_dc;
    double array_tilt_degrees;
    double array_azimuth_degrees;
    double system_derate_factor;
    double latitude_degrees;
    bool has_latitude;
    double estimated_annual_kwh;
    double estimated_daily_kwh;
    solar_monthly_production_row_t months[12];
    const char *method_note;
} solar_detailed_production_estimate_t;

typedef struct {
    const char *id;
    const char *display_name;
    double latitude_degrees;
    double longitude_degrees;
    bool has_latitude;
    bool has_longitude;
    double min_ambient_celsius;
    double hot_cell_celsius;
    double peak_sun_hours_per_day;
    double system_derate_factor;
} solar_site_climate_preset_t;

void solar_site_conditions_init(solar_site_conditions_t *site);

void solar_energy_estimate_simple(
    double total_dc_watts,
    const solar_site_conditions_t *site,
    solar_energy_estimate_t *out);

void solar_detailed_production_estimate(
    double total_dc_watts,
    const solar_site_conditions_t *site,
    solar_detailed_production_estimate_t *out);

bool solar_site_conditions_apply_preset(
    solar_site_conditions_t *site,
    const char *preset_id);

/* Copy site conditions into an already-allocated destination. */
void solar_site_conditions_clone(
    const solar_site_conditions_t *src,
    solar_site_conditions_t *dst);

/* Compare two site conditions objects for equality. */
bool solar_site_conditions_equal(
    const solar_site_conditions_t *a,
    const solar_site_conditions_t *b);

/* Format site conditions into a human-readable string. */
void solar_site_conditions_to_string(
    const solar_site_conditions_t *site,
    char *out,
    size_t out_size);

/* Format a simple energy estimate into a human-readable string. */
void solar_energy_estimate_to_string(
    const solar_energy_estimate_t *est,
    char *out,
    size_t out_size);

/* Format a detailed production estimate into a human-readable string. */
void solar_detailed_production_estimate_to_string(
    const solar_detailed_production_estimate_t *est,
    char *out,
    size_t out_size);

/* Number of built-in climate presets. */
size_t solar_site_conditions_preset_count(void);

/* Get a built-in climate preset by index (read-only). Returns null if out of range. */
const solar_site_climate_preset_t *solar_site_conditions_preset_get(size_t index);

#ifdef __cplusplus
}
#endif

#endif
