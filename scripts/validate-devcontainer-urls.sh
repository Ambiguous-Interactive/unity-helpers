#!/usr/bin/env bash
# Validates that all GitHub release download URLs in the devcontainer Dockerfile
# are reachable (HTTP 200). This catches broken URLs (wrong version, wrong arch
# suffix like gnu vs musl, yanked releases) before they break the container build.
#
# Usage:
#   ./scripts/validate-devcontainer-urls.sh
#   ./scripts/validate-devcontainer-urls.sh --dockerfile .devcontainer/Dockerfile
#   ./scripts/validate-devcontainer-urls.sh --contracts-only
#   ./scripts/validate-devcontainer-urls.sh --list-tools
#
# Requires: bash 4+, curl, sed

set -euo pipefail

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Default Dockerfile path (relative to repo root)
DOCKERFILE=".devcontainer/Dockerfile"
CONTRACTS_ONLY=0
LIST_TOOLS=0

# Parse arguments
while [[ $# -gt 0 ]]; do
    case "$1" in
        --dockerfile)
            DOCKERFILE="$2"
            shift 2
            ;;
        --contracts-only)
            CONTRACTS_ONLY=1
            shift
            ;;
        --list-tools)
            LIST_TOOLS=1
            shift
            ;;
        *)
            echo "Usage: $0 [--dockerfile <path>] [--contracts-only] [--list-tools]" >&2
            exit 1
            ;;
    esac
done

# Resolve to repo root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
if [[ "$DOCKERFILE" = /* ]]; then
    DOCKERFILE_PATH="$DOCKERFILE"
else
    DOCKERFILE_PATH="$REPO_ROOT/$DOCKERFILE"
fi

if [ ! -f "$DOCKERFILE_PATH" ]; then
    echo "Error: Dockerfile not found at $DOCKERFILE_PATH" >&2
    exit 1
fi

assert_no_apt_powershell_install() {
    local hits

    # PowerShell apt packages are not consistently available across all target
    # architectures used in buildx. Require release-tarball installs instead.
    hits=$(awk '
        function trim(s) {
            sub(/^[[:space:]]+/, "", s)
            sub(/[[:space:]]+$/, "", s)
            return s
        }

        function check_buffer(start_line, cmd, lc_cmd) {
            lc_cmd = tolower(cmd)
            if (lc_cmd ~ /apt(-get)?[[:space:]]+install[^&;|]*([^[:alnum:]_]|^)powershell([^[:alnum:]_]|$)/) {
                print start_line ":" cmd
            }
        }

        {
            line=$0

            if (line ~ /^[[:space:]]*#/) {
                next
            }

            if (!in_run && line ~ /^[[:space:]]*RUN[[:space:]]+/) {
                in_run=1
                start_line=NR
                cmd=line
            } else if (in_run) {
                cmd=cmd " " line
            }

            if (in_run) {
                if (line !~ /\\[[:space:]]*$/) {
                    check_buffer(start_line, trim(cmd))
                    in_run=0
                    cmd=""
                }
            }
        }

        END {
            if (in_run && cmd != "") {
                check_buffer(start_line, trim(cmd))
            }
        }
    ' "$DOCKERFILE_PATH")

    if [ -n "$hits" ]; then
        printf "%b\n" "${RED}Detected apt-based PowerShell install in Dockerfile:${NC}" >&2
        echo "$hits" >&2
        echo "Use PowerShell release tarballs with TARGETARCH mapping (amd64=x64, arm64=arm64)." >&2
        exit 1
    fi
}

echo "========================================"
echo "Devcontainer Download URL Validation"
echo "Dockerfile: $DOCKERFILE"
echo "========================================"
echo ""

assert_no_apt_powershell_install

if [ "$CONTRACTS_ONLY" -eq 1 ]; then
    echo "Contract checks passed (no URL probing requested)."
    exit 0
fi

FAILED=0
PASSED=0
CHECKED=0

# Extract tool definitions: VERSION, ARCH mappings, and URL template
# Each RUN block that downloads from GitHub follows a pattern:
#   *_VERSION="X.Y.Z"
#   case ... amd64) *_ARCH="..." ;; arm64) *_ARCH="..." ;;
#   curl ... "https://github.com/.../${VERSION}/...-${ARCH}..."

# Data-driven tool definitions extracted from the Dockerfile.
# Format: TOOL_NAME|VERSION|AMD64_ARCH|ARM64_ARCH|URL_TEMPLATE
# URL_TEMPLATE uses {VERSION} and {ARCH} as placeholders.
tools=()

extract_tools() {
    local version=""
    local amd64_arch=""
    local arm64_arch=""
    local url_template=""
    local tool_name=""
    local in_run_block=false
    local version_var=""

    while IFS= read -r line; do
        # Detect VERSION assignment (e.g., FD_VERSION="10.4.2")
        if [[ "$line" =~ ([A-Z_]+)_VERSION=\"([^\"]+)\" ]]; then
            version_var="${BASH_REMATCH[1]}"
            version="${BASH_REMATCH[2]}"
            tool_name=$(echo "$version_var" | tr '[:upper:]' '[:lower:]')
            amd64_arch=""
            arm64_arch=""
            url_template=""
            in_run_block=true
        fi

        if [ "$in_run_block" = true ]; then
            # Extract amd64 arch mapping
            if [[ "$line" =~ amd64\)\ +[A-Z_]+=\"([^\"]+)\" ]]; then
                amd64_arch="${BASH_REMATCH[1]}"
            fi

            # Extract arm64 arch mapping
            if [[ "$line" =~ arm64\)\ +[A-Z_]+=\"([^\"]+)\" ]]; then
                arm64_arch="${BASH_REMATCH[1]}"
            fi

            # Extract URL template from curl command
            if [[ "$line" =~ curl[[:space:]]+-[a-zA-Z]*[[:space:]]+\"(https://github\.com/[^\"]+)\" ]]; then
                url_template="${BASH_REMATCH[1]}"

                # Replace variable references with placeholders
                # Handle ${VAR_VERSION} and ${VAR_ARCH} patterns
                # Specific variable first, then generic catch-all
                url_template=$(echo "$url_template" | sed -E "s/\\$\\{${version_var}_VERSION\\}/{VERSION}/g")
                url_template=$(echo "$url_template" | sed -E "s/\\$\\{[A-Z_]+_VERSION\\}/{VERSION}/g")
                url_template=$(echo "$url_template" | sed -E "s/\\$\\{[A-Z_]+_ARCH\\}/{ARCH}/g")

                # Only add if we have all required fields
                if [ -n "$version" ] && [ -n "$url_template" ]; then
                    # Some tools (like yq) don't have arch case blocks
                    if [ -z "$amd64_arch" ]; then
                        amd64_arch="NONE"
                    fi
                    if [ -z "$arm64_arch" ]; then
                        arm64_arch="NONE"
                    fi
                    tools+=("${tool_name}|${version}|${amd64_arch}|${arm64_arch}|${url_template}")
                fi
                in_run_block=false
            fi
        fi
    done < "$DOCKERFILE_PATH"
}

check_url() {
    local arch_label="$1"
    local url="$2"

    CHECKED=$((CHECKED + 1))
    local status=""
    local attempt

    # Retried, because this is a network call to an external host and a single blip on one of
    # thirty URLs reds the whole Repo Lint job -- observed as a 302 whose redirect could not be
    # followed, on an asset that answers 200 every time by hand.
    for attempt in 1 2 3; do
        # Assigned, then replaced on failure. `status=$(cmd || echo "000")` APPENDS the fallback to
        # whatever curl already wrote, so a curl that emits "302" and then fails reports the
        # impossible status "302000" -- which is not 200, so the check fails, and is not a status
        # anyone can look up either.
        if ! status=$(
            curl -sI -o /dev/null -w "%{http_code}" -L --max-time 30 "$url" 2>/dev/null
        ); then
            status="000"
        fi

        if [ "$status" = "200" ]; then
            break
        fi

        if [ "$attempt" -lt 3 ]; then
            sleep $((attempt * 2))
        fi
    done

    if [ "$status" = "200" ]; then
        printf "  ${GREEN}✓${NC} %-8s %s\n" "[$arch_label]" "$url"
        PASSED=$((PASSED + 1))
        return 0
    else
        printf "  ${RED}✗${NC} %-8s HTTP %s  %s\n" "[$arch_label]" "$status" "$url"
        FAILED=$((FAILED + 1))
        return 1
    fi
}

# Extract tools from Dockerfile
extract_tools

if [ ${#tools[@]} -eq 0 ]; then
    echo "Warning: No tool definitions found in Dockerfile" >&2
    exit 1
fi

if [ "$LIST_TOOLS" -eq 1 ]; then
    echo "Found ${#tools[@]} tools"
    for entry in "${tools[@]}"; do
        IFS='|' read -r tool_name version amd64_arch arm64_arch url_template <<< "$entry"
        printf '%s|%s|%s|%s|%s\n' "$tool_name" "$version" "$amd64_arch" "$arm64_arch" "$url_template"
    done
    exit 0
fi

echo "Found ${#tools[@]} tools to validate"
echo ""

# Check each tool's URLs for both architectures
for entry in "${tools[@]}"; do
    IFS='|' read -r tool_name version amd64_arch arm64_arch url_template <<< "$entry"

    printf "${YELLOW}%s${NC} v%s\n" "$tool_name" "$version"

    # Check amd64
    if [ "$amd64_arch" != "NONE" ]; then
        amd64_url=$(echo "$url_template" | sed "s/{VERSION}/$version/g; s/{ARCH}/$amd64_arch/g")
        check_url "amd64" "$amd64_url" || true
    else
        amd64_url=$(echo "$url_template" | sed "s/{VERSION}/$version/g; s/{ARCH}//g")
        check_url "amd64" "$amd64_url" || true
    fi

    # Check arm64
    if [ "$arm64_arch" != "NONE" ]; then
        arm64_url=$(echo "$url_template" | sed "s/{VERSION}/$version/g; s/{ARCH}/$arm64_arch/g")
        check_url "arm64" "$arm64_url" || true
    else
        arm64_url=$(echo "$url_template" | sed "s/{VERSION}/$version/g; s/{ARCH}//g")
        check_url "arm64" "$arm64_url" || true
    fi

    echo ""
done

echo "========================================"
echo "Results: $PASSED passed, $FAILED failed ($CHECKED total)"
echo "========================================"

if [ "$FAILED" -gt 0 ]; then
    printf "${RED}%d URL(s) returned non-200 status!${NC}\n" "$FAILED"
    echo ""
    echo "Common causes:"
    echo "  - Wrong libc suffix (gnu vs musl) — check release assets"
    echo "  - Version yanked or doesn't exist"
    echo "  - Archive naming convention changed in new release"
    echo "  - arm64 builds not published for this version"
    exit 1
else
    printf "${GREEN}All download URLs are valid!${NC}\n"
    exit 0
fi
