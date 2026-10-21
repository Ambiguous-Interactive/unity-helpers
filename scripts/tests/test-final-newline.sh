#!/usr/bin/env bash
# =============================================================================
# Test Script: Final Newline Validation
# =============================================================================
# Validates that important configuration and source files end with a newline.
# Many tools (Prettier, POSIX text utilities) expect files to end with '\n'.
#
# Usage:
#   bash scripts/tests/test-final-newline.sh
#   bash scripts/tests/test-final-newline.sh --verbose
#
# Exit codes:
#   0 - All checked files have a final newline
#   1 - One or more files are missing a final newline
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

VERBOSE=0
case "${1:-}" in
    --verbose|-v) VERBOSE=1 ;;
esac

cd "$REPO_ROOT"

# Counters
files_checked=0
files_passed=0
files_failed=0
failed_files=()
declare -A checked_files=()
declare -A file_sources=()

# ============================================================================
# CRLF Detection Helper
# ============================================================================
# Detects if a file uses CRLF (Windows) line endings by checking for carriage
# return characters (0x0d). This is used to provide the correct fix suggestion
# when a file is missing its final newline.
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

# ---------------------------------------------------------------------------
# print_file_diagnostics: show high-signal details for failed files
# ---------------------------------------------------------------------------
print_file_diagnostics() {
    local file="$1"
    local line_ending="LF"
    local byte_count
    local tail_hex

    if file_uses_crlf "$file"; then
        line_ending="CRLF"
    fi

    byte_count="$(wc -c < "$file" | tr -d '[:space:]')"
    tail_hex="$(tail -c 16 -- "$file" | od -An -tx1 | tr '\n' ' ' | sed 's/^ *//;s/ *$//')"

    echo -e "      Detected: $line_ending | Bytes: $byte_count | Sources: ${file_sources["$file"]}"
    if [[ -n "$tail_hex" ]]; then
        echo -e "      Tail hex: $tail_hex"
    fi
}

# ---------------------------------------------------------------------------
# check_file: verify a single file ends with a newline
# ---------------------------------------------------------------------------
check_file() {
    local file="$1"
    local source="${2:-unknown}"

    # Skip if file does not exist or is empty
    if [[ ! -s "$file" ]]; then
        return
    fi

    if [[ -n "${file_sources["$file"]+x}" ]]; then
        file_sources["$file"]="${file_sources["$file"]},${source}"
    else
        file_sources["$file"]="$source"
    fi

    # Check each file only once to avoid duplicate failures from overlapping
    # selectors (for example explicit config list + JSON glob).
    if [[ -n "${checked_files["$file"]+x}" ]]; then
        return
    fi

    checked_files["$file"]=1
    files_checked=$((files_checked + 1))

    if [[ "$(tail -c 1 -- "$file" | wc -l)" -eq 0 ]]; then
        local attributes
        if ! attributes=$(git check-attr --cached text -- "$file"); then
            echo "Cannot inspect staged text attribute for $file" >&2
            exit 1
        fi
        if [[ "$attributes" == *': text: unset' ]]; then
            files_checked=$((files_checked - 1))
            return
        fi
        files_failed=$((files_failed + 1))
        failed_files+=("$file")
        # Include line ending type in the failure message for clarity
        if file_uses_crlf "$file"; then
            echo -e "  ${RED}FAIL${NC} $file (CRLF)"
        else
            echo -e "  ${RED}FAIL${NC} $file (LF)"
        fi
        print_file_diagnostics "$file"
    else
        files_passed=$((files_passed + 1))
        if [[ $VERBOSE -eq 1 ]]; then
            echo -e "  ${GREEN}PASS${NC} $file"
        fi
    fi
}

# ---------------------------------------------------------------------------
# check_glob: find files matching a pattern and check each
# Uses git ls-files for consistency with validate-formatting.sh (only checks
# tracked files). Falls back to find if not inside a git repository.
# ---------------------------------------------------------------------------
check_glob() {
    local pattern="$1"
    if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
        while IFS= read -r -d '' file; do
            # Skip generated / vendored directories
            case "$file" in
                node_modules/*|site/*|.git/*) continue ;;
            esac
            check_file "$file" "glob:$pattern"
        done < <(git ls-files -z -- "$pattern" 2>/dev/null)
    else
        while IFS= read -r file; do
            # Skip generated / vendored directories
            case "$file" in
                node_modules/*|site/*|.git/*) continue ;;
            esac
            check_file "$file" "glob:$pattern"
        done < <(find . -name "$pattern" -not -path './node_modules/*' -not -path './site/*' -not -path './.git/*' -type f 2>/dev/null | sort)
    fi
}

# ===========================================================================
echo -e "${BLUE}── Final Newline Check ──${NC}"
echo ""

# 1) Explicit high-value config files
echo -e "${BLUE}Checking configuration files...${NC}"
for cfg in \
    package.json \
    .prettierrc.json \
    .editorconfig \
    .markdownlint.json \
    .yamllint.yaml \
    cspell.json \
    mkdocs.yml \
    _config.yml \
    Gemfile \
    requirements-docs.txt; do
    check_file "$cfg" "config-list"
done

# 2) GitHub workflow files
echo -e "${BLUE}Checking GitHub workflow files...${NC}"
check_glob "*.yml"
check_glob "*.yaml"

# 3) Shell scripts
echo -e "${BLUE}Checking shell scripts...${NC}"
check_glob "*.sh"

# 4) PowerShell scripts
echo -e "${BLUE}Checking PowerShell scripts...${NC}"
check_glob "*.ps1"

# 5) JavaScript files
echo -e "${BLUE}Checking JavaScript files...${NC}"
check_glob "*.js"

# 6) TypeScript files
echo -e "${BLUE}Checking TypeScript files...${NC}"
check_glob "*.ts"

# 7) JSON files (asmdef / asmref included)
echo -e "${BLUE}Checking JSON / asmdef / asmref files...${NC}"
check_glob "*.json"
check_glob "*.jsonc"
check_glob "*.asmdef"
check_glob "*.asmref"

# 8) Markdown files
echo -e "${BLUE}Checking Markdown files...${NC}"
check_glob "*.md"
check_glob "*.markdown"

# 9) C# source files
echo -e "${BLUE}Checking C# source files...${NC}"
check_glob "*.cs"

# 10) Text / HTML / CSS / XML files
echo -e "${BLUE}Checking text / HTML / CSS / XML files...${NC}"
check_glob "*.txt"
check_glob "*.html"
check_glob "*.css"
check_glob "*.xml"

# ===========================================================================
# Summary
# ===========================================================================
echo ""
echo -e "${BLUE}── Summary ──${NC}"
echo -e "  Files checked: $files_checked"
echo -e "  ${GREEN}Passed${NC}:  $files_passed"
echo -e "  ${RED}Failed${NC}:  $files_failed"

if [[ $files_failed -gt 0 ]]; then
    echo ""
    echo -e "${RED}The following files are missing a final newline:${NC}"
    for f in "${failed_files[@]}"; do
        echo -e "  ${RED}-${NC} $f"
    done
    echo ""
    echo -e "Fix with (CRLF-aware):"
    echo -e "  ${YELLOW}For CRLF files: printf '\\r\\n' >> <file>${NC}"
    echo -e "  ${YELLOW}For LF files:   printf '\\n' >> <file>${NC}"
    echo -e "Or run:   ${YELLOW}./scripts/validate-formatting.sh --fix${NC} (auto-detects line endings)"
    exit 1
else
    echo ""
    echo -e "${GREEN}All files have a final newline.${NC}"
    exit 0
fi
