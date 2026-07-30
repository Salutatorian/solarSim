#include <cstddef>
#include <cstdint>

#include "project_file.h"

extern "C" int LLVMFuzzerTestOneInput(const uint8_t *data, size_t size) {
    solar_project_t project;
    solar_project_init(&project);
    if (solar_project_file_parse(data, size, &project)) {
        solar_project_file_validate(&project);
    }
    return 0;
}
