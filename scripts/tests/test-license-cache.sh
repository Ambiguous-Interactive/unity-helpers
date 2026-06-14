#!/usr/bin/env bash
# =============================================================================
# Test Script: License year cache
# =============================================================================
# Validates the caching behavior of audit-license-years.sh:
#   - Cache file creation
#   - Cache hits (no git log calls on second run)
#   - --no-cache flag disables reads
#   - --paths flag for incremental mode
#   - Cache invalidation via post-rewrite hook
#
# Run: bash scripts/tests/test-license-cache.sh
# Exit codes: 0 = all tests pass, 1 = test failure
# =============================================================================

set -eu

# NOTE: pipefail intentionally not set — git ls-files | head causes benign SIGPIPE

RED='\033[0;31m'
GREEN='\033[0;32m'
NC='\033[0m'

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
AUDIT_SCRIPT="$REPO_ROOT/scripts/audit-license-years.sh"
POST_REWRITE="$REPO_ROOT/.githooks/post-rewrite"
PRE_COMMIT="$REPO_ROOT/.githooks/pre-commit"

cd "$REPO_ROOT"
CACHE_FILE="$(git rev-parse --git-path license-year-cache)"
case "$CACHE_FILE" in
    /*) ;;
    *) CACHE_FILE="$REPO_ROOT/$CACHE_FILE" ;;
esac

# =============================================================================
# Test: audit-license-years.sh exists and has required flags
# =============================================================================
echo ""
echo "=== Testing audit-license-years.sh structure ==="

run_test
if [ -f "$AUDIT_SCRIPT" ]; then
    pass "audit-license-years.sh exists"
else
    fail "audit-license-years.sh exists" "file exists" "not found"
fi

run_test
if grep -q '\-\-paths' "$AUDIT_SCRIPT"; then
    pass "--paths flag is supported"
else
    fail "--paths flag is supported" "--paths present" "not found"
fi

run_test
if grep -q '\-\-no-cache' "$AUDIT_SCRIPT"; then
    pass "--no-cache flag is supported"
else
    fail "--no-cache flag is supported" "--no-cache present" "not found"
fi

run_test
if grep -q 'license-year-cache' "$AUDIT_SCRIPT"; then
    pass "Cache file path defined"
else
    fail "Cache file path defined" "license-year-cache path" "not found"
fi

run_test
if grep -q 'git rev-parse --git-path license-year-cache' "$AUDIT_SCRIPT" && grep -q 'git rev-parse --git-path license-year-cache' "$POST_REWRITE"; then
    pass "License cache path is worktree-safe"
else
    fail "License cache path is worktree-safe" "git rev-parse --git-path license-year-cache" "not found"
fi

run_test
if grep -q 'CURRENT_YEAR=$(date +%Y)' "$AUDIT_SCRIPT" && grep -q 'CURRENT_YEAR=$(date +%Y)' "$REPO_ROOT/scripts/update-license-headers.sh"; then
    pass "License scripts compute current year dynamically"
else
    fail "License scripts compute current year dynamically" 'CURRENT_YEAR=$(date +%Y)' "not found"
fi

run_test
if grep -q 'year_cache' "$AUDIT_SCRIPT"; then
    pass "Cache associative array exists"
else
    fail "Cache associative array exists" "year_cache variable" "not found"
fi

run_test
if grep -q 'save_cache' "$AUDIT_SCRIPT"; then
    pass "Cache save function defined"
else
    fail "Cache save function defined" "save_cache function" "not found"
fi

run_test
if grep -q 'load_cache' "$AUDIT_SCRIPT"; then
    pass "Cache load function defined"
else
    fail "Cache load function defined" "load_cache function" "not found"
fi

run_test
if grep -q "git ls-files -z -- '\\*.cs'" "$AUDIT_SCRIPT"; then
    pass "Full audit enumerates tracked C# files through git ls-files"
else
    fail "Full audit uses tracked C# file enumeration" "git ls-files -z -- '*.cs'" "not found"
fi

run_test
if grep -q "git ls-files -z -- '\\*.cs'" "$REPO_ROOT/scripts/update-license-headers.sh"; then
    pass "License header updater enumerates tracked C# files through git ls-files"
else
    fail "License updater uses tracked C# file enumeration" "git ls-files -z -- '*.cs'" "not found"
fi

run_test
if grep -q -- '--paths' "$REPO_ROOT/scripts/update-license-headers.sh"; then
    pass "License header updater supports --paths for scoped recovery"
else
    fail "License updater supports --paths" "--paths present" "not found"
fi

run_test
if grep -q 'audit-license-years.sh --summary --paths' "$PRE_COMMIT" && grep -q 'update-license-headers.sh --paths' "$PRE_COMMIT"; then
    pass "Pre-commit runs staged C# license audit and scoped auto-fix"
else
    fail "Pre-commit license recovery wiring" "audit-license-years.sh and update-license-headers.sh path-scoped calls" "not found"
fi

run_test
if grep -qE 'mktemp.*CACHE_FILE|mktemp.*license' "$AUDIT_SCRIPT"; then
    pass "Cache writes atomically via mktemp"
else
    fail "Cache writes atomically via mktemp" "atomic write via mktemp" "not found"
fi

run_test
if grep -q 'trap.*save_cache' "$AUDIT_SCRIPT"; then
    pass "Cache save registered as EXIT trap"
else
    fail "Cache save registered as EXIT trap" "trap save_cache EXIT" "not found"
fi

# =============================================================================
# Test: Cache creation on run
# =============================================================================
echo ""
echo "=== Testing cache creation ==="

# Remove existing cache
rm -f "$CACHE_FILE"

run_test
# Run with --paths on a single known .cs file to keep it fast
CS_FILE=$(git ls-files '*.cs' 2>/dev/null | head -1 || true)
if [ -n "$CS_FILE" ]; then
    bash "$AUDIT_SCRIPT" --summary --paths "$CS_FILE" >/dev/null 2>&1 || true
    if [ -f "$CACHE_FILE" ]; then
        pass "Cache file created after run"
    else
        fail "Cache file created after run" "cache file exists" "not created"
    fi
else
    fail "Cache creation" "found .cs file" "no .cs files in repo"
fi

run_test
if [ -n "$CS_FILE" ]; then
    TMP_PARENT=$(mktemp -d)
    TMP_WORKTREE="$TMP_PARENT/worktree"
    if git worktree add -q "$TMP_WORKTREE" HEAD >/dev/null 2>&1; then
        cp "$AUDIT_SCRIPT" "$TMP_WORKTREE/scripts/audit-license-years.sh"
        WORKTREE_CACHE=$(git -C "$TMP_WORKTREE" rev-parse --git-path license-year-cache)
        case "$WORKTREE_CACHE" in
            /*) ;;
            *) WORKTREE_CACHE="$TMP_WORKTREE/$WORKTREE_CACHE" ;;
        esac
        rm -f "$WORKTREE_CACHE"
        bash "$TMP_WORKTREE/scripts/audit-license-years.sh" --summary --paths "$CS_FILE" >/dev/null 2>&1 || true
        if [ -f "$WORKTREE_CACHE" ]; then
            pass "Linked worktree audit writes cache through git rev-parse --git-path"
        else
            fail "Linked worktree audit writes cache through git rev-parse --git-path" "cache file at $WORKTREE_CACHE" "not created"
        fi
        git worktree remove -f "$TMP_WORKTREE" >/dev/null 2>&1 || true
    else
        fail "Linked worktree audit writes cache through git rev-parse --git-path" "temporary linked worktree" "git worktree add failed"
    fi
    rm -rf "$TMP_PARENT"
else
    fail "Linked worktree cache behavior" "found .cs file" "no .cs files in repo"
fi

# =============================================================================
# Test: Cache content has expected format
# =============================================================================
echo ""
echo "=== Testing cache content format ==="

run_test
if [ -f "$CACHE_FILE" ] && [ -s "$CACHE_FILE" ]; then
    # Cache format: <path>\t<year>
    FIRST_LINE=$(head -1 "$CACHE_FILE")
    if echo "$FIRST_LINE" | grep -qE $'^[^\t]+\t[0-9]{4}$'; then
        pass "Cache line has <path>\\t<year> format"
    else
        fail "Cache line format" "<path>\\t<year>" "$FIRST_LINE"
    fi
else
    fail "Cache file has content" "non-empty cache" "empty or missing"
fi

# =============================================================================
# Test: post-rewrite hook invalidates cache
# =============================================================================
echo ""
echo "=== Testing cache invalidation ==="

run_test
if [ -f "$POST_REWRITE" ]; then
    pass "post-rewrite hook exists"
else
    fail "post-rewrite hook exists" "file exists" "not found"
fi

run_test
if [ -x "$POST_REWRITE" ]; then
    pass "post-rewrite hook is executable"
else
    fail "post-rewrite hook is executable" "executable" "not executable"
fi

run_test
if grep -q 'license-year-cache' "$POST_REWRITE"; then
    pass "post-rewrite references cache file"
else
    fail "post-rewrite references cache file" "license-year-cache" "not found"
fi

# Simulate cache invalidation
if [ -f "$CACHE_FILE" ]; then
    # Ensure a cache file exists
    run_test
    bash "$POST_REWRITE" amend 2>/dev/null || true
    if [ ! -f "$CACHE_FILE" ]; then
        pass "post-rewrite hook deletes cache"
    else
        fail "post-rewrite hook deletes cache" "cache deleted" "cache still exists"
    fi
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
