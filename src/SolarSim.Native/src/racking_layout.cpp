#include "racking_layout.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <vector>

struct RackingFootprint {
    double x;
    double y;
    double w;
    double h;
    double center_y;
    size_t panel_index;
};

static int rotation_mod_180(int rotation_degrees) {
    int rot = rotation_degrees % 180;
    if (rot < 0) rot += 180;
    return rot;
}

static bool footprint_dimensions(
    const solar_panel_instance_t *panel,
    const solar_panel_definition_t *def,
    double *out_w,
    double *out_h) {
    if (!panel || !def || !out_w || !out_h) return false;
    int rot = rotation_mod_180(panel->rotation_degrees);
    if (rot == 90) {
        *out_w = def->height_mm;
        *out_h = def->width_mm;
    } else {
        *out_w = def->width_mm;
        *out_h = def->height_mm;
    }
    return true;
}

static const solar_panel_definition_t *find_definition(
    const solar_panel_definition_t *definitions,
    size_t definition_count,
    const solar_guid_t *id) {
    if (!definitions || !id) return NULL;
    for (size_t i = 0; i < definition_count; i++) {
        if (solar_panel_guid_equals(&definitions[i].id, id)) {
            return &definitions[i];
        }
    }
    return NULL;
}

static bool add_attachment(
    solar_racking_layout_result_t *result,
    double x_mm,
    double y_mm) {
    if (!result || result->attachment_count >= SOLAR_RACKING_MAX_ATTACHMENTS) return false;
    roof_point_set(&result->attachment_points[result->attachment_count], x_mm, y_mm);
    result->attachment_count++;
    return true;
}

static double place_attachments_along_rail(
    solar_racking_layout_result_t *result,
    double start_x,
    double end_x,
    double y_mm,
    double spacing_mm) {
    if (!result) return 0.0;
    if (end_x <= start_x) {
        add_attachment(result, (start_x + end_x) * 0.5, y_mm);
        return 0.0;
    }

    double length = end_x - start_x;
    double first = start_x + std::min(spacing_mm * 0.25, length * 0.5);
    add_attachment(result, first, y_mm);
    double prev = first;
    double max_span = 0.0;

    for (double x = start_x + spacing_mm; x < end_x - spacing_mm * 0.2; x += spacing_mm) {
        add_attachment(result, x, y_mm);
        if (x - prev > max_span) max_span = x - prev;
        prev = x;
    }

    double last = end_x - std::min(spacing_mm * 0.25, length * 0.5);
    if (std::fabs(last - first) > spacing_mm * 0.35) {
        add_attachment(result, last, y_mm);
        if (last - prev > max_span) max_span = last - prev;
    }

    return max_span;
}

static int estimate_rail_splices(double rail_length_mm, double max_stock_length_mm) {
    if (rail_length_mm <= 0.0 || max_stock_length_mm <= 0.0) return 0;
    int pieces = static_cast<int>(std::ceil(rail_length_mm / max_stock_length_mm));
    if (pieces < 1) pieces = 1;
    return pieces - 1;
}

static bool build_footprints(
    const solar_panel_instance_t *panels,
    size_t panel_count,
    const solar_panel_definition_t *definitions,
    size_t definition_count,
    std::vector<RackingFootprint> *out_footprints) {
    if (!out_footprints) return false;
    out_footprints->clear();
    for (size_t i = 0; i < panel_count; i++) {
        const solar_panel_instance_t *panel = &panels[i];
        const solar_panel_definition_t *def = find_definition(
            definitions, definition_count, &panel->definition_id);
        if (!def) continue;
        double w, h;
        if (!footprint_dimensions(panel, def, &w, &h)) continue;
        RackingFootprint fp;
        fp.x = panel->position_x_mm;
        fp.y = panel->position_y_mm;
        fp.w = w;
        fp.h = h;
        fp.center_y = panel->position_y_mm + h * 0.5;
        fp.panel_index = i;
        out_footprints->push_back(fp);
    }
    return !out_footprints->empty();
}

static std::vector<std::vector<size_t>> group_rows(
    std::vector<RackingFootprint> *footprints) {
    std::vector<std::vector<size_t>> rows;
    if (!footprints || footprints->empty()) return rows;

    std::sort(footprints->begin(), footprints->end(),
        [](const RackingFootprint &a, const RackingFootprint &b) {
            if (a.center_y != b.center_y) return a.center_y < b.center_y;
            return a.x < b.x;
        });

    for (size_t i = 0; i < footprints->size(); i++) {
        const RackingFootprint &fp = (*footprints)[i];
        double tolerance = std::max(80.0, fp.h * 0.35);
        size_t matched_row = rows.size();
        for (size_t r = 0; r < rows.size(); r++) {
            const RackingFootprint &first = (*footprints)[rows[r][0]];
            if (std::fabs(first.center_y - fp.center_y) <= tolerance) {
                matched_row = r;
                break;
            }
        }
        if (matched_row == rows.size()) {
            rows.emplace_back();
        }
        rows[matched_row].push_back(i);
    }
    return rows;
}

static bool compute_row_layout(
    const std::vector<RackingFootprint> &footprints,
    const std::vector<size_t> &row,
    double spacing_mm,
    double overhang_mm,
    double edge_offset_mm,
    double max_stock_length_mm,
    solar_racking_layout_result_t *result) {
    if (row.empty() || !result) return false;

    std::vector<size_t> sorted_row = row;
    std::sort(sorted_row.begin(), sorted_row.end(),
        [&footprints](size_t a, size_t b) {
            return footprints[a].x < footprints[b].x;
        });

    double min_x = footprints[sorted_row[0]].x;
    double max_x = footprints[sorted_row[0]].x + footprints[sorted_row[0]].w;
    double min_y = footprints[sorted_row[0]].y;
    double max_y = footprints[sorted_row[0]].y + footprints[sorted_row[0]].h;

    for (size_t idx : sorted_row) {
        const RackingFootprint &fp = footprints[idx];
        if (fp.x < min_x) min_x = fp.x;
        if (fp.x + fp.w > max_x) max_x = fp.x + fp.w;
        if (fp.y < min_y) min_y = fp.y;
        if (fp.y + fp.h > max_y) max_y = fp.y + fp.h;
    }

    double height = max_y - min_y;
    double inset = std::min(edge_offset_mm, std::max(0.0, height * 0.5 - 1.0));
    double rail_y_bottom = min_y + inset;
    double rail_y_top = max_y - inset;
    double rail_start_x = min_x - overhang_mm;
    double rail_end_x = max_x + overhang_mm;
    double rail_len = std::max(0.0, rail_end_x - rail_start_x);

    double span_top = place_attachments_along_rail(
        result, rail_start_x, rail_end_x, rail_y_top, spacing_mm);
    double span_bottom = place_attachments_along_rail(
        result, rail_start_x, rail_end_x, rail_y_bottom, spacing_mm);

    double row_max_span = std::max(span_top, span_bottom);
    if (row_max_span > result->max_unsupported_span_mm) {
        result->max_unsupported_span_mm = row_max_span;
    }

    result->rail_count += 2;
    result->total_rail_length_mm += 2.0 * rail_len;
    result->end_clamp_count += 4;
    if (sorted_row.size() > 1) {
        result->mid_clamp_count += static_cast<int>((sorted_row.size() - 1) * 2);
    }
    result->splice_count += 2 * estimate_rail_splices(rail_len, max_stock_length_mm);

    return true;
}

void solar_racking_parameters_defaults(solar_racking_parameters_t *params) {
    if (!params) return;
    params->rafter_spacing_mm = SOLAR_RACKING_DEFAULT_RAFTER_SPACING_MM;
    params->rail_overhang_mm = SOLAR_RACKING_DEFAULT_RAIL_OVERHANG_MM;
    params->attachment_edge_offset_mm = SOLAR_RACKING_DEFAULT_ATTACHMENT_EDGE_OFFSET_MM;
    params->max_rail_stock_length_mm = SOLAR_RACKING_DEFAULT_MAX_RAIL_STOCK_MM;
}

bool solar_racking_parameters_validate(const solar_racking_parameters_t *params) {
    if (!params) return false;
    if (!std::isfinite(params->rafter_spacing_mm) || params->rafter_spacing_mm < 50.0) return false;
    if (!std::isfinite(params->rail_overhang_mm) || params->rail_overhang_mm < 0.0) return false;
    if (!std::isfinite(params->attachment_edge_offset_mm) || params->attachment_edge_offset_mm < 0.0) return false;
    if (!std::isfinite(params->max_rail_stock_length_mm) || params->max_rail_stock_length_mm < 1000.0) return false;
    return true;
}

void solar_racking_parameters_copy(
    const solar_racking_parameters_t *source,
    solar_racking_parameters_t *dest) {
    if (!dest) return;
    if (!source) {
        solar_racking_parameters_defaults(dest);
        return;
    }
    std::memcpy(dest, source, sizeof(*dest));
}

void solar_racking_layout_result_init(solar_racking_layout_result_t *result) {
    if (!result) return;
    std::memset(result, 0, sizeof(*result));
}

int solar_racking_layout_result_total_clamps(const solar_racking_layout_result_t *result) {
    if (!result) return 0;
    return result->mid_clamp_count + result->end_clamp_count;
}

bool solar_racking_layout_result_attachment_spacing_ok(
    const solar_racking_layout_result_t *result,
    double rafter_spacing_mm) {
    if (!result) return false;
    if (result->attachment_count <= 1 || result->rail_count <= 0) return true;
    double avg_span = result->total_rail_length_mm / static_cast<double>(result->attachment_count - 1);
    return avg_span <= rafter_spacing_mm * 1.05;
}

bool solar_racking_layout_compute(
    const solar_panel_instance_t *panels,
    size_t panel_count,
    const solar_panel_definition_t *definitions,
    size_t definition_count,
    const solar_racking_parameters_t *params,
    solar_racking_layout_result_t *out) {
    if (!out) return false;
    solar_racking_layout_result_init(out);

    if (!panels || panel_count == 0 || definition_count == 0) return true;

    solar_racking_parameters_t defaults;
    solar_racking_parameters_defaults(&defaults);
    const solar_racking_parameters_t *p = params ? params : &defaults;
    if (!solar_racking_parameters_validate(p)) return false;

    double spacing = std::max(50.0, p->rafter_spacing_mm);
    double overhang = std::max(0.0, p->rail_overhang_mm);
    double edge_offset = std::max(0.0, p->attachment_edge_offset_mm);
    double max_stock = p->max_rail_stock_length_mm;

    std::vector<RackingFootprint> footprints;
    if (!build_footprints(panels, panel_count, definitions, definition_count, &footprints)) {
        return true;
    }

    std::vector<std::vector<size_t>> rows = group_rows(&footprints);

    for (size_t r = 0; r < rows.size(); r++) {
        compute_row_layout(
            footprints,
            rows[r],
            spacing,
            overhang,
            edge_offset,
            max_stock,
            out);
    }

    out->row_count = static_cast<int>(rows.size());
    return true;
}
