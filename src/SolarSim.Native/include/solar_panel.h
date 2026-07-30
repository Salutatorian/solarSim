#ifndef SOLAR_PANEL_H
#define SOLAR_PANEL_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Solar panel definition and instance types ported from the SolarSim domain.
 * A SolarPanelDefinition is a catalog entry shared by many instances.
 * A SolarPanelInstance is a placed copy with its own electrical ports.
 */

#define SOLAR_PORT_NAME_LEN 32
#define SOLAR_CONNECTOR_FAMILY_LEN 32
#define SOLAR_MANUFACTURER_LEN 64
#define SOLAR_MODEL_LEN 64
#define SOLAR_MAX_PORTS_PER_PANEL 8

typedef enum {
    SOLAR_PORT_PV_POSITIVE = 0,
    SOLAR_PORT_PV_NEGATIVE,
    SOLAR_PORT_STRING_INPUT_POSITIVE,
    SOLAR_PORT_STRING_INPUT_NEGATIVE,
    SOLAR_PORT_OUTPUT_POSITIVE,
    SOLAR_PORT_OUTPUT_NEGATIVE,
    SOLAR_PORT_MPPT_INPUT_POSITIVE,
    SOLAR_PORT_MPPT_INPUT_NEGATIVE
} solar_port_type_t;

typedef enum {
    SOLAR_POLARITY_POSITIVE = 0,
    SOLAR_POLARITY_NEGATIVE
} solar_polarity_t;

typedef enum {
    SOLAR_CONNECTOR_MALE = 0,
    SOLAR_CONNECTOR_FEMALE,
    SOLAR_CONNECTOR_UNSPECIFIED
} solar_connector_interface_t;

typedef struct {
    uint64_t id_high;
    uint64_t id_low;
} solar_guid_t;

typedef struct {
    solar_guid_t id;
    solar_guid_t owner_id;
    solar_port_type_t type;
    solar_polarity_t polarity;
    char connector_family[SOLAR_CONNECTOR_FAMILY_LEN];
    solar_connector_interface_t interface_type;
    solar_guid_t connection_id; /* zero guid means unconnected */
    bool is_occupied;
} solar_port_t;

typedef struct {
    solar_guid_t id;
    char manufacturer[SOLAR_MANUFACTURER_LEN];
    char model[SOLAR_MODEL_LEN];
    double pmax_watts;
    double vmp_volts;
    double imp_amps;
    double voc_volts;
    double isc_amps;
    double width_mm;
    double height_mm;
    double depth_mm;
    double temp_coeff_voc_pct_per_c;
    double temp_coeff_pmax_pct_per_c;
    char connector_family[SOLAR_CONNECTOR_FAMILY_LEN];
    double positive_lead_length_mm;
    double negative_lead_length_mm;
    bool is_custom;
} solar_panel_definition_t;

typedef enum {
    SOLAR_VISUAL_SIMPLE = 0,
    SOLAR_VISUAL_BLUEPRINT,
    SOLAR_VISUAL_PRODUCT_IMAGE
} solar_visual_mode_t;

typedef struct {
    solar_guid_t id;
    solar_guid_t definition_id;
    double position_x_mm;
    double position_y_mm;
    int rotation_degrees;
    solar_visual_mode_t visual_mode;
    solar_port_t ports[SOLAR_MAX_PORTS_PER_PANEL];
    size_t port_count;
} solar_panel_instance_t;

/* Built-in panel definitions. */
void solar_panel_definition_boviet_270(solar_panel_definition_t *def);
void solar_panel_definition_generic_400(solar_panel_definition_t *def);
void solar_panel_definition_generic_550(solar_panel_definition_t *def);
void solar_panel_definition_generic_650(solar_panel_definition_t *def);

/* Validation and helpers. */
bool solar_panel_definition_is_valid(const solar_panel_definition_t *def);
bool solar_panel_guid_equals(const solar_guid_t *a, const solar_guid_t *b);
void solar_panel_guid_zero(solar_guid_t *guid);
bool solar_panel_guid_is_zero(const solar_guid_t *guid);
void solar_panel_guid_from_u64_pair(solar_guid_t *guid, uint64_t high, uint64_t low);

/* Instance creation. */
void solar_panel_instance_init(
    solar_panel_instance_t *inst,
    const solar_guid_t *id,
    const solar_guid_t *definition_id,
    double x_mm,
    double y_mm,
    int rotation_degrees);

solar_port_t *solar_panel_find_port(solar_panel_instance_t *inst, const solar_guid_t *port_id);
const solar_port_t *solar_panel_find_port_const(const solar_panel_instance_t *inst, const solar_guid_t *port_id);
void solar_panel_set_position(solar_panel_instance_t *inst, double x_mm, double y_mm);
void solar_panel_set_rotation(solar_panel_instance_t *inst, int degrees);

#ifdef __cplusplus
}
#endif

#endif
