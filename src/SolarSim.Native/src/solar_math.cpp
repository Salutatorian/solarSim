#include "solar_math.h"

#include <cmath>
#include <cstring>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

double solar_degrees_to_radians(double deg) {
    return deg * M_PI / 180.0;
}

double solar_radians_to_degrees(double rad) {
    return rad * 180.0 / M_PI;
}

bool solar_is_leap_year(int year) {
    if (year % 4 != 0) return false;
    if (year % 100 == 0 && year % 400 != 0) return false;
    return true;
}

static int days_in_month(int year, int month) {
    switch (month) {
        case 1: return 31;
        case 2: return solar_is_leap_year(year) ? 29 : 28;
        case 3: return 31;
        case 4: return 30;
        case 5: return 31;
        case 6: return 30;
        case 7: return 31;
        case 8: return 31;
        case 9: return 30;
        case 10: return 31;
        case 11: return 30;
        case 12: return 31;
        default: return 30;
    }
}

static int day_of_year(int year, int month, int day) {
    int doy = day;
    for (int m = 1; m < month; m++) {
        doy += days_in_month(year, m);
    }
    return doy;
}

double solar_julian_day(int year, int month, int day, double hour_utc) {
    if (month <= 2) {
        year -= 1;
        month += 12;
    }
    int a = year / 100;
    int b = 2 - a + a / 4;
    double jd = (int)(365.25 * (year + 4716)) + (int)(30.6001 * (month + 1)) + day + hour_utc / 24.0 + b - 1524.5;
    return jd;
}

void solar_day_info_calculate(int year, int month, int day, solar_day_info_t *out_info) {
    if (!out_info) return;
    std::memset(out_info, 0, sizeof(*out_info));
    int doy = day_of_year(year, month, day);
    out_info->day_of_year = (double)doy;

    double gamma = 2.0 * M_PI * (out_info->day_of_year - 1) / 365.0;
    double eqtime = 229.18 * (
        0.000075 +
        0.001868 * std::cos(gamma) -
        0.032077 * std::sin(gamma) -
        0.014615 * std::cos(2.0 * gamma) -
        0.040849 * std::sin(2.0 * gamma));
    out_info->equation_of_time_minutes = eqtime;

    double eccentricity = 1.000110 + 0.034221 * std::cos(gamma) + 0.001280 * std::sin(gamma) +
                          0.000719 * std::cos(2.0 * gamma) + 0.000077 * std::sin(2.0 * gamma);
    double solar_constant = 1367.0;
    out_info->extraterrestrial_irradiance_w_m2 = solar_constant * eccentricity;
}

static void local_to_utc(const solar_date_time_t *local, double timezone_offset_h, int *out_year, int *out_month, int *out_day, double *out_hour_utc) {
    double frac = local->second / 3600.0 + local->minute / 60.0 + local->hour - timezone_offset_h;
    int day_adj = 0;
    double hour = frac;
    while (hour >= 24.0) { hour -= 24.0; day_adj += 1; }
    while (hour < 0.0) { hour += 24.0; day_adj -= 1; }
    *out_hour_utc = hour;

    int y = local->year;
    int m = local->month;
    int d = local->day + day_adj;
    while (d > days_in_month(y, m)) {
        d -= days_in_month(y, m);
        m++;
        if (m > 12) { m = 1; y++; }
    }
    while (d < 1) {
        m--;
        if (m < 1) { m = 12; y--; }
        d += days_in_month(y, m);
    }
    *out_year = y;
    *out_month = m;
    *out_day = d;
}

void solar_position_calculate(
    const solar_location_t *location,
    const solar_date_time_t *local_dt,
    solar_position_t *out_position) {
    if (!location || !local_dt || !out_position) return;
    std::memset(out_position, 0, sizeof(*out_position));

    int year, month, day;
    double hour_utc;
    local_to_utc(local_dt, location->timezone_offset_h, &year, &month, &day, &hour_utc);

    double doy = (double)day_of_year(year, month, day);
    double gamma = 2.0 * M_PI * (doy - 1) / 365.0;

    double eqtime = 229.18 * (
        0.000075 +
        0.001868 * std::cos(gamma) -
        0.032077 * std::sin(gamma) -
        0.014615 * std::cos(2.0 * gamma) -
        0.040849 * std::sin(2.0 * gamma));

    double declination = 0.006918 - 0.399912 * std::cos(gamma) + 0.070257 * std::sin(gamma) -
                         0.006758 * std::cos(2.0 * gamma) + 0.000907 * std::sin(2.0 * gamma) -
                         0.002697 * std::cos(3.0 * gamma) + 0.001480 * std::sin(3.0 * gamma);
    out_position->declination_deg = solar_radians_to_degrees(declination);

    double time_offset = eqtime + 4.0 * location->longitude_deg - 60.0 * location->timezone_offset_h;
    double true_solar_time = local_dt->hour * 60.0 + local_dt->minute + local_dt->second / 60.0 + time_offset;
    while (true_solar_time > 1440.0) true_solar_time -= 1440.0;
    while (true_solar_time < 0.0) true_solar_time += 1440.0;

    double hour_angle = (true_solar_time / 4.0) - 180.0;
    out_position->hour_angle_deg = hour_angle;

    double lat_rad = solar_degrees_to_radians(location->latitude_deg);
    double ha_rad = solar_degrees_to_radians(hour_angle);

    double cos_zenith = std::sin(lat_rad) * std::sin(declination) +
                        std::cos(lat_rad) * std::cos(declination) * std::cos(ha_rad);
    if (cos_zenith > 1.0) cos_zenith = 1.0;
    if (cos_zenith < -1.0) cos_zenith = -1.0;

    double zenith_rad = std::acos(cos_zenith);
    out_position->zenith_deg = solar_radians_to_degrees(zenith_rad);
    out_position->elevation_deg = 90.0 - out_position->zenith_deg;
    out_position->sun_up = out_position->elevation_deg > 0.0;

    double numerator = -std::sin(ha_rad);
    double denominator = std::tan(declination) * std::cos(lat_rad) - std::sin(lat_rad);
    double azimuth_rad = std::atan2(numerator, denominator);
    out_position->azimuth_deg = solar_radians_to_degrees(azimuth_rad);
    if (out_position->azimuth_deg < 0.0) out_position->azimuth_deg += 360.0;

    /* Air mass approximation (Kasten-Young). */
    if (out_position->elevation_deg > 0.0) {
        double elev_rad = solar_degrees_to_radians(out_position->elevation_deg);
        out_position->air_mass = 1.0 / (std::sin(elev_rad) + 0.50572 * std::pow(6.07995 + out_position->elevation_deg, -1.6364));
    } else {
        out_position->air_mass = 100.0;
    }
}

double solar_beam_irradiance_on_plane(double beam_horizontal_w_m2, double incidence_angle_deg) {
    if (beam_horizontal_w_m2 < 0.0) return 0.0;
    double incidence_rad = solar_degrees_to_radians(incidence_angle_deg);
    double factor = std::cos(incidence_rad);
    if (factor < 0.0) factor = 0.0;
    return beam_horizontal_w_m2 * factor;
}

double solar_diffuse_irradiance_on_plane(
    double diffuse_horizontal_w_m2,
    double tilt_deg,
    double albedo,
    double global_horizontal_w_m2) {
    if (diffuse_horizontal_w_m2 < 0.0) return 0.0;
    double tilt_rad = solar_degrees_to_radians(tilt_deg);
    double sky_diffuse = diffuse_horizontal_w_m2 * (1.0 + std::cos(tilt_rad)) / 2.0;
    double ground_reflected = global_horizontal_w_m2 * albedo * (1.0 - std::cos(tilt_rad)) / 2.0;
    return sky_diffuse + ground_reflected;
}
