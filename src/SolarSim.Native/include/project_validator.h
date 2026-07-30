#ifndef PROJECT_VALIDATOR_H
#define PROJECT_VALIDATOR_H

#include <stdbool.h>
#include <stddef.h>

#include "solar_project_state.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Design-rule validation for a solar project state.
 * Checks electrical, geometric, and site assumptions for common issues.
 */

#define PROJECT_VALIDATOR_MAX_ISSUES 64
#define PROJECT_VALIDATOR_MSG_LEN 256
#define PROJECT_VALIDATOR_CODE_LEN 32

typedef enum {
    PROJECT_VALIDATOR_INFO = 0,
    PROJECT_VALIDATOR_WARNING,
    PROJECT_VALIDATOR_ERROR
} project_validator_severity_t;

typedef struct {
    project_validator_severity_t severity;
    char code[PROJECT_VALIDATOR_CODE_LEN];
    char message[PROJECT_VALIDATOR_MSG_LEN];
} project_validator_issue_t;

typedef struct {
    project_validator_issue_t issues[PROJECT_VALIDATOR_MAX_ISSUES];
    size_t issue_count;
    size_t error_count;
    size_t warning_count;
    size_t info_count;
    bool is_valid;
} project_validator_result_t;

void project_validator_validate(
    const solar_project_state_t *state,
    project_validator_result_t *out_result);

/* Individual rule checks exposed for fuzzing and reuse. */
bool project_validator_check_inverter_dc_voltage(
    const solar_project_state_t *state,
    project_validator_result_t *out_result);

bool project_validator_check_mppt_window(
    const solar_project_state_t *state,
    project_validator_result_t *out_result);

bool project_validator_check_panel_containment(
    const solar_project_state_t *state,
    project_validator_result_t *out_result);

bool project_validator_check_obstacle_overlap(
    const solar_project_state_t *state,
    project_validator_result_t *out_result);

bool project_validator_check_unconnected_panels(
    const solar_project_state_t *state,
    project_validator_result_t *out_result);

bool project_validator_check_string_current(
    const solar_project_state_t *state,
    project_validator_result_t *out_result);

bool project_validator_check_site_temperatures(
    const solar_project_state_t *state,
    project_validator_result_t *out_result);

void project_validator_add_issue(
    project_validator_result_t *result,
    project_validator_severity_t severity,
    const char *code,
    const char *message);

#ifdef __cplusplus
}
#endif

#endif
