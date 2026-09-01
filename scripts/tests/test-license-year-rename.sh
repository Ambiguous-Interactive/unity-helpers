#!/usr/bin/env bash
# =============================================================================
# Test Script: License year resolution across a staged rename
# =============================================================================
# Regression coverage for issue #668, where the license-year fixer and the
# license-year audit disagreed about a renamed file.
#
# `git log --follow` reads committed history and never consults the index, so a
# STAGED rename (`git mv old.cs new.cs`) produced no creation year for the new
# path. scripts/update-license-headers.sh fell back to the current year and
# rewrote the header; scripts/audit-license-years.sh asked the identical
# question in --paths mode, got the identical empty answer, and agreed. The
# disagreement only surfaced after the rename was committed, when the audit's
# repository-wide scan followed the `R` status back to the original year and
# reddened CI.
#
# The non-rename case always passed, and that is exactly what hid the defect, so
# every leg below is about a rename. Two negative legs keep the fix honest: a
# genuinely new file must still resolve to the current year.
#
# All git work happens in a throwaway repository under `mktemp -d`. This test
# must never touch the checkout it lives in.
#
# Run: bash scripts/tests/test-license-year-rename.sh
# Exit codes: 0 = all tests pass, 1 = test failure
# =============================================================================

set -euo pipefail

# cspell:ignore gpgsign

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

FIXER_SOURCE="$REPO_ROOT/scripts/update-license-headers.sh"
AUDIT_SOURCE="$REPO_ROOT/scripts/audit-license-years.sh"
LIB_SOURCE="$REPO_ROOT/scripts/license-year-lib.sh"

for required in "$FIXER_SOURCE" "$AUDIT_SOURCE" "$LIB_SOURCE"; do
    if [[ ! -f "$required" ]]; then
        echo "Error: required script not found: $required" >&2
        exit 1
    fi
done

CURRENT_YEAR=$(date +%Y)
HISTORY_YEAR=$((CURRENT_YEAR - 2))
if [[ "$HISTORY_YEAR" -lt 2023 ]]; then
    HISTORY_YEAR=2023
fi

# Every assertion below distinguishes the created-then-renamed year from the current year. If the
# clock ever made those the same value the whole file would pass while checking nothing.
if [[ "$HISTORY_YEAR" == "$CURRENT_YEAR" ]]; then
    echo "Error: history year and current year are both $CURRENT_YEAR; this test cannot discriminate." >&2
    exit 1
fi

TMP_REPO="$(mktemp -d)"
TMP_REPO="$(cd "$TMP_REPO" && pwd)"
cleanup() {
    rm -rf "$TMP_REPO"
}
trap cleanup EXIT

FIXER="$TMP_REPO/scripts/update-license-headers.sh"
AUDIT="$TMP_REPO/scripts/audit-license-years.sh"

# Both scripts derive their repository root from their own location, so a copy inside the throwaway
# repository operates on the throwaway repository.
mkdir -p "$TMP_REPO/scripts" "$TMP_REPO/Runtime"
cp "$FIXER_SOURCE" "$AUDIT_SOURCE" "$LIB_SOURCE" "$TMP_REPO/scripts/"

git -C "$TMP_REPO" init -q
git -C "$TMP_REPO" config user.email "license-year-test@example.com"
git -C "$TMP_REPO" config user.name "License Year Test"
git -C "$TMP_REPO" config commit.gpgsign false
# A globally configured hooks path would otherwise run the real checkout's hooks in here.
git -C "$TMP_REPO" config core.hooksPath "$TMP_REPO/.git/hooks"

CACHE_FILE="$TMP_REPO/.git/license-year-cache"

# Each fixture gets a body built from its own name. The audit's repository-wide walk runs with
# --find-copies-harder, and near-identical fixtures would be detected as copies of one another and
# inherit a year the test never intended.
write_cs_file() {
    local path="$1"
    local year="$2"
    local name="${path##*/}"
    name="${name%.cs}"
    {
        echo "// MIT License - Copyright (c) $year wallstop"
        echo "// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE"
        echo ""
        echo "public static class $name"
        echo "{"
        echo "    public const string ${name}Marker = \"$name\";"
        echo "    public static string Describe$name() => ${name}Marker;"
        echo "    public static int ${name}MarkerLength => ${name}Marker.Length;"
        echo "    public static bool Is$name(string value) => value == ${name}Marker;"
        echo "}"
    } > "$TMP_REPO/$path"
}

# Rewrite only the copyright year, leaving the body byte-identical. Replacing the whole file would
# change how similar it looks to the path it was renamed from, and rename detection is the subject
# of this test rather than an incidental detail of it.
set_header_year() {
    local path="$1"
    local year="$2"
    local target="$TMP_REPO/$path"
    local temp_file
    temp_file=$(mktemp)
    {
        echo "// MIT License - Copyright (c) $year wallstop"
        tail -n +2 "$target"
    } > "$temp_file"
    mv "$temp_file" "$target"
}

read_header_year() {
    local first_line=""
    IFS= read -r first_line < "$TMP_REPO/$1" || true
    if [[ "$first_line" =~ Copyright\ \(c\)\ ([0-9]{4}) ]]; then
        printf '%s' "${BASH_REMATCH[1]}"
    else
        printf '%s' "(no year in header)"
    fi
}

run_fixer() {
    local exit_code=0
    fixer_output=$(cd "$TMP_REPO" && bash "$FIXER" --paths "$@" 2>&1) || exit_code=$?
    return "$exit_code"
}

run_audit() {
    audit_exit=0
    audit_output=$(cd "$TMP_REPO" && bash "$AUDIT" "$@" 2>&1) || audit_exit=$?
}

echo "Running license-year rename regression tests..."
echo "History year: $HISTORY_YEAR   Current year: $CURRENT_YEAR"
echo "Throwaway repository: $TMP_REPO"

# =============================================================================
# Fixture: a file created and committed in $HISTORY_YEAR
# =============================================================================
write_cs_file "Runtime/Original.cs" "$HISTORY_YEAR"
write_cs_file "Runtime/Untouched.cs" "$HISTORY_YEAR"
git -C "$TMP_REPO" add Runtime/Original.cs Runtime/Untouched.cs
GIT_AUTHOR_DATE="$HISTORY_YEAR-06-01T12:00:00 +0000" \
    GIT_COMMITTER_DATE="$HISTORY_YEAR-06-01T12:00:00 +0000" \
    git -C "$TMP_REPO" commit -q -m "Add sources"

# =============================================================================
# Staged rename: the case the fixer and the audit used to agree about, wrongly
# =============================================================================
echo ""
echo "=== Staged rename ==="

git -C "$TMP_REPO" mv Runtime/Original.cs Runtime/Renamed.cs

run_test
if run_fixer "Runtime/Renamed.cs"; then
    actual=$(read_header_year "Runtime/Renamed.cs")
    if [[ "$actual" == "$HISTORY_YEAR" ]]; then
        pass "Fixer keeps the original year across a staged rename"
    else
        fail "Fixer keeps the original year across a staged rename" "$HISTORY_YEAR" "$actual"
    fi
else
    fail "Fixer keeps the original year across a staged rename" "fixer exits 0" "$fixer_output"
fi

run_test
run_audit --summary --paths "Runtime/Renamed.cs"
if [[ "$audit_exit" -eq 0 ]]; then
    pass "Audit accepts the original year on a staged rename"
else
    fail "Audit accepts the original year on a staged rename" "exit 0" "exit $audit_exit: $audit_output"
fi

# The leg that actually discriminates. Before the shared resolver existed the audit shared the
# fixer's blind spot, expected the current year here, and reported success -- which is why adding
# the audit to a local gate would not have caught anything.
run_test
set_header_year "Runtime/Renamed.cs" "$CURRENT_YEAR"
run_audit --paths "Runtime/Renamed.cs"
if [[ "$audit_exit" -ne 0 && "$audit_output" == *"expected $HISTORY_YEAR"* ]]; then
    pass "Audit rejects the current year on a staged rename"
else
    fail "Audit rejects the current year on a staged rename" \
        "non-zero exit naming 'expected $HISTORY_YEAR'" "exit $audit_exit: $audit_output"
fi

run_test
if run_fixer "Runtime/Renamed.cs"; then
    actual=$(read_header_year "Runtime/Renamed.cs")
    if [[ "$actual" == "$HISTORY_YEAR" ]]; then
        pass "Fixer repairs a wrong year on a staged rename"
    else
        fail "Fixer repairs a wrong year on a staged rename" "$HISTORY_YEAR" "$actual"
    fi
else
    fail "Fixer repairs a wrong year on a staged rename" "fixer exits 0" "$fixer_output"
fi

# A staged rename is a claim about the index, not about history, and the index can be reset. The
# persistent cache is keyed by path and outlives the run, so only a committed answer belongs in it.
run_test
if [[ ! -f "$CACHE_FILE" ]] || ! grep -q '^Runtime/Renamed\.cs	' "$CACHE_FILE"; then
    pass "Staged-rename answer is not written to the persistent cache"
else
    fail "Staged-rename answer is not written to the persistent cache" \
        "no Runtime/Renamed.cs row" "$(cat "$CACHE_FILE")"
fi

# =============================================================================
# Negative cases: a genuinely new file still gets the current year
# =============================================================================
echo ""
echo "=== Genuinely new files ==="

run_test
write_cs_file "Runtime/Untracked.cs" "$HISTORY_YEAR"
if run_fixer "Runtime/Untracked.cs"; then
    actual=$(read_header_year "Runtime/Untracked.cs")
    if [[ "$actual" == "$CURRENT_YEAR" ]]; then
        pass "Fixer gives an untracked new file the current year"
    else
        fail "Fixer gives an untracked new file the current year" "$CURRENT_YEAR" "$actual"
    fi
else
    fail "Fixer gives an untracked new file the current year" "fixer exits 0" "$fixer_output"
fi

# Staged as an addition rather than a rename, so the index reports `A` and there is no source path
# to inherit a year from.
run_test
write_cs_file "Runtime/StagedAddition.cs" "$HISTORY_YEAR"
git -C "$TMP_REPO" add Runtime/StagedAddition.cs
if run_fixer "Runtime/StagedAddition.cs"; then
    actual=$(read_header_year "Runtime/StagedAddition.cs")
    if [[ "$actual" == "$CURRENT_YEAR" ]]; then
        pass "Fixer gives a staged new file the current year"
    else
        fail "Fixer gives a staged new file the current year" "$CURRENT_YEAR" "$actual"
    fi
else
    fail "Fixer gives a staged new file the current year" "fixer exits 0" "$fixer_output"
fi

run_test
run_audit --summary --paths "Runtime/StagedAddition.cs"
if [[ "$audit_exit" -eq 0 ]]; then
    pass "Audit accepts the current year on a staged new file"
else
    fail "Audit accepts the current year on a staged new file" "exit 0" "exit $audit_exit: $audit_output"
fi

# =============================================================================
# Full scan with the rename staged: the repository-wide walk must agree too
# =============================================================================
echo ""
echo "=== Full scan, rename staged ==="

run_test
rm -f "$CACHE_FILE"
run_audit --summary
if [[ "$audit_exit" -eq 0 ]]; then
    pass "Full scan agrees while the rename is still staged"
else
    fail "Full scan agrees while the rename is still staged" "exit 0" "exit $audit_exit: $audit_output"
fi

# =============================================================================
# Full scan after committing the rename: the leg that went red in CI
# =============================================================================
echo ""
echo "=== Full scan, rename committed ==="

git -C "$TMP_REPO" add Runtime
git -C "$TMP_REPO" commit -q -m "Rename source and add new files"

run_test
rm -f "$CACHE_FILE"
run_audit --summary
if [[ "$audit_exit" -eq 0 ]]; then
    pass "Full scan agrees once the rename is committed"
else
    fail "Full scan agrees once the rename is committed" "exit 0" "exit $audit_exit: $audit_output"
fi

run_test
rm -f "$CACHE_FILE"
run_audit --csv
csv_row=""
while IFS= read -r line; do
    if [[ "$line" == "Runtime/Renamed.cs,"* ]]; then
        csv_row="$line"
    fi
done <<< "$audit_output"
if [[ "$csv_row" == "Runtime/Renamed.cs,$HISTORY_YEAR,$HISTORY_YEAR,ok" ]]; then
    pass "Committed rename reports the original year in both columns"
else
    fail "Committed rename reports the original year in both columns" \
        "Runtime/Renamed.cs,$HISTORY_YEAR,$HISTORY_YEAR,ok" "${csv_row:-(no row emitted)}"
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
    echo -e "${RED}FAILED${NC}"
    exit 1
else
    echo -e "Tests failed: ${GREEN}0${NC}"
    echo ""
    echo -e "${GREEN}ALL TESTS PASSED${NC}"
    exit 0
fi
