#include <cstdint>
#include <cmath>
#include <cstdlib>

#include "roof_surface.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *Data, size_t Size) {
    if (Size < 6) return 0;

    solar_roof_surface_t surface;
    solar_roof_surface_init(&surface, "fuzzer");

    size_t max_vertices = Size / 2;
    if (max_vertices > SOLAR_ROOF_MAX_VERTICES) max_vertices = SOLAR_ROOF_MAX_VERTICES;
    size_t vertex_count = max_vertices % 8;
    if (vertex_count < 3) vertex_count = 3;

    for (size_t i = 0; i < vertex_count; i++) {
        size_t offset = i * 2;
        double x = static_cast<double>(Data[offset]) * 100.0;
        double y = static_cast<double>(Data[offset + 1]) * 100.0;
        roof_point_t pt;
        roof_point_set(&pt, x, y);
        solar_roof_surface_add_vertex(&surface, &pt);
    }

    solar_roof_surface_try_close(&surface);
    solar_roof_surface_area_square_meters(&surface);

    solar_roof_edge_measurement_t edges[16];
    solar_roof_surface_edge_measurements(&surface, edges, 16);

    if (Size >= 10) {
        solar_roof_obstacle_t obstacle;
        solar_guid_t obs_id;
        solar_panel_guid_from_u64_pair(&obs_id, 0xF2, Data[Size - 1]);
        solar_roof_obstacle_init(
            &obstacle, &obs_id, SOLAR_ROOF_OBSTACLE_VENT,
            static_cast<double>(Data[Size - 4]) * 50.0,
            static_cast<double>(Data[Size - 3]) * 50.0,
            static_cast<double>(Data[Size - 2]) * 100.0 + 10.0,
            static_cast<double>(Data[Size - 5]) * 100.0 + 10.0,
            "vent", false);
        solar_roof_surface_add_obstacle(&surface, &obstacle);
        solar_roof_obstacle_intersects_rect(&obstacle, 0.0, 0.0, 200.0, 200.0);
    }

    solar_roof_document_t doc;
    solar_roof_document_init(&doc);
    solar_roof_document_add_surface(&doc, &surface);
    solar_roof_document_has_any_closed_surface(&doc);
    solar_roof_document_total_area_square_meters(&doc);
    solar_roof_document_ensure_active_surface(&doc);
    solar_roof_document_clear(&doc);

    return 0;
}
