#!/usr/bin/env bash
# Verification script to check that all CI/CD tools are available in the dev container.
# Run this after building the container to ensure everything is properly installed.
#
# NOTE: Some tools are only installed in the dev container (actionlint, shellcheck,
# yamllint, lychee, codex, and high-performance CLI tools). These are marked as optional
# when running outside the container. The git hooks gracefully skip these tools
# if not present - CI will catch any issues.

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Detect if running in dev container
IN_CONTAINER=false
if [ -f "/.dockerenv" ] || [ -n "$REMOTE_CONTAINERS" ] || [ -n "$CODESPACES" ]; then
    IN_CONTAINER=true
fi

echo "========================================"
echo "Dev Container Tool Verification"
if [ "$IN_CONTAINER" = true ]; then
    printf "Environment: ${GREEN}Dev Container${NC}\n"
else
    printf "Environment: ${YELLOW}Host Machine${NC} (some tools optional)\n"
fi
echo "========================================"
echo ""

FAILED=0
WARNINGS=0

# Function to check if a command exists and print its version
check_tool() {
    local name="$1"
    local cmd="$2"
    local version_flag="${3:---version}"
    local required="${4:-true}"

    printf "%-20s" "$name:"
    if command -v "$cmd" >/dev/null 2>&1; then
        version=$("$cmd" $version_flag 2>&1 | head -1)
        printf "${GREEN}✓${NC} %s\n" "$version"
        return 0
    else
        if [ "$required" = "true" ]; then
            printf "${RED}✗ NOT FOUND (REQUIRED)${NC}\n"
            FAILED=$((FAILED + 1))
            return 1
        else
            printf "${YELLOW}⚠ not installed (optional)${NC}\n"
            WARNINGS=$((WARNINGS + 1))
            return 0
        fi
    fi
}

# Function to check npm package availability
check_npm_tool() {
    local name="$1"
    local cmd="$2"
    local version_flag="${3:---version}"
    local output=""
    local status=0

    printf "%-20s" "$name:"
    if output="$(timeout 30s npx --no-install "$cmd" $version_flag 2>&1)"; then
        version=$(printf '%s\n' "$output" | head -1)
        printf "${GREEN}✓${NC} %s (via npx)\n" "$version"
        return 0
    else
        status=$?
        if [ "$status" -eq 124 ]; then
            printf "${RED}✗ TIMED OUT - npx %s %s${NC}\n" "$cmd" "$version_flag"
        else
            printf "${RED}✗ NOT FOUND - run 'npm install'${NC}\n"
        fi
        FAILED=$((FAILED + 1))
        return 1
    fi
}

# Function to check .NET tool availability
check_dotnet_tool() {
    local name="$1"
    local cmd="$2"
    local version_flag="${3:---version}"

    printf "%-20s" "$name:"
    if dotnet tool run "$cmd" $version_flag >/dev/null 2>&1; then
        version=$(dotnet tool run "$cmd" $version_flag 2>&1 | head -1)
        printf "${GREEN}✓${NC} %s (via dotnet tool)\n" "$version"
        return 0
    elif command -v "$cmd" >/dev/null 2>&1; then
        version=$("$cmd" $version_flag 2>&1 | head -1)
        printf "${GREEN}✓${NC} %s (global)\n" "$version"
        return 0
    else
        printf "${RED}✗ NOT FOUND - run 'dotnet tool restore'${NC}\n"
        FAILED=$((FAILED + 1))
        return 1
    fi
}

echo "=== CI/CD Linting Tools (container-only) ==="
# These tools are only reliably installed in the dev container.
# Git hooks gracefully skip them if not present.
check_tool "actionlint" "actionlint" "--version" "$IN_CONTAINER"
check_tool "shellcheck" "shellcheck" "--version" "$IN_CONTAINER"
check_tool "yamllint" "yamllint" "--version" "$IN_CONTAINER"
check_tool "lychee" "lychee" "--version" "$IN_CONTAINER"
echo ""

echo "=== npm-based Tools (via package.json) ==="
check_npm_tool "markdownlint" "markdownlint" "--version"
check_npm_tool "prettier" "prettier" "--version"
check_npm_tool "cspell" "cspell" "--version"
echo ""

echo "=== .NET Tools ==="
check_dotnet_tool "csharpier" "csharpier" "--version"
echo ""

echo "=== Core Development Tools ==="
check_tool "node" "node" "--version"
check_tool "npm" "npm" "--version"
check_tool "dotnet" "dotnet" "--version"
check_tool "pwsh" "pwsh" "--version"
check_tool "python3" "python3" "--version"
check_tool "git" "git" "--version"
check_tool "gh" "gh" "--version"
echo ""

echo "=== AI CLI Tools (container-only) ==="
check_tool "codex" "codex" "--version" "$IN_CONTAINER"
check_tool "opencode" "opencode" "--version" "$IN_CONTAINER"
check_tool "nanocoder" "nanocoder" "--version" "$IN_CONTAINER"
echo ""

echo "=== High-Performance CLI Tools (container-only) ==="
# These tools are only installed in the dev container.
check_tool "ripgrep (rg)" "rg" "--version" "$IN_CONTAINER"
check_tool "fd" "fd" "--version" "$IN_CONTAINER"
check_tool "bat" "bat" "--version" "$IN_CONTAINER"
check_tool "fzf" "fzf" "--version" "$IN_CONTAINER"
check_tool "delta" "delta" "--version" "$IN_CONTAINER"
check_tool "eza" "eza" "--version" "$IN_CONTAINER"
check_tool "zoxide" "zoxide" "--version" "$IN_CONTAINER"
check_tool "yq" "yq" "--version" "$IN_CONTAINER"
check_tool "jq" "jq" "--version" "$IN_CONTAINER"
echo ""

echo "========================================"
if [ $FAILED -eq 0 ]; then
    if [ $WARNINGS -gt 0 ]; then
        printf "${GREEN}All required tools are installed!${NC}\n"
        printf "${YELLOW}$WARNINGS optional tool(s) not installed.${NC}\n"
    else
        printf "${GREEN}All tools are installed and ready!${NC}\n"
    fi
    echo "========================================"
    exit 0
else
    printf "${RED}$FAILED required tool(s) missing!${NC}\n"
    echo "========================================"
    echo ""
    echo "To fix missing tools:"
    echo "  - npm tools: npm install"
    echo "  - dotnet tools: dotnet tool restore"
    echo "  - system tools: rebuild dev container"
    exit 1
fi
