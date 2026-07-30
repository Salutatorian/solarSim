#include <cstddef>
#include <cstdint>
#include <cstring>

#include "string_calculation.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size < 48) return 0;

    solar_panel_definition_t def;
    solar_panel_definition_boviet_270(&def);

    solar_string_calculation_input_t input;
    std::memcpy(&input, data, sizeof(input));
    input.module_count = 2 + (data[0] % 20);
    input.string_count = 1 + (data[1] % 4);
    input.inverter_min_mppt_volts = 100.0 + (data[2] * 2.0);
    input.inverter_max_mppt_volts = input.inverter_min_mppt_volts + 100.0 + (data[3] * 5.0);
    input.inverter_max_dc_volts = input.inverter_max_mppt_volts + 50.0;
    input.min_ambient_c = -20.0 + (data[4] % 50);
    input.hot_cell_c = input.min_ambient_c + 20.0 + (data[5] % 40);

    solar_string_calculation_result_t result;
    solar_string_calculation_evaluate(&input, &result);
    return 0;
}
