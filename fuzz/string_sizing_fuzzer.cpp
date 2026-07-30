#include <cstddef>
#include <cstdint>
#include <cstring>

#include "solar_panel.h"
#include "string_sizing.h"
#include "temperature_derating.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size < sizeof(solar_panel_definition_t) + sizeof(solar_inverter_electrical_specs_t)) return 0;

    solar_panel_definition_t panel;
    std::memcpy(&panel, data, sizeof(panel));
    panel.manufacturer[sizeof(panel.manufacturer) - 1] = '\0';
    panel.model[sizeof(panel.model) - 1] = '\0';

    solar_inverter_electrical_specs_t inverter;
    std::memcpy(&inverter, data + sizeof(panel), sizeof(inverter));

    solar_site_design_conditions_t site;
    solar_site_design_conditions_init_default(&site);
    if (size >= sizeof(panel) + sizeof(inverter) + sizeof(site)) {
        std::memcpy(&site, data + sizeof(panel) + sizeof(inverter), sizeof(site));
        site.location_name[sizeof(site.location_name) - 1] = '\0';
    }

    solar_string_sizing_advice_t advice;
    solar_string_sizing_advise(&panel, &inverter, &site, &advice);

    solar_string_sizing_advise_count(&panel, &inverter, &site, 12, &advice);
    (void)solar_string_sizing_clamp_module_count(14, &advice);
    return 0;
}
