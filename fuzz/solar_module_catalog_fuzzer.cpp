#include <cstddef>
#include <cstdint>

#include "solar_module_catalog.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    solar_module_catalog_parse(data, size);
    return 0;
}
