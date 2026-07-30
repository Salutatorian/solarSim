#include "units.h"

#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <cmath>

static const double MM_PER_METER = 1000.0;
static const double MM_PER_FOOT = 304.8;
static const double MM_PER_INCH = 25.4;
static const double MM_PER_YARD = 914.4;

double solar_unit_to_mm(double value, solar_length_unit_t unit) {
    switch (unit) {
        case SOLAR_UNIT_MILLIMETER: return value;
        case SOLAR_UNIT_METER: return value * MM_PER_METER;
        case SOLAR_UNIT_FOOT: return value * MM_PER_FOOT;
        case SOLAR_UNIT_INCH: return value * MM_PER_INCH;
        case SOLAR_UNIT_YARD: return value * MM_PER_YARD;
        case SOLAR_UNIT_FOOT_INCH: return value * MM_PER_FOOT; /* Simplified: value is total feet. */
        default: return value;
    }
}

double solar_unit_from_mm(double value_mm, solar_length_unit_t unit) {
    switch (unit) {
        case SOLAR_UNIT_MILLIMETER: return value_mm;
        case SOLAR_UNIT_METER: return value_mm / MM_PER_METER;
        case SOLAR_UNIT_FOOT: return value_mm / MM_PER_FOOT;
        case SOLAR_UNIT_INCH: return value_mm / MM_PER_INCH;
        case SOLAR_UNIT_YARD: return value_mm / MM_PER_YARD;
        case SOLAR_UNIT_FOOT_INCH: return value_mm / MM_PER_FOOT; /* Simplified. */
        default: return value_mm;
    }
}

static const char *unit_symbol(solar_length_unit_t unit) {
    switch (unit) {
        case SOLAR_UNIT_MILLIMETER: return "mm";
        case SOLAR_UNIT_METER: return "m";
        case SOLAR_UNIT_FOOT: return "ft";
        case SOLAR_UNIT_INCH: return "in";
        case SOLAR_UNIT_YARD: return "yd";
        case SOLAR_UNIT_FOOT_INCH: return "ft-in";
        default: return "";
    }
}

bool solar_unit_format_length(
    double value_mm,
    solar_length_unit_t unit,
    solar_unit_format_t format,
    char *out_buffer,
    size_t buffer_size) {
    if (!out_buffer || buffer_size == 0) return false;
    if (!std::isfinite(value_mm)) {
        std::snprintf(out_buffer, buffer_size, "invalid");
        return false;
    }
    double converted = solar_unit_from_mm(value_mm, unit);
    const char *sym = unit_symbol(unit);
    if (format == SOLAR_UNIT_FORMAT_COMPACT) {
        std::snprintf(out_buffer, buffer_size, "%.1f%s", converted, sym);
    } else if (format == SOLAR_UNIT_FORMAT_FULL) {
        std::snprintf(out_buffer, buffer_size, "%.2f %s", converted, sym);
    } else {
        std::snprintf(out_buffer, buffer_size, "%.0f", converted);
    }
    return true;
}

bool solar_unit_parse_length(const char *input, solar_length_unit_t default_unit, double *out_mm) {
    if (!input || !out_mm) return false;
    char buffer[64];
    size_t len = std::strlen(input);
    if (len >= sizeof(buffer)) len = sizeof(buffer) - 1;
    std::memcpy(buffer, input, len);
    buffer[len] = '\0';

    double value = 0.0;
    char unit_str[16] = {0};
    int n = std::sscanf(buffer, "%lf%15s", &value, unit_str);
    if (n < 1) return false;

    solar_length_unit_t unit = default_unit;
    if (n >= 2) {
        if (std::strcmp(unit_str, "mm") == 0) unit = SOLAR_UNIT_MILLIMETER;
        else if (std::strcmp(unit_str, "m") == 0) unit = SOLAR_UNIT_METER;
        else if (std::strcmp(unit_str, "ft") == 0) unit = SOLAR_UNIT_FOOT;
        else if (std::strcmp(unit_str, "in") == 0) unit = SOLAR_UNIT_INCH;
        else if (std::strcmp(unit_str, "yd") == 0) unit = SOLAR_UNIT_YARD;
        else if (std::strcmp(unit_str, "ft-in") == 0) unit = SOLAR_UNIT_FOOT_INCH;
    }

    *out_mm = solar_unit_to_mm(value, unit);
    return std::isfinite(*out_mm);
}

double solar_celsius_to_fahrenheit(double c) {
    return c * 9.0 / 5.0 + 32.0;
}

double solar_fahrenheit_to_celsius(double f) {
    return (f - 32.0) * 5.0 / 9.0;
}

double solar_kw_to_watts(double kw) {
    return kw * 1000.0;
}

double solar_watts_to_kw(double w) {
    return w / 1000.0;
}

double solar_kwh_per_year_from_kw(double kw, double psh, double derate) {
    if (kw < 0.0 || psh < 0.0 || derate < 0.0 || derate > 1.0) return 0.0;
    return kw * psh * derate * 365.0;
}
