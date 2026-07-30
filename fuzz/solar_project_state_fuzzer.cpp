#include <cstdint>
#include <cstring>
#include <cmath>

#include "solar_project_state.h"
#include "json_project_serializer.h"

static void mutate_state_from_bytes(const uint8_t *data, size_t size) {
    solar_project_state_t state;
    solar_project_state_init(&state);

    if (state.definitions.count == 0) {
        solar_project_state_clear(&state);
        return;
    }

    solar_guid_t def_id = state.definitions.definitions[0].id;
    size_t op_count = 0;
    if (size > 0) op_count = static_cast<size_t>(data[0] % 32) + 1;

    for (size_t i = 0; i < op_count && i + 1 < size; ++i) {
        uint8_t op = data[i + 1] % 4;
        if (op == 0) {
            double x = 0.0, y = 0.0;
            if (i + 8 < size) {
                uint8_t xb = data[i + 2];
                uint8_t yb = data[i + 3];
                x = static_cast<double>(xb) * 250.0;
                y = static_cast<double>(yb) * 250.0;
            }
            solar_project_state_add_panel(&state, &def_id, x, y, 0, nullptr);
        } else if (op == 1) {
            solar_project_state_create_demo_rectangular_roof(&state, 12000.0, 8000.0, 457.2);
        } else if (op == 2) {
            solar_project_state_compute_racking_layout(&state);
        } else if (op == 3) {
            solar_project_state_calculate(&state);
        }
    }

    solar_project_state_clear(&state);
}

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size > 65536) return 0;

    if (size > 0 && data[0] == '{') {
        solar_project_state_t state;
        solar_project_state_init(&state);
        char error[256];
        solar_project_json_parse_bytes(data, size, &state, error, sizeof(error));
        solar_project_state_compute_racking_layout(&state);
        solar_project_state_calculate(&state);
        solar_project_state_clear(&state);
    } else {
        mutate_state_from_bytes(data, size);
    }
    return 0;
}
