#!/usr/bin/env bash
set -euo pipefail

###############################################################################
# run-tests.sh
#
# Runs Unity tests (EditMode, PlayMode, or both) for the Unity package.
#
# Environment variables:
#   UNITY_VERSION          - Unity Editor version (default: 2021.3.45f1)
#   UNITY_IMAGE_VERSION    - GameCI image version (default: 3)
#   UNITY_LICENSE          - Contents of .ulf license file
#   UNITY_SERIAL           - Pro license serial key
#   UNITY_EMAIL            - Unity account email
#   UNITY_PASSWORD         - Unity account password
#   UNITY_TEST_PROJECT_DIR - Path to test project (default: /home/vscode/.unity-test-project)
#
# Flags:
#   --mode MODE       Test mode: editmode (default), playmode, or all
#   --filter FILTER   Test name filter expression
#   --assembly NAME   Specific test assembly name
#   --clean           Force recreation of the test project before testing
#
# Usage:
#   ./run-tests.sh
#   ./run-tests.sh --mode playmode
#   ./run-tests.sh --mode all --filter "TestClassName"
#   ./run-tests.sh --assembly "WallstopStudios.UnityHelpers.Tests.Editor"
###############################################################################

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORKSPACE_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
UNITY_TEST_PROJECT_DIR="${UNITY_TEST_PROJECT_DIR:-/home/vscode/.unity-test-project}"

MODE="editmode"
FILTER=""
ASSEMBLY=""
CLEAN_FLAG=""

# Parse arguments
while [[ $# -gt 0 ]]; do
    case "$1" in
        --mode)
            MODE="$2"
            shift 2
            ;;
        --filter)
            FILTER="$2"
            shift 2
            ;;
        --assembly)
            ASSEMBLY="$2"
            shift 2
            ;;
        --clean)
            CLEAN_FLAG="--force"
            shift
            ;;
        *)
            echo "WARNING: Unknown argument: $1"
            shift
            ;;
    esac
done

# Validate mode
case "${MODE}" in
    editmode|playmode|all)
        ;;
    *)
        echo "ERROR: Invalid mode '${MODE}'. Must be editmode, playmode, or all."
        exit 1
        ;;
esac

echo "==> [run-tests] Test mode: ${MODE}"
if [[ -n "${FILTER}" ]]; then
    echo "==> [run-tests] Test filter: ${FILTER}"
fi
if [[ -n "${ASSEMBLY}" ]]; then
    echo "==> [run-tests] Test assembly: ${ASSEMBLY}"
fi

# Validate the results path before creating the test project. Writing a Unity
# project or test output under WORKSPACE_DIR makes those generated files part of
# the imported package and can trigger Unity import-loop failures.
RESULTS_DIR="${UNITY_TEST_PROJECT_DIR}/test-results"
workspace_realpath="$(cd "${WORKSPACE_DIR}" && pwd -P)"
results_realpath="$(realpath -m "${RESULTS_DIR}")"
case "${results_realpath}" in
    "${workspace_realpath}"|"${workspace_realpath}"/*)
        echo "ERROR: Refusing to write Unity test results inside the package root: ${results_realpath}"
        echo "ERROR: Set UNITY_TEST_PROJECT_DIR outside ${workspace_realpath} to avoid Unity package import loops."
        exit 1
        ;;
esac

# Step 1: Ensure test project exists
echo "==> [run-tests] Step 1: Ensuring test project exists..."
if [[ -n "${CLEAN_FLAG}" ]]; then
    "${SCRIPT_DIR}/create-test-project.sh" --force
else
    "${SCRIPT_DIR}/create-test-project.sh"
fi

# Create test results directory inside the test project (writable Docker mount).
# Keep generated results outside WORKSPACE_DIR: the Unity test project imports
# this package via file:/workspace, so result files or symlinks in the package
# root become package assets and can trigger import-loop errors while tests run.
mkdir -p "${RESULTS_DIR}"
echo "==> [run-tests] Results directory: ${RESULTS_DIR}"

# Remove the legacy workspace-root symlink created by older versions of this
# script. A real directory is left intact so user-authored artifacts are not
# deleted implicitly; the results location is printed above instead.
WORKSPACE_RESULTS="${WORKSPACE_DIR}/test-results"
if [[ -L "${WORKSPACE_RESULTS}" ]]; then
    rm -f "${WORKSPACE_RESULTS}"
elif [[ -e "${WORKSPACE_RESULTS}" ]]; then
    echo "WARNING: ${WORKSPACE_RESULTS} exists inside the Unity package root."
    echo "WARNING: Generated test results should live at ${RESULTS_DIR} to avoid Unity package import loops."
fi

###############################################################################
# run_test_mode: Runs tests for a single platform (EditMode or PlayMode).
#
# Arguments:
#   $1 - Platform name (EditMode or PlayMode)
###############################################################################
run_test_mode() {
    local platform="$1"
    local platform_lower
    platform_lower="$(echo "${platform}" | tr '[:upper:]' '[:lower:]')"
    # Results are written to /project/test-results/ inside Docker (writable mount)
    # and read from UNITY_TEST_PROJECT_DIR/test-results/ on the host.
    local results_file="/project/test-results/${platform_lower}-results.xml"
    local local_results_file="${UNITY_TEST_PROJECT_DIR}/test-results/${platform_lower}-results.xml"

    echo "==> [run-tests] Running ${platform} tests..."
    local start_time
    start_time=$(date +%s)

    # Build Unity arguments as an array to preserve argument boundaries
    local -a unity_args=(
        -batchmode -nographics -projectPath /project
        -runTests -testPlatform "${platform}"
        -testResults "${results_file}"
        -logFile -
    )

    if [[ -n "${FILTER}" ]]; then
        unity_args+=(-testFilter "${FILTER}")
    fi

    if [[ -n "${ASSEMBLY}" ]]; then
        unity_args+=(-assemblyNames "${ASSEMBLY}")
    fi

    # PlayMode tests need xvfb. Use env prefix to avoid polluting parent shell.
    local xvfb_flag=0
    if [[ "${platform}" == "PlayMode" ]]; then
        xvfb_flag=1
    fi

    local exit_code=0
    UNITY_USE_XVFB="${xvfb_flag}" "${SCRIPT_DIR}/run-unity-docker.sh" "${unity_args[@]}" || exit_code=$?

    local end_time
    end_time=$(date +%s)
    local elapsed=$((end_time - start_time))

    echo "==> [run-tests] ${platform} tests completed in ${elapsed}s (exit code: ${exit_code})."

    # Parse NUnit XML results if the file exists
    if [[ -f "${local_results_file}" ]]; then
        parse_nunit_results "${local_results_file}" "${platform}"
        # Surface the slowest fixtures/cases so local runs see what to optimize.
        if command -v pwsh > /dev/null 2>&1; then
            pwsh -NoProfile -File "${SCRIPT_DIR}/report-slow-tests.ps1" \
                -ResultsPath "${local_results_file}" -Top 20 || true
        fi
    else
        echo "    WARNING: Results file not found at ${local_results_file}"
    fi

    return "${exit_code}"
}

###############################################################################
# parse_nunit_results: Parses an NUnit XML results file and prints a summary.
#
# Arguments:
#   $1 - Path to the NUnit XML results file
#   $2 - Platform name (for display)
###############################################################################
parse_nunit_results() {
    local results_file="$1"
    local platform="$2"

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

        # Parse the first line for counts
        local counts_line
        counts_line=$(echo "${summary}" | head -n 1)
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

        total=$(echo "${test_run_line}" | sed -n 's/.*total="\([0-9]*\)".*/\1/p' | head -n 1)
        passed=$(echo "${test_run_line}" | sed -n 's/.*passed="\([0-9]*\)".*/\1/p' | head -n 1)
        failed=$(echo "${test_run_line}" | sed -n 's/.*failed="\([0-9]*\)".*/\1/p' | head -n 1)
        skipped=$(echo "${test_run_line}" | sed -n 's/.*skipped="\([0-9]*\)".*/\1/p' | head -n 1)

        echo "    Total:   ${total:-0}"
        echo "    Passed:  ${passed:-0}"
        echo "    Failed:  ${failed:-0}"
        echo "    Skipped: ${skipped:-0}"
    fi

    echo "    ----------------------------------------"
    echo ""
}

# Run tests based on mode
OVERALL_EXIT=0

case "${MODE}" in
    editmode)
        run_test_mode "EditMode" || OVERALL_EXIT=$?
        ;;
    playmode)
        run_test_mode "PlayMode" || OVERALL_EXIT=$?
        ;;
    all)
        echo "==> [run-tests] Running all test modes..."
        EDIT_EXIT=0
        PLAY_EXIT=0

        run_test_mode "EditMode" || EDIT_EXIT=$?
        run_test_mode "PlayMode" || PLAY_EXIT=$?

        if [[ "${EDIT_EXIT}" -ne 0 || "${PLAY_EXIT}" -ne 0 ]]; then
            OVERALL_EXIT=1
            echo "==> [run-tests] Some test modes failed:"
            echo "    EditMode: exit code ${EDIT_EXIT}"
            echo "    PlayMode: exit code ${PLAY_EXIT}"
        fi
        ;;
esac

if [[ "${OVERALL_EXIT}" -eq 0 ]]; then
    echo "==> [run-tests] All tests PASSED."
else
    echo "==> [run-tests] Some tests FAILED (exit code: ${OVERALL_EXIT})."
fi

exit "${OVERALL_EXIT}"
