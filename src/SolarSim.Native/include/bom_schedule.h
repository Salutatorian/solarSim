#ifndef BOM_SCHEDULE_H
#define BOM_SCHEDULE_H

#include <stdbool.h>
#include <stddef.h>

#include "electrical_graph.h"
#include "solar_panel.h"
#include "string_calculation.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Bill of materials / wire schedule generation.
 * Mirrors SolarSim.Domain.Electrical.BomScheduleService.
 */

#define SOLAR_BOM_MAX_ITEMS 256
#define SOLAR_BOM_CATEGORY_LEN 16
#define SOLAR_BOM_DESCRIPTION_LEN 128
#define SOLAR_BOM_UNIT_LEN 8
#define SOLAR_BOM_NOTES_LEN 128
#define SOLAR_BOM_PLAIN_TEXT_LEN 8192

typedef struct {
    char category[SOLAR_BOM_CATEGORY_LEN];
    char description[SOLAR_BOM_DESCRIPTION_LEN];
    int quantity;
    char unit[SOLAR_BOM_UNIT_LEN];
    double total_length_mm;
    bool has_length;
    char notes[SOLAR_BOM_NOTES_LEN];
} solar_bom_line_item_t;

typedef struct {
    size_t rail_count;
    double total_rail_length_mm;
    size_t row_count;
    size_t attachment_count;
    size_t end_clamp_count;
    size_t mid_clamp_count;
} solar_racking_layout_t;

void solar_racking_layout_init(solar_racking_layout_t *layout);

typedef struct {
    solar_bom_line_item_t items[SOLAR_BOM_MAX_ITEMS];
    size_t item_count;
    int panel_count;
    double total_dc_watts;
    int wire_run_count;
    double total_wire_length_mm;
} solar_bom_report_t;

void solar_bom_report_init(solar_bom_report_t *report);

/* Build a BOM from the electrical graph, definition catalog, and optional racking layout. */
void solar_bom_schedule_build(
    const solar_electrical_graph_t *graph,
    const solar_definition_catalog_t *catalog,
    const solar_racking_layout_t *racking,
    solar_bom_report_t *out_report);

/* Render a BOM report to a plain-text summary. */
const char *solar_bom_report_to_plain_text(
    const solar_bom_report_t *report,
    char *buffer,
    size_t buffer_size);

/* Add a line item directly (used by tests / fuzzers). */
bool solar_bom_report_add_item(solar_bom_report_t *report, const solar_bom_line_item_t *item);

/* Categorize a graph connection by gauge and wire type. */
void solar_bom_connection_properties(
    const solar_connection_t *connection,
    const char **out_material,
    const char **out_type,
    const char **out_color);

/* Add an equipment line item to an existing report. */
void solar_bom_schedule_add_equipment(
    solar_bom_report_t *report,
    const char *name,
    const char *kind,
    int quantity,
    const char *notes);

/* Estimate extra connector pairs for occupied equipment ports. */
void solar_bom_schedule_estimate_connectors(
    solar_bom_report_t *report,
    size_t occupied_equipment_ports);

/* Merge line items with identical category/description/unit, summing quantities. */
void solar_bom_schedule_merge_duplicates(solar_bom_report_t *report);

/* Summarize total quantity and length for a category. */
void solar_bom_schedule_summary(
    const solar_bom_report_t *report,
    const char *category,
    int *out_quantity,
    double *out_length_mm);

/* Build a BOM that also includes an external equipment list. */
void solar_bom_schedule_build_with_equipment(
    const solar_electrical_graph_t *graph,
    const solar_definition_catalog_t *catalog,
    const solar_racking_layout_t *racking,
    const char * const *equipment_names,
    const char * const *equipment_kinds,
    size_t equipment_count,
    solar_bom_report_t *out_report);

#ifdef __cplusplus
}
#endif

#endif
