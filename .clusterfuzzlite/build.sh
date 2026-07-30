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

INC="${SRC}/src/SolarSim.Native/include"

# Compile all native library source files into object files.
NATIVE_SRCS=()
for src in "${SRC}/src/SolarSim.Native/src/"*.cpp; do
    if [ -f "${src}" ]; then
        NATIVE_SRCS+=("${src}")
    fi
done

NATIVE_OBJS=()
for src in "${NATIVE_SRCS[@]}"; do
    obj="${WORK}/native_build/$(basename "${src}" .cpp).o"
    "${CXX}" ${CXXFLAGS} -I"${INC}" -c "${src}" -o "${obj}"
    NATIVE_OBJS+=("${obj}")
done

# Build every fuzzer harness discovered in fuzz/.
for fuzzer_src in "${SRC}/fuzz/"*_fuzzer.cpp; do
    if [ ! -f "${fuzzer_src}" ]; then
        continue
    fi
    name=$(basename "${fuzzer_src}" .cpp)
    "${CXX}" ${CXXFLAGS} -I"${INC}" \
        -c "${fuzzer_src}" \
        -o "${WORK}/native_build/${name}.o"
    "${CXX}" ${CXXFLAGS} ${LIB_FUZZING_ENGINE} \
        "${NATIVE_OBJS[@]}" \
        "${WORK}/native_build/${name}.o" \
        -o "${OUT}/${name}"

    corpus_dir="${SRC}/fuzz/corpus/${name}"
    if [ -d "${corpus_dir}" ] && command -v zip >/dev/null 2>&1; then
        zip -j -r "${OUT}/${name}_seed_corpus.zip" "${corpus_dir}"
    fi
done

echo "Build complete."
ls -la "${OUT}"
