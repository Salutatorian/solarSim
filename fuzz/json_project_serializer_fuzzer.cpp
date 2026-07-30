#include <cstddef>
#include <cstdint>
#include <cstring>

#include "json_project_serializer.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size > 65536) return 0;

    solar_project_state_t state;
    solar_project_state_init(&state);

    char error[256];
    if (solar_project_json_parse_bytes(data, size, &state, error, sizeof(error))) {
        char output[4096];
        size_t output_length = 0;
        solar_project_json_serialize(&state, output, sizeof(output), &output_length);
    }

    solar_project_state_clear(&state);
    return 0;
}
