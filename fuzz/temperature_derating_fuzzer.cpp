#include <cstddef>
#include <cstdint>
#include <cstring>

#include "solar_panel.h"
#include "temperature_derating.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size < sizeof(solar_panel_definition_t) + sizeof(solar_site_design_conditions_t)) return 0;

    solar_panel_definition_t panel;
    std::memcpy(&panel, data, sizeof(panel));
    panel.manufacturer[sizeof(panel.manufacturer) - 1] = '\0';
    panel.model[sizeof(panel.model) - 1] = '\0';
    panel.connector_family[sizeof(panel.connector_family) - 1] = '\0';

    solar_site_design_conditions_t site;
    std::memcpy(&site, data + sizeof(panel), sizeof(site));
    site.location_name[sizeof(site.location_name) - 1] = '\0';

    solar_module_temp_report_t report;
    solar_derate_module(&panel, &site, &report);

    /* Also exercise series-string derating with a synthetic array of pointers. */
    const solar_panel_definition_t *modules[4] = { &panel, &panel, &panel, &panel };
    solar_string_temp_report_t string_report;
    solar_derate_string(modules, 4, &site, &string_report);

    (void)solar_annual_energy_estimate_kwh(panel.pmax_watts / 1000.0, &site);
    return 0;
}
