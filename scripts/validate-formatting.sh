#!/usr/bin/env bash
set -euo pipefail

# =============================================================================
# Formatting Validation Script
# =============================================================================
# Runs all Prettier formatting checks and provides clear error messages.
# Use this locally before pushing to catch CI formatting failures early.
#
# Usage:
#   ./scripts/validate-formatting.sh           # Check all formatting
#   ./scripts/validate-formatting.sh --fix     # Auto-fix all formatting issues
#   ./scripts/validate-formatting.sh --help    # Show help
#
# Exit codes:
#   0 - All checks passed
#   1 - Formatting issues found (or tool missing)
# =============================================================================

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$REPO_ROOT"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

FAILED=0
FIX_MODE=0
CHECKS_RUN=0
CHECKS_PASSED=0

print_header() {
    echo ""
    echo -e "${BLUE}── $1 ──${NC}"
}

print_success() {
    echo -e "${GREEN}  PASS${NC} $1"
}

print_fail() {
    echo -e "${RED}  FAIL${NC} $1"
}

print_info() {
    echo -e "${BLUE}  INFO${NC} $1"
}

show_help() {
    echo "Formatting Validation Script"
    echo ""
    echo "Usage: $0 [OPTIONS]"
    echo ""
    echo "Options:"
    echo "  --fix     Auto-fix formatting issues instead of just checking"
    echo "  --help    Show this help message"
    echo ""
    echo "Checks performed:"
    echo "  1. Markdown formatting    (*.md, *.markdown)"
    echo "  2. JSON formatting        (*.json, *.jsonc, *.asmdef, *.asmref)"
    echo "  3. YAML formatting        (*.yml, *.yaml)"
    echo "  4. JavaScript formatting  (*.js)"
    echo "  5. Final newline          (tracked text files)"
    echo ""
    echo "This script mirrors the CI formatting checks so you can"
    echo "catch issues before pushing."
}

run_check() {
    local label="$1"
    local pattern="$2"
    CHECKS_RUN=$((CHECKS_RUN + 1))

    if [[ $FIX_MODE -eq 1 ]]; then
        print_info "$label: auto-fixing..."
        if node scripts/run-prettier.js --write -- "$pattern" 2>/dev/null; then
            print_success "$label"
            CHECKS_PASSED=$((CHECKS_PASSED + 1))
        else
            print_fail "$label: prettier --write failed"
            FAILED=1
        fi
    else
        if node scripts/run-prettier.js --check -- "$pattern" >/dev/null 2>&1; then
            print_success "$label"
            CHECKS_PASSED=$((CHECKS_PASSED + 1))
        else
            print_fail "$label"
            # Show which files failed
            echo -e "         Failing files:"
            node scripts/run-prettier.js --list-different -- "$pattern" 2>/dev/null | while IFS= read -r file; do
                echo -e "           ${RED}-${NC} $file"
            done
            echo -e "         Fix with: ${YELLOW}node scripts/run-prettier.js --write -- \"$pattern\"${NC}"
            FAILED=1
        fi
    fi
}

# ============================================================================
# CRLF Detection Helper
# ============================================================================
# Detects if a file uses CRLF (Windows) line endings by checking for carriage
# return characters (0x0d). This is used to preserve the correct line ending
# style when appending a final newline to files.
#
# Returns:
#   0 (true)  - File uses CRLF line endings
#   1 (false) - File uses LF line endings (or is empty/binary)
# ============================================================================
file_uses_crlf() {
    local file="$1"
    # Check if file is non-empty and contains CR characters (part of CRLF)
    if [[ -s "$file" ]] && grep -q $'\r' "$file" 2>/dev/null; then
        return 0  # Uses CRLF
    fi
    return 1  # Uses LF
}

# Check that tracked text files end with a final newline
run_final_newline_check() {
    local label="Final newline (tracked text files)"
    CHECKS_RUN=$((CHECKS_RUN + 1))

    # Text file extensions to check for final newline
    local -a patterns=(
        '*.json' '*.jsonc' '*.asmdef' '*.asmref'
        '*.md' '*.markdown'
        '*.yml' '*.yaml'
        '*.js' '*.ts'
        '*.cs'
        '*.sh' '*.ps1'
        '*.txt' '*.html' '*.css' '*.xml'
    )

    local missing_newline_files=()

    # Gather all tracked files matching the patterns
    for pat in "${patterns[@]}"; do
        while IFS= read -r -d '' file; do
            # Skip files in node_modules, site, or other generated directories
            case "$file" in
                node_modules/*|site/*|.git/*) continue ;;
            esac
            # Check if file is non-empty and missing final newline
            if [[ -s "$file" ]] && [[ "$(tail -c 1 -- "$file" | wc -l)" -eq 0 ]]; then
                local attributes
                if ! attributes=$(git check-attr --cached text -- "$file"); then
                    print_fail "$label: cannot inspect staged text attribute for $file"
                    FAILED=1
                    return 1
                fi
                if [[ "$attributes" == *': text: unset' ]]; then
                    continue
                fi
                missing_newline_files+=("$file")
            fi
        done < <(git ls-files -z -- "$pat" 2>/dev/null)
    done

    if [[ ${#missing_newline_files[@]} -eq 0 ]]; then
        print_success "$label"
        CHECKS_PASSED=$((CHECKS_PASSED + 1))
    else
        if [[ $FIX_MODE -eq 1 ]]; then
            print_info "$label: auto-fixing..."
            for file in "${missing_newline_files[@]}"; do
                # Append the correct newline based on the file's line ending style
                # CRLF files get \r\n, LF files get \n to avoid mixed line endings
                if file_uses_crlf "$file"; then
                    printf '\r\n' >> "$file"
                else
                    printf '\n' >> "$file"
                fi
            done
            print_success "$label (fixed ${#missing_newline_files[@]} file(s))"
            CHECKS_PASSED=$((CHECKS_PASSED + 1))
        else
            print_fail "$label"
            echo -e "         Files missing final newline:"
            for file in "${missing_newline_files[@]}"; do
                echo -e "           ${RED}-${NC} $file"
            done
            echo -e "         Fix with: ${YELLOW}$0 --fix${NC} or add a newline at end of file"
            FAILED=1
        fi
    fi
}

# Parse arguments
case "${1:-}" in
    --fix)
        FIX_MODE=1
        ;;
    --help|-h)
        show_help
        exit 0
        ;;
    "")
        # Default: check mode
        ;;
    *)
        echo "Unknown option: $1"
        show_help
        exit 1
        ;;
esac

cd "$REPO_ROOT"

# Verify prerequisites
if ! command -v node >/dev/null 2>&1; then
    echo -e "${RED}Error: node is not available. Install Node.js first.${NC}"
    exit 1
fi

if [[ ! -d "node_modules" ]]; then
    echo -e "${RED}Error: node_modules not found. Run 'npm install' first.${NC}"
    exit 1
fi

if ! node scripts/run-prettier.js --version >/dev/null 2>&1; then
    echo -e "${RED}Error: prettier not found. Run 'npm install' first.${NC}"
    exit 1
fi

if [[ $FIX_MODE -eq 1 ]]; then
    print_header "Auto-fixing formatting issues"
else
    print_header "Checking formatting (use --fix to auto-fix)"
fi

# Run all formatting checks
run_check "Markdown (*.md, *.markdown)"          "**/*.{md,markdown}"
run_check "JSON (*.json, *.jsonc, *.asmdef, *.asmref)" "**/*.{json,jsonc,asmdef,asmref}"
run_check "YAML (*.yml, *.yaml)"                 "**/*.{yml,yaml}"
run_check "JavaScript (*.js)"                     "**/*.js"

# Final newline check on tracked text files
run_final_newline_check

# Summary
echo ""
echo -e "${BLUE}── Summary ──${NC}"
echo -e "  Checks: $CHECKS_PASSED/$CHECKS_RUN passed"

if [[ $FAILED -ne 0 ]]; then
    echo ""
    echo -e "${RED}Formatting issues found.${NC}"
    echo -e "Run ${YELLOW}$0 --fix${NC} to auto-fix all issues."
    echo -e "Or run ${YELLOW}npm run format:check${NC} then ${YELLOW}npm run format:fix${NC}."
    exit 1
else
    echo -e "${GREEN}All formatting checks passed.${NC}"
    exit 0
fi
