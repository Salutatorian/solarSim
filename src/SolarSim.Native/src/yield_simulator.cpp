#include "yield_simulator.h"

#include <cmath>
#include <cstdio>
#include <cstring>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

double yield_cell_temperature(
    double poa_irradiance_w_m2,
    double ambient_temp_c,
    double wind_speed_m_s) {
    if (poa_irradiance_w_m2 <= 0.0) return ambient_temp_c;
    /* Faiman model with light wind. */
    double wind_factor = 1.0;
    if (wind_speed_m_s > 0.0) {
        wind_factor = 1.0 - 0.05 * std::sqrt(wind_speed_m_s);
    }
    double delta_t = poa_irradiance_w_m2 / 800.0 * 25.0 * wind_factor;
    return ambient_temp_c + delta_t;
}

double yield_dc_power_from_irradiance(
    double system_dc_kw,
    double poa_irradiance_w_m2,
    double reference_irradiance_w_m2,
    double cell_temp_c,
    double temp_coeff_pmax_pct_per_c) {
    if (system_dc_kw <= 0.0) return 0.0;
    if (poa_irradiance_w_m2 <= 0.0) return 0.0;
    if (reference_irradiance_w_m2 <= 0.0) reference_irradiance_w_m2 = 1000.0;

    double normalized_irradiance = poa_irradiance_w_m2 / reference_irradiance_w_m2;
    double stc_temp_c = 25.0;
    double temp_factor = 1.0 + (temp_coeff_pmax_pct_per_c / 100.0) * (cell_temp_c - stc_temp_c);
    if (temp_factor < 0.0) temp_factor = 0.0;

    double dc_w = system_dc_kw * 1000.0 * normalized_irradiance * temp_factor;
    if (dc_w < 0.0) dc_w = 0.0;
    return dc_w;
}

double yield_ac_power_clip(
    double dc_power_w,
    double system_ac_kw,
    double inverter_efficiency) {
    if (dc_power_w <= 0.0) return 0.0;
    double max_ac_w = system_ac_kw * 1000.0;
    double ac_w = dc_power_w * inverter_efficiency;
    if (ac_w > max_ac_w) ac_w = max_ac_w;
    return ac_w;
}

double yield_poa_irradiance_clear_sky(
    double extraterrestrial_irradiance_w_m2,
    double sun_zenith_deg,
    double air_mass,
    double altitude_m) {
    if (sun_zenith_deg >= 90.0) return 0.0;
    if (extraterrestrial_irradiance_w_m2 <= 0.0) return 0.0;

    double elevation_km = altitude_m / 1000.0;
    double pressure_ratio = std::pow(1.0 - 0.0065 * elevation_km / 288.15, 5.255);
    double a = 0.14; /* broadband aerosol optical depth */
    double b = 0.55;
    double tau = a + b * air_mass * 0.01;
    double beam = extraterrestrial_irradiance_w_m2 * std::exp(-tau * air_mass * pressure_ratio);
    if (beam < 0.0) beam = 0.0;

    double zenith_rad = sun_zenith_deg * M_PI / 180.0;
    double diffuse = 0.15 * extraterrestrial_irradiance_w_m2 * std::cos(zenith_rad) * (1.0 - std::exp(-tau * air_mass));
    if (diffuse < 0.0) diffuse = 0.0;

    return beam + diffuse;
}

static double plane_of_array_irradiance(
    double beam_horizontal_w_m2,
    double diffuse_horizontal_w_m2,
    double global_horizontal_w_m2,
    double tilt_deg,
    double azimuth_deg,
    double sun_zenith_deg,
    double sun_azimuth_deg,
    double albedo) {
    double tilt_rad = tilt_deg * M_PI / 180.0;
    double zenith_rad = sun_zenith_deg * M_PI / 180.0;
    double azimuth_diff = (azimuth_deg - sun_azimuth_deg) * M_PI / 180.0;

    double cos_incidence = std::cos(zenith_rad) * std::cos(tilt_rad) +
                           std::sin(zenith_rad) * std::sin(tilt_rad) * std::cos(azimuth_diff);
    if (cos_incidence < 0.0) cos_incidence = 0.0;

    double beam_poa = beam_horizontal_w_m2 * cos_incidence / std::cos(zenith_rad);
    if (beam_poa < 0.0) beam_poa = 0.0;

    double sky_diffuse = diffuse_horizontal_w_m2 * (1.0 + std::cos(tilt_rad)) / 2.0;
    double ground_reflected = global_horizontal_w_m2 * albedo * (1.0 - std::cos(tilt_rad)) / 2.0;
    return beam_poa + sky_diffuse + ground_reflected;
}

void yield_simulate_day(
    const yield_system_t *system,
    const yield_site_t *site,
    int year,
    int month,
    int day,
    yield_hourly_point_t *out_points,
    size_t max_points,
    size_t *out_count) {
    if (!system || !site || !out_points || !out_count) return;
    *out_count = 0;
    if (max_points == 0) return;

    solar_location_t location;
    location.latitude_deg = site->latitude_deg;
    location.longitude_deg = site->longitude_deg;
    location.timezone_offset_h = site->timezone_offset_h;

    for (int h = 0; h < 24 && *out_count < max_points; h++) {
        solar_date_time_t dt;
        dt.year = year;
        dt.month = month;
        dt.day = day;
        dt.hour = h;
        dt.minute = 0;
        dt.second = 0.0;

        solar_position_t sun;
        solar_position_calculate(&location, &dt, &sun);
        solar_day_info_t info;
        solar_day_info_calculate(year, month, day, &info);

        yield_hourly_point_t *point = &out_points[*out_count];
        point->hour = h + 0.5;
        point->sun_up = sun.elevation_deg > 0.0;

        if (!point->sun_up) {
            point->poa_irradiance_w_m2 = 0.0;
            point->cell_temp_c = site->ambient_temp_c;
            point->dc_power_w = 0.0;
            point->ac_power_w = 0.0;
        } else {
            double beam = 0.8 * info.extraterrestrial_irradiance_w_m2 * std::cos(sun.zenith_deg * M_PI / 180.0);
            if (beam < 0.0) beam = 0.0;
            double diffuse = 0.2 * info.extraterrestrial_irradiance_w_m2 * std::cos(sun.zenith_deg * M_PI / 180.0);
            if (diffuse < 0.0) diffuse = 0.0;
            double global = beam + diffuse;

            point->poa_irradiance_w_m2 = plane_of_array_irradiance(
                beam, diffuse, global,
                system->tilt_deg, system->azimuth_deg,
                sun.zenith_deg, sun.azimuth_deg,
                system->albedo);
            point->cell_temp_c = yield_cell_temperature(point->poa_irradiance_w_m2, site->ambient_temp_c, site->wind_speed_m_s);
            point->dc_power_w = yield_dc_power_from_irradiance(
                system->system_dc_kw, point->poa_irradiance_w_m2, 1000.0,
                point->cell_temp_c, system->temp_coeff_pmax_pct_per_c);
            point->ac_power_w = yield_ac_power_clip(point->dc_power_w, system->system_ac_kw, system->inverter_efficiency);
        }
        (*out_count)++;
    }
}

void yield_simulate_annual(
    const yield_system_t *system,
    const yield_site_t *site,
    yield_result_t *out_result) {
    if (!system || !site || !out_result) return;
    std::memset(out_result, 0, sizeof(*out_result));
    out_result->is_valid = true;

    if (system->system_dc_kw <= 0.0 || system->system_ac_kw <= 0.0) {
        out_result->is_valid = false;
        std::snprintf(out_result->error_message, sizeof(out_result->error_message), "System power must be positive");
        return;
    }
    if (system->inverter_efficiency <= 0.0 || system->inverter_efficiency > 1.0) {
        out_result->is_valid = false;
        std::snprintf(out_result->error_message, sizeof(out_result->error_message), "Inverter efficiency must be between 0 and 1");
        return;
    }

    double annual_kwh = 0.0;
    int day_of_month[] = {21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21, 21};

    yield_hourly_point_t points[24];
    for (int m = 0; m < 12; m++) {
        double monthly_kwh = 0.0;
        size_t count = 0;
        yield_simulate_day(system, site, 2026, m + 1, day_of_month[m], points, 24, &count);
        for (size_t i = 0; i < count; i++) {
            monthly_kwh += points[i].ac_power_w / 1000.0;
        }
        /* Scale monthly from representative day to month length. */
        int days_in_month = 30; /* Simplified */
        monthly_kwh *= days_in_month;
        out_result->monthly_kwh[m] = monthly_kwh;
        annual_kwh += monthly_kwh;
    }

    out_result->annual_kwh = annual_kwh;
    if (system->system_dc_kw > 0.0) {
        out_result->specific_yield_kwh_kwp = annual_kwh / system->system_dc_kw;
    }
    out_result->capacity_factor = annual_kwh / (system->system_ac_kw * 8760.0);
    out_result->peak_sun_hours_annual = site->psh_annual;
    out_result->losses_percent = (1.0 - system->derate) * 100.0;
}
