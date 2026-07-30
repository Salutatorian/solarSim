#include "solar_panel.h"

#include <cstddef>
#include <cstring>
#include <cmath>

static void copy_string(char *dest, size_t dest_size, const char *src) {
    if (!src || dest_size == 0) return;
    size_t len = std::strlen(src);
    if (len >= dest_size) len = dest_size - 1;
    std::memcpy(dest, src, len);
    dest[len] = '\0';
}

static int normalize_rotation(int degrees) {
    int normalized = degrees % 360;
    if (normalized < 0) normalized += 360;
    int snapped = (int)std::round(normalized / 90.0) * 90;
    snapped %= 360;
    if (snapped < 0) snapped += 360;
    return snapped;
}

bool solar_panel_guid_equals(const solar_guid_t *a, const solar_guid_t *b) {
    return a->id_high == b->id_high && a->id_low == b->id_low;
}

void solar_panel_guid_zero(solar_guid_t *guid) {
    guid->id_high = 0;
    guid->id_low = 0;
}

bool solar_panel_guid_is_zero(const solar_guid_t *guid) {
    return guid->id_high == 0 && guid->id_low == 0;
}

void solar_panel_guid_from_u64_pair(solar_guid_t *guid, uint64_t high, uint64_t low) {
    guid->id_high = high;
    guid->id_low = low;
}

bool solar_panel_definition_is_valid(const solar_panel_definition_t *def) {
    if (!def) return false;
    if (def->manufacturer[0] == '\0') return false;
    if (def->model[0] == '\0') return false;
    if (def->pmax_watts <= 0.0) return false;
    if (def->vmp_volts <= 0.0) return false;
    if (def->imp_amps <= 0.0) return false;
    if (def->voc_volts <= 0.0) return false;
    if (def->isc_amps <= 0.0) return false;
    if (def->width_mm <= 0.0) return false;
    if (def->height_mm <= 0.0) return false;
    if (def->depth_mm <= 0.0) return false;
    if (def->positive_lead_length_mm < 0.0) return false;
    if (def->negative_lead_length_mm < 0.0) return false;
    if (!std::isfinite(def->pmax_watts)) return false;
    return true;
}

static void set_default_port(
    solar_port_t *port,
    const solar_guid_t *owner_id,
    solar_port_type_t type,
    solar_polarity_t polarity,
    const char *connector_family,
    solar_connector_interface_t interface_type) {
    static uint64_t next_port_low = 0x1000;
    port->id.id_high = 0;
    port->id.id_low = next_port_low++;
    port->owner_id = *owner_id;
    port->type = type;
    port->polarity = polarity;
    copy_string(port->connector_family, SOLAR_CONNECTOR_FAMILY_LEN, connector_family);
    port->interface_type = interface_type;
    solar_panel_guid_zero(&port->connection_id);
    port->is_occupied = false;
}

void solar_panel_definition_boviet_270(solar_panel_definition_t *def) {
    if (!def) return;
    std::memset(def, 0, sizeof(*def));
    def->id.id_high = 0x11111111;
    def->id.id_low = 0x111111110001;
    copy_string(def->manufacturer, SOLAR_MANUFACTURER_LEN, "Boviet");
    copy_string(def->model, SOLAR_MODEL_LEN, "270 W");
    def->pmax_watts = 270.0;
    def->vmp_volts = 31.2;
    def->imp_amps = 8.65;
    def->voc_volts = 38.1;
    def->isc_amps = 9.20;
    def->width_mm = 992.0;
    def->height_mm = 1640.0;
    def->depth_mm = 35.0;
    def->temp_coeff_voc_pct_per_c = -0.28;
    def->temp_coeff_pmax_pct_per_c = -0.36;
    copy_string(def->connector_family, SOLAR_CONNECTOR_FAMILY_LEN, "MC4-compatible");
    def->positive_lead_length_mm = 1000.0;
    def->negative_lead_length_mm = 1000.0;
    def->is_custom = false;
}

void solar_panel_definition_generic_400(solar_panel_definition_t *def) {
    if (!def) return;
    std::memset(def, 0, sizeof(*def));
    def->id.id_high = 0x11111111;
    def->id.id_low = 0x111111110002;
    copy_string(def->manufacturer, SOLAR_MANUFACTURER_LEN, "Generic");
    copy_string(def->model, SOLAR_MODEL_LEN, "400 W");
    def->pmax_watts = 400.0;
    def->vmp_volts = 31.25;
    def->imp_amps = 12.80;
    def->voc_volts = 37.1;
    def->isc_amps = 13.50;
    def->width_mm = 1134.0;
    def->height_mm = 1722.0;
    def->depth_mm = 35.0;
    def->temp_coeff_voc_pct_per_c = -0.28;
    def->temp_coeff_pmax_pct_per_c = -0.35;
    copy_string(def->connector_family, SOLAR_CONNECTOR_FAMILY_LEN, "MC4-compatible");
    def->positive_lead_length_mm = 1000.0;
    def->negative_lead_length_mm = 1000.0;
    def->is_custom = false;
}

void solar_panel_definition_generic_550(solar_panel_definition_t *def) {
    if (!def) return;
    std::memset(def, 0, sizeof(*def));
    def->id.id_high = 0x11111111;
    def->id.id_low = 0x111111110003;
    copy_string(def->manufacturer, SOLAR_MANUFACTURER_LEN, "Generic");
    copy_string(def->model, SOLAR_MODEL_LEN, "550 W");
    def->pmax_watts = 550.0;
    def->vmp_volts = 41.5;
    def->imp_amps = 13.25;
    def->voc_volts = 49.8;
    def->isc_amps = 13.85;
    def->width_mm = 1134.0;
    def->height_mm = 2278.0;
    def->depth_mm = 35.0;
    def->temp_coeff_voc_pct_per_c = -0.27;
    def->temp_coeff_pmax_pct_per_c = -0.35;
    copy_string(def->connector_family, SOLAR_CONNECTOR_FAMILY_LEN, "MC4-compatible");
    def->positive_lead_length_mm = 1200.0;
    def->negative_lead_length_mm = 1200.0;
    def->is_custom = false;
}

void solar_panel_definition_generic_650(solar_panel_definition_t *def) {
    if (!def) return;
    std::memset(def, 0, sizeof(*def));
    def->id.id_high = 0x11111111;
    def->id.id_low = 0x111111110004;
    copy_string(def->manufacturer, SOLAR_MANUFACTURER_LEN, "Generic");
    copy_string(def->model, SOLAR_MODEL_LEN, "650 W");
    def->pmax_watts = 650.0;
    def->vmp_volts = 43.2;
    def->imp_amps = 15.05;
    def->voc_volts = 51.2;
    def->isc_amps = 15.70;
    def->width_mm = 1303.0;
    def->height_mm = 2278.0;
    def->depth_mm = 40.0;
    def->temp_coeff_voc_pct_per_c = -0.26;
    def->temp_coeff_pmax_pct_per_c = -0.34;
    copy_string(def->connector_family, SOLAR_CONNECTOR_FAMILY_LEN, "MC4-compatible");
    def->positive_lead_length_mm = 1400.0;
    def->negative_lead_length_mm = 1400.0;
    def->is_custom = false;
}

void solar_panel_instance_init(
    solar_panel_instance_t *inst,
    const solar_guid_t *id,
    const solar_guid_t *definition_id,
    double x_mm,
    double y_mm,
    int rotation_degrees) {
    if (!inst || !id || !definition_id) return;
    std::memset(inst, 0, sizeof(*inst));
    inst->id = *id;
    inst->definition_id = *definition_id;
    inst->position_x_mm = x_mm;
    inst->position_y_mm = y_mm;
    inst->rotation_degrees = normalize_rotation(rotation_degrees);
    inst->visual_mode = SOLAR_VISUAL_SIMPLE;
    inst->port_count = 2;
    set_default_port(
        &inst->ports[0],
        id,
        SOLAR_PORT_PV_POSITIVE,
        SOLAR_POLARITY_POSITIVE,
        "MC4-compatible",
        SOLAR_CONNECTOR_MALE);
    set_default_port(
        &inst->ports[1],
        id,
        SOLAR_PORT_PV_NEGATIVE,
        SOLAR_POLARITY_NEGATIVE,
        "MC4-compatible",
        SOLAR_CONNECTOR_FEMALE);
}

solar_port_t *solar_panel_find_port(solar_panel_instance_t *inst, const solar_guid_t *port_id) {
    if (!inst || !port_id) return NULL;
    for (size_t i = 0; i < inst->port_count; i++) {
        if (solar_panel_guid_equals(&inst->ports[i].id, port_id)) {
            return &inst->ports[i];
        }
    }
    return NULL;
}

const solar_port_t *solar_panel_find_port_const(const solar_panel_instance_t *inst, const solar_guid_t *port_id) {
    if (!inst || !port_id) return NULL;
    for (size_t i = 0; i < inst->port_count; i++) {
        if (solar_panel_guid_equals(&inst->ports[i].id, port_id)) {
            return &inst->ports[i];
        }
    }
    return NULL;
}

void solar_panel_set_position(solar_panel_instance_t *inst, double x_mm, double y_mm) {
    if (!inst) return;
    inst->position_x_mm = x_mm;
    inst->position_y_mm = y_mm;
}

void solar_panel_set_rotation(solar_panel_instance_t *inst, int degrees) {
    if (!inst) return;
    inst->rotation_degrees = normalize_rotation(degrees);
}
