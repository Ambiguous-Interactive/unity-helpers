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
#   * The driver lives under `testu01~/` because **Unity compiles any `.c` it can see**. This
#     repository IS the package, so a `.c` anywhere under it is picked up as native plugin source
#     and handed to IL2CPP, which then fails the standalone player build on
#     `fatal error C1083: Cannot open include file: 'bbattery.h'` and never produces
#     GameAssembly.dll. Measured: all four gated standalone legs, twice. A `~` suffix is the
#     directory Unity ignores, the same convention `Samples~` and `Generator~` use.
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
    gcc -O2 -I"${BUILD_ROOT}/install/include" -o "${DRIVER}" "${SCRIPT_DIR}/testu01~/testu01-driver.c" \
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
# Keep diagnostics separate: stderr can interrupt a buffered stdout summary mid-sentence.
# Only the driver's status matters; finishing its fixed sample may SIGPIPE the producer.
control_reports="${TESTU01_CONTROL_REPORT_DIRECTORY:-${BUILD_ROOT}/self-test}"
mkdir -p "${control_reports}"
set +e
"${HOST}" --generator "${CONTROL_GENERATOR}" --width 32 --bytes "${CONTROL_BYTES}" \
    2>"${control_reports}/host.stderr.log" \
    | "${DRIVER}" "${CONTROL_BATTERY}" >"${control_reports}/report.txt" 2>"${control_reports}/driver.stderr.log"
control_status=("${PIPESTATUS[@]}")
set -e

if [[ "${control_status[1]}" -ne 0 ]]; then
    echo "[testu01] SELF-TEST INCONCLUSIVE: the driver exited ${control_status[1]}."
    cat "${control_reports}/driver.stderr.log"
    exit 1
fi
if grep -q 'input stream exhausted' "${control_reports}/driver.stderr.log"; then
    echo "[testu01] SELF-TEST INCONCLUSIVE: ${CONTROL_BATTERY} wanted more than ${CONTROL_BYTES} bytes."
    echo "[testu01] Raise TESTU01_CONTROL_BYTES. Reporting this as a harness fault, not a result."
    exit 1
fi

node --input-type=module - "${SCRIPT_DIR}/evaluate-testu01.mjs" "${control_reports}/report.txt" <<'JS'
import { readFileSync } from "node:fs";
import { pathToFileURL } from "node:url";
const { verdict } = await import(pathToFileURL(process.argv[2]).href);
const result = verdict(readFileSync(process.argv[3], "utf8"));
if (!result.ranBattery || !result.failed) {
    console.error(result.ranBattery
        ? "[testu01] SELF-TEST FAILED: the control has no decisive failure."
        : "[testu01] SELF-TEST INCONCLUSIVE: the control has no battery summary.");
    process.exitCode = 1;
} else {
    console.log("[testu01] Self-test passed; the harness detects decisive failures:");
    for (const row of result.decisive) console.log(`  ${row.test}: ${row.raw}`);
}
JS

echo "[testu01] driver: ${DRIVER}"
