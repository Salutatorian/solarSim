#include "panel_port_layout.h"

#include <cmath>
#include <cstring>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

static int rotation_mod_180(int degrees) {
    int rot = degrees % 180;
    if (rot < 0) rot += 180;
    return rot;
}

static double normalize_rotation(double degrees) {
    double n = std::fmod(degrees, 360.0);
    if (n < 0.0) n += 360.0;
    if (std::fabs(n - 360.0) < 1e-9) n = 0.0;
    return n;
}

static void rotate_point_around_center(
    double px,
    double py,
    double cx,
    double cy,
    double radians,
    double *out_x,
    double *out_y) {
    double dx = px - cx;
    double dy = py - cy;
    *out_x = cx + dx * std::cos(radians) - dy * std::sin(radians);
    *out_y = cy + dx * std::sin(radians) + dy * std::cos(radians);
}

static void layout_from_dimensions(
    double width_mm,
    double height_mm,
    solar_panel_port_layout_t *out) {
    if (!out) return;
    std::memset(out, 0, sizeof(*out));

    if (!std::isfinite(width_mm) || width_mm <= 0.0) width_mm = 1.0;
    if (!std::isfinite(height_mm) || height_mm <= 0.0) height_mm = 1.0;

    double neg_x = width_mm * SOLAR_PANEL_PORT_NEG_FRACTION_ALONG_EDGE;
    double pos_x = width_mm * SOLAR_PANEL_PORT_POS_FRACTION_ALONG_EDGE;
    double edge_y = height_mm;
    double terminal_y = height_mm + SOLAR_PANEL_PORT_LEAD_LENGTH_MM;

    out->neg_local_x_mm = neg_x;
    out->neg_local_y_mm = terminal_y;
    out->pos_local_x_mm = pos_x;
    out->pos_local_y_mm = terminal_y;
    out->neg_lead_start_x_mm = neg_x;
    out->neg_lead_start_y_mm = edge_y;
    out->pos_lead_start_x_mm = pos_x;
    out->pos_lead_start_y_mm = edge_y;
    out->exit_normal_x = 0.0;
    out->exit_normal_y = 1.0;
}

void solar_panel_port_layout_for_axis_aligned(
    double width_mm,
    double height_mm,
    solar_panel_port_layout_t *out) {
    layout_from_dimensions(width_mm, height_mm, out);
}

void solar_panel_port_layout_for_rotated(
    double width_mm,
    double height_mm,
    int rotation_degrees,
    solar_panel_port_layout_t *out) {
    if (!out) return;
    layout_from_dimensions(width_mm, height_mm, out);

    double cx = width_mm * 0.5;
    double cy = height_mm * 0.5;
    double rad = normalize_rotation(static_cast<double>(rotation_degrees)) * M_PI / 180.0;

    rotate_point_around_center(out->neg_local_x_mm, out->neg_local_y_mm, cx, cy, rad,
        &out->neg_local_x_mm, &out->neg_local_y_mm);
    rotate_point_around_center(out->pos_local_x_mm, out->pos_local_y_mm, cx, cy, rad,
        &out->pos_local_x_mm, &out->pos_local_y_mm);
    rotate_point_around_center(out->neg_lead_start_x_mm, out->neg_lead_start_y_mm, cx, cy, rad,
        &out->neg_lead_start_x_mm, &out->neg_lead_start_y_mm);
    rotate_point_around_center(out->pos_lead_start_x_mm, out->pos_lead_start_y_mm, cx, cy, rad,
        &out->pos_lead_start_x_mm, &out->pos_lead_start_y_mm);
    rotate_point_around_center(out->exit_normal_x, out->exit_normal_y, 0.0, 0.0, rad,
        &out->exit_normal_x, &out->exit_normal_y);
}

void solar_panel_port_layout_for_instance(
    const solar_panel_instance_t *panel,
    const solar_panel_definition_t *definition,
    solar_panel_port_layout_t *out) {
    if (!out) return;
    double width_mm = 1.0;
    double height_mm = 1.0;
    if (definition) {
        width_mm = definition->width_mm;
        height_mm = definition->height_mm;
    }
    if (panel) {
        int rot = rotation_mod_180(panel->rotation_degrees);
        if (rot == 90) {
            double tmp = width_mm;
            width_mm = height_mm;
            height_mm = tmp;
        }
    }
    layout_from_dimensions(width_mm, height_mm, out);
}

void solar_panel_port_layout_to_world(
    const solar_panel_port_layout_t *layout,
    double width_mm,
    double height_mm,
    double center_x_mm,
    double center_y_mm,
    int rotation_degrees,
    solar_panel_port_world_positions_t *out) {
    if (!layout || !out) return;
    std::memset(out, 0, sizeof(*out));

    if (!std::isfinite(width_mm) || width_mm <= 0.0) width_mm = 1.0;
    if (!std::isfinite(height_mm) || height_mm <= 0.0) height_mm = 1.0;

    double local_cx = width_mm * 0.5;
    double local_cy = height_mm * 0.5;
    double rad = normalize_rotation(static_cast<double>(rotation_degrees)) * M_PI / 180.0;

    rotate_point_around_center(layout->neg_local_x_mm, layout->neg_local_y_mm,
        local_cx, local_cy, rad, &out->neg_world_x_mm, &out->neg_world_y_mm);
    out->neg_world_x_mm += center_x_mm - local_cx;
    out->neg_world_y_mm += center_y_mm - local_cy;

    rotate_point_around_center(layout->pos_local_x_mm, layout->pos_local_y_mm,
        local_cx, local_cy, rad, &out->pos_world_x_mm, &out->pos_world_y_mm);
    out->pos_world_x_mm += center_x_mm - local_cx;
    out->pos_world_y_mm += center_y_mm - local_cy;

    rotate_point_around_center(layout->neg_lead_start_x_mm, layout->neg_lead_start_y_mm,
        local_cx, local_cy, rad, &out->neg_lead_start_world_x_mm, &out->neg_lead_start_world_y_mm);
    out->neg_lead_start_world_x_mm += center_x_mm - local_cx;
    out->neg_lead_start_world_y_mm += center_y_mm - local_cy;

    rotate_point_around_center(layout->pos_lead_start_x_mm, layout->pos_lead_start_y_mm,
        local_cx, local_cy, rad, &out->pos_lead_start_world_x_mm, &out->pos_lead_start_world_y_mm);
    out->pos_lead_start_world_x_mm += center_x_mm - local_cx;
    out->pos_lead_start_world_y_mm += center_y_mm - local_cy;
}

void solar_panel_port_layout_for_instance_world(
    const solar_panel_instance_t *panel,
    const solar_panel_definition_t *definition,
    solar_panel_port_world_positions_t *out) {
    if (!out) return;
    std::memset(out, 0, sizeof(*out));
    if (!panel) return;

    double width_mm = 1.0;
    double height_mm = 1.0;
    if (definition) {
        width_mm = definition->width_mm;
        height_mm = definition->height_mm;
    }
    int rotation = panel->rotation_degrees;
    if (rotation_mod_180(rotation) == 90) {
        double tmp = width_mm;
        width_mm = height_mm;
        height_mm = tmp;
    }

    solar_panel_port_layout_t layout;
    layout_from_dimensions(width_mm, height_mm, &layout);
    solar_panel_port_layout_to_world(
        &layout,
        width_mm,
        height_mm,
        panel->position_x_mm,
        panel->position_y_mm,
        rotation,
        out);
}

bool solar_panel_port_layout_hit_test(
    const solar_panel_port_layout_t *layout,
    double local_x_mm,
    double local_y_mm,
    double hit_radius_mm,
    bool *out_is_positive) {
    if (!layout || !std::isfinite(hit_radius_mm) || hit_radius_mm < 0.0) return false;
    if (!std::isfinite(local_x_mm) || !std::isfinite(local_y_mm)) return false;

    double radius_sq = hit_radius_mm * hit_radius_mm;
    double dx_neg = local_x_mm - layout->neg_local_x_mm;
    double dy_neg = local_y_mm - layout->neg_local_y_mm;
    double dist_neg_sq = dx_neg * dx_neg + dy_neg * dy_neg;

    double dx_pos = local_x_mm - layout->pos_local_x_mm;
    double dy_pos = local_y_mm - layout->pos_local_y_mm;
    double dist_pos_sq = dx_pos * dx_pos + dy_pos * dy_pos;

    bool neg_hit = dist_neg_sq <= radius_sq;
    bool pos_hit = dist_pos_sq <= radius_sq;

    if (out_is_positive) {
        *out_is_positive = pos_hit;
    }
    return neg_hit || pos_hit;
}

double solar_panel_port_layout_terminal_spacing_mm(const solar_panel_port_layout_t *layout) {
    if (!layout) return 0.0;
    double dx = layout->pos_local_x_mm - layout->neg_local_x_mm;
    double dy = layout->pos_local_y_mm - layout->neg_local_y_mm;
    return std::sqrt(dx * dx + dy * dy);
}
