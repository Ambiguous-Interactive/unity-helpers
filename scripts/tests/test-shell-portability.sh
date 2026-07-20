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
# Section H: Unity Docker watchdog and container cleanup
# =============================================================================
echo ""
echo '=== Section H: Unity Docker watchdog and container cleanup ==='

echo ""
echo '--- H1-H3: process-group kill, PID 1 return, and uncertain cleanup ---'

run_test
h1_tempdir="$(mktemp -d)"
h1_bin="$h1_tempdir/bin"
h1_project="$h1_tempdir/project"
h1_container_root="$h1_tempdir/container-root"
h1_docker_log="$h1_tempdir/docker.log"
h1_unity_log="$h1_tempdir/unity.log"
h1_output="$h1_tempdir/wrapper.log"
h1_pid_file="$h1_tempdir/unity.pid"
h1_descendant_pid_file="$h1_tempdir/unity-descendant.pid"
h1_container_pid_file="$h1_tempdir/container.pid"
h1_container_name_file="$h1_tempdir/container.name"
h1_docker_run_pid_file="$h1_tempdir/docker-run.pid"
h1_docker_daemon_pid_file="$h1_tempdir/docker-daemon.pid"
h1_registration_release_file="$h1_tempdir/release-registration"
h1_return_ready_file="$h1_tempdir/return-ready"
h1_return_count_file="$h1_tempdir/return-count"
h1_container_stopping_file="$h1_tempdir/container-stopping"
h1_events="$h1_tempdir/events.log"
mkdir -p "$h1_bin" "$h1_project" "$h1_container_root"

cat > "$h1_bin/docker" << 'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "${FAKE_DOCKER_LOG}"
printf 'docker %s\n' "$*" >> "${FAKE_EVENT_LOG}"

case "${1:-}" in
    inspect)
        if [[ "${FAKE_DOCKER_INSPECT_FAIL:-0}" == "1" ]]; then
            printf 'simulated inspect failure\n' >&2
            exit 9
        fi
        if [[ ! -s "${FAKE_CONTAINER_NAME_FILE}" ]]; then
            printf 'No such object\n' >&2
            exit 1
        fi
        container_pid="$(cat "${FAKE_CONTAINER_PID_FILE}" 2>/dev/null || true)"
        if [[ -n "${container_pid}" ]] && kill -0 "${container_pid}" 2>/dev/null; then
            printf 'true\n'
        else
            printf 'false\n'
        fi
        ;;
    stop)
        stop_timeout=10
        if [[ "${2:-}" == "--timeout" && "${3:-}" =~ ^[0-9]+$ ]]; then
            stop_timeout="${3}"
        fi
        stop_deadline=$((SECONDS + stop_timeout))
        printf 'fake stop begin seconds=%s deadline=%s epoch=%s\n' "${SECONDS}" "${stop_deadline}" "$(date +%s)" >> "${FAKE_EVENT_LOG}"
        container_pid="$(cat "${FAKE_CONTAINER_PID_FILE}" 2>/dev/null || true)"
        while [[ "${SECONDS}" -lt "${stop_deadline}" ]]; do
            [[ -n "${container_pid}" ]] && kill -0 "${container_pid}" 2>/dev/null && break
            sleep 0.1
            container_pid="$(cat "${FAKE_CONTAINER_PID_FILE}" 2>/dev/null || true)"
        done
        if [[ -n "${container_pid}" ]] && kill -0 "${container_pid}" 2>/dev/null && \
            [[ ! -f "${FAKE_CONTAINER_STOPPING_FILE}" ]]; then
            kill -TERM "${container_pid}"
            while [[ "${SECONDS}" -lt "${stop_deadline}" ]]; do
                kill -0 "${container_pid}" 2>/dev/null || break
                sleep 1
            done
        fi
        printf 'fake stop end seconds=%s deadline=%s epoch=%s\n' "${SECONDS}" "${stop_deadline}" "$(date +%s)" >> "${FAKE_EVENT_LOG}"
        ;;
    rm)
        container_pid="$(cat "${FAKE_CONTAINER_PID_FILE}" 2>/dev/null || true)"
        if [[ -n "${container_pid}" ]] && kill -0 "${container_pid}" 2>/dev/null; then
            kill -KILL "${container_pid}" 2>/dev/null || true
        fi
        ;;
    run)
        printf '%s\n' "$$" > "${FAKE_DOCKER_RUN_PID_FILE}"
        arguments=("$@")
        container_name=""
        container_stop_timeout=10
        for ((index = 0; index < ${#arguments[@]}; index++)); do
            if [[ "${arguments[index]}" == "--name" ]]; then
                container_name="${arguments[index + 1]}"
            elif [[ "${arguments[index]}" == "--stop-timeout" ]]; then
                container_stop_timeout="${arguments[index + 1]}"
            fi
        done
        inner_script="${arguments[${#arguments[@]} - 1]}"
        inner_script="${inner_script//\/root/${FAKE_CONTAINER_ROOT}}"
        inner_script="${inner_script//\/project/${FAKE_PROJECT_DIR}}"
        inner_script="${inner_script//\/workspace/${FAKE_WORKSPACE_DIR}}"
        launch_fake_container() {
            printf '%s\n' "${container_name}" > "${FAKE_CONTAINER_NAME_FILE}"
            printf 'docker registration complete\n' >> "${FAKE_EVENT_LOG}"
            cd "${FAKE_PROJECT_DIR}"
            # A real Docker daemon owns the container independently of the
            # initiating CLI. Give the fake container its own process group so
            # terminating the fake client cannot deliver an extra TERM that
            # bypasses PID 1's bounded return handler.
            set -m
            PATH="${FAKE_BIN}:$PATH" bash -c "${inner_script}" &
            container_pid=$!
            set +m
            printf '%s\n' "${container_pid}" > "${FAKE_CONTAINER_PID_FILE}"
            handle_fake_client_term() {
                # Model Docker's signal proxy and record the daemon-side stop
                # grace selected by `docker run` (10 seconds when omitted).
                printf 'docker client TERM stop_timeout=%s\n' "${container_stop_timeout}" >> "${FAKE_EVENT_LOG}"
                : > "${FAKE_CONTAINER_STOPPING_FILE}"
                kill -TERM "${container_pid}" 2>/dev/null || true
            }
            if [[ -n "${FAKE_SIGNAL_PHASE:-}" ]]; then
                trap handle_fake_client_term TERM
            fi
            wait "${container_pid}"
        }
        if [[ "${FAKE_SIGNAL_PHASE:-main}" == "registration" ]]; then
            printf 'docker registration pending\n' >> "${FAKE_EVENT_LOG}"
            trap '' TERM
            while [[ ! -f "${FAKE_REGISTRATION_RELEASE_FILE}" ]]; do
                sleep 0.1
            done
            (
                # Model a daemon independent of the canceled CLI. Without this
                # reset, the fake container inherits the client's ignored TERM.
                trap - TERM
                sleep 2
                launch_fake_container
            ) &
            printf '%s\n' "$!" > "${FAKE_DOCKER_DAEMON_PID_FILE}"
            exit 143
        fi
        launch_fake_container
        ;;
    *)
        printf 'unexpected fake docker command: %s\n' "$*" >&2
        exit 2
        ;;
esac
EOF

cat > "$h1_bin/unity-editor" << 'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "${FAKE_UNITY_LOG}"

case " $* " in
    *' -returnlicense '*)
        return_count=0
        if [[ -s "${FAKE_RETURN_COUNT_FILE}" ]]; then
            return_count="$(cat "${FAKE_RETURN_COUNT_FILE}")"
        fi
        return_count=$((return_count + 1))
        printf '%s\n' "${return_count}" > "${FAKE_RETURN_COUNT_FILE}"
        printf 'unity return begin attempt=%s\n' "${return_count}" >> "${FAKE_EVENT_LOG}"
        if [[ "${FAKE_SIGNAL_PHASE:-main}" == "return" && "${return_count}" -eq 1 ]]; then
            : > "${FAKE_RETURN_READY_FILE}"
            trap '' TERM
            while true; do
                sleep 1
            done
        fi
        printf 'Successfully returned the entitlement license\n'
        printf 'unity return complete attempt=%s\n' "${return_count}" >> "${FAKE_EVENT_LOG}"
        ;;
    *' -serial '*)
        printf 'unity activation\n' >> "${FAKE_EVENT_LOG}"
        if [[ "${FAKE_SIGNAL_PHASE:-main}" == "activation" ]]; then
            trap '' TERM
            while true; do
                sleep 1
            done
        fi
        mkdir -p "${FAKE_CONTAINER_ROOT}/.local/share/unity3d/Unity"
        printf 'fixture-license\n' > "${FAKE_CONTAINER_ROOT}/.local/share/unity3d/Unity/Unity_lic.ulf"
        ;;
    *)
        if [[ "${FAKE_SIGNAL_PHASE:-main}" == "return" ]]; then
            exit 0
        fi
        printf '%s\n' "$$" > "${FAKE_UNITY_PID_FILE}"
        trap '' TERM
        (
            trap '' TERM
            printf '%s\n' "${BASHPID}" > "${FAKE_UNITY_DESCENDANT_PID_FILE}"
            while true; do
                sleep 1
            done
        ) &
        while true; do
            sleep 1
        done
        ;;
esac
EOF

cat > "$h1_bin/pgrep" << 'EOF'
#!/usr/bin/env bash
set -euo pipefail
[[ "${1:-}" == "-P" && -n "${2:-}" ]] || exit 2
ps -ef | awk -v parent="$2" 'NR > 1 && $3 == parent { print $2 }'
EOF
chmod +x "$h1_bin/docker" "$h1_bin/unity-editor" "$h1_bin/pgrep"

export FAKE_BIN="$h1_bin"
export FAKE_DOCKER_LOG="$h1_docker_log"
export FAKE_UNITY_LOG="$h1_unity_log"
export FAKE_UNITY_PID_FILE="$h1_pid_file"
export FAKE_UNITY_DESCENDANT_PID_FILE="$h1_descendant_pid_file"
export FAKE_CONTAINER_PID_FILE="$h1_container_pid_file"
export FAKE_CONTAINER_NAME_FILE="$h1_container_name_file"
export FAKE_DOCKER_RUN_PID_FILE="$h1_docker_run_pid_file"
export FAKE_DOCKER_DAEMON_PID_FILE="$h1_docker_daemon_pid_file"
export FAKE_REGISTRATION_RELEASE_FILE="$h1_registration_release_file"
export FAKE_RETURN_READY_FILE="$h1_return_ready_file"
export FAKE_RETURN_COUNT_FILE="$h1_return_count_file"
export FAKE_CONTAINER_STOPPING_FILE="$h1_container_stopping_file"
export FAKE_EVENT_LOG="$h1_events"
export FAKE_CONTAINER_ROOT="$h1_container_root"
export FAKE_PROJECT_DIR="$h1_project"
export FAKE_WORKSPACE_DIR="$REPO_ROOT"

h1_exit=0
PATH="$h1_bin:$PATH" \
UNITY_TEST_PROJECT_DIR="$h1_project" \
UNITY_LICENSE_CACHE_DIR="$h1_tempdir/license-cache" \
UNITY_SERIAL='FAKE-SERIAL' \
UNITY_EMAIL='fixture@example.invalid' \
UNITY_PASSWORD='fixture-password' \
UNITY_TIMEOUT=1 \
UNITY_LICENSE_ACTIVATION_TIMEOUT=2 \
UNITY_LICENSE_RETURN_TIMEOUT=2 \
UNITY_TERMINATION_GRACE_SECONDS=1 \
UNITY_CONTAINER_WRAPPER_SECONDS=1 \
UNITY_DOCKER_CLIENT_TIMEOUT=1 \
UNITY_DOCKER_CLIENT_KILL_GRACE=1 \
    "$REPO_ROOT/scripts/unity/run-unity-docker.sh" -batchmode -quit > "$h1_output" 2>&1 || h1_exit=$?

h1_failure=""
if [[ "$h1_exit" -ne 124 && "$h1_exit" -ne 137 ]]; then
    h1_failure="expected watchdog exit 124 or 137, got $h1_exit: $(tail -n 8 "$h1_output" | tr '\n' '|')"
elif ! grep -Fq 'TERM-to-KILL watchdog' "$h1_output"; then
    h1_failure="watchdog escalation evidence was missing"
elif ! grep -Fq -- '-returnlicense' "$h1_unity_log"; then
    h1_failure="serial return did not run after the main Unity timeout"
elif [[ -s "$h1_pid_file" ]] && kill -0 "$(cat "$h1_pid_file")" 2>/dev/null; then
    h1_failure="TERM-resistant Unity process remained alive"
elif [[ -s "$h1_descendant_pid_file" ]] && kill -0 "$(cat "$h1_descendant_pid_file")" 2>/dev/null; then
    h1_failure="TERM-resistant Unity descendant escaped the process-group KILL"
elif ! grep -Eq '^run --name unity-helpers-[0-9]+-[0-9]+' "$h1_docker_log"; then
    h1_failure="Docker run did not use a unique container name"
elif ! grep -Eq '^rm -f unity-helpers-[0-9]+-[0-9]+' "$h1_docker_log"; then
    h1_failure="host cleanup did not remove the named container"
fi

# Docker's `-e NAME` form reads only the caller's exported environment. Wrapper
# defaults are shell-local, so every non-secret inner-script control must carry
# its explicit validated value instead of accidentally disappearing in CI.
if [[ -z "$h1_failure" ]]; then
    for h1_container_control in \
        'UNITY_TIMEOUT=1' \
        'UNITY_LICENSE_ACTIVATION_TIMEOUT=2' \
        'UNITY_LICENSE_RETURN_TIMEOUT=2' \
        'UNITY_TERMINATION_GRACE_SECONDS=1'
    do
        if ! grep -Fq -- "-e ${h1_container_control}" "$h1_docker_log"; then
            h1_failure="Docker run did not pass explicit ${h1_container_control} control"
            break
        fi
    done
fi

# Signal the production wrapper before registration, during activation, and
# during main work. EXIT cleanup must settle the initiating client before
# inspect, pass TERM through Docker, return the seat, then remove the container.
# These phases share assertions so coverage cannot drift apart.
if [[ -z "$h1_failure" ]]; then
    for h1_signal_phase in registration activation main return; do
        : > "$h1_docker_log"
        : > "$h1_unity_log"
        : > "$h1_events"
        rm -f "$h1_pid_file" "$h1_descendant_pid_file" "$h1_container_pid_file" \
            "$h1_container_name_file" "$h1_docker_run_pid_file" \
            "$h1_docker_daemon_pid_file" "$h1_registration_release_file"
        rm -f "$h1_return_ready_file" "$h1_return_count_file"
        rm -f "$h1_container_stopping_file"
        h1_signal_output="$h1_tempdir/signal-${h1_signal_phase}.log"
        FAKE_SIGNAL_PHASE="$h1_signal_phase" \
        PATH="$h1_bin:$PATH" \
        UNITY_TEST_PROJECT_DIR="$h1_project" \
        UNITY_LICENSE_CACHE_DIR="$h1_tempdir/license-cache" \
        UNITY_SERIAL='FAKE-SERIAL' \
        UNITY_EMAIL='fixture@example.invalid' \
        UNITY_PASSWORD='fixture-password' \
        UNITY_TIMEOUT=30 \
        UNITY_LICENSE_ACTIVATION_TIMEOUT=30 \
        UNITY_LICENSE_RETURN_TIMEOUT=7 \
        UNITY_TERMINATION_GRACE_SECONDS=1 \
        UNITY_CONTAINER_WRAPPER_SECONDS=3 \
        UNITY_DOCKER_CLIENT_TIMEOUT=3 \
        UNITY_DOCKER_CLIENT_KILL_GRACE=1 \
            "$REPO_ROOT/scripts/unity/run-unity-docker.sh" -batchmode -quit > "$h1_signal_output" 2>&1 &
        h1_wrapper_pid=$!
        h1_ready=0
        for _ in 1 2 3 4 5 6 7 8 9 10; do
            if { [[ "$h1_signal_phase" == "registration" ]] && \
                    grep -Fq 'docker registration pending' "$h1_events" && \
                    [[ ! -e "$h1_container_name_file" ]]; } || \
                { [[ -s "$h1_container_name_file" ]] && \
                    { [[ "$h1_signal_phase" == "activation" ]] && grep -Fq 'unity activation' "$h1_events" || \
                      [[ "$h1_signal_phase" == "main" && -s "$h1_pid_file" ]]; }; }; then
                h1_ready=1
                break
            elif [[ "$h1_signal_phase" == "return" && -f "$h1_return_ready_file" ]]; then
                h1_ready=1
                break
            fi
            sleep 1
        done
        if [[ "$h1_ready" -ne 1 ]]; then
            h1_failure="${h1_signal_phase} signal fixture did not reach its licensed phase"
            kill -KILL "$h1_wrapper_pid" 2>/dev/null || true
            wait "$h1_wrapper_pid" 2>/dev/null || true
            break
        fi

        kill -TERM "$h1_wrapper_pid"
        if [[ "$h1_signal_phase" == "registration" ]]; then
            : > "$h1_registration_release_file"
        fi
        ( sleep 20; kill -KILL "$h1_wrapper_pid" 2>/dev/null || true ) &
        h1_wait_guard=$!
        h1_wrapper_exit=0
        wait "$h1_wrapper_pid" || h1_wrapper_exit=$?
        kill "$h1_wait_guard" 2>/dev/null || true
        wait "$h1_wait_guard" 2>/dev/null || true
        return_line="$(grep -nF 'unity return complete' "$h1_events" | tail -n 1 | cut -d: -f1 || true)"
        remove_line="$(grep -nE '^docker rm -f ' "$h1_events" | tail -n 1 | cut -d: -f1 || true)"
        registration_line="$(grep -nF 'docker registration complete' "$h1_events" | tail -n 1 | cut -d: -f1 || true)"
        first_inspect_line="$(grep -nE '^docker inspect ' "$h1_events" | head -n 1 | cut -d: -f1 || true)"
        inspect_line="$(grep -nE '^docker inspect ' "$h1_events" | tail -n 1 | cut -d: -f1 || true)"
        if [[ "$h1_wrapper_exit" -ne 143 ]]; then
            h1_failure="${h1_signal_phase} wrapper TERM exited ${h1_wrapper_exit}, expected 143"
        elif [[ "$h1_signal_phase" == "return" && "$(grep -cF -- '-returnlicense' "$h1_unity_log")" -ne 2 ]]; then
            h1_failure="return cancellation did not retry the interrupted serial return exactly once"
        elif [[ "$h1_signal_phase" != "return" && "$(grep -cF -- '-returnlicense' "$h1_unity_log")" -ne 1 ]]; then
            h1_failure="${h1_signal_phase} cancellation did not perform exactly one serial return: $(grep -cF -- '-returnlicense' "$h1_unity_log" || true) attempts; events=$(tr '\n' '|' < "$h1_events")"
        elif [[ -s "$h1_pid_file" ]] && kill -0 "$(cat "$h1_pid_file")" 2>/dev/null; then
            h1_failure="${h1_signal_phase} cancellation left the TERM-resistant Unity process alive"
        elif [[ -s "$h1_descendant_pid_file" ]] && kill -0 "$(cat "$h1_descendant_pid_file")" 2>/dev/null; then
            h1_failure="${h1_signal_phase} cancellation left a TERM-resistant descendant alive"
        elif [[ -s "$h1_container_pid_file" ]] && kill -0 "$(cat "$h1_container_pid_file")" 2>/dev/null; then
            h1_failure="${h1_signal_phase} cancellation left the fake container alive"
        elif [[ -s "$h1_docker_run_pid_file" ]] && kill -0 "$(cat "$h1_docker_run_pid_file")" 2>/dev/null; then
            h1_failure="${h1_signal_phase} cancellation left the initiating Docker client alive"
        elif [[ -s "$h1_docker_daemon_pid_file" ]] && kill -0 "$(cat "$h1_docker_daemon_pid_file")" 2>/dev/null; then
            h1_failure="${h1_signal_phase} cancellation left the delayed daemon registration helper alive"
        elif [[ "$h1_signal_phase" == "registration" && \
                ( -z "$first_inspect_line" || -z "$registration_line" || -z "$inspect_line" || \
                  "$first_inspect_line" -ge "$registration_line" || "$registration_line" -ge "$inspect_line" ) ]]; then
            h1_failure="cleanup did not retry inspection across delayed daemon registration"
        elif [[ -z "$return_line" || -z "$remove_line" || "$return_line" -ge "$remove_line" ]]; then
            h1_failure="${h1_signal_phase} serial return was not observed before forced container removal"
        elif ! grep -Eq '^stop --timeout 12 unity-helpers-[0-9]+-[0-9]+' "$h1_docker_log"; then
            h1_failure="mutated 2x1s TERM + 7s return + 3s wrapper reserve did not produce stop timeout 12"
        elif ! grep -Eq '^run --name unity-helpers-[0-9]+-[0-9]+ --stop-timeout 12 ' "$h1_docker_log"; then
            h1_failure="docker run did not configure the computed 12-second container cleanup grace"
        elif [[ "$h1_signal_phase" != "registration" ]] && \
            ! grep -Fq 'docker client TERM stop_timeout=12' "$h1_events"; then
            h1_failure="docker run client TERM did not preserve the configured 12-second container cleanup grace"
        fi
        if [[ -n "$h1_failure" ]]; then
            break
        fi
    done
fi

# Inspect uncertainty must never bypass the final bounded rm -f attempt.
if [[ -z "$h1_failure" ]]; then
    : > "$h1_docker_log"
    rm -f "$h1_pid_file" "$h1_descendant_pid_file" "$h1_container_pid_file" "$h1_container_name_file"
    PATH="$h1_bin:$PATH" \
    FAKE_DOCKER_INSPECT_FAIL=1 \
    UNITY_TEST_PROJECT_DIR="$h1_project" \
    UNITY_LICENSE_CACHE_DIR="$h1_tempdir/license-cache" \
    UNITY_SERIAL='FAKE-SERIAL' \
    UNITY_EMAIL='fixture@example.invalid' \
    UNITY_PASSWORD='fixture-password' \
    UNITY_TIMEOUT=1 \
    UNITY_LICENSE_ACTIVATION_TIMEOUT=2 \
    UNITY_LICENSE_RETURN_TIMEOUT=2 \
    UNITY_TERMINATION_GRACE_SECONDS=1 \
    UNITY_CONTAINER_WRAPPER_SECONDS=1 \
    UNITY_DOCKER_CLIENT_TIMEOUT=1 \
    UNITY_DOCKER_CLIENT_KILL_GRACE=1 \
        "$REPO_ROOT/scripts/unity/run-unity-docker.sh" -batchmode -quit > "$h1_tempdir/inspect-failure.log" 2>&1 || true
    if ! grep -Eq '^rm -f unity-helpers-[0-9]+-[0-9]+' "$h1_docker_log"; then
        h1_failure="inspect failure bypassed the unconditional rm -f attempt"
    fi
fi

unset FAKE_BIN FAKE_DOCKER_LOG FAKE_UNITY_LOG FAKE_UNITY_PID_FILE \
    FAKE_UNITY_DESCENDANT_PID_FILE FAKE_CONTAINER_PID_FILE \
    FAKE_CONTAINER_NAME_FILE FAKE_DOCKER_RUN_PID_FILE FAKE_DOCKER_DAEMON_PID_FILE \
    FAKE_REGISTRATION_RELEASE_FILE FAKE_EVENT_LOG FAKE_CONTAINER_ROOT \
    FAKE_RETURN_READY_FILE FAKE_RETURN_COUNT_FILE FAKE_CONTAINER_STOPPING_FILE \
    FAKE_PROJECT_DIR \
    FAKE_WORKSPACE_DIR FAKE_SIGNAL_PHASE
rm -rf "$h1_tempdir"

if [[ -z "$h1_failure" ]]; then
    pass "Unity process groups and PID 1 return safely across watchdog and host cleanup paths"
else
    fail "Unity watchdog/container cleanup regression" "$h1_failure"
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
