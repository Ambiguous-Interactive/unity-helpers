#!/usr/bin/env bash
# Validates code fence syntax in markdown files for MkDocs/GitHub Pages compatibility.
# MkDocs does not support comma-separated attributes in code fences (e.g., ```csharp,ignore).
#
# Exit codes: 0 = success, 1 = validation errors found
#
# Usage: ./scripts/check-code-fence-syntax.sh [DOCS_DIR]
# Default docs directory: docs/

set -euo pipefail

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Script location and default docs directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DOCS_DIR="${1:-$REPO_ROOT/docs}"

# Counter for issues
ISSUES=0

echo "========================================"
echo "Code Fence Syntax Validator"
echo "========================================"
echo ""
printf "Docs Directory: ${BLUE}%s${NC}\n" "$DOCS_DIR"
echo ""

# Check if docs directory exists
if [ ! -d "$DOCS_DIR" ]; then
    printf "${RED}ERROR: Docs directory not found: %s${NC}\n" "$DOCS_DIR"
    exit 1
fi

echo "----------------------------------------"
echo "Checking for invalid code fence syntax"
echo "----------------------------------------"
echo ""
echo "Pattern: code fences with comma-separated attributes"
echo "  Invalid: \`\`\`csharp,ignore or \`\`\`rust,no_run"
echo "  Valid:   \`\`\`csharp or \`\`\`rust"
echo ""

# Array to store issues for summary
declare -a ISSUE_LIST

# A scan that matched nothing is the absence of a measurement, not a pass (#556): docs/ renamed,
# a corpus moved, or a find that stopped matching all report "no issues found" otherwise.
SCANNED=0

# Find all markdown files and check for invalid code fence syntax
# Pattern matches code fences with language followed by comma and attributes
# Examples of invalid patterns:
#   ```csharp,ignore
#   ```rust,no_run
#   ```python,something
while IFS= read -r -d '' mdfile; do
    SCANNED=$((SCANNED + 1))
    line_num=0

    while IFS= read -r line || [[ -n "$line" ]]; do
        line_num=$((line_num + 1))

        # Check for code fence with comma-separated attributes
        # Pattern: ``` followed by word characters (language), then comma, then more characters
        # This matches the opening of a code fence only
        if [[ "$line" =~ ^[[:space:]]*([\`]{3,}|\~{3,})([a-zA-Z][a-zA-Z0-9_+-]*),([a-zA-Z0-9_,+-]+) ]]; then
            language="${BASH_REMATCH[2]}"
            attributes="${BASH_REMATCH[3]}"

            # Get relative path for cleaner output
            rel_path="${mdfile#"$REPO_ROOT"/}"

            printf "${RED}ISSUE${NC} %s:%d\n" "$rel_path" "$line_num"
            printf "  Found: \`\`\`%s,%s\n" "$language" "$attributes"
            printf "  Fix:   \`\`\`%s  ${YELLOW}(remove ',%s')${NC}\n" "$language" "$attributes"
            echo ""

            ISSUE_LIST+=("$rel_path:$line_num: \`\`\`$language,$attributes -> \`\`\`$language")
            ISSUES=$((ISSUES + 1))
        fi
    done < "$mdfile"
done < <(find "$DOCS_DIR" -name "*.md" -type f -print0)

echo "----------------------------------------"
echo "Summary"
echo "----------------------------------------"

if [ "$SCANNED" -eq 0 ]; then
    printf "${RED}ERROR: No markdown files found under %s${NC}\n" "$DOCS_DIR"
    echo ""
    echo "The corpus is empty, so this run checked nothing. Point the validator at a"
    echo "directory that contains markdown, or fix the path if the docs tree moved."
    echo ""
    printf "${RED}VALIDATION FAILED${NC}\n"
    exit 1
fi

printf "Markdown files scanned: ${BLUE}%d${NC}\n" "$SCANNED"
echo ""

if [ "$ISSUES" -eq 0 ]; then
    printf "${GREEN}No code fence syntax issues found.${NC}\n"
    echo ""
    printf "${GREEN}VALIDATION PASSED${NC}\n"
    exit 0
else
    printf "${RED}Found %d code fence syntax issue(s)${NC}\n" "$ISSUES"
    echo ""
    echo "MkDocs and GitHub Pages do not support comma-separated"
    echo "attributes in code fences. Remove the comma and everything"
    echo "after it from the language identifier."
    echo ""
    printf "${RED}VALIDATION FAILED${NC}\n"
    exit 1
fi
