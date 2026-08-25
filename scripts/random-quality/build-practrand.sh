#!/usr/bin/env bash
# Builds PractRand from source and refuses to hand back a binary that cannot fail.
#
# PractRand ships no build system, so the compile command IS the pin: it is recorded here rather
# than in a comment somewhere, together with the toolchain it was proven on.
#
# Proven 2026-08-24 on Debian 12 (bookworm), g++ 12.2.0, PractRand pre0.95.
#
# The self-test is not optional. A statistical battery that reports "no anomalies" is
# indistinguishable from one that is not testing anything, which is the failure class
# https://github.com/Ambiguous-Interactive/unity-helpers/issues/556 is about. This script runs a
# generator whose failure is recorded in expected-outcomes.json and refuses to succeed unless
# PractRand reports it.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

PRACTRAND_URL="${PRACTRAND_URL:-https://sourceforge.net/projects/pracrand/files/PractRand-pre0.95.zip/download}"
BUILD_ROOT="${PRACTRAND_BUILD_ROOT:-${TMPDIR:-/tmp}/practrand-build}"
CONTROL_GENERATOR="${PRACTRAND_CONTROL_GENERATOR:-XorShiftRandom}"
CONTROL_BYTES="${PRACTRAND_CONTROL_BYTES:-16777216}"

mkdir -p "${BUILD_ROOT}"

if [[ ! -x "${BUILD_ROOT}/RNG_test" ]]; then
    echo "[practrand] Fetching PractRand into ${BUILD_ROOT}"
    curl -sSL -o "${BUILD_ROOT}/practrand.zip" "${PRACTRAND_URL}"
    unzip -q -o "${BUILD_ROOT}/practrand.zip" -d "${BUILD_ROOT}/src-tree"

    echo "[practrand] Compiling (g++ $(g++ -dumpversion))"
    (
        cd "${BUILD_ROOT}/src-tree"
        # -std=c++14 because PractRand pre0.95 predates the stricter defaults of newer standards;
        # -Iinclude because its headers are not installed anywhere.
        g++ -std=c++14 -O2 -Iinclude -c src/*.cpp src/RNGs/*.cpp src/RNGs/other/*.cpp
        g++ -std=c++14 -O2 -Iinclude -o "${BUILD_ROOT}/RNG_test" tools/RNG_test.cpp ./*.o -pthread
    )
fi

echo "[practrand] $("${BUILD_ROOT}/RNG_test" -version 2>&1 | head -1)"

HOST_DIR="${REPO_ROOT}/Generator~/WallstopStudios.UnityHelpers.RandomQuality"
HOST="${HOST_DIR}/bin/Release/net9.0/WallstopStudios.UnityHelpers.RandomQuality"
if [[ ! -x "${HOST}" ]]; then
    echo "[practrand] Building the deterministic stream host"
    dotnet build "${HOST_DIR}" -c Release --nologo -v quiet
fi

echo "[practrand] Self-test: ${CONTROL_GENERATOR} must FAIL, or this binary proves nothing"
# `|| true` and a disabled pipefail for this one command: RNG_test stops reading at -tlmax, which
# SIGPIPEs the producer, and under `set -euo pipefail` that would abort the script before the verdict
# below -- turning the one check that matters into a bare non-zero exit.
set +o pipefail
control_output="$("${HOST}" --generator "${CONTROL_GENERATOR}" --width 32 --bytes "${CONTROL_BYTES}" \
    | "${BUILD_ROOT}/RNG_test" stdin32 -tlmin 1KB -tlmax 16MB -tf 2 -te 0 -multithreaded 2>&1 || true)"
set -o pipefail

if ! grep -q 'FAIL' <<<"${control_output}"; then
    echo "[practrand] SELF-TEST FAILED: ${CONTROL_GENERATOR} produced no FAIL through ${CONTROL_BYTES} bytes."
    echo "[practrand] expected-outcomes.json records that it fails, so either the harness, the host"
    echo "[practrand] or the manifest is wrong. Refusing to report this build as usable."
    echo "${control_output}" | tail -20
    exit 1
fi

echo "[practrand] Self-test passed; the harness detects the failure class:"
grep 'FAIL' <<<"${control_output}" | head -3
echo "[practrand] RNG_test: ${BUILD_ROOT}/RNG_test"
