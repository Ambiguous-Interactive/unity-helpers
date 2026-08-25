#!/usr/bin/env bash
# =============================================================================
# Self-test for scripts/validate-github-pages-css.sh
# =============================================================================
# The validator scans one CSS file for a fixed list of required layout,
# breakpoint and accessibility rules. Run against the repository's own theme.css
# it prints "VALIDATION PASSED", which is evidence about theme.css and no
# evidence at all about whether the validator still reports (#556, #562).
#
# It already takes the CSS path as $1, so no production change was needed here.
#
# Green half:
#   - the repository's real theme.css passes
#   - a minimal fixture carrying every REQUIRED rule passes, even though it
#     trips several of the warning-only checks -- warnings must not fail a run
#
# Red halves, one per rule that increments ERRORS, each asserted on its own
# message so a fixture that trips a neighbouring rule cannot read as covering
# the one it is named for:
#   - a missing CSS file
#   - unbalanced braces
#   - an unclosed comment
#   - each of the ten required layout / breakpoint / accessibility rules
# =============================================================================

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
VALIDATOR="$REPO_ROOT/scripts/validate-github-pages-css.sh"
REAL_CSS="$REPO_ROOT/assets/css/theme.css"

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

# Every required rule lives on its own line and carries a unique OMIT-<token>
# marker in a comment, so a red half is "the base file minus one line" rather
# than a second hand-maintained copy that can drift from it.
write_base_css() {
    local target="$1"
    cat > "$target" <<'CSS'
:root {
    --page-background: #ffffff;
}
.wrapper {
    max-width: 1400px; /* OMIT-wrapper-max-width */
}
header {
    float: none; /* OMIT-header-float */
}
section {
    float: none; /* OMIT-section-float */
    max-width: 1200px; /* OMIT-section-max-width */
}
pre {
    overflow-x: auto; /* OMIT-pre-overflow */
}
a:focus { /* OMIT-focus */
    outline: 2px solid #000000;
}
@media (max-width: 768px) { /* OMIT-media-768 */
    section {
        padding: 0;
    }
}
@media (max-width: 480px) { /* OMIT-media-480 */
    section {
        padding: 0;
    }
}
@media (min-width: 1400px) { /* OMIT-media-1400 */
    section {
        padding: 0;
    }
}
@media (min-width: 2000px) { /* OMIT-media-2000 */
    section {
        padding: 0;
    }
}
@media (prefers-reduced-motion: reduce) { /* OMIT-reduced-motion */
    * {
        animation: none;
    }
}
CSS
}

# Removing a whole line keeps braces balanced only for the single-property
# lines; the media queries open a block, so their marker line is rewritten to a
# different breakpoint rather than deleted.
write_css_without() {
    local target="$1"
    local marker="$2"
    write_base_css "$target"
    case "$marker" in
        media-*)
            sed -i "s/^@media (\(max\|min\)-width: [0-9]*px) { \/\* OMIT-$marker \*\//@media (min-width: 1px) {/" "$target"
            ;;
        reduced-motion)
            sed -i "s/^@media (prefers-reduced-motion: reduce) { \/\* OMIT-$marker \*\//@media (min-width: 3px) {/" "$target"
            ;;
        focus)
            sed -i "s/^a:focus { \/\* OMIT-$marker \*\//a.plain {/" "$target"
            ;;
        *)
            sed -i "/OMIT-$marker/d" "$target"
            ;;
    esac
}

run_validator() {
    local css="$1"
    VALIDATOR_OUTPUT="$(bash "$VALIDATOR" "$css" 2>&1)"
    VALIDATOR_EXIT=$?
    return 0
}

expect_pass() {
    local name="$1"
    local css="$2"
    run_validator "$css"
    if [ "$VALIDATOR_EXIT" -ne 0 ]; then
        fail "$name" "validator rejected a file it must accept (exit $VALIDATOR_EXIT): $VALIDATOR_OUTPUT"
        return
    fi
    pass "$name"
}

expect_fail() {
    local name="$1"
    local css="$2"
    local expected="$3"
    run_validator "$css"
    if [ "$VALIDATOR_EXIT" -eq 0 ]; then
        fail "$name" "validator accepted a file it must reject"
        return
    fi
    if ! printf '%s' "$VALIDATOR_OUTPUT" | grep -qF -- "$expected"; then
        fail "$name" "rejected, but not for the reason under test. Expected to contain '$expected'. Got: $VALIDATOR_OUTPUT"
        return
    fi
    pass "$name"
}

echo ""
echo "Running validate-github-pages-css.sh self-tests"
echo ""

# ── Green half ───────────────────────────────────────────────────────────────
expect_pass "the repository theme.css passes" "$REAL_CSS"

write_base_css "$WORKSPACE/base.css"
expect_pass "a minimal fixture with every required rule passes" "$WORKSPACE/base.css"

# The minimal fixture has no vendor prefixes, no data-theme selectors and no
# print styles, so it must produce warnings. A run with zero warnings would mean
# the warning checks stopped looking, which the exit code alone cannot show.
run_validator "$WORKSPACE/base.css"
if printf '%s' "$VALIDATOR_OUTPUT" | grep -q "warning(s)"; then
    pass "warnings alone do not fail a run"
else
    fail "warnings alone do not fail a run" "expected warnings from the minimal fixture, got: $VALIDATOR_OUTPUT"
fi

# ── Red halves ───────────────────────────────────────────────────────────────
expect_fail "a missing CSS file is rejected" "$WORKSPACE/absent.css" "CSS file not found"

write_base_css "$WORKSPACE/unbalanced.css"
printf '\nbody {\n    color: red;\n' >> "$WORKSPACE/unbalanced.css"
expect_fail "unbalanced braces are rejected" "$WORKSPACE/unbalanced.css" "Unbalanced braces"

write_base_css "$WORKSPACE/unclosed-comment.css"
printf '\n/* this comment never closes\nbody { color: red; }\n' >> "$WORKSPACE/unclosed-comment.css"
expect_fail "an unclosed comment is rejected" "$WORKSPACE/unclosed-comment.css" "Unclosed comments"

# marker -> the validator's own description for the rule it removes.
REQUIRED_RULES=(
    "wrapper-max-width|MISSING: .wrapper max-width: 1400px"
    "header-float|MISSING: header float: none (banner layout)"
    "section-float|MISSING: section float: none"
    "section-max-width|MISSING: section max-width: 1200px"
    "media-768|MISSING: @media max-width: 768px breakpoint"
    "media-480|MISSING: @media max-width: 480px breakpoint"
    "media-1400|MISSING: @media min-width: 1400px breakpoint"
    "media-2000|MISSING: @media min-width: 2000px breakpoint"
    "pre-overflow|MISSING: pre overflow-x: auto"
    "reduced-motion|MISSING: @media prefers-reduced-motion"
    "focus|MISSING: Focus styles (:focus or :focus-visible)"
)

for entry in "${REQUIRED_RULES[@]}"; do
    marker="${entry%%|*}"
    expected="${entry#*|}"
    css="$WORKSPACE/without-$marker.css"
    write_css_without "$css" "$marker"
    expect_fail "removing $marker is rejected" "$css" "$expected"
done

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
