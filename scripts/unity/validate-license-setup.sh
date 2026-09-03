#!/usr/bin/env bash
set -euo pipefail

###############################################################################
# validate-license-setup.sh
#
# Pre-flight validation for Unity license setup in devcontainers.
# Runs quick checks before expensive Unity Docker operations.
#
# Usage:
#   bash scripts/unity/validate-license-setup.sh
#
# Exit codes:
#   0 = All checks passed
#   1 = One or more checks failed
#
###############################################################################

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORKSPACE_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
SECRETS_DIR="${WORKSPACE_DIR}/.unity-secrets"
UNITY_TEST_PROJECT_DIR="${UNITY_TEST_PROJECT_DIR:-/home/vscode/.unity-test-project}"

# shellcheck source=scripts/unity/lib/license-cache-dir.sh
source "${SCRIPT_DIR}/lib/license-cache-dir.sh"

CACHE_DIR="$(resolve_unity_license_cache_dir \
    "${UNITY_TEST_PROJECT_DIR}" "${WORKSPACE_DIR}" "${UNITY_LICENSE_CACHE_DIR:-}")"

# Color codes for terminal output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo "==============================================================="
echo "Unity License Setup Validation"
echo "==============================================================="
echo ""

PASSED=0
FAILED=0

check_pass() {
    echo -e "  ${GREEN}[PASS]${NC} $1"
    PASSED=$((PASSED + 1))
}

check_fail() {
    echo -e "  ${RED}[FAIL]${NC} $1"
    FAILED=$((FAILED + 1))
}

check_warn() {
    echo -e "  ${YELLOW}[WARN]${NC} $1"
}

check_info() {
    echo -e "  ${BLUE}[INFO]${NC} $1"
}

# Check 1: Credentials Available

echo -e "${BLUE}1. Checking credentials...${NC}"

HAS_ENV_EMAIL=0
HAS_ENV_PASSWORD=0
HAS_ENV_SERIAL=0
HAS_FILE_CREDS=0

if [[ -n "${UNITY_EMAIL:-}" ]]; then
    check_pass "UNITY_EMAIL is set in environment"
    HAS_ENV_EMAIL=1
fi

if [[ -n "${UNITY_PASSWORD:-}" ]]; then
    check_pass "UNITY_PASSWORD is set in environment"
    HAS_ENV_PASSWORD=1
fi

if [[ -n "${UNITY_SERIAL:-}" ]]; then
    check_pass "UNITY_SERIAL is set in environment"
    HAS_ENV_SERIAL=1
fi

if [[ -f "${SECRETS_DIR}/credentials.env" ]]; then
    check_pass ".unity-secrets/credentials.env exists"
    HAS_FILE_CREDS=1

    if grep -q "UNITY_EMAIL" "${SECRETS_DIR}/credentials.env"; then
        check_info "credentials.env contains UNITY_EMAIL"
    fi
    if grep -q "UNITY_PASSWORD" "${SECRETS_DIR}/credentials.env"; then
        check_info "credentials.env contains UNITY_PASSWORD"
    fi
else
    if [[ $HAS_ENV_EMAIL -eq 0 && $HAS_ENV_PASSWORD -eq 0 && $HAS_ENV_SERIAL -eq 0 ]]; then
        check_fail "No credentials found (set env vars or run: npm run unity:setup-license)"
    fi
fi

echo ""

# Check 2: License File Available

echo -e "${BLUE}2. Checking license files...${NC}"

if [[ -f "${SECRETS_DIR}/license.ulf" ]]; then
    ULF_SIZE=$(stat -f%z "${SECRETS_DIR}/license.ulf" 2>/dev/null || stat -c%s "${SECRETS_DIR}/license.ulf" 2>/dev/null || echo "0")
    check_pass ".unity-secrets/license.ulf exists (${ULF_SIZE} bytes)"
    if [[ $ULF_SIZE -lt 100 ]]; then
        check_warn ".ulf file is very small; it may be invalid"
    fi
else
    check_info ".unity-secrets/license.ulf not present (online activation path only)"
fi

echo ""

# Check 3: Cache Directory

echo -e "${BLUE}3. Checking license cache directory...${NC}"

if [[ -d "${CACHE_DIR}" ]]; then
    check_pass "Cache directory exists: ${CACHE_DIR}"

    if [[ -w "${CACHE_DIR}" ]]; then
        check_pass "Cache directory is writable"
    else
        check_fail "Cache directory is not writable"
    fi

    ARTIFACT_COUNT=0
    for candidate in \
        "${CACHE_DIR}/local-share-unity3d/Unity/Unity_lic.ulf" \
        "${CACHE_DIR}/config-unity3d/Unity/Unity_lic.ulf" \
        "${CACHE_DIR}/local-share-unity3d/Unity/UnityEntitlementLicense.xml" \
        "${CACHE_DIR}/config-unity3d/Unity/UnityEntitlementLicense.xml"
    do
        if [[ -s "${candidate}" ]]; then
            check_pass "Found cached license artifact: $(basename "${candidate}")"
            ARTIFACT_COUNT=$((ARTIFACT_COUNT + 1))
        fi
    done

    if [[ $ARTIFACT_COUNT -eq 0 ]]; then
        check_info "No cached license artifacts found (first run will activate)"
    fi
else
    check_info "Cache directory does not exist yet (will be created during activation)"
fi

echo ""

# Check 4: Docker Availability

echo -e "${BLUE}4. Checking Docker setup...${NC}"

if command -v docker >/dev/null 2>&1; then
    check_pass "Docker command is available"

    if docker info >/dev/null 2>&1; then
        check_pass "Docker daemon is running"
    else
        check_fail "Docker daemon is not running"
    fi
else
    check_fail "Docker command not found"
fi

echo ""

# Check 5: Required Scripts

echo -e "${BLUE}5. Checking required scripts...${NC}"

REQUIRED_SCRIPTS=(
    "scripts/unity/run-unity-docker.sh"
    "scripts/unity/compile.sh"
    ".devcontainer/post-create.sh"
)

for script in "${REQUIRED_SCRIPTS[@]}"; do
    if [[ -f "${WORKSPACE_DIR}/${script}" ]]; then
        check_pass "${script} exists"
    else
        check_fail "${script} missing"
    fi
done

echo ""
echo "==============================================================="
echo "Validation Summary"
echo "==============================================================="

if [[ $FAILED -eq 0 ]]; then
    echo -e "  ${GREEN}All checks passed${NC}"
else
    echo -e "  ${RED}${FAILED} check(s) failed${NC}"
fi

echo -e "  Passed: ${GREEN}${PASSED}${NC}  Failed: ${RED}${FAILED}${NC}"
echo ""
echo "Next steps:"

if [[ $HAS_ENV_EMAIL -eq 0 && $HAS_FILE_CREDS -eq 0 ]]; then
    echo "  1. Run: npm run unity:setup-license"
fi

echo "  2. Run: npm run unity:compile"
echo ""

if [[ $FAILED -eq 0 ]]; then
    exit 0
else
    exit 1
fi
