#!/usr/bin/env bash
# Validates GitHub Pages CSS for syntax correctness and required layout rules.
# Exit codes: 0 = success, 1 = validation errors found
#
# Usage: ./scripts/validate-github-pages-css.sh [CSS_FILE]
# Default CSS file: assets/css/theme.css

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Use explicit grep path to avoid ripgrep aliases that have different syntax
# Fall back to grep if /usr/bin/grep doesn't exist
if [ -x /usr/bin/grep ]; then
    GREP=/usr/bin/grep
else
    GREP="grep"
fi

# Script location and default CSS file
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CSS_FILE="${1:-$REPO_ROOT/assets/css/theme.css}"

# Counters
ERRORS=0
WARNINGS=0

echo "========================================"
echo "GitHub Pages CSS Validator"
echo "========================================"
echo ""
printf "CSS File: ${BLUE}%s${NC}\n" "$CSS_FILE"
echo ""

# Check if CSS file exists
if [ ! -f "$CSS_FILE" ]; then
    printf "${RED}ERROR: CSS file not found: %s${NC}\n" "$CSS_FILE"
    exit 1
fi

# ============================================================================
# SECTION 1: CSS Syntax Validation
# ============================================================================

echo "----------------------------------------"
echo "1. CSS Syntax Validation"
echo "----------------------------------------"

# Check for balanced braces
check_balanced_braces() {
    local open_braces
    local close_braces
    open_braces=$($GREP -o '{' "$CSS_FILE" | wc -l)
    close_braces=$($GREP -o '}' "$CSS_FILE" | wc -l)

    if [ "$open_braces" -eq "$close_braces" ]; then
        printf "  ${GREEN}✓${NC} Balanced braces: %d open, %d close\n" "$open_braces" "$close_braces"
        return 0
    else
        printf "  ${RED}✗${NC} Unbalanced braces: %d open, %d close\n" "$open_braces" "$close_braces"
        ERRORS=$((ERRORS + 1))
        return 1
    fi
}

# Check for unclosed comments
check_unclosed_comments() {
    local open_comments
    local close_comments
    open_comments=$($GREP -o '/\*' "$CSS_FILE" | wc -l)
    close_comments=$($GREP -o '\*/' "$CSS_FILE" | wc -l)

    if [ "$open_comments" -eq "$close_comments" ]; then
        printf "  ${GREEN}✓${NC} Balanced comments: %d open, %d close\n" "$open_comments" "$close_comments"
        return 0
    else
        printf "  ${RED}✗${NC} Unclosed comments: %d open, %d close\n" "$open_comments" "$close_comments"
        ERRORS=$((ERRORS + 1))
        return 1
    fi
}

# Check for empty rule blocks (potential errors)
check_empty_rules() {
    # Look for patterns like "selector { }" with only whitespace
    if $GREP -E '\{[[:space:]]*\}' "$CSS_FILE" >/dev/null 2>&1; then
        printf "  ${YELLOW}⚠${NC} Empty rule blocks detected (may be intentional)\n"
        WARNINGS=$((WARNINGS + 1))
        return 0
    else
        printf "  ${GREEN}✓${NC} No empty rule blocks\n"
        return 0
    fi
}

# Check for common CSS errors
check_common_errors() {
    # Check for missing semicolons before closing brace (common error pattern)
    # This is a heuristic - looks for property: value } without semicolon
    if $GREP -E '[a-z-]+:[^;{}]+\}' "$CSS_FILE" | $GREP -v -E '^\s*/\*' >/dev/null 2>&1; then
        printf "  ${YELLOW}⚠${NC} Possible missing semicolons detected\n"
        WARNINGS=$((WARNINGS + 1))
    else
        printf "  ${GREEN}✓${NC} No obvious missing semicolons\n"
    fi

    # Check for invalid color values (common typos)
    # Valid hex: #RGB, #RRGGBB, #RGBA, #RRGGBBAA
    if $GREP -E '#[0-9a-fA-F]{1,2}[^0-9a-fA-F;,\s)}]|#[0-9a-fA-F]{5}[^0-9a-fA-F;,\s)}]|#[0-9a-fA-F]{7}[^0-9a-fA-F;,\s)}]' "$CSS_FILE" >/dev/null 2>&1; then
        printf "  ${YELLOW}⚠${NC} Possible invalid hex color values\n"
        WARNINGS=$((WARNINGS + 1))
    else
        printf "  ${GREEN}✓${NC} Hex color values appear valid\n"
    fi

    return 0
}

check_balanced_braces
check_unclosed_comments
check_empty_rules
check_common_errors

echo ""

# ============================================================================
# SECTION 2: Required Layout Rules Verification
# ============================================================================

echo "----------------------------------------"
echo "2. Required Layout Rules"
echo "----------------------------------------"

# Helper function to check for a simple pattern (single line)
check_pattern() {
    local description="$1"
    local pattern="$2"
    local required="${3:-true}"

    if $GREP -E "$pattern" "$CSS_FILE" >/dev/null 2>&1; then
        printf "  ${GREEN}✓${NC} %s\n" "$description"
        return 0
    else
        if [ "$required" = "true" ]; then
            printf "  ${RED}✗${NC} MISSING: %s\n" "$description"
            ERRORS=$((ERRORS + 1))
            return 1
        else
            printf "  ${YELLOW}⚠${NC} Optional: %s\n" "$description"
            WARNINGS=$((WARNINGS + 1))
            return 0
        fi
    fi
}

# Extracts every top-level block opened by a line matching the selector pattern,
# brace-depth counted so a nested at-rule cannot end the block early.
extract_selector_blocks() {
    local selector_pattern="$1"

    awk -v pat="$selector_pattern" '
        !inblock && $0 ~ pat { inblock = 1; depth = 0 }
        inblock {
            print
            opens = gsub(/\{/, "{")
            closes = gsub(/\}/, "}")
            depth += opens - closes
            if (depth <= 0) { inblock = 0 }
        }
    ' "$CSS_FILE"
}

# The property must be INSIDE the selector's own block. Searching the whole file
# for each half independently answers "does this file contain a float: none
# anywhere", which `section { float: none }` satisfies on behalf of `header` --
# the rule then cannot report the removal it exists to catch (#556).
check_css_rule() {
    local description="$1"
    local selector_pattern="$2"
    local property_pattern="$3"
    local required="${4:-true}"

    local has_selector=0
    local has_property=0
    local blocks

    blocks="$(extract_selector_blocks "$selector_pattern")"

    if [ -n "$blocks" ]; then
        has_selector=1
    fi

    if printf '%s\n' "$blocks" | $GREP -E "$property_pattern" >/dev/null 2>&1; then
        has_property=1
    fi

    if [ "$has_selector" -eq 1 ] && [ "$has_property" -eq 1 ]; then
        printf "  ${GREEN}✓${NC} %s\n" "$description"
        return 0
    else
        if [ "$required" = "true" ]; then
            printf "  ${RED}✗${NC} MISSING: %s\n" "$description"
            if [ "$has_selector" -eq 0 ]; then
                printf "      (selector not found)\n"
            fi
            if [ "$has_property" -eq 0 ]; then
                printf "      (property not found)\n"
            fi
            ERRORS=$((ERRORS + 1))
            return 1
        else
            printf "  ${YELLOW}⚠${NC} Optional: %s\n" "$description"
            WARNINGS=$((WARNINGS + 1))
            return 0
        fi
    fi
}

echo ""
echo "  Layout Structure:"

# .wrapper with max-width: 1400px
check_css_rule ".wrapper max-width: 1400px" '^\.wrapper[[:space:]]*\{' 'max-width:[[:space:]]*1400px'

# header with float: none (banner layout)
check_css_rule "header float: none (banner layout)" '^header[[:space:]]*\{' 'float:[[:space:]]*none'

# section with float: none
check_css_rule "section float: none" '^section[[:space:]]*\{' 'float:[[:space:]]*none'

# section with max-width: 1200px
check_css_rule "section max-width: 1200px" '^section[[:space:]]*\{' 'max-width:[[:space:]]*1200px'

echo ""
echo "  Responsive Breakpoints:"

# 768px breakpoint (tablet)
check_pattern "@media max-width: 768px breakpoint" '@media[^{]*max-width:[[:space:]]*768px'

# 480px breakpoint (mobile)
check_pattern "@media max-width: 480px breakpoint" '@media[^{]*max-width:[[:space:]]*480px'

# 1400px breakpoint (large screens)
check_pattern "@media min-width: 1400px breakpoint" '@media[^{]*min-width:[[:space:]]*1400px'

# 2000px breakpoint (ultrawide)
check_pattern "@media min-width: 2000px breakpoint" '@media[^{]*min-width:[[:space:]]*2000px'

echo ""
echo "  Code Block Styling:"

# pre with overflow-x: auto
check_css_rule "pre overflow-x: auto" '^pre[[:space:]]*\{' 'overflow-x:[[:space:]]*auto'

echo ""
echo "  Accessibility Rules:"

# prefers-reduced-motion media query
check_pattern "@media prefers-reduced-motion" '@media[^{]*prefers-reduced-motion'

# Focus styles (focus-visible or :focus)
check_pattern "Focus styles (:focus or :focus-visible)" ':focus-visible|:focus[^-]'

echo ""

# ============================================================================
# SECTION 3: Vendor Prefix Validation
# ============================================================================

echo "----------------------------------------"
echo "3. Vendor Prefix Validation"
echo "----------------------------------------"

# Check for flex display with vendor prefixes
check_flex_prefixes() {
    local has_flex=0
    local has_webkit_flex=0
    local has_ms_flexbox=0

    if $GREP -E 'display:[[:space:]]*flex' "$CSS_FILE" >/dev/null 2>&1; then
        has_flex=1
    fi

    if $GREP -E 'display:[[:space:]]*-webkit-flex' "$CSS_FILE" >/dev/null 2>&1; then
        has_webkit_flex=1
    fi

    if $GREP -E 'display:[[:space:]]*-ms-flexbox' "$CSS_FILE" >/dev/null 2>&1; then
        has_ms_flexbox=1
    fi

    if [ "$has_flex" -eq 1 ]; then
        if [ "$has_webkit_flex" -eq 1 ] && [ "$has_ms_flexbox" -eq 1 ]; then
            printf "  ${GREEN}✓${NC} Flex display has vendor prefixes (-webkit-flex, -ms-flexbox)\n"
            return 0
        elif [ "$has_webkit_flex" -eq 1 ]; then
            printf "  ${YELLOW}⚠${NC} Flex display missing -ms-flexbox prefix\n"
            WARNINGS=$((WARNINGS + 1))
            return 0
        elif [ "$has_ms_flexbox" -eq 1 ]; then
            printf "  ${YELLOW}⚠${NC} Flex display missing -webkit-flex prefix\n"
            WARNINGS=$((WARNINGS + 1))
            return 0
        else
            printf "  ${YELLOW}⚠${NC} Flex display missing vendor prefixes\n"
            WARNINGS=$((WARNINGS + 1))
            return 0
        fi
    else
        printf "  ${GREEN}✓${NC} No flex display rules to check\n"
        return 0
    fi
}

# Check for box-sizing with vendor prefixes
check_box_sizing_prefixes() {
    local has_standard=0
    local has_webkit=0
    local has_moz=0

    if $GREP -E 'box-sizing:[[:space:]]*border-box' "$CSS_FILE" >/dev/null 2>&1; then
        has_standard=1
    fi

    if $GREP -E -- '-webkit-box-sizing:[[:space:]]*border-box' "$CSS_FILE" >/dev/null 2>&1; then
        has_webkit=1
    fi

    if $GREP -E -- '-moz-box-sizing:[[:space:]]*border-box' "$CSS_FILE" >/dev/null 2>&1; then
        has_moz=1
    fi

    if [ "$has_standard" -eq 1 ]; then
        if [ "$has_webkit" -eq 1 ] && [ "$has_moz" -eq 1 ]; then
            printf "  ${GREEN}✓${NC} box-sizing has vendor prefixes (-webkit, -moz)\n"
            return 0
        else
            printf "  ${YELLOW}⚠${NC} box-sizing may need vendor prefixes for older browsers\n"
            WARNINGS=$((WARNINGS + 1))
            return 0
        fi
    else
        printf "  ${GREEN}✓${NC} No box-sizing rules to check\n"
        return 0
    fi
}

# Check for transition with vendor prefixes
check_transition_prefixes() {
    local has_standard=0
    local has_webkit=0

    if $GREP -E '[^-]transition:' "$CSS_FILE" >/dev/null 2>&1; then
        has_standard=1
    fi

    if $GREP -E -- '-webkit-transition:' "$CSS_FILE" >/dev/null 2>&1; then
        has_webkit=1
    fi

    if [ "$has_standard" -eq 1 ]; then
        if [ "$has_webkit" -eq 1 ]; then
            printf "  ${GREEN}✓${NC} Transitions have -webkit- prefix\n"
            return 0
        else
            printf "  ${YELLOW}⚠${NC} Transitions may need -webkit- prefix for Safari\n"
            WARNINGS=$((WARNINGS + 1))
            return 0
        fi
    else
        printf "  ${GREEN}✓${NC} No transition rules to check\n"
        return 0
    fi
}

check_flex_prefixes
check_box_sizing_prefixes
check_transition_prefixes

echo ""

# ============================================================================
# SECTION 4: Additional Quality Checks
# ============================================================================

echo "----------------------------------------"
echo "4. Additional Quality Checks"
echo "----------------------------------------"

# Check for CSS custom properties (variables)
# Use -- as separator to prevent grep from treating --[a-z] as an option
if $GREP -E -- '--[a-z]' "$CSS_FILE" >/dev/null 2>&1; then
    var_count=$($GREP -oE -- '--[a-z][a-z0-9-]*' "$CSS_FILE" | sort -u | wc -l)
    printf "  ${GREEN}✓${NC} CSS custom properties defined: %d unique variables\n" "$var_count"
else
    printf "  ${YELLOW}⚠${NC} No CSS custom properties found\n"
    WARNINGS=$((WARNINGS + 1))
fi

# Check for dark/light theme support
if $GREP -E '\[data-theme=' "$CSS_FILE" >/dev/null 2>&1; then
    printf "  ${GREEN}✓${NC} Theme switching support (data-theme attribute)\n"
else
    printf "  ${YELLOW}⚠${NC} No data-theme attribute selectors found\n"
    WARNINGS=$((WARNINGS + 1))
fi

# Check for print styles
if $GREP -E '@media[[:space:]]*print' "$CSS_FILE" >/dev/null 2>&1; then
    printf "  ${GREEN}✓${NC} Print stylesheet included\n"
else
    printf "  ${YELLOW}⚠${NC} No print styles found\n"
    WARNINGS=$((WARNINGS + 1))
fi

# Check file size (warn if over 100KB)
file_size=$(wc -c < "$CSS_FILE")
file_size_kb=$((file_size / 1024))
if [ "$file_size_kb" -lt 100 ]; then
    printf "  ${GREEN}✓${NC} File size: %d KB (under 100KB limit)\n" "$file_size_kb"
else
    printf "  ${YELLOW}⚠${NC} File size: %d KB (consider minification)\n" "$file_size_kb"
    WARNINGS=$((WARNINGS + 1))
fi

# Count total rules (approximate by counting closing braces)
rule_count=$($GREP -c '}' "$CSS_FILE")
printf "  ${BLUE}ℹ${NC} Approximate rule count: %d\n" "$rule_count"

echo ""

# ============================================================================
# Summary
# ============================================================================

echo "========================================"
echo "Validation Summary"
echo "========================================"

if [ "$ERRORS" -eq 0 ]; then
    printf "${GREEN}✓ All required checks passed${NC}\n"
else
    printf "${RED}✗ %d error(s) found${NC}\n" "$ERRORS"
fi

if [ "$WARNINGS" -gt 0 ]; then
    printf "${YELLOW}⚠ %d warning(s)${NC}\n" "$WARNINGS"
fi

echo ""

if [ "$ERRORS" -gt 0 ]; then
    printf "${RED}VALIDATION FAILED${NC}\n"
    exit 1
else
    printf "${GREEN}VALIDATION PASSED${NC}\n"
    exit 0
fi
