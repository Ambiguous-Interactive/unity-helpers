#!/usr/bin/env bash
# =============================================================================
# Test Script: Pre-push changed-file detection
# =============================================================================
# Validates that the pre-push hook correctly parses stdin to determine
# which files have changed, handling edge cases like new branches,
# force pushes, and delete refs.
#
# Run: bash scripts/tests/test-pre-push-changed-files.sh
# Exit codes: 0 = all tests pass, 1 = test failure
# =============================================================================

set -euo pipefail

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

# Get repo root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PRE_PUSH="$REPO_ROOT/.githooks/pre-push"

extract_pre_push_helpers() {
    local helper_script="$1"
    awk '
        /^clean_gitignored_hook_artifacts "\$REPO_ROOT"$/ { exit }
        /^while read -r _local_ref local_sha _remote_ref remote_sha; do$/ { exit }
        { print }
    ' "$PRE_PUSH" > "$helper_script"
}

run_pre_push_helper_script() {
    local script_body="$1"
    local helper_script
    local runner_script

    helper_script=$(mktemp)
    runner_script=$(mktemp)

    if ! extract_pre_push_helpers "$helper_script"; then
        rm -f "$helper_script" "$runner_script"
        return 1
    fi

    cat > "$runner_script" <<EOF
#!/usr/bin/env bash
set -euo pipefail
source "$helper_script"
trap - EXIT INT TERM
$script_body
EOF

    if bash "$runner_script"; then
        rm -f "$helper_script" "$runner_script"
        return 0
    fi

    rm -f "$helper_script" "$runner_script"
    return 1
}

# =============================================================================
# Test: Pre-push hook file exists and is executable
# =============================================================================
echo ""
echo "=== Testing pre-push hook existence and permissions ==="

run_test
if [ -f "$PRE_PUSH" ]; then
    pass "Pre-push hook exists"
else
    fail "Pre-push hook exists" "file exists" "file not found"
fi

run_test
if [ -x "$PRE_PUSH" ]; then
    pass "Pre-push hook is executable"
else
    fail "Pre-push hook is executable" "executable" "not executable"
fi

# =============================================================================
# Test: Pre-push hook reads stdin
# =============================================================================
echo ""
echo "=== Testing pre-push hook reads stdin ==="

run_test
if grep -q 'while read' "$PRE_PUSH"; then
    pass "Hook contains stdin read loop"
else
    fail "Hook contains stdin read loop" "'while read' present" "not found"
fi

run_test
if grep -q 'local_sha' "$PRE_PUSH"; then
    pass "Hook parses local_sha"
else
    fail "Hook parses local_sha" "'local_sha' present" "not found"
fi

run_test
if grep -q 'remote_sha' "$PRE_PUSH"; then
    pass "Hook parses remote_sha"
else
    fail "Hook parses remote_sha" "'remote_sha' present" "not found"
fi

run_test
if grep -q 'ZERO_SHA' "$PRE_PUSH"; then
    pass "Hook handles zero SHA (new branch/delete)"
else
    fail "Hook handles zero SHA" "'ZERO_SHA' present" "not found"
fi

# =============================================================================
# Test: Changed-file collection and safe transport
# =============================================================================
echo ""
echo "=== Testing changed-file collection and safe transport ==="

run_test
if grep -q 'ALL_CHANGED_FILES' "$PRE_PUSH"; then
    pass "Hook collects changed files into ALL_CHANGED_FILES array"
else
    fail "Hook collects changed files into ALL_CHANGED_FILES array" "ALL_CHANGED_FILES present" "not found"
fi

run_test
if grep -q 'collect_changed_files' "$PRE_PUSH"; then
    pass "Hook uses collect_changed_files helper"
else
    fail "Hook uses collect_changed_files helper" "collect_changed_files present" "not found"
fi

run_test
if grep -q -- '--name-only -z' "$PRE_PUSH"; then
    pass "Hook requests null-delimited git file lists"
else
    fail "Hook requests null-delimited git file lists" "--name-only -z present" "not found"
fi

run_test
diff_filter_count=$(grep -c -- '--diff-filter=ACMRTUXB' "$PRE_PUSH" || true)
if [ "$diff_filter_count" -ge 2 ]; then
    pass "Hook excludes deleted paths from changed-file validation"
else
    fail "Hook excludes deleted paths from changed-file validation" "diff-filter=ACMRTUXB on git diff calls" "found $diff_filter_count occurrence(s)"
fi

run_test
if grep -q "read -r -d ''" "$PRE_PUSH"; then
    pass "Hook reads changed files with null-delimited loop"
else
    fail "Hook reads changed files with null-delimited loop" "read -r -d '' present" "not found"
fi

run_test
if ! grep -q 'PID_NODE' "$PRE_PUSH" && ! grep -q 'run_.* &' "$PRE_PUSH"; then
    pass "Hook has no stale background-process cleanup path"
else
    fail "Hook has no stale background-process cleanup path" "no background PID cleanup" "found stale background cleanup markers"
fi

run_test
if run_pre_push_helper_script '
ALL_CHANGED_FILES=()
array_contains_exact "-plugin=evil.js" "normal.txt" "-plugin=evil.js"
array_contains_exact "file with spaces.cs" "file with spaces.cs"
if array_contains_exact "missing.txt" "normal.txt" "-plugin=evil.js"; then
    exit 1
fi
if array_contains_exact "anything"; then
    exit 1
fi
'; then
    pass "array_contains_exact handles spaces, leading dashes, and empty arrays"
else
    fail "array_contains_exact handles spaces, leading dashes, and empty arrays" "helper returns correct exact-match results" "helper returned unexpected result"
fi

run_test
if run_pre_push_helper_script '
ALL_CHANGED_FILES=()
add_changed_file "file with spaces.cs"
add_changed_file "-plugin=evil.js"
add_changed_file "file with spaces.cs"
[[ ${#ALL_CHANGED_FILES[@]} -eq 3 ]]
[[ "${ALL_CHANGED_FILES[0]}" == "file with spaces.cs" ]]
[[ "${ALL_CHANGED_FILES[1]}" == "-plugin=evil.js" ]]
[[ "${ALL_CHANGED_FILES[2]}" == "file with spaces.cs" ]]
'; then
    pass "add_changed_file appends exact filenames without shell splitting"
else
    fail "add_changed_file appends exact filenames without shell splitting" "three exact filenames preserved in insertion order" "exact storage failed"
fi

run_test
if run_pre_push_helper_script '
ALL_CHANGED_FILES=()
CS_SCAN_SHAS=()
CS_SCAN_PATHS=()
scan_sha="0123456789012345678901234567890123456789"
input_file=$(mktemp)
printf "file with spaces.cs\0-plugin=evil.js\0file with spaces.cs\0" > "$input_file"
collect_changed_files "$scan_sha" < "$input_file"
rm -f "$input_file"
[[ ${#ALL_CHANGED_FILES[@]} -eq 3 ]]
[[ "${ALL_CHANGED_FILES[0]}" == "file with spaces.cs" ]]
[[ "${ALL_CHANGED_FILES[1]}" == "-plugin=evil.js" ]]
[[ "${ALL_CHANGED_FILES[2]}" == "file with spaces.cs" ]]
[[ ${#CS_SCAN_PATHS[@]} -eq 2 ]]
[[ "${CS_SCAN_SHAS[0]}" == "$scan_sha" ]]
[[ "${CS_SCAN_SHAS[1]}" == "$scan_sha" ]]
[[ "${CS_SCAN_PATHS[0]}" == "file with spaces.cs" ]]
[[ "${CS_SCAN_PATHS[1]}" == "file with spaces.cs" ]]
'; then
    pass "collect_changed_files parses NUL-delimited input safely"
else
    fail "collect_changed_files parses NUL-delimited input safely" "exact files and C# scan pairs parsed from NUL-delimited stream" "parser failed for exact filename transport or C# pairing"
fi

run_test
if run_pre_push_helper_script '
sandbox=$(mktemp -d)
cleanup_sandbox() { rm -rf "$sandbox"; }
trap cleanup_sandbox EXIT
git -C "$sandbox" init -q
mkdir -p "$sandbox/.githooks"
printf "#!/usr/bin/env bash\n" > "$sandbox/.githooks/pre-push"
printf "pre-push.txt*\n.githooks/*.txt\n" > "$sandbox/.gitignore"
printf "redirected output\n" > "$sandbox/pre-push.txt"
printf "redirected output\n" > "$sandbox/.githooks/pre-push.txt"
clean_gitignored_hook_artifacts "$sandbox"
[[ ! -e "$sandbox/pre-push.txt" ]]
[[ ! -e "$sandbox/.githooks/pre-push.txt" ]]
'; then
    pass "pre-push auto-removes gitignored root and .githooks hook artifacts"
else
    fail "pre-push auto-removes gitignored root and .githooks hook artifacts" "ignored artifacts removed" "cleanup did not remove expected files"
fi

run_test
if run_pre_push_helper_script '
sandbox=$(mktemp -d)
cleanup_sandbox() { rm -rf "$sandbox"; }
trap cleanup_sandbox EXIT
git -C "$sandbox" init -q
mkdir -p "$sandbox/.githooks"
printf "#!/usr/bin/env bash\n" > "$sandbox/.githooks/pre-push"
printf ".githooks/*.txt\n" > "$sandbox/.gitignore"
printf "local note\n" > "$sandbox/.githooks/notes.txt"
clean_gitignored_hook_artifacts "$sandbox"
[[ -e "$sandbox/.githooks/notes.txt" ]]
'; then
    pass "pre-push artifact cleanup ignores non-hook-named .githooks files"
else
    fail "pre-push artifact cleanup ignores non-hook-named .githooks files" "notes.txt preserved" "cleanup treated non-hook note as artifact"
fi

run_test
if run_pre_push_helper_script '
sandbox=$(mktemp -d)
cleanup_sandbox() { rm -rf "$sandbox"; }
trap cleanup_sandbox EXIT
git -C "$sandbox" init -q
mkdir -p "$sandbox/.githooks"
printf "#!/usr/bin/env bash\n" > "$sandbox/.githooks/pre-push"
printf "intentional note\n" > "$sandbox/pre-push.txt"
if clean_gitignored_hook_artifacts "$sandbox"; then
    exit 1
fi
[[ -e "$sandbox/pre-push.txt" ]]
'; then
    pass "pre-push refuses to delete hook artifacts that are not gitignored"
else
    fail "pre-push refuses to delete hook artifacts that are not gitignored" "cleanup fails and preserves file" "cleanup deleted or accepted an unsafe file"
fi

run_test
sandbox=$(mktemp -d)
cleanup_output_file=$(mktemp)
git -C "$sandbox" init -q
mkdir -p "$sandbox/.githooks"
printf "#!/usr/bin/env bash\n" > "$sandbox/.githooks/pre-push"
printf "pre-push.txt*\n" > "$sandbox/.gitignore"
printf "redirected output\n" > "$sandbox/pre-push.txt"
if (cd "$sandbox" && bash "$PRE_PUSH" </dev/null >"$cleanup_output_file" 2>&1) && [ ! -e "$sandbox/pre-push.txt" ]; then
    pass "pre-push cleanup runs before no-ref early exit"
else
    cleanup_output=$(cat "$cleanup_output_file" 2>/dev/null || true)
    fail "pre-push cleanup runs before no-ref early exit" "artifact removed and hook exits zero" "$cleanup_output"
fi
rm -rf "$sandbox" "$cleanup_output_file"

run_test
sandbox=$(mktemp -d)
subdir_output_file=$(mktemp)
git -C "$sandbox" init -q
git -C "$sandbox" config user.email "test@example.com"
git -C "$sandbox" config user.name "Test User"
mkdir -p "$sandbox/Runtime" "$sandbox/subdir"
printf "public sealed class RegionGuard\n{\n#region Bad\n}\n" > "$sandbox/Runtime/RegionGuard.cs"
git -C "$sandbox" add Runtime/RegionGuard.cs
git -C "$sandbox" commit -q -m "Add region guard fixture"
local_sha=$(git -C "$sandbox" rev-parse HEAD)
zero_sha="0000000000000000000000000000000000000000"
if (cd "$sandbox/subdir" && printf "refs/heads/main %s refs/heads/main %s\n" "$local_sha" "$zero_sha" | bash "$PRE_PUSH" >"$subdir_output_file" 2>&1); then
    subdir_output=$(cat "$subdir_output_file" 2>/dev/null || true)
    fail "pre-push anchors execution to repo root for repo-relative changed paths" "hook fails on forbidden #region from subdirectory" "$subdir_output"
else
    subdir_output=$(cat "$subdir_output_file" 2>/dev/null || true)
    if printf "%s" "$subdir_output" | grep -q "RegionGuard.cs" && printf "%s" "$subdir_output" | grep -q "#region"; then
        pass "pre-push anchors execution to repo root for repo-relative changed paths"
    else
        fail "pre-push anchors execution to repo root for repo-relative changed paths" "region violation reported for Runtime/RegionGuard.cs" "$subdir_output"
    fi
fi
rm -rf "$sandbox" "$subdir_output_file"

run_test
sandbox=$(mktemp -d)
commit_output_file=$(mktemp)
git -C "$sandbox" init -q
git -C "$sandbox" config user.email "test@example.com"
git -C "$sandbox" config user.name "Test User"
mkdir -p "$sandbox/Runtime"
printf "public sealed class RegionGuard\n{\n#region Bad\n}\n" > "$sandbox/Runtime/RegionGuard.cs"
git -C "$sandbox" add Runtime/RegionGuard.cs
git -C "$sandbox" commit -q -m "Commit region violation"
local_sha=$(git -C "$sandbox" rev-parse HEAD)
printf "public sealed class RegionGuard\n{\n}\n" > "$sandbox/Runtime/RegionGuard.cs"
zero_sha="0000000000000000000000000000000000000000"
if (cd "$sandbox" && printf "refs/heads/main %s refs/heads/main %s\n" "$local_sha" "$zero_sha" | bash "$PRE_PUSH" >"$commit_output_file" 2>&1); then
    commit_output=$(cat "$commit_output_file" 2>/dev/null || true)
    fail "pre-push scans pushed commit when worktree removes #region" "hook fails on committed #region" "$commit_output"
else
    commit_output=$(cat "$commit_output_file" 2>/dev/null || true)
    if printf "%s" "$commit_output" | grep -q "RegionGuard.cs" && printf "%s" "$commit_output" | grep -q "#region"; then
        pass "pre-push scans pushed commit when worktree removes #region"
    else
        fail "pre-push scans pushed commit when worktree removes #region" "committed region violation reported" "$commit_output"
    fi
fi
rm -rf "$sandbox" "$commit_output_file"

run_test
sandbox=$(mktemp -d)
worktree_output_file=$(mktemp)
git -C "$sandbox" init -q
git -C "$sandbox" config user.email "test@example.com"
git -C "$sandbox" config user.name "Test User"
mkdir -p "$sandbox/Runtime"
printf "public sealed class RegionGuard\n{\n}\n" > "$sandbox/Runtime/RegionGuard.cs"
git -C "$sandbox" add Runtime/RegionGuard.cs
git -C "$sandbox" commit -q -m "Commit clean region guard fixture"
local_sha=$(git -C "$sandbox" rev-parse HEAD)
printf "public sealed class RegionGuard\n{\n#region DirtyWorktreeOnly\n}\n" > "$sandbox/Runtime/RegionGuard.cs"
zero_sha="0000000000000000000000000000000000000000"
if (cd "$sandbox" && printf "refs/heads/main %s refs/heads/main %s\n" "$local_sha" "$zero_sha" | bash "$PRE_PUSH" >"$worktree_output_file" 2>&1); then
    pass "pre-push ignores uncommitted worktree #region not in pushed commit"
else
    worktree_output=$(cat "$worktree_output_file" 2>/dev/null || true)
    fail "pre-push ignores uncommitted worktree #region not in pushed commit" "hook passes because pushed commit is clean" "$worktree_output"
fi
rm -rf "$sandbox" "$worktree_output_file"

run_test
sandbox=$(mktemp -d)
colon_output_file=$(mktemp)
git -C "$sandbox" init -q
git -C "$sandbox" config user.email "test@example.com"
git -C "$sandbox" config user.name "Test User"
mkdir -p "$sandbox/Runtime"
if printf "public sealed class ColonRegion\n{\n#region Bad\n}\n" > "$sandbox/Runtime/Foo:Bar.cs" 2>/dev/null && \
   git -C "$sandbox" add "Runtime/Foo:Bar.cs" 2>/dev/null; then
    git -C "$sandbox" commit -q -m "Commit colon path region violation"
    local_sha=$(git -C "$sandbox" rev-parse HEAD)
    zero_sha="0000000000000000000000000000000000000000"
    if (cd "$sandbox" && printf "refs/heads/main %s refs/heads/main %s\n" "$local_sha" "$zero_sha" | bash "$PRE_PUSH" >"$colon_output_file" 2>&1); then
        colon_output=$(cat "$colon_output_file" 2>/dev/null || true)
        fail "pre-push detects #region in changed C# paths containing colon" "hook fails on Runtime/Foo:Bar.cs" "$colon_output"
    else
        colon_output=$(cat "$colon_output_file" 2>/dev/null || true)
        if printf "%s" "$colon_output" | grep -q "Runtime/Foo:Bar.cs" && printf "%s" "$colon_output" | grep -q "#region"; then
            pass "pre-push detects #region in changed C# paths containing colon"
        else
            fail "pre-push detects #region in changed C# paths containing colon" "colon path region violation reported" "$colon_output"
        fi
    fi
else
    pass "pre-push detects #region in changed C# paths containing colon (filesystem unsupported)"
fi
rm -rf "$sandbox" "$colon_output_file"

run_test
sandbox=$(mktemp -d)
bracket_output_file=$(mktemp)
git -C "$sandbox" init -q
git -C "$sandbox" config user.email "test@example.com"
git -C "$sandbox" config user.name "Test User"
mkdir -p "$sandbox/Runtime"
printf "public sealed class Fooa\n{\n#region ExistingBaseViolation\n}\n" > "$sandbox/Runtime/Fooa.cs"
git -C "$sandbox" add Runtime/Fooa.cs
git -C "$sandbox" commit -q -m "Base commit with unrelated existing region"
remote_sha=$(git -C "$sandbox" rev-parse HEAD)
printf "public sealed class FooBracket { }\n" > "$sandbox/Runtime/Foo[abc].cs"
git -C "$sandbox" add "Runtime/Foo[abc].cs"
git -C "$sandbox" commit -q -m "Add clean bracket path"
local_sha=$(git -C "$sandbox" rev-parse HEAD)
if (cd "$sandbox" && printf "refs/heads/main %s refs/heads/main %s\n" "$local_sha" "$remote_sha" | bash "$PRE_PUSH" >"$bracket_output_file" 2>&1); then
    pass "pre-push treats changed C# paths with [] as literal pathspecs"
else
    bracket_output=$(cat "$bracket_output_file" 2>/dev/null || true)
    fail "pre-push treats changed C# paths with [] as literal pathspecs" "hook passes because changed Foo[abc].cs is clean" "$bracket_output"
fi
rm -rf "$sandbox" "$bracket_output_file"

# =============================================================================
# Test: New branch handling (merge-base fallback)
# =============================================================================
echo ""
echo "=== Testing new branch handling ==="

run_test
if grep -qE 'merge-base|merge_base' "$PRE_PUSH"; then
    pass "Hook uses merge-base for new branches"
else
    fail "Hook uses merge-base for new branches" "merge-base present" "not found"
fi

# =============================================================================
# Test: No auto-fix behavior (validation-only)
# =============================================================================
echo ""
echo "=== Testing validation-only behavior ==="

run_test
# Check that the hook does not EXECUTE Prettier with --write (auto-fix)
# Mentioning it in user-facing hints is fine.
if grep -v '^[[:space:]]*echo' "$PRE_PUSH" | grep -Eiq '(prettier|run-prettier\.js).*--write'; then
    fail "No prettier --write execution in pre-push" "no auto-fix" "prettier --write found"
else
    pass "No prettier --write execution in pre-push (validation-only)"
fi

run_test
if grep -q 'normalize-eol' "$PRE_PUSH"; then
    fail "No normalize-eol in pre-push" "no EOL auto-fix" "normalize-eol found"
else
    pass "No normalize-eol in pre-push (validation-only)"
fi

# =============================================================================
# Test: Fast execution model
# =============================================================================
echo ""
echo "=== Testing fast execution model ==="

run_test
if grep -q 'run_fast_checks' "$PRE_PUSH"; then
    pass "Hook uses fast local check function"
else
    fail "Hook uses fast local check function" "run_fast_checks present" "not found"
fi

run_test
if grep -v '^[[:space:]]*#' "$PRE_PUSH" | grep -Eq '(^|[[:space:]])(node|pwsh|powershell)([[:space:]]|$)'; then
    fail "No direct Node or PowerShell execution in pre-push" "no node/pwsh/powershell commands" "found direct dependency"
else
    pass "No direct Node or PowerShell execution in pre-push"
fi

run_test
if ! grep -q 'audit-license-years.sh' "$PRE_PUSH"; then
    pass "License audit is delegated outside pre-push"
else
    fail "License audit is delegated outside pre-push" "no audit-license-years.sh invocation" "found license audit in pre-push"
fi

run_test
if grep -q 'HOOK_FAILED' "$PRE_PUSH"; then
    pass "Hook tracks failure status"
else
    fail "Hook tracks failure status" "HOOK_FAILED tracking" "not found"
fi

# =============================================================================
# Test: Bash compatibility
# =============================================================================
echo ""
echo "=== Testing bash compatibility ==="

run_test
SHEBANG=$(head -1 "$PRE_PUSH")
if [ "$SHEBANG" = "#!/usr/bin/env bash" ]; then
    pass "Shebang is #!/usr/bin/env bash"
else
    fail "Shebang is #!/usr/bin/env bash" "#!/usr/bin/env bash" "$SHEBANG"
fi

run_test
if bash -n "$PRE_PUSH"; then
    pass "Pre-push hook has valid bash syntax"
else
    fail "Pre-push hook has valid bash syntax" "bash -n passes" "bash -n failed"
fi

# =============================================================================
# Test: Emergency skip documentation
# =============================================================================
echo ""
echo "=== Testing emergency skip documentation ==="

run_test
if grep -q 'no-verify' "$PRE_PUSH"; then
    pass "Hook documents --no-verify escape hatch"
else
    fail "Hook documents --no-verify" "--no-verify mentioned" "not found"
fi

# =============================================================================
# Test: Behavioral regex tests (verify patterns actually match correctly)
# =============================================================================
echo ""
echo "=== Testing changed-file detection regex patterns ==="

# Validate the literal regexes embedded in the hook.

# Test CHANGED_LLM regex (C1 regression test - was ^\\.llm/ which matched backslash)
run_test
TEST_FILE=$(mktemp)
printf '.llm/context.md\n.llm/skills/foo.md\nRuntime/Foo.cs\n' > "$TEST_FILE"
# The hook uses '^\.llm/' in single quotes — in grep -E, \. matches literal dot
LLM_PATTERN='^\.llm/'
RESULT=$(grep -Ec "$LLM_PATTERN" "$TEST_FILE" 2>/dev/null || echo "0")
if [ "$RESULT" = "2" ]; then
    pass "LLM regex matches .llm/ paths correctly (matched $RESULT/2)"
else
    fail "LLM regex matches .llm/ paths" "2 matches" "$RESULT matches"
fi

# Verify it does NOT match with double-backslash (the former bug)
run_test
BAD_LLM_PATTERN='^\\.llm/'
BAD_RESULT=$(grep -Ec "$BAD_LLM_PATTERN" "$TEST_FILE" 2>/dev/null) || true
if [ "$BAD_RESULT" = "0" ]; then
    pass "Double-backslash regex correctly does NOT match .llm/ paths"
else
    fail "Double-backslash regex should not match" "0 matches" "$BAD_RESULT matches"
fi

# Test CHANGED_CS regex
run_test
printf 'Runtime/Foo.cs\nEditor/Bar.cs\nRuntime/Foo.cs.meta\ndocs/readme.md\n' > "$TEST_FILE"
CS_RESULT=$(grep -Ec '\.cs$' "$TEST_FILE" 2>/dev/null || echo "0")
if [ "$CS_RESULT" = "2" ]; then
    pass "CS regex matches .cs files (matched $CS_RESULT/2, excludes .cs.meta)"
else
    fail "CS regex matches .cs files" "2 matches" "$CS_RESULT matches"
fi

# Test region check pattern (POSIX ERE, not GNU BRE \|)
run_test
printf '  #region Foo\n  #endregion\n  // normal code\n#REGION Upper\n' > "$TEST_FILE"
REGION_RESULT=$(grep -E -c -i '^[[:space:]]*#[[:space:]]*(region|endregion)' "$TEST_FILE" 2>/dev/null || echo "0")
if [ "$REGION_RESULT" = "3" ]; then
    pass "Region regex matches #region/#endregion with POSIX character classes ($REGION_RESULT/3)"
else
    fail "Region regex matches correctly" "3 matches" "$REGION_RESULT matches"
fi

# Test CHANGED_GITIGNORE regex (pre-captured for C3 fix)
run_test
printf '.gitignore\ndocs/.gitignore\n.gitignore-backup\n' > "$TEST_FILE"
GITIGNORE_RESULT=$(grep -Ec '^\.gitignore$' "$TEST_FILE" 2>/dev/null || echo "0")
if [ "$GITIGNORE_RESULT" = "1" ]; then
    pass "Gitignore regex matches only root .gitignore ($GITIGNORE_RESULT/1)"
else
    fail "Gitignore regex matches only root .gitignore" "1 match" "$GITIGNORE_RESULT matches"
fi

# Test CHANGED_TESTS regex
run_test
printf 'Tests/Foo.cs\nTests/Editor/Bar.cs\nRuntime/Foo.cs\n' > "$TEST_FILE"
TESTS_RESULT=$(grep -Ec '^Tests/' "$TEST_FILE" 2>/dev/null || echo "0")
if [ "$TESTS_RESULT" = "2" ]; then
    pass "Tests regex matches only Tests/ paths ($TESTS_RESULT/2)"
else
    fail "Tests regex matches only Tests/ paths" "2 matches" "$TESTS_RESULT matches"
fi

# Cleanup test temp file
rm -f "$TEST_FILE"

# =============================================================================
# Test: Pre-push safety invariants
# =============================================================================
echo ""
echo "=== Testing pre-push safety invariants ==="

run_test
UNSAFE_XARGS_COUNT=$(grep -v '^[[:space:]]*#' "$PRE_PUSH" | grep -Ec 'echo[[:space:]]+"\$[A-Z_][A-Z0-9_]*"[[:space:]]*\|[[:space:]]*xargs' 2>/dev/null || true)
if [ "$UNSAFE_XARGS_COUNT" = "0" ]; then
    pass "No unsafe echo-to-xargs file transport remains"
else
    fail "No unsafe echo-to-xargs file transport remains" "0 occurrences" "$UNSAFE_XARGS_COUNT occurrences"
fi

run_test
if grep -Fq -- "git grep -z -H -n -i -E" "$PRE_PUSH" && \
   grep -Fq -- "-- '*.cs'" "$PRE_PUSH" && \
   grep -Fq -- 'array_contains_exact "$match_path" "${cs_paths[@]}"' "$PRE_PUSH" && \
   grep -Fq -- ':(literal)${cs_paths[$index]}' "$PRE_PUSH"; then
    pass "Region guard scans pushed commits with literal changed-path filtering"
else
    fail "Region guard scans pushed commits with literal changed-path filtering" "literal small-path grep plus large-path exact filter" "missing literal pathspec scan or changed-path filter"
fi

run_test
if ! grep -q 'run-doc-link-lint' "$PRE_PUSH" && \
   ! grep -q 'cspell lint' "$PRE_PUSH" && \
   ! grep -q 'lint-meta-files' "$PRE_PUSH"; then
    pass "Slow validation is delegated outside pre-push"
else
    fail "Slow validation is delegated outside pre-push" "no doc-link/cspell/meta commands" "found slow command in pre-push"
fi

run_test
if ! grep -q 'run_conditional_tests' "$PRE_PUSH" && \
   ! grep -q 'scripts/tests/test-lint-tests\.ps1' "$PRE_PUSH" && \
   ! grep -q 'scripts/tests/test-validate-lint-error-codes\.ps1' "$PRE_PUSH"; then
    pass "Pre-push hook does not run heavyweight regression suites"
else
    fail "Pre-push hook does not run heavyweight regression suites" "no heavyweight test-suite calls" "found regression-suite call in pre-push"
fi

run_test
HOOK_IGNORE_FAILURES=0
while IFS= read -r hook_path; do
    hook_name="${hook_path##*/}"
    case "$hook_name" in
        *.sample|*.txt|*.log|*.out|*.err|*.tmp)
            continue
            ;;
    esac
    if [[ "$hook_name" == *.* ]]; then
        hook_name="${hook_name%.*}"
    fi
    for ext in txt log out err tmp; do
        if ! git -C "$REPO_ROOT" check-ignore -q -- "$hook_name.$ext"; then
            HOOK_IGNORE_FAILURES=$((HOOK_IGNORE_FAILURES + 1))
        fi
        if ! git -C "$REPO_ROOT" check-ignore -q -- ".githooks/$hook_name.$ext"; then
            HOOK_IGNORE_FAILURES=$((HOOK_IGNORE_FAILURES + 1))
        fi
    done
done < <(git -C "$REPO_ROOT" ls-files .githooks)

if [ "$HOOK_IGNORE_FAILURES" -eq 0 ]; then
    pass "All hook artifact patterns are gitignored for auto-recovery"
else
    fail "All hook artifact patterns are gitignored for auto-recovery" "0 missing ignore patterns" "$HOOK_IGNORE_FAILURES missing pattern(s)"
fi

# Verify no \s escape sequences in grep patterns (non-POSIX)
run_test
# Use -F for fixed-string search to find literal \s (not interpreted as whitespace class)
# Skip comment lines
BACKSLASH_S_COUNT=$(grep -v '^[[:space:]]*#' "$PRE_PUSH" | grep -cF '\s' 2>/dev/null) || true
if [ "$BACKSLASH_S_COUNT" = "0" ]; then
    pass "No \\s escape sequences in non-comment lines"
else
    fail "No \\s in non-comment lines" "0 occurrences" "$BACKSLASH_S_COUNT occurrences"
fi

# Verify no GNU BRE \| alternation (should use grep -E with |)
run_test
# Use -F for fixed-string search to find literal \|
BRE_ALT_COUNT=$(grep -v '^[[:space:]]*#' "$PRE_PUSH" | grep -cF '\|' 2>/dev/null) || true
if [ "$BRE_ALT_COUNT" = "0" ]; then
    pass "No GNU BRE \\| alternation in non-comment lines"
else
    fail "No GNU BRE \\| in non-comment lines" "0 occurrences" "$BRE_ALT_COUNT occurrences"
fi

# Verify git grep uses -E for extended regex alternation patterns
run_test
if grep -Fq -- "git grep -z -H -n -i -E" "$PRE_PUSH"; then
    pass "Uses git grep -E for extended regex (POSIX ERE)"
else
    fail "Uses git grep -E" "git grep -E present" "not found"
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
