#ifndef SOLAR_PROJECT_STATE_H
#define SOLAR_PROJECT_STATE_H

#include <stddef.h>
#include <vector>

#include "solar_panel.h"
#include "electrical_graph.h"
#include "roof_geometry.h"
#include "string_calculation.h"
#include "production_estimate.h"

#define SOLAR_PROJECT_STATE_NAME_LEN 128
#define SOLAR_PROJECT_STATE_FILE_PATH_LEN 512
#define SOLAR_EQUIPMENT_NAME_LEN 64
#define SOLAR_EQUIPMENT_CATALOG_SERIES_LEN 64
#define SOLAR_EQUIPMENT_MAX_PORTS 8

typedef enum {
    SOLAR_EQUIPMENT_KIND_COMBINER = 0,
    SOLAR_EQUIPMENT_KIND_PV_DISCONNECT,
    SOLAR_EQUIPMENT_KIND_STRING_INVERTER,
    SOLAR_EQUIPMENT_KIND_AC_DISCONNECT,
    SOLAR_EQUIPMENT_KIND_AC_LOAD_CENTER,
    SOLAR_EQUIPMENT_KIND_BATTERY,
    SOLAR_EQUIPMENT_KIND_BATTERY_DISCONNECT,
    SOLAR_EQUIPMENT_KIND_BRANCH_Y
} solar_equipment_kind_t;

typedef struct {
    solar_guid_t id;
    solar_equipment_kind_t kind;
    char name[SOLAR_EQUIPMENT_NAME_LEN];
    double position_x_mm;
    double position_y_mm;
    double width_mm;
    double height_mm;
    int rotation_degrees;
    int string_input_count;
    int rated_amps;
    char catalog_series[SOLAR_EQUIPMENT_CATALOG_SERIES_LEN];
    solar_port_t ports[SOLAR_EQUIPMENT_MAX_PORTS];
    size_t port_count;
} solar_equipment_instance_t;

typedef struct {
    double rafter_spacing_mm;
    double rail_overhang_mm;
    double attachment_edge_offset_mm;
} solar_racking_parameters_t;

typedef struct {
    int row_count;
    int rail_count;
    double total_rail_length_mm;
    int attachment_count;
    int end_clamp_count;
    int mid_clamp_count;
    bool valid;
} solar_racking_layout_t;

struct solar_project_state_t {
    int schema_version = 10;
    solar_guid_t project_id = {0, 0};
    char name[SOLAR_PROJECT_STATE_NAME_LEN] = {0};
    char file_path[SOLAR_PROJECT_STATE_FILE_PATH_LEN] = {0};
    solar_definition_catalog_t definitions = {};
    solar_electrical_graph_t graph = {};
    roof_document_t roof = {};
    solar_site_conditions_t site = {};
    struct {
        bool show_grid = true;
        bool snap_to_grid = false;
        bool panel_snapping = true;
        bool electrical_terminal_snapping = true;
        double panel_spacing_mm = 20.0;
        double grid_size_mm = 100.0;
        double zoom = 1.0;
        double camera_x_mm = 0.0;
        double camera_y_mm = 0.0;
    } canvas;
    solar_racking_parameters_t racking = {};
    std::vector<solar_equipment_instance_t> equipment;
    solar_racking_layout_t racking_layout = {};
};

#ifdef __cplusplus
extern "C" {
#endif

void solar_project_state_init(solar_project_state_t *state);
void solar_project_state_clear(solar_project_state_t *state);

const solar_panel_definition_t *solar_project_state_find_definition(
    const solar_project_state_t *state,
    const solar_guid_t *id);

bool solar_project_state_add_definition(
    solar_project_state_t *state,
    const solar_panel_definition_t *def);

bool solar_project_state_remove_definition(
    solar_project_state_t *state,
    const solar_guid_t *id);

solar_panel_instance_t *solar_project_state_add_panel(
    solar_project_state_t *state,
    const solar_guid_t *definition_id,
    double x_mm,
    double y_mm,
    int rotation_degrees,
    const solar_guid_t *id);

bool solar_project_state_remove_panel(
    solar_project_state_t *state,
    const solar_guid_t *panel_id);

bool solar_project_state_try_connect(
    solar_project_state_t *state,
    const solar_guid_t *start_port_id,
    const solar_guid_t *end_port_id,
    double length_mm,
    int gauge_awg);

bool solar_project_state_disconnect(
    solar_project_state_t *state,
    const solar_guid_t *connection_id);

solar_equipment_instance_t *solar_project_state_add_equipment(
    solar_project_state_t *state,
    const solar_equipment_instance_t *equipment);

bool solar_project_state_remove_equipment(
    solar_project_state_t *state,
    const solar_guid_t *equipment_id);

solar_project_result_t solar_project_state_calculate(
    const solar_project_state_t *state);

void solar_project_state_get_energy_estimate(
    const solar_project_state_t *state,
    solar_energy_estimate_t *out);

void solar_project_state_get_detailed_production_estimate(
    const solar_project_state_t *state,
    solar_detailed_production_estimate_t *out);

/* Sum the STC Pmax of all placed panel instances. */
double solar_project_state_get_total_dc_watts(
    const solar_project_state_t *state);

/* Count the number of placed panel instances. */
size_t solar_project_state_get_panel_count(
    const solar_project_state_t *state);

/* Count the number of equipment instances. */
size_t solar_project_state_get_equipment_count(
    const solar_project_state_t *state);

void solar_project_state_compute_racking_layout(
    solar_project_state_t *state);

void solar_project_state_create_demo_rectangular_roof(
    solar_project_state_t *state,
    double width_mm,
    double height_mm,
    double setback_mm);

void solar_project_state_create_demo_l_shaped_roof(
    solar_project_state_t *state,
    double setback_mm);

bool solar_project_state_evaluate_panel_placement(
    const solar_project_state_t *state,
    const solar_panel_instance_t *panel,
    double x_mm,
    double y_mm,
    bool *out_inside,
    double *out_distance_to_edge_mm);

#ifdef __cplusplus
}
#endif

#endif
