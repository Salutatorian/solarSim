#ifndef ELECTRICAL_GRAPH_H
#define ELECTRICAL_GRAPH_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "solar_panel.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Electrical graph: panels, equipment, ports, connections, and discovered series strings.
 * Mirrors SolarSim.Domain.Electrical.ElectricalGraph.
 */

#define SOLAR_MAX_COMPONENTS 256
#define SOLAR_MAX_CONNECTIONS 512
#define SOLAR_MAX_PORTS 1024
#define SOLAR_MAX_STRINGS 128
#define SOLAR_MAX_STRING_PANELS 64
#define SOLAR_MAX_VALIDATION_ERRORS 8

typedef enum {
    SOLAR_COMPONENT_PANEL = 0,
    SOLAR_COMPONENT_EQUIPMENT
} solar_component_kind_t;

typedef struct {
    solar_guid_t id;
    solar_component_kind_t kind;
    union {
        solar_panel_instance_t panel;
    } data;
} solar_component_t;

typedef struct {
    solar_guid_t id;
    solar_guid_t start_port_id;
    solar_guid_t end_port_id;
    double length_mm;
    int gauge_awg;
    char wire_type[32];
} solar_connection_t;

typedef struct {
    solar_guid_t id;
    char display_name[32];
    solar_guid_t panel_ids[SOLAR_MAX_STRING_PANELS];
    size_t panel_count;
} solar_pv_string_t;

typedef struct {
    char code[32];
    char message[128];
    char detail[256];
} solar_validation_error_t;

typedef struct {
    solar_component_t components[SOLAR_MAX_COMPONENTS];
    size_t component_count;
    solar_connection_t connections[SOLAR_MAX_CONNECTIONS];
    size_t connection_count;
    solar_pv_string_t strings[SOLAR_MAX_STRINGS];
    size_t string_count;
    solar_validation_error_t errors[SOLAR_MAX_VALIDATION_ERRORS];
    size_t error_count;
} solar_electrical_graph_t;

void solar_electrical_graph_init(solar_electrical_graph_t *graph);
bool solar_electrical_graph_add_panel(solar_electrical_graph_t *graph, const solar_panel_instance_t *panel);
bool solar_electrical_graph_remove_panel(solar_electrical_graph_t *graph, const solar_guid_t *panel_id);
bool solar_electrical_graph_try_connect(
    solar_electrical_graph_t *graph,
    const solar_guid_t *start_port_id,
    const solar_guid_t *end_port_id,
    double length_mm,
    int gauge_awg);
bool solar_electrical_graph_disconnect(solar_electrical_graph_t *graph, const solar_guid_t *connection_id);

const solar_component_t *solar_electrical_graph_find_component(
    const solar_electrical_graph_t *graph,
    const solar_guid_t *component_id);
solar_port_t *solar_electrical_graph_find_port(
    solar_electrical_graph_t *graph,
    const solar_guid_t *port_id);
const solar_connection_t *solar_electrical_graph_find_connection(
    const solar_electrical_graph_t *graph,
    const solar_guid_t *connection_id);

void solar_electrical_graph_rebuild_strings(solar_electrical_graph_t *graph);
void solar_electrical_graph_clear(solar_electrical_graph_t *graph);

bool solar_electrical_graph_validate_dc_connection(
    const solar_port_t *start,
    const solar_port_t *end,
    const solar_component_t *start_owner,
    const solar_component_t *end_owner,
    solar_validation_error_t *out_error);

#ifdef __cplusplus
}
#endif

#endif
