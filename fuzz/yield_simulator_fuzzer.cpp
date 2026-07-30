#include <cstddef>
#include <cstdint>

#include "yield_simulator.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size < 80) return 0;

    yield_system_t system;
    yield_site_t site;
    std::memcpy(&system, data, sizeof(system));
    std::memcpy(&site, data + sizeof(system), sizeof(site));

    if (system.system_dc_kw <= 0.0) system.system_dc_kw = 5.0;
    if (system.system_ac_kw <= 0.0) system.system_ac_kw = 4.5;
    if (system.inverter_efficiency <= 0.0 || system.inverter_efficiency > 1.0) system.inverter_efficiency = 0.97;
    if (system.dc_ac_ratio <= 0.0) system.dc_ac_ratio = 1.1;
    if (system.derate < 0.0 || system.derate > 1.0) system.derate = 0.86;
    if (system.albedo < 0.0 || system.albedo > 1.0) system.albedo = 0.2;

    if (site.latitude_deg < -90.0 || site.latitude_deg > 90.0) site.latitude_deg = -33.0;
    if (site.longitude_deg < -180.0 || site.longitude_deg > 180.0) site.longitude_deg = 151.0;
    if (site.psh_annual < 0.0) site.psh_annual = 4.5;
    if (site.system_derate < 0.0 || site.system_derate > 1.0) site.system_derate = 0.86;

    yield_result_t result;
    yield_simulate_annual(&system, &site, &result);
    return 0;
}
