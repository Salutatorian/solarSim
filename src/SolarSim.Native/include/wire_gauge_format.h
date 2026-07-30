#ifndef WIRE_GAUGE_FORMAT_H
#define WIRE_GAUGE_FORMAT_H

#include <stdbool.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

/* AWG gauge enum, battery cable gauges, PV string gauges, ampacity tables,
 * and format helpers ported from SolarSim.Domain.Electrical.WireGaugeFormat.
 */

typedef enum {
    SOLAR_AWG_4_0 = -40,
    SOLAR_AWG_3_0 = -30,
    SOLAR_AWG_2_0 = -20,
    SOLAR_AWG_1_0 = -10,
    SOLAR_AWG_6 = 6,
    SOLAR_AWG_8 = 8,
    SOLAR_AWG_10 = 10,
    SOLAR_AWG_12 = 12,
    SOLAR_AWG_INVALID = 0
} solar_wire_gauge_awg_t;

#define SOLAR_WIRE_MATERIAL_LEN 16
#define SOLAR_WIRE_TYPE_LEN 32
#define SOLAR_WIRE_COLOR_LEN 16

typedef struct {
    solar_wire_gauge_awg_t gauge;
    char material[SOLAR_WIRE_MATERIAL_LEN];
    char wire_type[SOLAR_WIRE_TYPE_LEN];
    char color[SOLAR_WIRE_COLOR_LEN];
} solar_wire_properties_t;

/* Format a gauge enum to a human-readable display string (e.g. "4/0" or "10 AWG"). */
const char *solar_wire_gauge_to_display(solar_wire_gauge_awg_t gauge, char *buffer, size_t buffer_size);

/* Parse a display string back to an enum. Returns true on success. */
bool solar_wire_gauge_from_display(const char *text, solar_wire_gauge_awg_t *out_gauge);

/* Built-in gauge lists. */
const solar_wire_gauge_awg_t *solar_wire_battery_cable_gauges(size_t *out_count);
const solar_wire_gauge_awg_t *solar_wire_pv_string_gauges(size_t *out_count);
const solar_wire_gauge_awg_t *solar_wire_all_gauges(size_t *out_count);

/* DC resistance per 1000 ft (ohms) for copper at 25°C. Returns 0.0 if unknown. */
double solar_wire_copper_ohms_per_1000ft(solar_wire_gauge_awg_t gauge);

/* Aluminum resistance is approximately 1.6x copper for the same AWG. */
double solar_wire_aluminum_ohms_per_1000ft(solar_wire_gauge_awg_t gauge);

/* Ampacity lookup (approximate, NEC-style, 75°C copper). Returns 0 if unknown. */
double solar_wire_copper_ampacity_amps(solar_wire_gauge_awg_t gauge);

/* Recommend a PV string conductor gauge based on current and length (design aid). */
solar_wire_gauge_awg_t solar_wire_recommend_pv_string_gauge(double current_amps, double one_way_length_mm);

/* Recommend a battery cable gauge based on continuous current and peak current. */
solar_wire_gauge_awg_t solar_wire_recommend_battery_gauge(double continuous_amps, double peak_amps);

/* Format complete wire properties as a BOM-style description. */
const char *solar_wire_properties_to_display(
    const solar_wire_properties_t *props,
    char *buffer,
    size_t buffer_size);

/* Helpers for working with raw integer gauge codes. */
solar_wire_gauge_awg_t solar_wire_gauge_from_int(int code);
bool solar_wire_gauge_is_valid(solar_wire_gauge_awg_t gauge);
int solar_wire_gauge_compare(solar_wire_gauge_awg_t a, solar_wire_gauge_awg_t b);

/* Approximate conductor cross-sectional area in circular mils. */
double solar_wire_circular_mils(solar_wire_gauge_awg_t gauge);

/* DC voltage drop for a round-trip conductor. Optionally returns resistance. */
double solar_wire_voltage_drop(
    solar_wire_gauge_awg_t gauge,
    const char *material,
    double one_way_length_mm,
    double current_amps,
    double *out_resistance_ohms);

/* Temperature correction factor for ampacity. */
double solar_wire_temperature_factor(double ambient_celsius);

/* Ampacity adjusted for ambient temperature and material. */
double solar_wire_adjusted_ampacity(solar_wire_gauge_awg_t gauge, double ambient_celsius, const char *material);

/* Recommend a conductor for a 240 V AC branch circuit given a voltage-drop budget. */
solar_wire_gauge_awg_t solar_wire_recommend_ac_gauge(double current_amps, double one_way_length_mm, double max_drop_pct);

/* Recommend a rough conduit size given gauge and conductor count. */
const char *solar_wire_recommend_conduit_size(
    solar_wire_gauge_awg_t gauge,
    size_t conductor_count,
    char *buffer,
    size_t buffer_size);

#ifdef __cplusplus
}
#endif

#endif
