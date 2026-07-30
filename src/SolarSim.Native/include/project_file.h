#ifndef PROJECT_FILE_H
#define PROJECT_FILE_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "electrical_graph.h"
#include "roof_geometry.h"
#include "string_calculation.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Binary solar project file format and loader.
 * This is a native, compact counterpart to the .solarproj JSON file used by
 * the C# application. It is not wire-compatible with JSON; it is a separate
 * export/import path intended for high-performance native interop.
 */

#define PROJECT_MAGIC "SOLP"
#define PROJECT_MAX_SITE_NAME 128
#define PROJECT_MAX_LOCATION 128
#define PROJECT_MAX_CLIMATE 64
#define SOLAR_MAX_NAME_LEN 128

typedef struct {
    char magic[4];
    uint32_t version;
    uint32_t flags;
    uint32_t module_count;
    uint32_t connection_count;
    uint32_t surface_count;
    uint32_t obstacle_count;
    uint32_t tag_count;
    double site_latitude;
    double site_longitude;
    double cold_voc_temp_c;
    double hot_cell_temp_c;
    double peak_sun_hours;
    double system_derate;
    double site_tilt_degrees;
    double site_azimuth_degrees;
    char site_name[PROJECT_MAX_SITE_NAME];
    char location[PROJECT_MAX_LOCATION];
    char climate_preset[PROJECT_MAX_CLIMATE];
} solar_project_header_t;

/* A project file may contain a catalog of definitions, a graph, and roof data. */
typedef struct {
    solar_project_header_t header;
    solar_panel_definition_t definitions[256];
    size_t definition_count;
    solar_electrical_graph_t graph;
    roof_document_t roof;
} solar_project_t;

/* High-level API. */
bool solar_project_file_parse(const uint8_t *data, size_t size, solar_project_t *out_project);
bool solar_project_file_validate(const solar_project_t *project);

/* Lower-level helpers exposed for fuzzing and reuse. */
bool solar_project_parse_header(const uint8_t *data, size_t size, size_t *out_header_bytes, solar_project_header_t *out_header);
bool solar_project_parse_definition(const uint8_t **data, const uint8_t *end, solar_panel_definition_t *out_def);
bool solar_project_parse_graph(const uint8_t **data, const uint8_t *end, solar_electrical_graph_t *out_graph);
bool solar_project_parse_roof(const uint8_t **data, const uint8_t *end, roof_document_t *out_roof);

void solar_project_init(solar_project_t *project);

#ifdef __cplusplus
}
#endif

#endif
