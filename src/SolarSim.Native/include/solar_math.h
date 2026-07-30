#ifndef SOLAR_MATH_H
#define SOLAR_MATH_H

#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Astronomical and solar-position calculations for PV yield estimates.
 * Based on standard solar engineering equations (SPAs).
 */

typedef struct {
    double latitude_deg;
    double longitude_deg;
    double timezone_offset_h;
} solar_location_t;

typedef struct {
    int year;
    int month;
    int day;
    int hour;
    int minute;
    double second;
} solar_date_time_t;

typedef struct {
    double declination_deg;
    double hour_angle_deg;
    double elevation_deg;
    double azimuth_deg;
    double zenith_deg;
    double air_mass;
    bool sun_up;
} solar_position_t;

typedef struct {
    double day_of_year;
    double equation_of_time_minutes;
    double extraterrestrial_irradiance_w_m2;
} solar_day_info_t;

/* Solar position for a given location and UTC-local date/time. */
void solar_position_calculate(
    const solar_location_t *location,
    const solar_date_time_t *local_dt,
    solar_position_t *out_position);

/* Day-of-year, equation of time, and extraterrestrial irradiance. */
void solar_day_info_calculate(int year, int month, int day, solar_day_info_t *out_info);

/* Plane-of-array irradiance components. */
double solar_beam_irradiance_on_plane(
    double beam_horizontal_w_m2,
    double incidence_angle_deg);
double solar_diffuse_irradiance_on_plane(
    double diffuse_horizontal_w_m2,
    double tilt_deg,
    double albedo,
    double global_horizontal_w_m2);

/* Helpers. */
double solar_degrees_to_radians(double deg);
double solar_radians_to_degrees(double rad);
double solar_julian_day(int year, int month, int day, double hour_utc);
bool solar_is_leap_year(int year);

#ifdef __cplusplus
}
#endif

#endif
