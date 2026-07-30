#include <cstddef>
#include <cstdint>
#include <cstring>

#include "voltage_drop.h"
#include "wire_gauge_format.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size < 24) return 0;

    voltage_drop_input_t input;
    std::memcpy(&input, data, sizeof(input));
    if (input.voltage <= 0.0) input.voltage = 400.0;
    if (input.current_amps <= 0.0) input.current_amps = 10.0;
    if (input.length_mm <= 0.0) input.length_mm = 20000.0;
    if (input.gauge < WIRE_AWG_14 || input.gauge > WIRE_AWG_0000) input.gauge = WIRE_AWG_10;

    voltage_drop_result_t result;
    voltage_drop_calculate(&input, &result);
    return 0;
}
