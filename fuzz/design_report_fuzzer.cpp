#include <cstdint>
#include <cstring>
#include <cmath>

#include "design_report.h"
#include "json_project_serializer.h"

static void build_demo_report_from_bytes(const uint8_t *data, size_t size) {
    solar_project_state_t state;
    solar_project_state_init(&state);

    if (state.definitions.count == 0) {
        solar_project_state_clear(&state);
        return;
    }

    solar_guid_t def_id = state.definitions.definitions[0].id;
    size_t panel_count = 0;
    if (size > 0) panel_count = static_cast<size_t>(data[0] % 16) + 1;
    if (panel_count > SOLAR_MAX_COMPONENTS) panel_count = SOLAR_MAX_COMPONENTS;

    for (size_t i = 0; i < panel_count; ++i) {
        double offset = static_cast<double>(i) * 1200.0;
        double x = offset;
        double y = 0.0;
        if (size >= 2 + i * 4) {
            uint8_t xb = data[1 + (i * 4) % (size - 1)];
            uint8_t yb = data[2 + (i * 4 + 1) % (size - 1)];
            x = static_cast<double>(xb) * 200.0;
            y = static_cast<double>(yb) * 200.0;
        }
        int rotation = 0;
        if (size > 3) {
            rotation = static_cast<int>(data[3 + i % (size - 3)] % 4) * 90;
        }
        solar_project_state_add_panel(&state, &def_id, x, y, rotation, nullptr);
    }

    solar_project_state_compute_racking_layout(&state);
    design_report_t report;
    design_report_build(&state, &report);
    char html[65536];
    design_report_to_html(&report, html, sizeof(html), nullptr);
    solar_project_state_clear(&state);
}

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size > 65536) return 0;

    if (size > 0 && data[0] == '{') {
        solar_project_state_t state;
        solar_project_state_init(&state);
        char error[256];
        if (solar_project_json_parse_bytes(data, size, &state, error, sizeof(error))) {
            design_report_t report;
            design_report_build(&state, &report);
            char html[65536];
            design_report_to_html(&report, html, sizeof(html), nullptr);
        }
        solar_project_state_clear(&state);
    } else {
        build_demo_report_from_bytes(data, size);
    }
    return 0;
}
