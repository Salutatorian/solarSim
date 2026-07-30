#include <cstddef>
#include <cstdint>
#include <cstring>

#include "roof_geometry.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size < 8) return 0;

    uint32_t vertex_count = data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
    if (vertex_count > 64 || vertex_count < 3) return 0;

    roof_surface_t surface;
    roof_surface_init(&surface, "fuzz");

    for (uint32_t i = 0; i < vertex_count && (i + 1) * 16 + 8 <= size; i++) {
        const uint8_t *p = data + 8 + i * 16;
        double x, y;
        std::memcpy(&x, p, sizeof(double));
        std::memcpy(&y, p + 8, sizeof(double));
        roof_point_t point = {x, y};
        roof_surface_add_vertex(&surface, &point);
    }

    if (roof_surface_is_valid(&surface)) {
        roof_polygon_area_square_mm(surface.vertices, surface.vertex_count);
        roof_point_t probe = {0.0, 0.0};
        roof_is_point_inside_polygon(&probe, surface.vertices, surface.vertex_count);
        roof_distance_to_nearest_edge_mm(&probe, surface.vertices, surface.vertex_count);
    }
    return 0;
}
