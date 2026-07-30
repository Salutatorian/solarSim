#include "bom_schedule.h"

#include <cstddef>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cmath>

#include "wire_gauge_format.h"

static void copy_string(char *dest, size_t dest_size, const char *src) {
    if (!src || dest_size == 0) return;
    size_t len = std::strlen(src);
    if (len >= dest_size) len = dest_size - 1;
    std::memcpy(dest, src, len);
    dest[len] = '\0';
}

static bool eq_guid(const solar_guid_t *a, const solar_guid_t *b) {
    return a->id_high == b->id_high && a->id_low == b->id_low;
}

static void add_item(
    solar_bom_report_t *report,
    const char *category,
    const char *description,
    int quantity,
    const char *unit,
    double total_length_mm,
    bool has_length,
    const char *notes) {
    if (!report || !category || !description || !unit) return;
    if (report->item_count >= SOLAR_BOM_MAX_ITEMS) return;
    solar_bom_line_item_t *item = &report->items[report->item_count];
    std::memset(item, 0, sizeof(*item));
    copy_string(item->category, SOLAR_BOM_CATEGORY_LEN, category);
    copy_string(item->description, SOLAR_BOM_DESCRIPTION_LEN, description);
    item->quantity = quantity;
    copy_string(item->unit, SOLAR_BOM_UNIT_LEN, unit);
    item->total_length_mm = total_length_mm;
    item->has_length = has_length;
    if (notes) copy_string(item->notes, SOLAR_BOM_NOTES_LEN, notes);
    report->item_count++;
}

void solar_racking_layout_init(solar_racking_layout_t *layout) {
    if (!layout) return;
    std::memset(layout, 0, sizeof(*layout));
}

void solar_bom_report_init(solar_bom_report_t *report) {
    if (!report) return;
    std::memset(report, 0, sizeof(*report));
}

bool solar_bom_report_add_item(solar_bom_report_t *report, const solar_bom_line_item_t *item) {
    if (!report || !item) return false;
    if (report->item_count >= SOLAR_BOM_MAX_ITEMS) return false;
    report->items[report->item_count++] = *item;
    return true;
}

void solar_bom_connection_properties(
    const solar_connection_t *connection,
    const char **out_material,
    const char **out_type,
    const char **out_color) {
    if (!connection) return;
    if (out_material) *out_material = "Copper";
    if (out_type) *out_type = connection->wire_type[0] ? connection->wire_type : "PV wire";
    if (out_color) *out_color = "Black";
}

static int compare_wire_group(const void *a, const void *b) {
    const solar_bom_line_item_t *ia = static_cast<const solar_bom_line_item_t*>(a);
    const solar_bom_line_item_t *ib = static_cast<const solar_bom_line_item_t*>(b);
    /* Gauge stored as first integer in description for grouping. */
    int ga = 0, gb = 0;
    std::sscanf(ia->description, "%d", &ga);
    std::sscanf(ib->description, "%d", &gb);
    if (ga != gb) return ga - gb;
    return std::strcmp(ia->description, ib->description);
}

static void group_wire_runs(
    const solar_electrical_graph_t *graph,
    solar_bom_line_item_t *groups,
    size_t *group_count,
    int *total_runs,
    double *total_mm) {
    if (!graph || !groups || !group_count) return;
    *group_count = 0;
    if (total_runs) *total_runs = 0;
    if (total_mm) *total_mm = 0.0;

    for (size_t i = 0; i < graph->connection_count; i++) {
        const solar_connection_t *conn = &graph->connections[i];
        solar_wire_gauge_awg_t gauge = solar_wire_gauge_from_int(conn->gauge_awg);
        if (!solar_wire_gauge_is_valid(gauge)) gauge = SOLAR_AWG_10;

        const char *material = "Copper";
        const char *type = conn->wire_type[0] ? conn->wire_type : "PV wire";
        const char *color = "Black";
        solar_bom_connection_properties(conn, &material, &type, &color);

        char gauge_str[32];
        solar_wire_gauge_to_display(gauge, gauge_str, sizeof(gauge_str));

        char desc[128];
        std::snprintf(desc, sizeof(desc), "%s %s %s (%s)", gauge_str, material, type, color);

        size_t found = 0;
        for (; found < *group_count; found++) {
            if (std::strcmp(groups[found].description, desc) == 0) {
                groups[found].quantity++;
                groups[found].total_length_mm += conn->length_mm;
                break;
            }
        }
        if (found == *group_count && *group_count < SOLAR_BOM_MAX_ITEMS) {
            solar_bom_line_item_t *g = &groups[*group_count];
            std::memset(g, 0, sizeof(*g));
            copy_string(g->category, SOLAR_BOM_CATEGORY_LEN, "Wire");
            copy_string(g->description, SOLAR_BOM_DESCRIPTION_LEN, desc);
            g->quantity = 1;
            copy_string(g->unit, SOLAR_BOM_UNIT_LEN, "run");
            g->total_length_mm = conn->length_mm;
            g->has_length = true;
            copy_string(g->notes, SOLAR_BOM_NOTES_LEN, "one-way length (circuit ≈ ×2 for +/− pairs if shared)");
            (*group_count)++;
        }

        if (total_runs) (*total_runs)++;
        if (total_mm) *total_mm += conn->length_mm;
    }

    if (*group_count > 1) {
        std::qsort(groups, *group_count, sizeof(solar_bom_line_item_t), compare_wire_group);
    }
}

static void add_panel_items(
    const solar_electrical_graph_t *graph,
    const solar_definition_catalog_t *catalog,
    solar_bom_report_t *report) {
    if (!graph || !catalog || !report) return;

    /* Count panels by definition. */
    struct def_count_t {
        solar_guid_t id;
        int count;
    } counts[SOLAR_MAX_DEFINITIONS];
    size_t count_len = 0;

    for (size_t i = 0; i < graph->component_count; i++) {
        if (graph->components[i].kind != SOLAR_COMPONENT_PANEL) continue;
        const solar_guid_t *def_id = &graph->components[i].data.panel.definition_id;
        size_t found = 0;
        for (; found < count_len; found++) {
            if (eq_guid(&counts[found].id, def_id)) {
                counts[found].count++;
                break;
            }
        }
        if (found == count_len && count_len < SOLAR_MAX_DEFINITIONS) {
            counts[count_len].id = *def_id;
            counts[count_len].count = 1;
            count_len++;
        }
    }

    for (size_t i = 0; i < count_len; i++) {
        const solar_panel_definition_t *def = solar_definition_catalog_find(catalog, &counts[i].id);
        if (!def) continue;
        char notes[128];
        std::snprintf(notes, sizeof(notes), "%.1f W · %.1f Voc", def->pmax_watts, def->voc_volts);
        add_item(report, "Module", def->model, counts[i].count, "ea", 0.0, false, notes);
        report->panel_count += counts[i].count;
        report->total_dc_watts += counts[i].count * def->pmax_watts;
    }
}

static void add_equipment_items(
    const solar_electrical_graph_t *graph,
    solar_bom_report_t *report) {
    /* The base graph does not carry equipment components, so this helper is a stub
     * reserved for callers that populate the report with equipment through the public API. */
    (void)graph;
    (void)report;
}

static void add_racking_items(
    const solar_racking_layout_t *racking,
    solar_bom_report_t *report) {
    if (!racking || !report) return;
    if (racking->rail_count == 0) return;

    char notes[128];
    std::snprintf(notes, sizeof(notes), "%zu row(s)", racking->row_count);
    add_item(report, "Racking", "Rail run (est.)", static_cast<int>(racking->rail_count),
        "ea", racking->total_rail_length_mm, true, notes);

    add_item(report, "Racking", "Roof attachment / lag (est.)",
        static_cast<int>(racking->attachment_count), "ea", 0.0, false, NULL);

    if (racking->end_clamp_count > 0) {
        add_item(report, "Racking", "End clamp (est.)",
            static_cast<int>(racking->end_clamp_count), "ea", 0.0, false, NULL);
    }
    if (racking->mid_clamp_count > 0) {
        add_item(report, "Racking", "Mid clamp (est.)",
            static_cast<int>(racking->mid_clamp_count), "ea", 0.0, false, NULL);
    }
}

void solar_bom_schedule_build(
    const solar_electrical_graph_t *graph,
    const solar_definition_catalog_t *catalog,
    const solar_racking_layout_t *racking,
    solar_bom_report_t *out_report) {
    solar_bom_report_init(out_report);
    if (!graph || !catalog || !out_report) return;

    add_panel_items(graph, catalog, out_report);
    add_equipment_items(graph, out_report);

    solar_bom_line_item_t wire_groups[SOLAR_BOM_MAX_ITEMS];
    size_t wire_group_count = 0;
    int total_runs = 0;
    double total_mm = 0.0;
    group_wire_runs(graph, wire_groups, &wire_group_count, &total_runs, &total_mm);
    for (size_t i = 0; i < wire_group_count && out_report->item_count < SOLAR_BOM_MAX_ITEMS; i++) {
        out_report->items[out_report->item_count++] = wire_groups[i];
    }
    out_report->wire_run_count = total_runs;
    out_report->total_wire_length_mm = total_mm;

    if (out_report->panel_count > 0) {
        add_item(out_report, "Connector", "MC4-compatible pair (est. 1 pair / module)",
            out_report->panel_count, "pair", 0.0, false, NULL);
    }

    add_racking_items(racking, out_report);
}

const char *solar_bom_report_to_plain_text(
    const solar_bom_report_t *report,
    char *buffer,
    size_t buffer_size) {
    if (!report || !buffer || buffer_size == 0) return "";

    int written = std::snprintf(buffer, buffer_size,
        "solarSim — BOM / Wire Schedule\n"
        "Design aid only — not a purchasing quote.\n"
        "\n"
        "Modules: %d  |  ΣPmax %.1f W\n"
        "Wire runs: %d  |  Total one-way %.3f m\n"
        "\n"
        "%-6s %-6s %-12s Description\n"
        "%-72s\n",
        report->panel_count,
        report->total_dc_watts,
        report->wire_run_count,
        report->total_wire_length_mm / 1000.0,
        "Qty", "Unit", "Category",
        "------------------------------------------------------------------------");

    if (written < 0 || static_cast<size_t>(written) >= buffer_size) return buffer;

    size_t offset = static_cast<size_t>(written);
    for (size_t i = 0; i < report->item_count; i++) {
        const solar_bom_line_item_t *item = &report->items[i];
        char desc_line[256];
        if (item->has_length && item->total_length_mm > 0.0) {
            std::snprintf(desc_line, sizeof(desc_line), "%s  (%.3f m)", item->description, item->total_length_mm / 1000.0);
        } else {
            copy_string(desc_line, sizeof(desc_line), item->description);
        }
        if (item->notes[0]) {
            size_t len = std::strlen(desc_line);
            if (len + 4 < sizeof(desc_line)) {
                std::snprintf(desc_line + len, sizeof(desc_line) - len, "  — %s", item->notes);
            }
        }

        int n = std::snprintf(buffer + offset, buffer_size - offset,
            "%-6d %-6s %-12s %s\n",
            item->quantity, item->unit, item->category, desc_line);
        if (n < 0 || static_cast<size_t>(n) >= buffer_size - offset) break;
        offset += static_cast<size_t>(n);
    }
    return buffer;
}

void solar_bom_schedule_add_equipment(
    solar_bom_report_t *report,
    const char *name,
    const char *kind,
    int quantity,
    const char *notes) {
    if (!report || !name || !kind) return;
    char description[128];
    std::snprintf(description, sizeof(description), "%s (%s)", name, kind);
    add_item(report, "Equipment", description, quantity, "ea", 0.0, false, notes);
}

void solar_bom_schedule_estimate_connectors(
    solar_bom_report_t *report,
    size_t occupied_equipment_ports) {
    if (!report) return;
    if (occupied_equipment_ports == 0) return;
    int extra = static_cast<int>(occupied_equipment_ports + 1) / 2;
    add_item(report, "Connector", "Equipment connector (est.)", extra, "pair", 0.0, false,
        "extras for occupied equipment terminals");
}

void solar_bom_schedule_merge_duplicates(solar_bom_report_t *report) {
    if (!report || report->item_count < 2) return;
    for (size_t i = 0; i < report->item_count; i++) {
        solar_bom_line_item_t *a = &report->items[i];
        if (a->quantity == 0) continue;
        for (size_t j = i + 1; j < report->item_count; j++) {
            solar_bom_line_item_t *b = &report->items[j];
            if (b->quantity == 0) continue;
            if (std::strcmp(a->category, b->category) != 0) continue;
            if (std::strcmp(a->description, b->description) != 0) continue;
            if (std::strcmp(a->unit, b->unit) != 0) continue;
            a->quantity += b->quantity;
            if (a->has_length && b->has_length) {
                a->total_length_mm += b->total_length_mm;
            } else if (b->has_length) {
                a->has_length = true;
                a->total_length_mm = b->total_length_mm;
            }
            if (b->notes[0] && !a->notes[0]) {
                copy_string(a->notes, SOLAR_BOM_NOTES_LEN, b->notes);
            }
            b->quantity = 0;
        }
    }

    /* Compact out zeroed items. */
    size_t write = 0;
    for (size_t i = 0; i < report->item_count; i++) {
        if (report->items[i].quantity > 0) {
            report->items[write++] = report->items[i];
        }
    }
    report->item_count = write;
}

void solar_bom_schedule_summary(
    const solar_bom_report_t *report,
    const char *category,
    int *out_quantity,
    double *out_length_mm) {
    if (!report) return;
    if (out_quantity) *out_quantity = 0;
    if (out_length_mm) *out_length_mm = 0.0;
    if (!category) return;
    for (size_t i = 0; i < report->item_count; i++) {
        if (std::strcmp(report->items[i].category, category) == 0) {
            if (out_quantity) *out_quantity += report->items[i].quantity;
            if (out_length_mm && report->items[i].has_length) {
                *out_length_mm += report->items[i].total_length_mm;
            }
        }
    }
}

void solar_bom_schedule_build_with_equipment(
    const solar_electrical_graph_t *graph,
    const solar_definition_catalog_t *catalog,
    const solar_racking_layout_t *racking,
    const char * const *equipment_names,
    const char * const *equipment_kinds,
    size_t equipment_count,
    solar_bom_report_t *out_report) {
    solar_bom_schedule_build(graph, catalog, racking, out_report);
    if (!out_report) return;
    for (size_t i = 0; i < equipment_count; i++) {
        const char *name = equipment_names ? equipment_names[i] : NULL;
        const char *kind = equipment_kinds ? equipment_kinds[i] : NULL;
        if (!name || !kind) continue;
        solar_bom_schedule_add_equipment(out_report, name, kind, 1, NULL);
    }
    solar_bom_schedule_merge_duplicates(out_report);
}

