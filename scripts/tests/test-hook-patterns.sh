#!/usr/bin/env bash
# =============================================================================
# Test Script: Bash Case Pattern Matching in Git Hooks
# =============================================================================
# Validates that * in bash case statements matches / (unlike filename globbing).
# This is a regression test for the pre-commit hook's case patterns.
#
# Run: bash scripts/tests/test-hook-patterns.sh
# Exit codes: 0 = all tests pass, 1 = test failure
#
# Note: Ensure this file is executable (chmod +x scripts/tests/test-hook-patterns.sh)
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PRE_COMMIT="$REPO_ROOT/.githooks/pre-commit"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
NC='\033[0m' # No Color

# Test counters
tests_run=0
tests_passed=0
tests_failed=0

# Test helper functions
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

# =============================================================================
# Helper: test whether a path matches a case pattern
# Returns 0 if matched, 1 if not
# =============================================================================
case_matches() {
    local path="$1"
    local pattern="$2"
    # Use eval to expand the pattern in a case statement
    eval "case \"\$path\" in $pattern) return 0 ;; esac"
    return 1
}

# =============================================================================
# Test: * matches / in bash case statements
# =============================================================================
echo ""
echo "=== Testing * matches / in bash case statements ==="

run_test
if case_matches "a/b/c" "a*c"; then
    pass "* matches / in case: 'a/b/c' matches 'a*c'"
else
    fail "* matches / in case: 'a/b/c' matches 'a*c'" "match" "no match"
fi

run_test
if case_matches "a/b/c/d/e" "a*e"; then
    pass "* matches multiple / in case: 'a/b/c/d/e' matches 'a*e'"
else
    fail "* matches multiple / in case: 'a/b/c/d/e' matches 'a*e'" "match" "no match"
fi

# =============================================================================
# Test: .llm/skills/*.md pattern matches at all depths
# This is the pattern used in the pre-commit hook (step 11)
# =============================================================================
echo ""
echo "=== Testing .llm/skills/*.md pattern (pre-commit step 11) ==="

run_test
if case_matches ".llm/skills/foo.md" ".llm/skills/*.md"; then
    pass "Shallow match: .llm/skills/foo.md"
else
    fail "Shallow match: .llm/skills/foo.md" "match" "no match"
fi

run_test
if case_matches ".llm/skills/sub/foo.md" ".llm/skills/*.md"; then
    pass "One level deep: .llm/skills/sub/foo.md"
else
    fail "One level deep: .llm/skills/sub/foo.md" "match" "no match"
fi

run_test
if case_matches ".llm/skills/sub/dir/foo.md" ".llm/skills/*.md"; then
    pass "Two levels deep: .llm/skills/sub/dir/foo.md"
else
    fail "Two levels deep: .llm/skills/sub/dir/foo.md" "match" "no match"
fi

run_test
if case_matches ".llm/skills/a/b/c/d/e.md" ".llm/skills/*.md"; then
    pass "Many levels deep: .llm/skills/a/b/c/d/e.md"
else
    fail "Many levels deep: .llm/skills/a/b/c/d/e.md" "match" "no match"
fi

# =============================================================================
# Test: Non-matching paths correctly rejected
# =============================================================================
echo ""
echo "=== Testing non-matching paths ==="

run_test
if case_matches ".llm/other/foo.md" ".llm/skills/*.md"; then
    fail "Wrong directory rejected: .llm/other/foo.md" "no match" "match"
else
    pass "Wrong directory rejected: .llm/other/foo.md"
fi

run_test
if case_matches ".llm/skills/foo.txt" ".llm/skills/*.md"; then
    fail "Wrong extension rejected: .llm/skills/foo.txt" "no match" "match"
else
    pass "Wrong extension rejected: .llm/skills/foo.txt"
fi

run_test
if case_matches "other/.llm/skills/foo.md" ".llm/skills/*.md"; then
    fail "Wrong prefix rejected: other/.llm/skills/foo.md" "no match" "match"
else
    pass "Wrong prefix rejected: other/.llm/skills/foo.md"
fi

run_test
if case_matches ".llm/skillsfoo.md" ".llm/skills/*.md"; then
    fail "Missing / after skills rejected: .llm/skillsfoo.md" "no match" "match"
else
    pass "Missing / after skills rejected: .llm/skillsfoo.md"
fi

# =============================================================================
# Test: Other pre-commit hook case patterns
# =============================================================================
echo ""
echo "=== Testing other pre-commit hook patterns ==="

# .llm/* pattern (step 10)
run_test
if case_matches ".llm/context.md" ".llm/*"; then
    pass ".llm/* matches shallow: .llm/context.md"
else
    fail ".llm/* matches shallow: .llm/context.md" "match" "no match"
fi

run_test
if case_matches ".llm/skills/foo.md" ".llm/*"; then
    pass ".llm/* matches deep: .llm/skills/foo.md"
else
    fail ".llm/* matches deep: .llm/skills/foo.md" "match" "no match"
fi

# Tests/*.cs pattern (step 12)
run_test
if case_matches "Tests/Runtime/FooTest.cs" "Tests/*.cs"; then
    pass "Tests/*.cs matches nested: Tests/Runtime/FooTest.cs"
else
    fail "Tests/*.cs matches nested: Tests/Runtime/FooTest.cs" "match" "no match"
fi

run_test
if case_matches "Tests/Editor/Sub/BarTest.cs" "Tests/*.cs"; then
    pass "Tests/*.cs matches deeply nested: Tests/Editor/Sub/BarTest.cs"
else
    fail "Tests/*.cs matches deeply nested: Tests/Editor/Sub/BarTest.cs" "match" "no match"
fi

# *Drawer.cs pattern (step 14)
run_test
if case_matches "Editor/CustomDrawers/FooDrawer.cs" "*Drawer.cs"; then
    pass "*Drawer.cs matches with path: Editor/CustomDrawers/FooDrawer.cs"
else
    fail "*Drawer.cs matches with path: Editor/CustomDrawers/FooDrawer.cs" "match" "no match"
fi

run_test
if grep -q -- '--diff-filter=ACMR' "$PRE_COMMIT"; then
    pass "Pre-commit staged path collection includes renamed new paths"
else
    fail "Pre-commit staged path collection includes renamed new paths" "--diff-filter=ACMR" "not found"
fi

# =============================================================================
# Summary
# =============================================================================
echo ""
echo "=== Test Summary ==="
echo "Tests run:    $tests_run"
echo -e "Tests passed: ${GREEN}$tests_passed${NC}"
if [ "$tests_failed" -gt 0 ]; then
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
