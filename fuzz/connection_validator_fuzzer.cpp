#include <cstddef>
#include <cstdint>
#include <cstring>

#include "connection_validator.h"
#include "electrical_graph.h"
#include "mppt_compatibility.h"
#include "solar_panel.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size < 2 * sizeof(solar_equipment_port_t)) return 0;

    solar_equipment_port_t start_port;
    solar_equipment_port_t end_port;
    std::memcpy(&start_port, data, sizeof(start_port));
    std::memcpy(&end_port, data + sizeof(start_port), sizeof(end_port));
    start_port.base.connector_family[sizeof(start_port.base.connector_family) - 1] = '\0';
    end_port.base.connector_family[sizeof(end_port.base.connector_family) - 1] = '\0';
    start_port.label[sizeof(start_port.label) - 1] = '\0';
    end_port.label[sizeof(end_port.label) - 1] = '\0';

    solar_equipment_instance_t start_owner;
    solar_equipment_instance_t end_owner;
    solar_guid_t id1, id2;
    solar_panel_guid_from_u64_pair(&id1, 0, 1);
    solar_panel_guid_from_u64_pair(&id2, 0, 2);
    solar_mppt_equipment_instance_init(&start_owner, &id1, SOLAR_EQUIPMENT_STRING_INVERTER, "Inv1");
    solar_mppt_equipment_instance_init(&end_owner, &id2, SOLAR_EQUIPMENT_BATTERY, "Bat1");

    solar_connection_validation_result_t result;
    solar_connection_validator_validate(&start_port.base, &end_port.base, &start_owner, &end_owner, &result);
    return 0;
}
