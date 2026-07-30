#include <cstdint>
#include <cmath>
#include <cstdlib>

#include "racking_layout.h"
#include "solar_panel.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *Data, size_t Size) {
    if (Size < 4) return 0;

    solar_panel_definition_t definitions[4];
    solar_panel_definition_boviet_270(&definitions[0]);
    solar_panel_definition_generic_400(&definitions[1]);
    solar_panel_definition_generic_550(&definitions[2]);
    solar_panel_definition_generic_650(&definitions[3]);

    constexpr size_t max_panels = 8;
    size_t panel_count = (Size / 4) % (max_panels + 1);
    if (panel_count == 0) return 0;

    solar_panel_instance_t panels[max_panels];
    solar_guid_t id;
    solar_panel_guid_from_u64_pair(&id, 0xF1, 1);

    for (size_t i = 0; i < panel_count; i++) {
        size_t offset = i * 4;
        uint8_t def_idx = Data[offset] % 4;
        double x = static_cast<double>(Data[offset + 1]) * 250.0;
        double y = static_cast<double>(Data[offset + 2]) * 250.0;
        int rotation = (Data[offset + 3] % 4) * 90;
        solar_panel_guid_from_u64_pair(&id, 0xF1, 1 + i);
        solar_panel_instance_init(&panels[i], &id, &definitions[def_idx].id, x, y, rotation);
    }

    solar_racking_parameters_t params;
    solar_racking_parameters_defaults(&params);
    if (Size >= 2) {
        params.rafter_spacing_mm = 50.0 + static_cast<double>(Data[Size - 2]) * 20.0;
    }
    if (Size >= 3) {
        params.rail_overhang_mm = static_cast<double>(Data[Size - 3]) * 5.0;
    }

    solar_racking_layout_result_t result;
    solar_racking_layout_compute(
        panels, panel_count,
        definitions, 4,
        &params, &result);

    return 0;
}
