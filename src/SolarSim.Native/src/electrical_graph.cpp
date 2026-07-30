#include "electrical_graph.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cmath>

static uint64_t g_next_guid_low = 1;

static void make_guid(solar_guid_t *guid) {
    guid->id_high = 0;
    guid->id_low = g_next_guid_low++;
}

static void copy_validation_error(solar_validation_error_t *dest, const char *code, const char *message, const char *detail) {
    if (!dest) return;
    std::strncpy(dest->code, code, sizeof(dest->code) - 1);
    dest->code[sizeof(dest->code) - 1] = '\0';
    std::strncpy(dest->message, message, sizeof(dest->message) - 1);
    dest->message[sizeof(dest->message) - 1] = '\0';
    std::strncpy(dest->detail, detail, sizeof(dest->detail) - 1);
    dest->detail[sizeof(dest->detail) - 1] = '\0';
}

static void add_error(solar_electrical_graph_t *graph, const char *code, const char *message, const char *detail) {
    if (graph->error_count >= SOLAR_MAX_VALIDATION_ERRORS) return;
    copy_validation_error(&graph->errors[graph->error_count], code, message, detail);
    graph->error_count++;
}

void solar_electrical_graph_init(solar_electrical_graph_t *graph) {
    if (!graph) return;
    std::memset(graph, 0, sizeof(*graph));
}

static int find_component_index(const solar_electrical_graph_t *graph, const solar_guid_t *id) {
    for (size_t i = 0; i < graph->component_count; i++) {
        if (solar_panel_guid_equals(&graph->components[i].id, id)) {
            return (int)i;
        }
    }
    return -1;
}

static int find_connection_index(const solar_electrical_graph_t *graph, const solar_guid_t *id) {
    for (size_t i = 0; i < graph->connection_count; i++) {
        if (solar_panel_guid_equals(&graph->connections[i].id, id)) {
            return (int)i;
        }
    }
    return -1;
}

bool solar_electrical_graph_add_panel(solar_electrical_graph_t *graph, const solar_panel_instance_t *panel) {
    if (!graph || !panel) return false;
    if (graph->component_count >= SOLAR_MAX_COMPONENTS) {
        add_error(graph, "GRAPH_FULL", "Component limit reached", "Cannot add more components to the graph.");
        return false;
    }
    if (find_component_index(graph, &panel->id) >= 0) {
        add_error(graph, "DUPLICATE_COMPONENT", "Duplicate component id", "A component with this id already exists.");
        return false;
    }
    solar_component_t *comp = &graph->components[graph->component_count];
    comp->id = panel->id;
    comp->kind = SOLAR_COMPONENT_PANEL;
    comp->data.panel = *panel;
    graph->component_count++;
    solar_electrical_graph_rebuild_strings(graph);
    return true;
}

bool solar_electrical_graph_remove_panel(solar_electrical_graph_t *graph, const solar_guid_t *panel_id) {
    if (!graph || !panel_id) return false;
    int idx = find_component_index(graph, panel_id);
    if (idx < 0) return false;
    solar_component_t *comp = &graph->components[idx];
    for (size_t i = 0; i < comp->data.panel.port_count; i++) {
        solar_guid_t pid = comp->data.panel.ports[i].id;
        for (size_t c = 0; c < graph->connection_count; ) {
            if (solar_panel_guid_equals(&graph->connections[c].start_port_id, &pid) ||
                solar_panel_guid_equals(&graph->connections[c].end_port_id, &pid)) {
                solar_guid_t cid = graph->connections[c].id;
                solar_electrical_graph_disconnect(graph, &cid);
                continue;
            }
            c++;
        }
    }
    if (idx + 1 < (int)graph->component_count) {
        std::memmove(&graph->components[idx], &graph->components[idx + 1],
                     (graph->component_count - idx - 1) * sizeof(solar_component_t));
    }
    graph->component_count--;
    solar_electrical_graph_rebuild_strings(graph);
    return true;
}

const solar_component_t *solar_electrical_graph_find_component(
    const solar_electrical_graph_t *graph,
    const solar_guid_t *component_id) {
    if (!graph || !component_id) return NULL;
    int idx = find_component_index(graph, component_id);
    return idx >= 0 ? &graph->components[idx] : NULL;
}

solar_port_t *solar_electrical_graph_find_port(
    solar_electrical_graph_t *graph,
    const solar_guid_t *port_id) {
    if (!graph || !port_id) return NULL;
    for (size_t i = 0; i < graph->component_count; i++) {
        solar_component_t *comp = &graph->components[i];
        if (comp->kind == SOLAR_COMPONENT_PANEL) {
            solar_port_t *p = solar_panel_find_port(&comp->data.panel, port_id);
            if (p) return p;
        }
    }
    return NULL;
}

const solar_connection_t *solar_electrical_graph_find_connection(
    const solar_electrical_graph_t *graph,
    const solar_guid_t *connection_id) {
    if (!graph || !connection_id) return NULL;
    int idx = find_connection_index(graph, connection_id);
    return idx >= 0 ? &graph->connections[idx] : NULL;
}

bool solar_electrical_graph_validate_dc_connection(
    const solar_port_t *start,
    const solar_port_t *end,
    const solar_component_t *start_owner,
    const solar_component_t *end_owner,
    solar_validation_error_t *out_error) {
    if (!start || !end || !start_owner || !end_owner || !out_error) return false;

    if (solar_panel_guid_equals(&start->id, &end->id)) {
        copy_validation_error(out_error, "SELF_CONNECT", "Cannot connect port to itself",
            "Start and end port are the same.");
        return false;
    }
    if (solar_panel_guid_equals(&start_owner->id, &end_owner->id)) {
        copy_validation_error(out_error, "SAME_OWNER", "Cannot connect ports on the same component",
            "Both ports belong to the same component.");
        return false;
    }
    if (start->is_occupied || end->is_occupied) {
        copy_validation_error(out_error, "PORT_OCCUPIED", "Port already occupied",
            "One or both ports are already connected.");
        return false;
    }
    if (start->polarity == end->polarity) {
        copy_validation_error(out_error, "POLARITY_MISMATCH", "Polarity mismatch",
            "Cannot connect two ports with the same polarity.");
        return false;
    }
    if (start->type != SOLAR_PORT_PV_POSITIVE && start->type != SOLAR_PORT_PV_NEGATIVE) {
        copy_validation_error(out_error, "UNSUPPORTED_PORT", "Unsupported port type",
            "Start port is not a PV terminal.");
        return false;
    }
    if (end->type != SOLAR_PORT_PV_POSITIVE && end->type != SOLAR_PORT_PV_NEGATIVE) {
        copy_validation_error(out_error, "UNSUPPORTED_PORT", "Unsupported port type",
            "End port is not a PV terminal.");
        return false;
    }
    if (std::strcmp(start->connector_family, end->connector_family) != 0) {
        copy_validation_error(out_error, "CONNECTOR_MISMATCH", "Connector family mismatch",
            "Ports use incompatible connector families.");
        return false;
    }
    return true;
}

bool solar_electrical_graph_try_connect(
    solar_electrical_graph_t *graph,
    const solar_guid_t *start_port_id,
    const solar_guid_t *end_port_id,
    double length_mm,
    int gauge_awg) {
    if (!graph || !start_port_id || !end_port_id) return false;
    if (graph->connection_count >= SOLAR_MAX_CONNECTIONS) {
        add_error(graph, "GRAPH_FULL", "Connection limit reached", "Cannot add more connections.");
        return false;
    }

    solar_port_t *start = solar_electrical_graph_find_port(graph, start_port_id);
    solar_port_t *end = solar_electrical_graph_find_port(graph, end_port_id);
    if (!start || !end) {
        add_error(graph, "PORT_NOT_FOUND", "Port not found", "One or both ports do not exist.");
        return false;
    }

    const solar_component_t *start_owner = solar_electrical_graph_find_component(graph, &start->owner_id);
    const solar_component_t *end_owner = solar_electrical_graph_find_component(graph, &end->owner_id);
    if (!start_owner || !end_owner) {
        add_error(graph, "COMPONENT_NOT_FOUND", "Component not found", "Port owner is missing.");
        return false;
    }

    for (size_t i = 0; i < graph->connection_count; i++) {
        if ((solar_panel_guid_equals(&graph->connections[i].start_port_id, start_port_id) &&
             solar_panel_guid_equals(&graph->connections[i].end_port_id, end_port_id)) ||
            (solar_panel_guid_equals(&graph->connections[i].start_port_id, end_port_id) &&
             solar_panel_guid_equals(&graph->connections[i].end_port_id, start_port_id))) {
            add_error(graph, "DUPLICATE_CONNECTION", "Duplicate connection", "These ports are already connected.");
            return false;
        }
    }

    solar_validation_error_t err;
    if (!solar_electrical_graph_validate_dc_connection(start, end, start_owner, end_owner, &err)) {
        add_error(graph, err.code, err.message, err.detail);
        return false;
    }

    solar_connection_t *conn = &graph->connections[graph->connection_count];
    make_guid(&conn->id);
    conn->start_port_id = *start_port_id;
    conn->end_port_id = *end_port_id;
    conn->length_mm = length_mm >= 0.0 ? length_mm : 0.0;
    conn->gauge_awg = gauge_awg;
    std::strncpy(conn->wire_type, "PV wire", sizeof(conn->wire_type) - 1);
    conn->wire_type[sizeof(conn->wire_type) - 1] = '\0';
    graph->connection_count++;

    start->is_occupied = true;
    start->connection_id = conn->id;
    end->is_occupied = true;
    end->connection_id = conn->id;

    solar_electrical_graph_rebuild_strings(graph);
    return true;
}

bool solar_electrical_graph_disconnect(solar_electrical_graph_t *graph, const solar_guid_t *connection_id) {
    if (!graph || !connection_id) return false;
    int idx = find_connection_index(graph, connection_id);
    if (idx < 0) return false;
    solar_connection_t *conn = &graph->connections[idx];
    solar_port_t *start = solar_electrical_graph_find_port(graph, &conn->start_port_id);
    solar_port_t *end = solar_electrical_graph_find_port(graph, &conn->end_port_id);
    if (start) {
        start->is_occupied = false;
        solar_panel_guid_zero(&start->connection_id);
    }
    if (end) {
        end->is_occupied = false;
        solar_panel_guid_zero(&end->connection_id);
    }
    if (idx + 1 < (int)graph->connection_count) {
        std::memmove(&graph->connections[idx], &graph->connections[idx + 1],
                     (graph->connection_count - idx - 1) * sizeof(solar_connection_t));
    }
    graph->connection_count--;
    solar_electrical_graph_rebuild_strings(graph);
    return true;
}

static void visit_string_from(
    solar_electrical_graph_t *graph,
    const solar_component_t *first_panel,
    const solar_port_t *start_port,
    solar_pv_string_t *out_string) {
    if (!first_panel || !start_port || !out_string) return;
    const solar_component_t *current_panel = first_panel;
    const solar_port_t *current_port = start_port;
    size_t depth = 0;

    while (current_panel && current_port && depth < SOLAR_MAX_STRING_PANELS) {
        out_string->panel_ids[depth] = current_panel->id;
        out_string->panel_count = depth + 1;
        depth++;

        solar_guid_t next_port_id;
        solar_panel_guid_zero(&next_port_id);
        bool found_next = false;
        for (size_t i = 0; i < graph->connection_count; i++) {
            solar_connection_t *conn = &graph->connections[i];
            if (solar_panel_guid_equals(&conn->start_port_id, &current_port->id)) {
                next_port_id = conn->end_port_id;
                found_next = true;
                break;
            }
            if (solar_panel_guid_equals(&conn->end_port_id, &current_port->id)) {
                next_port_id = conn->start_port_id;
                found_next = true;
                break;
            }
        }
        if (!found_next || solar_panel_guid_is_zero(&next_port_id)) break;

        solar_port_t *next_port = solar_electrical_graph_find_port(graph, &next_port_id);
        if (!next_port) break;

        /* Expect the next port to be the opposite polarity on the next panel. */
        if (next_port->polarity == current_port->polarity) break;
        if (!solar_panel_guid_equals(&next_port->owner_id, &current_panel->id)) {
            const solar_component_t *next_owner = solar_electrical_graph_find_component(graph, &next_port->owner_id);
            if (!next_owner || next_owner->kind != SOLAR_COMPONENT_PANEL) break;
            current_panel = next_owner;
        }
        current_port = next_port;
    }
}

void solar_electrical_graph_rebuild_strings(solar_electrical_graph_t *graph) {
    if (!graph) return;
    graph->string_count = 0;
    for (size_t i = 0; i < graph->component_count && graph->string_count < SOLAR_MAX_STRINGS; i++) {
        solar_component_t *comp = &graph->components[i];
        if (comp->kind != SOLAR_COMPONENT_PANEL) continue;
        for (size_t p = 0; p < comp->data.panel.port_count; p++) {
            solar_port_t *port = &comp->data.panel.ports[p];
            if (port->type != SOLAR_PORT_PV_POSITIVE || !port->is_occupied) continue;
            bool already_in_string = false;
            for (size_t s = 0; s < graph->string_count; s++) {
                for (size_t k = 0; k < graph->strings[s].panel_count; k++) {
                    if (solar_panel_guid_equals(&graph->strings[s].panel_ids[k], &comp->id)) {
                        already_in_string = true;
                        break;
                    }
                }
                if (already_in_string) break;
            }
            if (already_in_string) continue;

            solar_pv_string_t *str = &graph->strings[graph->string_count];
            make_guid(&str->id);
            std::snprintf(str->display_name, sizeof(str->display_name), "String %zu", graph->string_count + 1);
            str->panel_count = 0;
            visit_string_from(graph, comp, port, str);
            if (str->panel_count > 0) {
                graph->string_count++;
            }
        }
    }
}

void solar_electrical_graph_clear(solar_electrical_graph_t *graph) {
    if (!graph) return;
    std::memset(graph, 0, sizeof(*graph));
}
