#!/usr/bin/env bash
# =============================================================================
# Pre-commit and hook-adjacent regression tests (surgical)
# =============================================================================
# APPROACH CHOICE:
#   This suite combines focused CLI smoke tests with end-to-end checks for the
#   current fast pre-commit hook contract. Rationale:
#
#     1. Historical PWS001 regressions are CLI-arg-binding bugs. Invoking the
#        target script directly reproduces the failure without pretending that
#        the fast hook still owns every validation.
#     2. The actual hook is exercised end-to-end only for fast, last-resort
#        behavior that it still owns: launcher delegation, artifact cleanup,
#        final-newline safety, LLM checks, and staged C# blob checks.
#     3. This approach avoids starting slow tools in pre-commit coverage and
#        leaves the working tree untouched. A cleanup trap guarantees no
#        leftover temp state.
#
#   Trade-off: agent-preflight-owned tools such as spelling, Markdown lint,
#   CSharpier formatting, drawer lint, duplicate-using lint, and test lint are
#   covered here only as standalone CLI smoke tests where useful.
#
# Scope: current pre-commit behavior plus hook-adjacent CLI regression guards.
#        Missing sub-tool dependencies cause the corresponding CLI smoke test
#        to SKIP, not FAIL.
#
# Run:   bash scripts/tests/test-precommit-integration.sh
# Exit:  0 on all-pass/skip, non-zero on any failure.
# =============================================================================

set -euo pipefail

# cspell:ignore ZZQWERTYNOISE gpgsign

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[0;33m'
NC='\033[0m'

# -----------------------------------------------------------------------------
# Hard precondition: pwsh must be available. Every test in this file exercises
# the `pwsh -NoProfile -File ...` invocation path, so without pwsh the suite
# would silently all-skip and return exit 0 — a false green on CI. Fail loudly
# and non-zero instead.
# -----------------------------------------------------------------------------
if ! command -v pwsh >/dev/null 2>&1; then
    echo -e "${RED}[FAIL]${NC} pwsh is not installed; cannot run pre-commit integration tests."
    echo "       Install PowerShell (https://aka.ms/powershell) before running this suite."
    exit 2
fi

tests_passed=0
tests_failed=0
tests_skipped=0

# Absolute path to the repo root (parent of scripts/).
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

TEMPDIR="$(mktemp -d)"
# shellcheck disable=SC2329  # invoked via trap
cleanup() {
    rm -rf "$TEMPDIR"
}
trap cleanup EXIT

pass() {
    tests_passed=$((tests_passed + 1))
    echo -e "${GREEN}[PASS]${NC} $1"
}

fail() {
    tests_failed=$((tests_failed + 1))
    echo -e "${RED}[FAIL]${NC} $1"
    if [[ -n "${2:-}" ]]; then
        echo "        $2"
    fi
}

skip() {
    tests_skipped=$((tests_skipped + 1))
    echo -e "${YELLOW}[SKIP]${NC} $1 (${2:-no reason given})"
}

# Note: we used to have a `need_pwsh()` helper that every test called to
# short-circuit on missing pwsh. The hard precondition at the top of this file
# now exits 2 when pwsh is absent, so the helper's false-branch is unreachable.
# It has been removed and all call sites deleted.

# -----------------------------------------------------------------------------
# Test: dependabot.yml CLI binding regression.
# -----------------------------------------------------------------------------
# Write a synthetic, known-good Dependabot v2 fixture to TEMPDIR and echo the
# path. Using a synthetic fixture decouples this regression test from the live
# .github/dependabot.yml — a future schema violation in that file must not
# cause the PWS001 regression guard to fail for unrelated reasons. The fixture
# is deliberately minimal while still satisfying every rule in
# scripts/lint-dependabot.ps1 (DEP001 version:2 before updates:, DEP005
# schedule: on each entry, DEP006 patterns: inside every group, etc.).
write_synthetic_dependabot_fixture() {
    local fixture="$TEMPDIR/dependabot-synthetic.yml"
    cat > "$fixture" <<'EOF'
version: 2
updates:
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
    groups:
      all-dependencies:
        patterns:
          - "*"
EOF
    echo "$fixture"
}

test_dependabot_branch() {
    local name="dependabot.yml CLI binding (PWS001 regression)"

    # Synthetic fixture — intentionally decoupled from the live file.
    local target
    target=$(write_synthetic_dependabot_fixture)

    if pwsh -NoProfile -File "$REPO_ROOT/scripts/lint-dependabot.ps1" -Paths "$target" >/dev/null 2>&1; then
        pass "$name"
    else
        local ec=$?
        fail "$name" "pwsh lint-dependabot exited $ec on synthetic fixture"
    fi
}

# -----------------------------------------------------------------------------
# Test: YAML lint invocation shape.
# -----------------------------------------------------------------------------
test_yaml_lint_invocation() {
    local name="lint-yaml.ps1 invocation"
    if [[ ! -f "$REPO_ROOT/scripts/lint-yaml.ps1" ]]; then
        skip "$name" "no scripts/lint-yaml.ps1"; return
    fi

    # Invoke with -VerboseOutput on the script itself (doesn't require yamllint
    # binary for the parse/help check). Use --Help equivalent by calling with
    # Get-Help to confirm the script is CLI-syntactically valid.
    if pwsh -NoProfile -Command "Get-Help '$REPO_ROOT/scripts/lint-yaml.ps1' | Out-Null" >/dev/null 2>&1; then
        pass "$name"
    else
        fail "$name" "pwsh could not parse lint-yaml.ps1"
    fi
}

# -----------------------------------------------------------------------------
# Test: lint-skill-sizes.ps1 accepts explicit -Paths.
# -----------------------------------------------------------------------------
test_skill_sizes_branch() {
    local name="lint-skill-sizes.ps1 CLI binding"

    # Use an arbitrary existing skill file as the "staged" fixture.
    local target
    target=$(find "$REPO_ROOT/.llm/skills" -maxdepth 1 -name '*.md' -type f 2>/dev/null | head -n1 || true)
    if [[ -z "$target" ]]; then
        skip "$name" "no .llm/skills/*.md present"; return
    fi

    if pwsh -NoProfile -File "$REPO_ROOT/scripts/lint-skill-sizes.ps1" -Paths "$target" >/dev/null 2>&1; then
        pass "$name"
    else
        fail "$name" "lint-skill-sizes.ps1 failed on $target"
    fi
}

# -----------------------------------------------------------------------------
# Test: lint-tests.ps1 accepts explicit -Paths.
# -----------------------------------------------------------------------------
test_lint_tests_branch() {
    local name="lint-tests.ps1 CLI binding"
    if [[ ! -f "$REPO_ROOT/scripts/lint-tests.ps1" ]]; then
        skip "$name" "no scripts/lint-tests.ps1"; return
    fi

    # Create a trivial compliant test file in the tempdir.
    local fixture="$TEMPDIR/NoOpTest.cs"
    cat > "$fixture" <<'EOF'
using NUnit.Framework;

namespace WallstopStudios.UnityHelpers.Tests
{
    public class NoOpTest
    {
        [Test]
        public void DoesNothing_OK()
        {
            Assert.IsTrue(true);
        }
    }
}
EOF
    if pwsh -NoProfile -File "$REPO_ROOT/scripts/lint-tests.ps1" -Paths "$fixture" >/dev/null 2>&1; then
        pass "$name"
    else
        local ec=$?
        # lint-tests may warn on a trivial file; accept exit 0 OR any exit that
        # is not a PWS001-style parameter-binding error. We re-run and grep.
        local out
        out=$(pwsh -NoProfile -File "$REPO_ROOT/scripts/lint-tests.ps1" -Paths "$fixture" 2>&1 || true)
        if echo "$out" | grep -q "Parameter cannot be processed"; then
            fail "$name" "PWS001-style param binding failure: $out"
        else
            # Any other exit is acceptable for this integration smoke — the
            # point is the CLI binding works, not that the file is lint-clean.
            pass "$name"
        fi
    fi
}

# -----------------------------------------------------------------------------
# Test: format-staged-csharp.ps1 accepts staged-file arguments.
# Pre-commit no longer runs CSharpier, but this script remains part of the
# agentic formatting workflow and must not regress parameter binding.
# -----------------------------------------------------------------------------
test_format_staged_csharp_branch() {
    local name="format-staged-csharp.ps1 CLI binding"
    if [[ ! -f "$REPO_ROOT/scripts/format-staged-csharp.ps1" ]]; then
        skip "$name" "no scripts/format-staged-csharp.ps1"; return
    fi

    local fixture="$TEMPDIR/FormatFixture.cs"
    echo "namespace X { public class Y { } }" > "$fixture"

    local out
    out=$(pwsh -NoProfile -File "$REPO_ROOT/scripts/format-staged-csharp.ps1" "$fixture" 2>&1 || true)
    if echo "$out" | grep -q "Parameter cannot be processed"; then
        fail "$name" "PWS001-style param binding failure: $out"
    else
        pass "$name"
    fi
}

# -----------------------------------------------------------------------------
# Test: lint-drawer-multiobject.ps1 accepts explicit -Paths.
# -----------------------------------------------------------------------------
test_drawer_branch() {
    local name="lint-drawer-multiobject.ps1 CLI binding"
    if [[ ! -f "$REPO_ROOT/scripts/lint-drawer-multiobject.ps1" ]]; then
        skip "$name" "no scripts/lint-drawer-multiobject.ps1"; return
    fi

    local fixture="$TEMPDIR/SampleDrawer.cs"
    cat > "$fixture" <<'EOF'
using UnityEditor;
using UnityEngine;

public class SampleDrawer : PropertyDrawer
{
}
EOF

    local out
    out=$(pwsh -NoProfile -File "$REPO_ROOT/scripts/lint-drawer-multiobject.ps1" -Paths "$fixture" 2>&1 || true)
    if echo "$out" | grep -q "Parameter cannot be processed"; then
        fail "$name" "PWS001-style param binding failure: $out"
    else
        pass "$name"
    fi
}

# -----------------------------------------------------------------------------
# Test: lint-duplicate-usings.ps1 accepts explicit -Paths.
# -----------------------------------------------------------------------------
test_duplicate_usings_branch() {
    local name="lint-duplicate-usings.ps1 CLI binding"
    if [[ ! -f "$REPO_ROOT/scripts/lint-duplicate-usings.ps1" ]]; then
        skip "$name" "no scripts/lint-duplicate-usings.ps1"; return
    fi

    local fixture="$TEMPDIR/SampleDuplicateUsingFixture.cs"
    cat > "$fixture" <<'EOF'
namespace WallstopStudios.UnityHelpers.Tests
{
    using System;
    using System;

    internal sealed class Sample
    {
    }
}
EOF

    local out
    out=$(pwsh -NoProfile -File "$REPO_ROOT/scripts/lint-duplicate-usings.ps1" -Paths "$fixture" 2>&1 || true)
    if echo "$out" | grep -q "Parameter cannot be processed"; then
        fail "$name" "PWS001-style param binding failure: $out"
    elif ! echo "$out" | grep -q "UNH007"; then
        fail "$name" "Expected UNH007 duplicate-using violation not detected: $out"
    else
        pass "$name"
    fi
}

# -----------------------------------------------------------------------------
# Test: sync scripts remain parseable for direct invocation.
# -----------------------------------------------------------------------------
test_sync_scripts_branch() {
    local name="sync scripts (banner + issue templates)"

    local banner="$REPO_ROOT/scripts/sync-banner-version.ps1"
    if [[ -f "$banner" ]]; then
        if ! pwsh -NoProfile -Command "Get-Help '$banner' | Out-Null" >/dev/null 2>&1; then
            fail "$name" "could not parse sync-banner-version.ps1"
            return
        fi
    fi
    local issue="$REPO_ROOT/scripts/sync-issue-template-versions.ps1"
    if [[ -f "$issue" ]]; then
        if ! pwsh -NoProfile -Command "Get-Help '$issue' | Out-Null" >/dev/null 2>&1; then
            fail "$name" "could not parse sync-issue-template-versions.ps1"
            return
        fi
    fi
    pass "$name"
}

# -----------------------------------------------------------------------------
# Test: the exact original failing command line now exits 0
# This is the canonical regression reproducer.
# -----------------------------------------------------------------------------
test_original_failing_command() {
    local name="regression: original failing pwsh -File -Paths invocation"

    # Synthetic fixture — the regression is about CLI param binding, not about
    # the contents of the live .github/dependabot.yml.
    local target
    target=$(write_synthetic_dependabot_fixture)

    local out ec
    out=$(pwsh -NoProfile -File "$REPO_ROOT/scripts/lint-dependabot.ps1" -Paths "$target" 2>&1)
    ec=$?
    if [[ $ec -eq 0 ]]; then
        pass "$name"
    else
        fail "$name" "exit=$ec output=$out"
    fi
}

# -----------------------------------------------------------------------------
# Test (P1-4): Pre-commit spell-check speed boundary.
#
# Spelling belongs in agent-preflight, validate:prepush, and CI. The local
# pre-commit hook is now a last-resort fast guard and must not reintroduce
# cspell process startup or pipeline-exit fragility.
# -----------------------------------------------------------------------------
test_precommit_spellcheck_regression() {
    local name="pre-commit excludes cspell; agent-preflight owns spelling"
    if grep -REq 'cspell[[:space:]]+(lint|--no-progress)|SPELL_FILES_ARRAY' "$REPO_ROOT/.githooks/pre-commit" "$REPO_ROOT/.githooks/pre-commit.ps1"; then
        fail "$name" "pre-commit must not run spelling checks"
    elif grep -q 'cspell lint' "$REPO_ROOT/scripts/agent-preflight.ps1"; then
        pass "$name"
    else
        fail "$name" "agent-preflight no longer invokes cspell lint"
    fi
}

test_precommit_entrypoint_delegates_to_ps1() {
    local name="pre-commit entrypoint delegates to .ps1 implementation"
    local sandbox="$TEMPDIR/precommit-entrypoint"
    mkdir -p "$sandbox/.githooks"

    git -C "$sandbox" init -q
    git -C "$sandbox" config user.email test@example.com
    git -C "$sandbox" config user.name "Test User"
    git -C "$sandbox" config commit.gpgsign false
    git -C "$sandbox" config core.hooksPath .githooks

    local marker_file="$TEMPDIR/pre-commit-entrypoint-marker-$$"
    rm -f "$marker_file"

    cp "$REPO_ROOT/.githooks/pre-commit" "$sandbox/.githooks/pre-commit"
    cat > "$sandbox/.githooks/pre-commit.ps1" <<EOF
#!/usr/bin/env pwsh
New-Item -ItemType File -Path '$marker_file' -Force | Out-Null
exit 0
EOF
    chmod +x "$sandbox/.githooks/pre-commit"

    printf 'entrypoint\n' > "$sandbox/README.md"
    git -C "$sandbox" add README.md

    local output exit_code
    output=$(cd "$sandbox" && git commit -q -m "entrypoint smoke" 2>&1) || exit_code=$?
    exit_code="${exit_code:-0}"

    if [[ "$exit_code" -ne 0 ]]; then
        fail "$name" "git commit exited $exit_code
--- output ---
$output
--- end ---"
        return
    fi

    if [[ -f "$marker_file" ]]; then
        pass "$name"
        rm -f "$marker_file"
    else
        fail "$name" "marker not created; extensionless hook did not invoke pre-commit.ps1"
    fi
}

test_precommit_fast_path_removes_ignored_artifacts() {
    local name="pre-commit no-staged fast path removes ignored hook artifacts"
    local sandbox="$TEMPDIR/precommit-artifact-cleanup"
    mkdir -p "$sandbox/.githooks"

    git -C "$sandbox" init -q
    git -C "$sandbox" config user.email test@example.com
    git -C "$sandbox" config user.name "Test User"
    git -C "$sandbox" config commit.gpgsign false
    git -C "$sandbox" config core.hooksPath .githooks

    cp "$REPO_ROOT/.githooks/pre-commit" "$sandbox/.githooks/pre-commit"
    chmod +x "$sandbox/.githooks/pre-commit"

cat > "$sandbox/.gitignore" <<'EOF'
*.log
pre-commit.txt
.githooks/pre-commit.txt
EOF
    git -C "$sandbox" add .gitignore
    git -C "$sandbox" commit -q --no-verify -m "baseline"

    printf 'stale root artifact\n' > "$sandbox/pre-commit.txt"
    printf 'stale hook artifact\n' > "$sandbox/.githooks/pre-commit.txt"
    printf 'local diagnostic log\n' > "$sandbox/pre-commit.log"

    local output exit_code=0
    output=$(cd "$sandbox" && .githooks/pre-commit 2>&1) || exit_code=$?
    if [[ $exit_code -ne 0 ]]; then
        fail "$name" "pre-commit exited $exit_code
--- output ---
$output
--- end ---"
        return
    fi

    if [[ -e "$sandbox/pre-commit.txt" || -e "$sandbox/.githooks/pre-commit.txt" ]]; then
        fail "$name" "ignored hook artifacts were not removed
--- output ---
$output
--- end ---"
        return
    fi

    if [[ ! -e "$sandbox/pre-commit.log" ]]; then
        fail "$name" "root pre-commit.log should be preserved because root .log files are not hook-owned artifacts
--- output ---
$output
--- end ---"
        return
    fi

    if grep -q 'No staged files to check' <<<"$output"; then
        pass "$name"
    else
        fail "$name" "missing no-staged fast-path message
--- output ---
$output
--- end ---"
    fi
}

# -----------------------------------------------------------------------------
# Test (P1-5): pre-merge-commit delegates to pre-commit for auto-created
# merge commits.
#
# pre-merge-commit fires when `git merge` auto-creates a merge commit (no
# conflicts to resolve). Round 1 introduced .githooks/pre-merge-commit to
# `exec` the pre-commit hook on that path. Without this, a merge that
# introduces new content (via a new skill file in the merged-in branch)
# bypasses pre-commit entirely — the exact failure mode that produced the
# PWS001 incident on 2026-04-19.
#
# This test verifies delegation by replacing pre-commit with a STUB that
# writes a marker file and exits 0. If the marker exists after `git merge`,
# the delegation chain worked.
# -----------------------------------------------------------------------------
test_premergecommit_delegates_to_precommit() {
    local name="pre-merge-commit delegates to pre-commit (P1-5)"

    if [[ ! -x "$REPO_ROOT/.githooks/pre-merge-commit" ]]; then
        skip "$name" "no .githooks/pre-merge-commit"
        return
    fi

    local sandbox="$TEMPDIR/premerge-delegation"
    rm -rf "$sandbox"
    mkdir -p "$sandbox"

    # Init repo, configure committer identity. -b main requires modern git,
    # fall back to checkout if needed.
    if ! git -C "$sandbox" init -q -b main 2>/dev/null; then
        git -C "$sandbox" init -q
        git -C "$sandbox" checkout -q -b main 2>/dev/null || true
    fi
    git -C "$sandbox" config user.email "test@wallstopstudios.com"
    git -C "$sandbox" config user.name "test"
    git -C "$sandbox" config commit.gpgsign false

    mkdir -p "$sandbox/.githooks"

    # Stub pre-commit: writes a unique marker file tied to the sandbox path
    # (using $$) and exits 0. Making it unique lets parallel test runs not
    # stomp on each other.
    local marker_file="$TEMPDIR/pre-commit-marker-$$"
    # Pre-nuke in case of a stale marker from a previous in-session run.
    rm -f "$marker_file"
    cat > "$sandbox/.githooks/pre-commit.ps1" <<EOF
#!/usr/bin/env pwsh
New-Item -ItemType File -Path '$marker_file' -Force | Out-Null
exit 0
EOF
    chmod +x "$sandbox/.githooks/pre-commit.ps1"

    # Install the REAL pre-merge-commit hook and its PowerShell implementation.
    cp "$REPO_ROOT/.githooks/pre-merge-commit" "$sandbox/.githooks/pre-merge-commit"
    cp "$REPO_ROOT/.githooks/pre-merge-commit.ps1" "$sandbox/.githooks/pre-merge-commit.ps1"
    chmod +x "$sandbox/.githooks/pre-merge-commit" "$sandbox/.githooks/pre-merge-commit.ps1"
    git -C "$sandbox" config core.hooksPath .githooks

    # Create initial commit on main so we have something to branch from.
    echo "root" > "$sandbox/README.md"
    git -C "$sandbox" add README.md
    # Bypass the stub for the seed commit (we only care about merge behavior).
    git -C "$sandbox" commit -q --no-verify -m "root"

    # Create a feature branch and add a non-conflicting file.
    git -C "$sandbox" checkout -q -b feature
    echo "feature" > "$sandbox/feature.txt"
    git -C "$sandbox" add feature.txt
    git -C "$sandbox" commit -q --no-verify -m "feature"

    # Return to main and add a different non-conflicting file so the merge
    # is non-fast-forward (forces a real merge commit → pre-merge-commit
    # fires). A fast-forward merge would BYPASS the hook, which is a
    # known-limitation documented in the hook header.
    git -C "$sandbox" checkout -q main
    echo "main change" > "$sandbox/main.txt"
    git -C "$sandbox" add main.txt
    git -C "$sandbox" commit -q --no-verify -m "main change"

    # Now merge feature into main. Because both sides advanced, git must
    # create a merge commit, and pre-merge-commit should fire.
    # --no-ff guarantees a merge commit even on otherwise fast-forwardable
    # histories (paranoid defense).
    local merge_output merge_exit
    merge_output=$(cd "$sandbox" && git merge --no-ff -m "merge feature" feature 2>&1) || merge_exit=$?
    merge_exit="${merge_exit:-0}"

    if [[ -f "$marker_file" ]]; then
        pass "$name"
        rm -f "$marker_file"
    else
        fail "$name" "marker not created → pre-merge-commit did not delegate to pre-commit
merge_exit=$merge_exit
--- merge output ---
$merge_output
--- end ---"
    fi
}

test_precommit_refuses_partial_final_newline_before_write() {
    local name="pre-commit refuses partial final-newline fix before write"
    local sandbox="$TEMPDIR/precommit-partial-final-newline"
    mkdir -p "$sandbox/.githooks" "$sandbox/scripts"

    cp "$REPO_ROOT/.githooks/pre-commit.ps1" "$sandbox/.githooks/pre-commit.ps1"
    cp "$REPO_ROOT/scripts/git-staging-helpers.ps1" "$sandbox/scripts/git-staging-helpers.ps1"
    cp "$REPO_ROOT/scripts/normalize-eol.ps1" "$sandbox/scripts/normalize-eol.ps1"

    git -C "$sandbox" init -q
    git -C "$sandbox" config user.email test@example.com
    git -C "$sandbox" config user.name "Test User"

    printf 'line' > "$sandbox/README.md"
    git -C "$sandbox" add README.md
    printf 'line modified' > "$sandbox/README.md"
    cp "$sandbox/README.md" "$TEMPDIR/precommit-partial-before"

    local output exit_code
    output=$(cd "$sandbox" && pwsh -NoProfile -File .githooks/pre-commit.ps1 2>&1) || exit_code=$?
    exit_code="${exit_code:-0}"

    if [[ "$exit_code" -eq 0 ]]; then
        fail "$name" "pre-commit unexpectedly succeeded"
        return
    fi

    if ! grep -q 'Refusing to auto-stage whole file(s) with pre-existing unstaged changes before final newline normalization' <<<"$output"; then
        fail "$name" "missing refusal diagnostic
--- output ---
$output
--- end ---"
        return
    fi

    if git diff --no-index --quiet "$sandbox/README.md" "$TEMPDIR/precommit-partial-before"; then
        pass "$name"
    else
        fail "$name" "README.md changed even though the hook refused before auto-fix"
    fi
}

test_precommit_checks_staged_csharp_blob() {
    local name="pre-commit checks staged C# blob for regions"
    local sandbox="$TEMPDIR/precommit-staged-csharp-blob"
    mkdir -p "$sandbox/.githooks" "$sandbox/scripts"

    cp "$REPO_ROOT/.githooks/pre-commit.ps1" "$sandbox/.githooks/pre-commit.ps1"
    cp "$REPO_ROOT/scripts/git-staging-helpers.ps1" "$sandbox/scripts/git-staging-helpers.ps1"
    cp "$REPO_ROOT/scripts/normalize-eol.ps1" "$sandbox/scripts/normalize-eol.ps1"

    git -C "$sandbox" init -q
    git -C "$sandbox" config user.email test@example.com
    git -C "$sandbox" config user.name "Test User"

    printf 'public sealed class Loose\r\n{\r\n#region Bad\r\n#endregion\r\n}\r\n' > "$sandbox/Loose.cs"
    git -C "$sandbox" add Loose.cs

    printf 'public sealed class Loose\r\n{\r\n}\r\n' > "$sandbox/Loose.cs"

    local output exit_code
    output=$(cd "$sandbox" && pwsh -NoProfile -File .githooks/pre-commit.ps1 2>&1) || exit_code=$?
    exit_code="${exit_code:-0}"

    if [[ "$exit_code" -eq 0 ]]; then
        fail "$name" "pre-commit passed while the staged blob still contained #region"
        return
    fi

    if grep -q 'Forbidden #region/#endregion directives detected' <<<"$output"; then
        pass "$name"
    else
        fail "$name" "missing staged-blob region diagnostic
--- output ---
$output
--- end ---"
    fi
}

# -----------------------------------------------------------------------------
# Guard: the anti-pattern lint itself passes on the repo.
# If THIS fails, there is a lingering -- argv form somewhere in the codebase.
# -----------------------------------------------------------------------------
test_antipattern_lint_clean() {
    local name="lint-pwsh-invocations.ps1 is clean on the repo"
    if [[ ! -f "$REPO_ROOT/scripts/lint-pwsh-invocations.ps1" ]]; then
        skip "$name" "anti-pattern lint not present"; return
    fi
    if pwsh -NoProfile -File "$REPO_ROOT/scripts/lint-pwsh-invocations.ps1" >/dev/null 2>&1; then
        pass "$name"
    else
        local out
        out=$(pwsh -NoProfile -File "$REPO_ROOT/scripts/lint-pwsh-invocations.ps1" 2>&1 || true)
        fail "$name" "$out"
    fi
}

echo "=== Pre-commit integration tests ==="
echo "Repo root: $REPO_ROOT"
echo "Tempdir:   $TEMPDIR"
echo ""

test_dependabot_branch
test_original_failing_command
test_yaml_lint_invocation
test_skill_sizes_branch
test_lint_tests_branch
test_format_staged_csharp_branch
test_drawer_branch
test_duplicate_usings_branch
test_sync_scripts_branch
test_antipattern_lint_clean
test_precommit_spellcheck_regression
test_precommit_entrypoint_delegates_to_ps1
test_precommit_fast_path_removes_ignored_artifacts
test_premergecommit_delegates_to_precommit
test_precommit_refuses_partial_final_newline_before_write
test_precommit_checks_staged_csharp_blob

echo ""
echo "=== Summary ==="
echo "Passed:  $tests_passed"
echo "Failed:  $tests_failed"
echo "Skipped: $tests_skipped"

if [[ $tests_failed -gt 0 ]]; then
    exit 1
fi
# Safety net: if every test got skipped, that's not a pass — it means the
# harness has no way to actually exercise the invocation path. Fail loud.
if [[ $tests_passed -eq 0 ]]; then
    echo -e "${RED}[FAIL]${NC} No tests ran (all skipped). Something is wrong with the harness."
    exit 3
fi
exit 0
