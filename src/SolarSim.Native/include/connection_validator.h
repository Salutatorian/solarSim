#ifndef CONNECTION_VALIDATOR_H
#define CONNECTION_VALIDATOR_H

#include <stdbool.h>
#include <stddef.h>

#include "electrical_graph.h"
#include "mppt_compatibility.h"
#include "solar_panel.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Comprehensive DC connection validation.
 * Extends the basic graph validation in electrical_graph.h with polarity rules,
 * connector family checks, gender compatibility, port occupancy, and battery/
 * disconnect topology rules ported from ConnectionValidator.cs.
 */

#define SOLAR_CONN_VALIDATION_MAX_ISSUES 16
#define SOLAR_CONN_VALIDATION_CODE_LEN 32
#define SOLAR_CONN_VALIDATION_MSG_LEN 256
#define SOLAR_CONN_VALIDATION_MAX_AFFECTED 4

typedef enum {
    SOLAR_CONN_VALIDATION_SEVERITY_INFO = 0,
    SOLAR_CONN_VALIDATION_SEVERITY_WARNING,
    SOLAR_CONN_VALIDATION_SEVERITY_ERROR
} solar_conn_validation_severity_t;

typedef struct {
    solar_conn_validation_severity_t severity;
    char code[SOLAR_CONN_VALIDATION_CODE_LEN];
    char message[SOLAR_CONN_VALIDATION_MSG_LEN];
    char detail[SOLAR_CONN_VALIDATION_MSG_LEN];
    solar_guid_t affected_ids[SOLAR_CONN_VALIDATION_MAX_AFFECTED];
    size_t affected_count;
} solar_conn_validation_issue_t;

typedef struct {
    solar_conn_validation_issue_t errors[SOLAR_CONN_VALIDATION_MAX_ISSUES];
    size_t error_count;
    solar_conn_validation_issue_t warnings[SOLAR_CONN_VALIDATION_MAX_ISSUES];
    size_t warning_count;
    solar_conn_validation_issue_t info[SOLAR_CONN_VALIDATION_MAX_ISSUES];
    size_t info_count;
    bool is_valid;
} solar_connection_validation_result_t;

void solar_connection_validation_result_init(solar_connection_validation_result_t *result);

/* Main validation entry point. Both owner pointers may be NULL for one or both sides;
 * when an owner is known it is used for equipment-kind-specific rules. */
void solar_connection_validator_validate(
    const solar_port_t *start,
    const solar_port_t *end,
    const solar_equipment_instance_t *start_owner,
    const solar_equipment_instance_t *end_owner,
    solar_connection_validation_result_t *out_result);

/* Back-compat alias for series-only validation. */
void solar_connection_validator_validate_series(
    const solar_port_t *start,
    const solar_port_t *end,
    const solar_equipment_instance_t *start_owner,
    const solar_equipment_instance_t *end_owner,
    solar_connection_validation_result_t *out_result);

/* Helpers for specific rule families. */
void solar_connection_validator_validate_battery_rules(
    const solar_port_t *start,
    const solar_port_t *end,
    const solar_equipment_instance_t *start_owner,
    const solar_equipment_instance_t *end_owner,
    solar_connection_validation_result_t *result);

void solar_connection_validator_validate_disconnect_to_inverter(
    const solar_port_t *start,
    const solar_port_t *end,
    const solar_equipment_instance_t *start_owner,
    const solar_equipment_instance_t *end_owner,
    solar_connection_validation_result_t *result);

void solar_connection_validator_validate_connector_compatibility(
    const solar_port_t *start,
    const solar_port_t *end,
    solar_connection_validation_result_t *result);

#ifdef __cplusplus
}
#endif

#endif
