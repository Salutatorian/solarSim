#include "mppt_compatibility.h"

#include <cstddef>
#include <cstdio>
#include <cstring>
#include <cmath>

static void copy_string(char *dest, size_t dest_size, const char *src) {
    if (!src || dest_size == 0) return;
    size_t len = std::strlen(src);
    if (len >= dest_size) len = dest_size - 1;
    std::memcpy(dest, src, len);
    dest[len] = '\0';
}

static void guid_from_u64_pair(solar_guid_t *guid, uint64_t high, uint64_t low) {
    if (!guid) return;
    guid->id_high = high;
    guid->id_low = low;
}

static void zero_guid(solar_guid_t *guid) {
    guid_from_u64_pair(guid, 0, 0);
}

static bool eq_guid(const solar_guid_t *a, const solar_guid_t *b) {
    return a->id_high == b->id_high && a->id_low == b->id_low;
}

static void add_issue(solar_mppt_issue_t *list, size_t *count, size_t max_count,
                      solar_mppt_severity_t severity, const char *code, const char *message, const char *detail) {
    if (!list || !count || !code || !message || !detail) return;
    if (*count >= max_count) return;
    solar_mppt_issue_t *issue = &list[*count];
    issue->severity = severity;
    std::strncpy(issue->code, code, sizeof(issue->code) - 1);
    issue->code[sizeof(issue->code) - 1] = '\0';
    std::strncpy(issue->message, message, sizeof(issue->message) - 1);
    issue->message[sizeof(issue->message) - 1] = '\0';
    std::strncpy(issue->detail, detail, sizeof(issue->detail) - 1);
    issue->detail[sizeof(issue->detail) - 1] = '\0';
    (*count)++;
}

void solar_equipment_instance_init(solar_equipment_instance_t *eq, const solar_guid_t *id, solar_equipment_kind_t kind, const char *name) {
    if (!eq || !id) return;
    std::memset(eq, 0, sizeof(*eq));
    eq->id = *id;
    eq->kind = kind;
    copy_string(eq->name, sizeof(eq->name), name ? name : "Equipment");
    eq->position_x_mm = 0.0;
    eq->position_y_mm = 0.0;
    eq->width_mm = 1000.0;
    eq->height_mm = 1000.0;
    eq->string_input_count = 0;
    eq->has_inverter_specs = false;
    eq->port_count = 0;
    eq->rated_amps = 0;
}

bool solar_equipment_add_port(solar_equipment_instance_t *eq, const solar_equipment_port_t *port) {
    if (!eq || !port) return false;
    if (eq->port_count >= SOLAR_EQUIPMENT_MAX_PORTS) return false;
    eq->ports[eq->port_count] = *port;
    eq->port_count++;
    return true;
}

solar_equipment_port_t *solar_equipment_find_port_by_label(solar_equipment_instance_t *eq, const char *label) {
    if (!eq || !label) return NULL;
    for (size_t i = 0; i < eq->port_count; i++) {
        if (std::strcmp(eq->ports[i].label, label) == 0) {
            return &eq->ports[i];
        }
    }
    return NULL;
}

solar_equipment_port_t *solar_equipment_find_port_by_type_and_label(solar_equipment_instance_t *eq, int port_type, const char *label) {
    if (!eq || !label) return NULL;
    for (size_t i = 0; i < eq->port_count; i++) {
        if (eq->ports[i].port_type == port_type && std::strcmp(eq->ports[i].label, label) == 0) {
            return &eq->ports[i];
        }
    }
    return NULL;
}

bool solar_equipment_is_inverter(const solar_equipment_instance_t *eq) {
    return eq && eq->kind == SOLAR_EQUIPMENT_STRING_INVERTER;
}

bool solar_equipment_is_battery_disconnect(const solar_equipment_instance_t *eq) {
    return eq && eq->kind == SOLAR_EQUIPMENT_BATTERY_DISCONNECT;
}

bool solar_equipment_is_battery(const solar_equipment_instance_t *eq) {
    return eq && eq->kind == SOLAR_EQUIPMENT_BATTERY;
}

void solar_equipment_create_string_inverter(solar_equipment_instance_t *eq, const solar_guid_t *id, const solar_inverter_electrical_specs_t *specs, const char *name) {
    if (!eq || !id) return;
    solar_equipment_instance_init(eq, id, SOLAR_EQUIPMENT_STRING_INVERTER, name);
    if (specs) {
        eq->inverter_specs = *specs;
        eq->has_inverter_specs = true;
    }
    static uint64_t next_port_low = 0x2000;
    for (int i = 1; i <= eq->inverter_specs.mppt_count; i++) {
        solar_equipment_port_t plus;
        std::memset(&plus, 0, sizeof(plus));
        plus.base.owner_id = *id;
        plus.base.type = SOLAR_PORT_MPPT_INPUT_POSITIVE;
        plus.base.polarity = SOLAR_POLARITY_POSITIVE;
        plus.base.interface_type = SOLAR_CONNECTOR_UNSPECIFIED;
        plus.base.is_occupied = false;
        zero_guid(&plus.base.id);
        plus.base.id.id_low = next_port_low++;
        copy_string(plus.base.connector_family, SOLAR_CONNECTOR_FAMILY_LEN, "MC4-compatible");
        std::snprintf(plus.label, SOLAR_EQUIPMENT_PORT_LABEL_LEN, "MPPT%d+", i);
        plus.port_type = 18; /* MpptInputPositive */
        solar_equipment_add_port(eq, &plus);

        solar_equipment_port_t minus;
        std::memset(&minus, 0, sizeof(minus));
        minus.base.owner_id = *id;
        minus.base.type = SOLAR_PORT_MPPT_INPUT_NEGATIVE;
        minus.base.polarity = SOLAR_POLARITY_NEGATIVE;
        minus.base.interface_type = SOLAR_CONNECTOR_UNSPECIFIED;
        minus.base.is_occupied = false;
        zero_guid(&minus.base.id);
        minus.base.id.id_low = next_port_low++;
        copy_string(minus.base.connector_family, SOLAR_CONNECTOR_FAMILY_LEN, "MC4-compatible");
        std::snprintf(minus.label, SOLAR_EQUIPMENT_PORT_LABEL_LEN, "MPPT%d-", i);
        minus.port_type = 19; /* MpptInputNegative */
        solar_equipment_add_port(eq, &minus);
    }
}

static const solar_equipment_port_t *find_mppt_port(const solar_equipment_instance_t *inverter, int channel, bool positive) {
    if (!inverter) return NULL;
    char label[32];
    std::snprintf(label, sizeof(label), "MPPT%d%c", channel, positive ? '+' : '-');
    for (size_t i = 0; i < inverter->port_count; i++) {
        if (std::strcmp(inverter->ports[i].label, label) == 0) {
            return &inverter->ports[i];
        }
    }
    return NULL;
}

static const solar_panel_definition_t *find_definition_for_panel(
    const solar_electrical_graph_t *graph,
    const solar_definition_catalog_t *catalog,
    const solar_guid_t *panel_id) {
    const solar_component_t *comp = solar_electrical_graph_find_component(graph, panel_id);
    if (!comp || comp->kind != SOLAR_COMPONENT_PANEL) return NULL;
    return solar_definition_catalog_find(catalog, &comp->data.panel.definition_id);
}

static void collect_reachable_panels(
    const solar_electrical_graph_t *graph,
    const solar_guid_t *start_port_id,
    solar_guid_t *panel_ids,
    size_t *count,
    size_t max_count) {
    if (!graph || !start_port_id || !panel_ids || !count) return;
    solar_port_t *start_port = solar_electrical_graph_find_port(
        const_cast<solar_electrical_graph_t*>(graph), start_port_id);
    if (!start_port || !start_port->is_occupied) return;

    solar_guid_t visited[SOLAR_MAX_PORTS];
    size_t visited_count = 0;
    solar_guid_t queue[SOLAR_MAX_PORTS];
    size_t queue_head = 0;
    size_t queue_tail = 0;

    visited[visited_count++] = *start_port_id;
    queue[queue_tail++] = *start_port_id;

    while (queue_head < queue_tail && queue_tail < SOLAR_MAX_PORTS) {
        solar_guid_t port_id = queue[queue_head++];
        solar_port_t *port = solar_electrical_graph_find_port(
            const_cast<solar_electrical_graph_t*>(graph), &port_id);
        if (!port) continue;

        const solar_component_t *owner = solar_electrical_graph_find_component(graph, &port->owner_id);
        if (owner && owner->kind == SOLAR_COMPONENT_PANEL) {
            bool already = false;
            for (size_t i = 0; i < *count; i++) {
                if (eq_guid(&panel_ids[i], &owner->id)) {
                    already = true;
                    break;
                }
            }
            if (!already && *count < max_count) {
                panel_ids[(*count)++] = owner->id;
            }
        }

        if (solar_panel_guid_is_zero(&port->connection_id)) continue;
        const solar_connection_t *conn = solar_electrical_graph_find_connection(graph, &port->connection_id);
        if (!conn) continue;
        solar_guid_t other_port_id = eq_guid(&conn->start_port_id, &port_id) ? conn->end_port_id : conn->start_port_id;

        bool already_visited = false;
        for (size_t i = 0; i < visited_count; i++) {
            if (eq_guid(&visited[i], &other_port_id)) {
                already_visited = true;
                break;
            }
        }
        if (already_visited) continue;
        if (visited_count >= SOLAR_MAX_PORTS) continue;
        visited[visited_count++] = other_port_id;
        if (queue_tail < SOLAR_MAX_PORTS) {
            queue[queue_tail++] = other_port_id;
        }
    }
}

static const solar_string_result_t *find_string_result(const solar_project_result_t *project, const solar_guid_t *string_id) {
    if (!project) return NULL;
    for (size_t i = 0; i < project->string_result_count; i++) {
        if (eq_guid(&project->strings[i].string_id, string_id)) {
            return &project->strings[i];
        }
    }
    return NULL;
}

static void evaluate_channel(
    const solar_electrical_graph_t *graph,
    const solar_equipment_instance_t *inverter,
    const solar_inverter_electrical_specs_t *specs,
    int channel_index,
    const solar_equipment_port_t *plus,
    const solar_equipment_port_t *minus,
    const solar_project_result_t *project_calc,
    const solar_definition_catalog_t *catalog,
    const solar_site_design_conditions_t *site,
    solar_mppt_channel_report_t *out_channel) {
    std::memset(out_channel, 0, sizeof(*out_channel));
    out_channel->channel_index = channel_index;
    if (plus) out_channel->positive_port_id = plus->base.id;
    if (minus) out_channel->negative_port_id = minus->base.id;
    if (plus) out_channel->positive_connected = plus->base.is_occupied;
    if (minus) out_channel->negative_connected = minus->base.is_occupied;

    if (plus && minus && plus->base.is_occupied != minus->base.is_occupied) {
        char detail[256];
        std::snprintf(detail, sizeof(detail), "MPPT%d has only one polarity connected.", channel_index);
        add_issue(out_channel->issues, &out_channel->issue_count, SOLAR_MPPT_MAX_ISSUES,
            SOLAR_MPPT_SEVERITY_WARNING, "MPPT_PARTIAL_WIRING", "Incomplete MPPT wiring", detail);
    }

    solar_guid_t reached[SOLAR_MPPT_MAX_REACHED_PANELS];
    size_t reached_count = 0;
    if (plus && plus->base.is_occupied) {
        collect_reachable_panels(graph, &plus->base.id, reached, &reached_count, SOLAR_MPPT_MAX_REACHED_PANELS);
    }
    if (minus && minus->base.is_occupied) {
        collect_reachable_panels(graph, &minus->base.id, reached, &reached_count, SOLAR_MPPT_MAX_REACHED_PANELS);
    }

    for (size_t i = 0; i < reached_count && i < SOLAR_MPPT_MAX_REACHED_PANELS; i++) {
        out_channel->panel_ids[i] = reached[i];
    }
    out_channel->panel_count = reached_count;

    if (reached_count == 0) {
        if ((plus && plus->base.is_occupied) || (minus && minus->base.is_occupied)) {
            char detail[256];
            std::snprintf(detail, sizeof(detail), "MPPT%d is wired but no panels were reached through the DC graph.", channel_index);
            add_issue(out_channel->issues, &out_channel->issue_count, SOLAR_MPPT_MAX_ISSUES,
                SOLAR_MPPT_SEVERITY_WARNING, "MPPT_NO_PANELS", "No panels found", detail);
        }
        return;
    }

    double vocs[2 * SOLAR_MAX_STRING_PANELS];
    double vmps[2 * SOLAR_MAX_STRING_PANELS];
    double imps[2 * SOLAR_MAX_STRING_PANELS];
    double iscs[2 * SOLAR_MAX_STRING_PANELS];
    double pmaxes[2 * SOLAR_MAX_STRING_PANELS];
    double cold_vocs[2 * SOLAR_MAX_STRING_PANELS];
    double hot_vmps[2 * SOLAR_MAX_STRING_PANELS];
    size_t value_count = 0;
    size_t matched_strings = 0;
    bool any_default_coeff = false;

    for (size_t s = 0; s < graph->string_count; s++) {
        const solar_pv_string_t *pv_string = &graph->strings[s];
        bool overlaps = false;
        for (size_t p = 0; p < pv_string->panel_count; p++) {
            for (size_t r = 0; r < reached_count; r++) {
                if (eq_guid(&pv_string->panel_ids[p], &reached[r])) {
                    overlaps = true;
                    break;
                }
            }
            if (overlaps) break;
        }
        if (!overlaps) continue;

        const solar_string_result_t *calc = find_string_result(project_calc, &pv_string->id);
        if (calc) {
            if (value_count < sizeof(vocs) / sizeof(vocs[0])) {
                vocs[value_count] = calc->voc_volts;
                vmps[value_count] = calc->vmp_volts;
                imps[value_count] = calc->imp_amps;
                iscs[value_count] = calc->isc_amps;
                pmaxes[value_count] = calc->total_pmax_watts;
                value_count++;
            }
            if (out_channel->string_count < SOLAR_MPPT_MAX_STRING_IDS) {
                out_channel->string_ids[out_channel->string_count++] = pv_string->id;
            }
            matched_strings++;
        }

        for (size_t p = 0; p < pv_string->panel_count; p++) {
            const solar_panel_definition_t *def = find_definition_for_panel(graph, catalog, &pv_string->panel_ids[p]);
            if (def) {
                if (solar_uses_default_voc_coeff(def)) any_default_coeff = true;
                if (value_count > 0) {
                    cold_vocs[value_count - 1] += solar_cold_voc_volts_for_module(def, site);
                    hot_vmps[value_count - 1] += solar_hot_vmp_volts_for_module(def, site);
                }
            }
        }
    }

    for (size_t r = 0; r < reached_count; r++) {
        bool in_string = false;
        for (size_t s = 0; s < graph->string_count; s++) {
            for (size_t p = 0; p < graph->strings[s].panel_count; p++) {
                if (eq_guid(&graph->strings[s].panel_ids[p], &reached[r])) {
                    in_string = true;
                    break;
                }
            }
            if (in_string) break;
        }
        if (in_string) continue;

        const solar_panel_definition_t *def = find_definition_for_panel(graph, catalog, &reached[r]);
        if (def) {
            if (value_count < sizeof(vocs) / sizeof(vocs[0])) {
                vocs[value_count] = def->voc_volts;
                vmps[value_count] = def->vmp_volts;
                imps[value_count] = def->imp_amps;
                iscs[value_count] = def->isc_amps;
                pmaxes[value_count] = def->pmax_watts;
                cold_vocs[value_count] = solar_cold_voc_volts_for_module(def, site);
                hot_vmps[value_count] = solar_hot_vmp_volts_for_module(def, site);
                value_count++;
            }
            if (solar_uses_default_voc_coeff(def)) any_default_coeff = true;
        }
    }

    if (value_count == 0) return;

    double max_voc = vocs[0];
    double max_vmp = vmps[0];
    double sum_imp = 0.0;
    double sum_isc = 0.0;
    double sum_pmax = 0.0;
    double max_cold_voc = cold_vocs[0];
    double max_hot_vmp = hot_vmps[0];
    for (size_t i = 0; i < value_count; i++) {
        if (vocs[i] > max_voc) max_voc = vocs[i];
        if (vmps[i] > max_vmp) max_vmp = vmps[i];
        if (cold_vocs[i] > max_cold_voc) max_cold_voc = cold_vocs[i];
        if (hot_vmps[i] > max_hot_vmp) max_hot_vmp = hot_vmps[i];
        sum_imp += imps[i];
        sum_isc += iscs[i];
        sum_pmax += pmaxes[i];
    }

    out_channel->voc_volts = max_voc;
    out_channel->has_voc = true;
    out_channel->cold_voc_volts = max_cold_voc;
    out_channel->has_cold_voc = true;
    out_channel->vmp_volts = max_vmp;
    out_channel->has_vmp = true;
    out_channel->hot_vmp_volts = max_hot_vmp;
    out_channel->has_hot_vmp = true;
    out_channel->imp_amps = sum_imp;
    out_channel->has_imp = true;
    out_channel->isc_amps = sum_isc;
    out_channel->has_isc = true;
    out_channel->pmax_watts = sum_pmax;
    out_channel->has_pmax = true;
    out_channel->module_count = static_cast<int>(reached_count);

    if (any_default_coeff) {
        char detail[256];
        std::snprintf(detail, sizeof(detail),
            "MPPT%d: at least one module is missing datasheet Voc coeff — using %.2f %%/°C.",
            channel_index, SOLAR_DEFAULT_VOC_TEMP_COEFF_PCT_PER_C);
        add_issue(out_channel->issues, &out_channel->issue_count, SOLAR_MPPT_MAX_ISSUES,
            SOLAR_MPPT_SEVERITY_INFO, "TEMP_COEFF_DEFAULT", "Using default Voc temp coeff", detail);
    }

    if (value_count > 1) {
        double min_voc = vocs[0];
        for (size_t i = 1; i < value_count; i++) {
            if (vocs[i] < min_voc) min_voc = vocs[i];
        }
        if (max_voc - min_voc > 5.0) {
            char detail[256];
            std::snprintf(detail, sizeof(detail),
                "MPPT%d paralleled sources differ by %.1f V Voc.",
                channel_index, max_voc - min_voc);
            add_issue(out_channel->issues, &out_channel->issue_count, SOLAR_MPPT_MAX_ISSUES,
                SOLAR_MPPT_SEVERITY_WARNING, "MPPT_PARALLEL_VOC_MISMATCH", "Parallel Voc mismatch", detail);
        }
    }

    double voc_for_max_check = out_channel->has_cold_voc ? out_channel->cold_voc_volts : out_channel->voc_volts;
    if (voc_for_max_check > specs->max_dc_volts) {
        const char *label = out_channel->has_cold_voc ? "cold Voc" : "Voc";
        char detail[256];
        std::snprintf(detail, sizeof(detail),
            "MPPT%d %s %.1f V > max DC %.1f V.",
            channel_index, label, voc_for_max_check, specs->max_dc_volts);
        add_issue(out_channel->issues, &out_channel->issue_count, SOLAR_MPPT_MAX_ISSUES,
            SOLAR_MPPT_SEVERITY_ERROR, "MPPT_VOC_EXCEEDED", "Voc exceeds inverter max", detail);
    }

    double vmp_for_window = out_channel->has_hot_vmp ? out_channel->hot_vmp_volts : out_channel->vmp_volts;
    if (std::isfinite(vmp_for_window)) {
        const char *label = out_channel->has_hot_vmp ? "hot Vmp" : "Vmp";
        if (vmp_for_window < specs->min_mppt_volts) {
            char detail[256];
            std::snprintf(detail, sizeof(detail),
                "MPPT%d %s %.1f V < min %.1f V.",
                channel_index, label, vmp_for_window, specs->min_mppt_volts);
            add_issue(out_channel->issues, &out_channel->issue_count, SOLAR_MPPT_MAX_ISSUES,
                SOLAR_MPPT_SEVERITY_WARNING, "MPPT_VMP_LOW", "Vmp below MPPT window", detail);
        } else if (vmp_for_window > specs->max_mppt_volts) {
            char detail[256];
            std::snprintf(detail, sizeof(detail),
                "MPPT%d %s %.1f V > max %.1f V.",
                channel_index, label, vmp_for_window, specs->max_mppt_volts);
            add_issue(out_channel->issues, &out_channel->issue_count, SOLAR_MPPT_MAX_ISSUES,
                SOLAR_MPPT_SEVERITY_WARNING, "MPPT_VMP_HIGH", "Vmp above MPPT window", detail);
        }
    }

    if (out_channel->has_imp && out_channel->imp_amps > specs->max_current_per_mppt_amps) {
        char detail[256];
        std::snprintf(detail, sizeof(detail),
            "MPPT%d Imp %.2f A > max %.2f A.",
            channel_index, out_channel->imp_amps, specs->max_current_per_mppt_amps);
        add_issue(out_channel->issues, &out_channel->issue_count, SOLAR_MPPT_MAX_ISSUES,
            SOLAR_MPPT_SEVERITY_ERROR, "MPPT_IMP_EXCEEDED", "Imp exceeds MPPT current", detail);
    }

    if (out_channel->has_pmax && out_channel->pmax_watts > specs->max_dc_power_per_mppt_watts) {
        char detail[256];
        std::snprintf(detail, sizeof(detail),
            "MPPT%d %.1f W > recommended %.1f W.",
            channel_index, out_channel->pmax_watts, specs->max_dc_power_per_mppt_watts);
        add_issue(out_channel->issues, &out_channel->issue_count, SOLAR_MPPT_MAX_ISSUES,
            SOLAR_MPPT_SEVERITY_WARNING, "MPPT_POWER_HIGH", "DC power high for MPPT", detail);
    }
}

void solar_mppt_compatibility_evaluate_inverter(
    const solar_electrical_graph_t *graph,
    const solar_equipment_instance_t *inverter,
    const solar_project_result_t *project_calc,
    const solar_definition_catalog_t *catalog,
    const solar_site_design_conditions_t *site,
    solar_inverter_mppt_report_t *out_report) {
    std::memset(out_report, 0, sizeof(*out_report));
    if (!graph || !inverter || !out_report) return;
    if (!solar_equipment_is_inverter(inverter) || !inverter->has_inverter_specs) {
        add_issue(out_report->issues, &out_report->issue_count, SOLAR_MPPT_MAX_ISSUES,
            SOLAR_MPPT_SEVERITY_ERROR, "NOT_INVERTER", "Component is not an inverter",
            "Cannot evaluate MPPT compatibility for a non-inverter component.");
        return;
    }

    out_report->inverter_id = inverter->id;
    copy_string(out_report->name, sizeof(out_report->name), inverter->name);
    out_report->specs = inverter->inverter_specs;

    const solar_inverter_electrical_specs_t *specs = &inverter->inverter_specs;
    double total_dc_watts = 0.0;
    bool any_feed = false;

    for (int i = 1; i <= specs->mppt_count && out_report->channel_count < SOLAR_MPPT_MAX_CHANNELS; i++) {
        const solar_equipment_port_t *plus = find_mppt_port(inverter, i, true);
        const solar_equipment_port_t *minus = find_mppt_port(inverter, i, false);

        if (!plus || !minus) {
            char detail[256];
            std::snprintf(detail, sizeof(detail), "Inverter is missing MPPT%d+/− ports.", i);
            add_issue(out_report->issues, &out_report->issue_count, SOLAR_MPPT_MAX_ISSUES,
                SOLAR_MPPT_SEVERITY_ERROR, "MPPT_PORT_MISSING", "MPPT ports missing", detail);
            continue;
        }

        solar_mppt_channel_report_t *channel = &out_report->channels[out_report->channel_count];
        evaluate_channel(graph, inverter, specs, i, plus, minus, project_calc, catalog, site, channel);
        out_report->channel_count++;

        if (channel->has_pmax) {
            total_dc_watts += channel->pmax_watts;
            any_feed = true;
        }
    }

    out_report->total_dc_watts = total_dc_watts;

    if (any_feed && total_dc_watts > specs->ac_rated_watts * 1.5) {
        char detail[256];
        std::snprintf(detail, sizeof(detail),
            "Total DC on MPPTs is %.1f W vs %.1f W AC rating.",
            total_dc_watts, specs->ac_rated_watts);
        add_issue(out_report->issues, &out_report->issue_count, SOLAR_MPPT_MAX_ISSUES,
            SOLAR_MPPT_SEVERITY_WARNING, "INVERTER_DC_AC_RATIO_HIGH", "High DC/AC ratio", detail);
    }
}

void solar_mppt_compatibility_evaluate_all(
    const solar_electrical_graph_t *graph,
    const solar_equipment_instance_t *inverters,
    size_t inverter_count,
    const solar_project_result_t *project_calc,
    const solar_definition_catalog_t *catalog,
    const solar_site_design_conditions_t *site,
    solar_inverter_mppt_report_t *out_reports,
    size_t max_reports,
    size_t *out_report_count) {
    if (!out_report_count) return;
    *out_report_count = 0;
    if (!graph || !inverters || !out_reports || max_reports == 0) return;

    for (size_t i = 0; i < inverter_count && *out_report_count < max_reports; i++) {
        const solar_equipment_instance_t *inverter = &inverters[i];
        if (!solar_equipment_is_inverter(inverter) || !inverter->has_inverter_specs) continue;
        solar_mppt_compatibility_evaluate_inverter(
            graph, inverter, project_calc, catalog, site, &out_reports[*out_report_count]);
        (*out_report_count)++;
    }
}
