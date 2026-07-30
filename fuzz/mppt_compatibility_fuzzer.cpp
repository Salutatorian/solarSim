#include <cstddef>
#include <cstdint>
#include <cstring>

#include "electrical_graph.h"
#include "mppt_compatibility.h"
#include "solar_panel.h"
#include "string_calculation.h"
#include "string_sizing.h"
#include "temperature_derating.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size < sizeof(solar_inverter_electrical_specs_t)) return 0;

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

    solar_inverter_electrical_specs_t inverter_specs;
    std::memcpy(&inverter_specs, data, sizeof(inverter_specs));

    solar_guid_t inv_id;
    solar_panel_guid_from_u64_pair(&inv_id, 0, 0x100);
    solar_equipment_instance_t inverter;
    solar_mppt_equipment_create_string_inverter(&inverter, &inv_id, &inverter_specs, "Fuzzer Inverter");

    solar_project_result_t project;
    solar_calculate_project(&graph, &catalog, &project);

    solar_site_design_conditions_t site;
    solar_site_design_conditions_init_default(&site);

    solar_inverter_mppt_report_t report;
    solar_mppt_compatibility_evaluate_inverter(&graph, &inverter, &project, &catalog, &site, &report);
    return 0;
}
