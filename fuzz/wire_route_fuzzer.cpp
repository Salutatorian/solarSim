#include <cstddef>
#include <cstdint>

#include "wire_route.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size < 4) return 0;

    wire_route_t route;
    wire_route_init(&route, static_cast<wire_awg_t>(data[0]), WIRE_MATERIAL_COPPER);

    for (size_t i = 4; i + 15 < size; i += 16) {
        double x, y;
        std::memcpy(&x, data + i, sizeof(double));
        std::memcpy(&y, data + i + 8, sizeof(double));
        wire_route_add_point_xy(&route, x, y);
    }

    wire_route_result_t result;
    wire_route_result_calculate(&route, static_cast<double>(data[1]), &result);
    return 0;
}
