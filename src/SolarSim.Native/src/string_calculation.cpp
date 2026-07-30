#include "string_calculation.h"

#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <cmath>

static void add_issue(
    solar_issue_t *list,
    size_t *count,
    size_t max_count,
    solar_issue_severity_t severity,
    const char *code,
    const char *message,
    const char *detail,
    ...) {
    if (*count >= max_count) return;
    solar_issue_t *issue = &list[*count];
    issue->severity = severity;
    std::strncpy(issue->code, code, sizeof(issue->code) - 1);
    issue->code[sizeof(issue->code) - 1] = '\0';
    std::strncpy(issue->message, message, sizeof(issue->message) - 1);
    issue->message[sizeof(issue->message) - 1] = '\0';

    if (detail) {
        char buffer[sizeof(issue->detail)];
        std::va_list args;
        va_start(args, detail);
        std::vsnprintf(buffer, sizeof(buffer), detail, args);
        va_end(args);
        std::strncpy(issue->detail, buffer, sizeof(issue->detail) - 1);
        issue->detail[sizeof(issue->detail) - 1] = '\0';
    } else {
        issue->detail[0] = '\0';
    }
    issue->related_count = 0;
    (*count)++;
}

void solar_definition_catalog_init(solar_definition_catalog_t *catalog) {
    if (!catalog) return;
    std::memset(catalog, 0, sizeof(*catalog));
}

bool solar_definition_catalog_add(solar_definition_catalog_t *catalog, const solar_panel_definition_t *def) {
    if (!catalog || !def) return false;
    if (catalog->count >= SOLAR_MAX_DEFINITIONS) return false;
    if (!solar_panel_definition_is_valid(def)) return false;
    catalog->definitions[catalog->count] = *def;
    catalog->count++;
    return true;
}

const solar_panel_definition_t *solar_definition_catalog_find(
    const solar_definition_catalog_t *catalog,
    const solar_guid_t *id) {
    if (!catalog || !id) return NULL;
    for (size_t i = 0; i < catalog->count; i++) {
        if (solar_panel_guid_equals(&catalog->definitions[i].id, id)) {
            return &catalog->definitions[i];
        }
    }
    return NULL;
}

double solar_cold_voc_volts(double voc_at_stc, double temp_coeff_voc_pct_per_c, double delta_c) {
    if (!std::isfinite(voc_at_stc) || !std::isfinite(delta_c)) return 0.0;
    return voc_at_stc * (1.0 + (temp_coeff_voc_pct_per_c / 100.0) * delta_c);
}

double solar_hot_vmp_volts(double vmp_at_stc, double temp_coeff_vmp_pct_per_c, double delta_c) {
    if (!std::isfinite(vmp_at_stc) || !std::isfinite(delta_c)) return 0.0;
    return vmp_at_stc * (1.0 + (temp_coeff_vmp_pct_per_c / 100.0) * delta_c);
}

static bool is_mixed_module_string(
    const solar_panel_definition_t *defs,
    size_t count) {
    if (count <= 1) return false;
    const double first_imp = defs[0].imp_amps;
    for (size_t i = 1; i < count; i++) {
        if (std::fabs(defs[i].imp_amps - first_imp) > 0.05) {
            return true;
        }
    }
    return false;
}

void solar_calculate_string(
    const solar_pv_string_t *pv_string,
    const solar_electrical_graph_t *graph,
    const solar_definition_catalog_t *catalog,
    solar_string_result_t *out_result) {
    if (!pv_string || !graph || !catalog || !out_result) return;
    std::memset(out_result, 0, sizeof(*out_result));
    out_result->string_id = pv_string->id;
    std::strncpy(out_result->display_name, pv_string->display_name, sizeof(out_result->display_name) - 1);
    out_result->display_name[sizeof(out_result->display_name) - 1] = '\0';

    const solar_panel_definition_t *defs[SOLAR_MAX_STRING_PANELS];
    size_t def_count = 0;

    for (size_t i = 0; i < pv_string->panel_count; i++) {
        const solar_guid_t *panel_id = &pv_string->panel_ids[i];
        const solar_component_t *comp = solar_electrical_graph_find_component(graph, panel_id);
        if (!comp || comp->kind != SOLAR_COMPONENT_PANEL) {
            add_issue(out_result->errors, &out_result->error_count, SOLAR_MAX_ERRORS,
                SOLAR_SEVERITY_ERROR, "MISSING_PANEL", "Missing panel",
                "Panel referenced by string was not found.");
            continue;
        }
        const solar_panel_definition_t *def = solar_definition_catalog_find(catalog, &comp->data.panel.definition_id);
        if (!def) {
            add_issue(out_result->errors, &out_result->error_count, SOLAR_MAX_ERRORS,
                SOLAR_SEVERITY_ERROR, "MISSING_DEFINITION", "Missing definition",
                "Definition was not found for panel in string.");
            continue;
        }
        if (def_count < SOLAR_MAX_STRING_PANELS) {
            defs[def_count++] = def;
        }
    }

    out_result->panel_count = def_count;
    if (def_count == 0) {
        return;
    }

    if (is_mixed_module_string(defs, def_count)) {
        add_issue(out_result->warnings, &out_result->warning_count, SOLAR_MAX_WARNINGS,
            SOLAR_SEVERITY_WARNING, "MIXED_MODULE_STRING", "Mixed module string",
            "Modules in this series string have different operating-current characteristics. Results are simplified.");
        out_result->is_mixed_module_string = true;
        out_result->is_simplified = true;
    }

    double total_pmax = 0.0;
    double total_vmp = 0.0;
    double total_voc = 0.0;
    double min_imp = defs[0]->imp_amps;
    double min_isc = defs[0]->isc_amps;

    for (size_t i = 0; i < def_count; i++) {
        total_pmax += defs[i]->pmax_watts;
        total_vmp += defs[i]->vmp_volts;
        total_voc += defs[i]->voc_volts;
        if (defs[i]->imp_amps < min_imp) min_imp = defs[i]->imp_amps;
        if (defs[i]->isc_amps < min_isc) min_isc = defs[i]->isc_amps;
    }

    out_result->total_pmax_watts = total_pmax;
    out_result->vmp_volts = total_vmp;
    out_result->voc_volts = total_voc;
    out_result->imp_amps = min_imp;
    out_result->isc_amps = min_isc;

    /* Conservative warning for very high string voltage. */
    if (total_voc > 1000.0) {
        add_issue(out_result->warnings, &out_result->warning_count, SOLAR_MAX_WARNINGS,
            SOLAR_SEVERITY_WARNING, "HIGH_STRING_VOLTAGE", "High string voltage",
            "String open-circuit voltage exceeds 1000 V. Verify inverter input limits.");
    }
    if (total_voc <= 0.0 || !std::isfinite(total_voc)) {
        add_issue(out_result->errors, &out_result->error_count, SOLAR_MAX_ERRORS,
            SOLAR_SEVERITY_ERROR, "INVALID_VOLTAGE", "Invalid string voltage",
            "Calculated string voltage is non-finite or non-positive.");
    }
}

void solar_calculate_project(
    const solar_electrical_graph_t *graph,
    const solar_definition_catalog_t *catalog,
    solar_project_result_t *out_result) {
    if (!graph || !catalog || !out_result) return;
    std::memset(out_result, 0, sizeof(*out_result));

    out_result->total_panels = graph->component_count;

    double total_pmax = 0.0;
    for (size_t i = 0; i < graph->component_count; i++) {
        if (graph->components[i].kind != SOLAR_COMPONENT_PANEL) continue;
        const solar_panel_instance_t *panel = &graph->components[i].data.panel;
        const solar_panel_definition_t *def = solar_definition_catalog_find(catalog, &panel->definition_id);
        if (def) {
            total_pmax += def->pmax_watts;
        }
    }
    out_result->total_pmax_watts = total_pmax;

    for (size_t i = 0; i < graph->string_count && out_result->string_result_count < SOLAR_MAX_STRING_RESULTS; i++) {
        solar_string_result_t str_result;
        solar_calculate_string(&graph->strings[i], graph, catalog, &str_result);
        out_result->strings[out_result->string_result_count] = str_result;
        out_result->string_result_count++;

        for (size_t j = 0; j < str_result.panel_count; j++) {
            bool already = false;
            for (size_t k = 0; k < out_result->connected_panels; k++) {
                /* connected_panels count is approximate because we don't store ids here. */
            }
            (void)already;
            out_result->connected_panels++;
        }
        for (size_t w = 0; w < str_result.warning_count && out_result->warning_count < SOLAR_MAX_WARNINGS; w++) {
            out_result->warnings[out_result->warning_count++] = str_result.warnings[w];
        }
        for (size_t e = 0; e < str_result.error_count && out_result->error_count < SOLAR_MAX_ERRORS; e++) {
            out_result->errors[out_result->error_count++] = str_result.errors[e];
        }
    }

    if (out_result->connected_panels > out_result->total_panels) {
        out_result->connected_panels = out_result->total_panels;
    }
    out_result->unconnected_panels = out_result->total_panels - out_result->connected_panels;
    out_result->string_count = graph->string_count;

    for (size_t i = 0; i < graph->component_count; i++) {
        if (graph->components[i].kind != SOLAR_COMPONENT_PANEL) continue;
        const solar_panel_instance_t *panel = &graph->components[i].data.panel;
        bool has_connection = false;
        for (size_t p = 0; p < panel->port_count; p++) {
            if (panel->ports[p].is_occupied) {
                has_connection = true;
                break;
            }
        }
        if (!has_connection && out_result->warning_count < SOLAR_MAX_WARNINGS) {
            add_issue(out_result->warnings, &out_result->warning_count, SOLAR_MAX_WARNINGS,
                SOLAR_SEVERITY_INFO, "UNCONNECTED_PANEL", "Unconnected panel",
                "This panel is not part of any electrical string yet.");
        }
    }
}
