#include "project_file.h"

#include <cstdarg>
#include <cstdint>
#include <cstring>
#include <cmath>

#define PROJECT_VERSION 1
#define PROJECT_MAX_DEFINITIONS 256
#define PROJECT_MAX_COMPONENTS 256
#define PROJECT_MAX_SURFACES 32
#define PROJECT_MAX_OBSTACLES 32
#define PROJECT_MAX_TAGS 64

static void read_u32(const uint8_t *p, uint32_t *out) {
    *out = (uint32_t)p[0] |
           ((uint32_t)p[1] << 8) |
           ((uint32_t)p[2] << 16) |
           ((uint32_t)p[3] << 24);
}

static void read_f64(const uint8_t *p, double *out) {
    std::memcpy(out, p, sizeof(double));
}

static void copy_fixed_string(char *dest, size_t dest_size, const uint8_t *src, size_t src_len) {
    size_t len = src_len;
    if (len >= dest_size) len = dest_size - 1;
    std::memcpy(dest, src, len);
    dest[len] = '\0';
}

static bool advance(const uint8_t **p, const uint8_t *end, size_t n) {
    if (*p + n > end) return false;
    *p += n;
    return true;
}

static bool read_u32_adv(const uint8_t **p, const uint8_t *end, uint32_t *out) {
    if (*p + 4 > end) return false;
    read_u32(*p, out);
    *p += 4;
    return true;
}

static bool read_f64_adv(const uint8_t **p, const uint8_t *end, double *out) {
    if (*p + 8 > end) return false;
    read_f64(*p, out);
    *p += 8;
    return true;
}

void solar_project_init(solar_project_t *project) {
    if (!project) return;
    std::memset(project, 0, sizeof(*project));
}

bool solar_project_parse_header(const uint8_t *data, size_t size, size_t *out_header_bytes, solar_project_header_t *out_header) {
    if (!data || !out_header || size < sizeof(solar_project_header_t)) return false;
    std::memcpy(out_header, data, sizeof(solar_project_header_t));
    if (std::memcmp(out_header->magic, PROJECT_MAGIC, 4) != 0) return false;
    if (out_header->version != PROJECT_VERSION) return false;
    if (out_header->module_count > PROJECT_MAX_COMPONENTS) return false;
    if (out_header->connection_count > SOLAR_MAX_CONNECTIONS) return false;
    if (out_header->surface_count > PROJECT_MAX_SURFACES) return false;
    if (out_header->obstacle_count > PROJECT_MAX_OBSTACLES) return false;
    if (out_header->tag_count > PROJECT_MAX_TAGS) return false;
    if (out_header_bytes) *out_header_bytes = sizeof(solar_project_header_t);
    return true;
}

bool solar_project_parse_definition(const uint8_t **data, const uint8_t *end, solar_panel_definition_t *out_def) {
    if (!data || !out_def || !*data) return false;
    solar_guid_t id;
    if (!advance(data, end, sizeof(id))) return false;
    std::memcpy(&id, *data - sizeof(id), sizeof(id));

    uint32_t name_len, manufacturer_len, model_len, connector_len;
    if (!read_u32_adv(data, end, &name_len)) return false;
    if (name_len > SOLAR_MAX_NAME_LEN) return false;
    if (!advance(data, end, name_len)) return false;
    const uint8_t *name_start = *data - name_len;

    if (!read_u32_adv(data, end, &manufacturer_len)) return false;
    if (manufacturer_len > SOLAR_MANUFACTURER_LEN) return false;
    if (!advance(data, end, manufacturer_len)) return false;
    const uint8_t *manufacturer_start = *data - manufacturer_len;

    if (!read_u32_adv(data, end, &model_len)) return false;
    if (model_len > SOLAR_MODEL_LEN) return false;
    if (!advance(data, end, model_len)) return false;
    const uint8_t *model_start = *data - model_len;

    if (!read_u32_adv(data, end, &connector_len)) return false;
    if (connector_len > SOLAR_CONNECTOR_FAMILY_LEN) return false;
    if (!advance(data, end, connector_len)) return false;
    const uint8_t *connector_start = *data - connector_len;

    double electrical[6];
    for (int i = 0; i < 6; i++) {
        if (!read_f64_adv(data, end, &electrical[i])) return false;
    }

    double width, height, depth;
    if (!read_f64_adv(data, end, &width)) return false;
    if (!read_f64_adv(data, end, &height)) return false;
    if (!read_f64_adv(data, end, &depth)) return false;

    double temp_voc, temp_pmax;
    if (!read_f64_adv(data, end, &temp_voc)) return false;
    if (!read_f64_adv(data, end, &temp_pmax)) return false;

    double pos_lead, neg_lead;
    if (!read_f64_adv(data, end, &pos_lead)) return false;
    if (!read_f64_adv(data, end, &neg_lead)) return false;

    uint32_t is_custom;
    if (!read_u32_adv(data, end, &is_custom)) return false;

    std::memset(out_def, 0, sizeof(*out_def));
    out_def->id = id;
    copy_fixed_string(out_def->manufacturer, SOLAR_MANUFACTURER_LEN, manufacturer_start, manufacturer_len);
    copy_fixed_string(out_def->model, SOLAR_MODEL_LEN, model_start, model_len);
    copy_fixed_string(out_def->connector_family, SOLAR_CONNECTOR_FAMILY_LEN, connector_start, connector_len);
    out_def->pmax_watts = electrical[0];
    out_def->vmp_volts = electrical[1];
    out_def->imp_amps = electrical[2];
    out_def->voc_volts = electrical[3];
    out_def->isc_amps = electrical[4];
    out_def->temp_coeff_voc_pct_per_c = temp_voc;
    out_def->temp_coeff_pmax_pct_per_c = temp_pmax;
    out_def->width_mm = width;
    out_def->height_mm = height;
    out_def->depth_mm = depth;
    out_def->positive_lead_length_mm = pos_lead;
    out_def->negative_lead_length_mm = neg_lead;
    out_def->is_custom = is_custom != 0;
    return true;
}

bool solar_project_parse_graph(const uint8_t **data, const uint8_t *end, solar_electrical_graph_t *out_graph) {
    if (!data || !out_graph || !*data) return false;
    uint32_t panel_count;
    if (!read_u32_adv(data, end, &panel_count)) return false;
    if (panel_count > SOLAR_MAX_COMPONENTS) return false;

    solar_electrical_graph_init(out_graph);

    for (uint32_t i = 0; i < panel_count; i++) {
        solar_guid_t id;
        solar_guid_t def_id;
        double x, y;
        int32_t rotation;
        if (!advance(data, end, sizeof(id))) return false;
        std::memcpy(&id, *data - sizeof(id), sizeof(id));
        if (!advance(data, end, sizeof(def_id))) return false;
        std::memcpy(&def_id, *data - sizeof(def_id), sizeof(def_id));
        if (!read_f64_adv(data, end, &x)) return false;
        if (!read_f64_adv(data, end, &y)) return false;
        if (!read_u32_adv(data, end, (uint32_t*)&rotation)) return false;

        solar_panel_instance_t panel;
        solar_panel_instance_init(&panel, &id, &def_id, x, y, (int)rotation);
        if (!solar_electrical_graph_add_panel(out_graph, &panel)) return false;
    }

    uint32_t connection_count;
    if (!read_u32_adv(data, end, &connection_count)) return false;
    if (connection_count > SOLAR_MAX_CONNECTIONS) return false;
    for (uint32_t i = 0; i < connection_count; i++) {
        solar_guid_t start_id, end_id;
        double length;
        int32_t gauge;
        if (!advance(data, end, sizeof(start_id))) return false;
        std::memcpy(&start_id, *data - sizeof(start_id), sizeof(start_id));
        if (!advance(data, end, sizeof(end_id))) return false;
        std::memcpy(&end_id, *data - sizeof(end_id), sizeof(end_id));
        if (!read_f64_adv(data, end, &length)) return false;
        if (!read_u32_adv(data, end, (uint32_t*)&gauge)) return false;
        if (!solar_electrical_graph_try_connect(out_graph, &start_id, &end_id, length, (int)gauge)) return false;
    }

    solar_electrical_graph_rebuild_strings(out_graph);
    return true;
}

bool solar_project_parse_roof(const uint8_t **data, const uint8_t *end, roof_document_t *out_roof) {
    if (!data || !out_roof || !*data) return false;
    roof_document_init(out_roof);

    uint32_t surface_count;
    if (!read_u32_adv(data, end, &surface_count)) return false;
    if (surface_count > PROJECT_MAX_SURFACES) return false;

    for (uint32_t i = 0; i < surface_count; i++) {
        roof_surface_t surface;
        roof_surface_init(&surface, "");
        uint32_t name_len;
        if (!read_u32_adv(data, end, &name_len)) return false;
        if (name_len > ROOF_NAME_LEN) return false;
        if (!advance(data, end, name_len)) return false;
        copy_fixed_string(surface.name, ROOF_NAME_LEN, *data - name_len, name_len);
        if (!read_f64_adv(data, end, &surface.setback_mm)) return false;

        uint32_t vertex_count;
        if (!read_u32_adv(data, end, &vertex_count)) return false;
        if (vertex_count > ROOF_MAX_VERTICES) return false;
        for (uint32_t v = 0; v < vertex_count; v++) {
            double x, y;
            if (!read_f64_adv(data, end, &x)) return false;
            if (!read_f64_adv(data, end, &y)) return false;
            roof_point_t p = {x, y};
            roof_surface_add_vertex(&surface, &p);
        }
        if (!roof_document_add_surface(out_roof, &surface)) return false;
    }

    uint32_t obstacle_count;
    if (!read_u32_adv(data, end, &obstacle_count)) return false;
    if (obstacle_count > PROJECT_MAX_OBSTACLES) return false;
    for (uint32_t i = 0; i < obstacle_count; i++) {
        roof_obstacle_t obstacle;
        std::memset(&obstacle, 0, sizeof(obstacle));
        uint32_t name_len;
        if (!read_u32_adv(data, end, &name_len)) return false;
        if (name_len > ROOF_NAME_LEN) return false;
        if (!advance(data, end, name_len)) return false;
        copy_fixed_string(obstacle.name, ROOF_NAME_LEN, *data - name_len, name_len);

        uint32_t vertex_count;
        if (!read_u32_adv(data, end, &vertex_count)) return false;
        if (vertex_count > ROOF_MAX_VERTICES) return false;
        for (uint32_t v = 0; v < vertex_count; v++) {
            double x, y;
            if (!read_f64_adv(data, end, &x)) return false;
            if (!read_f64_adv(data, end, &y)) return false;
            obstacle.vertices[v].x_mm = x;
            obstacle.vertices[v].y_mm = y;
        }
        obstacle.vertex_count = vertex_count;
        if (!roof_document_add_obstacle(out_roof, &obstacle)) return false;
    }

    return true;
}

bool solar_project_file_parse(const uint8_t *data, size_t size, solar_project_t *out_project) {
    if (!data || !out_project || size < sizeof(solar_project_header_t)) return false;
    solar_project_init(out_project);

    size_t header_size = 0;
    if (!solar_project_parse_header(data, size, &header_size, &out_project->header)) return false;

    const uint8_t *p = data + header_size;
    const uint8_t *end = data + size;

    uint32_t definition_count;
    if (!read_u32_adv(&p, end, &definition_count)) return false;
    if (definition_count > PROJECT_MAX_DEFINITIONS) return false;

    for (uint32_t i = 0; i < definition_count; i++) {
        solar_panel_definition_t def;
        if (!solar_project_parse_definition(&p, end, &def)) return false;
        out_project->definitions[out_project->definition_count++] = def;
    }

    if (!solar_project_parse_graph(&p, end, &out_project->graph)) return false;
    if (!solar_project_parse_roof(&p, end, &out_project->roof)) return false;

    return true;
}

bool solar_project_file_validate(const solar_project_t *project) {
    if (!project) return false;
    if (std::memcmp(project->header.magic, PROJECT_MAGIC, 4) != 0) return false;
    if (project->header.version != PROJECT_VERSION) return false;

    for (size_t i = 0; i < project->definition_count; i++) {
        if (!solar_panel_definition_is_valid(&project->definitions[i])) return false;
    }

    if (project->header.site_latitude < -90.0 || project->header.site_latitude > 90.0) return false;
    if (project->header.site_longitude < -180.0 || project->header.site_longitude > 180.0) return false;
    if (!std::isfinite(project->header.cold_voc_temp_c)) return false;
    if (!std::isfinite(project->header.hot_cell_temp_c)) return false;
    if (project->header.peak_sun_hours < 0.0 || project->header.peak_sun_hours > 24.0) return false;
    if (project->header.system_derate < 0.0 || project->header.system_derate > 1.0) return false;

    return true;
}
