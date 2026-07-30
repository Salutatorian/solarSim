#include <cstddef>
#include <cstdint>

#include "mc4_connector.h"
#include "wire_route.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size < 4) return 0;

    mc4_connector_t connectors[16];
    size_t count = data[0] % 16;
    if (count < 2) count = 2;
    for (size_t i = 0; i < count && i * 3 + 4 < size; i++) {
        connectors[i].interface_type = static_cast<solar_connector_interface_t>(data[i * 3 + 1] % 3);
        connectors[i].polarity = static_cast<solar_polarity_t>(data[i * 3 + 2] % 2);
        connectors[i].family = static_cast<solar_connector_family_id_t>(data[i * 3 + 3] % SOLAR_CONNECTOR_FAMILY_COUNT);
    }

    mc4_series_result_t result;
    mc4_validate_series(connectors, count, &result);

    mc4_check_wire_fit(SOLAR_CONNECTOR_FAMILY_MC4, static_cast<wire_awg_t>(10), NULL, 0);
    return 0;
}
