#include <cstddef>
#include <cstdint>
#include <cstring>

#include "wire_gauge_format.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size < 4) return 0;

    int gauge_code = 0;
    std::memcpy(&gauge_code, data, sizeof(int));
    solar_wire_gauge_awg_t gauge = solar_wire_gauge_from_int(gauge_code);

    double current = 0.0;
    double length = 0.0;
    if (size >= 12) {
        std::memcpy(&current, data + 4, sizeof(double));
        std::memcpy(&length, data + 12, sizeof(double));
    }

    char buf[32];
    solar_wire_gauge_to_display(gauge, buf, sizeof(buf));
    (void)solar_wire_gauge_is_valid(gauge);
    (void)solar_wire_copper_ohms_per_1000ft(gauge);
    (void)solar_wire_aluminum_ohms_per_1000ft(gauge);
    (void)solar_wire_copper_ampacity_amps(gauge);
    (void)solar_wire_recommend_pv_string_gauge(current, length);
    (void)solar_wire_recommend_battery_gauge(current, current * 1.25);

    solar_wire_properties_t props;
    std::memset(&props, 0, sizeof(props));
    props.gauge = gauge;
    std::memcpy(props.material, "Copper", 6);
    props.material[6] = '\0';
    std::memcpy(props.wire_type, "PV wire", 7);
    props.wire_type[7] = '\0';
    std::memcpy(props.color, "Black", 5);
    props.color[5] = '\0';
    char out[128];
    solar_wire_properties_to_display(&props, out, sizeof(out));
    return 0;
}
