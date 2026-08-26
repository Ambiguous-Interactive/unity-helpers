#!/usr/bin/env bash
# Builds TestU01 from source and refuses to hand back a binary that cannot fail.
#
# Same contract as build-practrand.sh, and for the same reason: a statistical battery that reports
# "All tests were passed" is indistinguishable from one that is not testing anything, which is the
# failure class https://github.com/Ambiguous-Interactive/unity-helpers/issues/556 is about. This
# script runs a generator whose weakness is already recorded in expected-outcomes.json and refuses
# to succeed unless TestU01 reports it.
#
# Two things that cost a build each and are therefore pinned here rather than rediscovered:
#
#   * The canonical download host `simul.iro.umontreal.ca` returns 404. The archive is served from
#     the author's directory, below.
#   * `make install` produces shared libraries, and a driver linked against them fails at run time
#     with `libtestu01.so.0: cannot open shared object file` unless LD_LIBRARY_PATH is set. The
#     driver is linked against the STATIC archives so the binary is self-contained, which is what a
#     CI leg and a developer's shell both want.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

TESTU01_URL="${TESTU01_URL:-http://www.iro.umontreal.ca/~simardr/testu01/TestU01.zip}"
BUILD_ROOT="${TESTU01_BUILD_ROOT:-${TMPDIR:-/tmp}/testu01-build}"
CONTROL_GENERATOR="${TESTU01_CONTROL_GENERATOR:-XorShiftRandom}"
CONTROL_BATTERY="${TESTU01_CONTROL_BATTERY:-SmallCrush}"
# Measured: SmallCrush drew 227,019,838 words (908 MB) and cost 6.33 s of CPU. The budget is set
# above that rather than at it, because running out is a harness fault the driver reports explicitly
# while over-supplying costs only an unread tail the producer is SIGPIPEd out of.
CONTROL_BYTES="${TESTU01_CONTROL_BYTES:-2000000000}"

mkdir -p "${BUILD_ROOT}"
DRIVER="${BUILD_ROOT}/wallstop-testu01"

if [[ ! -x "${DRIVER}" ]]; then
    if [[ ! -d "${BUILD_ROOT}/install/include" ]]; then
        echo "[testu01] Fetching TestU01 into ${BUILD_ROOT}"
        curl -sSL -o "${BUILD_ROOT}/TestU01.zip" "${TESTU01_URL}"
        unzip -q -o "${BUILD_ROOT}/TestU01.zip" -d "${BUILD_ROOT}"

        source_dir="$(find "${BUILD_ROOT}" -maxdepth 1 -type d -name 'TestU01-*' | head -1)"
        if [[ -z "${source_dir}" ]]; then
            echo "[testu01] The archive did not contain a TestU01-* directory. Refusing to guess."
            exit 1
        fi

        echo "[testu01] Configuring and compiling (gcc $(gcc -dumpversion))"
        (
            cd "${source_dir}"
            ./configure --prefix="${BUILD_ROOT}/install" >"${BUILD_ROOT}/configure.log" 2>&1
            make -j"$(nproc)" >"${BUILD_ROOT}/make.log" 2>&1
            make install >"${BUILD_ROOT}/install.log" 2>&1
        )
    fi

    echo "[testu01] Linking the stream driver"
    gcc -O2 -I"${BUILD_ROOT}/install/include" -o "${DRIVER}" "${SCRIPT_DIR}/testu01-driver.c" \
        "${BUILD_ROOT}/install/lib/libtestu01.a" \
        "${BUILD_ROOT}/install/lib/libprobdist.a" \
        "${BUILD_ROOT}/install/lib/libmylib.a" -lm
fi

HOST_DIR="${REPO_ROOT}/Generator~/WallstopStudios.UnityHelpers.RandomQuality"
HOST="${HOST_DIR}/bin/Release/net9.0/WallstopStudios.UnityHelpers.RandomQuality"
if [[ ! -x "${HOST}" ]]; then
    echo "[testu01] Building the deterministic stream host"
    dotnet build "${HOST_DIR}" -c Release --nologo -v quiet
fi

echo "[testu01] Self-test: ${CONTROL_GENERATOR} must fail ${CONTROL_BATTERY}, or this binary proves nothing"
# pipefail is disabled for this one command: the battery stops reading once it has drawn its fixed
# number of words, which SIGPIPEs the producer. Under `set -o pipefail` that would abort the script
# before the verdict below, turning the one check that matters into a bare non-zero exit.
set +o pipefail
control_output="$("${HOST}" --generator "${CONTROL_GENERATOR}" --width 32 --bytes "${CONTROL_BYTES}" 2>/dev/null \
    | "${DRIVER}" "${CONTROL_BATTERY}" 2>&1 || true)"
set -o pipefail

if grep -q 'input stream exhausted' <<<"${control_output}"; then
    echo "[testu01] SELF-TEST INCONCLUSIVE: ${CONTROL_BATTERY} wanted more than ${CONTROL_BYTES} bytes."
    echo "[testu01] Raise TESTU01_CONTROL_BYTES. Reporting this as a harness fault, not a result."
    exit 1
fi

# TestU01 prints exactly one of two verdicts. "All tests were passed" is the clean one; the other
# names the statistics that fell outside [0.001, 0.9990]. Matching the second phrase rather than the
# absence of the first is deliberate: "All OTHER tests were passed" also appears in a failing report,
# so a negated match on the clean phrase would have to be careful in a way this does not.
if ! grep -q 'The following tests gave p-values outside' <<<"${control_output}"; then
    echo "[testu01] SELF-TEST FAILED: ${CONTROL_GENERATOR} passed ${CONTROL_BATTERY} cleanly."
    echo "[testu01] expected-outcomes.json records that it is weak, so either the harness, the host"
    echo "[testu01] or the manifest is wrong. Refusing to report this build as usable."
    tail -20 <<<"${control_output}"
    exit 1
fi

echo "[testu01] Self-test passed; the harness detects the failure class:"
# The failing statistics are numbered rows in the summary table. Printing them is not decoration:
# a self-test that reports "passed" without showing what it caught is the thing it exists to stop.
grep -E '^ +[0-9]+ +[A-Za-z][A-Za-z0-9 ]* +(eps|1 - eps|[0-9])' <<<"${control_output}" | head -6
echo "[testu01] driver: ${DRIVER}"
