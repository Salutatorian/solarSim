#ifndef WIRE_ROUTE_H
#define WIRE_ROUTE_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Wire routing polyline and voltage-drop estimation.
 * Mirrors SolarSim.Domain.Electrical.WireRouting and WireGaugeFormat.
 */

#define WIRE_ROUTE_MAX_POINTS 64
#define WIRE_TYPE_LEN 32

/* Resistivity in ohms per meter at 75C for common copper AWG gauges. */
typedef enum {
    WIRE_AWG_4_0 = -40,
    WIRE_AWG_3_0 = -30,
    WIRE_AWG_2_0 = -20,
    WIRE_AWG_1_0 = -10,
    WIRE_AWG_6 = 6,
    WIRE_AWG_8 = 8,
    WIRE_AWG_10 = 10,
    WIRE_AWG_12 = 12
} wire_awg_t;

typedef enum {
    WIRE_MATERIAL_COPPER = 0,
    WIRE_MATERIAL_ALUMINUM
} wire_material_t;

typedef struct {
    double x_mm;
    double y_mm;
} wire_point_t;

typedef struct {
    wire_point_t points[WIRE_ROUTE_MAX_POINTS];
    size_t point_count;
    wire_awg_t gauge_awg;
    wire_material_t material;
    char wire_type[WIRE_TYPE_LEN];
    double ambient_temp_c;
} wire_route_t;

typedef struct {
    double length_mm;
    double length_m;
    double resistance_ohms;
    double voltage_drop_volts;
    double voltage_drop_percent;
    double ampacity_amps;
    bool is_valid;
} wire_route_result_t;

void wire_route_init(wire_route_t *route, wire_awg_t gauge, wire_material_t material);
bool wire_route_add_point(wire_route_t *route, const wire_point_t *point);
bool wire_route_add_point_xy(wire_route_t *route, double x_mm, double y_mm);
bool wire_route_ortho_route(wire_route_t *route, const wire_point_t *start, const wire_point_t *end);

double wire_route_length_mm(const wire_route_t *route);

void wire_route_result_calculate(
    const wire_route_t *route,
    double current_amps,
    wire_route_result_t *out_result);

double wire_resistance_per_meter_copper(wire_awg_t gauge);
double wire_resistance_per_meter_aluminum(wire_awg_t gauge);
double wire_ampacity_amps(wire_awg_t gauge, wire_material_t material);
bool wire_awg_is_valid(wire_awg_t gauge);

/* Convert between metric and imperial units for wire length. */
double wire_mm_to_feet(double mm);
double wire_feet_to_mm(double feet);

#ifdef __cplusplus
}
#endif

#endif
