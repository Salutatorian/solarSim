#!/bin/bash -eu
#
# ClusterFuzzLite build entry point for solarSim.
#
# Environment provided by the runner:
#   $SRC   - repository root
#   $OUT   - directory where fuzzer binaries must be written
#   $WORK  - scratch workspace
#   $CC, $CXX, $CFLAGS, $CXXFLAGS, $LIB_FUZZING_ENGINE

set -o pipefail

SRC="${SRC:-$(pwd)}"
OUT="${OUT:-${SRC}/out}"
WORK="${WORK:-${SRC}/work}"

mkdir -p "${OUT}"
mkdir -p "${WORK}/native_build"

# Native library: solar module catalog parser.
"${CXX}" ${CXXFLAGS} -I"${SRC}/src/SolarSim.Native/include" \
    -c "${SRC}/src/SolarSim.Native/src/solar_module_catalog.cpp" \
    -o "${WORK}/native_build/solar_module_catalog.o"

# Fuzzer harness.
"${CXX}" ${CXXFLAGS} -I"${SRC}/src/SolarSim.Native/include" \
    -c "${SRC}/fuzz/solar_module_catalog_fuzzer.cpp" \
    -o "${WORK}/native_build/solar_module_catalog_fuzzer.o"

# Link fuzzer into $OUT.
"${CXX}" ${CXXFLAGS} ${LIB_FUZZING_ENGINE} \
    "${WORK}/native_build/solar_module_catalog.o" \
    "${WORK}/native_build/solar_module_catalog_fuzzer.o" \
    -o "${OUT}/solar_module_catalog_fuzzer"

# Package seeds as an OSS-Fuzz-style seed corpus zip. The runner also
# auto-discovers corpus/ directories, but this guarantees seeds are used.
CORPUS_DIR="${SRC}/fuzz/corpus/solar_module_catalog_fuzzer"
if [ -d "${CORPUS_DIR}" ] && command -v zip >/dev/null 2>&1; then
    zip -j -r "${OUT}/solar_module_catalog_fuzzer_seed_corpus.zip" \
        "${CORPUS_DIR}"
fi

echo "Build complete: ${OUT}/solar_module_catalog_fuzzer"
ls -la "${OUT}"
