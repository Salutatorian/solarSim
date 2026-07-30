#include "mc4_connector.h"

#include <cstdio>
#include <cstring>

const char *solar_connector_family_name(solar_connector_family_id_t family) {
    switch (family) {
        case SOLAR_CONNECTOR_FAMILY_MC4: return "MC4";
        case SOLAR_CONNECTOR_FAMILY_TYCO: return "Tyco";
        case SOLAR_CONNECTOR_FAMILY_AIMETT: return "Aimett";
        case SOLAR_CONNECTOR_FAMILY_OTHER: return "Other";
        default: return "Unknown";
    }
}

bool solar_connector_family_is_compatible(solar_connector_family_id_t a, solar_connector_family_id_t b) {
    if (a == SOLAR_CONNECTOR_FAMILY_OTHER || b == SOLAR_CONNECTOR_FAMILY_OTHER) {
        /* Generic family may mate with anything with explicit override. */
        return true;
    }
    return a == b;
}

bool solar_connector_can_mate(
    solar_connector_interface_t a_interface,
    solar_polarity_t a_polarity,
    solar_connector_interface_t b_interface,
    solar_polarity_t b_polarity) {
    /* Same polarity cannot mate (e.g., male+ to male+). */
    if (a_polarity == b_polarity) return false;

    /* Male must mate with female for mechanical compatibility. */
    if (a_interface == SOLAR_CONNECTOR_MALE && b_interface == SOLAR_CONNECTOR_FEMALE) return true;
    if (a_interface == SOLAR_CONNECTOR_FEMALE && b_interface == SOLAR_CONNECTOR_MALE) return true;

    /* Unspecified can mate with either gender. */
    if (a_interface == SOLAR_CONNECTOR_UNSPECIFIED || b_interface == SOLAR_CONNECTOR_UNSPECIFIED) {
        return true;
    }

    return false;
}

static void set_error(mc4_series_result_t *result, const char *code, const char *message, size_t index) {
    if (!result) return;
    result->is_valid = false;
    std::strncpy(result->error_code, code, sizeof(result->error_code) - 1);
    result->error_code[sizeof(result->error_code) - 1] = '\0';
    std::strncpy(result->error_message, message, sizeof(result->error_message) - 1);
    result->error_message[sizeof(result->error_message) - 1] = '\0';
    result->error_index = index;
}

void mc4_validate_series(
    const mc4_connector_t *connectors,
    size_t count,
    mc4_series_result_t *out_result) {
    if (!out_result) return;
    std::memset(out_result, 0, sizeof(*out_result));
    out_result->is_valid = true;

    if (!connectors) {
        set_error(out_result, "NULL_INPUT", "Connector array is null", 0);
        return;
    }
    if (count == 0) {
        set_error(out_result, "EMPTY_SERIES", "Connector series is empty", 0);
        return;
    }
    if (count > MC4_MAX_SERIES_CONNECTORS) {
        set_error(out_result, "TOO_MANY_CONNECTORS", "Series exceeds maximum connector count", 0);
        return;
    }

    for (size_t i = 0; i < count; i++) {
        const mc4_connector_t *c = &connectors[i];
        if (c->interface_type != SOLAR_CONNECTOR_MALE &&
            c->interface_type != SOLAR_CONNECTOR_FEMALE &&
            c->interface_type != SOLAR_CONNECTOR_UNSPECIFIED) {
            set_error(out_result, "INVALID_INTERFACE", "Invalid connector interface type", i);
            return;
        }
        if (c->polarity != SOLAR_POLARITY_POSITIVE && c->polarity != SOLAR_POLARITY_NEGATIVE) {
            set_error(out_result, "INVALID_POLARITY", "Invalid connector polarity", i);
            return;
        }
        if (c->family >= SOLAR_CONNECTOR_FAMILY_COUNT) {
            set_error(out_result, "INVALID_FAMILY", "Invalid connector family", i);
            return;
        }
    }

    /* In a series string, polarities must alternate and adjacent connectors must mate. */
    for (size_t i = 0; i + 1 < count; i++) {
        const mc4_connector_t *a = &connectors[i];
        const mc4_connector_t *b = &connectors[i + 1];

        if (!solar_connector_family_is_compatible(a->family, b->family)) {
            set_error(out_result, "FAMILY_MISMATCH", "Incompatible connector families in series", i);
            return;
        }
        if (!solar_connector_can_mate(a->interface_type, a->polarity, b->interface_type, b->polarity)) {
            set_error(out_result, "MATE_FAILURE", "Adjacent connectors cannot mate", i);
            return;
        }
        /* For a clean string, polarities should alternate. */
        if (a->polarity == b->polarity) {
            set_error(out_result, "POLARITY_REPEAT", "Polarity did not alternate in series", i);
            return;
        }
    }

    /* First and last connectors in a series string should have opposite polarity
     * (positive at one end, negative at the other). */
    if (connectors[0].polarity == connectors[count - 1].polarity) {
        set_error(out_result, "OPEN_STRING", "Series string is not closed between opposite polarities", 0);
        return;
    }
}

bool mc4_check_wire_fit(
    solar_connector_family_id_t family,
    wire_awg_t gauge,
    char *out_error,
    size_t error_size) {
    if (out_error && error_size > 0) out_error[0] = '\0';

    if (family >= SOLAR_CONNECTOR_FAMILY_COUNT) {
        if (out_error && error_size > 0) {
            std::snprintf(out_error, error_size, "Invalid connector family");
        }
        return false;
    }

    switch (family) {
        case SOLAR_CONNECTOR_FAMILY_MC4:
            if (gauge != WIRE_AWG_10 && gauge != WIRE_AWG_12) {
                if (out_error && error_size > 0) {
                    std::snprintf(out_error, error_size, "MC4 connectors typically fit 10 or 12 AWG PV wire");
                }
                return false;
            }
            break;
        case SOLAR_CONNECTOR_FAMILY_TYCO:
            if (gauge != WIRE_AWG_10 && gauge != WIRE_AWG_8) {
                if (out_error && error_size > 0) {
                    std::snprintf(out_error, error_size, "Tyco connectors typically fit 8 or 10 AWG wire");
                }
                return false;
            }
            break;
        case SOLAR_CONNECTOR_FAMILY_AIMETT:
            if (gauge != WIRE_AWG_12) {
                if (out_error && error_size > 0) {
                    std::snprintf(out_error, error_size, "Aimett connectors typically fit 12 AWG wire");
                }
                return false;
            }
            break;
        default:
            break;
    }
    return true;
}
