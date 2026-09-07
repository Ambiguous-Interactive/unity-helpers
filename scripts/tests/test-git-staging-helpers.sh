#!/usr/bin/env bash
# Regression tests for scripts/git-staging-helpers.sh runtime behavior.
# Focus: prevent hidden stderr suppression and trap leakage that can mask
# hook failures and make diagnostics unreliable.

set -euo pipefail

# cspell:ignore Fxq gpgsign

# A hook caller's repository and index must never redirect these temporary-repository tests.
local_git_environment="$(git rev-parse --local-env-vars)"
while IFS= read -r git_environment_name; do
    unset "$git_environment_name"
done <<< "$local_git_environment"
unset local_git_environment git_environment_name

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

export STAGING_HELPERS_ROOT="$REPO_ROOT"
for helper_shell in bash powershell; do
    for commit_kind in ordinary partial alternate; do
        run_test
        if (
            set -euo pipefail
            repo_path="$TMP_DIR/$helper_shell-$commit_kind"
            create_repo "$repo_path"
            cd "$repo_path"
            export GIT_HELPERS_LOCK_FILE="$repo_path/.git-staging.lock"
            source "$HELPERS_PATH"
            printf 'before\n' > selected.txt
            printf 'before\n' > unrelated.txt
            git_add_with_retry selected.txt unrelated.txt
            git -c core.hooksPath= -c commit.gpgsign=false commit -qm initial
            printf 'after\n' > selected.txt
            printf 'after\n' > unrelated.txt
            if [[ "$commit_kind" == alternate ]]; then
                cp .git/index 'alternate index'
                export GIT_INDEX_FILE='alternate index'
            fi
            git_add_with_retry selected.txt unrelated.txt
            mkdir hooks
            git config core.hooksPath hooks
            if [[ "$helper_shell" == bash ]]; then
                cat > hooks/pre-commit <<'HOOK'
#!/usr/bin/env bash
set -euo pipefail
source "$STAGING_HELPERS_ROOT/scripts/git-staging-helpers.sh"
ensure_no_index_lock 100
printf 'hook\n' >> selected.txt
git_add_with_retry selected.txt
HOOK
            else
                cat > hooks/pre-commit <<'HOOK'
#!/usr/bin/env bash
exec pwsh -NoProfile -File hooks/check.ps1
HOOK
                cat > hooks/check.ps1 <<'HOOK'
$ErrorActionPreference = 'Stop'
. "$env:STAGING_HELPERS_ROOT/scripts/git-staging-helpers.ps1"
if (-not (Invoke-EnsureNoIndexLock -MaxWaitMilliseconds 100)) { exit 1 }
Add-Content -LiteralPath selected.txt -Value 'hook'
$info = Get-GitRepositoryInfo
Invoke-GitAddWithRetry -Items @('selected.txt') -IndexLockPath $info.IndexLockPath
HOOK
            fi
            chmod +x hooks/pre-commit
            if [[ "$commit_kind" == partial ]]; then
                git -c commit.gpgsign=false commit -qm partial --only -- selected.txt || exit 1
                [[ "$(git show HEAD:unrelated.txt)" == before ]] || exit 1
                [[ "$(git diff --cached --name-only -- unrelated.txt)" == unrelated.txt ]] || exit 1
                [[ "$(git show :unrelated.txt)" == after ]] || exit 1
            else
                git -c commit.gpgsign=false commit -qm complete || exit 1
                [[ "$(git show HEAD:unrelated.txt)" == after ]] || exit 1
                [[ -z "$(git diff --cached --name-only)" ]] || exit 1
            fi
            [[ "$(git show HEAD:selected.txt)" == $'after\nhook' ]] || exit 1
        ) > "$TMP_DIR/commit-output" 2>&1; then
            pass "$helper_shell $commit_kind commit checks and restages the effective index"
        else
            fail "$helper_shell $commit_kind commit checks and restages the effective index" "$(cat "$TMP_DIR/commit-output")"
        fi
    done
    for index_kind in ordinary alternate worktree; do
        run_test
        if (
            set -euo pipefail
            repo_path="$TMP_DIR/$helper_shell-contention-$index_kind"
            create_repo "$repo_path"
            cd "$repo_path"
            git -c core.hooksPath= -c commit.gpgsign=false commit --allow-empty -qm initial || exit 1
            if [[ "$index_kind" == worktree ]]; then
                git worktree add -qb linked "$repo_path/linked" || exit 1
                cd linked
            elif [[ "$index_kind" == alternate ]]; then
                export GIT_INDEX_FILE="$repo_path/alternate index"
            fi
            EXPECTED_INDEX_LOCK="$(git rev-parse --git-path index).lock"
            export EXPECTED_INDEX_LOCK
            printf 'external writer\n' > "$EXPECTED_INDEX_LOCK"
            if [[ "$helper_shell" == bash ]]; then
                source "$HELPERS_PATH"
                [[ "$(get_index_lock_path)" == "$EXPECTED_INDEX_LOCK" ]] || exit 1
                if ensure_no_index_lock 100; then exit 1; fi
            else
                EXPECTED_INDEX_LOCK="$(realpath "$EXPECTED_INDEX_LOCK")"
                pwsh -NoProfile -Command '
                    $ErrorActionPreference = "Stop"
                    . "$env:STAGING_HELPERS_ROOT/scripts/git-staging-helpers.ps1"
                    if ((Get-GitRepositoryInfo).IndexLockPath -ne $env:EXPECTED_INDEX_LOCK) { exit 1 }
                    if (Invoke-EnsureNoIndexLock -MaxWaitMilliseconds 100) { exit 1 }
                ' || exit 1
            fi
            [[ "$(cat "$EXPECTED_INDEX_LOCK")" == 'external writer' ]] || exit 1
            rm "$EXPECTED_INDEX_LOCK"
            if [[ "$helper_shell" == bash ]]; then
                ensure_no_index_lock 100 || exit 1
            else
                pwsh -NoProfile -Command '
                    . "$env:STAGING_HELPERS_ROOT/scripts/git-staging-helpers.ps1"
                    if (-not (Invoke-EnsureNoIndexLock -MaxWaitMilliseconds 100)) { exit 1 }
                ' || exit 1
            fi
        ) > "$TMP_DIR/contention-output" 2>&1; then
            pass "$helper_shell protects the $index_kind index until its lock is released"
        else
            fail "$helper_shell protects the $index_kind index until its lock is released" "$(cat "$TMP_DIR/contention-output")"
        fi
    done
done

echo ""
for index_kind in ordinary relative; do
    run_test
    if (
        set -euo pipefail
        repo_path="$TMP_DIR/location-$index_kind"
        create_repo "$repo_path"
        mkdir "$repo_path/nested"
        export INDEX_LOCATION_ROOT="$repo_path"
        export INDEX_LOCATION_KIND="$index_kind"
        pwsh -NoProfile -Command '
            $ErrorActionPreference = "Stop"
            . "$env:STAGING_HELPERS_ROOT/scripts/git-staging-helpers.ps1"
            Set-Location (Join-Path $env:INDEX_LOCATION_ROOT "nested")
            $expected = Join-Path $env:INDEX_LOCATION_ROOT ".git/index.lock"
            if ($env:INDEX_LOCATION_KIND -eq "relative") {
                $env:GIT_INDEX_FILE = "alternate index"
                $expected = Join-Path $env:INDEX_LOCATION_ROOT "alternate index.lock"
            }
            $info = Get-GitRepositoryInfo
            if ($info.IndexLockPath -ne $expected) { throw "Index path was not captured absolutely" }
            [System.IO.File]::WriteAllText($expected, "external writer")
            Set-Location $env:INDEX_LOCATION_ROOT
            if (Wait-ForGitIndexLock -IndexLockPath $info.IndexLockPath -MaxWaitMilliseconds 0) {
                throw "Changing location hid the captured index lock"
            }
            Remove-Item -LiteralPath $expected
            if (-not (Wait-ForGitIndexLock -IndexLockPath $info.IndexLockPath -MaxWaitMilliseconds 0)) {
                throw "Released index is still reported locked"
            }
        '
    ) > "$TMP_DIR/location-output" 2>&1; then
        pass "PowerShell $index_kind index remains valid after changing location"
    else
        fail "PowerShell $index_kind index remains valid after changing location" "$(cat "$TMP_DIR/location-output")"
    fi
done

if [[ "${GIT_HELPERS_ISOLATION_PROBE:-0}" != 1 ]]; then
    for suite in test-git-staging-helpers.sh test-precommit-integration.sh; do
        run_test
        if (
            set -euo pipefail
            caller="$TMP_DIR/caller-$suite"
            create_repo "$caller"
            cd "$caller"
            source "$HELPERS_PATH"
            printf 'caller index sentinel\n' > sentinel.txt
            git_add_with_retry sentinel.txt
            cp .git/index index-before
            cp .git/config config-before
            export GIT_DIR="$caller/.git"
            export GIT_COMMON_DIR="$caller/.git"
            export GIT_WORK_TREE="$caller"
            export GIT_INDEX_FILE="$caller/.git/index"
            export GIT_HELPERS_ISOLATION_PROBE=1
            bash "$SCRIPT_DIR/$suite" || exit 1
            cmp index-before .git/index || exit 1
            cmp config-before .git/config || exit 1
            [[ "$(cat sentinel.txt)" == 'caller index sentinel' ]] || exit 1
        ) > "$TMP_DIR/isolation-output" 2>&1; then
            pass "$suite preserves the caller index and config under inherited Git environment"
        else
            fail "$suite preserves the caller index and config under inherited Git environment" "$(cat "$TMP_DIR/isolation-output")"
        fi
    done
fi

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
