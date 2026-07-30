#ifndef EQUIPMENT_LIBRARY_H
#define EQUIPMENT_LIBRARY_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "solar_panel.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Equipment library: inverters, batteries, combiners, disconnects, and branch connectors.
 * Mirrors SolarSim.Domain.Equipment.InverterDefinition and ElectricalEquipmentInstance.
 */

#define SOLAR_EQUIPMENT_NAME_LEN 64
#define SOLAR_EQUIPMENT_LABEL_LEN 24
#define SOLAR_EQUIPMENT_CATALOG_LEN 32
#define SOLAR_EQUIPMENT_MAX_PORTS 32
#define SOLAR_EQUIPMENT_MAX_MPPT 8
#define SOLAR_EQUIPMENT_MAX_BUILT_IN 8

/* Equipment family classification. */
typedef enum {
    SOLAR_EQUIPMENT_KIND_COMBINER_BOX = 0,
    SOLAR_EQUIPMENT_KIND_PV_DISCONNECT,
    SOLAR_EQUIPMENT_KIND_BRANCH_Y_POSITIVE,
    SOLAR_EQUIPMENT_KIND_BRANCH_Y_NEGATIVE,
    SOLAR_EQUIPMENT_KIND_STRING_INVERTER,
    SOLAR_EQUIPMENT_KIND_AC_DISCONNECT,
    SOLAR_EQUIPMENT_KIND_AC_LOAD_CENTER,
    SOLAR_EQUIPMENT_KIND_BATTERY,
    SOLAR_EQUIPMENT_KIND_BATTERY_DISCONNECT
} solar_equipment_kind_t;

/* Electrical polarity for a single port. */
typedef enum {
    SOLAR_EQUIPMENT_POLARITY_POSITIVE = 0,
    SOLAR_EQUIPMENT_POLARITY_NEGATIVE
} solar_equipment_polarity_t;

/* Port role on a piece of equipment. */
typedef enum {
    SOLAR_EQUIPMENT_PORT_STRING_INPUT_POSITIVE = 0,
    SOLAR_EQUIPMENT_PORT_STRING_INPUT_NEGATIVE,
    SOLAR_EQUIPMENT_PORT_OUTPUT_POSITIVE,
    SOLAR_EQUIPMENT_PORT_OUTPUT_NEGATIVE,
    SOLAR_EQUIPMENT_PORT_DISCONNECT_IN_POSITIVE,
    SOLAR_EQUIPMENT_PORT_DISCONNECT_IN_NEGATIVE,
    SOLAR_EQUIPMENT_PORT_DISCONNECT_OUT_POSITIVE,
    SOLAR_EQUIPMENT_PORT_DISCONNECT_OUT_NEGATIVE,
    SOLAR_EQUIPMENT_PORT_MPPT_INPUT_POSITIVE,
    SOLAR_EQUIPMENT_PORT_MPPT_INPUT_NEGATIVE,
    SOLAR_EQUIPMENT_PORT_AC_LINE,
    SOLAR_EQUIPMENT_PORT_AC_NEUTRAL,
    SOLAR_EQUIPMENT_PORT_AC_GROUND,
    SOLAR_EQUIPMENT_PORT_AC_LOAD,
    SOLAR_EQUIPMENT_PORT_BRANCH_IN_1,
    SOLAR_EQUIPMENT_PORT_BRANCH_IN_2,
    SOLAR_EQUIPMENT_PORT_BRANCH_OUT
} solar_equipment_port_type_t;

/* Catalog definition for a string / hybrid inverter. */
typedef struct {
    solar_guid_t id;
    char manufacturer[SOLAR_MANUFACTURER_LEN];
    char model[SOLAR_MODEL_LEN];
    double ac_rated_watts;
    int mppt_count;
    double min_mppt_volts;
    double max_mppt_volts;
    double max_dc_volts;
    double max_current_per_mppt_amps;
    double max_dc_power_per_mppt_watts;
    bool is_custom;
    bool has_hybrid_terminals;
} solar_inverter_definition_t;

/* Electrical snapshot copied onto an inverter instance. */
typedef struct {
    solar_guid_t definition_id;
    double ac_rated_watts;
    int mppt_count;
    double min_mppt_volts;
    double max_mppt_volts;
    double max_dc_volts;
    double max_current_per_mppt_amps;
    double max_dc_power_per_mppt_watts;
} solar_inverter_electrical_specs_t;

/* A single port on a piece of electrical equipment. */
typedef struct {
    solar_guid_t id;
    solar_guid_t owner_id;
    solar_equipment_port_type_t type;
    solar_equipment_polarity_t polarity;
    char label[SOLAR_EQUIPMENT_LABEL_LEN];
    bool is_occupied;
    solar_guid_t connection_id;
} solar_equipment_port_t;

/* Placed instance of a combiner, inverter, disconnect, battery, etc. */
typedef struct {
    solar_guid_t id;
    solar_equipment_kind_t kind;
    char name[SOLAR_EQUIPMENT_NAME_LEN];
    double position_x_mm;
    double position_y_mm;
    double width_mm;
    double height_mm;
    double rotation_degrees;
    int string_input_count;
    solar_inverter_electrical_specs_t inverter_specs;
    bool has_inverter_specs;
    int rated_amps;
    char catalog_series[SOLAR_EQUIPMENT_CATALOG_LEN];
    solar_equipment_port_t ports[SOLAR_EQUIPMENT_MAX_PORTS];
    size_t port_count;
} solar_equipment_instance_t;

/* Inverter definition helpers and validation. */
bool solar_inverter_definition_is_valid(const solar_inverter_definition_t *def);
void solar_inverter_electrical_specs_from_definition(
    const solar_inverter_definition_t *def,
    solar_inverter_electrical_specs_t *specs);

/* Built-in inverter definitions. */
void solar_inverter_definition_generic_5kw_2mppt(solar_inverter_definition_t *def);
void solar_inverter_definition_generic_7_6kw_3mppt(solar_inverter_definition_t *def);
void solar_inverter_definition_anenji_12kw_2mppt(solar_inverter_definition_t *def);
void solar_inverter_definition_anenji_4_2kw_1mppt(solar_inverter_definition_t *def);
void solar_inverter_definition_anenji_6_5kw_2mppt(solar_inverter_definition_t *def);

/* Enumerate the built-in inverter library.
 * out_array must be able to hold at least SOLAR_EQUIPMENT_MAX_BUILT_IN entries.
 * out_count receives the number written. */
void solar_equipment_built_in_inverters(
    solar_inverter_definition_t *out_array,
    size_t max_count,
    size_t *out_count);

/* Equipment instance lifecycle and mutation. */
void solar_equipment_instance_init(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    solar_equipment_kind_t kind,
    const char *name,
    double x_mm,
    double y_mm,
    double width_mm,
    double height_mm,
    int string_input_count);

void solar_equipment_instance_set_position(solar_equipment_instance_t *inst, double x_mm, double y_mm);
void solar_equipment_instance_set_size(solar_equipment_instance_t *inst, double width_mm, double height_mm);
void solar_equipment_instance_set_rotation(solar_equipment_instance_t *inst, double degrees);
void solar_equipment_instance_rotate_by(solar_equipment_instance_t *inst, double delta_degrees);

const solar_equipment_port_t *solar_equipment_find_port(
    const solar_equipment_instance_t *inst,
    const solar_guid_t *port_id);
bool solar_equipment_instance_is_valid(const solar_equipment_instance_t *inst);

/* Factory constructors for each equipment family. */
void solar_equipment_create_combiner(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    int string_inputs,
    const char *name);

void solar_equipment_create_pv_disconnect(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const char *name);

void solar_equipment_create_branch_y(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    solar_equipment_polarity_t polarity,
    const char *name);

void solar_equipment_create_string_inverter(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const solar_inverter_definition_t *definition,
    const char *name);

void solar_equipment_create_ac_disconnect(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const char *name);

void solar_equipment_create_ac_load_center(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const char *name);

void solar_equipment_create_battery(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const char *name,
    const char *catalog_series);

void solar_equipment_create_battery_16kwh(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const char *name);

void solar_equipment_create_battery_10kw_wall(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const char *name);

void solar_equipment_create_battery_5_1kwh_rack(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const char *name);

void solar_equipment_create_battery_12_8v_300ah(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const char *name);

void solar_equipment_create_battery_disconnect(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const char *name,
    int rated_amps,
    const char *catalog_series);

/* Battery classification helpers. */
bool solar_equipment_is_battery_dual_terminal(const solar_equipment_instance_t *inst);
bool solar_equipment_is_battery_prismatic(const solar_equipment_instance_t *inst);
bool solar_equipment_is_battery_rack(const solar_equipment_instance_t *inst);
bool solar_equipment_is_battery_10kw_wall(const solar_equipment_instance_t *inst);

#ifdef __cplusplus
}
#endif

#endif
