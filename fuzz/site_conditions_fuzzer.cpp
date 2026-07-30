#include <cstdint>
#include <cmath>
#include <cstdlib>
#include <cstring>

#include "site_conditions.h"

static double bytes_to_double(const uint8_t *Data, size_t Size, size_t offset) {
    if (offset + sizeof(double) > Size) {
        return static_cast<double>(offset % 100);
    }
    double value = 0.0;
    std::memcpy(&value, Data + offset, sizeof(double));
    if (!std::isfinite(value)) return 0.0;
    return value;
}

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *Data, size_t Size) {
    if (Size < 1) return 0;

    solar_site_design_conditions_t conditions;
    solar_site_conditions_init(&conditions);

    size_t preset_count = solar_site_climate_preset_count();
    size_t preset_index = Data[0] % (preset_count + 1);
    if (preset_index < preset_count) {
        const solar_site_climate_preset_t *preset = solar_site_climate_preset_at(preset_index);
        if (preset) {
            solar_site_conditions_apply_preset(&conditions, preset);
        }
    }

    if (Size >= 2) {
        const solar_site_climate_preset_t *by_id = solar_site_climate_preset_by_id("sydney");
        if (by_id) {
            solar_site_conditions_apply_preset(&conditions, by_id);
        }
    }

    solar_site_design_conditions_t clone;
    solar_site_conditions_clone(&conditions, &clone);

    double stc_kw = bytes_to_double(Data, Size, 1);
    solar_site_estimate_annual_kwh(stc_kw, conditions.peak_sun_hours_per_day, conditions.system_derate_factor);

    solar_site_normalize_tilt(conditions.array_tilt_degrees);
    solar_site_normalize_azimuth(conditions.array_azimuth_degrees);

    return 0;
}
