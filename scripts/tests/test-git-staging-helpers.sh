#!/usr/bin/env bash
# Regression tests for scripts/git-staging-helpers.sh runtime behavior.
# Focus: prevent hidden stderr suppression and trap leakage that can mask
# hook failures and make diagnostics unreliable.

set -euo pipefail

# Colors for output
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
    if [[ -n "${2:-}" ]]; then
        echo "  $2"
    fi
}

run_test() {
    tests_run=$((tests_run + 1))
}

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
HELPERS_PATH="$REPO_ROOT/scripts/git-staging-helpers.sh"

if [[ ! -f "$HELPERS_PATH" ]]; then
    echo "Error: helper script not found: $HELPERS_PATH" >&2
    exit 1
fi

TMP_DIR="$(mktemp -d)"
cleanup() {
    rm -rf "$TMP_DIR"
}
trap cleanup EXIT

create_repo() {
    local repo_path="$1"
    mkdir -p "$repo_path"
    git -C "$repo_path" init -q
    git -C "$repo_path" config user.email test@example.com
    git -C "$repo_path" config user.name "Helper Test"
}

echo "Running git staging helper regression tests..."

# Test 1: git_add_with_retry stages files successfully
run_test
repo1="$TMP_DIR/repo1"
create_repo "$repo1"
if (
    set -euo pipefail
    cd "$repo1"
    export GIT_HELPERS_LOCK_FILE="$repo1/.git-staging.lock"
    # shellcheck disable=SC1090
    source "$HELPERS_PATH"

    echo "hello" > sample.txt
    git_add_with_retry sample.txt
    staged="$(git diff --cached --name-only || true)"
    grep -Fxq -- sample.txt <<<"$staged"
); then
    pass "git_add_with_retry stages file"
else
    fail "git_add_with_retry stages file"
fi

# Test 2: helper must not leak RETURN traps into caller scope
run_test
repo2="$TMP_DIR/repo2"
create_repo "$repo2"
trap_state_output="$({
    set -euo pipefail
    cd "$repo2"
    export GIT_HELPERS_LOCK_FILE="$repo2/.git-staging.lock"
    # shellcheck disable=SC1090
    source "$HELPERS_PATH"

    echo "trap" > trap-test.txt
    git_add_with_retry trap-test.txt
    trap -p RETURN || true
} 2>&1)"

if [[ -z "$trap_state_output" ]]; then
    pass "git_add_with_retry does not leak RETURN trap"
else
    fail "git_add_with_retry does not leak RETURN trap" "Unexpected RETURN trap: $trap_state_output"
fi

# Test 3: helper cleanup must not redirect caller stderr to /dev/null
run_test
repo3="$TMP_DIR/repo3"
create_repo "$repo3"
probe_output="$({
    set -euo pipefail
    cd "$repo3"
    export GIT_HELPERS_LOCK_FILE="$repo3/.git-staging.lock"
    # shellcheck disable=SC1090
    source "$HELPERS_PATH"

    echo "stderr" > stderr-test.txt
    git_add_with_retry stderr-test.txt
    release_git_lock

    echo "__STDERR_PROBE__" >&2
} 2>&1 >/dev/null)"

if [[ "$probe_output" == *"__STDERR_PROBE__"* ]]; then
    pass "helper cleanup preserves stderr"
else
    fail "helper cleanup preserves stderr" "stderr probe was suppressed"
fi

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
