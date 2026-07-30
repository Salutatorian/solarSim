#include <cstdint>
#include <cmath>
#include <cstdlib>

#include "panel_port_layout.h"
#include "solar_panel.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *Data, size_t Size) {
    if (Size < 6) return 0;

    double width = static_cast<double>(Data[0]) * 20.0 + 100.0;
    double height = static_cast<double>(Data[1]) * 30.0 + 100.0;

    solar_panel_port_layout_t layout;
    solar_panel_port_layout_for_axis_aligned(width, height, &layout);

    solar_panel_definition_t def;
    solar_panel_definition_generic_400(&def);

    solar_guid_t id;
    solar_panel_guid_from_u64_pair(&id, 0xF3, 1);
    int rotation = (Data[2] % 4) * 90;
    solar_panel_instance_t panel;
    solar_panel_instance_init(&panel, &id, &def.id, 0.0, 0.0, rotation);

    solar_panel_port_layout_for_instance(&panel, &def, &layout);

    double local_x = static_cast<double>(Data[3]) * 10.0;
    double local_y = static_cast<double>(Data[4]) * 10.0;
    double radius = static_cast<double>(Data[5]) * 2.0 + 5.0;
    bool is_positive = false;
    solar_panel_port_layout_hit_test(&layout, local_x, local_y, radius, &is_positive);
    solar_panel_port_layout_terminal_spacing_mm(&layout);

    solar_panel_port_world_positions_t world;
    solar_panel_port_layout_for_instance_world(&panel, &def, &world);

    solar_panel_port_layout_t rotated;
    solar_panel_port_layout_for_rotated(width, height, rotation, &rotated);

    return 0;
}
