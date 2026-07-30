#ifndef YIELD_SIMULATOR_H
#define YIELD_SIMULATOR_H

#include <stdbool.h>
#include <stddef.h>

#include "solar_math.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Simplified PV yield simulation (hourly time series and annual summary).
 * Mirrors the energy-estimate logic in the C# domain.
 */

#define YIELD_MONTHS 12
#define YIELD_HOURS_PER_YEAR 8760
#define YIELD_MAX_SYSTEM_KW 10000.0

typedef struct {
    double latitude_deg;
    double longitude_deg;
    double timezone_offset_h;
    double altitude_m;
    double ambient_temp_c;
    double wind_speed_m_s;
    double psh_annual; /* peak sun hours per day average */
    double system_derate;
} yield_site_t;

typedef struct {
    double system_dc_kw;
    double system_ac_kw;
    double inverter_efficiency;
    double dc_ac_ratio;
    double tilt_deg;
    double azimuth_deg;
    double derate;
    double albedo;
    double temp_coeff_pmax_pct_per_c;
} yield_system_t;

typedef struct {
    double monthly_kwh[YIELD_MONTHS];
    double annual_kwh;
    double capacity_factor;
    double specific_yield_kwh_kwp;
    double peak_sun_hours_annual;
    double losses_percent;
    bool is_valid;
    char error_message[128];
} yield_result_t;

void yield_simulate_annual(
    const yield_system_t *system,
    const yield_site_t *site,
    yield_result_t *out_result);

/* Hourly plane-of-array irradiance and AC power for a single day. */
typedef struct {
    double hour;
    double poa_irradiance_w_m2;
    double cell_temp_c;
    double dc_power_w;
    double ac_power_w;
    bool sun_up;
} yield_hourly_point_t;

void yield_simulate_day(
    const yield_system_t *system,
    const yield_site_t *site,
    int year,
    int month,
    int day,
    yield_hourly_point_t *out_points,
    size_t max_points,
    size_t *out_count);

/* Helpers. */
double yield_cell_temperature(
    double poa_irradiance_w_m2,
    double ambient_temp_c,
    double wind_speed_m_s);

double yield_dc_power_from_irradiance(
    double system_dc_kw,
    double poa_irradiance_w_m2,
    double reference_irradiance_w_m2,
    double cell_temp_c,
    double temp_coeff_pmax_pct_per_c);

double yield_ac_power_clip(
    double dc_power_w,
    double system_ac_kw,
    double inverter_efficiency);

double yield_poa_irradiance_clear_sky(
    double extraterrestrial_irradiance_w_m2,
    double sun_zenith_deg,
    double air_mass,
    double altitude_m);

#ifdef __cplusplus
}
#endif

#endif
