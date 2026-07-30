#include "connection_validator.h"

#include <cstddef>
#include <cstdio>
#include <cstring>
#include <cmath>

static void copy_string(char *dest, size_t dest_size, const char *src) {
    if (!src || dest_size == 0) return;
    size_t len = std::strlen(src);
    if (len >= dest_size) len = dest_size - 1;
    std::memcpy(dest, src, len);
    dest[len] = '\0';
}

static bool eq_guid(const solar_guid_t *a, const solar_guid_t *b) {
    return a->id_high == b->id_high && a->id_low == b->id_low;
}

static bool is_zero_guid(const solar_guid_t *guid) {
    return guid->id_high == 0 && guid->id_low == 0;
}

static int starts_with_case_insensitive(const char *str, const char *prefix) {
    if (!str || !prefix) return 0;
    while (*prefix) {
        char a = *str;
        char b = *prefix;
        if (a >= 'A' && a <= 'Z') a += 'a' - 'A';
        if (b >= 'A' && b <= 'Z') b += 'a' - 'A';
        if (a != b) return 0;
        str++;
        prefix++;
    }
    return 1;
}

static int case_insensitive_equals(const char *a, const char *b) {
    if (!a || !b) return a == b ? 1 : 0;
    while (*a && *b) {
        char ca = *a;
        char cb = *b;
        if (ca >= 'A' && ca <= 'Z') ca += 'a' - 'A';
        if (cb >= 'A' && cb <= 'Z') cb += 'a' - 'A';
        if (ca != cb) return 0;
        a++;
        b++;
    }
    return *a == '\0' && *b == '\0' ? 1 : 0;
}

static void add_issue(
    solar_connection_validation_result_t *result,
    solar_conn_validation_severity_t severity,
    const char *code,
    const char *message,
    const char *detail,
    const solar_guid_t *affected,
    size_t affected_count) {
    if (!result || !code || !message || !detail) return;
    solar_conn_validation_issue_t *issue = NULL;
    size_t *count = NULL;
    size_t max = SOLAR_CONN_VALIDATION_MAX_ISSUES;
    if (severity == SOLAR_CONN_VALIDATION_SEVERITY_ERROR) {
        issue = result->errors;
        count = &result->error_count;
    } else if (severity == SOLAR_CONN_VALIDATION_SEVERITY_WARNING) {
        issue = result->warnings;
        count = &result->warning_count;
    } else {
        issue = result->info;
        count = &result->info_count;
    }
    if (*count >= max) return;
    solar_conn_validation_issue_t *slot = &issue[*count];
    slot->severity = severity;
    copy_string(slot->code, SOLAR_CONN_VALIDATION_CODE_LEN, code);
    copy_string(slot->message, SOLAR_CONN_VALIDATION_MSG_LEN, message);
    copy_string(slot->detail, SOLAR_CONN_VALIDATION_MSG_LEN, detail);
    slot->affected_count = 0;
    if (affected) {
        for (size_t i = 0; i < affected_count && i < SOLAR_CONN_VALIDATION_MAX_AFFECTED; i++) {
            slot->affected_ids[slot->affected_count++] = affected[i];
        }
    }
    (*count)++;
}

void solar_connection_validation_result_init(solar_connection_validation_result_t *result) {
    if (!result) return;
    std::memset(result, 0, sizeof(*result));
    result->is_valid = true;
}

static bool is_panel_pv_port(const solar_port_t *port) {
    if (!port) return false;
    return port->type == SOLAR_PORT_PV_POSITIVE || port->type == SOLAR_PORT_PV_NEGATIVE;
}

static int port_equipment_type(const solar_port_t *port, const solar_equipment_instance_t *owner) {
    if (!port || !owner) return -1;
    for (size_t i = 0; i < owner->port_count; i++) {
        if (eq_guid(&owner->ports[i].base.id, &port->id)) {
            return owner->ports[i].port_type;
        }
    }
    return -1;
}

static const char *port_label(const solar_port_t *port, const solar_equipment_instance_t *owner) {
    if (!port || !owner) return "";
    for (size_t i = 0; i < owner->port_count; i++) {
        if (eq_guid(&owner->ports[i].base.id, &port->id)) {
            return owner->ports[i].label;
        }
    }
    return "";
}

static bool is_ac_port_type(int port_type) {
    return port_type == 16 || port_type == 17 || port_type == 18 || port_type == 19 || port_type == 20;
}

static bool port_is_branch(const solar_port_t *port, const solar_equipment_instance_t *owner) {
    if (!port || !owner) return false;
    return owner->kind == SOLAR_EQUIPMENT_BRANCH_Y_POSITIVE ||
           owner->kind == SOLAR_EQUIPMENT_BRANCH_Y_NEGATIVE;
}

void solar_connection_validator_validate_connector_compatibility(
    const solar_port_t *start,
    const solar_port_t *end,
    solar_connection_validation_result_t *result) {
    if (!start || !end || !result) return;

    if (start->connector_family[0] == '\0' || end->connector_family[0] == '\0') return;
    if (!case_insensitive_equals(start->connector_family, end->connector_family)) {
        char detail[256];
        std::snprintf(detail, sizeof(detail),
            "Connector families differ (%s vs %s).",
            start->connector_family, end->connector_family);
        add_issue(result, SOLAR_CONN_VALIDATION_SEVERITY_WARNING,
            "CONNECTOR_FAMILY_MISMATCH", "Connector family mismatch", detail, NULL, 0);
    }

    /* Gender: MC4-style positive is male, negative is female. Warn if both are male or both female. */
    if (start->interface_type != SOLAR_CONNECTOR_UNSPECIFIED &&
        end->interface_type != SOLAR_CONNECTOR_UNSPECIFIED) {
        if (start->interface_type == end->interface_type) {
            const char *gender = start->interface_type == SOLAR_CONNECTOR_MALE ? "male" : "female";
            char detail[256];
            std::snprintf(detail, sizeof(detail),
                "Both terminals are %s; MC4-style connectors normally mate opposite genders.", gender);
            add_issue(result, SOLAR_CONN_VALIDATION_SEVERITY_WARNING,
                "CONNECTOR_GENDER_SAME", "Connector gender", detail, NULL, 0);
        }
    }
}

void solar_connection_validator_validate_disconnect_to_inverter(
    const solar_port_t *start,
    const solar_port_t *end,
    const solar_equipment_instance_t *start_owner,
    const solar_equipment_instance_t *end_owner,
    solar_connection_validation_result_t *result) {
    if (!start || !end || !result) return;
    if (!start_owner || !end_owner) return;

    bool start_is_batt_disc = solar_equipment_is_battery_disconnect(start_owner);
    bool end_is_batt_disc = solar_equipment_is_battery_disconnect(end_owner);
    bool start_is_inv = solar_equipment_is_inverter(start_owner);
    bool end_is_inv = solar_equipment_is_inverter(end_owner);

    if (!start_is_batt_disc && !end_is_batt_disc) return;
    if (!start_is_inv && !end_is_inv) return;

    const solar_port_t *disc_port = start_is_batt_disc ? start : end;
    const solar_port_t *inv_port = start_is_inv ? start : end;
    const solar_equipment_instance_t *disc_owner = start_is_batt_disc ? start_owner : end_owner;
    const solar_equipment_instance_t *inv_owner = start_is_inv ? start_owner : end_owner;
    (void)disc_port;
    (void)disc_owner;

    bool inv_is_bat_terminal = starts_with_case_insensitive(port_label(inv_port, inv_owner), "BAT");
    if (!inv_is_bat_terminal) {
        solar_guid_t affected[2];
        affected[0] = disc_owner->id;
        affected[1] = inv_owner->id;
        add_issue(result, SOLAR_CONN_VALIDATION_SEVERITY_ERROR,
            "BATT_DISC_TO_INV", "Battery disconnect → inverter",
            "Connect the battery disconnect to the inverter BAT+ / BAT− terminals (not PV/MPPT).",
            affected, 2);
    }
}

void solar_connection_validator_validate_battery_rules(
    const solar_port_t *start,
    const solar_port_t *end,
    const solar_equipment_instance_t *start_owner,
    const solar_equipment_instance_t *end_owner,
    solar_connection_validation_result_t *result) {
    if (!start || !end || !result) return;
    if (!start_owner || !end_owner) return;

    bool start_is_bat = solar_equipment_is_battery(start_owner);
    bool end_is_bat = solar_equipment_is_battery(end_owner);
    if (!start_is_bat && !end_is_bat) return;

    const solar_port_t *battery_port = start_is_bat ? start : end;
    const solar_equipment_instance_t *other_owner = start_is_bat ? end_owner : start_owner;
    const solar_port_t *other_port = start_is_bat ? end : start;
    const solar_equipment_instance_t *battery_owner = start_is_bat ? start_owner : end_owner;

    solar_guid_t affected[2];
    affected[0] = battery_owner->id;
    affected[1] = other_owner->id;

    if (solar_equipment_is_inverter(other_owner)) {
        bool other_bat = starts_with_case_insensitive(port_label(other_port, other_owner), "BAT");
        bool battery_bat = starts_with_case_insensitive(port_label(battery_port, battery_owner), "BAT");
        if (!other_bat || !battery_bat) {
            add_issue(result, SOLAR_CONN_VALIDATION_SEVERITY_ERROR,
                "BATTERY_TERMINAL", "Battery terminal",
                "Use BAT+ / BAT− on both the battery and the inverter.", affected, 2);
        }
        return;
    }

    if (solar_equipment_is_battery_disconnect(other_owner)) {
        bool disc_side = starts_with_case_insensitive(port_label(other_port, other_owner), "IN") ||
                         starts_with_case_insensitive(port_label(other_port, other_owner), "OUT");
        bool battery_bat = starts_with_case_insensitive(port_label(battery_port, battery_owner), "BAT");
        if (!disc_side || !battery_bat) {
            add_issue(result, SOLAR_CONN_VALIDATION_SEVERITY_ERROR,
                "BATTERY_DISCONNECT_SIDE", "Battery disconnect",
                "Wire the battery to the disconnect IN± or OUT± terminals (top or bottom — use the nearer side).",
                affected, 2);
        }
        return;
    }

    if (other_owner->kind == SOLAR_EQUIPMENT_PV_DISCONNECT) {
        bool disc_side = starts_with_case_insensitive(port_label(other_port, other_owner), "IN") ||
                         starts_with_case_insensitive(port_label(other_port, other_owner), "OUT");
        bool battery_bat = starts_with_case_insensitive(port_label(battery_port, battery_owner), "BAT");
        if (!disc_side || !battery_bat) {
            add_issue(result, SOLAR_CONN_VALIDATION_SEVERITY_ERROR,
                "BATTERY_SOLAR_DISCONNECT", "Solar disconnect",
                "Wire the battery to the solar disconnect IN± or OUT± (design-aid layout).",
                affected, 2);
        }
        return;
    }

    add_issue(result, SOLAR_CONN_VALIDATION_SEVERITY_ERROR,
        "BATTERY_PATH", "Battery wiring",
        "Battery cables connect to an inverter BAT±, a battery disconnect, or a solar disconnect.",
        affected, 2);
}

void solar_connection_validator_validate(
    const solar_port_t *start,
    const solar_port_t *end,
    const solar_equipment_instance_t *start_owner,
    const solar_equipment_instance_t *end_owner,
    solar_connection_validation_result_t *out_result) {
    solar_connection_validation_result_init(out_result);
    if (!start || !end || !out_result) {
        if (out_result) out_result->is_valid = false;
        return;
    }

    if (eq_guid(&start->id, &end->id) || is_zero_guid(&start->id) || is_zero_guid(&end->id)) {
        add_issue(out_result, SOLAR_CONN_VALIDATION_SEVERITY_ERROR,
            "SELF_CONNECTION", "Invalid connection",
            "A port cannot connect to itself.", &start->owner_id, 1);
        out_result->is_valid = false;
        return;
    }

    if (start_owner && end_owner && eq_guid(&start_owner->id, &end_owner->id)) {
        solar_guid_t affected[1];
        affected[0] = start_owner->id;
        add_issue(out_result, SOLAR_CONN_VALIDATION_SEVERITY_ERROR,
            "SAME_COMPONENT", "Invalid connection",
            "Cannot directly connect two ports on the same component.", affected, 1);
        out_result->is_valid = false;
        return;
    }

    if (start->is_occupied || end->is_occupied) {
        solar_guid_t affected[2];
        affected[0] = start->owner_id;
        affected[1] = end->owner_id;
        add_issue(out_result, SOLAR_CONN_VALIDATION_SEVERITY_ERROR,
            "PORT_ALREADY_OCCUPIED", "Port occupied",
            "One or more selected terminals are already connected.", affected, 2);
        out_result->is_valid = false;
    }

    int start_port_type = port_equipment_type(start, start_owner);
    int end_port_type = port_equipment_type(end, end_owner);

    /* AC/DC mix is illegal. */
    bool start_ac = is_ac_port_type(start_port_type);
    bool end_ac = is_ac_port_type(end_port_type);
    if (start_ac != end_ac) {
        solar_guid_t affected[2];
        affected[0] = start->owner_id;
        affected[1] = end->owner_id;
        add_issue(out_result, SOLAR_CONN_VALIDATION_SEVERITY_ERROR,
            "AC_DC_MIX", "AC/DC mix",
            "Cannot connect AC terminals to DC terminals.", affected, 2);
        out_result->is_valid = false;
        return;
    }

    bool both_ac = start_ac && end_ac;
    if (both_ac) {
        /* AC pairs are allowed regardless of polarity labels. */
        solar_connection_validator_validate_connector_compatibility(start, end, out_result);
        return;
    }

    bool opposite_polarity = (start->polarity == SOLAR_POLARITY_POSITIVE && end->polarity == SOLAR_POLARITY_NEGATIVE) ||
                             (start->polarity == SOLAR_POLARITY_NEGATIVE && end->polarity == SOLAR_POLARITY_POSITIVE);
    bool same_polarity_branch = (start->polarity == end->polarity) &&
                                (port_is_branch(start, start_owner) || port_is_branch(end, end_owner));
    bool same_polarity_equipment_dc = (start->polarity == end->polarity) &&
                                      (!is_panel_pv_port(start) || !is_panel_pv_port(end));

    if (!opposite_polarity && !same_polarity_branch && !same_polarity_equipment_dc) {
        solar_guid_t affected[2];
        affected[0] = start->owner_id;
        affected[1] = end->owner_id;
        if (start->polarity == SOLAR_POLARITY_POSITIVE && end->polarity == SOLAR_POLARITY_POSITIVE) {
            add_issue(out_result, SOLAR_CONN_VALIDATION_SEVERITY_ERROR,
                "INVALID_SERIES_CONNECTION", "Invalid connection",
                "Positive terminals cannot connect directly. Use an MC4 Y branch connector for parallel wiring.",
                affected, 2);
        } else if (start->polarity == SOLAR_POLARITY_NEGATIVE && end->polarity == SOLAR_POLARITY_NEGATIVE) {
            add_issue(out_result, SOLAR_CONN_VALIDATION_SEVERITY_ERROR,
                "INVALID_SERIES_CONNECTION", "Invalid connection",
                "Negative terminals cannot connect directly. Use an MC4 Y branch connector for parallel wiring.",
                affected, 2);
        } else {
            add_issue(out_result, SOLAR_CONN_VALIDATION_SEVERITY_ERROR,
                "INVALID_SERIES_CONNECTION", "Invalid connection",
                "These terminals are not a valid DC pair.", affected, 2);
        }
        out_result->is_valid = false;
    }

    solar_connection_validator_validate_connector_compatibility(start, end, out_result);
    solar_connection_validator_validate_disconnect_to_inverter(start, end, start_owner, end_owner, out_result);
    solar_connection_validator_validate_battery_rules(start, end, start_owner, end_owner, out_result);

    out_result->is_valid = out_result->error_count == 0;
}

void solar_connection_validator_validate_series(
    const solar_port_t *start,
    const solar_port_t *end,
    const solar_equipment_instance_t *start_owner,
    const solar_equipment_instance_t *end_owner,
    solar_connection_validation_result_t *out_result) {
    solar_connection_validator_validate(start, end, start_owner, end_owner, out_result);
}
