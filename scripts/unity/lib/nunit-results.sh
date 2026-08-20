#!/usr/bin/env bash
###############################################################################
# nunit-results.sh
#
# Summarizes an NUnit3 results.xml and decides whether the run is believable.
#
# Sourced by scripts/unity/run-tests.sh; exercised directly by
# scripts/tests/test-unity-nunit-results.sh.
#
# The gate matters as much as the summary. Unity exits 0 when a -testFilter
# matches nothing, so a run that verified nothing is otherwise indistinguishable
# from a clean one -- the same false-green shape as a "Files checked: 0" report.
# scripts/unity/run-ci-tests.ps1 and .github/actions/verify-unity-results already
# refuse a zero-test run; this is the local path saying the same thing.
###############################################################################

###############################################################################
# parse_nunit_results: Prints a summary and returns non-zero for an unusable run.
#
# Arguments:
#   $1 - Path to the NUnit XML results file
#   $2 - Platform name (for display)
#
# Returns:
#   0 - at least one test ran and none failed
#   1 - the file is missing or unparsable, no tests ran, or tests failed
###############################################################################
parse_nunit_results() {
    local results_file="$1"
    local platform="$2"

    if [[ ! -f "${results_file}" ]]; then
        echo ""
        echo "==> [run-tests] ${platform}: no results at ${results_file} -- tests did not run."
        return 1
    fi

    echo ""
    echo "==> [run-tests] ${platform} Test Results Summary:"
    echo "    ----------------------------------------"

    # Extract attributes from the top-level test-run element
    local total=0
    local passed=0
    local failed=0
    local skipped=0

    # Use simple text parsing to extract counts from the XML
    if command -v python3 > /dev/null 2>&1; then
        # Use Python for reliable XML parsing if available
        local summary
        # Pass file path as argv[1] to avoid injection via string interpolation.
        # Uses local variable instead of backslash-escapes in f-strings for
        # Python < 3.12 compatibility (PEP 701 only available in 3.12+).
        summary=$(python3 -c '
import xml.etree.ElementTree as ET
import sys

try:
    tree = ET.parse(sys.argv[1])
    root = tree.getroot()
    total = root.get("total", "0")
    passed = root.get("passed", "0")
    failed = root.get("failed", "0")
    skipped = root.get("skipped", root.get("inconclusive", "0"))
    print(f"{total} {passed} {failed} {skipped}")

    # Print failed test names
    for test_case in root.iter("test-case"):
        if test_case.get("result", "").lower() in ("failed", "error"):
            name = test_case.get("fullname", test_case.get("name", "unknown"))
            print(f"FAILED: {name}")
except Exception as e:
    print("0 0 0 0", file=sys.stderr)
    print(f"ERROR: {e}", file=sys.stderr)
' "${results_file}" 2>&1) || true

        # Parse the first line for counts.
        #
        # Taken with parameter expansion rather than `echo "${summary}" | head -n 1`: on a failing
        # run ${summary} carries one line per failed test, `head` exits after the first, the
        # producer dies of SIGPIPE, and under `set -o pipefail` this capture reports 141 -- which
        # `set -e` turns into an aborted run at the moment the summary was about to be printed
        # (#465). The larger the failure, the likelier the abort.
        local counts_line
        counts_line="${summary%%$'\n'*}"
        total=$(echo "${counts_line}" | cut -d' ' -f1)
        passed=$(echo "${counts_line}" | cut -d' ' -f2)
        failed=$(echo "${counts_line}" | cut -d' ' -f3)
        skipped=$(echo "${counts_line}" | cut -d' ' -f4)

        echo "    Total:   ${total}"
        echo "    Passed:  ${passed}"
        echo "    Failed:  ${failed}"
        echo "    Skipped: ${skipped}"

        # Print any failed test names
        local failed_tests
        failed_tests=$(echo "${summary}" | tail -n +2)
        if [[ -n "${failed_tests}" ]]; then
            echo ""
            echo "    Failed tests:"
            echo "${failed_tests}" | while IFS= read -r line; do
                echo "      ${line}"
            done
        fi
    else
        # Fallback: basic attribute extraction with bash
        local test_run_line
        test_run_line=$(head -n 5 "${results_file}" | tr ' ' '\n' | tr '>' '\n')

        total=$(echo "${test_run_line}" | sed -n 's/.*total="\([0-9]*\)".*/\1/p' | head -n 1 || true)
        passed=$(echo "${test_run_line}" | sed -n 's/.*passed="\([0-9]*\)".*/\1/p' | head -n 1 || true)
        failed=$(echo "${test_run_line}" | sed -n 's/.*failed="\([0-9]*\)".*/\1/p' | head -n 1 || true)
        skipped=$(echo "${test_run_line}" | sed -n 's/.*skipped="\([0-9]*\)".*/\1/p' | head -n 1 || true)

        total="${total:-0}"
        passed="${passed:-0}"
        failed="${failed:-0}"
        skipped="${skipped:-0}"

        echo "    Total:   ${total}"
        echo "    Passed:  ${passed}"
        echo "    Failed:  ${failed}"
        echo "    Skipped: ${skipped}"
    fi

    echo "    ----------------------------------------"
    echo ""

    local status=0

    if ! [[ "${total}" =~ ^[0-9]+$ ]]; then
        echo "==> [run-tests] ${platform}: could not read a test count from ${results_file}."
        return 1
    fi

    if [[ "${total}" -lt 1 ]]; then
        echo "==> [run-tests] ${platform}: 0 tests ran -- check assembly selection, the --filter" \
            "expression, and the host project's testables."
        status=1
    fi

    if [[ "${failed}" =~ ^[0-9]+$ ]] && [[ "${failed}" -gt 0 ]]; then
        echo "==> [run-tests] ${platform}: ${failed} test(s) failed."
        status=1
    fi

    return "${status}"
}
