#include "solar_project_state.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <vector>

static uint64_t g_next_guid_low = 0x10000000;

static void copy_string(char *dest, size_t dest_size, const char *src) {
    if (!dest || dest_size == 0) return;
    if (!src) {
        dest[0] = '\0';
        return;
    }
    size_t len = std::strlen(src);
    if (len >= dest_size) len = dest_size - 1;
    std::memcpy(dest, src, len);
    dest[len] = '\0';
}

static void make_guid(solar_guid_t *guid) {
    if (!guid) return;
    guid->id_high = 0;
    guid->id_low = g_next_guid_low++;
}

static bool guid_is_zero(const solar_guid_t *guid) {
    return !guid || (guid->id_high == 0 && guid->id_low == 0);
}

static void get_panel_footprint(
    const solar_panel_definition_t *def,
    int rotation_degrees,
    double *out_width,
    double *out_height) {
    int rot = ((rotation_degrees % 180) + 180) % 180;
    if (rot == 90) {
        *out_width = def->height_mm;
        *out_height = def->width_mm;
    } else {
        *out_width = def->width_mm;
        *out_height = def->height_mm;
    }
}

static double total_dc_watts_from_graph(
    const solar_electrical_graph_t *graph,
    const solar_definition_catalog_t *catalog) {
    double total = 0.0;
    for (size_t i = 0; i < graph->component_count; ++i) {
        if (graph->components[i].kind != SOLAR_COMPONENT_PANEL) continue;
        const solar_panel_instance_t *panel = &graph->components[i].data.panel;
        const solar_panel_definition_t *def = solar_definition_catalog_find(catalog, &panel->definition_id);
        if (def) total += def->pmax_watts;
    }
    return total;
}

static void load_builtin_definitions(solar_project_state_t *state) {
    solar_panel_definition_t def;
    solar_panel_definition_boviet_270(&def);
    solar_project_state_add_definition(state, &def);
    solar_panel_definition_generic_400(&def);
    solar_project_state_add_definition(state, &def);
    solar_panel_definition_generic_550(&def);
    solar_project_state_add_definition(state, &def);
    solar_panel_definition_generic_650(&def);
    solar_project_state_add_definition(state, &def);
}

void solar_project_state_init(solar_project_state_t *state) {
    if (!state) return;
    solar_project_state_clear(state);
    make_guid(&state->project_id);
    copy_string(state->name, sizeof(state->name), "Untitled");
    state->schema_version = 10;
    load_builtin_definitions(state);
}

void solar_project_state_clear(solar_project_state_t *state) {
    if (!state) return;
    state->schema_version = 10;
    state->project_id = {0, 0};
    state->name[0] = '\0';
    state->file_path[0] = '\0';
    solar_definition_catalog_init(&state->definitions);
    solar_electrical_graph_init(&state->graph);
    roof_document_init(&state->roof);
    solar_site_conditions_init(&state->site);
    state->canvas.show_grid = true;
    state->canvas.snap_to_grid = false;
    state->canvas.panel_snapping = true;
    state->canvas.electrical_terminal_snapping = true;
    state->canvas.panel_spacing_mm = 20.0;
    state->canvas.grid_size_mm = 100.0;
    state->canvas.zoom = 1.0;
    state->canvas.camera_x_mm = 0.0;
    state->canvas.camera_y_mm = 0.0;
    state->racking.rafter_spacing_mm = 610.0;
    state->racking.rail_overhang_mm = 200.0;
    state->racking.attachment_edge_offset_mm = 150.0;
    state->equipment.clear();
    state->racking_layout = {};
}

const solar_panel_definition_t *solar_project_state_find_definition(
    const solar_project_state_t *state,
    const solar_guid_t *id) {
    if (!state || !id) return NULL;
    return solar_definition_catalog_find(&state->definitions, id);
}

bool solar_project_state_add_definition(
    solar_project_state_t *state,
    const solar_panel_definition_t *def) {
    if (!state || !def) return false;
    return solar_definition_catalog_add(&state->definitions, def);
}

bool solar_project_state_remove_definition(
    solar_project_state_t *state,
    const solar_guid_t *id) {
    if (!state || !id) return false;
    for (size_t i = 0; i < state->definitions.count; ++i) {
        if (solar_panel_guid_equals(&state->definitions.definitions[i].id, id)) {
            if (i + 1 < state->definitions.count) {
                std::memmove(
                    &state->definitions.definitions[i],
                    &state->definitions.definitions[i + 1],
                    (state->definitions.count - i - 1) * sizeof(solar_panel_definition_t));
            }
            state->definitions.count--;
            return true;
        }
    }
    return false;
}

solar_panel_instance_t *solar_project_state_add_panel(
    solar_project_state_t *state,
    const solar_guid_t *definition_id,
    double x_mm,
    double y_mm,
    int rotation_degrees,
    const solar_guid_t *id) {
    if (!state || !definition_id) return NULL;
    if (!solar_project_state_find_definition(state, definition_id)) return NULL;

    solar_guid_t panel_id;
    if (id && !guid_is_zero(id)) {
        panel_id = *id;
    } else {
        make_guid(&panel_id);
    }

    solar_panel_instance_t panel;
    solar_panel_instance_init(&panel, &panel_id, definition_id, x_mm, y_mm, rotation_degrees);
    if (!solar_electrical_graph_add_panel(&state->graph, &panel)) return NULL;
    int idx = -1;
    for (size_t i = 0; i < state->graph.component_count; ++i) {
        if (solar_panel_guid_equals(&state->graph.components[i].id, &panel_id)) {
            idx = (int)i;
            break;
        }
    }
    return idx >= 0 ? &state->graph.components[idx].data.panel : NULL;
}

bool solar_project_state_remove_panel(
    solar_project_state_t *state,
    const solar_guid_t *panel_id) {
    if (!state || !panel_id) return false;
    return solar_electrical_graph_remove_panel(&state->graph, panel_id);
}

bool solar_project_state_try_connect(
    solar_project_state_t *state,
    const solar_guid_t *start_port_id,
    const solar_guid_t *end_port_id,
    double length_mm,
    int gauge_awg) {
    if (!state || !start_port_id || !end_port_id) return false;
    return solar_electrical_graph_try_connect(&state->graph, start_port_id, end_port_id, length_mm, gauge_awg);
}

bool solar_project_state_disconnect(
    solar_project_state_t *state,
    const solar_guid_t *connection_id) {
    if (!state || !connection_id) return false;
    return solar_electrical_graph_disconnect(&state->graph, connection_id);
}

solar_equipment_instance_t *solar_project_state_add_equipment(
    solar_project_state_t *state,
    const solar_equipment_instance_t *equipment) {
    if (!state || !equipment) return NULL;
    if (state->equipment.size() >= SOLAR_MAX_COMPONENTS) return NULL;
    state->equipment.push_back(*equipment);
    return &state->equipment.back();
}

bool solar_project_state_remove_equipment(
    solar_project_state_t *state,
    const solar_guid_t *equipment_id) {
    if (!state || !equipment_id) return false;
    for (auto it = state->equipment.begin(); it != state->equipment.end(); ++it) {
        if (solar_panel_guid_equals(&it->id, equipment_id)) {
            state->equipment.erase(it);
            return true;
        }
    }
    return false;
}

solar_project_result_t solar_project_state_calculate(
    const solar_project_state_t *state) {
    solar_project_result_t result = {};
    if (!state) return result;
    solar_calculate_project(&state->graph, &state->definitions, &result);
    return result;
}

void solar_project_state_get_energy_estimate(
    const solar_project_state_t *state,
    solar_energy_estimate_t *out) {
    if (!out) return;
    double total = state ? total_dc_watts_from_graph(&state->graph, &state->definitions) : 0.0;
    solar_energy_estimate_simple(total, state ? &state->site : NULL, out);
}

void solar_project_state_get_detailed_production_estimate(
    const solar_project_state_t *state,
    solar_detailed_production_estimate_t *out) {
    if (!out) return;
    double total = state ? total_dc_watts_from_graph(&state->graph, &state->definitions) : 0.0;
    solar_detailed_production_estimate(total, state ? &state->site : NULL, out);
}

void solar_project_state_compute_racking_layout(
    solar_project_state_t *state) {
    if (!state) return;
    state->racking_layout = {};

    struct Row {
        double y_mm;
        int panel_count;
        double total_width_mm;
    };
    std::vector<Row> rows;
    const double row_tolerance_mm = 10.0;

    for (size_t i = 0; i < state->graph.component_count; ++i) {
        if (state->graph.components[i].kind != SOLAR_COMPONENT_PANEL) continue;
        const solar_panel_instance_t *panel = &state->graph.components[i].data.panel;
        const solar_panel_definition_t *def = solar_project_state_find_definition(state, &panel->definition_id);
        if (!def) continue;
        double w, h;
        get_panel_footprint(def, panel->rotation_degrees, &w, &h);

        bool found = false;
        for (auto &row : rows) {
            if (std::fabs(row.y_mm - panel->position_y_mm) <= row_tolerance_mm) {
                row.panel_count++;
                row.total_width_mm += w;
                found = true;
                break;
            }
        }
        if (!found) {
            rows.push_back({panel->position_y_mm, 1, w});
        }
    }

    if (rows.empty()) return;

    int rail_count = 0;
    double total_rail_mm = 0.0;
    int attachments = 0;
    int end_clamps = 0;
    int mid_clamps = 0;

    for (const auto &row : rows) {
        double rail_length_mm = row.total_width_mm + 2.0 * state->racking.rail_overhang_mm;
        rail_count += 2;
        total_rail_mm += 2.0 * rail_length_mm;

        double spacing = state->racking.rafter_spacing_mm;
        if (spacing <= 0.0) spacing = 610.0;
        int attachments_per_rail = static_cast<int>(std::max(2.0, std::floor(rail_length_mm / spacing) + 1.0));
        attachments += 2 * attachments_per_rail;

        end_clamps += 4;
        if (row.panel_count > 1) {
            mid_clamps += (row.panel_count - 1) * 2;
        }
    }

    state->racking_layout = {
        static_cast<int>(rows.size()),
        rail_count,
        total_rail_mm,
        attachments,
        end_clamps,
        mid_clamps,
        true
    };
}

void solar_project_state_create_demo_rectangular_roof(
    solar_project_state_t *state,
    double width_mm,
    double height_mm,
    double setback_mm) {
    if (!state) return;
    roof_document_init(&state->roof);
    roof_surface_t surface;
    roof_surface_init(&surface, "Main Roof");
    roof_point_t p;
    p = {0.0, 0.0}; roof_surface_add_vertex(&surface, &p);
    p = {width_mm, 0.0}; roof_surface_add_vertex(&surface, &p);
    p = {width_mm, height_mm}; roof_surface_add_vertex(&surface, &p);
    p = {0.0, height_mm}; roof_surface_add_vertex(&surface, &p);
    surface.setback_mm = setback_mm;
    roof_document_add_surface(&state->roof, &surface);
}

void solar_project_state_create_demo_l_shaped_roof(
    solar_project_state_t *state,
    double setback_mm) {
    if (!state) return;
    roof_document_init(&state->roof);
    roof_surface_t a;
    roof_surface_init(&a, "L Wing A");
    roof_point_t p;
    p = {0.0, 0.0}; roof_surface_add_vertex(&a, &p);
    p = {12000.0, 0.0}; roof_surface_add_vertex(&a, &p);
    p = {12000.0, 6000.0}; roof_surface_add_vertex(&a, &p);
    p = {0.0, 6000.0}; roof_surface_add_vertex(&a, &p);
    a.setback_mm = setback_mm;
    roof_document_add_surface(&state->roof, &a);

    roof_surface_t b;
    roof_surface_init(&b, "L Wing B");
    p = {0.0, 6000.0}; roof_surface_add_vertex(&b, &p);
    p = {5000.0, 6000.0}; roof_surface_add_vertex(&b, &p);
    p = {5000.0, 12000.0}; roof_surface_add_vertex(&b, &p);
    p = {0.0, 12000.0}; roof_surface_add_vertex(&b, &p);
    b.setback_mm = setback_mm;
    roof_document_add_surface(&state->roof, &b);
}

bool solar_project_state_evaluate_panel_placement(
    const solar_project_state_t *state,
    const solar_panel_instance_t *panel,
    double x_mm,
    double y_mm,
    bool *out_inside,
    double *out_distance_to_edge_mm) {
    if (!state || !panel) return false;
    const solar_panel_definition_t *def = solar_project_state_find_definition(state, &panel->definition_id);
    if (!def) return false;

    double w, h;
    get_panel_footprint(def, panel->rotation_degrees, &w, &h);
    roof_point_t center = {x_mm, y_mm};

    bool inside = false;
    double distance_mm = 0.0;
    for (size_t i = 0; i < state->roof.surface_count; ++i) {
        const roof_surface_t *surface = &state->roof.surfaces[i];
        if (roof_contains_panel_rect(surface, &center, w, h, panel->rotation_degrees)) {
            inside = true;
            double d = roof_distance_to_nearest_edge_mm(&center, surface->vertices, surface->vertex_count);
            if (distance_mm == 0.0 || d < distance_mm) distance_mm = d;
        }
    }

    bool overlap = roof_panel_overlaps_obstacle(&state->roof, &center, w, h, panel->rotation_degrees);
    if (overlap) inside = false;

    if (out_inside) *out_inside = inside;
    if (out_distance_to_edge_mm) *out_distance_to_edge_mm = distance_mm;
    return true;
}
