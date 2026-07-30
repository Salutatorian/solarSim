#include "solar_module_catalog.h"

#include <cmath>
#include <cstdint>
#include <cstring>

#define SMC_MAGIC "SMC\0"
#define MAX_NAME_LEN 256
#define MAX_TAG_COUNT 64
#define MAX_MODULES 1024

struct smc_header {
    uint8_t magic[4];
    uint32_t version;
    uint32_t module_count;
    uint32_t reserved;
};

static int read_u32(const uint8_t *p, uint32_t *out) {
    *out = (uint32_t)p[0] |
           ((uint32_t)p[1] << 8) |
           ((uint32_t)p[2] << 16) |
           ((uint32_t)p[3] << 24);
    return 0;
}

static int read_f64(const uint8_t *p, double *out) {
    std::memcpy(out, p, sizeof(double));
    return 0;
}

static int validate_electrical(const double *values, size_t count) {
    for (size_t i = 0; i < count; i++) {
        if (values[i] <= 0.0) return -1;
        if (!std::isfinite(values[i])) return -1;
    }
    return 0;
}

int solar_module_catalog_parse(const uint8_t *data, size_t size) {
    if (size < sizeof(smc_header)) {
        return -1;
    }

    struct smc_header hdr;
    std::memcpy(&hdr, data, sizeof(hdr));
    if (std::memcmp(hdr.magic, SMC_MAGIC, 4) != 0) {
        return -1;
    }
    if (hdr.version != 1) {
        return -1;
    }
    if (hdr.reserved != 0) {
        return -1;
    }
    if (hdr.module_count == 0 || hdr.module_count > MAX_MODULES) {
        return -1;
    }

    const uint8_t *p = data + sizeof(smc_header);
    const uint8_t *end = data + size;

    double total_pmax = 0.0;
    double max_voc = 0.0;

    for (uint32_t i = 0; i < hdr.module_count; i++) {
        if (p + 4 > end) {
            return -1;
        }
        uint32_t name_len;
        read_u32(p, &name_len);
        p += 4;

        if (name_len > MAX_NAME_LEN) {
            return -1;
        }
        if (p + name_len > end) {
            return -1;
        }

        char name[MAX_NAME_LEN];
        std::memcpy(name, p, name_len);
        name[name_len] = '\0';

        p += name_len;

        const size_t electrical_size = 6 * sizeof(double) + 2 * sizeof(uint32_t);
        if (p + electrical_size > end) {
            return -1;
        }

        double electrical[6];
        for (size_t j = 0; j < 6; j++) {
            read_f64(p, &electrical[j]);
            p += sizeof(double);
        }
        if (validate_electrical(electrical, 6) != 0) {
            return -1;
        }

        uint32_t width_mm, height_mm;
        read_u32(p, &width_mm);
        p += 4;
        read_u32(p, &height_mm);
        p += 4;
        if (width_mm == 0 || height_mm == 0) {
            return -1;
        }

        total_pmax += electrical[0];
        if (electrical[3] > max_voc) {
            max_voc = electrical[3];
        }

        if (p + 4 > end) {
            return -1;
        }
        uint32_t tag_count;
        read_u32(p, &tag_count);
        p += 4;
        if (tag_count > MAX_TAG_COUNT) {
            return -1;
        }

        for (uint32_t j = 0; j < tag_count; j++) {
            if (p + 4 > end) {
                return -1;
            }
            uint32_t tag_len;
            read_u32(p, &tag_len);
            p += 4;
            if (p + tag_len > end) {
                return -1;
            }
            p += tag_len;
        }
    }

    if (max_voc > 1000.0) {
        return -1;
    }

    (void)total_pmax;
    return 0;
}
