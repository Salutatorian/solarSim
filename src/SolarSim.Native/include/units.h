#ifndef UNITS_H
#define UNITS_H

#include <stdbool.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Unit conversion service for the CAD domain.
 * Supported length units: millimeters, meters, feet, inches, feet-inches, yards.
 * Mirrors SolarSim.Application.Units.UnitConversionService.
 */

typedef enum {
    SOLAR_UNIT_MILLIMETER = 0,
    SOLAR_UNIT_METER,
    SOLAR_UNIT_FOOT,
    SOLAR_UNIT_INCH,
    SOLAR_UNIT_YARD,
    SOLAR_UNIT_FOOT_INCH
} solar_length_unit_t;

typedef enum {
    SOLAR_UNIT_FORMAT_PLAIN = 0,
    SOLAR_UNIT_FORMAT_COMPACT,
    SOLAR_UNIT_FORMAT_FULL
} solar_unit_format_t;

typedef struct {
    double value_mm;
    solar_length_unit_t unit;
} solar_length_t;

double solar_unit_to_mm(double value, solar_length_unit_t unit);
double solar_unit_from_mm(double value_mm, solar_length_unit_t unit);

/* Format a length into a user-facing string. */
bool solar_unit_format_length(
    double value_mm,
    solar_length_unit_t unit,
    solar_unit_format_t format,
    char *out_buffer,
    size_t buffer_size);

/* Parse a length string back into millimeters. */
bool solar_unit_parse_length(const char *input, solar_length_unit_t default_unit, double *out_mm);

/* Temperature conversion helpers. */
double solar_celsius_to_fahrenheit(double c);
double solar_fahrenheit_to_celsius(double f);

/* Power/energy conversions. */
double solar_kw_to_watts(double kw);
double solar_watts_to_kw(double w);
double solar_kwh_per_year_from_kw(double kw, double psh, double derate);

#ifdef __cplusplus
}
#endif

#endif
