#ifndef PANEL_PORT_LAYOUT_H
#define PANEL_PORT_LAYOUT_H

#include <stdbool.h>
#include <stddef.h>

#include "solar_panel.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Visual-only PV terminal layout for a panel AABB.
 * Mirrors SolarSim.Domain.Electrical.PanelPortLayoutService.
 * These coordinates are for rendering and hit-testing; electrical topology
 * must not depend on them.
 */

#define SOLAR_PANEL_PORT_NEG_FRACTION_ALONG_EDGE 0.42
#define SOLAR_PANEL_PORT_POS_FRACTION_ALONG_EDGE 0.58
#define SOLAR_PANEL_PORT_LEAD_LENGTH_MM 48.0
#define SOLAR_PANEL_PORT_VISIBLE_CIRCLE_DIAMETER_PX 8.0
#define SOLAR_PANEL_PORT_HIT_TARGET_SIZE_PX 22.0

/* Local layout in panel space: origin is the top-left of the axis-aligned body,
 * +X right, +Y down. Both terminals sit on the bottom service edge. */
typedef struct {
    double neg_local_x_mm;
    double neg_local_y_mm;
    double pos_local_x_mm;
    double pos_local_y_mm;
    double neg_lead_start_x_mm;
    double neg_lead_start_y_mm;
    double pos_lead_start_x_mm;
    double pos_lead_start_y_mm;
    double exit_normal_x;
    double exit_normal_y;
} solar_panel_port_layout_t;

/* World-space positions of the PV terminals after rotating the panel around
 * its center. */
typedef struct {
    double neg_world_x_mm;
    double neg_world_y_mm;
    double pos_world_x_mm;
    double pos_world_y_mm;
    double neg_lead_start_world_x_mm;
    double neg_lead_start_world_y_mm;
    double pos_lead_start_world_x_mm;
    double pos_lead_start_world_y_mm;
} solar_panel_port_world_positions_t;

/* Compute port positions for an axis-aligned panel body of the given size.
 * width_mm and height_mm are the displayed AABB (already swapped when rotated). */
void solar_panel_port_layout_for_axis_aligned(
    double width_mm,
    double height_mm,
    solar_panel_port_layout_t *out);

/* Compute port positions for a panel body and then rotate the lead points
 * around the panel center by rotation_degrees. */
void solar_panel_port_layout_for_rotated(
    double width_mm,
    double height_mm,
    int rotation_degrees,
    solar_panel_port_layout_t *out);

/* Compute port positions for a placed panel instance, using its definition to
 * obtain the physical dimensions and accounting for 90-degree rotation. */
void solar_panel_port_layout_for_instance(
    const solar_panel_instance_t *panel,
    const solar_panel_definition_t *definition,
    solar_panel_port_layout_t *out);

/* Transform a local (unrotated) layout into world coordinates given the panel
 * body size, center, and rotation. */
void solar_panel_port_layout_to_world(
    const solar_panel_port_layout_t *layout,
    double width_mm,
    double height_mm,
    double center_x_mm,
    double center_y_mm,
    int rotation_degrees,
    solar_panel_port_world_positions_t *out);

/* Compute world positions directly for a placed panel instance. */
void solar_panel_port_layout_for_instance_world(
    const solar_panel_instance_t *panel,
    const solar_panel_definition_t *definition,
    solar_panel_port_world_positions_t *out);

/* Hit-test a point (in panel-local mm) against the PV+ or PV- terminal. */
bool solar_panel_port_layout_hit_test(
    const solar_panel_port_layout_t *layout,
    double local_x_mm,
    double local_y_mm,
    double hit_radius_mm,
    bool *out_is_positive);

/* Return the Euclidean distance between the PV+ and PV- terminals in the
 * given local layout. */
double solar_panel_port_layout_terminal_spacing_mm(const solar_panel_port_layout_t *layout);

#ifdef __cplusplus
}
#endif

#endif
