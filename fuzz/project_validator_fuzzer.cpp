#include <cstddef>
#include <cstdint>
#include <cstring>

#include "project_validator.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size < 8) return 0;

    solar_project_state_t state;
    solar_project_state_init(&state);

    state.site.min_ambient_c = -10.0 + (data[0] % 60);
    state.site.hot_cell_c = 40.0 + (data[1] % 60);
    state.site.peak_sun_hours = 2.0 + (data[2] % 60) / 10.0;
    state.site.system_derate = 0.6 + (data[3] % 40) / 100.0;
    state.site.array_tilt_deg = data[4] % 90;
    state.site.array_azimuth_deg = data[5] % 360;

    project_validator_result_t result;
    project_validator_validate(&state, &result);

    solar_project_state_clear(&state);
    return 0;
}
