#ifndef DESIGN_REPORT_H
#define DESIGN_REPORT_H

#include <stddef.h>
#include "solar_project_state.h"

#ifdef __cplusplus
extern "C" {
#endif

#define DESIGN_REPORT_NAME_LEN 128
#define DESIGN_REPORT_LOCATION_LEN 128
#define DESIGN_REPORT_STRING_NAME_LEN 64
#define DESIGN_REPORT_MAX_MODULES 512
#define DESIGN_REPORT_MAX_MONTHS 12
#define DESIGN_REPORT_MAX_WARNINGS 64
#define DESIGN_REPORT_METHOD_NOTE_LEN 256

typedef struct {
    int index;
    char name[DESIGN_REPORT_NAME_LEN];
    double x_mm;
    double y_mm;
    double width_mm;
    double height_mm;
    int rotation_degrees;
    char string_name[DESIGN_REPORT_STRING_NAME_LEN];
} design_report_module_t;

typedef struct {
    int month;
    char month_name[DESIGN_REPORT_NAME_LEN];
    double peak_sun_hours_per_day;
    double estimated_kwh;
} design_report_monthly_row_t;

typedef struct {
    int row_count;
    int rail_count;
    double total_rail_length_mm;
    int attachment_count;
    int end_clamp_count;
    int mid_clamp_count;
} design_report_racking_t;

typedef struct {
    char project_name[DESIGN_REPORT_NAME_LEN];
    char generated_utc[32];
    char single_line_text[2048];
    char string_results_text[2048];
    char bom_text[4096];
    design_report_module_t modules[DESIGN_REPORT_MAX_MODULES];
    size_t module_count;
    size_t panel_count;
    double total_dc_watts;
    size_t string_count;
    double min_ambient_celsius;
    double hot_cell_celsius;
    char location_name[DESIGN_REPORT_LOCATION_LEN];
    double latitude_degrees;
    double longitude_degrees;
    bool has_latitude;
    bool has_longitude;
    double peak_sun_hours_per_day;
    double system_derate_factor;
    double array_tilt_degrees;
    double array_azimuth_degrees;
    double estimated_annual_kwh;
    double estimated_daily_kwh;
    design_report_monthly_row_t monthly_production[DESIGN_REPORT_MAX_MONTHS];
    size_t monthly_production_count;
    char method_note[DESIGN_REPORT_METHOD_NOTE_LEN];
    bool has_racking;
    design_report_racking_t racking;
    char warnings[DESIGN_REPORT_MAX_WARNINGS][256];
    size_t warning_count;
} design_report_t;

void design_report_init(design_report_t *report);

bool design_report_build(const solar_project_state_t *state, design_report_t *out);

bool design_report_to_html(
    const design_report_t *report,
    char *out_buffer,
    size_t out_buffer_size,
    size_t *out_written);

/* Render a plain-text version of the report (summary + module schedule + BOM). */
bool design_report_to_plain_text(
    const design_report_t *report,
    char *out_buffer,
    size_t out_buffer_size,
    size_t *out_written);

/* Format a compact per-string summary into the provided buffer. */
void design_report_string_results_to_text(
    const design_report_t *report,
    char *out_buffer,
    size_t out_buffer_size);

#ifdef __cplusplus
}
#endif

#endif
