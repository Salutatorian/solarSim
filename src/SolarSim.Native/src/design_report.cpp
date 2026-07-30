#include "design_report.h"

#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <cmath>

static void copy_string(char *dest, size_t dest_size, const char *src) {
    if (!dest || dest_size == 0) return;
    if (!src) {
        dest[0] = '\0';
        return;
    }
    size_t len = std::strlen(src);
    if (len >= dest_size) len = dest_size - 1;
    std::memcpy(dest, src, len);
    dest[len] = '\0';
}

static void appendf(char *buffer, size_t buffer_size, size_t *cursor, const char *fmt, ...) {
    if (!buffer || !cursor) return;
    std::va_list args;
    va_start(args, fmt);
    int n = std::vsnprintf(buffer + *cursor, buffer_size - *cursor, fmt, args);
    va_end(args);
    if (n > 0) {
        *cursor += (size_t)n;
        if (*cursor > buffer_size) *cursor = buffer_size;
    }
}

static void html_escape(const char *input, char *out_buffer, size_t buffer_size) {
    if (!input || !out_buffer || buffer_size == 0) return;
    size_t out = 0;
    for (size_t i = 0; input[i] != '\0' && out < buffer_size - 1; i++) {
        char c = input[i];
        const char *replacement = NULL;
        switch (c) {
            case '&': replacement = "&amp;"; break;
            case '<': replacement = "&lt;"; break;
            case '>': replacement = "&gt;"; break;
            case '"': replacement = "&quot;"; break;
            case '\'': replacement = "&#39;"; break;
        }
        if (replacement) {
            size_t rlen = std::strlen(replacement);
            if (out + rlen >= buffer_size) break;
            std::memcpy(out_buffer + out, replacement, rlen);
            out += rlen;
        } else {
            out_buffer[out++] = c;
        }
    }
    out_buffer[out] = '\0';
}

static void append_escaped(char *buffer, size_t buffer_size, size_t *cursor, const char *s) {
    char escaped[512];
    html_escape(s, escaped, sizeof(escaped));
    appendf(buffer, buffer_size, cursor, "%s", escaped);
}

void design_report_init(design_report_t *report) {
    if (!report) return;
    std::memset(report, 0, sizeof(*report));
}

bool design_report_build(const solar_project_state_t *state, design_report_t *out) {
    if (!state || !out) return false;
    design_report_init(out);

    copy_string(out->project_name, sizeof(out->project_name), state->name);
    copy_string(out->location_name, sizeof(out->location_name), state->site.location_name);
    out->latitude_degrees = state->site.latitude_deg;
    out->longitude_degrees = state->site.longitude_deg;
    out->has_latitude = true;
    out->has_longitude = true;
    out->min_ambient_celsius = state->site.min_ambient_c;
    out->hot_cell_celsius = state->site.hot_cell_c;
    out->peak_sun_hours_per_day = state->site.peak_sun_hours;
    out->system_derate_factor = state->site.system_derate;
    out->array_tilt_degrees = state->site.array_tilt_deg;
    out->array_azimuth_degrees = state->site.array_azimuth_deg;

    solar_project_result_t calc = solar_project_state_calculate(state);
    out->panel_count = (size_t)calc.total_panels;
    out->string_count = (size_t)calc.string_count;
    out->total_dc_watts = calc.total_pmax_watts;

    for (size_t i = 0; i < calc.string_result_count && i < SOLAR_MAX_STRING_RESULTS; i++) {
        const solar_string_result_t *str = &calc.strings[i];
        for (size_t j = 0; j < str->panel_count && out->module_count < DESIGN_REPORT_MAX_MODULES; j++) {
            design_report_module_t *mod = &out->modules[out->module_count];
            mod->index = (int)out->module_count + 1;
            std::snprintf(mod->name, sizeof(mod->name), "Module %d", mod->index);
            std::snprintf(mod->string_name, sizeof(mod->string_name), "%s", str->display_name);
            mod->x_mm = 0.0;
            mod->y_mm = 0.0;
            mod->width_mm = 1000.0;
            mod->height_mm = 1700.0;
            mod->rotation_degrees = 0;
            out->module_count++;
        }
    }

    out->has_racking = state->racking_layout.valid;
    out->racking.row_count = state->racking_layout.row_count;
    out->racking.rail_count = state->racking_layout.rail_count;
    out->racking.total_rail_length_mm = state->racking_layout.total_rail_length_mm;
    out->racking.attachment_count = state->racking_layout.attachment_count;
    out->racking.end_clamp_count = state->racking_layout.end_clamp_count;
    out->racking.mid_clamp_count = state->racking_layout.mid_clamp_count;

    std::snprintf(out->single_line_text, sizeof(out->single_line_text),
        "%zu modules | %zu strings | %.0f W DC | Cold %.1f C | Hot %.1f C",
        out->panel_count, out->string_count, out->total_dc_watts,
        out->min_ambient_celsius, out->hot_cell_celsius);

    std::snprintf(out->method_note, sizeof(out->method_note),
        "Rough estimate: STC kW * PSH * derate * 365. Not a bankable yield.");

    out->estimated_daily_kwh = out->total_dc_watts / 1000.0 * out->peak_sun_hours_per_day * out->system_derate_factor;
    out->estimated_annual_kwh = out->estimated_daily_kwh * 365.0;

    for (int m = 0; m < 12; m++) {
        out->monthly_production[m].month = m + 1;
        std::snprintf(out->monthly_production[m].month_name, sizeof(out->monthly_production[m].month_name), "%d", m + 1);
        out->monthly_production[m].peak_sun_hours_per_day = out->peak_sun_hours_per_day;
        out->monthly_production[m].estimated_kwh = out->estimated_annual_kwh / 12.0;
    }
    out->monthly_production_count = 12;

    std::snprintf(out->bom_text, sizeof(out->bom_text),
        "Solar modules: %zu ea\nRails, clamps, and attachments per racking estimate.",
        out->panel_count);

    for (size_t i = 0; i < calc.warning_count && out->warning_count < DESIGN_REPORT_MAX_WARNINGS; i++) {
        copy_string(out->warnings[out->warning_count++], 256, calc.warnings[i].message);
    }
    return true;
}

static bool build_array_svg(const design_report_t *report, char *out_buffer, size_t buffer_size, size_t *out_length) {
    if (!report || !out_buffer || !out_length) return false;
    *out_length = 0;
    if (buffer_size == 0) return false;

    if (report->module_count == 0) {
        std::snprintf(out_buffer, buffer_size,
            "<p style=\"color:#5c5c5c\">Place modules to generate an array layout sheet.</p>");
        *out_length = std::strlen(out_buffer);
        return true;
    }

    double min_x = report->modules[0].x_mm;
    double min_y = report->modules[0].y_mm;
    double max_x = report->modules[0].x_mm + report->modules[0].width_mm;
    double max_y = report->modules[0].y_mm + report->modules[0].height_mm;
    for (size_t i = 1; i < report->module_count; i++) {
        const design_report_module_t *m = &report->modules[i];
        if (m->x_mm < min_x) min_x = m->x_mm;
        if (m->y_mm < min_y) min_y = m->y_mm;
        if (m->x_mm + m->width_mm > max_x) max_x = m->x_mm + m->width_mm;
        if (m->y_mm + m->height_mm > max_y) max_y = m->y_mm + m->height_mm;
    }
    double width = max_x - min_x;
    double height = max_y - min_y;
    if (width < 1.0) width = 1.0;
    if (height < 1.0) height = 1.0;
    const double pad = 200.0;
    double vb_w = width + pad * 2.0;
    double vb_h = height + pad * 2.0;
    double svg_w = 720.0;
    double svg_h = svg_w * vb_h / vb_w;
    if (svg_h < 220.0) svg_h = 220.0;
    if (svg_h > 900.0) svg_h = 900.0;

    size_t cursor = 0;
    appendf(out_buffer, buffer_size, &cursor,
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"%.0f\" height=\"%.0f\" viewBox=\"0 0 %.0f %.0f\">\n",
        svg_w, svg_h, vb_w, vb_h);
    appendf(out_buffer, buffer_size, &cursor,
        "<rect x=\"0\" y=\"0\" width=\"%.0f\" height=\"%.0f\" fill=\"#fafaf8\"/>\n",
        vb_w, vb_h);

    for (size_t i = 0; i < report->module_count; i++) {
        const design_report_module_t *m = &report->modules[i];
        double x = m->x_mm - min_x + pad;
        double y = m->y_mm - min_y + pad;
        appendf(out_buffer, buffer_size, &cursor,
            "<rect x=\"%.1f\" y=\"%.1f\" width=\"%.1f\" height=\"%.1f\" fill=\"#2a3a52\" stroke=\"#1a2433\" stroke-width=\"20\" rx=\"30\"/>\n",
            x, y, m->width_mm, m->height_mm);
        double font = std::min(m->width_mm, m->height_mm) * 0.18;
        if (font < 80.0) font = 80.0;
        if (font > 220.0) font = 220.0;
        appendf(out_buffer, buffer_size, &cursor,
            "<text x=\"%.1f\" y=\"%.1f\" text-anchor=\"middle\" dominant-baseline=\"middle\" fill=\"#ffffff\" font-size=\"%.0f\" font-family=\"Segoe UI, sans-serif\">%d</text>\n",
            x + m->width_mm / 2.0, y + m->height_mm / 2.0, font, m->index);
    }
    appendf(out_buffer, buffer_size, &cursor, "</svg>\n");
    appendf(out_buffer, buffer_size, &cursor,
        "<p style=\"font-size:11px;color:#5c5c5c;margin:6px 0 0\">Plan view &middot; numbers match module schedule &middot; not to survey grade</p>");

    *out_length = cursor;
    return true;
}

bool design_report_to_html(
    const design_report_t *report,
    char *out_buffer,
    size_t out_buffer_size,
    size_t *out_written) {
    if (!report || !out_buffer) return false;
    if (out_written) *out_written = 0;
    if (out_buffer_size == 0) return false;

    size_t cursor = 0;
    appendf(out_buffer, out_buffer_size, &cursor, "<!DOCTYPE html>\n<html lang=\"en\"><head><meta charset=\"utf-8\"/>\n");
    appendf(out_buffer, out_buffer_size, &cursor, "<title>");
    append_escaped(out_buffer, out_buffer_size, &cursor, report->project_name);
    appendf(out_buffer, out_buffer_size, &cursor, " — solarSim Report</title>\n");
    appendf(out_buffer, out_buffer_size, &cursor, "<style>\n");
    appendf(out_buffer, out_buffer_size, &cursor,
        "body { font-family: \"Segoe UI\", system-ui, sans-serif; margin: 24px; color: #1f1f1f; }\n");
    appendf(out_buffer, out_buffer_size, &cursor, "h1 { font-size: 22px; margin: 0 0 4px; }\n");
    appendf(out_buffer, out_buffer_size, &cursor, "h2 { font-size: 15px; margin: 28px 0 8px; border-bottom: 1px solid #ddd; padding-bottom: 4px; }\n");
    appendf(out_buffer, out_buffer_size, &cursor,
        ".badge { display: inline-block; background: #eef3ff; color: #2f6fed; padding: 2px 8px; border-radius: 4px; font-size: 12px; margin-right: 6px; }\n");
    appendf(out_buffer, out_buffer_size, &cursor,
        "table { border-collapse: collapse; width: 100%%; font-size: 12px; }\n");
    appendf(out_buffer, out_buffer_size, &cursor,
        "th, td { border: 1px solid #e2e2de; padding: 6px 8px; text-align: left; }\n");
    appendf(out_buffer, out_buffer_size, &cursor, "th { background: #fafaf8; }\n");
    appendf(out_buffer, out_buffer_size, &cursor, ".layout { border: 1px solid #e2e2de; background: #fff; padding: 8px; }\n");
    appendf(out_buffer, out_buffer_size, &cursor, ".disclaimer { color: #b54708; font-size: 12px; margin-top: 28px; }\n");
    appendf(out_buffer, out_buffer_size, &cursor, "</style></head><body>\n");

    appendf(out_buffer, out_buffer_size, &cursor, "<h1>");
    append_escaped(out_buffer, out_buffer_size, &cursor, report->project_name);
    appendf(out_buffer, out_buffer_size, &cursor, "</h1>\n");
    appendf(out_buffer, out_buffer_size, &cursor, "<div class=\"meta\">solarSim design report</div>\n");
    appendf(out_buffer, out_buffer_size, &cursor,
        "<p><span class=\"badge\">%zu modules</span><span class=\"badge\">%.0f W DC</span><span class=\"badge\">%zu strings</span></p>\n",
        report->panel_count, report->total_dc_watts, report->string_count);

    appendf(out_buffer, out_buffer_size, &cursor, "<h2>1. Site assumptions</h2>\n");
    appendf(out_buffer, out_buffer_size, &cursor, "<pre>\n");
    appendf(out_buffer, out_buffer_size, &cursor, "Location: ");
    append_escaped(out_buffer, out_buffer_size, &cursor, report->location_name);
    appendf(out_buffer, out_buffer_size, &cursor, "\n");
    appendf(out_buffer, out_buffer_size, &cursor, "Lat/Lon: %.3f, %.3f\n", report->latitude_degrees, report->longitude_degrees);
    appendf(out_buffer, out_buffer_size, &cursor, "Cold Voc ambient: %.1f C\n", report->min_ambient_celsius);
    appendf(out_buffer, out_buffer_size, &cursor, "Hot cell: %.1f C\n", report->hot_cell_celsius);
    appendf(out_buffer, out_buffer_size, &cursor, "Peak sun hours: %.1f h/day\n", report->peak_sun_hours_per_day);
    appendf(out_buffer, out_buffer_size, &cursor, "Est. energy: %.1f kWh/day / %.0f kWh/year\n", report->estimated_daily_kwh, report->estimated_annual_kwh);
    appendf(out_buffer, out_buffer_size, &cursor, "%s\n", report->method_note);
    appendf(out_buffer, out_buffer_size, &cursor, "</pre>\n");

    appendf(out_buffer, out_buffer_size, &cursor, "<h2>2. Monthly production (est.)</h2>\n");
    appendf(out_buffer, out_buffer_size, &cursor, "<table><thead><tr>");
    for (size_t m = 0; m < report->monthly_production_count && m < DESIGN_REPORT_MAX_MONTHS; m++) {
        appendf(out_buffer, out_buffer_size, &cursor, "<th>%s</th>", report->monthly_production[m].month_name);
    }
    appendf(out_buffer, out_buffer_size, &cursor, "</tr></thead><tbody><tr>");
    for (size_t m = 0; m < report->monthly_production_count && m < DESIGN_REPORT_MAX_MONTHS; m++) {
        appendf(out_buffer, out_buffer_size, &cursor, "<td>%.0f</td>", report->monthly_production[m].estimated_kwh);
    }
    appendf(out_buffer, out_buffer_size, &cursor, "</tr></tbody></table>\n");

    appendf(out_buffer, out_buffer_size, &cursor, "<h2>3. Single-line summary</h2>\n");
    appendf(out_buffer, out_buffer_size, &cursor, "<pre>");
    append_escaped(out_buffer, out_buffer_size, &cursor, report->single_line_text);
    appendf(out_buffer, out_buffer_size, &cursor, "</pre>\n");

    appendf(out_buffer, out_buffer_size, &cursor, "<h2>4. Array layout</h2>\n");
    appendf(out_buffer, out_buffer_size, &cursor, "<div class=\"layout\">\n");
    char svg[8192];
    size_t svg_len = 0;
    build_array_svg(report, svg, sizeof(svg), &svg_len);
    appendf(out_buffer, out_buffer_size, &cursor, "%s", svg);
    appendf(out_buffer, out_buffer_size, &cursor, "</div>\n");

    appendf(out_buffer, out_buffer_size, &cursor, "<h2>5. Module schedule</h2>\n");
    appendf(out_buffer, out_buffer_size, &cursor, "<table><thead><tr><th>#</th><th>Module</th><th>String</th></tr></thead><tbody>\n");
    for (size_t i = 0; i < report->module_count; i++) {
        appendf(out_buffer, out_buffer_size, &cursor, "<tr><td>%d</td><td>", report->modules[i].index);
        append_escaped(out_buffer, out_buffer_size, &cursor, report->modules[i].name);
        appendf(out_buffer, out_buffer_size, &cursor, "</td><td>");
        append_escaped(out_buffer, out_buffer_size, &cursor, report->modules[i].string_name);
        appendf(out_buffer, out_buffer_size, &cursor, "</td></tr>\n");
    }
    appendf(out_buffer, out_buffer_size, &cursor, "</tbody></table>\n");

    if (report->has_racking) {
        appendf(out_buffer, out_buffer_size, &cursor, "<h2>6. Racking estimate</h2>\n");
        appendf(out_buffer, out_buffer_size, &cursor, "<pre>\n");
        appendf(out_buffer, out_buffer_size, &cursor, "Rows: %d\n", report->racking.row_count);
        appendf(out_buffer, out_buffer_size, &cursor, "Rails: %d, total %.1f m\n", report->racking.rail_count, report->racking.total_rail_length_mm / 1000.0);
        appendf(out_buffer, out_buffer_size, &cursor, "Attachments: %d\n", report->racking.attachment_count);
        appendf(out_buffer, out_buffer_size, &cursor, "End clamps: %d, Mid clamps: %d\n", report->racking.end_clamp_count, report->racking.mid_clamp_count);
        appendf(out_buffer, out_buffer_size, &cursor, "</pre>\n");
    }

    appendf(out_buffer, out_buffer_size, &cursor, "<h2>7. BOM / wire schedule</h2>\n");
    appendf(out_buffer, out_buffer_size, &cursor, "<pre>");
    append_escaped(out_buffer, out_buffer_size, &cursor, report->bom_text);
    appendf(out_buffer, out_buffer_size, &cursor, "</pre>\n");

    if (report->warning_count > 0) {
        appendf(out_buffer, out_buffer_size, &cursor, "<h2>8. Warnings</h2>\n");
        appendf(out_buffer, out_buffer_size, &cursor, "<pre>\n");
        for (size_t i = 0; i < report->warning_count; i++) {
            append_escaped(out_buffer, out_buffer_size, &cursor, report->warnings[i]);
            appendf(out_buffer, out_buffer_size, &cursor, "\n");
        }
        appendf(out_buffer, out_buffer_size, &cursor, "</pre>\n");
    }

    appendf(out_buffer, out_buffer_size, &cursor, "<p class=\"disclaimer\">Design aid only — not for permit approval.</p>\n");
    appendf(out_buffer, out_buffer_size, &cursor, "</body></html>\n");

    if (out_written) *out_written = cursor;
    return true;
}
