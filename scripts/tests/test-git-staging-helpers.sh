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

assert_staging_cleanup() {
    if ( : >&200 ) 2>descriptor-probe; then
        printf 'descriptor 200 remains open\n' >&2
        return 1
    fi
    local flock_path
    flock_path="$(type -P flock || true)"
    if [[ -n "$flock_path" ]]; then
        "$flock_path" -n "$GIT_HELPERS_LOCK_FILE" true || return 1
    fi
    printf '__STDERR_AFTER_STAGING__\n' >&2
}

run_test
if (
    repo_path="$TMP_DIR/missing-path"
    create_repo "$repo_path"
    cd "$repo_path" || exit 1
    export GIT_HELPERS_LOCK_FILE="$repo_path/staging.lock"
    export GIT_LOCK_MAX_ATTEMPTS=1
    # shellcheck disable=SC1090
    source "$HELPERS_PATH"
    printf 'staged sentinel\n' > sentinel.txt
    git_add_with_retry sentinel.txt || exit 1
    cp .git/index index-before || exit 1
    direct_status=0
    command git add -- missing-path >direct-stdout 2>direct-stderr || direct_status=$?
    helper_status=0
    git_add_with_retry missing-path >helper-stdout 2>helper-stderr || helper_status=$?
    printf 'direct=%s helper=%s\n' "$direct_status" "$helper_status"
    [[ "$direct_status" -ne 0 && "$helper_status" -eq "$direct_status" ]] || exit 1
    [[ "$(cat helper-stderr)" == *"pathspec 'missing-path' did not match any files"* ]] || exit 1
    cmp index-before .git/index || exit 1
    assert_staging_cleanup || exit 1
) >"$TMP_DIR/missing-path-output" 2>&1; then
    pass "Missing path returns Git's exact failure and preserves the staged index"
else
    fail "Missing path returns Git's exact failure and preserves the staged index" "$(cat "$TMP_DIR/missing-path-output")"
fi

run_test
repo_path="$TMP_DIR/strict-failure"
create_repo "$repo_path"
strict_status=0
bash -c '
    set -euo pipefail
    cd "$1"
    export GIT_HELPERS_LOCK_FILE="$1/staging.lock"
    export GIT_LOCK_MAX_ATTEMPTS=1
    source "$2"
    git_add_with_retry missing-path
    printf "unexpected continuation\n" > caller-continued
' bash "$repo_path" "$HELPERS_PATH" >"$TMP_DIR/strict-failure-output" 2>&1 || strict_status=$?
if [[ "$strict_status" -eq 128 && ! -e "$repo_path/caller-continued" ]]; then
    pass "A strict-shell caller stops at the missing-path staging failure"
else
    fail "A strict-shell caller stops at the missing-path staging failure" "status=$strict_status $(cat "$TMP_DIR/strict-failure-output")"
fi

for scenario in nonretry-1 nonretry-2 nonretry-37 retry-code retry-message exhausted-code exhausted-message failed-flock-success failed-flock-error failed-flock-no-executable; do
    run_test
    if (
        repo_path="$TMP_DIR/$scenario"
        create_repo "$repo_path"
        cd "$repo_path" || exit 1
        export GIT_HELPERS_LOCK_FILE="$repo_path/staging.lock"
        export GIT_LOCK_MAX_ATTEMPTS=3
        export GIT_LOCK_INITIAL_DELAY_MS=1
        export GIT_LOCK_MAX_DELAY_MS=1
        # shellcheck disable=SC1090
        source "$HELPERS_PATH"
        flock_wait_attempts=0
        flock_reacquire_attempts=0
        if [[ "$scenario" == failed-flock-no-executable ]]; then
            type() {
                if [[ "$*" == '-P flock' ]]; then
                    return 1
                fi
                builtin type "$@"
            }
        fi
        if [[ "$scenario" == failed-flock-* ]]; then
            flock() {
                if [[ "${1:-}" == -w ]]; then
                    flock_wait_attempts=$((flock_wait_attempts + 1))
                    return 1
                fi
                if [[ "$scenario" == failed-flock-no-executable ]]; then
                    flock_reacquire_attempts=$((flock_reacquire_attempts + 1))
                    return 127
                fi
                command flock "$@"
            }
        fi
        printf 'stage after recovery\n' > sample.txt
        add_attempts=0
        git() {
            if [[ "${1:-}" != add ]]; then
                command git "$@"
                return $?
            fi
            add_attempts=$((add_attempts + 1))
            case "$scenario" in
                failed-flock-error)
                    printf 'controlled staging failure after flock timeout\n' >&2
                    return 37
                    ;;
                nonretry-*)
                    printf 'controlled nonretry failure\n' >&2
                    return "${scenario#nonretry-}"
                    ;;
                retry-code|exhausted-code)
                    if [[ "$scenario" == exhausted-code || "$add_attempts" -eq 1 ]]; then
                        printf 'controlled retry status\n' >&2
                        return 128
                    fi
                    ;;
                retry-message|exhausted-message)
                    if [[ "$scenario" == exhausted-message || "$add_attempts" -eq 1 ]]; then
                        printf 'controlled index.lock contention\n' >&2
                        return 7
                    fi
                    ;;
            esac
            command git "$@"
        }
        helper_status=0
        git_add_with_retry sample.txt || helper_status=$?
        printf 'scenario=%s attempts=%s status=%s\n' "$scenario" "$add_attempts" "$helper_status"
        case "$scenario" in
            failed-flock-success|failed-flock-no-executable)
                [[ "$flock_wait_attempts" -eq 1 ]] || exit 1
                [[ "$helper_status" -eq 0 && "$add_attempts" -eq 1 ]] || exit 1
                [[ "$(command git show :sample.txt)" == 'stage after recovery' ]] || exit 1
                ;;
            failed-flock-error)
                [[ "$flock_wait_attempts" -eq 1 ]] || exit 1
                [[ "$helper_status" -eq 37 && "$add_attempts" -eq 1 ]] || exit 1
                [[ -z "$(command git diff --cached --name-only)" ]] || exit 1
                ;;
            nonretry-*)
                [[ "$helper_status" -eq "${scenario#nonretry-}" && "$add_attempts" -eq 1 ]] || exit 1
                [[ -z "$(command git diff --cached --name-only)" ]] || exit 1
                ;;
            retry-*)
                [[ "$helper_status" -eq 0 && "$add_attempts" -eq 2 ]] || exit 1
                [[ "$(command git show :sample.txt)" == 'stage after recovery' ]] || exit 1
                ;;
            exhausted-*)
                expected_status=128
                [[ "$scenario" != exhausted-message ]] || expected_status=7
                [[ "$helper_status" -eq "$expected_status" && "$add_attempts" -eq 3 ]] || exit 1
                [[ -z "$(command git diff --cached --name-only)" ]] || exit 1
                ;;
        esac
        assert_staging_cleanup || exit 1
        if [[ "$scenario" == failed-flock-no-executable ]]; then
            [[ "$flock_reacquire_attempts" -eq 0 ]] || exit 1
        fi
    ) >"$TMP_DIR/$scenario-output" 2>&1; then
        if [[ "$(cat "$TMP_DIR/$scenario-output")" == *"__STDERR_AFTER_STAGING__"* ]]; then
            pass "$scenario preserves status, attempt bounds, flock cleanup and stderr"
        else
            fail "$scenario preserves status, attempt bounds, flock cleanup and stderr" "stderr was suppressed"
        fi
    else
        fail "$scenario preserves status, attempt bounds, flock cleanup and stderr" "$(cat "$TMP_DIR/$scenario-output")"
    fi
done

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
            $script:requestedSleep = -1
            function Start-Sleep {
                param([int]$Milliseconds)
                $script:requestedSleep = $Milliseconds
                if ($Milliseconds -gt 1) { throw "Lock polling exceeded its one-millisecond deadline" }
            }
            if (Wait-ForGitIndexLock -IndexLockPath $info.IndexLockPath -MaxWaitMilliseconds 1 -PollIntervalMilliseconds 5000) {
                throw "The held index lock was reported clear"
            }
            if ($script:requestedSleep -ne 1) { throw "The capped lock wait was not exercised" }
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
