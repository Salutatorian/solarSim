#include "equipment_library.h"

#include <cctype>
#include <cmath>
#include <cstdio>
#include <cstring>

static uint64_t g_next_guid_low = 0x2000;

static void make_guid(solar_guid_t *guid) {
    if (!guid) return;
    guid->id_high = 0;
    guid->id_low = g_next_guid_low++;
}

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

static double normalize_rotation(double degrees) {
    double n = std::fmod(degrees, 360.0);
    if (n < 0.0) n += 360.0;
    if (std::fabs(n - 360.0) < 1e-9) n = 0.0;
    return n;
}

static bool str_equals_ignore_case(const char *a, const char *b) {
    if (!a || !b) return a == b;
    while (*a && *b) {
        if (std::tolower(static_cast<unsigned char>(*a)) !=
            std::tolower(static_cast<unsigned char>(*b))) {
            return false;
        }
        ++a;
        ++b;
    }
    return *a == '\0' && *b == '\0';
}

static bool add_port(
    solar_equipment_instance_t *inst,
    solar_equipment_port_type_t type,
    solar_equipment_polarity_t polarity,
    const char *label) {
    if (!inst || inst->port_count >= SOLAR_EQUIPMENT_MAX_PORTS) return false;
    solar_equipment_port_t *port = &inst->ports[inst->port_count];
    std::memset(port, 0, sizeof(*port));
    make_guid(&port->id);
    port->owner_id = inst->id;
    port->type = type;
    port->polarity = polarity;
    copy_string(port->label, SOLAR_EQUIPMENT_LABEL_LEN, label);
    port->is_occupied = false;
    solar_panel_guid_zero(&port->connection_id);
    inst->port_count++;
    return true;
}

bool solar_inverter_definition_is_valid(const solar_inverter_definition_t *def) {
    if (!def) return false;
    if (def->mppt_count < 1 || def->mppt_count > SOLAR_EQUIPMENT_MAX_MPPT) return false;
    if (!std::isfinite(def->min_mppt_volts) || def->min_mppt_volts <= 0.0) return false;
    if (!std::isfinite(def->max_mppt_volts) || def->max_mppt_volts <= def->min_mppt_volts) return false;
    if (!std::isfinite(def->max_dc_volts) || def->max_dc_volts < def->max_mppt_volts) return false;
    if (!std::isfinite(def->ac_rated_watts) || def->ac_rated_watts <= 0.0) return false;
    if (!std::isfinite(def->max_current_per_mppt_amps) || def->max_current_per_mppt_amps <= 0.0) return false;
    if (!std::isfinite(def->max_dc_power_per_mppt_watts) || def->max_dc_power_per_mppt_watts <= 0.0) return false;
    return true;
}

void solar_inverter_electrical_specs_from_definition(
    const solar_inverter_definition_t *def,
    solar_inverter_electrical_specs_t *specs) {
    if (!specs) return;
    std::memset(specs, 0, sizeof(*specs));
    if (!def) return;
    specs->definition_id = def->id;
    specs->ac_rated_watts = def->ac_rated_watts;
    specs->mppt_count = def->mppt_count;
    specs->min_mppt_volts = def->min_mppt_volts;
    specs->max_mppt_volts = def->max_mppt_volts;
    specs->max_dc_volts = def->max_dc_volts;
    specs->max_current_per_mppt_amps = def->max_current_per_mppt_amps;
    specs->max_dc_power_per_mppt_watts = def->max_dc_power_per_mppt_watts;
}

static void set_inverter_definition_id(
    solar_inverter_definition_t *def,
    uint64_t low) {
    if (!def) return;
    def->id.id_high = 0xA111111100044000ULL;
    def->id.id_low = 0x8000000000000000ULL | low;
}

void solar_inverter_definition_generic_5kw_2mppt(solar_inverter_definition_t *def) {
    if (!def) return;
    std::memset(def, 0, sizeof(*def));
    set_inverter_definition_id(def, 0x1);
    copy_string(def->manufacturer, SOLAR_MANUFACTURER_LEN, "Generic");
    copy_string(def->model, SOLAR_MODEL_LEN, "5kW-2MPPT");
    def->ac_rated_watts = 5000.0;
    def->mppt_count = 2;
    def->min_mppt_volts = 80.0;
    def->max_mppt_volts = 480.0;
    def->max_dc_volts = 600.0;
    def->max_current_per_mppt_amps = 12.5;
    def->max_dc_power_per_mppt_watts = 4000.0;
    def->is_custom = false;
    def->has_hybrid_terminals = false;
}

void solar_inverter_definition_generic_7_6kw_3mppt(solar_inverter_definition_t *def) {
    if (!def) return;
    std::memset(def, 0, sizeof(*def));
    set_inverter_definition_id(def, 0x2);
    copy_string(def->manufacturer, SOLAR_MANUFACTURER_LEN, "Generic");
    copy_string(def->model, SOLAR_MODEL_LEN, "7.6kW-3MPPT");
    def->ac_rated_watts = 7600.0;
    def->mppt_count = 3;
    def->min_mppt_volts = 100.0;
    def->max_mppt_volts = 500.0;
    def->max_dc_volts = 600.0;
    def->max_current_per_mppt_amps = 13.0;
    def->max_dc_power_per_mppt_watts = 4500.0;
    def->is_custom = false;
    def->has_hybrid_terminals = false;
}

void solar_inverter_definition_anenji_12kw_2mppt(solar_inverter_definition_t *def) {
    if (!def) return;
    std::memset(def, 0, sizeof(*def));
    set_inverter_definition_id(def, 0x3);
    copy_string(def->manufacturer, SOLAR_MANUFACTURER_LEN, "ANENJI");
    copy_string(def->model, SOLAR_MODEL_LEN, "12kW Hybrid");
    def->ac_rated_watts = 12000.0;
    def->mppt_count = 2;
    def->min_mppt_volts = 90.0;
    def->max_mppt_volts = 500.0;
    def->max_dc_volts = 500.0;
    def->max_current_per_mppt_amps = 22.0;
    def->max_dc_power_per_mppt_watts = 7500.0;
    def->is_custom = false;
    def->has_hybrid_terminals = true;
}

void solar_inverter_definition_anenji_4_2kw_1mppt(solar_inverter_definition_t *def) {
    if (!def) return;
    std::memset(def, 0, sizeof(*def));
    set_inverter_definition_id(def, 0x4);
    copy_string(def->manufacturer, SOLAR_MANUFACTURER_LEN, "ANENJI");
    copy_string(def->model, SOLAR_MODEL_LEN, "4.2kW Hybrid");
    def->ac_rated_watts = 4200.0;
    def->mppt_count = 1;
    def->min_mppt_volts = 60.0;
    def->max_mppt_volts = 450.0;
    def->max_dc_volts = 500.0;
    def->max_current_per_mppt_amps = 18.0;
    def->max_dc_power_per_mppt_watts = 4500.0;
    def->is_custom = false;
    def->has_hybrid_terminals = true;
}

void solar_inverter_definition_anenji_6_5kw_2mppt(solar_inverter_definition_t *def) {
    if (!def) return;
    std::memset(def, 0, sizeof(*def));
    set_inverter_definition_id(def, 0x5);
    copy_string(def->manufacturer, SOLAR_MANUFACTURER_LEN, "ANENJI");
    copy_string(def->model, SOLAR_MODEL_LEN, "6.5kW Hybrid");
    def->ac_rated_watts = 6500.0;
    def->mppt_count = 2;
    def->min_mppt_volts = 90.0;
    def->max_mppt_volts = 500.0;
    def->max_dc_volts = 500.0;
    def->max_current_per_mppt_amps = 18.0;
    def->max_dc_power_per_mppt_watts = 4000.0;
    def->is_custom = false;
    def->has_hybrid_terminals = true;
}

void solar_equipment_built_in_inverters(
    solar_inverter_definition_t *out_array,
    size_t max_count,
    size_t *out_count) {
    if (!out_count) return;
    *out_count = 0;
    if (!out_array || max_count == 0) return;

    solar_inverter_definition_t defs[5];
    solar_inverter_definition_generic_5kw_2mppt(&defs[0]);
    solar_inverter_definition_generic_7_6kw_3mppt(&defs[1]);
    solar_inverter_definition_anenji_4_2kw_1mppt(&defs[2]);
    solar_inverter_definition_anenji_6_5kw_2mppt(&defs[3]);
    solar_inverter_definition_anenji_12kw_2mppt(&defs[4]);

    size_t n = max_count < 5 ? max_count : 5;
    for (size_t i = 0; i < n; i++) {
        out_array[i] = defs[i];
    }
    *out_count = n;
}

void solar_equipment_instance_init(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    solar_equipment_kind_t kind,
    const char *name,
    double x_mm,
    double y_mm,
    double width_mm,
    double height_mm,
    int string_input_count) {
    if (!inst) return;
    std::memset(inst, 0, sizeof(*inst));
    if (id) inst->id = *id;
    inst->kind = kind;
    copy_string(inst->name, SOLAR_EQUIPMENT_NAME_LEN, name);
    inst->position_x_mm = x_mm;
    inst->position_y_mm = y_mm;
    inst->width_mm = width_mm;
    inst->height_mm = height_mm;
    inst->rotation_degrees = 0.0;
    inst->string_input_count = string_input_count;
    inst->has_inverter_specs = false;
    inst->rated_amps = 0;
    inst->port_count = 0;
}

void solar_equipment_instance_set_position(solar_equipment_instance_t *inst, double x_mm, double y_mm) {
    if (!inst) return;
    inst->position_x_mm = x_mm;
    inst->position_y_mm = y_mm;
}

void solar_equipment_instance_set_size(solar_equipment_instance_t *inst, double width_mm, double height_mm) {
    if (!inst) return;
    if (width_mm < 180.0) width_mm = 180.0;
    if (width_mm > 4000.0) width_mm = 4000.0;
    if (height_mm < 180.0) height_mm = 180.0;
    if (height_mm > 4000.0) height_mm = 4000.0;
    inst->width_mm = width_mm;
    inst->height_mm = height_mm;
}

void solar_equipment_instance_set_rotation(solar_equipment_instance_t *inst, double degrees) {
    if (!inst) return;
    inst->rotation_degrees = normalize_rotation(degrees);
}

void solar_equipment_instance_rotate_by(solar_equipment_instance_t *inst, double delta_degrees) {
    if (!inst) return;
    inst->rotation_degrees = normalize_rotation(inst->rotation_degrees + delta_degrees);
}

const solar_equipment_port_t *solar_equipment_find_port(
    const solar_equipment_instance_t *inst,
    const solar_guid_t *port_id) {
    if (!inst || !port_id) return NULL;
    for (size_t i = 0; i < inst->port_count; i++) {
        if (solar_panel_guid_equals(&inst->ports[i].id, port_id)) {
            return &inst->ports[i];
        }
    }
    return NULL;
}

bool solar_equipment_instance_is_valid(const solar_equipment_instance_t *inst) {
    if (!inst) return false;
    if (inst->port_count == 0) return false;
    if (!std::isfinite(inst->width_mm) || inst->width_mm <= 0.0) return false;
    if (!std::isfinite(inst->height_mm) || inst->height_mm <= 0.0) return false;
    for (size_t i = 0; i < inst->port_count; i++) {
        if (!solar_panel_guid_equals(&inst->ports[i].owner_id, &inst->id)) return false;
    }
    return true;
}

void solar_equipment_create_combiner(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    int string_inputs,
    const char *name) {
    if (!inst) return;
    if (string_inputs < 1) string_inputs = 1;
    if (string_inputs > 12) string_inputs = 12;

    char display_name[SOLAR_EQUIPMENT_NAME_LEN];
    if (name && name[0] != '\0') {
        copy_string(display_name, sizeof(display_name), name);
    } else {
        std::snprintf(display_name, sizeof(display_name), "%d-String Combiner", string_inputs);
    }

    solar_equipment_instance_init(
        inst, id, SOLAR_EQUIPMENT_KIND_COMBINER_BOX, display_name,
        x_mm, y_mm, 1000.0, 980.0, string_inputs);

    for (int i = 1; i <= string_inputs; i++) {
        char label[SOLAR_EQUIPMENT_LABEL_LEN];
        std::snprintf(label, sizeof(label), "S%d+", i);
        add_port(inst, SOLAR_EQUIPMENT_PORT_STRING_INPUT_POSITIVE, SOLAR_EQUIPMENT_POLARITY_POSITIVE, label);
        std::snprintf(label, sizeof(label), "S%d-", i);
        add_port(inst, SOLAR_EQUIPMENT_PORT_STRING_INPUT_NEGATIVE, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, label);
    }
    add_port(inst, SOLAR_EQUIPMENT_PORT_OUTPUT_POSITIVE, SOLAR_EQUIPMENT_POLARITY_POSITIVE, "OUT+");
    add_port(inst, SOLAR_EQUIPMENT_PORT_OUTPUT_NEGATIVE, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, "OUT-");
}

void solar_equipment_create_pv_disconnect(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const char *name) {
    if (!inst) return;
    solar_equipment_instance_init(
        inst, id, SOLAR_EQUIPMENT_KIND_PV_DISCONNECT,
        name && name[0] != '\0' ? name : "Solar Disconnect",
        x_mm, y_mm, 400.0, 900.0, 0);
    add_port(inst, SOLAR_EQUIPMENT_PORT_DISCONNECT_IN_POSITIVE, SOLAR_EQUIPMENT_POLARITY_POSITIVE, "IN+");
    add_port(inst, SOLAR_EQUIPMENT_PORT_DISCONNECT_IN_NEGATIVE, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, "IN-");
    add_port(inst, SOLAR_EQUIPMENT_PORT_DISCONNECT_OUT_POSITIVE, SOLAR_EQUIPMENT_POLARITY_POSITIVE, "OUT+");
    add_port(inst, SOLAR_EQUIPMENT_PORT_DISCONNECT_OUT_NEGATIVE, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, "OUT-");
}

void solar_equipment_create_branch_y(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    solar_equipment_polarity_t polarity,
    const char *name) {
    if (!inst) return;
    solar_equipment_kind_t kind = (polarity == SOLAR_EQUIPMENT_POLARITY_POSITIVE)
        ? SOLAR_EQUIPMENT_KIND_BRANCH_Y_POSITIVE
        : SOLAR_EQUIPMENT_KIND_BRANCH_Y_NEGATIVE;
    const char *prefix = (polarity == SOLAR_EQUIPMENT_POLARITY_POSITIVE) ? "Y+" : "Y-";
    char display_name[SOLAR_EQUIPMENT_NAME_LEN];
    if (name && name[0] != '\0') {
        copy_string(display_name, sizeof(display_name), name);
    } else {
        std::snprintf(display_name, sizeof(display_name), "MC4 Y (%s)",
            polarity == SOLAR_EQUIPMENT_POLARITY_POSITIVE ? "Positive" : "Negative");
    }
    solar_equipment_instance_init(inst, id, kind, display_name, x_mm, y_mm, 420.0, 280.0, 0);
    char label[SOLAR_EQUIPMENT_LABEL_LEN];
    std::snprintf(label, sizeof(label), "%s A", prefix);
    add_port(inst, SOLAR_EQUIPMENT_PORT_BRANCH_IN_1, polarity, label);
    std::snprintf(label, sizeof(label), "%s B", prefix);
    add_port(inst, SOLAR_EQUIPMENT_PORT_BRANCH_IN_2, polarity, label);
    std::snprintf(label, sizeof(label), "%s Out", prefix);
    add_port(inst, SOLAR_EQUIPMENT_PORT_BRANCH_OUT, polarity, label);
}

void solar_equipment_create_string_inverter(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const solar_inverter_definition_t *definition,
    const char *name) {
    if (!inst) return;
    if (!definition) {
        solar_equipment_instance_init(inst, id, SOLAR_EQUIPMENT_KIND_STRING_INVERTER, name, x_mm, y_mm, 1100.0, 520.0, 0);
        return;
    }

    solar_inverter_electrical_specs_t specs;
    solar_inverter_electrical_specs_from_definition(definition, &specs);

    char display_name[SOLAR_EQUIPMENT_NAME_LEN];
    if (name && name[0] != '\0') {
        copy_string(display_name, sizeof(display_name), name);
    } else {
        std::snprintf(display_name, sizeof(display_name), "%s %s",
            definition->manufacturer, definition->model);
    }

    double width = definition->has_hybrid_terminals ? 720.0 : 1100.0;
    double height = definition->has_hybrid_terminals
        ? 1280.0
        : 520.0 + specs.mppt_count * 70.0;

    solar_equipment_instance_init(
        inst, id, SOLAR_EQUIPMENT_KIND_STRING_INVERTER, display_name,
        x_mm, y_mm, width, height, specs.mppt_count);
    inst->inverter_specs = specs;
    inst->has_inverter_specs = true;

    for (int i = 1; i <= specs.mppt_count; i++) {
        char label[SOLAR_EQUIPMENT_LABEL_LEN];
        std::snprintf(label, sizeof(label), "MPPT%d+", i);
        add_port(inst, SOLAR_EQUIPMENT_PORT_MPPT_INPUT_POSITIVE, SOLAR_EQUIPMENT_POLARITY_POSITIVE, label);
        std::snprintf(label, sizeof(label), "MPPT%d-", i);
        add_port(inst, SOLAR_EQUIPMENT_PORT_MPPT_INPUT_NEGATIVE, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, label);
    }

    if (definition->has_hybrid_terminals) {
        add_port(inst, SOLAR_EQUIPMENT_PORT_AC_LINE, SOLAR_EQUIPMENT_POLARITY_POSITIVE, "AC IN L");
        add_port(inst, SOLAR_EQUIPMENT_PORT_AC_NEUTRAL, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, "AC IN N");
        add_port(inst, SOLAR_EQUIPMENT_PORT_AC_GROUND, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, "AC IN G");
        add_port(inst, SOLAR_EQUIPMENT_PORT_AC_LINE, SOLAR_EQUIPMENT_POLARITY_POSITIVE, "AC OUT L");
        add_port(inst, SOLAR_EQUIPMENT_PORT_AC_NEUTRAL, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, "AC OUT N");
        add_port(inst, SOLAR_EQUIPMENT_PORT_AC_GROUND, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, "AC OUT G");
    }

    add_port(inst, SOLAR_EQUIPMENT_PORT_OUTPUT_POSITIVE, SOLAR_EQUIPMENT_POLARITY_POSITIVE, "BAT+");
    add_port(inst, SOLAR_EQUIPMENT_PORT_OUTPUT_NEGATIVE, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, "BAT-");
}

void solar_equipment_create_ac_disconnect(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const char *name) {
    if (!inst) return;
    solar_equipment_instance_init(
        inst, id, SOLAR_EQUIPMENT_KIND_AC_DISCONNECT,
        name && name[0] != '\0' ? name : "AC Disconnect",
        x_mm, y_mm, 700.0, 520.0, 0);
    add_port(inst, SOLAR_EQUIPMENT_PORT_AC_LINE, SOLAR_EQUIPMENT_POLARITY_POSITIVE, "AC IN L");
    add_port(inst, SOLAR_EQUIPMENT_PORT_AC_NEUTRAL, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, "AC IN N");
    add_port(inst, SOLAR_EQUIPMENT_PORT_AC_LINE, SOLAR_EQUIPMENT_POLARITY_POSITIVE, "AC OUT L");
    add_port(inst, SOLAR_EQUIPMENT_PORT_AC_NEUTRAL, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, "AC OUT N");
    add_port(inst, SOLAR_EQUIPMENT_PORT_AC_GROUND, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, "GND");
}

void solar_equipment_create_ac_load_center(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const char *name) {
    if (!inst) return;
    solar_equipment_instance_init(
        inst, id, SOLAR_EQUIPMENT_KIND_AC_LOAD_CENTER,
        name && name[0] != '\0' ? name : "AC Load Center",
        x_mm, y_mm, 900.0, 700.0, 0);
    add_port(inst, SOLAR_EQUIPMENT_PORT_AC_LINE, SOLAR_EQUIPMENT_POLARITY_POSITIVE, "AC IN L");
    add_port(inst, SOLAR_EQUIPMENT_PORT_AC_NEUTRAL, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, "AC IN N");
    add_port(inst, SOLAR_EQUIPMENT_PORT_AC_LOAD, SOLAR_EQUIPMENT_POLARITY_POSITIVE, "LOAD L");
    add_port(inst, SOLAR_EQUIPMENT_PORT_AC_LOAD, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, "LOAD N");
    add_port(inst, SOLAR_EQUIPMENT_PORT_AC_GROUND, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, "GND");
}

static void create_battery_common(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const char *name,
    const char *catalog_series,
    double width_mm,
    double height_mm) {
    solar_equipment_instance_init(
        inst, id, SOLAR_EQUIPMENT_KIND_BATTERY, name,
        x_mm, y_mm, width_mm, height_mm, 0);
    copy_string(inst->catalog_series, SOLAR_EQUIPMENT_CATALOG_LEN, catalog_series);
    add_port(inst, SOLAR_EQUIPMENT_PORT_OUTPUT_POSITIVE, SOLAR_EQUIPMENT_POLARITY_POSITIVE, "BAT1+");
    add_port(inst, SOLAR_EQUIPMENT_PORT_OUTPUT_NEGATIVE, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, "BAT1-");
    add_port(inst, SOLAR_EQUIPMENT_PORT_OUTPUT_POSITIVE, SOLAR_EQUIPMENT_POLARITY_POSITIVE, "BAT2+");
    add_port(inst, SOLAR_EQUIPMENT_PORT_OUTPUT_NEGATIVE, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, "BAT2-");
}

void solar_equipment_create_battery(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const char *name,
    const char *catalog_series) {
    if (!inst) return;
    create_battery_common(
        inst, id, x_mm, y_mm,
        name && name[0] != '\0' ? name : "ANENJI 16kWh",
        catalog_series && catalog_series[0] != '\0' ? catalog_series : "ANENJI-16kWh",
        720.0, 1380.0);
}

void solar_equipment_create_battery_16kwh(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const char *name) {
    solar_equipment_create_battery(inst, id, x_mm, y_mm, name, "ANENJI-16kWh");
}

void solar_equipment_create_battery_10kw_wall(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const char *name) {
    if (!inst) return;
    create_battery_common(
        inst, id, x_mm, y_mm,
        name && name[0] != '\0' ? name : "ANENJI 10kW",
        "ANENJI-10kW", 720.0, 1280.0);
}

void solar_equipment_create_battery_5_1kwh_rack(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const char *name) {
    if (!inst) return;
    create_battery_common(
        inst, id, x_mm, y_mm,
        name && name[0] != '\0' ? name : "ANENJI 5.1kWh Rack",
        "ANENJI-5.1kWh-Rack", 1600.0, 500.0);
}

void solar_equipment_create_battery_12_8v_300ah(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const char *name) {
    if (!inst) return;
    solar_equipment_instance_init(
        inst, id, SOLAR_EQUIPMENT_KIND_BATTERY,
        name && name[0] != '\0' ? name : "ANENJI 12.8V 300Ah",
        x_mm, y_mm, 1400.0, 620.0, 0);
    copy_string(inst->catalog_series, SOLAR_EQUIPMENT_CATALOG_LEN, "ANENJI-12.8V-300Ah");
    add_port(inst, SOLAR_EQUIPMENT_PORT_OUTPUT_POSITIVE, SOLAR_EQUIPMENT_POLARITY_POSITIVE, "BAT+");
    add_port(inst, SOLAR_EQUIPMENT_PORT_OUTPUT_NEGATIVE, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, "BAT-");
}

void solar_equipment_create_battery_disconnect(
    solar_equipment_instance_t *inst,
    const solar_guid_t *id,
    double x_mm,
    double y_mm,
    const char *name,
    int rated_amps,
    const char *catalog_series) {
    if (!inst) return;
    static const int allowed[] = {100, 150, 200, 250, 300, 400};
    bool allowed_rating = false;
    for (size_t i = 0; i < sizeof(allowed) / sizeof(allowed[0]); i++) {
        if (rated_amps == allowed[i]) {
            allowed_rating = true;
            break;
        }
    }
    if (!allowed_rating) rated_amps = 250;
    char series[SOLAR_EQUIPMENT_CATALOG_LEN];
    if (catalog_series && catalog_series[0] != '\0') {
        copy_string(series, sizeof(series), catalog_series);
    } else {
        copy_string(series, sizeof(series), "DHM1B");
    }
    char display_name[SOLAR_EQUIPMENT_NAME_LEN];
    if (name && name[0] != '\0') {
        copy_string(display_name, sizeof(display_name), name);
    } else {
        std::snprintf(display_name, sizeof(display_name), "Battery Disconnect %dA", rated_amps);
    }
    solar_equipment_instance_init(
        inst, id, SOLAR_EQUIPMENT_KIND_BATTERY_DISCONNECT, display_name,
        x_mm, y_mm, 420.0, 920.0, 0);
    inst->rated_amps = rated_amps;
    copy_string(inst->catalog_series, SOLAR_EQUIPMENT_CATALOG_LEN, series);
    add_port(inst, SOLAR_EQUIPMENT_PORT_DISCONNECT_IN_POSITIVE, SOLAR_EQUIPMENT_POLARITY_POSITIVE, "IN+");
    add_port(inst, SOLAR_EQUIPMENT_PORT_DISCONNECT_IN_NEGATIVE, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, "IN-");
    add_port(inst, SOLAR_EQUIPMENT_PORT_DISCONNECT_OUT_POSITIVE, SOLAR_EQUIPMENT_POLARITY_POSITIVE, "OUT+");
    add_port(inst, SOLAR_EQUIPMENT_PORT_DISCONNECT_OUT_NEGATIVE, SOLAR_EQUIPMENT_POLARITY_NEGATIVE, "OUT-");
}

bool solar_equipment_is_battery_prismatic(const solar_equipment_instance_t *inst) {
    if (!inst) return false;
    return inst->kind == SOLAR_EQUIPMENT_KIND_BATTERY &&
        str_equals_ignore_case(inst->catalog_series, "ANENJI-12.8V-300Ah");
}

bool solar_equipment_is_battery_rack(const solar_equipment_instance_t *inst) {
    if (!inst) return false;
    return inst->kind == SOLAR_EQUIPMENT_KIND_BATTERY &&
        str_equals_ignore_case(inst->catalog_series, "ANENJI-5.1kWh-Rack");
}

bool solar_equipment_is_battery_10kw_wall(const solar_equipment_instance_t *inst) {
    if (!inst) return false;
    return inst->kind == SOLAR_EQUIPMENT_KIND_BATTERY &&
        str_equals_ignore_case(inst->catalog_series, "ANENJI-10kW");
}

bool solar_equipment_is_battery_dual_terminal(const solar_equipment_instance_t *inst) {
    if (!inst) return false;
    return inst->kind == SOLAR_EQUIPMENT_KIND_BATTERY && !solar_equipment_is_battery_prismatic(inst);
}
