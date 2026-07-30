#include <cstdint>
#include <cstring>
#include <cstdlib>

#include "equipment_library.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *Data, size_t Size) {
    if (Size < 2) return 0;

    solar_inverter_definition_t built_in[8];
    size_t built_in_count = 0;
    solar_equipment_built_in_inverters(built_in, 8, &built_in_count);
    if (built_in_count == 0) return 0;

    solar_guid_t id;
    solar_panel_guid_from_u64_pair(&id, 0xF0, 0x1000 + Data[0]);

    uint8_t choice = Data[0] % 8;
    solar_equipment_instance_t inst;

    switch (choice) {
        case 0: {
            int inputs = static_cast<int>((Data[1] % 12) + 1);
            solar_equipment_create_combiner(&inst, &id, 0.0, 0.0, inputs, "fuzzer");
            break;
        }
        case 1:
            solar_equipment_create_pv_disconnect(&inst, &id, 0.0, 0.0, "fuzzer");
            break;
        case 2: {
            solar_equipment_polarity_t polarity = (Data[1] % 2 == 0)
                ? SOLAR_EQUIPMENT_POLARITY_POSITIVE
                : SOLAR_EQUIPMENT_POLARITY_NEGATIVE;
            solar_equipment_create_branch_y(&inst, &id, 0.0, 0.0, polarity, "fuzzer");
            break;
        }
        case 3: {
            size_t inv_idx = Data[1] % built_in_count;
            solar_equipment_create_string_inverter(&inst, &id, 0.0, 0.0, &built_in[inv_idx], "fuzzer");
            break;
        }
        case 4:
            solar_equipment_create_ac_disconnect(&inst, &id, 0.0, 0.0, "fuzzer");
            break;
        case 5:
            solar_equipment_create_ac_load_center(&inst, &id, 0.0, 0.0, "fuzzer");
            break;
        case 6: {
            uint8_t battery_type = Data[1] % 4;
            switch (battery_type) {
                case 0: solar_equipment_create_battery_16kwh(&inst, &id, 0.0, 0.0, "fuzzer"); break;
                case 1: solar_equipment_create_battery_10kw_wall(&inst, &id, 0.0, 0.0, "fuzzer"); break;
                case 2: solar_equipment_create_battery_5_1kwh_rack(&inst, &id, 0.0, 0.0, "fuzzer"); break;
                default: solar_equipment_create_battery_12_8v_300ah(&inst, &id, 0.0, 0.0, "fuzzer"); break;
            }
            break;
        }
        default: {
            int rated_amps = static_cast<int>(Data[1] * 10);
            solar_equipment_create_battery_disconnect(&inst, &id, 0.0, 0.0, "fuzzer", rated_amps, "DHM1B");
            break;
        }
    }

    solar_equipment_instance_is_valid(&inst);
    solar_equipment_is_battery_dual_terminal(&inst);
    solar_equipment_is_battery_prismatic(&inst);
    solar_equipment_is_battery_rack(&inst);
    solar_equipment_is_battery_10kw_wall(&inst);

    if (Size >= 4) {
        solar_equipment_instance_set_size(&inst, static_cast<double>(Data[2]) * 10.0, static_cast<double>(Data[3]) * 10.0);
    }
    if (Size >= 6) {
        int rot = static_cast<int>(Data[4]) | (static_cast<int>(Data[5]) << 8);
        solar_equipment_instance_set_rotation(&inst, static_cast<double>(rot));
    }

    return 0;
}
