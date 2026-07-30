#ifndef JSON_PROJECT_SERIALIZER_H
#define JSON_PROJECT_SERIALIZER_H

#include <stddef.h>
#include <stdbool.h>

#include "solar_project_state.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Parse a .solarproj JSON document into a native project state.
 * Returns true on success and writes an error message into error_buffer on failure.
 */
bool solar_project_json_parse(
    const char *json,
    size_t length,
    solar_project_state_t *state,
    char *error_buffer,
    size_t error_buffer_size);

/* Parse from a raw byte buffer (e.g., a libFuzzer input). */
bool solar_project_json_parse_bytes(
    const uint8_t *data,
    size_t size,
    solar_project_state_t *state,
    char *error_buffer,
    size_t error_buffer_size);

/* Serialize a project state into a .solarproj JSON document.
 * The output is written to output_buffer and the written length is returned in output_length.
 */
bool solar_project_json_serialize(
    const solar_project_state_t *state,
    char *output_buffer,
    size_t output_buffer_size,
    size_t *output_length);

#ifdef __cplusplus
}
#endif

#endif
