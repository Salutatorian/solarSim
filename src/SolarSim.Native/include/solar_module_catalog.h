#ifndef SOLAR_MODULE_CATALOG_H
#define SOLAR_MODULE_CATALOG_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/*
 * Solar Module Catalog (SMC) binary format
 *
 * Header (16 bytes):
 *   magic[4]        = "SMC\0"
 *   version (u32)   = 1
 *   module_count    (u32)
 *   reserved        (u32)   must be 0
 *
 * Each module entry:
 *   name_len        (u32)
 *   name            (name_len bytes, no terminator stored)
 *   pmax            (f64)   STC max power, watts
 *   vmp             (f64)   max-power voltage, volts
 *   imp             (f64)   max-power current, amps
 *   voc             (f64)   open-circuit voltage, volts
 *   isc             (f64)   short-circuit current, amps
 *   vmp_temp_coeff  (f64)   Vmp temperature coefficient, %/C
 *   width_mm        (u32)
 *   height_mm       (u32)
 *   tag_count       (u32)
 *   tags: for each tag:
 *       tag_len     (u32)
 *       tag         (tag_len bytes)
 *
 * Returns 0 on successful parse, -1 on malformed input.
 */
int solar_module_catalog_parse(const uint8_t *data, size_t size);

#ifdef __cplusplus
}
#endif

#endif
