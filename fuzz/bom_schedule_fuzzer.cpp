#include <cstddef>
#include <cstdint>
#include <cstring>

#include "bom_schedule.h"
#include "electrical_graph.h"
#include "solar_panel.h"
#include "string_calculation.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size < sizeof(solar_racking_layout_t)) return 0;

    solar_electrical_graph_t graph;
    solar_electrical_graph_init(&graph);

    solar_definition_catalog_t catalog;
    solar_definition_catalog_init(&catalog);

    solar_panel_definition_t def;
    solar_panel_definition_generic_400(&def);
    solar_definition_catalog_add(&catalog, &def);

    solar_guid_t panel_id;
    solar_panel_guid_from_u64_pair(&panel_id, 0, 1);
    solar_panel_instance_t panel;
    solar_panel_instance_init(&panel, &panel_id, &def.id, 0.0, 0.0, 0);
    solar_electrical_graph_add_panel(&graph, &panel);

    solar_guid_t panel_id2;
    solar_panel_guid_from_u64_pair(&panel_id2, 0, 2);
    solar_panel_instance_t panel2;
    solar_panel_instance_init(&panel2, &panel_id2, &def.id, 1000.0, 0.0, 0);
    solar_electrical_graph_add_panel(&graph, &panel2);

    solar_electrical_graph_try_connect(&graph, &panel.ports[1].id, &panel2.ports[0].id, 15000.0, 10);

    solar_racking_layout_t racking;
    std::memcpy(&racking, data, sizeof(racking));

    solar_bom_report_t report;
    solar_bom_schedule_build(&graph, &catalog, &racking, &report);

    char text[SOLAR_BOM_PLAIN_TEXT_LEN];
    solar_bom_report_to_plain_text(&report, text, sizeof(text));
    return 0;
}
