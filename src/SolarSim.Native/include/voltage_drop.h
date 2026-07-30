#ifndef VOLTAGE_DROP_H
#define VOLTAGE_DROP_H

#include <stdbool.h>
#include <stddef.h>

#include "wire_route.h"

#ifdef __cplusplus
extern "C" {
#endif

/* DC conductor voltage-drop calculations.
 * Mirrors SolarSim.Domain.Electrical.VoltageDropCalculator.
 */

typedef struct {
    double one_way_length_mm;
    double circuit_length_mm;
    double resistance_ohms;
    double voltage_drop_volts;
    double power_loss_watts;
    double percent_drop;
    bool has_percent_drop;
    wire_awg_t gauge;
    char material[16];
    double current_amps;
    bool is_estimate;
} solar_voltage_drop_result_t;

double solar_voltage_drop_resistance_per_1000ft(wire_awg_t gauge, const char *material);

void solar_voltage_drop_calculate(
    wire_awg_t gauge,
    const char *material,
    double one_way_length_mm,
    double current_amps,
    double system_voltage_volts,
    solar_voltage_drop_result_t *out_result);

void solar_voltage_drop_calculate_from_route(
    const wire_route_t *route,
    double current_amps,
    double system_voltage_volts,
    solar_voltage_drop_result_t *out_result);

/* Suggest the smallest standard copper gauge that keeps voltage drop
 * under a target percent for a given one-way length and current. */
bool solar_voltage_drop_suggest_gauge(
    double one_way_length_mm,
    double current_amps,
    double system_voltage_volts,
    double max_percent_drop,
    wire_material_t material,
    wire_awg_t *out_gauge,
    double *out_actual_percent);

/* NEC-style ampacity check (design aid only, not code compliance). */
bool solar_voltage_drop_check_ampacity(
    wire_awg_t gauge,
    wire_material_t material,
    double current_amps,
    double ambient_temp_c);

#ifdef __cplusplus
}
#endif

#endif
