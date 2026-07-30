#include <cstddef>
#include <cstdint>

#include "solar_math.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    if (size < 20) return 0;

    solar_location_t location;
    solar_date_time_t dt;
    location.latitude_deg = static_cast<double>(*(int32_t *)(data + 0)) / 100.0;
    location.longitude_deg = static_cast<double>(*(int32_t *)(data + 4)) / 100.0;
    location.timezone_offset_h = static_cast<double>(*(int32_t *)(data + 8)) / 100.0;
    dt.year = 2000 + (data[12] % 30);
    dt.month = 1 + (data[13] % 12);
    dt.day = 1 + (data[14] % 28);
    dt.hour = data[15] % 24;
    dt.minute = data[16] % 60;
    dt.second = data[17];

    solar_position_t pos;
    solar_position_calculate(&location, &dt, &pos);

    solar_day_info_t info;
    solar_day_info_calculate(dt.year, dt.month, dt.day, &info);
    return 0;
}
