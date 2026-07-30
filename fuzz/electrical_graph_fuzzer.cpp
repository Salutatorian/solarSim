#include <cstddef>
#include <cstdint>
#include <cstring>

#include "electrical_graph.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size < 16) return 0;

    solar_electrical_graph_t graph;
    solar_electrical_graph_init(&graph);

    solar_panel_definition_t def;
    solar_panel_definition_boviet_270(&def);

    size_t panel_count = data[0] % 8;
    for (size_t i = 0; i < panel_count; i++) {
        solar_guid_t id = {0, 0x1000 + (uint64_t)i};
        solar_panel_instance_t panel;
        solar_panel_instance_init(&panel, &id, &def.id, i * 1000.0, 0.0, 0);
        solar_electrical_graph_add_panel(&graph, &panel);
    }

    if (graph.string_count > 0) {
        for (size_t s = 0; s < graph.string_count && s < 4; s++) {
            solar_string_result_t result;
            solar_electrical_graph_evaluate_string(&graph, &graph.strings[s], &result);
        }
    }

    solar_electrical_graph_discover_strings(&graph);
    return 0;
}
