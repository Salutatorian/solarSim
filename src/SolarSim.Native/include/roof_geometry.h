#ifndef ROOF_GEOMETRY_H
#define ROOF_GEOMETRY_H

#include <stdbool.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

/* 2D roof geometry helpers ported from SolarSim.Domain.Roof.RoofGeometry.
 * Operates in integer millimeters to match the CAD domain.
 */

#define ROOF_MAX_VERTICES 256
#define ROOF_MAX_OBSTACLES 32
#define ROOF_NAME_LEN 64

typedef struct {
    double x_mm;
    double y_mm;
} roof_point_t;

typedef struct {
    roof_point_t vertices[ROOF_MAX_VERTICES];
    size_t vertex_count;
    char name[ROOF_NAME_LEN];
    double setback_mm;
    bool is_locked;
} roof_surface_t;

typedef struct {
    roof_point_t vertices[ROOF_MAX_VERTICES];
    size_t vertex_count;
    char name[ROOF_NAME_LEN];
} roof_obstacle_t;

typedef struct {
    roof_surface_t surfaces[ROOF_MAX_OBSTACLES];
    size_t surface_count;
    roof_obstacle_t obstacles[ROOF_MAX_OBSTACLES];
    size_t obstacle_count;
} roof_document_t;

void roof_point_set(roof_point_t *p, double x_mm, double y_mm);
double roof_point_distance(const roof_point_t *a, const roof_point_t *b);

roof_point_t roof_snap_orthogonal(const roof_point_t *from, const roof_point_t *raw);
roof_point_t roof_snap_draw_point(
    const roof_point_t *last,
    const roof_point_t *raw,
    const roof_point_t *existing_vertices,
    size_t existing_count,
    double axis_tolerance_mm,
    bool free_angle);
roof_point_t roof_snap_edit_vertex(
    int index,
    const roof_point_t *raw,
    const roof_point_t *vertices,
    size_t vertex_count,
    double axis_tolerance_mm,
    bool free_angle);

double roof_polygon_area_square_mm(const roof_point_t *vertices, size_t vertex_count);
bool roof_is_point_inside_polygon(const roof_point_t *point, const roof_point_t *vertices, size_t vertex_count);
double roof_distance_to_nearest_edge_mm(const roof_point_t *point, const roof_point_t *vertices, size_t vertex_count);
double roof_distance_point_to_segment_mm(const roof_point_t *p, const roof_point_t *a, const roof_point_t *b);
roof_point_t roof_project_point_to_nearest_edge(const roof_point_t *point, const roof_point_t *vertices, size_t vertex_count);

/* Panel placement helpers. */
bool roof_contains_panel_rect(
    const roof_surface_t *surface,
    const roof_point_t *center,
    double width_mm,
    double height_mm,
    int rotation_degrees);
bool roof_panel_overlaps_obstacle(
    const roof_document_t *doc,
    const roof_point_t *center,
    double width_mm,
    double height_mm,
    int rotation_degrees);

void roof_surface_init(roof_surface_t *surface, const char *name);
bool roof_surface_add_vertex(roof_surface_t *surface, const roof_point_t *vertex);
bool roof_surface_is_valid(const roof_surface_t *surface);

void roof_document_init(roof_document_t *doc);
bool roof_document_add_surface(roof_document_t *doc, const roof_surface_t *surface);
bool roof_document_add_obstacle(roof_document_t *doc, const roof_obstacle_t *obstacle);

#ifdef __cplusplus
}
#endif

#endif
