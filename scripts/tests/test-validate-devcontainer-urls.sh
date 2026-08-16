#!/usr/bin/env bash
# =============================================================================
# Test Script: validate-devcontainer-urls parser + contract checks
# =============================================================================
# Validates that scripts/validate-devcontainer-urls.sh continues to:
# 1) Discover required tool URL definitions from .devcontainer/Dockerfile.
# 2) Reject apt-based PowerShell installs in contract-only mode.
#
# Run: bash scripts/tests/test-validate-devcontainer-urls.sh
# Exit codes: 0 = all tests pass, 1 = one or more tests fail
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
DOCKERFILE="$REPO_ROOT/.devcontainer/Dockerfile"
VALIDATOR="$REPO_ROOT/scripts/validate-devcontainer-urls.sh"

RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
NC='\033[0m'

tests_run=0
tests_passed=0
tests_failed=0

pass() {
    tests_run=$((tests_run + 1))
    tests_passed=$((tests_passed + 1))
    echo -e "  ${GREEN}PASS${NC} $1"
}

fail() {
    tests_run=$((tests_run + 1))
    tests_failed=$((tests_failed + 1))
    echo -e "  ${RED}FAIL${NC} $1"
    if [[ -n "${2:-}" ]]; then
        echo -e "       ${RED}$2${NC}"
    fi
}

assert_file_contains_literal() {
    local file_path="$1"
    local needle="$2"
    local test_name="$3"
    local failure_message="$4"

    if grep -Fq -- "$needle" "$file_path"; then
        pass "$test_name"
    else
        fail "$test_name" "$failure_message (literal: $needle)"
    fi
}

assert_contract_exit_code() {
    local fixture="$1"
    local expected_rc="$2"
    local test_name="$3"
    local rc=0

    if "$VALIDATOR" --contracts-only --dockerfile "$fixture" >/dev/null 2>&1; then
        rc=0
    else
        rc=$?
    fi

    if [[ "$rc" -eq "$expected_rc" ]]; then
        pass "$test_name"
    else
        fail "$test_name" "Expected exit code ${expected_rc}, got ${rc}"
    fi
}

relative_path() {
    local file_path="$1"
    printf '%s\n' "${file_path#"$REPO_ROOT"/}"
}

git_index_mode() {
    local file_path="$1"
    local rel_path
    rel_path="$(relative_path "$file_path")"

    git -C "$REPO_ROOT" ls-files -s -- "$rel_path" 2>/dev/null | sed -E 's/^([0-9]+).*/\1/' | head -n 1 || true
}

permission_diagnostics() {
    local file_path="$1"
    local rel_path
    local local_mode
    local index_entry
    rel_path="$(relative_path "$file_path")"
    local_mode="$(stat -c '%A %a' "$file_path" 2>/dev/null || ls -l "$file_path" 2>/dev/null || echo 'unavailable')"
    index_entry="$(git -C "$REPO_ROOT" ls-files -s -- "$rel_path" 2>/dev/null || true)"

    printf 'Filesystem mode: %s; git index: %s' "$local_mode" "${index_entry:-untracked}"
}

TMP_DIR="$(mktemp -d)"
cleanup() {
    rm -rf "$TMP_DIR"
}
trap cleanup EXIT

echo -e "${BLUE}── validate-devcontainer-urls parser contracts ──${NC}"
echo ""

if [[ -x "$VALIDATOR" && "$(git_index_mode "$VALIDATOR")" == "100755" ]]; then
    pass "validate-devcontainer-urls.sh is executable"
else
    fail "validate-devcontainer-urls.sh is executable" "$(permission_diagnostics "$VALIDATOR")"
fi

if [[ -f "$DOCKERFILE" ]]; then
    pass ".devcontainer/Dockerfile exists"
else
    fail ".devcontainer/Dockerfile exists" "Missing .devcontainer/Dockerfile"
fi

assert_file_contains_literal "$DOCKERFILE" \
    'POWERSHELL_VERSION=' \
    "Dockerfile defines POWERSHELL_VERSION for release URL validation" \
    "Missing POWERSHELL_VERSION assignment"

assert_file_contains_literal "$DOCKERFILE" \
    'https://github.com/PowerShell/PowerShell/releases/download/v${POWERSHELL_VERSION}/powershell-${POWERSHELL_VERSION}-linux-${POWERSHELL_ARCH}.tar.gz' \
    "Dockerfile contains inline PowerShell release URL template" \
    "Expected inline PowerShell GitHub release URL pattern"

if grep -Fq -- 'amd64) POWERSHELL_ARCH="x64"' "$DOCKERFILE" \
    && grep -Fq -- 'arm64) POWERSHELL_ARCH="arm64"' "$DOCKERFILE"; then
    pass "Dockerfile maps TARGETARCH to PowerShell asset architecture"
else
    fail "Dockerfile maps TARGETARCH to PowerShell asset architecture" "Expected amd64->x64 and arm64->arm64 mappings"
fi

assert_file_contains_literal "$DOCKERFILE" \
    'sha256sum -c -' \
    "Dockerfile verifies PowerShell tarball checksum" \
    "Expected checksum verification for PowerShell download"

# Contract: validator parser should still discover powershell tool entry from Dockerfile without live URL probes.
if validator_output="$("$VALIDATOR" --list-tools --dockerfile .devcontainer/Dockerfile 2>&1)"; then
    if grep -Fiq -- 'powershell|' <<< "$validator_output"; then
        pass "validator output includes powershell tool entry"
    else
        fail "validator output includes powershell tool entry" "Validator succeeded but did not report powershell entry. Output: $validator_output"
    fi
else
    fail "validator lists tools from current Dockerfile" "$validator_output"
fi

echo ""
echo -e "${BLUE}── apt-based PowerShell regression contracts ──${NC}"

fixture_fail="$TMP_DIR/fail-apt-powershell.Dockerfile"
fixture_pass="$TMP_DIR/pass-release-tarball.Dockerfile"
fixture_comment_ok="$TMP_DIR/pass-commented-apt.Dockerfile"
fixture_case_fail="$TMP_DIR/fail-case-variant-apt.Dockerfile"
fixture_chain_fail="$TMP_DIR/fail-chained-apt.Dockerfile"

cat > "$fixture_fail" <<'EOT'
FROM debian:12
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
    git \
    powershell \
    curl
EOT

cat > "$fixture_pass" <<'EOT'
FROM debian:12
ARG TARGETARCH
RUN POWERSHELL_VERSION="7.5.3" \
    && case "${TARGETARCH}" in \
        amd64) POWERSHELL_ARCH="x64" ;; \
        arm64) POWERSHELL_ARCH="arm64" ;; \
        *) exit 1 ;; \
    esac \
    && curl -fsSL "https://github.com/PowerShell/PowerShell/releases/download/v${POWERSHELL_VERSION}/powershell-${POWERSHELL_VERSION}-linux-${POWERSHELL_ARCH}.tar.gz" \
    | tar -xz -C /opt/pwsh
EOT

cat > "$fixture_comment_ok" <<'EOT'
FROM debian:12
# RUN apt-get install -y powershell
RUN echo "no apt powershell here"
EOT

cat > "$fixture_case_fail" <<'EOT'
FROM debian:12
RUN apt-get install -y PowerShell
EOT

cat > "$fixture_chain_fail" <<'EOT'
FROM debian:12
RUN apt-get update && apt-get install -y git && apt-get install -y powershell
EOT

assert_contract_exit_code "$fixture_fail" 1 "validator rejects apt-based powershell install"
assert_contract_exit_code "$fixture_pass" 0 "validator allows release tarball install"
assert_contract_exit_code "$fixture_comment_ok" 0 "validator ignores commented apt install"
assert_contract_exit_code "$fixture_case_fail" 1 "validator rejects case-variant apt powershell install"
assert_contract_exit_code "$fixture_chain_fail" 1 "validator rejects chained apt powershell install"

echo ""
echo -e "${BLUE}── Summary ──${NC}"
echo "  Tests run:    $tests_run"
echo -e "  ${GREEN}Passed${NC}:      $tests_passed"
echo -e "  ${RED}Failed${NC}:      $tests_failed"

if [[ $tests_failed -gt 0 ]]; then
    exit 1
fi

exit 0
