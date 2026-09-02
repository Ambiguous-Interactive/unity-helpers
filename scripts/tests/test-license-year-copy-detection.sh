#!/usr/bin/env bash
# =============================================================================
# Test Script: the license-year walk's copy detection is load-bearing
# =============================================================================
# Regression coverage for issue #680, which asked whether
# `--find-copies-harder` could be dropped or split out of the single history
# walk in scripts/license-year-lib.sh. It is 90% of the walk's cost, so the
# question was worth asking, and the answer is no: 24 tracked .cs files resolve
# to a DIFFERENT year without it, every one of them to a LATER year than the
# header the repository already carries. Dropping the flag would make
# scripts/audit-license-years.sh reject 24 files nobody has touched.
#
# This test names all 24, so removing the expensive pass reds the build here --
# with the list of casualties in the failure output -- rather than in a
# repository-wide audit that only says "24 mismatches".
#
# It runs against THIS repository rather than a synthetic one on purpose. The
# 24 files are a property of this history: they were extracted out of existing
# sources, so git can only date them by pairing each with the unmodified file it
# came from, and only `--find-copies-harder` considers unmodified files as copy
# sources. A fixture repository would be asserting the flag's documentation
# rather than the corpus that depends on it.
#
# The red half patches a COPY of the shipped library, deleting exactly the line
# that passes the flag, and asserts the deletion landed. Remove the flag from
# the real library and both halves go red: the green one because the 24 years
# move, the red one because there is no longer a line to delete.
#
# Run: bash scripts/tests/test-license-year-copy-detection.sh
# Exit codes: 0 = all tests pass, 1 = test failure
# =============================================================================

set -euo pipefail

RED='\033[0;31m'
GREEN='\033[0;32m'
NC='\033[0m' # No Color

tests_run=0
tests_passed=0
tests_failed=0

pass() {
    tests_passed=$((tests_passed + 1))
    echo -e "${GREEN}PASS${NC} $1"
}

fail() {
    tests_failed=$((tests_failed + 1))
    echo -e "${RED}FAIL${NC} $1"
    echo -e "  ${RED}Expected:${NC} $2"
    echo -e "  ${RED}Actual:${NC}   $3"
}

run_test() {
    tests_run=$((tests_run + 1))
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# The library is this script's sibling and travels with it, so it is resolved from the script's own
# location rather than from the repository under inspection (#681).
LIB_SOURCE="$SCRIPT_DIR/../license-year-lib.sh"
if [[ ! -f "$LIB_SOURCE" ]]; then
    echo "Error: required script not found: $LIB_SOURCE" >&2
    exit 1
fi

# path|year the shipped walk resolves|year the walk resolves without --find-copies-harder
#
# Both columns come from folding the same walk two ways over this checkout's history; neither is a
# clock reading, so both are stable as long as history is appended to rather than rewritten. The
# left year is also the year each file's committed header carries, which is asserted below rather
# than assumed.
declare -a COPY_DETECTED_FILES=(
    "Editor/Core/Helper/AnimationEventHelpers.cs|2023|2025"
    "Editor/Sprites/SpriteSettingsApplierWindow.cs|2024|2025"
    "Runtime/Core/Serialization/JsonConverters/ColorConverter.cs|2023|2025"
    "Runtime/Core/Serialization/JsonConverters/Vector2IntConverter.cs|2023|2025"
    "Runtime/Core/Serialization/JsonConverters/Vector4Converter.cs|2023|2025"
    "Tests/Core/TestTypes/VContainer/BaseWithSibling.cs|2025|2026"
    "Tests/Core/TestTypes/VContainer/Consumer.cs|2025|2026"
    "Tests/Core/TestTypes/VContainer/TestComponent.cs|2025|2026"
    "Tests/Core/TestTypes/Zenject/Consumer.cs|2025|2026"
    "Tests/Core/TestTypes/Zenject/TestComponent.cs|2025|2026"
    "Tests/Editor/AssemblyInfo.cs|2025|2026"
    "Tests/Editor/CustomDrawers/TestTypes/IntDropDownVeryLargeOptionsAsset.cs|2025|2026"
    "Tests/Editor/CustomDrawers/TestTypes/IntegrationTestWGroupSetHost.cs|2025|2026"
    "Tests/Editor/CustomDrawers/TestTypes/MultiObjectWValueDropDownTarget.cs|2025|2026"
    "Tests/Editor/TestTypes/MultiObjectIntDropDownTarget.cs|2025|2026"
    "Tests/Editor/Utils/WButton/TestTypes/HelperTargetCancellable.cs|2025|2026"
    "Tests/Runtime/Helper/TestTypes/AutoRuntimeSingleton.cs|2025|2026"
    "Tests/Runtime/Helper/TestTypes/RuntimeScriptableObjectTarget.cs|2025|2026"
    "Tests/Runtime/Integrations/Reflex/TestTypes/ReflexRelationalTester.cs|2025|2026"
    "Tests/Runtime/Integrations/VContainer/TestTypes/AttributeIncludeInactiveTester.cs|2025|2026"
    "Tests/Runtime/Integrations/VContainer/TestTypes/TestComponent.cs|2025|2026"
    "Tests/Runtime/Integrations/VContainer/TestTypes/VContainerRelationalTester.cs|2025|2026"
    "Tests/Runtime/Integrations/Zenject/TestTypes/ZenjectRelationalTester.cs|2025|2026"
    "Tests/Runtime/Tags/Helpers/ReentrantCosmeticComponent.cs|2025|2026"
)

EXPECTED_FILE_COUNT=24

declare -a NAMED_PATHS=()
declare -A EXPECTED_YEARS=()
declare -A WITHOUT_FLAG_YEARS=()

for row in "${COPY_DETECTED_FILES[@]}"; do
    IFS='|' read -r row_path row_expected row_without <<< "$row"
    NAMED_PATHS+=("$row_path")
    EXPECTED_YEARS["$row_path"]="$row_expected"
    WITHOUT_FLAG_YEARS["$row_path"]="$row_without"
done

TMP_DIR="$(mktemp -d)"
cleanup() {
    # Runs from the EXIT trap, which shellcheck cannot see reaching it.
    # shellcheck disable=SC2317
    rm -rf "$TMP_DIR"
}
trap cleanup EXIT

# The driver primes with whichever library it is handed and reports both the walk's own answer and
# the resolver's. Reporting the walk separately is what keeps the red half honest: a path missing
# from the map falls through to a per-path `git log --follow`, which answers like the expensive
# walk, and a resolver-only assertion would read that fallback as a pass.
DRIVER="$TMP_DIR/prime-driver.sh"
cat > "$DRIVER" <<'DRIVER_EOF'
#!/usr/bin/env bash
set -euo pipefail

lib_path="$1"
repo_root="$2"
shift 2

# shellcheck source=/dev/null
source "$lib_path"

license_year_init "$repo_root" "2023" "$(date +%Y)"
license_year_prime

for rel in "$@"; do
    if [[ -n "${LICENSE_YEAR_HISTORY_YEARS[$rel]+_}" ]]; then
        walk_year="${LICENSE_YEAR_HISTORY_YEARS[$rel]}"
    else
        walk_year="(absent)"
    fi
    license_year_resolve "$rel"
    printf '%s\t%s\t%s\t%s\n' "$rel" "$walk_year" "$LICENSE_YEAR_RESULT" "$LICENSE_YEAR_SOURCE"
done
DRIVER_EOF

# Args:
#   $1 - name of the associative array to fill with path -> walk year
#   $2 - name of the associative array to fill with path -> "resolved year:source"
#   $3 - the driver's stdout
read_driver_output() {
    local -n walk_target="$1"
    local -n resolved_target="$2"
    local output="$3"
    local out_path=""
    local out_walk=""
    local out_resolved=""
    local out_source=""

    while IFS=$'\t' read -r out_path out_walk out_resolved out_source; do
        if [[ -z "$out_path" ]]; then
            continue
        fi
        # Both writes land in the caller's arrays through the name references above, which is the
        # shape SC2034 cannot see.
        # shellcheck disable=SC2034
        walk_target["$out_path"]="$out_walk"
        # shellcheck disable=SC2034
        resolved_target["$out_path"]="$out_resolved:$out_source"
    done <<< "$output"
}

echo "Running license-year copy-detection regression tests..."
echo "Repository: $REPO_ROOT"
echo "Named files: ${#NAMED_PATHS[@]}"

# =============================================================================
# Corpus control: the subjects still exist
# =============================================================================
# A list of paths that have been deleted or renamed would let every assertion below pass by
# vacuity, so the corpus is asserted before it is used (.llm/skills/honest-gates.md).
echo ""
echo "=== Corpus ==="

run_test
if [[ "${#NAMED_PATHS[@]}" -eq "$EXPECTED_FILE_COUNT" ]]; then
    pass "The list names $EXPECTED_FILE_COUNT files"
else
    fail "The list names $EXPECTED_FILE_COUNT files" "$EXPECTED_FILE_COUNT" "${#NAMED_PATHS[@]}"
fi

run_test
declare -A tracked_lookup=()
while IFS= read -r -d '' tracked_path; do
    tracked_lookup["$tracked_path"]=1
done < <(git -C "$REPO_ROOT" ls-files -z -- '*.cs')

missing_paths=()
for named_path in "${NAMED_PATHS[@]}"; do
    if [[ -z "${tracked_lookup[$named_path]+_}" ]]; then
        missing_paths+=("$named_path")
    fi
done
if [[ "${#missing_paths[@]}" -eq 0 && 0 -lt "${#tracked_lookup[@]}" ]]; then
    pass "Every named file is still a tracked .cs file (${#tracked_lookup[@]} tracked in total)"
else
    fail "Every named file is still a tracked .cs file" \
        "all ${#NAMED_PATHS[@]} tracked, out of a non-empty corpus" \
        "${#tracked_lookup[@]} tracked .cs files, missing: ${missing_paths[*]:-(none)}"
fi

# =============================================================================
# The years the shipped walk resolves
# =============================================================================
echo ""
echo "=== Shipped walk ==="

declare -A shipped_walk_years=()
declare -A shipped_resolved=()
shipped_exit=0
shipped_output=$(bash "$DRIVER" "$LIB_SOURCE" "$REPO_ROOT" "${NAMED_PATHS[@]}" 2>&1) || shipped_exit=$?

run_test
if [[ "$shipped_exit" -eq 0 ]]; then
    pass "Priming this repository with the shipped library succeeds"
else
    fail "Priming this repository with the shipped library succeeds" "exit 0" \
        "exit $shipped_exit: $shipped_output"
fi

read_driver_output shipped_walk_years shipped_resolved "$shipped_output"

run_test
walk_mismatches=()
for named_path in "${NAMED_PATHS[@]}"; do
    expected="${EXPECTED_YEARS[$named_path]}"
    actual="${shipped_walk_years[$named_path]:-(no row)}"
    if [[ "$actual" != "$expected" ]]; then
        walk_mismatches+=("$named_path: expected $expected, walk said $actual")
    fi
done
if [[ "${#walk_mismatches[@]}" -eq 0 ]]; then
    pass "The shipped walk dates all ${#NAMED_PATHS[@]} copy-detected files to their source's year"
else
    fail "The shipped walk dates all ${#NAMED_PATHS[@]} copy-detected files to their source's year" \
        "every named file at its listed year" "$(printf '%s; ' "${walk_mismatches[@]}")"
fi

run_test
resolve_mismatches=()
for named_path in "${NAMED_PATHS[@]}"; do
    expected="${EXPECTED_YEARS[$named_path]}:history"
    actual="${shipped_resolved[$named_path]:-(no row)}"
    if [[ "$actual" != "$expected" ]]; then
        resolve_mismatches+=("$named_path: expected $expected, resolver said $actual")
    fi
done
if [[ "${#resolve_mismatches[@]}" -eq 0 ]]; then
    pass "license_year_resolve reports those years as committed history"
else
    fail "license_year_resolve reports those years as committed history" \
        "every named file at its listed year, sourced from history" \
        "$(printf '%s; ' "${resolve_mismatches[@]}")"
fi

# The audit compares the resolved year against the header the file carries, so this is the leg that
# says what dropping the flag would actually cost: 24 rejected files.
run_test
header_mismatches=()
for named_path in "${NAMED_PATHS[@]}"; do
    header_line=""
    IFS= read -r header_line < "$REPO_ROOT/$named_path" || true
    header_year=""
    if [[ "$header_line" =~ Copyright\ \(c\)\ ([0-9]{4}) ]]; then
        header_year="${BASH_REMATCH[1]}"
    fi
    if [[ "$header_year" != "${EXPECTED_YEARS[$named_path]}" ]]; then
        header_mismatches+=("$named_path: header ${header_year:-(none)}, walk ${EXPECTED_YEARS[$named_path]}")
    fi
done
if [[ "${#header_mismatches[@]}" -eq 0 ]]; then
    pass "Every named file's committed header already carries the copy-detected year"
else
    fail "Every named file's committed header already carries the copy-detected year" \
        "header year equals walk year for all ${#NAMED_PATHS[@]}" \
        "$(printf '%s; ' "${header_mismatches[@]}")"
fi

# =============================================================================
# The red half: the same walk without --find-copies-harder
# =============================================================================
echo ""
echo "=== Walk without --find-copies-harder ==="

PATCHED_LIB="$TMP_DIR/license-year-lib-without-copies-harder.sh"
FLAG_LINE_PATTERN='^[[:space:]]*--find-copies-harder \\$'

run_test
flag_line_count=$(grep -c -e "$FLAG_LINE_PATTERN" "$LIB_SOURCE" || true)
if [[ "$flag_line_count" -eq 1 ]]; then
    pass "The library passes --find-copies-harder to the walk on exactly one line"
else
    fail "The library passes --find-copies-harder to the walk on exactly one line" \
        "1 line matching $FLAG_LINE_PATTERN" "$flag_line_count"
fi

grep -v -e "$FLAG_LINE_PATTERN" "$LIB_SOURCE" > "$PATCHED_LIB"

run_test
if ! grep -q -e "$FLAG_LINE_PATTERN" "$PATCHED_LIB"; then
    pass "The patched copy no longer passes the flag"
else
    fail "The patched copy no longer passes the flag" "no matching line" \
        "$(grep -n -e "$FLAG_LINE_PATTERN" "$PATCHED_LIB")"
fi

declare -A cheap_walk_years=()
declare -A cheap_resolved=()
cheap_exit=0
cheap_output=$(bash "$DRIVER" "$PATCHED_LIB" "$REPO_ROOT" "${NAMED_PATHS[@]}" 2>&1) || cheap_exit=$?

run_test
if [[ "$cheap_exit" -eq 0 ]]; then
    pass "Priming with the patched library succeeds"
else
    fail "Priming with the patched library succeeds" "exit 0" "exit $cheap_exit: $cheap_output"
fi

read_driver_output cheap_walk_years cheap_resolved "$cheap_output"

run_test
still_agreeing=()
for named_path in "${NAMED_PATHS[@]}"; do
    if [[ "${cheap_walk_years[$named_path]:-(no row)}" == "${EXPECTED_YEARS[$named_path]}" ]]; then
        still_agreeing+=("$named_path")
    fi
done
if [[ "${#still_agreeing[@]}" -eq 0 ]]; then
    pass "Every named file changes year when the flag is removed"
else
    fail "Every named file changes year when the flag is removed" \
        "all ${#NAMED_PATHS[@]} differ" \
        "${#still_agreeing[@]} unchanged: ${still_agreeing[*]}"
fi

run_test
wrong_fallback_years=()
for named_path in "${NAMED_PATHS[@]}"; do
    expected="${WITHOUT_FLAG_YEARS[$named_path]}"
    actual="${cheap_walk_years[$named_path]:-(no row)}"
    resolved="${cheap_resolved[$named_path]:-(no row)}"
    if [[ "$actual" != "$expected" || "$resolved" != "$expected:history" ]]; then
        wrong_fallback_years+=("$named_path: expected $expected, walk said $actual, resolver said $resolved")
    fi
done
if [[ "${#wrong_fallback_years[@]}" -eq 0 ]]; then
    pass "Without the flag each named file falls back to the year it was added"
else
    fail "Without the flag each named file falls back to the year it was added" \
        "every named file at its listed without-flag year, walk and resolver alike" \
        "$(printf '%s; ' "${wrong_fallback_years[@]}")"
fi

# =============================================================================
# The pathspec narrowing's canary
# =============================================================================
# The walk is narrowed to '*.cs' (#680), which can only change an answer if git ever paired a .cs
# path with a non-.cs one through a rename or a copy. It never has here, and a rename is visible
# without paying for copy detection, so the cheap unnarrowed walk is enough to notice the day that
# stops being true. It sees renames only, so this is a canary rather than a proof.
echo ""
echo "=== Pathspec narrowing ==="

run_test
cross_extension_pairs=$(
    git -C "$REPO_ROOT" -c diff.renameLimit=999999 log --reverse --name-status \
        --diff-filter=RC --format='' --find-renames 2>/dev/null |
        awk -F'\t' 'NF == 3 { source_is_cs = ($2 ~ /\.cs$/); target_is_cs = ($3 ~ /\.cs$/); if (source_is_cs != target_is_cs) { print } }'
)
rename_record_count=$(
    git -C "$REPO_ROOT" -c diff.renameLimit=999999 log --reverse --name-status \
        --diff-filter=RC --format='' --find-renames 2>/dev/null | grep -c . || true
)
if [[ -z "$cross_extension_pairs" && 0 -lt "$rename_record_count" ]]; then
    pass "No rename in $rename_record_count records pairs a .cs path with a non-.cs path"
else
    fail "No rename pairs a .cs path with a non-.cs path" \
        "no cross-extension rename, out of a non-empty record set" \
        "$rename_record_count records, cross-extension: ${cross_extension_pairs:-(none)}"
fi

# =============================================================================
# Summary
# =============================================================================
echo ""
echo "=== Test Summary ==="
echo "Tests run:    $tests_run"
echo -e "Tests passed: ${GREEN}$tests_passed${NC}"
if [[ "$tests_failed" -gt 0 ]]; then
    echo -e "Tests failed: ${RED}$tests_failed${NC}"
    echo ""
    echo "The license-year walk's copy detection is what dates the files named above. See #680."
    exit 1
fi

echo "Tests failed: 0"
exit 0
