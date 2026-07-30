#include "roof_geometry.h"

#include <algorithm>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <cmath>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

static void copy_name(char *dest, size_t dest_size, const char *src) {
    if (!src || dest_size == 0) return;
    size_t len = std::strlen(src);
    if (len >= dest_size) len = dest_size - 1;
    std::memcpy(dest, src, len);
    dest[len] = '\0';
}

void roof_point_set(roof_point_t *p, double x_mm, double y_mm) {
    if (!p) return;
    p->x_mm = x_mm;
    p->y_mm = y_mm;
}

double roof_point_distance(const roof_point_t *a, const roof_point_t *b) {
    if (!a || !b) return 0.0;
    double dx = a->x_mm - b->x_mm;
    double dy = a->y_mm - b->y_mm;
    return std::sqrt(dx * dx + dy * dy);
}

roof_point_t roof_snap_orthogonal(const roof_point_t *from, const roof_point_t *raw) {
    roof_point_t result = *raw;
    if (!from || !raw) return result;
    double dx = raw->x_mm - from->x_mm;
    double dy = raw->y_mm - from->y_mm;
    if (std::fabs(dx) >= std::fabs(dy)) {
        result.y_mm = from->y_mm;
    } else {
        result.x_mm = from->x_mm;
    }
    return result;
}

roof_point_t roof_snap_draw_point(
    const roof_point_t *last,
    const roof_point_t *raw,
    const roof_point_t *existing_vertices,
    size_t existing_count,
    double axis_tolerance_mm,
    bool free_angle) {
    roof_point_t result = free_angle ? *raw : roof_snap_orthogonal(last, raw);
    if (!existing_vertices || existing_count == 0 || axis_tolerance_mm <= 0.0) {
        return result;
    }

    double first_tol = existing_count >= 3 ? axis_tolerance_mm * 2.0 : axis_tolerance_mm;
    const roof_point_t *first = &existing_vertices[0];
    if (std::fabs(result.x_mm - first->x_mm) <= first_tol) {
        result.x_mm = first->x_mm;
    }
    if (std::fabs(result.y_mm - first->y_mm) <= first_tol) {
        result.y_mm = first->y_mm;
    }

    for (size_t i = 1; i < existing_count; i++) {
        const roof_point_t *v = &existing_vertices[i];
        if (std::fabs(v->x_mm - last->x_mm) < 0.01 && std::fabs(v->y_mm - last->y_mm) < 0.01) {
            continue;
        }
        if (std::fabs(result.x_mm - v->x_mm) <= axis_tolerance_mm) {
            result.x_mm = v->x_mm;
        }
        if (std::fabs(result.y_mm - v->y_mm) <= axis_tolerance_mm) {
            result.y_mm = v->y_mm;
        }
    }
    return result;
}

roof_point_t roof_snap_edit_vertex(
    int index,
    const roof_point_t *raw,
    const roof_point_t *vertices,
    size_t vertex_count,
    double axis_tolerance_mm,
    bool free_angle) {
    if (!vertices || free_angle || vertex_count < 2 || index < 0 || (size_t)index >= vertex_count) {
        return raw ? *raw : (roof_point_t){0.0, 0.0};
    }
    size_t n = vertex_count;
    size_t prev_idx = (index - 1 + n) % n;
    size_t next_idx = (index + 1) % n;
    roof_point_t prev = vertices[prev_idx];
    roof_point_t next = vertices[next_idx];

    double dual_tol = std::max(axis_tolerance_mm * 1.75, 40.0);
    roof_point_t corner_a = {prev.x_mm, next.y_mm};
    roof_point_t corner_b = {next.x_mm, prev.y_mm};

    if (roof_point_distance(raw, &corner_a) <= dual_tol) {
        return corner_a;
    }
    if (roof_point_distance(raw, &corner_b) <= dual_tol) {
        return corner_b;
    }

    roof_point_t from_prev = roof_snap_orthogonal(&prev, raw);
    roof_point_t from_next = roof_snap_orthogonal(&next, raw);
    roof_point_t point = roof_point_distance(raw, &from_prev) <= roof_point_distance(raw, &from_next) ? from_prev : from_next;

    for (size_t i = 0; i < n; i++) {
        if (i == (size_t)index) continue;
        const roof_point_t *v = &vertices[i];
        if (std::fabs(point.x_mm - v->x_mm) <= axis_tolerance_mm) {
            point.x_mm = v->x_mm;
        }
        if (std::fabs(point.y_mm - v->y_mm) <= axis_tolerance_mm) {
            point.y_mm = v->y_mm;
        }
    }
    return point;
}

double roof_polygon_area_square_mm(const roof_point_t *vertices, size_t vertex_count) {
    if (!vertices || vertex_count < 3) return 0.0;
    double sum = 0.0;
    for (size_t i = 0; i < vertex_count; i++) {
        const roof_point_t *a = &vertices[i];
        const roof_point_t *b = &vertices[(i + 1) % vertex_count];
        sum += (a->x_mm * b->y_mm) - (b->x_mm * a->y_mm);
    }
    return std::fabs(sum) * 0.5;
}

bool roof_is_point_inside_polygon(const roof_point_t *point, const roof_point_t *vertices, size_t vertex_count) {
    if (!point || !vertices || vertex_count < 3) return false;
    bool inside = false;
    size_t j = vertex_count - 1;
    for (size_t i = 0; i < vertex_count; i++) {
        const roof_point_t *pi = &vertices[i];
        const roof_point_t *pj = &vertices[j];
        bool intersect = ((pi->y_mm > point->y_mm) != (pj->y_mm > point->y_mm)) &&
                         (point->x_mm < (pj->x_mm - pi->x_mm) * (point->y_mm - pi->y_mm) / (pj->y_mm - pi->y_mm + 1e-12) + pi->x_mm);
        if (intersect) inside = !inside;
        j = i;
    }
    return inside;
}

double roof_distance_point_to_segment_mm(const roof_point_t *p, const roof_point_t *a, const roof_point_t *b) {
    if (!p || !a || !b) return 0.0;
    double dx = b->x_mm - a->x_mm;
    double dy = b->y_mm - a->y_mm;
    if (std::fabs(dx) < 1e-9 && std::fabs(dy) < 1e-9) {
        return roof_point_distance(p, a);
    }
    double t = ((p->x_mm - a->x_mm) * dx + (p->y_mm - a->y_mm) * dy) / (dx * dx + dy * dy);
    if (t < 0.0) t = 0.0;
    if (t > 1.0) t = 1.0;
    roof_point_t proj = {a->x_mm + t * dx, a->y_mm + t * dy};
    return roof_point_distance(p, &proj);
}

double roof_distance_to_nearest_edge_mm(const roof_point_t *point, const roof_point_t *vertices, size_t vertex_count) {
    if (!point || !vertices || vertex_count < 2) return 1e308;
    double min_dist = 1e308;
    for (size_t i = 0; i < vertex_count; i++) {
        const roof_point_t *a = &vertices[i];
        const roof_point_t *b = &vertices[(i + 1) % vertex_count];
        double d = roof_distance_point_to_segment_mm(point, a, b);
        if (d < min_dist) min_dist = d;
    }
    return min_dist;
}

roof_point_t roof_project_point_to_nearest_edge(const roof_point_t *point, const roof_point_t *vertices, size_t vertex_count) {
    if (!point || !vertices || vertex_count < 2) return point ? *point : (roof_point_t){0.0, 0.0};
    roof_point_t best = *point;
    double best_dist = 1e308;
    for (size_t i = 0; i < vertex_count; i++) {
        const roof_point_t *a = &vertices[i];
        const roof_point_t *b = &vertices[(i + 1) % vertex_count];
        double dx = b->x_mm - a->x_mm;
        double dy = b->y_mm - a->y_mm;
        if (std::fabs(dx) < 1e-9 && std::fabs(dy) < 1e-9) continue;
        double t = ((point->x_mm - a->x_mm) * dx + (point->y_mm - a->y_mm) * dy) / (dx * dx + dy * dy);
        if (t < 0.0) t = 0.0;
        if (t > 1.0) t = 1.0;
        roof_point_t proj = {a->x_mm + t * dx, a->y_mm + t * dy};
        double d = roof_point_distance(point, &proj);
        if (d < best_dist) {
            best_dist = d;
            best = proj;
        }
    }
    return best;
}

static void rotate_rect_corners(
    const roof_point_t *center,
    double width_mm,
    double height_mm,
    int rotation_degrees,
    roof_point_t out_corners[4]) {
    double rad = rotation_degrees * M_PI / 180.0;
    double cos_a = std::cos(rad);
    double sin_a = std::sin(rad);
    double hw = width_mm * 0.5;
    double hh = height_mm * 0.5;
    double local[4][2] = {
        {-hw, -hh},
        {hw, -hh},
        {hw, hh},
        {-hw, hh}
    };
    for (int i = 0; i < 4; i++) {
        double lx = local[i][0];
        double ly = local[i][1];
        double rx = lx * cos_a - ly * sin_a;
        double ry = lx * sin_a + ly * cos_a;
        out_corners[i].x_mm = center->x_mm + rx;
        out_corners[i].y_mm = center->y_mm + ry;
    }
}

bool roof_contains_panel_rect(
    const roof_surface_t *surface,
    const roof_point_t *center,
    double width_mm,
    double height_mm,
    int rotation_degrees) {
    if (!surface || !center || !surface->vertices || surface->vertex_count < 3) return false;
    roof_point_t corners[4];
    rotate_rect_corners(center, width_mm, height_mm, rotation_degrees, corners);
    for (int i = 0; i < 4; i++) {
        if (!roof_is_point_inside_polygon(&corners[i], surface->vertices, surface->vertex_count)) {
            return false;
        }
    }
    return true;
}

bool roof_panel_overlaps_obstacle(
    const roof_document_t *doc,
    const roof_point_t *center,
    double width_mm,
    double height_mm,
    int rotation_degrees) {
    if (!doc || !center) return false;
    roof_point_t corners[4];
    rotate_rect_corners(center, width_mm, height_mm, rotation_degrees, corners);
    for (size_t o = 0; o < doc->obstacle_count; o++) {
        const roof_obstacle_t *obs = &doc->obstacles[o];
        if (obs->vertex_count < 3) continue;
        for (int i = 0; i < 4; i++) {
            if (roof_is_point_inside_polygon(&corners[i], obs->vertices, obs->vertex_count)) {
                return true;
            }
        }
    }
    return false;
}

void roof_surface_init(roof_surface_t *surface, const char *name) {
    if (!surface) return;
    std::memset(surface, 0, sizeof(*surface));
    copy_name(surface->name, ROOF_NAME_LEN, name ? name : "Roof");
    surface->setback_mm = 0.0;
    surface->is_locked = false;
}

bool roof_surface_add_vertex(roof_surface_t *surface, const roof_point_t *vertex) {
    if (!surface || !vertex) return false;
    if (surface->vertex_count >= ROOF_MAX_VERTICES) return false;
    surface->vertices[surface->vertex_count] = *vertex;
    surface->vertex_count++;
    return true;
}

bool roof_surface_is_valid(const roof_surface_t *surface) {
    if (!surface) return false;
    if (surface->vertex_count < 3) return false;
    double area = roof_polygon_area_square_mm(surface->vertices, surface->vertex_count);
    return area > 1.0;
}

void roof_document_init(roof_document_t *doc) {
    if (!doc) return;
    std::memset(doc, 0, sizeof(*doc));
}

bool roof_document_add_surface(roof_document_t *doc, const roof_surface_t *surface) {
    if (!doc || !surface) return false;
    if (doc->surface_count >= ROOF_MAX_OBSTACLES) return false;
    doc->surfaces[doc->surface_count] = *surface;
    doc->surface_count++;
    return true;
}

bool roof_document_add_obstacle(roof_document_t *doc, const roof_obstacle_t *obstacle) {
    if (!doc || !obstacle) return false;
    if (doc->obstacle_count >= ROOF_MAX_OBSTACLES) return false;
    doc->obstacles[doc->obstacle_count] = *obstacle;
    doc->obstacle_count++;
    return true;
}
