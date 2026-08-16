#!/usr/bin/env bash
# =============================================================================
# Test Script: lint-meta-files.ps1 Exclusion Configuration
# =============================================================================
# Validates that the meta file lint script correctly excludes common tooling
# artifacts, OS metadata files, and git conventions from meta file checks.
#
# This is a regression test for configuration bugs where exclusion lists were
# incomplete (e.g., missing .pytest_cache, .gitkeep).
#
# Run: bash scripts/tests/test-lint-meta-exclusions.sh
# Exit codes: 0 = all tests pass, 1 = test failure
#
# Note: Requires PowerShell (pwsh) for functional tests.
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
LINT_SCRIPT="$REPO_ROOT/scripts/lint-meta-files.ps1"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Test counters
tests_run=0
tests_passed=0
tests_failed=0
tests_skipped_pwsh=false

pass() {
  tests_passed=$((tests_passed + 1))
  echo -e "  ${GREEN}PASS${NC} $1"
}

fail() {
  tests_failed=$((tests_failed + 1))
  echo -e "  ${RED}FAIL${NC} $1"
  if [ -n "${2:-}" ]; then
    echo -e "       ${RED}Detail:${NC} $2"
  fi
}

run_test() {
  tests_run=$((tests_run + 1))
}

# =============================================================================
# Part 1: Configuration Verification (parse the script for expected entries)
# =============================================================================
# These tests read the PowerShell script and verify the exclusion arrays
# contain specific required entries. This catches the exact class of bug
# that was fixed: missing entries in exclusion lists.
# =============================================================================

echo ""
echo "=== Part 1: Configuration Verification ==="
echo "Checking that lint-meta-files.ps1 contains required exclusion entries..."
echo ""

# Read the script content once
SCRIPT_CONTENT="$(cat "$LINT_SCRIPT")"

# Each array's declaration is extracted ONCE, into a variable, and every membership question is then
# answered with bash string matching rather than a pipeline.
#
# This used to be `echo "$SCRIPT_CONTENT" | grep '\$excludeDirs' | grep -qF "'obj'"`, and under
# `set -o pipefail` that is a race: `grep -q` exits at its first match, the upstream `grep` dies of
# SIGPIPE, and pipefail reports the pipeline as 141 even though the match succeeded. It passes
# whenever the producer finishes writing first, which is almost always -- and lost that race once in
# CI under Repo Lint's eight concurrent workers, failing `obj` while `bin`, on the same line of the
# same string, passed.
# `$excludeDirs` is declared on ONE line, so it is matched as one line. A `/start/,/end/` range
# would not do: sed never closes a range on its start line, so it would run on to the closing `)` of
# `$excludeFilePatterns` and this check would accept a directory name that had been moved into the
# file-pattern array -- which is exactly the drift it exists to catch.
EXCLUDE_DIRS_BLOCK="$(printf '%s\n' "$SCRIPT_CONTENT" | sed -n '/^\$excludeDirs *=/p')"
EXCLUDE_FILE_PATTERNS_BLOCK="$(printf '%s\n' "$SCRIPT_CONTENT" | sed -n '/\$excludeFilePatterns/,/)/p')"
EXCLUDE_DIR_PATTERNS_BLOCK="$(printf '%s\n' "$SCRIPT_CONTENT" | sed -n '/\$excludeDirPatterns/,/)/p')"

# Helper: check that a string appears in the excludeDirs array declaration
check_exclude_dir() {
  local entry="$1"
  local description="$2"
  run_test
  if [ -n "$EXCLUDE_DIRS_BLOCK" ] && [[ "$EXCLUDE_DIRS_BLOCK" == *"'$entry'"* ]]; then
    pass "excludeDirs contains '$entry' ($description)"
  else
    fail "excludeDirs missing '$entry' ($description)"
  fi
}

# Helper: check that a pattern appears in the excludeFilePatterns array
check_exclude_file_pattern() {
  local pattern="$1"
  local description="$2"
  run_test
  if [ -n "$EXCLUDE_FILE_PATTERNS_BLOCK" ] && [[ "$EXCLUDE_FILE_PATTERNS_BLOCK" == *"'$pattern'"* ]]; then
    pass "excludeFilePatterns contains '$pattern' ($description)"
  else
    fail "excludeFilePatterns missing '$pattern' ($description)"
  fi
}

# Helper: check that an entry appears in the excludeDirPatterns array
check_exclude_dir_pattern() {
  local pattern="$1"
  local description="$2"
  run_test
  if [ -n "$EXCLUDE_DIR_PATTERNS_BLOCK" ] && [[ "$EXCLUDE_DIR_PATTERNS_BLOCK" == *"'$pattern'"* ]]; then
    pass "excludeDirPatterns contains '$pattern' ($description)"
  else
    fail "excludeDirPatterns missing '$pattern' ($description)"
  fi
}

echo "--- Directory exclusions (excludeDirs) ---"
check_exclude_dir "node_modules"    "npm packages"
check_exclude_dir ".git"            "git internals"
check_exclude_dir "obj"             ".NET build output"
check_exclude_dir "bin"             "binary build output"
check_exclude_dir "Library"         "Unity Library cache"
check_exclude_dir "Temp"            "Unity Temp folder"
check_exclude_dir ".pytest_cache"   "pytest cache (Python)"
check_exclude_dir "__pycache__"     "Python bytecode cache"
check_exclude_dir ".mypy_cache"     "mypy type checker cache"

echo ""
echo "--- File pattern exclusions (excludeFilePatterns) ---"
check_exclude_file_pattern "*.meta"             "meta files themselves"
check_exclude_file_pattern "package-lock.json"  "npm lock file"
check_exclude_file_pattern "Gemfile.lock"       "Ruby lock file"
check_exclude_file_pattern "*.tmp"              "temporary files"
check_exclude_file_pattern ".gitkeep"           "git directory placeholders"
check_exclude_file_pattern ".DS_Store"          "macOS Finder metadata"
check_exclude_file_pattern "Thumbs.db"          "Windows Explorer thumbnails"
check_exclude_file_pattern "*.pyc"              "Python compiled bytecode"
check_exclude_file_pattern "*.pyo"              "Python optimized bytecode"
check_exclude_file_pattern "*.swp"              "Vim swap files"
check_exclude_file_pattern "*.swo"              "Vim swap files"

echo ""
echo "--- Directory pattern exclusions (excludeDirPatterns) ---"
check_exclude_dir_pattern "Samples~" "Unity sample folder convention"

# =============================================================================
# Part 2: Functional Tests (run the lint against controlled workspaces)
# =============================================================================
# These tests create temporary directory structures with excluded artifacts
# (without .meta files) and verify the lint script passes. If exclusions are
# broken, the lint would flag the missing .meta files and fail.
# =============================================================================

echo ""
echo "=== Part 2: Functional Tests ==="

if ! command -v pwsh >/dev/null 2>&1; then
  echo -e "${YELLOW}SKIP${NC}: PowerShell (pwsh) not available; skipping functional tests."
  tests_skipped_pwsh=true
  echo ""
else

  echo "Running lint against controlled temporary workspaces..."
  echo ""

  # Helper: create a minimal valid workspace in a temp dir
  # Sets up a source root dir with its .meta file
  create_workspace() {
    local ws
    ws=$(mktemp -d) || {
      echo "ERROR: Failed to create temporary workspace" >&2
      return 97
    }

    if ! git init "$ws" >/dev/null 2>&1; then
      echo "ERROR: Failed to initialize git repository: $ws" >&2
      rm -rf "$ws" 2>/dev/null || true
      return 97
    fi

    if ! git -C "$ws" config user.email "test@test.com" >/dev/null 2>&1; then
      echo "ERROR: Failed to configure git user.email in workspace: $ws" >&2
      rm -rf "$ws" 2>/dev/null || true
      return 97
    fi

    if ! git -C "$ws" config user.name "Test" >/dev/null 2>&1; then
      echo "ERROR: Failed to configure git user.name in workspace: $ws" >&2
      rm -rf "$ws" 2>/dev/null || true
      return 97
    fi

    if ! git -C "$ws" config core.excludesFile "" >/dev/null 2>&1; then
      echo "ERROR: Failed to clear git excludesFile in workspace: $ws" >&2
      rm -rf "$ws" 2>/dev/null || true
      return 97
    fi

    # Keep script-derived repo root behavior intact by running a workspace-local copy.
    if ! mkdir -p "$ws/lint-runner"; then
      echo "ERROR: Failed to create lint runner directory in workspace: $ws" >&2
      rm -rf "$ws" 2>/dev/null || true
      return 97
    fi

    if ! cp "$LINT_SCRIPT" "$ws/lint-runner/lint-meta-files.ps1"; then
      echo "ERROR: Failed to copy lint script into workspace: $ws" >&2
      rm -rf "$ws" 2>/dev/null || true
      return 97
    fi

    CLEANUP_DIRS+=("$ws")
    echo "$ws"
  }

  # Lint output captured for diagnostics on failure
  LINT_OUTPUT=""
  LINT_DIAGNOSTICS=""

  CLEANUP_DIRS=()
  cleanup_workspaces() {
    for d in "${CLEANUP_DIRS[@]}"; do
      rm -rf "$d" 2>/dev/null || true
    done
  }
  trap cleanup_workspaces EXIT

  # Helper: run lint and capture result
  # Args: $1=workspace path
  # Returns: exit code of lint script
  run_lint() {
    local ws="$1"
    local lint_script_path="$ws/lint-runner/lint-meta-files.ps1"
    local git_root=""
    local tracked_count="0"
    local tracked_preview="<none>"
    local tracked_files=""
    local exit_code=0

    if ! git -C "$ws" rev-parse --git-dir >/dev/null 2>&1; then
      LINT_OUTPUT="ERROR: Workspace is not a git repository: $ws"
      LINT_DIAGNOSTICS="workspace=$ws"
      return 99
    fi

    if [[ ! -f "$lint_script_path" ]]; then
      LINT_OUTPUT="ERROR: Workspace-local lint script missing: $lint_script_path"
      LINT_DIAGNOSTICS="workspace=$ws; source_lint_script=$LINT_SCRIPT"
      return 98
    fi

    git_root=$(git -C "$ws" rev-parse --show-toplevel 2>/dev/null || echo "<unknown>")
    tracked_files=$(git -C "$ws" ls-files 2>/dev/null || true)
    if [[ -n "$tracked_files" ]]; then
      tracked_count=$(printf '%s\n' "$tracked_files" | wc -l | tr -d ' ')
      # `sed -n '1,12p'` rather than `head -n 12`: head exits at its limit, SIGPIPEs the producer,
      # and under `set -o pipefail` this capture then reports 141 and `set -e` aborts the run
      # mid-diagnostic (#465). sed reads its input to EOF, so no stage can short-circuit.
      tracked_preview=$(printf '%s\n' "$tracked_files" | sed -n '1,12p' | tr '\n' '; ')
    else
      tracked_count="0"
      tracked_preview="<none>"
    fi

    LINT_OUTPUT=$(cd "$ws" && pwsh -NoProfile -File "$lint_script_path" -VerboseOutput 2>&1) || exit_code=$?
    LINT_DIAGNOSTICS="workspace=$ws; git_root=$git_root; lint_script=$lint_script_path; tracked_count=$tracked_count; tracked_preview=$tracked_preview"
    return $exit_code
  }

  # Helper: test that excluded items don't cause lint failure
  # Args: $1=test name, $2...=setup commands
  test_exclusion() {
    local test_name="$1"
    local setup_fn="$2"
    run_test

    local ws=""
    if ! ws=$(create_workspace 2>&1); then
      fail "$test_name" "TEST_HARNESS: Workspace creation failed. Detail: $ws"
      return
    fi

    if ! "$setup_fn" "$ws"; then
      local setup_exit=$?
      rm -rf "$ws"
      fail "$test_name" "TEST_HARNESS: Setup function '$setup_fn' failed with code $setup_exit"
      return
    fi

    if ! (cd "$ws" && git add -A >/dev/null 2>&1); then
      local status_preview=""
      status_preview=$(cd "$ws" && git status --short 2>&1 | head -n 20 || true)
      rm -rf "$ws"
      fail "$test_name" "TEST_HARNESS: git add -A failed. Workspace: $ws. Git status: ${status_preview:-<empty>}"
      return
    fi

    local exit_code=0
    run_lint "$ws" || exit_code=$?

    rm -rf "$ws"

    if [ "$exit_code" -eq 98 ] || [ "$exit_code" -eq 99 ]; then
      fail "$test_name" "Test harness error (exit code $exit_code). Diagnostics: ${LINT_DIAGNOSTICS:-<none>}. Lint output: ${LINT_OUTPUT:-<empty>}"
    elif [ "$exit_code" -eq 0 ]; then
      pass "$test_name"
    else
      fail "$test_name" "Lint exited with code $exit_code (expected 0). Diagnostics: ${LINT_DIAGNOSTICS:-<none>}. Lint output: ${LINT_OUTPUT:-<empty>}"
    fi
  }

  # Helper: test that non-excluded items DO cause lint failure
  test_detected() {
    local test_name="$1"
    local setup_fn="$2"
    run_test

    local ws=""
    if ! ws=$(create_workspace 2>&1); then
      fail "$test_name" "TEST_HARNESS: Workspace creation failed. Detail: $ws"
      return
    fi

    if ! "$setup_fn" "$ws"; then
      local setup_exit=$?
      rm -rf "$ws"
      fail "$test_name" "TEST_HARNESS: Setup function '$setup_fn' failed with code $setup_exit"
      return
    fi

    if ! (cd "$ws" && git add -A >/dev/null 2>&1); then
      local status_preview=""
      status_preview=$(cd "$ws" && git status --short 2>&1 | head -n 20 || true)
      rm -rf "$ws"
      fail "$test_name" "TEST_HARNESS: git add -A failed. Workspace: $ws. Git status: ${status_preview:-<empty>}"
      return
    fi

    local exit_code=0
    run_lint "$ws" || exit_code=$?

    rm -rf "$ws"

    if [ "$exit_code" -eq 98 ] || [ "$exit_code" -eq 99 ]; then
      fail "$test_name" "Test harness error (exit code $exit_code). Diagnostics: ${LINT_DIAGNOSTICS:-<none>}. Lint output: ${LINT_OUTPUT:-<empty>}"
    elif [ "$exit_code" -ne 0 ]; then
      pass "$test_name"
    else
      fail "$test_name" "Expected lint failure but it passed. Diagnostics: ${LINT_DIAGNOSTICS:-<none>}. Lint output: ${LINT_OUTPUT:-<empty>}"
    fi
  }

  run_case_group() {
    local heading="$1"
    local runner="$2"
    shift 2

    echo ""
    echo "--- $heading ---"

    local case_entry=""
    local case_name=""
    local setup_fn=""
    for case_entry in "$@"; do
      case_name="${case_entry%%|*}"
      setup_fn="${case_entry#*|}"

      if [[ -z "$case_name" || -z "$setup_fn" || "$case_name" == "$setup_fn" ]]; then
        fail "Case definition format" "Expected '<name>|<setup_fn>' but got: $case_entry"
        continue
      fi

      if ! declare -f "$setup_fn" >/dev/null 2>&1; then
        fail "Case definition format" "Setup function '$setup_fn' is not defined for case '$case_name'"
        continue
      fi

      "$runner" "$case_name" "$setup_fn"
    done
  }

  # -------------------------------------------------------------------------
  # Setup functions for excluded directory tests
  # -------------------------------------------------------------------------

  setup_pytest_cache() {
    local ws="$1"
    mkdir -p "$ws/scripts"
    touch "$ws/scripts.meta"
    mkdir -p "$ws/scripts/.pytest_cache/v/cache"
    echo "test" > "$ws/scripts/.pytest_cache/README.md"
    echo "test" > "$ws/scripts/.pytest_cache/v/cache/nodeids"
  }

  setup_pycache() {
    local ws="$1"
    mkdir -p "$ws/scripts"
    touch "$ws/scripts.meta"
    mkdir -p "$ws/scripts/__pycache__"
    echo "test" > "$ws/scripts/__pycache__/module.cpython-310.pyc"
  }

  setup_mypy_cache() {
    local ws="$1"
    mkdir -p "$ws/scripts"
    touch "$ws/scripts.meta"
    mkdir -p "$ws/scripts/.mypy_cache/3.10"
    echo "{}" > "$ws/scripts/.mypy_cache/3.10/cache.json"
  }

  setup_node_modules() {
    local ws="$1"
    mkdir -p "$ws/scripts"
    touch "$ws/scripts.meta"
    mkdir -p "$ws/scripts/node_modules/some-pkg"
    echo "{}" > "$ws/scripts/node_modules/some-pkg/package.json"
  }

  # NOTE: .git/ exclusion cannot be functionally tested because git itself
  # refuses to track .git/ directories at any nesting level. The exclusion
  # is verified by the Part 1 config check above. This is defense-in-depth
  # in the linter for non-git-based file discovery scenarios.

  setup_obj_bin() {
    local ws="$1"
    mkdir -p "$ws/Runtime"
    touch "$ws/Runtime.meta"
    mkdir -p "$ws/Runtime/obj/Debug"
    echo "test" > "$ws/Runtime/obj/Debug/output.dll"
    mkdir -p "$ws/Runtime/bin/Release"
    echo "test" > "$ws/Runtime/bin/Release/output.dll"
  }

  # -------------------------------------------------------------------------
  # Setup functions for excluded file pattern tests
  # -------------------------------------------------------------------------

  setup_gitkeep() {
    local ws="$1"
    mkdir -p "$ws/docs/overrides"
    touch "$ws/docs.meta"
    touch "$ws/docs/overrides.meta"
    echo "" > "$ws/docs/overrides/.gitkeep"
  }

  setup_ds_store() {
    local ws="$1"
    mkdir -p "$ws/Runtime/Core"
    touch "$ws/Runtime.meta"
    touch "$ws/Runtime/Core.meta"
    printf '\x00\x00\x00\x01' > "$ws/Runtime/Core/.DS_Store"
  }

  setup_thumbs_db() {
    local ws="$1"
    mkdir -p "$ws/Editor"
    touch "$ws/Editor.meta"
    echo "thumbs" > "$ws/Editor/Thumbs.db"
  }

  setup_pyc_files() {
    local ws="$1"
    mkdir -p "$ws/scripts"
    touch "$ws/scripts.meta"
    echo "bytecode" > "$ws/scripts/helper.pyc"
    echo "bytecode" > "$ws/scripts/utils.pyo"
  }

  setup_vim_swap() {
    local ws="$1"
    mkdir -p "$ws/Runtime"
    touch "$ws/Runtime.meta"
    echo "swap" > "$ws/Runtime/.SomeFile.cs.swp"
    echo "swap" > "$ws/Runtime/.OtherFile.cs.swo"
  }

  setup_tmp_files() {
    local ws="$1"
    mkdir -p "$ws/scripts"
    touch "$ws/scripts.meta"
    echo "temp" > "$ws/scripts/build.tmp"
  }

  setup_package_lock() {
    local ws="$1"
    mkdir -p "$ws/scripts"
    touch "$ws/scripts.meta"
    echo "{}" > "$ws/scripts/package-lock.json"
  }

  setup_gemfile_lock() {
    local ws="$1"
    mkdir -p "$ws/docs"
    touch "$ws/docs.meta"
    echo "GEM" > "$ws/docs/Gemfile.lock"
  }

  # -------------------------------------------------------------------------
  # Setup: non-excluded item (should be detected as missing .meta)
  # -------------------------------------------------------------------------

  setup_normal_file_no_meta() {
    local ws="$1"
    mkdir -p "$ws/Runtime"
    touch "$ws/Runtime.meta"
    echo "public class Foo {}" > "$ws/Runtime/Foo.cs"
    # Intentionally no Foo.cs.meta
  }

  setup_normal_dir_no_meta() {
    local ws="$1"
    mkdir -p "$ws/Runtime/SubDir"
    touch "$ws/Runtime.meta"
    echo "public class Bar {}" > "$ws/Runtime/SubDir/Bar.cs"
    touch "$ws/Runtime/SubDir/Bar.cs.meta"
    # Intentionally no SubDir.meta
  }

  setup_orphaned_meta_no_source() {
    local ws="$1"
    mkdir -p "$ws/Runtime"
    touch "$ws/Runtime.meta"
    echo "public class Baz {}" > "$ws/Runtime/Baz.cs"
    touch "$ws/Runtime/Baz.cs.meta"
    touch "$ws/Runtime/Deleted.cs.meta"
    # Intentionally no Deleted.cs -- orphaned meta
  }

  # -------------------------------------------------------------------------
  # Setup: empty workspace (no issues)
  # -------------------------------------------------------------------------

  setup_empty() {
    local ws="$1"
    # No source roots at all - lint should pass with nothing to scan
    true
  }

  setup_samples_tilde() {
    local ws="$1"
    mkdir -p "$ws/Samples~/ExampleScene"
    echo "test" > "$ws/Samples~/ExampleScene/Example.cs"
    echo "test" > "$ws/Samples~/README.md"
    # No .meta files for anything under Samples~ - Unity ignores ~ dirs
  }

  setup_orphaned_meta_excluded() {
    local ws="$1"
    mkdir -p "$ws/scripts"
    touch "$ws/scripts.meta"
    # Orphaned .meta for an excluded dir that doesn't exist on disk
    touch "$ws/scripts/.pytest_cache.meta"
    # No scripts/.pytest_cache/ directory exists
  }

  setup_orphaned_meta_excluded_file_pattern() {
    local ws="$1"
    mkdir -p "$ws/scripts"
    touch "$ws/scripts.meta"
    # Orphaned .meta for excluded file patterns (*.pyc, *.tmp, etc.)
    # The source files are excluded by file pattern but .meta files remain
    touch "$ws/scripts/helper.pyc.meta"
    touch "$ws/scripts/build.tmp.meta"
    # No helper.pyc or build.tmp exist -- their .meta files are orphaned
    # but the lint should not flag them because the source patterns are excluded
  }

  setup_deeply_nested_exclusion() {
    local ws="$1"
    mkdir -p "$ws/scripts/sub/deeper/.pytest_cache/cache/data"
    touch "$ws/scripts.meta"
    touch "$ws/scripts/sub.meta"
    touch "$ws/scripts/sub/deeper.meta"
    echo "test" > "$ws/scripts/sub/deeper/.pytest_cache/cache/data/file.txt"
    # No .meta files for anything inside .pytest_cache
  }

  # -------------------------------------------------------------------------
  # Run directory exclusion tests
  # -------------------------------------------------------------------------

  excluded_directory_cases=(
    ".pytest_cache excluded from scripts/|setup_pytest_cache"
    "__pycache__ excluded from scripts/|setup_pycache"
    ".mypy_cache excluded from scripts/|setup_mypy_cache"
    "node_modules excluded from scripts/|setup_node_modules"
    "obj/ and bin/ excluded from Runtime/|setup_obj_bin"
  )

  excluded_file_pattern_cases=(
    ".gitkeep excluded from docs/|setup_gitkeep"
    ".DS_Store excluded from Runtime/|setup_ds_store"
    "Thumbs.db excluded from Editor/|setup_thumbs_db"
    "*.pyc and *.pyo excluded from scripts/|setup_pyc_files"
    "*.swp and *.swo excluded from Runtime/|setup_vim_swap"
    "*.tmp excluded from scripts/|setup_tmp_files"
    "package-lock.json excluded|setup_package_lock"
    "Gemfile.lock excluded|setup_gemfile_lock"
  )

  detection_cases=(
    "Normal .cs file without .meta detected|setup_normal_file_no_meta"
    "Normal dir without .meta detected|setup_normal_dir_no_meta"
    "Orphaned .meta without source detected|setup_orphaned_meta_no_source"
  )

  directory_pattern_cases=(
    "Samples~ dir excluded (no .meta needed)|setup_samples_tilde"
  )

  orphaned_excluded_cases=(
    "Orphaned .meta for excluded .pytest_cache|setup_orphaned_meta_excluded"
    "Orphaned .meta for excluded file patterns|setup_orphaned_meta_excluded_file_pattern"
  )

  nested_exclusion_cases=(
    "Deeply nested .pytest_cache excluded|setup_deeply_nested_exclusion"
  )

  edge_cases=(
    "Empty workspace (no source roots)|setup_empty"
  )

  run_case_group "Excluded directories (functional)" test_exclusion "${excluded_directory_cases[@]}"
  run_case_group "Excluded file patterns (functional)" test_exclusion "${excluded_file_pattern_cases[@]}"
  run_case_group "Detection tests (non-excluded items flagged)" test_detected "${detection_cases[@]}"
  run_case_group "Directory pattern exclusions (functional)" test_exclusion "${directory_pattern_cases[@]}"
  run_case_group "Orphaned .meta for excluded items" test_exclusion "${orphaned_excluded_cases[@]}"
  run_case_group "Deeply nested exclusions" test_exclusion "${nested_exclusion_cases[@]}"
  run_case_group "Edge cases" test_exclusion "${edge_cases[@]}"

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
  if [ "$tests_skipped_pwsh" = "true" ]; then
    echo -e "  ${YELLOW}WARNING${NC}: Functional tests were skipped (pwsh not available)"
  fi
  echo ""
  echo -e "${RED}FAILED${NC}"
  exit 1
else
  echo -e "Tests failed: ${GREEN}0${NC}"
  if [ "$tests_skipped_pwsh" = "true" ]; then
    echo -e "  ${YELLOW}WARNING${NC}: Functional tests were skipped (pwsh not available)"
  fi
  echo ""
  echo -e "${GREEN}ALL TESTS PASSED${NC}"
  exit 0
fi
