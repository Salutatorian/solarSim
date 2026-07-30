#include <cstdint>
#include <cstring>
#include <cmath>

#include "production_estimate.h"
#include "json_project_serializer.h"

static void estimate_from_bytes(const uint8_t *data, size_t size) {
    solar_site_conditions_t site;
    solar_site_conditions_init(&site);

    double total_dc_watts = 0.0;
    if (size >= 8) std::memcpy(&total_dc_watts, data, sizeof(double));
    if (size >= 16) std::memcpy(&site.peak_sun_hours_per_day, data + 8, sizeof(double));
    if (size >= 24) std::memcpy(&site.system_derate_factor, data + 16, sizeof(double));
    if (size >= 32) std::memcpy(&site.array_tilt_degrees, data + 24, sizeof(double));
    if (size >= 40) std::memcpy(&site.array_azimuth_degrees, data + 32, sizeof(double));
    if (size >= 48) {
        double lat = 0.0;
        std::memcpy(&lat, data + 40, sizeof(double));
        if (std::isfinite(lat)) {
            site.latitude_degrees = lat;
            site.has_latitude = true;
        }
    }
    if (size >= 56) {
        double hot = 0.0;
        std::memcpy(&hot, data + 48, sizeof(double));
        if (std::isfinite(hot)) site.hot_cell_celsius = hot;
    }

    solar_energy_estimate_t simple;
    solar_energy_estimate_simple(total_dc_watts, &site, &simple);
    solar_detailed_production_estimate_t detailed;
    solar_detailed_production_estimate(total_dc_watts, &site, &detailed);
}

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size > 65536) return 0;

    if (size > 0 && data[0] == '{') {
        solar_project_state_t state;
        solar_project_state_init(&state);
        char error[256];
        if (solar_project_json_parse_bytes(data, size, &state, error, sizeof(error))) {
            solar_detailed_production_estimate_t detailed;
            solar_project_state_get_detailed_production_estimate(&state, &detailed);
        }
        solar_project_state_clear(&state);
    } else {
        estimate_from_bytes(data, size);
    }
    return 0;
}
