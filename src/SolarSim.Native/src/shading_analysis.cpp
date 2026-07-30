#include "shading_analysis.h"

#include <algorithm>
#include <cmath>
#include <cstring>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

static double deg_to_rad(double deg) { return deg * M_PI / 180.0; }
static double rad_to_deg(double rad) { return rad * 180.0 / M_PI; }

static double clamp(double v, double lo, double hi) {
    if (v < lo) return lo;
    if (v > hi) return hi;
    return v;
}

void shading_calculate_row_to_row(
    const shading_field_layout_t *layout,
    const solar_position_t *sun,
    shading_result_t *out_result) {
    if (!layout || !sun || !out_result) return;
    std::memset(out_result, 0, sizeof(*out_result));

    out_result->sun_elevation_deg = sun->elevation_deg;
    out_result->sun_azimuth_deg = sun->azimuth_deg;

    if (sun->elevation_deg <= 0.0) {
        out_result->shaded_fraction = 1.0;
        out_result->self_shading_occurs = true;
        return;
    }

    double elevation_rad = deg_to_rad(sun->elevation_deg);
    double tilt_rad = deg_to_rad(layout->row_tilt_deg);

    /* Effective collector height above the ground plane. */
    double collector_height = layout->panel_height_mm * std::sin(tilt_rad) + layout->ground_clearance_mm;

    /* Shadow length cast by the row onto the ground plane. */
    double shadow_length = collector_height / std::tan(elevation_rad);

    /* Shaded fraction is the overlap of the shadow with the next row. */
    double shaded_fraction = 0.0;
    if (layout->row_spacing_mm > 0.0 && shadow_length > layout->row_spacing_mm) {
        double overlap = shadow_length - layout->row_spacing_mm;
        /* Project the overlap onto the plane of the panel. */
        double panel_height = layout->panel_height_mm * std::sin(tilt_rad);
        double projected_overlap = overlap * std::sin(tilt_rad);
        if (panel_height > 0.0) {
            shaded_fraction = clamp(projected_overlap / panel_height, 0.0, 1.0);
        }
    }

    out_result->shaded_fraction = shaded_fraction;
    out_result->self_shading_occurs = shaded_fraction > 0.01;
    out_result->critical_sun_angle_deg = rad_to_deg(std::atan2(collector_height, layout->row_spacing_mm));
}

static double polygon_bounding_height(const shading_obstacle_t *obstacle) {
    if (!obstacle || obstacle->vertex_count == 0) return 0.0;
    double min_y = obstacle->vertices[0].y_mm;
    double max_y = obstacle->vertices[0].y_mm;
    for (size_t i = 1; i < obstacle->vertex_count; i++) {
        if (obstacle->vertices[i].y_mm < min_y) min_y = obstacle->vertices[i].y_mm;
        if (obstacle->vertices[i].y_mm > max_y) max_y = obstacle->vertices[i].y_mm;
    }
    return max_y - min_y;
}

static double polygon_bounding_width(const shading_obstacle_t *obstacle) {
    if (!obstacle || obstacle->vertex_count == 0) return 0.0;
    double min_x = obstacle->vertices[0].x_mm;
    double max_x = obstacle->vertices[0].x_mm;
    for (size_t i = 1; i < obstacle->vertex_count; i++) {
        if (obstacle->vertices[i].x_mm < min_x) min_x = obstacle->vertices[i].x_mm;
        if (obstacle->vertices[i].x_mm > max_x) max_x = obstacle->vertices[i].x_mm;
    }
    return max_x - min_x;
}

void shading_calculate_obstacle(
    const shading_panel_field_t *field,
    const shading_obstacle_t *obstacle,
    const solar_position_t *sun,
    shading_result_t *out_result) {
    if (!field || !obstacle || !sun || !out_result) return;
    std::memset(out_result, 0, sizeof(*out_result));
    out_result->sun_elevation_deg = sun->elevation_deg;
    out_result->sun_azimuth_deg = sun->azimuth_deg;

    if (sun->elevation_deg <= 0.0) {
        out_result->shaded_fraction = 1.0;
        out_result->self_shading_occurs = true;
        return;
    }

    /* Distance from panel field to obstacle center. */
    double obs_cx = 0.0, obs_cy = 0.0;
    for (size_t i = 0; i < obstacle->vertex_count; i++) {
        obs_cx += obstacle->vertices[i].x_mm;
        obs_cy += obstacle->vertices[i].y_mm;
    }
    if (obstacle->vertex_count > 0) {
        obs_cx /= obstacle->vertex_count;
        obs_cy /= obstacle->vertex_count;
    }

    double dx = obs_cx - field->x_mm;
    double dy = obs_cy - field->y_mm;
    double distance = std::sqrt(dx * dx + dy * dy);
    if (distance < 1.0) {
        out_result->shaded_fraction = 0.0;
        return;
    }

    /* Azimuth from panel to obstacle. */
    double azimuth_to_obstacle = rad_to_deg(std::atan2(dy, dx));
    if (azimuth_to_obstacle < 0.0) azimuth_to_obstacle += 360.0;

    /* Only shade if the obstacle is in the general direction of the sun. */
    double azimuth_diff = std::fabs(azimuth_to_obstacle - sun->azimuth_deg);
    if (azimuth_diff > 180.0) azimuth_diff = 360.0 - azimuth_diff;
    if (azimuth_diff > 90.0) {
        out_result->shaded_fraction = 0.0;
        return;
    }

    double elevation_rad = deg_to_rad(sun->elevation_deg);
    double shadow_length = obstacle->height_mm / std::tan(elevation_rad);
    double obstacle_width = polygon_bounding_width(obstacle);

    /* Estimate the fraction of the panel field width shaded by the obstacle shadow. */
    double panel_width = std::max(field->width_mm, 1.0);
    double shaded_width = std::min(obstacle_width + shadow_length * 0.1, panel_width);
    double shaded_fraction = clamp(shaded_width / panel_width, 0.0, 1.0);

    out_result->shaded_fraction = shaded_fraction;
    out_result->self_shading_occurs = shaded_fraction > 0.01;
    out_result->critical_sun_angle_deg = rad_to_deg(std::atan2(obstacle->height_mm, distance));
}

double shading_minimum_row_spacing_for_no_shading(
    double panel_height_mm,
    double tilt_deg,
    double min_sun_elevation_deg) {
    if (panel_height_mm <= 0.0 || min_sun_elevation_deg <= 0.0 || min_sun_elevation_deg >= 90.0) {
        return 0.0;
    }
    double tilt_rad = deg_to_rad(tilt_deg);
    double elevation_rad = deg_to_rad(min_sun_elevation_deg);
    double collector_height = panel_height_mm * std::sin(tilt_rad);
    return collector_height / std::tan(elevation_rad);
}

double shading_critical_sun_elevation(
    double row_spacing_mm,
    double panel_height_mm,
    double tilt_deg) {
    if (row_spacing_mm <= 0.0 || panel_height_mm <= 0.0) return 90.0;
    double tilt_rad = deg_to_rad(tilt_deg);
    double collector_height = panel_height_mm * std::sin(tilt_rad);
    return rad_to_deg(std::atan2(collector_height, row_spacing_mm));
}

double shading_annual_loss_factor(
    const shading_field_layout_t *layout,
    double latitude_deg,
    double longitude_deg,
    double timezone_offset_h) {
    if (!layout) return 0.0;

    solar_location_t location;
    location.latitude_deg = latitude_deg;
    location.longitude_deg = longitude_deg;
    location.timezone_offset_h = timezone_offset_h;

    /* Sample 24 hours for the 21st of each month. */
    double total_shaded_energy = 0.0;
    double total_energy = 0.0;
    int months[] = {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12};

    for (int m = 0; m < 12; m++) {
        for (int h = 0; h < 24; h++) {
            solar_date_time_t dt;
            dt.year = 2026;
            dt.month = months[m];
            dt.day = 21;
            dt.hour = h;
            dt.minute = 0;
            dt.second = 0.0;

            solar_position_t sun;
            solar_position_calculate(&location, &dt, &sun);

            shading_result_t result;
            shading_calculate_row_to_row(layout, &sun, &result);

            double weight = sun.elevation_deg > 0.0 ? std::sin(deg_to_rad(sun.elevation_deg)) : 0.0;
            total_shaded_energy += result.shaded_fraction * weight;
            total_energy += weight;
        }
    }

    if (total_energy <= 0.0) return 0.0;
    return total_shaded_energy / total_energy;
}
