#include "project_validator.h"

#include <cstdio>
#include <cstring>
#include <cmath>

#include "electrical_graph.h"
#include "roof_geometry.h"
#include "string_calculation.h"

void project_validator_add_issue(
    project_validator_result_t *result,
    project_validator_severity_t severity,
    const char *code,
    const char *message) {
    if (!result || !code || !message) return;
    if (result->issue_count >= PROJECT_VALIDATOR_MAX_ISSUES) return;

    project_validator_issue_t *issue = &result->issues[result->issue_count++];
    issue->severity = severity;
    std::strncpy(issue->code, code, sizeof(issue->code) - 1);
    issue->code[sizeof(issue->code) - 1] = '\0';
    std::strncpy(issue->message, message, sizeof(issue->message) - 1);
    issue->message[sizeof(issue->message) - 1] = '\0';

    if (severity == PROJECT_VALIDATOR_ERROR) result->error_count++;
    else if (severity == PROJECT_VALIDATOR_WARNING) result->warning_count++;
    else result->info_count++;
}

bool project_validator_check_site_temperatures(
    const solar_project_state_t *state,
    project_validator_result_t *out_result) {
    if (!state || !out_result) return false;
    if (state->site.min_ambient_c < -60.0 || state->site.min_ambient_c > 60.0) {
        project_validator_add_issue(out_result, PROJECT_VALIDATOR_ERROR,
            "SITE_TEMP_RANGE", "Site min ambient temperature is outside a realistic range.");
        return false;
    }
    if (state->site.hot_cell_c < -20.0 || state->site.hot_cell_c > 120.0) {
        project_validator_add_issue(out_result, PROJECT_VALIDATOR_ERROR,
            "SITE_TEMP_RANGE", "Site hot cell temperature is outside a realistic range.");
        return false;
    }
    if (state->site.hot_cell_c <= state->site.min_ambient_c) {
        project_validator_add_issue(out_result, PROJECT_VALIDATOR_WARNING,
            "SITE_TEMP_ORDER", "Hot cell temperature should be greater than min ambient temperature.");
    }
    return true;
}

bool project_validator_check_inverter_dc_voltage(
    const solar_project_state_t *state,
    project_validator_result_t *out_result) {
    if (!state || !out_result) return false;

    /* Check each string against a conservative default inverter limit. */
    const double DEFAULT_MAX_DC_VOLTS = 600.0;
    const double DEFAULT_MPPT_MIN_VOLTS = 200.0;
    const double DEFAULT_MPPT_MAX_VOLTS = 500.0;

    for (size_t s = 0; s < state->graph.string_count; s++) {
        const solar_pv_string_t *str = &state->graph.strings[s];
        double cold_voc = 0.0;
        double hot_vmp = 0.0;
        for (size_t p = 0; p < str->panel_count; p++) {
            const solar_component_t *comp = solar_electrical_graph_find_component(&state->graph, &str->panel_ids[p]);
            if (!comp || comp->kind != SOLAR_COMPONENT_PANEL) continue;
            const solar_panel_definition_t *def = solar_definition_catalog_find(&state->definitions, &comp->data.panel.definition_id);
            if (!def) continue;
            cold_voc += solar_cold_voc_volts(def->voc_volts, def->temp_coeff_voc_pct_per_c,
                state->site.min_ambient_c - 25.0);
            hot_vmp += solar_hot_vmp_volts(def->vmp_volts, def->temp_coeff_vmp_pct_per_c,
                state->site.hot_cell_c - 25.0);
        }
        if (cold_voc > DEFAULT_MAX_DC_VOLTS) {
            project_validator_add_issue(out_result, PROJECT_VALIDATOR_ERROR,
                "INVERTER_MAX_DC", "String cold Voc exceeds default inverter maximum DC voltage.");
            return false;
        }
        if (hot_vmp < DEFAULT_MPPT_MIN_VOLTS || hot_vmp > DEFAULT_MPPT_MAX_VOLTS) {
            project_validator_add_issue(out_result, PROJECT_VALIDATOR_WARNING,
                "INVERTER_MPPT", "String hot Vmp is outside the default inverter MPPT window.");
        }
    }
    return true;
}

bool project_validator_check_mppt_window(
    const solar_project_state_t *state,
    project_validator_result_t *out_result) {
    return project_validator_check_inverter_dc_voltage(state, out_result);
}

bool project_validator_check_unconnected_panels(
    const solar_project_state_t *state,
    project_validator_result_t *out_result) {
    if (!state || !out_result) return false;
    bool all_ok = true;
    for (size_t i = 0; i < state->graph.component_count; i++) {
        if (state->graph.components[i].kind != SOLAR_COMPONENT_PANEL) continue;
        const solar_panel_instance_t *panel = &state->graph.components[i].data.panel;
        bool connected = false;
        for (size_t p = 0; p < panel->port_count; p++) {
            if (panel->ports[p].is_occupied) {
                connected = true;
                break;
            }
        }
        if (!connected) {
            project_validator_add_issue(out_result, PROJECT_VALIDATOR_INFO,
                "UNCONNECTED_PANEL", "A panel has no electrical connections.");
            all_ok = false;
        }
    }
    return all_ok;
}

bool project_validator_check_string_current(
    const solar_project_state_t *state,
    project_validator_result_t *out_result) {
    if (!state || !out_result) return false;
    for (size_t i = 0; i < state->graph.string_count; i++) {
        const solar_pv_string_t *str = &state->graph.strings[i];
        double min_isc = 1e9;
        for (size_t p = 0; p < str->panel_count; p++) {
            const solar_component_t *comp = solar_electrical_graph_find_component(&state->graph, &str->panel_ids[p]);
            if (!comp || comp->kind != SOLAR_COMPONENT_PANEL) continue;
            const solar_panel_definition_t *def = solar_definition_catalog_find(&state->definitions, &comp->data.panel.definition_id);
            if (!def) continue;
            if (def->isc_amps < min_isc) min_isc = def->isc_amps;
        }
        if (min_isc < 1e8) {
            for (size_t e = 0; e < state->equipment.size(); e++) {
                const solar_equipment_instance_t *eq = &state->equipment[e];
                if (eq->kind == SOLAR_EQUIPMENT_KIND_COMBINER && eq->rated_amps > 0 && min_isc > eq->rated_amps) {
                    project_validator_add_issue(out_result, PROJECT_VALIDATOR_ERROR,
                        "STRING_CURRENT", "String current exceeds combiner rating.");
                    return false;
                }
            }
        }
    }
    return true;
}

bool project_validator_check_panel_containment(
    const solar_project_state_t *state,
    project_validator_result_t *out_result) {
    if (!state || !out_result) return false;
    if (state->roof.surface_count == 0) return true;

    bool all_inside = true;
    for (size_t i = 0; i < state->graph.component_count; i++) {
        if (state->graph.components[i].kind != SOLAR_COMPONENT_PANEL) continue;
        const solar_panel_instance_t *panel = &state->graph.components[i].data.panel;
        bool inside_any = false;
        for (size_t s = 0; s < state->roof.surface_count; s++) {
            roof_point_t center = {panel->position_x_mm, panel->position_y_mm};
            double width = 1000.0, height = 1700.0;
            const solar_panel_definition_t *def = solar_definition_catalog_find(&state->definitions, &panel->definition_id);
            if (def) {
                width = def->width_mm;
                height = def->height_mm;
            }
            if (roof_contains_panel_rect(&state->roof.surfaces[s], &center, width, height, panel->rotation_degrees)) {
                inside_any = true;
                break;
            }
        }
        if (!inside_any) {
            project_validator_add_issue(out_result, PROJECT_VALIDATOR_WARNING,
                "PANEL_OUTSIDE_ROOF", "A panel is not fully contained within a roof surface.");
            all_inside = false;
        }
    }
    return all_inside;
}

bool project_validator_check_obstacle_overlap(
    const solar_project_state_t *state,
    project_validator_result_t *out_result) {
    if (!state || !out_result) return false;
    if (state->roof.obstacle_count == 0) return true;

    bool no_overlap = true;
    for (size_t i = 0; i < state->graph.component_count; i++) {
        if (state->graph.components[i].kind != SOLAR_COMPONENT_PANEL) continue;
        const solar_panel_instance_t *panel = &state->graph.components[i].data.panel;
        roof_point_t center = {panel->position_x_mm, panel->position_y_mm};
        double width = 1000.0, height = 1700.0;
        const solar_panel_definition_t *def = solar_definition_catalog_find(&state->definitions, &panel->definition_id);
        if (def) {
            width = def->width_mm;
            height = def->height_mm;
        }
        if (roof_panel_overlaps_obstacle(&state->roof, &center, width, height, panel->rotation_degrees)) {
            project_validator_add_issue(out_result, PROJECT_VALIDATOR_ERROR,
                "PANEL_OVER_OBSTACLE", "A panel overlaps a roof obstacle.");
            no_overlap = false;
        }
    }
    return no_overlap;
}

void project_validator_validate(
    const solar_project_state_t *state,
    project_validator_result_t *out_result) {
    if (!state || !out_result) return;
    std::memset(out_result, 0, sizeof(*out_result));
    out_result->is_valid = true;

    project_validator_check_site_temperatures(state, out_result);
    project_validator_check_inverter_dc_voltage(state, out_result);
    project_validator_check_unconnected_panels(state, out_result);
    project_validator_check_string_current(state, out_result);
    project_validator_check_panel_containment(state, out_result);
    project_validator_check_obstacle_overlap(state, out_result);

    if (out_result->error_count > 0) {
        out_result->is_valid = false;
    }
}
