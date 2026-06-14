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
if grep -qE 'trap.*cleanup|trap.*rm' "$PRE_PUSH"; then
    pass "Hook has cleanup trap"
else
    fail "Hook has cleanup trap" "cleanup trap present" "not found"
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
[[ ${#ALL_CHANGED_FILES[@]} -eq 2 ]]
[[ "${ALL_CHANGED_FILES[0]}" == "file with spaces.cs" ]]
[[ "${ALL_CHANGED_FILES[1]}" == "-plugin=evil.js" ]]
'; then
    pass "add_changed_file preserves exact filenames and deduplicates"
else
    fail "add_changed_file preserves exact filenames and deduplicates" "two unique files preserved in insertion order" "deduplication or exact storage failed"
fi

run_test
if run_pre_push_helper_script '
ALL_CHANGED_FILES=()
input_file=$(mktemp)
printf "file with spaces.cs\0-plugin=evil.js\0file with spaces.cs\0" > "$input_file"
collect_changed_files < "$input_file"
rm -f "$input_file"
[[ ${#ALL_CHANGED_FILES[@]} -eq 2 ]]
[[ "${ALL_CHANGED_FILES[0]}" == "file with spaces.cs" ]]
[[ "${ALL_CHANGED_FILES[1]}" == "-plugin=evil.js" ]]
'; then
    pass "collect_changed_files parses NUL-delimited input safely"
else
    fail "collect_changed_files parses NUL-delimited input safely" "two unique files parsed from NUL-delimited stream" "parser failed for deduplication or special filenames"
fi

run_test
if run_pre_push_helper_script '
DOC_LINK_FULL_SCAN_REQUIRED=0
input_file=$(mktemp)
printf "D\0docs/deleted.md\0R100\0docs/old-name.md\0docs/new-name.md\0D\0Runtime/Foo.cs\0" > "$input_file"
collect_doc_link_full_scan_triggers < "$input_file"
rm -f "$input_file"
[[ "$DOC_LINK_FULL_SCAN_REQUIRED" -eq 1 ]]
'; then
    pass "Deleted or renamed markdown targets force full doc-link lint"
else
    fail "Deleted or renamed markdown targets force full doc-link lint" "DOC_LINK_FULL_SCAN_REQUIRED=1" "trigger was not set"
fi

run_test
if run_pre_push_helper_script '
DOC_LINK_FULL_SCAN_REQUIRED=0
input_file=$(mktemp)
printf "D\0Runtime/Foo.cs\0R100\0scripts/old.ps1\0scripts/new.ps1\0" > "$input_file"
collect_doc_link_full_scan_triggers < "$input_file"
rm -f "$input_file"
[[ "$DOC_LINK_FULL_SCAN_REQUIRED" -eq 0 ]]
'; then
    pass "Non-markdown deletes do not force full doc-link lint"
else
    fail "Non-markdown deletes do not force full doc-link lint" "DOC_LINK_FULL_SCAN_REQUIRED=0" "trigger was set"
fi

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
# Test: Parallel execution
# =============================================================================
echo ""
echo "=== Testing parallel execution ==="

run_test
if grep -q 'run_node_checks &' "$PRE_PUSH"; then
    pass "Node checks run in background"
else
    fail "Node checks run in background" "background execution" "not found"
fi

run_test
if grep -q 'run_pwsh_checks &' "$PRE_PUSH"; then
    pass "PowerShell checks run in background"
else
    fail "PowerShell checks run in background" "background execution" "not found"
fi

run_test
if grep -q 'run_bash_checks &' "$PRE_PUSH"; then
    pass "Bash checks run in background"
else
    fail "Bash checks run in background" "background execution" "not found"
fi

run_test
if grep -q 'wait.*PID' "$PRE_PUSH"; then
    pass "Hook waits for background PIDs"
else
    fail "Hook waits for background PIDs" "wait for PIDs" "not found"
fi

run_test
if grep -q 'HOOK_FAILED' "$PRE_PUSH"; then
    pass "Hook tracks failure status across parallel groups"
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

# Test CHANGED_WIKI regex
run_test
printf 'scripts/wiki/build.py\nscripts/tests/test-wiki-generation.sh\n.github/workflows/deploy-wiki.yml\nscripts/wiki-helper.py\n' > "$TEST_FILE"
WIKI_RESULT=$(grep -Ec '^(scripts/wiki/.*|scripts/tests/test-wiki-generation\.sh|\.github/workflows/deploy-wiki\.yml)$' "$TEST_FILE" 2>/dev/null || echo "0")
if [ "$WIKI_RESULT" = "3" ]; then
    pass "Wiki regex matches only wiki-related paths ($WIKI_RESULT/3)"
else
    fail "Wiki regex matches only wiki-related paths" "3 matches" "$WIKI_RESULT matches"
fi

# Test CHANGED_HOOK_FILES regex
run_test
printf '.githooks/pre-push\n.githooks/sub/hook.sh\nscripts/tests/test-hook-patterns.sh\nscripts/tests/test-hook-patterns.sh.bak\n' > "$TEST_FILE"
HOOK_FILES_RESULT=$(grep -Ec '^(\.githooks/.*|scripts/tests/test-hook-patterns\.sh)$' "$TEST_FILE" 2>/dev/null || echo "0")
if [ "$HOOK_FILES_RESULT" = "3" ]; then
    pass "Hook-file regex matches hook paths and exact test script ($HOOK_FILES_RESULT/3)"
else
    fail "Hook-file regex matches hook paths and exact test script" "3 matches" "$HOOK_FILES_RESULT matches"
fi

# Test CHANGED_LINT_TEST regex
run_test
printf 'scripts/lint-tests.ps1\nscripts/tests/test-lint-tests.ps1\nscripts/lint-tests.ps1-backup\n' > "$TEST_FILE"
LINT_TEST_RESULT=$(grep -Ec '^(scripts/lint-tests\.ps1|scripts/tests/test-lint-tests\.ps1)$' "$TEST_FILE" 2>/dev/null || echo "0")
if [ "$LINT_TEST_RESULT" = "2" ]; then
    pass "Lint-test regex matches only the exact lint test files ($LINT_TEST_RESULT/2)"
else
    fail "Lint-test regex matches only the exact lint test files" "2 matches" "$LINT_TEST_RESULT matches"
fi

# Test CHANGED_GITIGNORE_DOCS_LINT regex
run_test
printf 'scripts/lint-gitignore-docs.ps1\nscripts/tests/test-gitignore-docs.ps1\nscripts/tests/test-gitignore-docs.ps1.disabled\n' > "$TEST_FILE"
GITIGNORE_DOCS_RESULT=$(grep -Ec '^(scripts/lint-gitignore-docs\.ps1|scripts/tests/test-gitignore-docs\.ps1)$' "$TEST_FILE" 2>/dev/null || echo "0")
if [ "$GITIGNORE_DOCS_RESULT" = "2" ]; then
    pass "Gitignore-docs regex matches only the exact lint files ($GITIGNORE_DOCS_RESULT/2)"
else
    fail "Gitignore-docs regex matches only the exact lint files" "2 matches" "$GITIGNORE_DOCS_RESULT matches"
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
if grep -Fq -- '"${CHANGED_PRETTIER[@]}"' "$PRE_PUSH" && \
   grep -Fq -- '"${CHANGED_MD[@]}"' "$PRE_PUSH" && \
   grep -Fq -- '"${CHANGED_CS[@]}"' "$PRE_PUSH"; then
    pass "Tool invocations consume quoted arrays"
else
    fail "Tool invocations consume quoted arrays" "quoted array expansions present" "one or more array expansions missing"
fi

run_test
if grep -q 'CHANGED_DOC_LINK=()' "$PRE_PUSH" && \
   grep -q 'CHANGED_DOC_LINK+=("$file")' "$PRE_PUSH" && \
   grep -q 'node ./scripts/run-doc-link-lint.js -Paths "${CHANGED_DOC_LINK\[@\]}"' "$PRE_PUSH" && \
   grep -q '\*.cs|.*\*.ps1|.*\*.json|.*\*.yaml' "$PRE_PUSH"; then
    pass "Doc-link lint runs for changed source and config files"
else
    fail "Doc-link lint runs for changed source and config files" "CHANGED_DOC_LINK classification and invocation present" "missing source/config doc-link guard"
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

# Verify grep -E used (not plain grep) for alternation patterns
run_test
if grep -q 'grep -E' "$PRE_PUSH"; then
    pass "Uses grep -E for extended regex (POSIX ERE)"
else
    fail "Uses grep -E" "grep -E present" "not found"
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
