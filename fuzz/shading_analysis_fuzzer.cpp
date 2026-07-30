#include <cstddef>
#include <cstdint>

#include "shading_analysis.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size < 48) return 0;

    shading_field_layout_t layout;
    std::memcpy(&layout, data, sizeof(layout));
    if (layout.panel_height_mm <= 0.0) layout.panel_height_mm = 1000.0;
    if (layout.row_spacing_mm <= 0.0) layout.row_spacing_mm = 2000.0;
    if (layout.row_tilt_deg < 0.0) layout.row_tilt_deg = 20.0;
    if (layout.ground_clearance_mm < 0.0) layout.ground_clearance_mm = 300.0;

    solar_position_t sun;
    std::memcpy(&sun, data + 32, sizeof(solar_position_t));
    if (sun.elevation_deg < -90.0 || sun.elevation_deg > 90.0) sun.elevation_deg = 45.0;

    shading_result_t result;
    shading_calculate_row_to_row(&layout, &sun, &result);

    shading_annual_loss_factor(
        &layout,
        layout.latitude_deg,
        0.0,
        0.0);
    return 0;
}
