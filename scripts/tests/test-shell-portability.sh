#!/usr/bin/env bash
# cspell:ignore EPF
# =============================================================================
# Test Script: Shell Script Portability & Hygiene
# =============================================================================
# Validates that all shell scripts and git hooks in the repository follow
# POSIX-portable patterns and avoid common hygiene issues:
#
#   A) Non-portable grep patterns (\| without -E, \s without -E/-P)
#   B) Hardcoded user paths without env var override in Unity scripts
#   C) Inappropriate stderr suppression hiding lint/validation output
#   D) PowerShell child process invocations missing $LASTEXITCODE checks
#   E) Unsafe filename transport and fragile git path parsing in shell hooks
#
# Run: bash scripts/tests/test-shell-portability.sh
# Exit codes: 0 = all tests pass, 1 = test failure
# =============================================================================

set -euo pipefail

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Test counters
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
        echo -e "  ${RED}Detail:${NC} $2"
    fi
}

run_test() {
    tests_run=$((tests_run + 1))
}

# Get repo root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Collect all shell scripts and git hooks to scan
SHELL_FILES=()
while IFS= read -r -d '' f; do
    # Skip this test script itself to avoid self-referential false positives
    [[ "$f" == *test-shell-portability.sh ]] && continue
    SHELL_FILES+=("$f")
done < <(find "$REPO_ROOT/scripts" -name '*.sh' -print0 2>/dev/null)
while IFS= read -r -d '' f; do
    first_line="$(head -n 1 "$f" 2>/dev/null || true)"
    case "$first_line" in
        *pwsh*) continue ;;
    esac
    case "$f" in
        *.ps1) continue ;;
    esac
    SHELL_FILES+=("$f")
done < <(find "$REPO_ROOT/.githooks" -type f -print0 2>/dev/null)

# Collect PowerShell scripts
PS1_FILES=()
while IFS= read -r -d '' f; do
    PS1_FILES+=("$f")
done < <(find "$REPO_ROOT/scripts" -name '*.ps1' -print0 2>/dev/null)

# =============================================================================
# Section A: Non-portable grep patterns
# =============================================================================
echo ""
echo "=== Section A: Non-portable grep patterns ==="

# A1: grep with \| (BRE alternation) without -E flag
# GNU grep supports \| in BRE mode, but this is a GNU extension not in POSIX.
# The fix is to use -E (ERE mode) so | works portably, or use -F for literals.
echo ""
echo "--- A1: grep with BRE \\| alternation (requires -E for portability) ---"

a1_violations=""
for file in "${SHELL_FILES[@]}"; do
    rel_path="${file#"$REPO_ROOT"/}"
    line_num=0
    while IFS= read -r line; do
        line_num=$((line_num + 1))

        # Skip comment lines
        stripped="${line#"${line%%[![:space:]]*}"}"
        [[ "$stripped" == \#* ]] && continue

        # Skip lines that don't contain grep
        case "$line" in
            *grep*) ;;
            *) continue ;;
        esac

        # Skip lines that already have -E, -P, or -F flags (portable or literal)
        # Match grep invocations with flags that include E, P, or F
        if echo "$line" | grep -qE 'grep[[:space:]]+-[a-zA-Z]*[EPF]'; then
            continue
        fi

        # Check if the pattern argument contains \|
        if echo "$line" | grep -qF '\|'; then
            # Allowlist: grep -cF '\|' is literal pipe counting (not alternation)
            if echo "$line" | grep -qE 'grep[[:space:]]+-[a-zA-Z]*F'; then
                continue
            fi
            a1_violations="${a1_violations}  ${rel_path}:${line_num}: ${line}"$'\n'
        fi
    done < "$file"
done

run_test
if [[ -z "$a1_violations" ]]; then
    pass "No grep with BRE \\| alternation found (all use -E or -F)"
else
    fail "Found grep with BRE \\| (non-portable, needs -E flag):" "$a1_violations"
fi

# A2: grep with \s (non-POSIX shorthand, should use [[:space:]])
echo ""
echo "--- A2: grep with \\s shorthand (non-POSIX, use [[:space:]]) ---"

a2_violations=""
for file in "${SHELL_FILES[@]}"; do
    rel_path="${file#"$REPO_ROOT"/}"
    line_num=0
    while IFS= read -r line; do
        line_num=$((line_num + 1))

        # Skip comment lines
        stripped="${line#"${line%%[![:space:]]*}"}"
        [[ "$stripped" == \#* ]] && continue

        # Skip lines that don't contain grep
        case "$line" in
            *grep*) ;;
            *) continue ;;
        esac

        # Skip lines using -P (PCRE, where \s is valid) or -F (fixed string, literal match)
        if echo "$line" | grep -qE 'grep[[:space:]]+-[a-zA-Z]*[PF]'; then
            continue
        fi

        # Check for \s in the grep pattern (not in [[:space:]] form)
        # We look for \s that isn't part of a word like "patterns" or variable like "$s"
        if echo "$line" | grep -qE '\\s[*+?)]|\\s[^a-zA-Z]|\\s$'; then
            a2_violations="${a2_violations}  ${rel_path}:${line_num}: ${line}"$'\n'
        fi
    done < "$file"
done

run_test
if [[ -z "$a2_violations" ]]; then
    pass "No grep with \\s shorthand found (all use [[:space:]])"
else
    fail "Found grep with \\s (non-POSIX, use [[:space:]]):" "$a2_violations"
fi

# =============================================================================
# Section B: Hardcoded paths without env var override
# =============================================================================
echo ""
echo "=== Section B: Hardcoded paths in Unity scripts ==="

# B1: /home/vscode/ paths that aren't inside ${VAR:-...} defaults or comments
echo ""
echo "--- B1: Hardcoded /home/vscode/ paths (should use env var override) ---"

b1_violations=""
while IFS= read -r -d '' file; do
    rel_path="${file#"$REPO_ROOT"/}"
    line_num=0
    while IFS= read -r line; do
        line_num=$((line_num + 1))

        # Skip comment lines (lines whose first non-whitespace is #)
        stripped="${line#"${line%%[![:space:]]*}"}"
        [[ "$stripped" == \#* ]] && continue

        # Skip if line doesn't contain /home/vscode/
        case "$line" in
            */home/vscode/*) ;;
            *) continue ;;
        esac

        # Allow: ${VAR:-/home/vscode/...} pattern (env var with default)
        if echo "$line" | grep -qE '\$\{[A-Z_]+:-/home/vscode/'; then
            continue
        fi

        # Allow: echo/printf statements (display-only, not assignment)
        if echo "$line" | grep -qE '^[[:space:]]*(echo|printf)[[:space:]]'; then
            continue
        fi

        b1_violations="${b1_violations}  ${rel_path}:${line_num}: ${line}"$'\n'
    done < "$file"
done < <(find "$REPO_ROOT/scripts/unity" -name '*.sh' -print0 2>/dev/null)

run_test
if [[ -z "$b1_violations" ]]; then
    pass "No hardcoded /home/vscode/ paths found without env var override"
else
    fail "Found hardcoded paths (should use \${VAR:-default} pattern):" "$b1_violations"
fi

# B2: Unity test runner must not place generated test results inside the package root
echo ""
echo "--- B2: Unity test results stay outside imported package root ---"

run_test
unity_run_tests="$REPO_ROOT/scripts/unity/run-tests.sh"
if grep -qE 'ln[[:space:]]+-s[f]?[[:space:]]+\$?\{?RESULTS_DIR' "$unity_run_tests"; then
    fail "Unity test runner creates a workspace test-results symlink" \
        "Generated result files under the package root are imported by Unity and can trigger infinite import-loop errors."
elif grep -qE 'ln[[:space:]]+-s[f]?[[:space:]]+.*WORKSPACE_RESULTS' "$unity_run_tests"; then
    fail "Unity test runner creates a workspace-root symlink" \
        "Generated result files under the package root are imported by Unity and can trigger infinite import-loop errors."
else
    pass "Unity test runner does not create generated result symlinks in the package root"
fi

run_test
guard_line=$(grep -n 'Refusing to write Unity test results inside the package root' "$unity_run_tests" | head -n 1 | cut -d: -f1)
create_line=$(grep -n 'create-test-project\.sh' "$unity_run_tests" | head -n 1 | cut -d: -f1)
mkdir_line=$(grep -n 'mkdir -p "\${RESULTS_DIR}"' "$unity_run_tests" | head -n 1 | cut -d: -f1)
if [[ -z "$guard_line" || -z "$create_line" || -z "$mkdir_line" ]]; then
    fail "Unity test runner package-root guard is missing expected structure" \
        "guard_line='${guard_line}', create_line='${create_line}', mkdir_line='${mkdir_line}'"
elif (( guard_line < create_line && guard_line < mkdir_line )); then
    pass "Unity test runner validates results path before creating projects or result directories"
else
    fail "Unity test runner validates results path too late" \
        "Guard line ${guard_line}, create-test-project line ${create_line}, mkdir line ${mkdir_line}"
fi

echo ""
echo "--- B3: Unity package export project stays below artifacts root ---"

run_test
unity_export_package="$REPO_ROOT/scripts/unity/export-unitypackage.sh"
root_guard_line=$(grep -nF '"${PROJECT_DIR}" == "${ARTIFACTS_ROOT}"' "$unity_export_package" | head -n 1 | cut -d: -f1)
outside_guard_line=$(grep -nF '"${PROJECT_DIR}" != "${ARTIFACTS_ROOT}/"*' "$unity_export_package" | head -n 1 | cut -d: -f1)
delete_line=$(grep -nF 'rm -rf "${PROJECT_DIR}"' "$unity_export_package" | head -n 1 | cut -d: -f1)
if [[ -z "$root_guard_line" || -z "$outside_guard_line" || -z "$delete_line" ]]; then
    fail "Unity package export project guard is missing expected structure" \
        "root_guard_line='${root_guard_line}', outside_guard_line='${outside_guard_line}', delete_line='${delete_line}'"
elif (( root_guard_line < delete_line && outside_guard_line < delete_line )); then
    pass "Unity package export refuses artifacts root before deleting the project directory"
else
    fail "Unity package export validates project path too late" \
        "Root guard line ${root_guard_line}, outside guard line ${outside_guard_line}, delete line ${delete_line}"
fi

echo ""
echo "--- B4: Unity package export supports bare output filenames ---"

run_test
dirname_line=$(grep -nF 'string outputDirectory = Path.GetDirectoryName(outputPath);' "$unity_export_package" | head -n 1 | cut -d: -f1)
fallback_line=$(grep -nF 'outputDirectory = Directory.GetCurrentDirectory();' "$unity_export_package" | head -n 1 | cut -d: -f1)
create_line=$(grep -nF 'Directory.CreateDirectory(outputDirectory);' "$unity_export_package" | head -n 1 | cut -d: -f1)
if [[ -z "$dirname_line" || -z "$fallback_line" || -z "$create_line" ]]; then
    fail "Unity package export output-directory fallback is missing expected structure" \
        "dirname_line='${dirname_line}', fallback_line='${fallback_line}', create_line='${create_line}'"
elif (( dirname_line < fallback_line && fallback_line < create_line )); then
    pass "Unity package export falls back to the current directory for bare output filenames"
else
    fail "Unity package export applies output-directory fallback too late" \
        "dirname line ${dirname_line}, fallback line ${fallback_line}, create line ${create_line}"
fi

echo ""
echo "--- B5: Unity package export stages package content roots ---"

run_test
stage_project="$REPO_ROOT/.artifacts/unity/shell-portability-unitypackage-stage"
stage_log="$(mktemp)"
rm -rf "$stage_project"
if bash "$unity_export_package" --stage-only --project-dir "$stage_project" >"$stage_log" 2>&1; then
    staged_root="$stage_project/Assets/WallstopStudios/UnityHelpers"
    required_stage_entries=(
        "Runtime"
        "Runtime.meta"
        "Editor"
        "Editor.meta"
        "Samples"
        "Shaders"
        "Shaders.meta"
        "Styles"
        "Styles.meta"
        "URP"
        "URP.meta"
        "link.xml"
        "link.xml.meta"
    )
    missing_stage_entries=()
    for entry in "${required_stage_entries[@]}"; do
        if [[ ! -e "$staged_root/$entry" ]]; then
            missing_stage_entries+=("$entry")
        fi
    done

    if (( ${#missing_stage_entries[@]} == 0 )); then
        pass "Unity package export stage contains all shipped package roots"
    else
        fail "Unity package export stage is missing package roots" \
            "Missing entries: ${missing_stage_entries[*]}"
    fi
else
    stage_tail="$(tail -n 40 "$stage_log" 2>/dev/null || true)"
    fail "Unity package export stage-only command failed" "$stage_tail"
fi
rm -rf "$stage_project"
rm -f "$stage_log"

echo ""
echo "--- B6: Unity package export rejects incomplete package metadata ---"

run_test
metadata_fixture="$(mktemp -d)"
metadata_log="$(mktemp)"
mkdir -p "$metadata_fixture/scripts/unity" "$metadata_fixture/.github"
cp "$unity_export_package" "$metadata_fixture/scripts/unity/export-unitypackage.sh"
printf '{ "release": "2022.3.45f1" }\n' > "$metadata_fixture/.github/unity-versions.json"
printf '{ "name": "fixture-package" }\n' > "$metadata_fixture/package.json"
if bash "$metadata_fixture/scripts/unity/export-unitypackage.sh" \
    --stage-only \
    --project-dir "$metadata_fixture/.artifacts/unity/unitypackage-stage" \
    >"$metadata_log" 2>&1; then
    fail "Unity package export fails fast when package metadata is incomplete" \
        "Expected export-unitypackage.sh to reject package.json without version."
else
    if grep -Fq 'must define non-empty string name and version fields' "$metadata_log"; then
        pass "Unity package export fails fast when package metadata is incomplete"
    else
        metadata_tail="$(tail -n 40 "$metadata_log" 2>/dev/null || true)"
        fail "Unity package export reports incomplete package metadata clearly" "$metadata_tail"
    fi
fi
rm -rf "$metadata_fixture"
rm -f "$metadata_log"

# =============================================================================
# Section C: Inappropriate stderr suppression in git hooks
# =============================================================================
echo ""
echo "=== Section C: Stderr suppression in git hooks ==="

# C1: 2>/dev/null on lint/validation commands in hooks
# Allowed: command -v, kill, grep ... || true, docker inspect, git merge-base,
#          tool version checks (--version), mktemp
echo ""
echo "--- C1: 2>/dev/null masking lint tool output ---"

c1_violations=""
for hookfile in "$REPO_ROOT"/.githooks/*; do
    [[ -f "$hookfile" ]] || continue
    rel_path="${hookfile#"$REPO_ROOT"/}"
    line_num=0
    while IFS= read -r line; do
        line_num=$((line_num + 1))

        # Skip comment lines
        stripped="${line#"${line%%[![:space:]]*}"}"
        [[ "$stripped" == \#* ]] && continue

        # Only check lines with 2>/dev/null
        case "$line" in
            *2\>/dev/null*) ;;
            *) continue ;;
        esac

        # Allowlist of safe 2>/dev/null usage
        skip=false

        # Tool detection: command -v, which
        echo "$line" | grep -qE 'command -v|which ' && skip=true

        # Process cleanup: kill
        echo "$line" | grep -qE '\bkill\b' && skip=true

        # Version checks: --version
        echo "$line" | grep -qF -- '--version' && skip=true

        # Docker inspect (checking if image exists)
        echo "$line" | grep -qE 'docker\b.*inspect' && skip=true
        echo "$line" | grep -qE 'docker\b.*info' && skip=true

        # Git operations that legitimately fail (merge-base on orphan, etc.)
        echo "$line" | grep -qE 'git (merge-base|rev-parse|diff|log|ls-tree)' && skip=true

        # Grep (exit code 1 on no match is expected)
        echo "$line" | grep -qE '\bgrep\b' && skip=true

        # Temp file creation
        echo "$line" | grep -qF 'mktemp' && skip=true

        # Tool restoration
        echo "$line" | grep -qE 'dotnet tool restore' && skip=true

        # Binary format checks (encoding detection)
        echo "$line" | grep -qF '$'"'"'\r' && skip=true

        if [[ "$skip" == false ]]; then
            c1_violations="${c1_violations}  ${rel_path}:${line_num}: ${line}"$'\n'
        fi
    done < "$hookfile"
done

run_test
if [[ -z "$c1_violations" ]]; then
    pass "No inappropriate stderr suppression in git hooks"
else
    fail "Found 2>/dev/null on lint/validation commands (warnings hidden):" "$c1_violations"
fi

# =============================================================================
# Section D: PowerShell child process exit code safety
# =============================================================================
echo ""
echo '=== Section D: PowerShell $LASTEXITCODE after child process calls ==='

# D1: & pwsh invocations without $LASTEXITCODE check nearby
echo ""
echo '--- D1: Missing $LASTEXITCODE check after & pwsh calls ---'

d1_violations=""
for file in "${PS1_FILES[@]}"; do
    rel_path="${file#"$REPO_ROOT"/}"

    # Find lines with "& pwsh" invocations
    line_num=0
    total_lines=$(wc -l < "$file")
    while IFS= read -r line; do
        line_num=$((line_num + 1))

        # Skip comment lines
        stripped="${line#"${line%%[![:space:]]*}"}"
        [[ "$stripped" == \#* ]] && continue

        # Check for & pwsh invocation
        case "$line" in
            *'& pwsh'*) ;;
            *) continue ;;
        esac

        # Look ahead up to 8 lines for $LASTEXITCODE check
        found_check=false
        end_line=$((line_num + 8))
        if [[ $end_line -gt $total_lines ]]; then
            end_line=$total_lines
        fi

        lookahead=$(sed -n "$((line_num + 1)),${end_line}p" "$file")
        if echo "$lookahead" | grep -qF 'LASTEXITCODE'; then
            found_check=true
        fi

        # Also check if the script immediately exits with $LASTEXITCODE
        # (pattern: "& pwsh ... ; exit $LASTEXITCODE" on same line or "exit $LASTEXITCODE" as next line)
        if echo "$line" | grep -qF 'LASTEXITCODE'; then
            found_check=true
        fi

        if [[ "$found_check" == false ]]; then
            d1_violations="${d1_violations}  ${rel_path}:${line_num}: ${line}"$'\n'
        fi
    done < "$file"
done

run_test
if [[ -z "$d1_violations" ]]; then
    pass "All & pwsh invocations have \$LASTEXITCODE checks"
else
    fail "Found & pwsh calls without \$LASTEXITCODE check within 8 lines:" "$d1_violations"
fi

# =============================================================================
# Section E: Filename transport and path parsing safety
# =============================================================================
echo ""
echo '=== Section E: Filename transport and path parsing safety ==='

# E1: echo "$VAR" | xargs is unsafe for file lists because xargs re-splits on
# spaces and other delimiters.
echo ""
echo '--- E1: Unsafe echo-to-xargs file transport ---'

e1_violations=""
for file in "${SHELL_FILES[@]}"; do
    rel_path="${file#"$REPO_ROOT"/}"
    line_num=0
    while IFS= read -r line; do
        line_num=$((line_num + 1))

        stripped="${line#"${line%%[![:space:]]*}"}"
        [[ "$stripped" == \#* ]] && continue

        if echo "$line" | grep -qE 'echo[[:space:]]+"\$[A-Z_][A-Z0-9_]*"[[:space:]]*\|[[:space:]]*xargs'; then
            e1_violations="${e1_violations}  ${rel_path}:${line_num}: ${line}"$'\n'
        fi
    done < "$file"
done

run_test
if [[ -z "$e1_violations" ]]; then
    pass "No unsafe echo-to-xargs file transport patterns found"
else
    fail "Found unsafe echo-to-xargs file transport patterns:" "$e1_violations"
fi

# E2: Exact grep matches on variable file names must include -- so leading-dash
# file names cannot be interpreted as options.
echo ""
echo '--- E2: grep exact-match variable arguments missing -- ---'

e2_violations=""
for file in "${SHELL_FILES[@]}"; do
    rel_path="${file#"$REPO_ROOT"/}"
    line_num=0
    while IFS= read -r line; do
        line_num=$((line_num + 1))

        stripped="${line#"${line%%[![:space:]]*}"}"
        [[ "$stripped" == \#* ]] && continue

        if echo "$line" | grep -qE 'grep[[:space:]]+-[a-zA-Z]*q[a-zA-Z]*F[[:space:]]+"\$[^"]+"'; then
            if ! echo "$line" | grep -qE 'grep[[:space:]]+-[a-zA-Z]*q[a-zA-Z]*F[[:space:]]+--[[:space:]]+"\$[^"]+"'; then
                e2_violations="${e2_violations}  ${rel_path}:${line_num}: ${line}"$'\n'
            fi
        fi
    done < "$file"
done

run_test
if [[ -z "$e2_violations" ]]; then
    pass "All grep exact-match variable arguments use --"
else
    fail "Found grep exact-match variable arguments missing --:" "$e2_violations"
fi

# E3: Fixed-field awk parsing is fragile for git paths with spaces.
echo ""
echo '--- E3: Fragile awk field parsing for git paths ---'

e3_violations=""
for file in "${SHELL_FILES[@]}"; do
    rel_path="${file#"$REPO_ROOT"/}"
    line_num=0
    while IFS= read -r line; do
        line_num=$((line_num + 1))

        stripped="${line#"${line%%[![:space:]]*}"}"
        [[ "$stripped" == \#* ]] && continue

        if echo "$line" | grep -qE "awk.*print[[:space:]]+\\\$4"; then
            e3_violations="${e3_violations}  ${rel_path}:${line_num}: ${line}"$'\n'
        fi
    done < "$file"
done

run_test
if [[ -z "$e3_violations" ]]; then
    pass "No fragile awk field parsing for git paths found"
else
    fail "Found fragile awk field parsing for git paths:" "$e3_violations"
fi

# =============================================================================
# Section F: Tracked executable modes for shell entrypoints
# =============================================================================
echo ""
echo '=== Section F: Shell executable mode metadata ==='

echo ""
echo '--- F1: Shell scripts with shebangs are tracked executable ---'

f1_violations=""
# Repository convention: a tracked shell file with a shebang is directly
# runnable, even when it is also safe to source from another script.
while IFS= read -r -d '' tracked_path; do
    case "$tracked_path" in
        *.sh|.githooks/*) ;;
        *) continue ;;
    esac
    case "$tracked_path" in
        .githooks/*.*) continue ;;
    esac

    absolute_path="$REPO_ROOT/$tracked_path"
    [[ -f "$absolute_path" ]] || continue

    first_line="$(head -n 1 "$absolute_path" 2>/dev/null || true)"
    case "$first_line" in
        '#!'*) ;;
        *) continue ;;
    esac

    index_entry="$(git -C "$REPO_ROOT" ls-files -s -- "$tracked_path" 2>/dev/null || true)"
    index_mode="${index_entry%% *}"
    filesystem_mode="$(stat -c '%A %a' "$absolute_path" 2>/dev/null || ls -l "$absolute_path" 2>/dev/null || echo 'unavailable')"

    if [[ "$index_mode" != "100755" || ! -x "$absolute_path" ]]; then
        f1_violations="${f1_violations}  ${tracked_path}: filesystem=${filesystem_mode}; git-index=${index_entry:-untracked}"$'\n'
    fi
done < <(git -C "$REPO_ROOT" ls-files -z -- .devcontainer .githooks scripts)

run_test
if [[ -z "$f1_violations" ]]; then
    pass "All tracked shell entrypoints are executable in filesystem and git index"
else
    fail "Found shell entrypoints without executable git metadata:" "$f1_violations"
fi

# =============================================================================
# Section G: Unity .meta generator output hygiene
# =============================================================================
echo ""
echo '=== Section G: Unity .meta generator output hygiene ==='

echo ""
echo '--- G1: generate-meta.sh emits no trailing whitespace ---'

run_test
g1_tempdir="$(mktemp -d)"
g1_output=""
if mkdir -p "$g1_tempdir/Tests/Editor/Validation" "$g1_tempdir/Tests/Editor/Textures" &&
    printf 'namespace WallstopStudios.UnityHelpers.Tests { public sealed class MetaFixture { } }\n' > "$g1_tempdir/Tests/Editor/Validation/MetaFixture.cs" &&
    : > "$g1_tempdir/Tests/Editor/Textures/TextureFixture.png" &&
    "$REPO_ROOT/scripts/generate-meta.sh" "$g1_tempdir/Tests/Editor/Validation/MetaFixture.cs" >/dev/null 2>&1 &&
    "$REPO_ROOT/scripts/generate-meta.sh" "$g1_tempdir/Tests/Editor/Textures/TextureFixture.png" >/dev/null 2>&1; then
    g1_output="$(grep -nE '[[:blank:]]+$' "$g1_tempdir/Tests/Editor/Validation/MetaFixture.cs.meta" "$g1_tempdir/Tests/Editor/Textures/TextureFixture.png.meta" 2>/dev/null || true)"
else
    g1_output="generate-meta.sh failed while creating representative .meta fixtures"
fi
rm -rf "$g1_tempdir"

if [[ -z "$g1_output" ]]; then
    pass "generate-meta.sh emits no trailing whitespace in representative .meta files"
else
    fail "generate-meta.sh emitted trailing whitespace:" "$g1_output"
fi

# =============================================================================
# Summary
# =============================================================================
echo ""
echo "==========================================="
echo "Shell Portability Test Results"
echo "==========================================="
echo -e "Tests run:    ${tests_run}"
echo -e "Tests passed: ${GREEN}${tests_passed}${NC}"
if [[ $tests_failed -gt 0 ]]; then
    echo -e "Tests failed: ${RED}${tests_failed}${NC}"
    echo ""
    exit 1
else
    echo -e "Tests failed: ${tests_failed}"
    echo ""
    echo "All portability checks passed!"
    exit 0
fi
