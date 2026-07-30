#include <cstddef>
#include <cstdint>
#include <cstring>

#include "electrical_graph.h"
#include "solar_panel.h"
#include "string_calculation.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size < 16) return 0;

    solar_electrical_graph_t graph;
    solar_electrical_graph_init(&graph);

    solar_panel_definition_t def;
    solar_panel_definition_boviet_270(&def);

    solar_definition_catalog_t catalog;
    solar_definition_catalog_init(&catalog);
    solar_definition_catalog_add(&catalog, &def);

    size_t panel_count = 2 + (data[0] % 8);
    for (size_t i = 0; i < panel_count; i++) {
        solar_guid_t id = {0, 0x1000 + (uint64_t)i};
        solar_panel_instance_t panel;
        solar_panel_instance_init(&panel, &id, &def.id, i * 1000.0, 0.0, 0);
        solar_electrical_graph_add_panel(&graph, &panel);
    }

    for (size_t i = 0; i + 1 < panel_count; i++) {
        const solar_component_t *a = solar_electrical_graph_find_component(&graph, &((solar_guid_t){0, 0x1000 + (uint64_t)i}));
        const solar_component_t *b = solar_electrical_graph_find_component(&graph, &((solar_guid_t){0, 0x1001 + (uint64_t)i}));
        if (a && b) {
            solar_electrical_graph_try_connect(&graph, &a->data.panel.ports[1].id, &b->data.panel.ports[0].id, 1500.0, 10);
        }
    }

    solar_electrical_graph_rebuild_strings(&graph);

    solar_project_result_t result;
    solar_calculate_project(&graph, &catalog, &result);

    for (size_t s = 0; s < result.string_result_count && s < 4; s++) {
        solar_calculate_string(&graph.strings[s], &graph, &catalog, &result.strings[s]);
    }
    return 0;
}
