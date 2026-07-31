#ifndef MC4_CONNECTOR_H
#define MC4_CONNECTOR_H

#include <stdbool.h>
#include <stddef.h>

#include "solar_panel.h"
#include "wire_route.h"

#ifdef __cplusplus
extern "C" {
#endif

/* MC4-style connector compatibility and assembly rules.
 * Mirrors the mechanical connector logic in the C# electrical domain.
 */

typedef enum {
    SOLAR_CONNECTOR_FAMILY_MC4 = 0,
    SOLAR_CONNECTOR_FAMILY_TYCO,
    SOLAR_CONNECTOR_FAMILY_AIMETT,
    SOLAR_CONNECTOR_FAMILY_OTHER,
    SOLAR_CONNECTOR_FAMILY_COUNT
} solar_connector_family_id_t;

const char *solar_connector_family_name(solar_connector_family_id_t family);
bool solar_connector_family_is_compatible(solar_connector_family_id_t a, solar_connector_family_id_t b);
bool solar_connector_can_mate(
    solar_connector_interface_t a_interface,
    solar_polarity_t a_polarity,
    solar_connector_interface_t b_interface,
    solar_polarity_t b_polarity);

/* Validate a string of connectors in series. */
#define MC4_MAX_SERIES_CONNECTORS 256

typedef struct {
    solar_connector_interface_t interface_type;
    solar_polarity_t polarity;
    solar_connector_family_id_t family;
} mc4_connector_t;

typedef struct {
    bool is_valid;
    char error_code[32];
    char error_message[128];
    size_t error_index;
} mc4_series_result_t;

void mc4_validate_series(
    const mc4_connector_t *connectors,
    size_t count,
    mc4_series_result_t *out_result);

/* Crimp and assembly checks (design aid). */
bool mc4_check_wire_fit(
    solar_connector_family_id_t family,
    wire_awg_t gauge,
    char *out_error,
    size_t error_size);

#ifdef __cplusplus
}
#endif

#endif
