#ifndef MPPT_COMPATIBILITY_H
#define MPPT_COMPATIBILITY_H

#include <stdbool.h>
#include <stddef.h>

#include "electrical_graph.h"
#include "solar_panel.h"
#include "string_calculation.h"
#include "string_sizing.h"
#include "temperature_derating.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Equipment kinds mirrored from SolarSim.Domain.Equipment.EquipmentKind. */
typedef enum {
    SOLAR_EQUIPMENT_COMBINER_BOX = 0,
    SOLAR_EQUIPMENT_PV_DISCONNECT,
    SOLAR_EQUIPMENT_BRANCH_Y_POSITIVE,
    SOLAR_EQUIPMENT_BRANCH_Y_NEGATIVE,
    SOLAR_EQUIPMENT_STRING_INVERTER,
    SOLAR_EQUIPMENT_AC_DISCONNECT,
    SOLAR_EQUIPMENT_AC_LOAD_CENTER,
    SOLAR_EQUIPMENT_BATTERY,
    SOLAR_EQUIPMENT_BATTERY_DISCONNECT,
} solar_equipment_kind_t;

#define SOLAR_EQUIPMENT_PORT_LABEL_LEN 32
#define SOLAR_EQUIPMENT_MAX_PORTS 32
#define SOLAR_EQUIPMENT_NAME_LEN 64

/* Equipment port with a label and a port type, in addition to the base solar port. */
typedef struct {
    solar_port_t base;
    char label[SOLAR_EQUIPMENT_PORT_LABEL_LEN];
    int port_type;
} solar_equipment_port_t;

/* Equipment instance. Mirrors ElectricalEquipmentInstance. */
typedef struct {
    solar_guid_t id;
    solar_equipment_kind_t kind;
    char name[SOLAR_EQUIPMENT_NAME_LEN];
    double position_x_mm;
    double position_y_mm;
    double width_mm;
    double height_mm;
    int string_input_count;
    bool has_inverter_specs;
    solar_inverter_electrical_specs_t inverter_specs;
    solar_equipment_port_t ports[SOLAR_EQUIPMENT_MAX_PORTS];
    size_t port_count;
    int rated_amps;
    char catalog_series[32];
} solar_equipment_instance_t;

#define SOLAR_MPPT_MAX_ISSUES 16
#define SOLAR_MPPT_MAX_REACHED_PANELS 64
#define SOLAR_MPPT_MAX_CHANNELS 8
#define SOLAR_MPPT_MAX_STRING_IDS 16

typedef enum {
    SOLAR_MPPT_SEVERITY_INFO = 0,
    SOLAR_MPPT_SEVERITY_WARNING,
    SOLAR_MPPT_SEVERITY_ERROR
} solar_mppt_severity_t;

typedef struct {
    solar_mppt_severity_t severity;
    char code[32];
    char message[128];
    char detail[256];
} solar_mppt_issue_t;

typedef struct {
    int channel_index;
    solar_guid_t positive_port_id;
    solar_guid_t negative_port_id;
    bool positive_connected;
    bool negative_connected;
    solar_guid_t panel_ids[SOLAR_MPPT_MAX_REACHED_PANELS];
    size_t panel_count;
    solar_guid_t string_ids[SOLAR_MPPT_MAX_STRING_IDS];
    size_t string_count;
    double voc_volts;
    double cold_voc_volts;
    double vmp_volts;
    double hot_vmp_volts;
    double imp_amps;
    double isc_amps;
    double pmax_watts;
    int module_count;
    bool has_voc;
    bool has_cold_voc;
    bool has_vmp;
    bool has_hot_vmp;
    bool has_imp;
    bool has_isc;
    bool has_pmax;
    solar_mppt_issue_t issues[SOLAR_MPPT_MAX_ISSUES];
    size_t issue_count;
} solar_mppt_channel_report_t;

typedef struct {
    solar_guid_t inverter_id;
    char name[SOLAR_EQUIPMENT_NAME_LEN];
    solar_inverter_electrical_specs_t specs;
    solar_mppt_channel_report_t channels[SOLAR_MPPT_MAX_CHANNELS];
    size_t channel_count;
    solar_mppt_issue_t issues[SOLAR_MPPT_MAX_ISSUES];
    size_t issue_count;
    double total_dc_watts;
} solar_inverter_mppt_report_t;

/* Equipment helpers. */
void solar_equipment_instance_init(solar_equipment_instance_t *eq, const solar_guid_t *id, solar_equipment_kind_t kind, const char *name);
bool solar_equipment_add_port(solar_equipment_instance_t *eq, const solar_equipment_port_t *port);
solar_equipment_port_t *solar_equipment_find_port_by_label(solar_equipment_instance_t *eq, const char *label);
solar_equipment_port_t *solar_equipment_find_port_by_type_and_label(solar_equipment_instance_t *eq, int port_type, const char *label);
bool solar_equipment_is_inverter(const solar_equipment_instance_t *eq);
bool solar_equipment_is_battery_disconnect(const solar_equipment_instance_t *eq);
bool solar_equipment_is_battery(const solar_equipment_instance_t *eq);

/* Factory helpers for common inverters. */
void solar_equipment_create_string_inverter(solar_equipment_instance_t *eq, const solar_guid_t *id, const solar_inverter_electrical_specs_t *specs, const char *name);

/* Evaluate a single inverter's MPPT inputs against the graph and string results. */
void solar_mppt_compatibility_evaluate_inverter(
    const solar_electrical_graph_t *graph,
    const solar_equipment_instance_t *inverter,
    const solar_project_result_t *project_calc,
    const solar_definition_catalog_t *catalog,
    const solar_site_design_conditions_t *site,
    solar_inverter_mppt_report_t *out_report);

/* Evaluate all inverters in an array. */
void solar_mppt_compatibility_evaluate_all(
    const solar_electrical_graph_t *graph,
    const solar_equipment_instance_t *inverters,
    size_t inverter_count,
    const solar_project_result_t *project_calc,
    const solar_definition_catalog_t *catalog,
    const solar_site_design_conditions_t *site,
    solar_inverter_mppt_report_t *out_reports,
    size_t max_reports,
    size_t *out_report_count);

#ifdef __cplusplus
}
#endif

#endif
