#!/usr/bin/env bash
###############################################################################
# test-unity-nunit-results.sh
#
# Contract test for scripts/unity/lib/nunit-results.sh.
#
# The case that matters is the zero-test run: Unity exits 0 when a -testFilter
# matches nothing, so before #400 a run that verified nothing printed the same
# "All tests PASSED." as a run that verified everything.
#
# No Unity and no license required -- every fixture is a synthetic results.xml.
###############################################################################
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
REPO_ROOT="$(cd "${REPO_ROOT}/.." && pwd)"
LIBRARY="${REPO_ROOT}/scripts/unity/lib/nunit-results.sh"

# shellcheck source=scripts/unity/lib/nunit-results.sh
source "${LIBRARY}"

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "${WORK_DIR}"' EXIT

FAILURES=0
CHECKS=0

write_results() {
    local name="$1"
    local attributes="$2"
    local body="${3:-}"
    local path="${WORK_DIR}/${name}.xml"
    {
        echo '<?xml version="1.0" encoding="utf-8"?>'
        if [[ -n "${body}" ]]; then
            echo "<test-run ${attributes}>${body}</test-run>"
        else
            echo "<test-run ${attributes} />"
        fi
    } > "${path}"
    echo "${path}"
}

expect_status() {
    local label="$1"
    local expected="$2"
    local results_path="$3"
    local expected_text="${4:-}"

    CHECKS=$((CHECKS + 1))

    local output=""
    local actual=0
    output="$(parse_nunit_results "${results_path}" "EditMode" 2>&1)" || actual=$?

    if [[ "${actual}" -ne "${expected}" ]]; then
        echo "FAIL: ${label}: expected exit ${expected}, got ${actual}"
        echo "${output}" | sed 's/^/      /'
        FAILURES=$((FAILURES + 1))
        return
    fi

    if [[ -n "${expected_text}" ]] && ! echo "${output}" | grep -qF -- "${expected_text}"; then
        echo "FAIL: ${label}: output did not mention '${expected_text}'"
        echo "${output}" | sed 's/^/      /'
        FAILURES=$((FAILURES + 1))
        return
    fi

    echo "PASS: ${label}"
}

echo "==> [test-unity-nunit-results] Testing ${LIBRARY}"

PASSING="$(write_results passing 'id="2" testcasecount="3" result="Passed" total="3" passed="3" failed="0" inconclusive="0" skipped="0" asserts="3"')"
expect_status "a run with passing tests succeeds" 0 "${PASSING}"

ZERO="$(write_results zero 'id="2" testcasecount="0" result="Passed" total="0" passed="0" failed="0" inconclusive="0" skipped="0" asserts="0"')"
expect_status "a filter that matched nothing fails" 1 "${ZERO}" "0 tests ran"

FAILED="$(write_results failed 'id="2" testcasecount="2" result="Failed" total="2" passed="1" failed="1" inconclusive="0" skipped="0" asserts="2"' '<test-case name="Broken" fullname="Suite.Broken" result="Failed" />')"
expect_status "a run with a failed test fails" 1 "${FAILED}" "Suite.Broken"

SKIPPED="$(write_results skipped 'id="2" testcasecount="2" result="Passed" total="2" passed="0" failed="0" inconclusive="0" skipped="2" asserts="0"')"
expect_status "a run where every test was skipped still counts as run" 0 "${SKIPPED}"

expect_status "a missing results file fails" 1 "${WORK_DIR}/absent.xml" "tests did not run"

MALFORMED="${WORK_DIR}/malformed.xml"
printf '<test-run total=' > "${MALFORMED}"
expect_status "an unparsable results file fails" 1 "${MALFORMED}"

echo ""
if [[ "${FAILURES}" -eq 0 ]]; then
    echo "==> [test-unity-nunit-results] ${CHECKS} checks passed."
    exit 0
fi

echo "==> [test-unity-nunit-results] ${FAILURES} of ${CHECKS} checks FAILED."
exit 1
