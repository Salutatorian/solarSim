#include "design_report.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <ctime>
#include <string>
#include <vector>

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

static void get_panel_footprint(
    const solar_panel_definition_t *def,
    int rotation_degrees,
    double *out_width,
    double *out_height) {
    int rot = ((rotation_degrees % 180) + 180) % 180;
    if (rot == 90) {
        *out_width = def->height_mm;
        *out_height = def->width_mm;
    } else {
        *out_width = def->width_mm;
        *out_height = def->height_mm;
    }
}

static void format_double_1(double value, char *buf, size_t size) {
    std::snprintf(buf, size, "%.1f", value);
}

static void format_double_2(double value, char *buf, size_t size) {
    std::snprintf(buf, size, "%.2f", value);
}

static void format_double_3(double value, char *buf, size_t size) {
    std::snprintf(buf, size, "%.3f", value);
}

static void format_double_0(double value, char *buf, size_t size) {
    std::snprintf(buf, size, "%.0f", value);
}

static void format_time_utc(char *buf, size_t size) {
    std::time_t now = std::time(nullptr);
    std::tm *tm = std::gmtime(&now);
    if (!tm) {
        copy_string(buf, size, "unknown");
        return;
    }
    std::strftime(buf, size, "%Y-%m-%d %H:%M", tm);
}

static void html_escape(const char *src, std::string &dest) {
    if (!src) return;
    for (const char *p = src; *p; ++p) {
        switch (*p) {
            case '&': dest += "&amp;"; break;
            case '<': dest += "&lt;"; break;
            case '>': dest += "&gt;"; break;
            case '"': dest += "&quot;"; break;
            case '\'': dest += "&#39;"; break;
            default: dest += *p; break;
        }
    }
}

static const char *find_string_name_for_panel(
    const solar_electrical_graph_t *graph,
    const solar_guid_t *panel_id) {
    static const char *none = "-";
    for (size_t s = 0; s < graph->string_count; ++s) {
        const solar_pv_string_t *str = &graph->strings[s];
        for (size_t p = 0; p < str->panel_count; ++p) {
            if (solar_panel_guid_equals(&str->panel_ids[p], panel_id)) {
                return str->display_name;
            }
        }
    }
    return none;
}

static void build_single_line_summary(
    const solar_project_state_t *state,
    const solar_project_result_t *calc,
    char *out,
    size_t out_size) {
    (void)state;
    char buf[256];
    std::snprintf(out, out_size, "PV array: %zu modules, %zu strings, ",
                  calc->total_panels, calc->string_count);
    format_double_1(calc->total_pmax_watts, buf, sizeof(buf));
    std::strncat(out, buf, out_size - std::strlen(out) - 1);
    std::strncat(out, " W DC", out_size - std::strlen(out) - 1);

    for (size_t i = 0; i < calc->string_result_count && i < 4; ++i) {
        const solar_string_result_t *str = &calc->strings[i];
        std::snprintf(buf, sizeof(buf), "\n%s: %zu panels, Vmp=",
                       str->display_name, str->panel_count);
        std::strncat(out, buf, out_size - std::strlen(out) - 1);
        format_double_1(str->vmp_volts, buf, sizeof(buf));
        std::strncat(out, buf, out_size - std::strlen(out) - 1);
        std::strncat(out, " V, Voc=", out_size - std::strlen(out) - 1);
        format_double_1(str->voc_volts, buf, sizeof(buf));
        std::strncat(out, buf, out_size - std::strlen(out) - 1);
        std::strncat(out, " V, Imp=", out_size - std::strlen(out) - 1);
        format_double_2(str->imp_amps, buf, sizeof(buf));
        std::strncat(out, buf, out_size - std::strlen(out) - 1);
        std::strncat(out, " A", out_size - std::strlen(out) - 1);
    }
}

static void build_string_results_text(
    const solar_project_result_t *calc,
    design_report_t *out) {
    if (!calc || !out) return;
    char *buffer = out->string_results_text;
    size_t size = sizeof(out->string_results_text);
    buffer[0] = '\0';
    if (calc->string_result_count == 0) {
        copy_string(buffer, size, "No strings discovered.");
        return;
    }
    for (size_t i = 0; i < calc->string_result_count; ++i) {
        const solar_string_result_t *str = &calc->strings[i];
        char line[256];
        char vmp[32], voc[32], imp[32];
        format_double_1(str->vmp_volts, vmp, sizeof(vmp));
        format_double_1(str->voc_volts, voc, sizeof(voc));
        format_double_2(str->imp_amps, imp, sizeof(imp));
        std::snprintf(line, sizeof(line),
            "%s: %zu panels, Vmp=%s V, Voc=%s V, Imp=%s A, Pmax=%.0f W%s\n",
            str->display_name, str->panel_count, vmp, voc, imp, str->total_pmax_watts,
            str->is_mixed_module_string ? " (mixed modules)" : "");
        std::strncat(buffer, line, size - std::strlen(buffer) - 1);
    }
}

static void build_bom_text(
    const solar_project_state_t *state,
    const solar_project_result_t *calc,
    bool has_racking,
    const design_report_racking_t *racking,
    char *out,
    size_t out_size) {
    std::string bom;
    bom += "solarSim - BOM / Wire Schedule\n";
    bom += "Design aid only - not a purchasing quote.\n\n";

    char buf[256];
    char val[64];
    format_double_1(calc->total_pmax_watts, val, sizeof(val));
    std::snprintf(buf, sizeof(buf), "Modules: %zu  |  Sigma Pmax %s W\n",
                  calc->total_panels, val);
    bom += buf;

    double total_wire_mm = 0.0;
    size_t wire_runs = 0;
    for (size_t i = 0; i < state->graph.connection_count; ++i) {
        total_wire_mm += state->graph.connections[i].length_mm;
        ++wire_runs;
    }
    format_double_3(total_wire_mm / 1000.0, val, sizeof(val));
    std::snprintf(buf, sizeof(buf), "Wire runs: %zu  |  Total one-way %s m\n\n",
                  wire_runs, val);
    bom += buf;

    bom += "Qty    Unit   Category     Description\n";
    bom += "------------------------------------------------------------------------\n";

    struct DefGroup {
        const solar_panel_definition_t *def;
        size_t count;
    };
    std::vector<DefGroup> groups;
    for (size_t i = 0; i < state->graph.component_count; ++i) {
        if (state->graph.components[i].kind != SOLAR_COMPONENT_PANEL) continue;
        const solar_panel_instance_t *panel = &state->graph.components[i].data.panel;
        const solar_panel_definition_t *def =
            solar_project_state_find_definition(state, &panel->definition_id);
        if (!def) continue;
        bool found = false;
        for (auto &g : groups) {
            if (solar_panel_guid_equals(&g.def->id, &def->id)) {
                g.count++;
                found = true;
                break;
            }
        }
        if (!found) groups.push_back({def, 1});
    }

    for (const auto &g : groups) {
        std::snprintf(buf, sizeof(buf), "%-6zu %-6s %-12s %s (%.1f W, %.1f Voc)",
                        g.count, "ea", "Module", g.def->model,
                        g.def->pmax_watts, g.def->voc_volts);
        bom += buf;
        bom += "\n";
    }

    for (const auto &eq : state->equipment) {
        std::snprintf(buf, sizeof(buf), "%-6zu %-6s %-12s %s (%s)\n",
                        1, "ea", "Equipment", eq.name, eq.catalog_series);
        bom += buf;
    }

    struct WireGroup {
        int gauge_awg;
        char wire_type[32];
        size_t runs;
        double total_length_mm;
    };
    std::vector<WireGroup> wire_groups;
    for (size_t i = 0; i < state->graph.connection_count; ++i) {
        const solar_connection_t *conn = &state->graph.connections[i];
        bool found = false;
        for (auto &wg : wire_groups) {
            if (wg.gauge_awg == conn->gauge_awg &&
                std::strcmp(wg.wire_type, conn->wire_type) == 0) {
                wg.runs++;
                wg.total_length_mm += conn->length_mm;
                found = true;
                break;
            }
        }
        if (!found) {
            wire_groups.push_back({conn->gauge_awg, {}, 1, conn->length_mm});
            copy_string(wire_groups.back().wire_type, sizeof(wire_groups.back().wire_type), conn->wire_type);
        }
    }
    for (const auto &wg : wire_groups) {
        std::snprintf(buf, sizeof(buf), "%-6zu %-6s %-12s AWG %d %s (%.3f m one-way)\n",
                        wg.runs, "run", "Wire", wg.gauge_awg, wg.wire_type,
                        wg.total_length_mm / 1000.0);
        bom += buf;
    }

    if (calc->total_panels > 0) {
        std::snprintf(buf, sizeof(buf), "%-6zu %-6s %-12s MC4-compatible pair (est. 1 pair / module)\n",
                        calc->total_panels, "pair", "Connector");
        bom += buf;
    }

    if (has_racking && racking->rail_count > 0) {
        std::snprintf(buf, sizeof(buf), "%-6d %-6s %-12s Rail run (%.3f m total)\n",
                        racking->rail_count, "ea", "Racking", racking->total_rail_length_mm / 1000.0);
        bom += buf;
        std::snprintf(buf, sizeof(buf), "%-6d %-6s %-12s Roof attachment / lag (est.)\n",
                        racking->attachment_count, "ea", "Racking");
        bom += buf;
        if (racking->end_clamp_count > 0) {
            std::snprintf(buf, sizeof(buf), "%-6d %-6s %-12s End clamp (est.)\n",
                            racking->end_clamp_count, "ea", "Racking");
            bom += buf;
        }
        if (racking->mid_clamp_count > 0) {
            std::snprintf(buf, sizeof(buf), "%-6d %-6s %-12s Mid clamp (est.)\n",
                            racking->mid_clamp_count, "ea", "Racking");
            bom += buf;
        }
    }

    copy_string(out, out_size, bom.c_str());
}

void design_report_init(design_report_t *report) {
    if (!report) return;
    std::memset(report, 0, sizeof(*report));
    copy_string(report->location_name, DESIGN_REPORT_LOCATION_LEN, "Unspecified");
    copy_string(report->method_note, DESIGN_REPORT_METHOD_NOTE_LEN, "");
}

bool design_report_build(const solar_project_state_t *state, design_report_t *out) {
    if (!state || !out) return false;
    design_report_init(out);

    solar_project_result_t calc = solar_project_state_calculate(state);
    solar_detailed_production_estimate_t energy;
    solar_project_state_get_detailed_production_estimate(state, &energy);

    copy_string(out->project_name, DESIGN_REPORT_NAME_LEN, state->name);
    format_time_utc(out->generated_utc, sizeof(out->generated_utc));

    build_single_line_summary(state, &calc, out->single_line_text, sizeof(out->single_line_text));
    build_string_results_text(&calc, out);

    out->panel_count = calc.total_panels;
    out->total_dc_watts = calc.total_pmax_watts;
    out->string_count = calc.string_count;
    out->min_ambient_celsius = state->site.min_ambient_celsius;
    out->hot_cell_celsius = state->site.hot_cell_celsius;
    copy_string(out->location_name, DESIGN_REPORT_LOCATION_LEN, state->site.location_name);
    out->has_latitude = state->site.has_latitude;
    out->has_longitude = state->site.has_longitude;
    out->latitude_degrees = state->site.latitude_degrees;
    out->longitude_degrees = state->site.longitude_degrees;
    out->peak_sun_hours_per_day = state->site.peak_sun_hours_per_day;
    out->system_derate_factor = state->site.system_derate_factor;
    out->array_tilt_degrees = energy.array_tilt_degrees;
    out->array_azimuth_degrees = energy.array_azimuth_degrees;
    out->estimated_annual_kwh = energy.estimated_annual_kwh;
    out->estimated_daily_kwh = energy.estimated_daily_kwh;
    out->monthly_production_count = 12;
    for (int i = 0; i < 12; ++i) {
        out->monthly_production[i].month = energy.months[i].month;
        copy_string(out->monthly_production[i].month_name, DESIGN_REPORT_NAME_LEN, energy.months[i].month_name);
        out->monthly_production[i].peak_sun_hours_per_day = energy.months[i].peak_sun_hours_per_day;
        out->monthly_production[i].estimated_kwh = energy.months[i].estimated_kwh;
    }
    copy_string(out->method_note, DESIGN_REPORT_METHOD_NOTE_LEN, energy.method_note);

    out->has_racking = state->racking_layout.valid;
    if (out->has_racking) {
        out->racking.row_count = state->racking_layout.row_count;
        out->racking.rail_count = state->racking_layout.rail_count;
        out->racking.total_rail_length_mm = state->racking_layout.total_rail_length_mm;
        out->racking.attachment_count = state->racking_layout.attachment_count;
        out->racking.end_clamp_count = state->racking_layout.end_clamp_count;
        out->racking.mid_clamp_count = state->racking_layout.mid_clamp_count;
    }

    struct PanelRef {
        size_t component_index;
        double y_mm;
        double x_mm;
    };
    std::vector<PanelRef> refs;
    for (size_t i = 0; i < state->graph.component_count; ++i) {
        if (state->graph.components[i].kind != SOLAR_COMPONENT_PANEL) continue;
        const solar_panel_instance_t *panel = &state->graph.components[i].data.panel;
        refs.push_back({i, panel->position_y_mm, panel->position_x_mm});
    }
    std::sort(refs.begin(), refs.end(), [](const PanelRef &a, const PanelRef &b) {
        if (a.y_mm != b.y_mm) return a.y_mm < b.y_mm;
        return a.x_mm < b.x_mm;
    });

    size_t index = 1;
    for (const auto &ref : refs) {
        if (out->module_count >= DESIGN_REPORT_MAX_MODULES) break;
        const solar_panel_instance_t *panel = &state->graph.components[ref.component_index].data.panel;
        const solar_panel_definition_t *def =
            solar_project_state_find_definition(state, &panel->definition_id);
        if (!def) continue;
        design_report_module_t *mod = &out->modules[out->module_count++];
        mod->index = static_cast<int>(index++);
        copy_string(mod->name, DESIGN_REPORT_NAME_LEN, def->model);
        mod->x_mm = panel->position_x_mm;
        mod->y_mm = panel->position_y_mm;
        get_panel_footprint(def, panel->rotation_degrees, &mod->width_mm, &mod->height_mm);
        mod->rotation_degrees = panel->rotation_degrees;
        copy_string(mod->string_name, DESIGN_REPORT_STRING_NAME_LEN,
                    find_string_name_for_panel(&state->graph, &panel->id));
    }

    for (size_t i = 0; i < calc.warning_count && out->warning_count < DESIGN_REPORT_MAX_WARNINGS; ++i) {
        std::snprintf(out->warnings[out->warning_count], sizeof(out->warnings[0]),
                      "[Warning] %s: %s", calc.warnings[i].code, calc.warnings[i].message);
        out->warning_count++;
    }
    for (size_t i = 0; i < calc.error_count && out->warning_count < DESIGN_REPORT_MAX_WARNINGS; ++i) {
        std::snprintf(out->warnings[out->warning_count], sizeof(out->warnings[0]),
                      "[Error] %s: %s", calc.errors[i].code, calc.errors[i].message);
        out->warning_count++;
    }

    build_bom_text(state, &calc, out->has_racking, &out->racking,
                   out->bom_text, sizeof(out->bom_text));
    return true;
}

static void build_svg(const design_report_t *report, std::string &svg) {
    if (report->module_count == 0) {
        svg += "<p style=\"color:#5c5c5c\">Place modules to generate an array layout sheet.</p>";
        return;
    }

    double min_x = report->modules[0].x_mm;
    double min_y = report->modules[0].y_mm;
    double max_x = report->modules[0].x_mm + report->modules[0].width_mm;
    double max_y = report->modules[0].y_mm + report->modules[0].height_mm;
    for (size_t i = 1; i < report->module_count; ++i) {
        const design_report_module_t *m = &report->modules[i];
        if (m->x_mm < min_x) min_x = m->x_mm;
        if (m->y_mm < min_y) min_y = m->y_mm;
        if (m->x_mm + m->width_mm > max_x) max_x = m->x_mm + m->width_mm;
        if (m->y_mm + m->height_mm > max_y) max_y = m->y_mm + m->height_mm;
    }
    double width = std::max(1.0, max_x - min_x);
    double height = std::max(1.0, max_y - min_y);
    const double pad = 200.0;
    double vb_w = width + pad * 2.0;
    double vb_h = height + pad * 2.0;
    const double svg_w = 720.0;
    double svg_h = svg_w * vb_h / vb_w;
    if (svg_h < 220.0) svg_h = 220.0;
    if (svg_h > 900.0) svg_h = 900.0;

    char buf[128];
    svg += "<svg xmlns=\"http://www.w3.org/2000/svg\" ";
    std::snprintf(buf, sizeof(buf), "width=\"%.0f\" height=\"%.0f\" ", svg_w, svg_h);
    svg += buf;
    std::snprintf(buf, sizeof(buf), "viewBox=\"0 0 %.0f %.0f\">\n", vb_w, vb_h);
    svg += buf;
    std::snprintf(buf, sizeof(buf), "<rect x=\"0\" y=\"0\" width=\"%.0f\" height=\"%.0f\" fill=\"#fafaf8\"/>\n", vb_w, vb_h);
    svg += buf;

    for (size_t i = 0; i < report->module_count; ++i) {
        const design_report_module_t *m = &report->modules[i];
        double x = m->x_mm - min_x + pad;
        double y = m->y_mm - min_y + pad;
        std::snprintf(buf, sizeof(buf),
                      "<rect x=\"%.1f\" y=\"%.1f\" width=\"%.1f\" height=\"%.1f\" "
                      "fill=\"#2a3a52\" stroke=\"#1a2433\" stroke-width=\"20\" rx=\"30\"/>\n",
                      x, y, m->width_mm, m->height_mm);
        svg += buf;
        double lx = x + m->width_mm / 2.0;
        double ly = y + m->height_mm / 2.0;
        double font = std::max(80.0, std::min(m->width_mm, m->height_mm) * 0.18);
        if (font > 220.0) font = 220.0;
        std::snprintf(buf, sizeof(buf),
                      "<text x=\"%.1f\" y=\"%.1f\" text-anchor=\"middle\" "
                      "dominant-baseline=\"middle\" fill=\"#ffffff\" font-size=\"%.1f\" "
                      "font-family=\"Segoe UI, sans-serif\">%d</text>\n",
                      lx, ly, font, m->index);
        svg += buf;
    }
    svg += "</svg>\n";
    svg += "<p style=\"font-size:11px;color:#5c5c5c;margin:6px 0 0\">";
    svg += "Plan view - numbers match module schedule - not to survey grade";
    svg += "</p>";
}

bool design_report_to_html(
    const design_report_t *report,
    char *out_buffer,
    size_t out_buffer_size,
    size_t *out_written) {
    if (!report || !out_buffer || out_buffer_size == 0) return false;

    std::string html;
    html += "<!DOCTYPE html>\n<html lang=\"en\"><head><meta charset=\"utf-8\"/>\n";
    html += "<title>";
    html_escape(report->project_name, html);
    html += " - solarSim Report</title>\n";
    html += "<style>\n";
    html += "body { font-family: \"Segoe UI\", system-ui, sans-serif; margin: 24px; color: #1f1f1f; }\n";
    html += "h1 { font-size: 22px; margin: 0 0 4px; }\n";
    html += "h2 { font-size: 15px; margin: 28px 0 8px; border-bottom: 1px solid #ddd; padding-bottom: 4px; }\n";
    html += ".meta { color: #5c5c5c; font-size: 12px; margin-bottom: 20px; }\n";
    html += ".badge { display: inline-block; background: #eef3ff; color: #2f6fed; padding: 2px 8px; border-radius: 4px; font-size: 12px; margin-right: 6px; }\n";
    html += "pre { background: #f7f7f5; border: 1px solid #e2e2de; padding: 12px; font-size: 12px; overflow: auto; white-space: pre-wrap; }\n";
    html += "table { border-collapse: collapse; width: 100%; font-size: 12px; }\n";
    html += "th, td { border: 1px solid #e2e2de; padding: 6px 8px; text-align: left; }\n";
    html += "th { background: #fafaf8; }\n";
    html += ".layout { border: 1px solid #e2e2de; background: #fff; padding: 8px; overflow: auto; }\n";
    html += ".disclaimer { color: #b54708; font-size: 12px; margin-top: 28px; }\n";
    html += "@media print { body { margin: 12mm; } .no-print { display: none; } h2 { break-after: avoid; } .layout, pre, table { break-inside: avoid; } }\n";
    html += "</style></head><body>\n";

    html += "<h1>";
    html_escape(report->project_name, html);
    html += "</h1>\n";
    html += "<div class=\"meta\">solarSim design report - generated ";
    html_escape(report->generated_utc, html);
    html += " UTC</div>\n";
    html += "<p class=\"no-print\"><em>Tip: Ctrl+P - Save as PDF</em></p>\n";

    html += "<p>\n";
    char buf[128];
    std::snprintf(buf, sizeof(buf), "<span class=\"badge\">%zu modules</span>\n", report->panel_count);
    html += buf;
    std::snprintf(buf, sizeof(buf), "<span class=\"badge\">%.1f W DC</span>\n", report->total_dc_watts);
    html += buf;
    std::snprintf(buf, sizeof(buf), "<span class=\"badge\">%zu strings</span>\n", report->string_count);
    html += buf;
    html += "<span class=\"badge\">";
    html_escape(report->location_name, html);
    html += "</span>\n";
    std::snprintf(buf, sizeof(buf), "<span class=\"badge\">Cold Voc %.1f C</span>\n", report->min_ambient_celsius);
    html += buf;
    std::snprintf(buf, sizeof(buf), "<span class=\"badge\">Hot cell %.1f C</span>\n", report->hot_cell_celsius);
    html += buf;
    std::snprintf(buf, sizeof(buf), "<span class=\"badge\">~%.0f kWh/yr</span>\n", report->estimated_annual_kwh);
    html += buf;
    html += "</p>\n";

    html += "<h2>0. Site assumptions</h2>\n<pre>\n";
    html += "Location: ";
    html_escape(report->location_name, html);
    html += "\n";
    if (report->has_latitude && report->has_longitude) {
        std::snprintf(buf, sizeof(buf), "Lat/Lon: %.3f, %.3f\n",
                      report->latitude_degrees, report->longitude_degrees);
        html += buf;
    }
    std::snprintf(buf, sizeof(buf), "Cold Voc ambient: %.1f C\n", report->min_ambient_celsius);
    html += buf;
    std::snprintf(buf, sizeof(buf), "Hot cell: %.1f C\n", report->hot_cell_celsius);
    html += buf;
    std::snprintf(buf, sizeof(buf), "Peak sun hours: %.1f h/day\n", report->peak_sun_hours_per_day);
    html += buf;
    std::snprintf(buf, sizeof(buf), "System derate: %.2f\n", report->system_derate_factor);
    html += buf;
    std::snprintf(buf, sizeof(buf), "Array tilt / az: %.1f / %.1f\n",
                  report->array_tilt_degrees, report->array_azimuth_degrees);
    html += buf;
    std::snprintf(buf, sizeof(buf), "Est. energy: ~%.2f kWh/day - ~%.0f kWh/year\n",
                  report->estimated_daily_kwh, report->estimated_annual_kwh);
    html += buf;
    html_escape(report->method_note, html);
    html += "\n</pre>\n";

    if (report->monthly_production_count > 0) {
        html += "<h2>0b. Monthly production (est.)</h2>\n<table><thead><tr>\n";
        for (size_t i = 0; i < report->monthly_production_count; ++i) {
            html += "<th>";
            html_escape(report->monthly_production[i].month_name, html);
            html += "</th>\n";
        }
        html += "</tr></thead><tbody><tr>\n";
        for (size_t i = 0; i < report->monthly_production_count; ++i) {
            std::snprintf(buf, sizeof(buf), "<td>%.0f</td>\n", report->monthly_production[i].estimated_kwh);
            html += buf;
        }
        html += "</tr></tbody></table>\n";
    }

    html += "<h2>1. Single-line summary</h2>\n<pre>";
    html_escape(report->single_line_text, html);
    html += "</pre>\n";

    html += "<h2>1b. String results</h2>\n<pre>";
    html_escape(report->string_results_text, html);
    html += "</pre>\n";

    html += "<h2>2. Array layout</h2>\n<div class=\"layout\">\n";
    build_svg(report, html);
    html += "\n</div>\n";

    html += "<h2>3. Module schedule</h2>\n<table><thead><tr>\n";
    html += "<th>#</th><th>Module</th><th>String</th><th>X (mm)</th><th>Y (mm)</th>";
    html += "<th>WxH (mm)</th><th>Rot</th>\n";
    html += "</tr></thead><tbody>\n";
    for (size_t i = 0; i < report->module_count; ++i) {
        const design_report_module_t *m = &report->modules[i];
        html += "<tr>\n";
        std::snprintf(buf, sizeof(buf), "<td>%d</td>\n", m->index);
        html += buf;
        html += "<td>";
        html_escape(m->name, html);
        html += "</td>\n";
        html += "<td>";
        html_escape(m->string_name, html);
        html += "</td>\n";
        std::snprintf(buf, sizeof(buf), "<td>%.1f</td><td>%.1f</td>\n", m->x_mm, m->y_mm);
        html += buf;
        std::snprintf(buf, sizeof(buf), "<td>%.1f x %.1f</td>\n", m->width_mm, m->height_mm);
        html += buf;
        std::snprintf(buf, sizeof(buf), "<td>%d</td>\n", m->rotation_degrees);
        html += buf;
        html += "</tr>\n";
    }
    if (report->module_count == 0) {
        html += "<tr><td colspan=\"7\">No modules placed.</td></tr>\n";
    }
    html += "</tbody></table>\n";

    if (report->has_racking && report->racking.rail_count > 0) {
        html += "<h2>4. Racking estimate</h2>\n<pre>\n";
        std::snprintf(buf, sizeof(buf), "Rows: %d\n", report->racking.row_count);
        html += buf;
        std::snprintf(buf, sizeof(buf), "Rails: %d  -  Total rail %.3f m\n",
                        report->racking.rail_count, report->racking.total_rail_length_mm / 1000.0);
        html += buf;
        std::snprintf(buf, sizeof(buf), "Attachments: %d\n", report->racking.attachment_count);
        html += buf;
        std::snprintf(buf, sizeof(buf), "End clamps: %d  -  Mid clamps: %d\n",
                        report->racking.end_clamp_count, report->racking.mid_clamp_count);
        html += buf;
        html += "Design aid only - not structural engineering.\n";
        html += "</pre>\n";
    }

    html += "<h2>5. BOM / wire schedule</h2>\n<pre>";
    html_escape(report->bom_text, html);
    html += "</pre>\n";

    if (report->warning_count > 0) {
        html += "<h2>6. Warnings / issues</h2>\n<pre>\n";
        for (size_t i = 0; i < report->warning_count; ++i) {
            html_escape(report->warnings[i], html);
            html += "\n";
        }
        html += "</pre>\n";
    }

    html += "<p class=\"disclaimer\">Design aid only - not for permit approval. ";
    html += "Verify with a licensed electrician / structural engineer.</p>\n";
    html += "</body></html>\n";

    if (html.size() >= out_buffer_size) return false;
    std::memcpy(out_buffer, html.data(), html.size());
    out_buffer[html.size()] = '\0';
    if (out_written) *out_written = html.size();
    return true;
}

bool design_report_to_plain_text(
    const design_report_t *report,
    char *out_buffer,
    size_t out_buffer_size,
    size_t *out_written) {
    if (!report || !out_buffer || out_buffer_size == 0) return false;

    std::string text;
    char buf[256];
    text += "solarSim Design Report\n";
    text += "======================\n";
    std::snprintf(buf, sizeof(buf), "Project: %s\n", report->project_name);
    text += buf;
    std::snprintf(buf, sizeof(buf), "Generated: %s UTC\n", report->generated_utc);
    text += buf;
    text += "\n";

    std::snprintf(buf, sizeof(buf), "Modules: %zu\n", report->panel_count);
    text += buf;
    std::snprintf(buf, sizeof(buf), "Strings: %zu\n", report->string_count);
    text += buf;
    std::snprintf(buf, sizeof(buf), "Total DC power: %.1f W\n", report->total_dc_watts);
    text += buf;
    std::snprintf(buf, sizeof(buf), "Estimated energy: %.2f kWh/day, %.0f kWh/year\n",
                  report->estimated_daily_kwh, report->estimated_annual_kwh);
    text += buf;
    text += "\n";

    text += "Single-line summary:\n";
    text += report->single_line_text;
    text += "\n\n";

    text += "String results:\n";
    text += report->string_results_text;
    text += "\n\n";

    text += "Module schedule:\n";
    for (size_t i = 0; i < report->module_count; ++i) {
        const design_report_module_t *m = &report->modules[i];
        std::snprintf(buf, sizeof(buf), "  #%d %s @ (%.1f, %.1f) %.1fx%.1f mm rot %d  [%s]\n",
                      m->index, m->name, m->x_mm, m->y_mm, m->width_mm, m->height_mm,
                      m->rotation_degrees, m->string_name);
        text += buf;
    }
    text += "\n";

    text += "BOM / wire schedule:\n";
    text += report->bom_text;
    text += "\n";

    if (report->warning_count > 0) {
        text += "\nWarnings:\n";
        for (size_t i = 0; i < report->warning_count; ++i) {
            text += "  ";
            text += report->warnings[i];
            text += "\n";
        }
    }

    if (text.size() >= out_buffer_size) return false;
    std::memcpy(out_buffer, text.data(), text.size());
    out_buffer[text.size()] = '\0';
    if (out_written) *out_written = text.size();
    return true;
}

void design_report_string_results_to_text(
    const design_report_t *report,
    char *out_buffer,
    size_t out_buffer_size) {
    if (!out_buffer || out_buffer_size == 0) return;
    if (!report) {
        copy_string(out_buffer, out_buffer_size, "");
        return;
    }
    copy_string(out_buffer, out_buffer_size, report->string_results_text);
}
