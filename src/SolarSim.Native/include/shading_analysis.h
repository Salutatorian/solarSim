#ifndef SHADING_ANALYSIS_H
#define SHADING_ANALYSIS_H

#include <stdbool.h>
#include <stddef.h>

#include "roof_geometry.h"
#include "solar_panel.h"
#include "solar_math.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Shading analysis for PV arrays: estimate row-to-row and obstacle shading
 * on a rectangular panel field. Mirrors the shading logic used in the
 * solarSim CAD layout helpers.
 */

#define SHADING_MAX_ROWS 128
#define SHADING_MAX_OBSTACLES 32
#define SHADING_MAX_STRINGS 64

typedef struct {
    double x_mm;
    double y_mm;
    double width_mm;
    double height_mm;
    double tilt_deg;
    double azimuth_deg;
    double ground_clearance_mm;
    int row_index;
} shading_panel_field_t;

typedef struct {
    roof_point_t vertices[ROOF_MAX_VERTICES];
    size_t vertex_count;
    double height_mm;
} shading_obstacle_t;

typedef struct {
    double row_spacing_mm;
    double row_tilt_deg;
    double panel_height_mm;
    double ground_clearance_mm;
    double azimuth_deg;
    double latitude_deg;
    double backtracking;
} shading_field_layout_t;

typedef struct {
    double shaded_fraction;
    double sun_elevation_deg;
    double sun_azimuth_deg;
    double critical_sun_angle_deg;
    bool self_shading_occurs;
} shading_result_t;

/* Compute shading on a single panel field from row spacing and sun position. */
void shading_calculate_row_to_row(
    const shading_field_layout_t *layout,
    const solar_position_t *sun,
    shading_result_t *out_result);

/* Compute shading from nearby obstacles (simplified 2.5D projection). */
void shading_calculate_obstacle(
    const shading_panel_field_t *field,
    const shading_obstacle_t *obstacle,
    const solar_position_t *sun,
    shading_result_t *out_result);

/* Estimate the annual shading factor for a fixed-tilt array at a given site. */
double shading_annual_loss_factor(
    const shading_field_layout_t *layout,
    double latitude_deg,
    double longitude_deg,
    double timezone_offset_h);

/* Helpers for layout optimization. */
double shading_minimum_row_spacing_for_no_shading(
    double panel_height_mm,
    double tilt_deg,
    double min_sun_elevation_deg);

double shading_critical_sun_elevation(
    double row_spacing_mm,
    double panel_height_mm,
    double tilt_deg);

#ifdef __cplusplus
}
#endif

#endif
