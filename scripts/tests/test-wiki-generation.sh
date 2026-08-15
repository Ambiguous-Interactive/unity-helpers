#!/usr/bin/env bash
# Test script for wiki generation sidebar link format
# Ensures we use Markdown link syntax [Display](Page) instead of MediaWiki [[Page|Display]]
#
# Run: bash scripts/tests/test-wiki-generation.sh
# Exit codes: 0 = all tests pass, 1 = test failure

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

# Test helper functions
pass() {
    tests_passed=$((tests_passed + 1))
    echo -e "${GREEN}✓${NC} $1"
}

fail() {
    tests_failed=$((tests_failed + 1))
    echo -e "${RED}✗${NC} $1"
    echo -e "  ${RED}Expected:${NC} $2"
    echo -e "  ${RED}Actual:${NC}   $3"
}

run_test() {
    tests_run=$((tests_run + 1))
}

# =============================================================================
# get_display_name function (EXACT copy from deploy-wiki.yml for testing)
# IMPORTANT: This MUST match the implementation in deploy-wiki.yml exactly.
# If you modify this, also update deploy-wiki.yml and vice versa.
#
# NOTE: The sed alternation only removes the FIRST matching prefix.
# This matches actual file naming conventions - files never have nested
# category prefixes like "Overview-Features-*" or "Features-Guides-*".
# =============================================================================
get_display_name() {
    local wiki_name="$1"
    local display="$wiki_name"

    # Remove top-level category prefix (Features, Overview, Performance, Guides, Project)
    # Using sed for extended regex alternation which can't be done with bash substitution
    # shellcheck disable=SC2001
    display=$(echo "$display" | sed -E 's/^(Features|Overview|Performance|Guides|Project)-//')

    # Remove subcategory prefix only if it matches known subcategories
    # This preserves context for files like "Utilities-Data-Structures" -> "Data Structures"
    # shellcheck disable=SC2001
    display=$(echo "$display" | sed -E 's/^(Inspector|Effects|Relational-Components|Serialization|Spatial|Logging|Utilities|Editor-Tools)-//')

    # Convert remaining dashes to spaces
    display="${display//-/ }"
    echo "$display"
}

# =============================================================================
# Test: get_display_name produces correct output
# =============================================================================
echo ""
echo "=== Testing get_display_name function ==="

run_test
result=$(get_display_name "Overview-Getting-Started")
if [ "$result" = "Getting Started" ]; then
    pass "Overview-Getting-Started -> 'Getting Started'"
else
    fail "Overview-Getting-Started -> 'Getting Started'" "Getting Started" "$result"
fi

run_test
result=$(get_display_name "Features-Inspector-Buttons")
if [ "$result" = "Buttons" ]; then
    pass "Features-Inspector-Buttons -> 'Buttons'"
else
    fail "Features-Inspector-Buttons -> 'Buttons'" "Buttons" "$result"
fi

run_test
result=$(get_display_name "Features-Relational-Components-Overview")
if [ "$result" = "Overview" ]; then
    pass "Features-Relational-Components-Overview -> 'Overview'"
else
    fail "Features-Relational-Components-Overview -> 'Overview'" "Overview" "$result"
fi

run_test
result=$(get_display_name "Guides-Odin-Migration")
if [ "$result" = "Odin Migration" ]; then
    pass "Guides-Odin-Migration -> 'Odin Migration'"
else
    fail "Guides-Odin-Migration -> 'Odin Migration'" "Odin Migration" "$result"
fi

run_test
result=$(get_display_name "Performance-Benchmarks")
if [ "$result" = "Benchmarks" ]; then
    pass "Performance-Benchmarks -> 'Benchmarks'"
else
    fail "Performance-Benchmarks -> 'Benchmarks'" "Benchmarks" "$result"
fi

run_test
result=$(get_display_name "Features-Utilities-Data-Structures")
if [ "$result" = "Data Structures" ]; then
    pass "Features-Utilities-Data-Structures -> 'Data Structures'"
else
    fail "Features-Utilities-Data-Structures -> 'Data Structures'" "Data Structures" "$result"
fi

# =============================================================================
# Test: Markdown link format is correct
# =============================================================================
echo ""
echo "=== Testing Markdown link format ==="

# Generate a link using the correct format
generate_sidebar_link() {
    local wiki_name="$1"
    local display
    display=$(get_display_name "$wiki_name")
    echo "- [$display]($wiki_name)"
}

run_test
result=$(generate_sidebar_link "Overview-Getting-Started")
expected="- [Getting Started](Overview-Getting-Started)"
if [ "$result" = "$expected" ]; then
    pass "Generates correct Markdown link format"
else
    fail "Generates correct Markdown link format" "$expected" "$result"
fi

run_test
result=$(generate_sidebar_link "Features-Inspector-Buttons")
expected="- [Buttons](Features-Inspector-Buttons)"
if [ "$result" = "$expected" ]; then
    pass "Generates correct Markdown link for nested categories"
else
    fail "Generates correct Markdown link for nested categories" "$expected" "$result"
fi

# =============================================================================
# Test: MediaWiki syntax is NOT used (regression prevention)
# =============================================================================
echo ""
echo "=== Testing MediaWiki syntax is NOT present ==="

run_test
result=$(generate_sidebar_link "Overview-Getting-Started")
if [[ "$result" != *"[["* ]] && [[ "$result" != *"]]"* ]]; then
    pass "No MediaWiki [[ brackets in output"
else
    fail "No MediaWiki [[ brackets in output" "No [[ or ]]" "$result"
fi

run_test
# MediaWiki format would be [[Page|Display]] - pipe inside brackets
if [[ "$result" != *"[[*|*]]"* ]]; then
    pass "No MediaWiki pipe syntax in output"
else
    fail "No MediaWiki pipe syntax in output" "No [[...|...]] pattern" "$result"
fi

# =============================================================================
# Test: Wiki link transformation (from deploy-wiki.yml)
# Tests link transformation that converts relative doc links to wiki page
# references (e.g., ./docs/overview/roadmap.md -> Overview-Roadmap).
# Note: Production uses Python (scripts/wiki/transform_wiki_links.py).
# This Perl function is a reference implementation for bash-level testing.
# =============================================================================
echo ""
echo "=== Testing wiki link transformation ==="

# This Perl function is a reference implementation for bash testing.
# Production uses Python: scripts/wiki/transform_wiki_links.py
transform_wiki_link() {
    local input="$1"
    echo "$input" | perl -pe '
        s{\(\.\.?/([^)]+)\.md\)}{
            my $path = $1;
            # Remove docs/ prefix if present
            $path =~ s{^docs/}{};
            # Replace all / with -
            $path =~ s{/}{-}g;
            # Capitalize each segment (after ^ or -)
            $path =~ s{(^|-)(\w)}{$1\u$2}g;
            # Special case: README -> Home (wiki convention)
            $path =~ s{^README$}{Home};
            "($path)"
        }ge;
    '
}

run_test
result=$(transform_wiki_link "(./docs/overview/roadmap.md)")
expected="(Overview-Roadmap)"
if [ "$result" = "$expected" ]; then
    pass "Transform: ./docs/overview/roadmap.md -> Overview-Roadmap"
else
    fail "Transform: ./docs/overview/roadmap.md -> Overview-Roadmap" "$expected" "$result"
fi

run_test
result=$(transform_wiki_link "(./docs/guides/odin-migration-guide.md)")
expected="(Guides-Odin-Migration-Guide)"
if [ "$result" = "$expected" ]; then
    pass "Transform: ./docs/guides/odin-migration-guide.md -> Guides-Odin-Migration-Guide"
else
    fail "Transform: ./docs/guides/odin-migration-guide.md -> Guides-Odin-Migration-Guide" "$expected" "$result"
fi

run_test
result=$(transform_wiki_link "(./docs/features/inspector/inspector-overview.md)")
expected="(Features-Inspector-Inspector-Overview)"
if [ "$result" = "$expected" ]; then
    pass "Transform: ./docs/features/inspector/inspector-overview.md -> Features-Inspector-Inspector-Overview"
else
    fail "Transform: ./docs/features/inspector/inspector-overview.md -> Features-Inspector-Inspector-Overview" "$expected" "$result"
fi

run_test
result=$(transform_wiki_link "(../README.md)")
expected="(Home)"
if [ "$result" = "$expected" ]; then
    pass "Transform: ../README.md -> Home (special case)"
else
    fail "Transform: ../README.md -> Home (special case)" "$expected" "$result"
fi

run_test
# Test full markdown link syntax preservation
result=$(transform_wiki_link "[Roadmap](./docs/overview/roadmap.md)")
expected="[Roadmap](Overview-Roadmap)"
if [ "$result" = "$expected" ]; then
    pass "Transform preserves markdown link display text"
else
    fail "Transform preserves markdown link display text" "$expected" "$result"
fi

run_test
# Test link in context
result=$(transform_wiki_link "See the [Roadmap](./docs/overview/roadmap.md) for details")
expected="See the [Roadmap](Overview-Roadmap) for details"
if [ "$result" = "$expected" ]; then
    pass "Transform works in sentence context"
else
    fail "Transform works in sentence context" "$expected" "$result"
fi

run_test
# Test multiple links on same line
result=$(transform_wiki_link "[A](./docs/overview/a.md) and [B](./docs/features/b.md)")
expected="[A](Overview-A) and [B](Features-B)"
if [ "$result" = "$expected" ]; then
    pass "Transform handles multiple links on same line"
else
    fail "Transform handles multiple links on same line" "$expected" "$result"
fi

# =============================================================================
# Test: Validation regex captures correct patterns
# =============================================================================
echo ""
echo "=== Testing validation regex ==="

# The validation regex from deploy-wiki.yml
validate_sidebar_link() {
    local line="$1"
    # Extract page name from Markdown link [Display](Page)
    echo "$line" | grep -oE '\]\([A-Za-z0-9_.-]+\)' | sed 's/\](//;s/)//'
}

run_test
result=$(validate_sidebar_link "- [Getting Started](Overview-Getting-Started)")
if [ "$result" = "Overview-Getting-Started" ]; then
    pass "Regex extracts page name from Markdown link"
else
    fail "Regex extracts page name from Markdown link" "Overview-Getting-Started" "$result"
fi

run_test
result=$(validate_sidebar_link "- [Data_Structures](Features-Utilities-Data_Structures)")
if [ "$result" = "Features-Utilities-Data_Structures" ]; then
    pass "Regex handles underscores in page names"
else
    fail "Regex handles underscores in page names" "Features-Utilities-Data_Structures" "$result"
fi

run_test
result=$(validate_sidebar_link "- [Version 2.0](Release-Notes-2.0)")
if [ "$result" = "Release-Notes-2.0" ]; then
    pass "Regex handles periods in page names"
else
    fail "Regex handles periods in page names" "Release-Notes-2.0" "$result"
fi

# Negative test - should NOT match MediaWiki syntax
run_test
result=$(echo "- [[Overview-Getting-Started|Getting Started]]" | grep -oE '\]\([A-Za-z0-9_.-]+\)' | sed 's/\](//;s/)//' || echo "")
if [ -z "$result" ]; then
    pass "Regex does NOT match MediaWiki syntax (regression prevention)"
else
    fail "Regex does NOT match MediaWiki syntax" "(empty)" "$result"
fi

# =============================================================================
# Test: Python sidebar generator uses correct Markdown syntax
# Note: The workflow now uses Python scripts (scripts/wiki/generate_wiki_sidebar.py)
# instead of inline bash echo statements. We test the Python script's output.
# =============================================================================
echo ""
echo "=== Testing generate_wiki_sidebar.py syntax ==="

SIDEBAR_SCRIPT="scripts/wiki/generate_wiki_sidebar.py"

if [ -f "$SIDEBAR_SCRIPT" ]; then
    run_test
    # Check that Python script generates Markdown syntax for Home link: - [Home](Home)
    if grep -qE '^[[:space:]]*"- \[Home\]\(Home\)"' "$SIDEBAR_SCRIPT" || grep -qE "'\- \[Home\]\(Home\)'" "$SIDEBAR_SCRIPT" || grep -qE 'f"- \[Home\]\(Home\)"' "$SIDEBAR_SCRIPT" || grep -qE "lines\.append\(f\"- \[" "$SIDEBAR_SCRIPT"; then
        pass "Workflow uses Markdown syntax for Home link"
    else
        fail "Workflow uses Markdown syntax for Home link" 'echo "- [Home](Home)"' "(not found)"
    fi

    run_test
    # Check that Python script generates dynamic links with Markdown syntax: f"- [{display}]({wiki_name})"
    if grep -qE 'f"- \[\{display\}\]\(\{wiki_name\}\)"' "$SIDEBAR_SCRIPT" || grep -qE "f\"- \[.*\]\(.*\)\"" "$SIDEBAR_SCRIPT"; then
        pass "Workflow uses Markdown syntax for dynamic links"
    else
        fail "Workflow uses Markdown syntax for dynamic links" 'echo "- [$display]($wiki_name)"' "(not found)"
    fi

    run_test
    # Check that Python script generates Markdown syntax for CHANGELOG link: - [Changelog](CHANGELOG)
    # The string appears in a list inside lines.extend() or similar
    if grep -q '\- \[Changelog\](CHANGELOG)' "$SIDEBAR_SCRIPT"; then
        pass "Workflow uses Markdown syntax for CHANGELOG link"
    else
        fail "Workflow uses Markdown syntax for CHANGELOG link" 'echo "- [Changelog](CHANGELOG)"' "(not found)"
    fi

    run_test
    # Regression test: MediaWiki syntax should NOT be used in Python script
    mediawiki_links=$(grep -E '\[\[.*\]\]' "$SIDEBAR_SCRIPT" 2>/dev/null | grep -v '^#' || echo "")
    if [ -z "$mediawiki_links" ]; then
        pass "No MediaWiki [[...]] syntax in echo statements (regression prevention)"
    else
        fail "No MediaWiki [[...]] syntax in echo statements" "(none)" "$mediawiki_links"
    fi

else
    echo -e "${YELLOW}⚠${NC} Python script not found: $SIDEBAR_SCRIPT (skipping script tests)"
fi

# =============================================================================
# Test: the workflow that extracts wiki page names accepts underscores in them
#
# Discovered rather than named (#445). This used to read `.github/workflows/deploy-wiki.yml` by
# path, with a fallback that checked THIS FILE for the same regex when the workflow was absent --
# and this file contains that regex in its own fixtures, so the fallback could not fail. The
# assertion passed whether or not any workflow validated anything.
#
# It also used to sit inside the `generate_wiki_sidebar.py` branch above, so deleting that Python
# script would have silently stopped checking the workflow too. It is its own test now.
# =============================================================================
echo ""
echo "=== Testing wiki page-name extraction regex ==="

run_test
sidebar_workflows=$(grep -rl '_Sidebar\.md' .github/workflows/ 2>/dev/null || true)
if [ -z "$sidebar_workflows" ]; then
    fail "Page-name extraction regex accepts underscores" \
        "a workflow that reads _Sidebar.md" "(no workflow does)"
else
    found_classes=0
    narrow_classes=""
    for wf in $sidebar_workflows; do
        for page_class in $(grep -oE '\\\]\\\(\[[^]]+\]\+\\\)' "$wf" || true); do
            found_classes=$((found_classes + 1))
            case "$page_class" in
                *_*) ;;
                *) narrow_classes="$narrow_classes $wf:$page_class" ;;
            esac
        done
    done
    if [ "$found_classes" -eq 0 ]; then
        # Without this the test passes vacuously the moment the extraction is rewritten in a shape
        # the pattern above does not recognize, which is the failure this rewrite exists to close.
        fail "Page-name extraction regex accepts underscores" \
            "at least one page-name character class" \
            "(none found in: $sidebar_workflows)"
    elif [ -n "$narrow_classes" ]; then
        fail "Page-name extraction regex accepts underscores" \
            "every class to contain _" "$narrow_classes"
    else
        pass "Page-name extraction regex accepts underscores ($found_classes class(es) checked)"
    fi
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
