#!/usr/bin/env bash
# =============================================================================
# Self-test for scripts/validate-hook-permissions.sh
# =============================================================================
# The validator asserts every extensionless file under .githooks/ is tracked
# 100755, because git silently skips a hook that is not executable -- a failure
# with no error message anywhere. Run against this repository it prints
# "All hook entrypoints have executable permissions", which is evidence about
# these four hooks and none at all about whether the validator still reports
# (#556, #562). It now takes --repo-root, and this drives throwaway repositories
# through every rule.
#
# Modes are set with `git update-index --chmod`, never with chmod(1): .git here
# is bind-mounted from a Windows host and carries filemode=false, so a chmod in
# the container never reaches the index and both halves of this test would see
# whatever mode git happened to record.
#
# Green half:
#   - this repository passes
#   - a fixture whose extensionless hook is tracked 100755 passes
#   - a companion .ps1 tracked 100644 is NOT flagged (only entrypoints need +x)
#   - --help exits 0
#   - --fix turns a 100644 entrypoint into 100755 and exits 0
#
# Red halves, each asserted on its own message:
#   - an entrypoint tracked 100644
#   - a .githooks directory with nothing tracked in it
#   - a missing .githooks directory
#   - an unknown option
#   - --repo-root naming a directory that does not exist
#   - --repo-root with no argument
# =============================================================================

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
VALIDATOR="$REPO_ROOT/scripts/validate-hook-permissions.sh"

WORKSPACE="$(mktemp -d)"
trap 'rm -rf "$WORKSPACE"' EXIT

PASSED=0
FAILED=0
FAILED_NAMES=()

pass() {
    echo "  [PASS] $1"
    PASSED=$((PASSED + 1))
}

fail() {
    echo "  [FAIL] $1"
    echo "         $2"
    FAILED=$((FAILED + 1))
    FAILED_NAMES+=("$1")
}

# A throwaway repository with a .githooks/ tree. $2 selects the shape.
make_repo() {
    local name="$1"
    local shape="$2"
    local root="$WORKSPACE/$name"

    mkdir -p "$root"
    git -C "$root" init --quiet
    git -C "$root" config user.email "self-test@example.com"
    git -C "$root" config user.name "self-test"

    case "$shape" in
        missing-hooks-dir)
            : > "$root/placeholder"
            git -C "$root" add placeholder
            ;;
        empty-hooks-dir)
            mkdir -p "$root/.githooks"
            : > "$root/placeholder"
            git -C "$root" add placeholder
            ;;
        *)
            mkdir -p "$root/.githooks"
            printf '#!/usr/bin/env bash\nexit 0\n' > "$root/.githooks/pre-commit"
            printf 'exit 0\n' > "$root/.githooks/pre-commit.ps1"
            git -C "$root" add .githooks/pre-commit .githooks/pre-commit.ps1
            # The companion implementation is deliberately left non-executable:
            # only entrypoints need the bit, and flagging the .ps1 would be a
            # false positive the validator must not produce.
            git -C "$root" update-index --chmod=-x .githooks/pre-commit.ps1
            if [ "$shape" = "executable" ]; then
                git -C "$root" update-index --chmod=+x .githooks/pre-commit
            else
                git -C "$root" update-index --chmod=-x .githooks/pre-commit
            fi
            ;;
    esac

    echo "$root"
}

run_validator() {
    VALIDATOR_OUTPUT="$(bash "$VALIDATOR" "$@" 2>&1)"
    VALIDATOR_EXIT=$?
    return 0
}

expect_pass() {
    local name="$1"
    shift
    run_validator "$@"
    if [ "$VALIDATOR_EXIT" -ne 0 ]; then
        fail "$name" "validator reported (exit $VALIDATOR_EXIT) on a tree it must accept: $VALIDATOR_OUTPUT"
        return
    fi
    pass "$name"
}

expect_fail() {
    local name="$1"
    local expected="$2"
    shift 2
    run_validator "$@"
    if [ "$VALIDATOR_EXIT" -eq 0 ]; then
        fail "$name" "validator accepted a tree it must reject"
        return
    fi
    if ! printf '%s' "$VALIDATOR_OUTPUT" | grep -qF -- "$expected"; then
        fail "$name" "rejected, but not for the reason under test. Expected to contain '$expected'. Got: $VALIDATOR_OUTPUT"
        return
    fi
    pass "$name"
}

echo ""
echo "Running validate-hook-permissions.sh self-tests"
echo ""

# ── Green half ───────────────────────────────────────────────────────────────
expect_pass "this repository passes"

GOOD_REPO="$(make_repo good executable)"
expect_pass "an entrypoint tracked 100755 passes" --repo-root "$GOOD_REPO"

# The .ps1 companion in that same fixture is tracked 100644. If it were being
# checked, the run above would have failed -- so assert it was seen and skipped
# rather than inferring it from a green exit code.
run_validator --repo-root "$GOOD_REPO"
if printf '%s' "$VALIDATOR_OUTPUT" | grep -q "Files checked: 1"; then
    pass "a non-executable companion .ps1 is not flagged"
else
    fail "a non-executable companion .ps1 is not flagged" "expected exactly one checked file, got: $VALIDATOR_OUTPUT"
fi

expect_pass "--help exits 0" --help

# ── Red halves ───────────────────────────────────────────────────────────────
BAD_REPO="$(make_repo bad non-executable)"
expect_fail "an entrypoint tracked 100644 is rejected" "NOT executable" --repo-root "$BAD_REPO"

EMPTY_REPO="$(make_repo empty empty-hooks-dir)"
expect_fail "a .githooks directory with nothing tracked is rejected" "No tracked files found" --repo-root "$EMPTY_REPO"

NO_HOOKS_REPO="$(make_repo nohooks missing-hooks-dir)"
expect_fail "a missing .githooks directory is rejected" "directory not found" --repo-root "$NO_HOOKS_REPO"

expect_fail "an unknown option is rejected" "Unknown option" --not-an-option

expect_fail "--repo-root naming a missing directory is rejected" "--repo-root directory not found" \
    --repo-root "$WORKSPACE/does-not-exist"

expect_fail "--repo-root with no argument is rejected" "--repo-root requires a directory argument" --repo-root

# ── --fix, which is a green half only because the red half above exists ──────
FIX_REPO="$(make_repo fixme non-executable)"
run_validator --repo-root "$FIX_REPO" --fix
if [ "$VALIDATOR_EXIT" -ne 0 ]; then
    fail "--fix repairs a 100644 entrypoint" "--fix exited $VALIDATOR_EXIT: $VALIDATOR_OUTPUT"
else
    MODE_AFTER="$(git -C "$FIX_REPO" ls-files -s -- .githooks/pre-commit | cut -d' ' -f1)"
    if [ "$MODE_AFTER" = "100755" ]; then
        pass "--fix repairs a 100644 entrypoint"
    else
        fail "--fix repairs a 100644 entrypoint" "index mode after --fix is $MODE_AFTER, expected 100755"
    fi
fi

echo ""
echo "Passed: $PASSED"
echo "Failed: $FAILED"

if [ "$FAILED" -gt 0 ]; then
    echo ""
    echo "Failed tests:"
    for name in "${FAILED_NAMES[@]}"; do
        echo "  - $name"
    done
    exit 1
fi

exit 0
