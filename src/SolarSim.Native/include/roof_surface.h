#ifndef ROOF_SURFACE_H
#define ROOF_SURFACE_H

#include <stdbool.h>
#include <stddef.h>

#include "roof_geometry.h"
#include "solar_panel.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Rich roof surface and multi-surface document model.
 * Mirrors SolarSim.Domain.Roof.RoofSurface and RoofDocument.
 * Distinct from roof_geometry.h's simple polygons to capture layers,
 * visibility, setbacks, and obstacles.
 */

#define SOLAR_ROOF_NAME_LEN 64
#define SOLAR_ROOF_LABEL_LEN 64
#define SOLAR_ROOF_MAX_VERTICES 256
#define SOLAR_ROOF_MAX_OBSTACLES 32
#define SOLAR_ROOF_MAX_SURFACES 32

/* Roof obstacle classification. */
typedef enum {
    SOLAR_ROOF_OBSTACLE_VENT = 0,
    SOLAR_ROOF_OBSTACLE_CHIMNEY,
    SOLAR_ROOF_OBSTACLE_SKYLIGHT,
    SOLAR_ROOF_OBSTACLE_AC_UNIT,
    SOLAR_ROOF_OBSTACLE_ANTENNA,
    SOLAR_ROOF_OBSTACLE_CUSTOM
} solar_roof_obstacle_kind_t;

/* Axis-aligned obstacle on a roof surface. */
typedef struct {
    solar_guid_t id;
    solar_roof_obstacle_kind_t kind;
    char label[SOLAR_ROOF_LABEL_LEN];
    double x_mm;
    double y_mm;
    double width_mm;
    double height_mm;
    bool allow_overlap;
} solar_roof_obstacle_t;

/* Single roof layer with vertices, obstacles, and placement constraints. */
typedef struct {
    solar_guid_t id;
    char name[SOLAR_ROOF_NAME_LEN];
    roof_point_t vertices[SOLAR_ROOF_MAX_VERTICES];
    size_t vertex_count;
    solar_roof_obstacle_t obstacles[SOLAR_ROOF_MAX_OBSTACLES];
    size_t obstacle_count;
    bool is_visible;
    bool is_locked;
    bool is_closed;
    double setback_mm;
    bool enforce_setback;
    bool enforce_boundary;
    bool enforce_obstacles;
} solar_roof_surface_t;

/* Edge measurement returned by solar_roof_surface_edge_measurements. */
typedef struct {
    roof_point_t a;
    roof_point_t b;
    double length_mm;
} solar_roof_edge_measurement_t;

/* Multi-surface roof plan. */
typedef struct {
    solar_roof_surface_t surfaces[SOLAR_ROOF_MAX_SURFACES];
    size_t surface_count;
    solar_guid_t active_surface_id;
    bool has_active_surface;
} solar_roof_document_t;

/* Obstacle helpers. */
void solar_roof_obstacle_init(
    solar_roof_obstacle_t *obstacle,
    const solar_guid_t *id,
    solar_roof_obstacle_kind_t kind,
    double x_mm,
    double y_mm,
    double width_mm,
    double height_mm,
    const char *label,
    bool allow_overlap);

bool solar_roof_obstacle_intersects_rect(
    const solar_roof_obstacle_t *obstacle,
    double rect_x,
    double rect_y,
    double rect_w,
    double rect_h);

/* Surface lifecycle. */
void solar_roof_surface_init(solar_roof_surface_t *surface, const char *name);
void solar_roof_surface_clear(solar_roof_surface_t *surface);

bool solar_roof_surface_add_vertex(solar_roof_surface_t *surface, const roof_point_t *vertex);
bool solar_roof_surface_insert_vertex(solar_roof_surface_t *surface, size_t index, const roof_point_t *vertex);
bool solar_roof_surface_move_vertex(solar_roof_surface_t *surface, size_t index, const roof_point_t *vertex);
bool solar_roof_surface_remove_vertex(solar_roof_surface_t *surface, size_t index);

bool solar_roof_surface_try_close(solar_roof_surface_t *surface);
void solar_roof_surface_open_for_edit(solar_roof_surface_t *surface);

bool solar_roof_surface_has_roof(const solar_roof_surface_t *surface);
double solar_roof_surface_area_square_meters(const solar_roof_surface_t *surface);

bool solar_roof_surface_add_obstacle(solar_roof_surface_t *surface, const solar_roof_obstacle_t *obstacle);
bool solar_roof_surface_remove_obstacle(solar_roof_surface_t *surface, const solar_guid_t *id);
const solar_roof_obstacle_t *solar_roof_surface_find_obstacle(
    const solar_roof_surface_t *surface,
    const solar_guid_t *id);

size_t solar_roof_surface_edge_measurements(
    const solar_roof_surface_t *surface,
    solar_roof_edge_measurement_t *out_edges,
    size_t max_count);

/* Document lifecycle. */
void solar_roof_document_init(solar_roof_document_t *doc);
bool solar_roof_document_add_surface(solar_roof_document_t *doc, const solar_roof_surface_t *surface);
bool solar_roof_document_remove_surface(solar_roof_document_t *doc, const solar_guid_t *id);
bool solar_roof_document_set_active_surface(solar_roof_document_t *doc, const solar_guid_t *id);
const solar_roof_surface_t *solar_roof_document_find_surface(
    const solar_roof_document_t *doc,
    const solar_guid_t *id);
solar_roof_surface_t *solar_roof_document_ensure_active_surface(solar_roof_document_t *doc);
bool solar_roof_document_has_any_closed_surface(const solar_roof_document_t *doc);
double solar_roof_document_total_area_square_meters(const solar_roof_document_t *doc);
void solar_roof_document_clear(solar_roof_document_t *doc);

#ifdef __cplusplus
}
#endif

#endif
