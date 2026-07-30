#ifndef RACKING_LAYOUT_H
#define RACKING_LAYOUT_H

#include <stdbool.h>
#include <stddef.h>

#include "roof_geometry.h"
#include "solar_panel.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Racking layout estimation: rails, attachments, clamps, and splices for
 * axis-aligned panel arrays. Mirrors SolarSim.Domain.Roof.RackingLayoutService
 * and RackingParameters.
 */

#define SOLAR_RACKING_DEFAULT_RAFTER_SPACING_MM 406.4
#define SOLAR_RACKING_DEFAULT_RAIL_OVERHANG_MM 150.0
#define SOLAR_RACKING_DEFAULT_ATTACHMENT_EDGE_OFFSET_MM 200.0
#define SOLAR_RACKING_DEFAULT_MAX_RAIL_STOCK_MM 4200.0
#define SOLAR_RACKING_MAX_ATTACHMENTS 1024

/* Project-level racking defaults. */
typedef struct {
    double rafter_spacing_mm;
    double rail_overhang_mm;
    double attachment_edge_offset_mm;
    double max_rail_stock_length_mm;
} solar_racking_parameters_t;

/* Layout result for a single array of panels. */
typedef struct {
    roof_point_t attachment_points[SOLAR_RACKING_MAX_ATTACHMENTS];
    size_t attachment_count;
    double total_rail_length_mm;
    int rail_count;
    int mid_clamp_count;
    int end_clamp_count;
    int splice_count;
    double max_unsupported_span_mm;
    int row_count;
} solar_racking_layout_result_t;

/* Initialize parameters to the project defaults. */
void solar_racking_parameters_defaults(solar_racking_parameters_t *params);

/* Validate that parameters are within sensible physical ranges. */
bool solar_racking_parameters_validate(const solar_racking_parameters_t *params);

/* Copy parameters from source to destination. */
void solar_racking_parameters_copy(
    const solar_racking_parameters_t *source,
    solar_racking_parameters_t *dest);

/* Initialize a result structure to empty. */
void solar_racking_layout_result_init(solar_racking_layout_result_t *result);

/* Return the total number of clamps (mid + end). */
int solar_racking_layout_result_total_clamps(const solar_racking_layout_result_t *result);

/* Return true if the average attachment spacing is not larger than the target
 * rafter spacing, within a small tolerance. */
bool solar_racking_layout_result_attachment_spacing_ok(
    const solar_racking_layout_result_t *result,
    double rafter_spacing_mm);

/* Compute rail runs, attachment points, clamps, and splices for an array of panels.
 * definitions is a parallel lookup table; each panel's definition_id is matched
 * against the supplied definitions to determine its physical footprint. */
bool solar_racking_layout_compute(
    const solar_panel_instance_t *panels,
    size_t panel_count,
    const solar_panel_definition_t *definitions,
    size_t definition_count,
    const solar_racking_parameters_t *params,
    solar_racking_layout_result_t *out);

#ifdef __cplusplus
}
#endif

#endif
