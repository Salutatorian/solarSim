#include "roof_surface.h"

#include <cmath>
#include <cstdio>
#include <cstring>

static uint64_t g_next_guid_low = 0x3000;

static void make_guid(solar_guid_t *guid) {
    if (!guid) return;
    guid->id_high = 0;
    guid->id_low = g_next_guid_low++;
}

static void copy_string(char *dest, size_t dest_size, const char *src) {
    if (!dest || dest_size == 0) return;
    if (!src) {
        dest[0] = '\0';
        return;
    }
    size_t len = std::strlen(src);
    if (len >= dest_size) len = dest_size - 1;
    std::memcpy(dest, src, len);
    dest[len] = '\0';
}

static int find_surface_index(const solar_roof_document_t *doc, const solar_guid_t *id) {
    if (!doc || !id) return -1;
    for (size_t i = 0; i < doc->surface_count; i++) {
        if (solar_panel_guid_equals(&doc->surfaces[i].id, id)) {
            return static_cast<int>(i);
        }
    }
    return -1;
}

static int find_surface_index(solar_roof_document_t *doc, const solar_guid_t *id) {
    if (!doc || !id) return -1;
    for (size_t i = 0; i < doc->surface_count; i++) {
        if (solar_panel_guid_equals(&doc->surfaces[i].id, id)) {
            return static_cast<int>(i);
        }
    }
    return -1;
}

static int find_obstacle_index(const solar_roof_surface_t *surface, const solar_guid_t *id) {
    if (!surface || !id) return -1;
    for (size_t i = 0; i < surface->obstacle_count; i++) {
        if (solar_panel_guid_equals(&surface->obstacles[i].id, id)) {
            return static_cast<int>(i);
        }
    }
    return -1;
}

static const char *obstacle_kind_label(solar_roof_obstacle_kind_t kind) {
    switch (kind) {
        case SOLAR_ROOF_OBSTACLE_VENT: return "Vent";
        case SOLAR_ROOF_OBSTACLE_CHIMNEY: return "Chimney";
        case SOLAR_ROOF_OBSTACLE_SKYLIGHT: return "Skylight";
        case SOLAR_ROOF_OBSTACLE_AC_UNIT: return "AC Unit";
        case SOLAR_ROOF_OBSTACLE_ANTENNA: return "Antenna";
        default: return "Custom";
    }
}

void solar_roof_obstacle_init(
    solar_roof_obstacle_t *obstacle,
    const solar_guid_t *id,
    solar_roof_obstacle_kind_t kind,
    double x_mm,
    double y_mm,
    double width_mm,
    double height_mm,
    const char *label,
    bool allow_overlap) {
    if (!obstacle) return;
    std::memset(obstacle, 0, sizeof(*obstacle));
    if (id) {
        obstacle->id = *id;
    } else {
        make_guid(&obstacle->id);
    }
    obstacle->kind = kind;
    obstacle->x_mm = x_mm;
    obstacle->y_mm = y_mm;
    obstacle->width_mm = width_mm > 0.0 ? width_mm : 1.0;
    obstacle->height_mm = height_mm > 0.0 ? height_mm : 1.0;
    obstacle->allow_overlap = allow_overlap;
    if (label && label[0] != '\0') {
        copy_string(obstacle->label, SOLAR_ROOF_LABEL_LEN, label);
    } else {
        copy_string(obstacle->label, SOLAR_ROOF_LABEL_LEN, obstacle_kind_label(kind));
    }
}

bool solar_roof_obstacle_intersects_rect(
    const solar_roof_obstacle_t *obstacle,
    double rect_x,
    double rect_y,
    double rect_w,
    double rect_h) {
    if (!obstacle) return false;
    if (obstacle->allow_overlap) return false;
    return rect_x < obstacle->x_mm + obstacle->width_mm &&
           rect_x + rect_w > obstacle->x_mm &&
           rect_y < obstacle->y_mm + obstacle->height_mm &&
           rect_y + rect_h > obstacle->y_mm;
}

void solar_roof_surface_init(solar_roof_surface_t *surface, const char *name) {
    if (!surface) return;
    std::memset(surface, 0, sizeof(*surface));
    make_guid(&surface->id);
    copy_string(surface->name, SOLAR_ROOF_NAME_LEN, name ? name : "Roof");
    surface->is_visible = true;
    surface->is_locked = false;
    surface->is_closed = false;
    surface->setback_mm = 457.2;
    surface->enforce_setback = true;
    surface->enforce_boundary = true;
    surface->enforce_obstacles = true;
    surface->vertex_count = 0;
    surface->obstacle_count = 0;
}

void solar_roof_surface_clear(solar_roof_surface_t *surface) {
    if (!surface) return;
    surface->vertex_count = 0;
    surface->obstacle_count = 0;
    surface->is_closed = false;
}

bool solar_roof_surface_add_vertex(solar_roof_surface_t *surface, const roof_point_t *vertex) {
    if (!surface || !vertex) return false;
    if (surface->is_locked || surface->is_closed) return false;
    if (surface->vertex_count >= SOLAR_ROOF_MAX_VERTICES) return false;
    surface->vertices[surface->vertex_count] = *vertex;
    surface->vertex_count++;
    return true;
}

bool solar_roof_surface_insert_vertex(solar_roof_surface_t *surface, size_t index, const roof_point_t *vertex) {
    if (!surface || !vertex) return false;
    if (index > surface->vertex_count) return false;
    if (surface->vertex_count >= SOLAR_ROOF_MAX_VERTICES) return false;
    if (surface->vertex_count > index) {
        std::memmove(&surface->vertices[index + 1], &surface->vertices[index],
                     (surface->vertex_count - index) * sizeof(roof_point_t));
    }
    surface->vertices[index] = *vertex;
    surface->vertex_count++;
    return true;
}

bool solar_roof_surface_move_vertex(solar_roof_surface_t *surface, size_t index, const roof_point_t *vertex) {
    if (!surface || !vertex) return false;
    if (surface->is_locked) return false;
    if (index >= surface->vertex_count) return false;
    surface->vertices[index] = *vertex;
    return true;
}

bool solar_roof_surface_remove_vertex(solar_roof_surface_t *surface, size_t index) {
    if (!surface) return false;
    if (index >= surface->vertex_count) return false;
    if (surface->vertex_count - index - 1 > 0) {
        std::memmove(&surface->vertices[index], &surface->vertices[index + 1],
                     (surface->vertex_count - index - 1) * sizeof(roof_point_t));
    }
    surface->vertex_count--;
    if (surface->vertex_count < 3) {
        surface->is_closed = false;
    }
    return true;
}

bool solar_roof_surface_try_close(solar_roof_surface_t *surface) {
    if (!surface) return false;
    if (surface->vertex_count < 3) return false;
    surface->is_closed = true;
    return true;
}

void solar_roof_surface_open_for_edit(solar_roof_surface_t *surface) {
    if (!surface) return;
    surface->is_closed = false;
}

bool solar_roof_surface_has_roof(const solar_roof_surface_t *surface) {
    if (!surface) return false;
    return surface->is_closed && surface->vertex_count >= 3;
}

double solar_roof_surface_area_square_meters(const solar_roof_surface_t *surface) {
    if (!solar_roof_surface_has_roof(surface)) return 0.0;
    double area_sq_mm = roof_polygon_area_square_mm(surface->vertices, surface->vertex_count);
    return area_sq_mm / 1e6;
}

bool solar_roof_surface_add_obstacle(solar_roof_surface_t *surface, const solar_roof_obstacle_t *obstacle) {
    if (!surface || !obstacle) return false;
    if (surface->obstacle_count >= SOLAR_ROOF_MAX_OBSTACLES) return false;
    surface->obstacles[surface->obstacle_count] = *obstacle;
    surface->obstacle_count++;
    return true;
}

bool solar_roof_surface_remove_obstacle(solar_roof_surface_t *surface, const solar_guid_t *id) {
    if (!surface || !id) return false;
    int idx = find_obstacle_index(surface, id);
    if (idx < 0) return false;
    if (surface->obstacle_count - static_cast<size_t>(idx) - 1 > 0) {
        std::memmove(&surface->obstacles[idx], &surface->obstacles[idx + 1],
                     (surface->obstacle_count - idx - 1) * sizeof(solar_roof_obstacle_t));
    }
    surface->obstacle_count--;
    return true;
}

const solar_roof_obstacle_t *solar_roof_surface_find_obstacle(
    const solar_roof_surface_t *surface,
    const solar_guid_t *id) {
    if (!surface || !id) return NULL;
    int idx = find_obstacle_index(surface, id);
    return idx >= 0 ? &surface->obstacles[idx] : NULL;
}

size_t solar_roof_surface_edge_measurements(
    const solar_roof_surface_t *surface,
    solar_roof_edge_measurement_t *out_edges,
    size_t max_count) {
    if (!surface || !out_edges || max_count == 0) return 0;
    if (surface->vertex_count < 2) return 0;
    size_t edge_count = surface->is_closed ? surface->vertex_count : surface->vertex_count - 1;
    size_t written = 0;
    for (size_t i = 0; i < edge_count && written < max_count; i++) {
        size_t next = (i + 1) % surface->vertex_count;
        out_edges[written].a = surface->vertices[i];
        out_edges[written].b = surface->vertices[next];
        out_edges[written].length_mm = roof_point_distance(&surface->vertices[i], &surface->vertices[next]);
        written++;
    }
    return written;
}

void solar_roof_document_init(solar_roof_document_t *doc) {
    if (!doc) return;
    std::memset(doc, 0, sizeof(*doc));
}

bool solar_roof_document_add_surface(solar_roof_document_t *doc, const solar_roof_surface_t *surface) {
    if (!doc || !surface) return false;
    if (doc->surface_count >= SOLAR_ROOF_MAX_SURFACES) return false;
    if (find_surface_index(doc, &surface->id) >= 0) return false;
    doc->surfaces[doc->surface_count] = *surface;
    doc->surface_count++;
    doc->active_surface_id = surface->id;
    doc->has_active_surface = true;
    return true;
}

bool solar_roof_document_remove_surface(solar_roof_document_t *doc, const solar_guid_t *id) {
    if (!doc || !id) return false;
    int idx = find_surface_index(doc, id);
    if (idx < 0) return false;
    if (doc->surface_count - static_cast<size_t>(idx) - 1 > 0) {
        std::memmove(&doc->surfaces[idx], &doc->surfaces[idx + 1],
                     (doc->surface_count - idx - 1) * sizeof(solar_roof_surface_t));
    }
    doc->surface_count--;
    if (solar_panel_guid_is_zero(id)) {
        doc->has_active_surface = false;
    } else if (doc->has_active_surface && solar_panel_guid_equals(&doc->active_surface_id, id)) {
        if (doc->surface_count > 0) {
            doc->active_surface_id = doc->surfaces[0].id;
        } else {
            doc->has_active_surface = false;
        }
    }
    return true;
}

bool solar_roof_document_set_active_surface(solar_roof_document_t *doc, const solar_guid_t *id) {
    if (!doc || !id) return false;
    if (find_surface_index(doc, id) < 0) return false;
    doc->active_surface_id = *id;
    doc->has_active_surface = true;
    return true;
}

const solar_roof_surface_t *solar_roof_document_find_surface(
    const solar_roof_document_t *doc,
    const solar_guid_t *id) {
    if (!doc || !id) return NULL;
    int idx = find_surface_index(doc, id);
    return idx >= 0 ? &doc->surfaces[idx] : NULL;
}

solar_roof_surface_t *solar_roof_document_ensure_active_surface(solar_roof_document_t *doc) {
    if (!doc) return NULL;
    if (doc->has_active_surface) {
        int idx = find_surface_index(doc, &doc->active_surface_id);
        if (idx >= 0) return &doc->surfaces[idx];
    }
    if (doc->surface_count >= SOLAR_ROOF_MAX_SURFACES) return NULL;
    solar_roof_surface_t *surface = &doc->surfaces[doc->surface_count];
    char name[SOLAR_ROOF_NAME_LEN];
    std::snprintf(name, sizeof(name), "Roof %zu", doc->surface_count + 1);
    solar_roof_surface_init(surface, name);
    doc->surface_count++;
    doc->active_surface_id = surface->id;
    doc->has_active_surface = true;
    return surface;
}

bool solar_roof_document_has_any_closed_surface(const solar_roof_document_t *doc) {
    if (!doc) return false;
    for (size_t i = 0; i < doc->surface_count; i++) {
        if (solar_roof_surface_has_roof(&doc->surfaces[i]) && doc->surfaces[i].is_visible) {
            return true;
        }
    }
    return false;
}

double solar_roof_document_total_area_square_meters(const solar_roof_document_t *doc) {
    if (!doc) return 0.0;
    double total = 0.0;
    for (size_t i = 0; i < doc->surface_count; i++) {
        const solar_roof_surface_t *surface = &doc->surfaces[i];
        if (solar_roof_surface_has_roof(surface) && surface->is_visible) {
            total += solar_roof_surface_area_square_meters(surface);
        }
    }
    return total;
}

void solar_roof_document_clear(solar_roof_document_t *doc) {
    if (!doc) return;
    std::memset(doc, 0, sizeof(*doc));
}
