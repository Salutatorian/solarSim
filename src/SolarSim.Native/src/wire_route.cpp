#include "wire_route.h"

#include <cstdint>
#include <cstring>
#include <cmath>

static void copy_wire_type(char *dest, size_t dest_size, const char *src) {
    if (!src || dest_size == 0) return;
    size_t len = std::strlen(src);
    if (len >= dest_size) len = dest_size - 1;
    std::memcpy(dest, src, len);
    dest[len] = '\0';
}

void wire_route_init(wire_route_t *route, wire_awg_t gauge, wire_material_t material) {
    if (!route) return;
    std::memset(route, 0, sizeof(*route));
    route->gauge_awg = gauge;
    route->material = material;
    route->ambient_temp_c = 30.0;
    if (material == WIRE_MATERIAL_ALUMINUM) {
        copy_wire_type(route->wire_type, WIRE_TYPE_LEN, "Aluminum PV wire");
    } else {
        copy_wire_type(route->wire_type, WIRE_TYPE_LEN, "Copper PV wire");
    }
}

bool wire_route_add_point(wire_route_t *route, const wire_point_t *point) {
    if (!route || !point) return false;
    if (route->point_count >= WIRE_ROUTE_MAX_POINTS) return false;
    route->points[route->point_count] = *point;
    route->point_count++;
    return true;
}

bool wire_route_add_point_xy(wire_route_t *route, double x_mm, double y_mm) {
    wire_point_t p = {x_mm, y_mm};
    return wire_route_add_point(route, &p);
}

bool wire_route_ortho_route(wire_route_t *route, const wire_point_t *start, const wire_point_t *end) {
    if (!route || !start || !end) return false;
    wire_route_init(route, route->gauge_awg, route->material);
    if (!wire_route_add_point(route, start)) return false;
    double dx = end->x_mm - start->x_mm;
    double dy = end->y_mm - start->y_mm;
    if (std::fabs(dx) >= std::fabs(dy)) {
        wire_point_t corner = {end->x_mm, start->y_mm};
        if (!wire_route_add_point(route, &corner)) return false;
    } else {
        wire_point_t corner = {start->x_mm, end->y_mm};
        if (!wire_route_add_point(route, &corner)) return false;
    }
    if (!wire_route_add_point(route, end)) return false;
    return true;
}

double wire_route_length_mm(const wire_route_t *route) {
    if (!route || route->point_count < 2) return 0.0;
    double total = 0.0;
    for (size_t i = 1; i < route->point_count; i++) {
        double dx = route->points[i].x_mm - route->points[i - 1].x_mm;
        double dy = route->points[i].y_mm - route->points[i - 1].y_mm;
        total += std::sqrt(dx * dx + dy * dy);
    }
    return total;
}

double wire_mm_to_feet(double mm) {
    return mm / 304.8;
}

double wire_feet_to_mm(double feet) {
    return feet * 304.8;
}

bool wire_awg_is_valid(wire_awg_t gauge) {
    switch (gauge) {
        case WIRE_AWG_4_0:
        case WIRE_AWG_3_0:
        case WIRE_AWG_2_0:
        case WIRE_AWG_1_0:
        case WIRE_AWG_6:
        case WIRE_AWG_8:
        case WIRE_AWG_10:
        case WIRE_AWG_12:
            return true;
        default:
            return false;
    }
}

double wire_resistance_per_meter_copper(wire_awg_t gauge) {
    switch (gauge) {
        case WIRE_AWG_4_0: return 0.0001608;
        case WIRE_AWG_3_0: return 0.0002028;
        case WIRE_AWG_2_0: return 0.0002557;
        case WIRE_AWG_1_0: return 0.0003224;
        case WIRE_AWG_6: return 0.001296;
        case WIRE_AWG_8: return 0.002061;
        case WIRE_AWG_10: return 0.003277;
        case WIRE_AWG_12: return 0.005211;
        default: return 0.0;
    }
}

double wire_resistance_per_meter_aluminum(wire_awg_t gauge) {
    /* Aluminum is roughly 1.6x copper resistance for the same gauge. */
    return wire_resistance_per_meter_copper(gauge) * 1.6;
}

double wire_ampacity_amps(wire_awg_t gauge, wire_material_t material) {
    double copper_ampacity = 0.0;
    switch (gauge) {
        case WIRE_AWG_4_0: copper_ampacity = 230.0; break;
        case WIRE_AWG_3_0: copper_ampacity = 200.0; break;
        case WIRE_AWG_2_0: copper_ampacity = 175.0; break;
        case WIRE_AWG_1_0: copper_ampacity = 150.0; break;
        case WIRE_AWG_6: copper_ampacity = 55.0; break;
        case WIRE_AWG_8: copper_ampacity = 40.0; break;
        case WIRE_AWG_10: copper_ampacity = 30.0; break;
        case WIRE_AWG_12: copper_ampacity = 20.0; break;
        default: return 0.0;
    }
    if (material == WIRE_MATERIAL_ALUMINUM) {
        return copper_ampacity * 0.78;
    }
    return copper_ampacity;
}

void wire_route_result_calculate(
    const wire_route_t *route,
    double current_amps,
    wire_route_result_t *out_result) {
    if (!route || !out_result) return;
    std::memset(out_result, 0, sizeof(*out_result));

    if (!wire_awg_is_valid(route->gauge_awg)) {
        out_result->is_valid = false;
        return;
    }
    if (!std::isfinite(current_amps) || current_amps < 0.0) {
        out_result->is_valid = false;
        return;
    }

    double length_mm = wire_route_length_mm(route);
    if (!std::isfinite(length_mm) || length_mm < 0.0) {
        out_result->is_valid = false;
        return;
    }

    double length_m = length_mm / 1000.0;
    double r_per_m = (route->material == WIRE_MATERIAL_ALUMINUM)
        ? wire_resistance_per_meter_aluminum(route->gauge_awg)
        : wire_resistance_per_meter_copper(route->gauge_awg);

    /* DC circuits go out and back, so total conductor length is 2x route length. */
    double total_resistance = r_per_m * length_m * 2.0;
    double voltage_drop = total_resistance * current_amps;
    double ampacity = wire_ampacity_amps(route->gauge_awg, route->material);

    out_result->length_mm = length_mm;
    out_result->length_m = length_m;
    out_result->resistance_ohms = total_resistance;
    out_result->voltage_drop_volts = voltage_drop;
    out_result->ampacity_amps = ampacity;
    out_result->is_valid = true;

    /* Voltage drop percent referenced to a typical 400 V string Vmp. */
    const double reference_voltage = 400.0;
    if (reference_voltage > 0.0) {
        out_result->voltage_drop_percent = (voltage_drop / reference_voltage) * 100.0;
    }

    /* Simple temperature derating of ampacity. */
    if (route->ambient_temp_c > 30.0) {
        double factor = 1.0 - (route->ambient_temp_c - 30.0) * 0.004;
        if (factor < 0.5) factor = 0.5;
        out_result->ampacity_amps *= factor;
    }

    if (current_amps > out_result->ampacity_amps) {
        out_result->is_valid = false;
    }
    if (out_result->voltage_drop_percent > 3.0) {
        out_result->is_valid = false;
    }
}
