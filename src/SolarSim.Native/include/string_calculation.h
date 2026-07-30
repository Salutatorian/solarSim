#ifndef STRING_CALCULATION_H
#define STRING_CALCULATION_H

#include <stdbool.h>
#include <stddef.h>

#include "electrical_graph.h"
#include "solar_panel.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Series string and project electrical calculations.
 * Mirrors SolarSim.Domain.Electrical.ElectricalCalculationService.
 */

#define SOLAR_MAX_DEFINITIONS 256
#define SOLAR_MAX_STRING_RESULTS 128
#define SOLAR_MAX_WARNINGS 32
#define SOLAR_MAX_ERRORS 16

typedef enum {
    SOLAR_SEVERITY_INFO = 0,
    SOLAR_SEVERITY_WARNING,
    SOLAR_SEVERITY_ERROR
} solar_issue_severity_t;

typedef struct {
    solar_issue_severity_t severity;
    char code[32];
    char message[128];
    char detail[256];
    solar_guid_t related_ids[8];
    size_t related_count;
} solar_issue_t;

typedef struct {
    solar_guid_t string_id;
    char display_name[32];
    size_t panel_count;
    double total_pmax_watts;
    double vmp_volts;
    double voc_volts;
    double imp_amps;
    double isc_amps;
    bool is_mixed_module_string;
    bool is_simplified;
    solar_issue_t warnings[SOLAR_MAX_WARNINGS];
    size_t warning_count;
    solar_issue_t errors[SOLAR_MAX_ERRORS];
    size_t error_count;
} solar_string_result_t;

typedef struct {
    size_t total_panels;
    size_t connected_panels;
    size_t unconnected_panels;
    size_t string_count;
    double total_pmax_watts;
    solar_string_result_t strings[SOLAR_MAX_STRING_RESULTS];
    size_t string_result_count;
    solar_issue_t warnings[SOLAR_MAX_WARNINGS];
    size_t warning_count;
    solar_issue_t errors[SOLAR_MAX_ERRORS];
    size_t error_count;
} solar_project_result_t;

typedef struct {
    solar_panel_definition_t definitions[SOLAR_MAX_DEFINITIONS];
    size_t count;
} solar_definition_catalog_t;

void solar_definition_catalog_init(solar_definition_catalog_t *catalog);
bool solar_definition_catalog_add(solar_definition_catalog_t *catalog, const solar_panel_definition_t *def);
const solar_panel_definition_t *solar_definition_catalog_find(
    const solar_definition_catalog_t *catalog,
    const solar_guid_t *id);

void solar_calculate_string(
    const solar_pv_string_t *pv_string,
    const solar_electrical_graph_t *graph,
    const solar_definition_catalog_t *catalog,
    solar_string_result_t *out_result);

void solar_calculate_project(
    const solar_electrical_graph_t *graph,
    const solar_definition_catalog_t *catalog,
    solar_project_result_t *out_result);

/* Temperature-adjusted values. */
double solar_cold_voc_volts(double voc_at_stc, double temp_coeff_voc_pct_per_c, double delta_c);
double solar_hot_vmp_volts(double vmp_at_stc, double temp_coeff_vmp_pct_per_c, double delta_c);

#ifdef __cplusplus
}
#endif

#endif
