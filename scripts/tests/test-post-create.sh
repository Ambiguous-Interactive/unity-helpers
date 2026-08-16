#!/usr/bin/env bash
# =============================================================================
# Test Script: Post-Create Script Validation
# =============================================================================
# Validates the devcontainer post-create.sh script for correctness:
#   - Shell syntax (bash -n)
#   - Executable permission
#   - Proper shebang line
#   - Volume mount directories in post-create.sh match devcontainer.json mounts
#   - Dockerfile pre-creates the same volume mount directories
#   - devcontainer.json postCreateCommand references the script
#   - No hardcoded user IDs (should use $(id -u) pattern)
#   - Codex installer and wrapper contracts are enforced
#
# This is a data-driven test: volume mount definitions are extracted from
# devcontainer.json and cross-checked against both the setup script and
# the Dockerfile.
#
# Usage:
#   bash scripts/tests/test-post-create.sh
#   bash scripts/tests/test-post-create.sh --verbose
#
# Exit codes:
#   0 - All checks passed
#   1 - One or more checks failed
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

# Ensure temporary directories are removed even on early failures.
cleanup_temp_dirs() {
    [[ -n "${tmp_bin_dir:-}" && -d "$tmp_bin_dir" ]] && rm -rf "$tmp_bin_dir" 2>/dev/null || true
    [[ -n "${tmp_yolo_bin_dir:-}" && -d "$tmp_yolo_bin_dir" ]] && rm -rf "$tmp_yolo_bin_dir" 2>/dev/null || true
}
trap cleanup_temp_dirs EXIT

# Files under test
POST_CREATE="$REPO_ROOT/.devcontainer/post-create.sh"
POST_START="$REPO_ROOT/.devcontainer/post-start.sh"
INSTALL_CODEX="$REPO_ROOT/.devcontainer/install-codex.sh"
CODEX_LOGIN_WRAPPER="$REPO_ROOT/scripts/codex-login.sh"
CODEX_YOLO_WRAPPER="$REPO_ROOT/scripts/codex-yolo.sh"
DEVCONTAINER_JSON="$REPO_ROOT/.devcontainer/devcontainer.json"
DOCKERFILE="$REPO_ROOT/.devcontainer/Dockerfile"
PACKAGE_JSON="$REPO_ROOT/package.json"

# Counters
tests_run=0
tests_passed=0
tests_failed=0
failed_tests=()

# ── Test Helpers ─────────────────────────────────────────────────────────────

pass() {
    local name="$1"
    tests_run=$((tests_run + 1))
    tests_passed=$((tests_passed + 1))
    if [[ $VERBOSE -eq 1 ]]; then
        echo -e "  ${GREEN}PASS${NC} $name"
    fi
}

fail() {
    local name="$1"
    local detail="${2:-}"
    tests_run=$((tests_run + 1))
    tests_failed=$((tests_failed + 1))
    failed_tests+=("$name")
    echo -e "  ${RED}FAIL${NC} $name"
    if [[ -n "$detail" ]]; then
        echo -e "       ${YELLOW}$detail${NC}"
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

assert_bash_script_contracts() {
    local file_path="$1"
    local display_name="$2"
    local shebang
    local index_mode

    if bash -n "$file_path" 2>/dev/null; then
        pass "$display_name passes bash -n syntax check"
    else
        fail "$display_name passes bash -n syntax check" "$(bash -n "$file_path" 2>&1)"
    fi

    index_mode="$(git_index_mode "$file_path")"
    if [[ -x "$file_path" && "$index_mode" == "100755" ]]; then
        pass "$display_name is executable"
    else
        fail "$display_name is executable" "$(permission_diagnostics "$file_path")"
    fi

    shebang=$(head -1 "$file_path")
    if [[ "$shebang" == "#!/usr/bin/env bash" ]] || [[ "$shebang" == "#!/bin/bash" ]]; then
        pass "$display_name has valid bash shebang"
    else
        fail "$display_name has valid bash shebang" "Got: $shebang"
    fi

    if grep -q 'set -euo pipefail' "$file_path"; then
        pass "$display_name uses 'set -euo pipefail'"
    else
        fail "$display_name uses 'set -euo pipefail'" "Missing strict error handling"
    fi
}

# ── Prerequisite Checks ─────────────────────────────────────────────────────

echo -e "${BLUE}── Post-Create Script Validation ──${NC}"
echo ""

if [[ ! -f "$POST_CREATE" ]]; then
    fail "post-create.sh exists" "File not found: .devcontainer/post-create.sh"
    echo ""
    echo -e "${RED}Cannot continue without post-create.sh${NC}"
    exit 1
fi

if [[ ! -f "$POST_START" ]]; then
    fail "post-start.sh exists" "File not found: .devcontainer/post-start.sh"
    echo ""
    echo -e "${RED}Cannot continue without post-start.sh${NC}"
    exit 1
fi

if [[ ! -f "$INSTALL_CODEX" ]]; then
    fail "install-codex.sh exists" "File not found: .devcontainer/install-codex.sh"
    echo ""
    echo -e "${RED}Cannot continue without install-codex.sh${NC}"
    exit 1
fi

if [[ ! -f "$CODEX_LOGIN_WRAPPER" ]]; then
    fail "codex-login.sh exists" "File not found: scripts/codex-login.sh"
    echo ""
    echo -e "${RED}Cannot continue without codex-login.sh${NC}"
    exit 1
fi

if [[ ! -f "$CODEX_YOLO_WRAPPER" ]]; then
    fail "codex-yolo.sh exists" "File not found: scripts/codex-yolo.sh"
    echo ""
    echo -e "${RED}Cannot continue without codex-yolo.sh${NC}"
    exit 1
fi

if [[ ! -f "$DEVCONTAINER_JSON" ]]; then
    fail "devcontainer.json exists" "File not found: .devcontainer/devcontainer.json"
    echo ""
    echo -e "${RED}Cannot continue without devcontainer.json${NC}"
    exit 1
fi

if [[ ! -f "$DOCKERFILE" ]]; then
    fail "Dockerfile exists" "File not found: .devcontainer/Dockerfile"
    echo ""
    echo -e "${RED}Cannot continue without Dockerfile${NC}"
    exit 1
fi

if [[ ! -f "$PACKAGE_JSON" ]]; then
    fail "package.json exists" "File not found: package.json"
    echo ""
    echo -e "${RED}Cannot continue without package.json${NC}"
    exit 1
fi

# ── Test 1: Shell syntax check ───────────────────────────────────────────────

echo -e "${BLUE}Checking script syntax...${NC}"

# ── Test 2: Executable permission ────────────────────────────────────────────

echo -e "${BLUE}Checking permissions...${NC}"

# ── Test 3: Proper shebang ───────────────────────────────────────────────────

echo -e "${BLUE}Checking shebang...${NC}"

# ── Test 4: Uses set -euo pipefail ───────────────────────────────────────────

echo -e "${BLUE}Checking error handling...${NC}"

SCRIPT_CONTRACTS=(
    "$POST_CREATE|post-create.sh"
    "$POST_START|post-start.sh"
    "$INSTALL_CODEX|install-codex.sh"
    "$CODEX_LOGIN_WRAPPER|codex-login.sh"
    "$CODEX_YOLO_WRAPPER|codex-yolo.sh"
)

for contract in "${SCRIPT_CONTRACTS[@]}"; do
    IFS='|' read -r script_path display_name <<< "$contract"
    assert_bash_script_contracts "$script_path" "$display_name"
done

# ── Test 5: No hardcoded UID/GID ─────────────────────────────────────────────

echo -e "${BLUE}Checking for hardcoded UIDs...${NC}"

# The script should use $(id -u):$(id -g), not 1000:1000 or vscode:vscode in chown
if grep -qE 'chown.*1000:1000' "$POST_CREATE"; then
    fail "post-create.sh avoids hardcoded UID 1000" "Found hardcoded 1000:1000 — use \$(id -u):\$(id -g) instead"
else
    pass "post-create.sh avoids hardcoded UID 1000"
fi

# ── Test 6: devcontainer.json references post-create.sh ──────────────────────

echo -e "${BLUE}Checking devcontainer.json integration...${NC}"

if grep -q 'post-create\.sh' "$DEVCONTAINER_JSON"; then
    pass "devcontainer.json postCreateCommand references post-create.sh"
else
    fail "devcontainer.json postCreateCommand references post-create.sh" \
        "postCreateCommand should call bash .devcontainer/post-create.sh"
fi

if grep -q 'post-start\.sh' "$DEVCONTAINER_JSON"; then
    pass "devcontainer.json postStartCommand references post-start.sh"
else
    fail "devcontainer.json postStartCommand references post-start.sh" \
        "postStartCommand should call bash .devcontainer/post-start.sh"
fi

if grep -q 'install-codex\.sh" --force-latest-check' "$POST_CREATE"; then
    pass "post-create.sh forces latest Codex install check"
else
    fail "post-create.sh forces latest Codex install check" \
        "post-create.sh should call install-codex.sh --force-latest-check"
fi

if grep -q 'CODEX_VERSION_TIMEOUT_SECONDS' "$POST_CREATE"; then
    pass "post-create.sh defines Codex version timeout"
else
    fail "post-create.sh defines Codex version timeout" \
        "Expected CODEX_VERSION_TIMEOUT_SECONDS in post-create.sh"
fi

if grep -qE 'timeout[[:space:]]+"\$\{CODEX_VERSION_TIMEOUT_SECONDS\}"[[:space:]]+codex --version' "$POST_CREATE"; then
    pass "post-create.sh bounds codex --version check with timeout"
else
    fail "post-create.sh bounds codex --version check with timeout" \
        "Expected timeout-wrapped codex --version in post-create.sh"
fi

if grep -q 'CODEX_LOGIN_STATUS_TIMEOUT_SECONDS' "$POST_CREATE"; then
    pass "post-create.sh defines Codex login status timeout"
else
    fail "post-create.sh defines Codex login status timeout" \
        "Expected CODEX_LOGIN_STATUS_TIMEOUT_SECONDS in post-create.sh"
fi

if grep -qE 'timeout[[:space:]]+"\$\{CODEX_LOGIN_STATUS_TIMEOUT_SECONDS\}"[[:space:]]+codex login status' "$POST_CREATE"; then
    pass "post-create.sh bounds codex login status check with timeout"
else
    fail "post-create.sh bounds codex login status check with timeout" \
        "Expected timeout-wrapped codex login status in post-create.sh"
fi

if grep -q 'install-codex\.sh' "$POST_START"; then
    pass "post-start.sh calls install-codex.sh"
else
    fail "post-start.sh calls install-codex.sh" \
        "post-start.sh should call install-codex.sh for runtime repair"
fi

if grep -q 'CODEX_VERSION_TIMEOUT_SECONDS' "$POST_START"; then
    pass "post-start.sh defines Codex version timeout"
else
    fail "post-start.sh defines Codex version timeout" \
        "Expected CODEX_VERSION_TIMEOUT_SECONDS in post-start.sh"
fi

if grep -qE 'timeout[[:space:]]+"\$\{CODEX_VERSION_TIMEOUT_SECONDS\}"[[:space:]]+codex --version' "$POST_START"; then
    pass "post-start.sh bounds codex --version check with timeout"
else
    fail "post-start.sh bounds codex --version check with timeout" \
        "Expected timeout-wrapped codex --version in post-start.sh"
fi

if grep -q '@openai/codex' "$INSTALL_CODEX"; then
    pass "install-codex.sh targets @openai/codex"
else
    fail "install-codex.sh targets @openai/codex" \
        "install-codex.sh must install @openai/codex"
fi

if grep -q 'NPM_CONFIG_PREFIX' "$INSTALL_CODEX"; then
    pass "install-codex.sh uses user-global npm prefix"
else
    fail "install-codex.sh uses user-global npm prefix" \
        "Expected NPM_CONFIG_PREFIX usage in install-codex.sh"
fi

if grep -Eq 'timeout[[:space:]]+"\$\{VIEW_TIMEOUT_SECONDS\}"[[:space:]]+npm view' "$INSTALL_CODEX"; then
    pass "install-codex.sh uses timeout for npm latest lookup"
else
    fail "install-codex.sh uses timeout for npm latest lookup" \
        "Expected timeout wrapping npm view"
fi

if grep -Eq 'timeout[[:space:]]+"\$\{INSTALL_TIMEOUT_SECONDS\}"[[:space:]]+npm install -g' "$INSTALL_CODEX"; then
    pass "install-codex.sh uses timeout for npm install"
else
    fail "install-codex.sh uses timeout for npm install" \
        "Expected timeout wrapping npm install"
fi

if grep -q 'for attempt in 1 2 3' "$INSTALL_CODEX"; then
    pass "install-codex.sh retries transient failures"
else
    fail "install-codex.sh retries transient failures" \
        "Expected three-attempt retry loop in install-codex.sh"
fi

if grep -q 'sudo npm install' "$INSTALL_CODEX"; then
    fail "install-codex.sh avoids sudo npm install" \
        "install-codex.sh should use writable user prefix instead of sudo"
else
    pass "install-codex.sh avoids sudo npm install"
fi

if grep -q 'hash -r' "$INSTALL_CODEX"; then
    pass "install-codex.sh refreshes command cache after install"
else
    fail "install-codex.sh refreshes command cache after install" \
        "Expected hash -r usage in install-codex.sh"
fi

if grep -q 'codex-install-failure-state' "$POST_START"; then
    pass "post-start.sh tracks Codex failure state"
else
    fail "post-start.sh tracks Codex failure state" \
        "Expected codex-install-failure-state usage in post-start.sh"
fi

if grep -q 'Skipping Codex retry' "$POST_START"; then
    pass "post-start.sh defers retries after repeated failures"
else
    fail "post-start.sh defers retries after repeated failures" \
        "Expected retry deferral log in post-start.sh"
fi

forward_ports_block="$(awk '/"forwardPorts"[[:space:]]*:/,/\]/ { print }' "$DEVCONTAINER_JSON")"
if grep -q '1455' <<<"$forward_ports_block"; then
    pass "devcontainer.json forwards Codex OAuth callback port 1455"
else
    fail "devcontainer.json forwards Codex OAuth callback port 1455" \
        "Expected forwardPorts to include 1455 for browser callback routing"
fi

if grep -q '"codex:login"' "$PACKAGE_JSON" && grep -q 'scripts/codex-login\.sh' "$PACKAGE_JSON"; then
    pass "package.json exposes codex:login wrapper command"
else
    fail "package.json exposes codex:login wrapper command" \
        "Expected codex:login script mapped to scripts/codex-login.sh"
fi

if grep -q '"codex:login:browser"' "$PACKAGE_JSON" && grep -q 'codex-login\.sh --browser' "$PACKAGE_JSON"; then
    pass "package.json exposes codex:login:browser override"
else
    fail "package.json exposes codex:login:browser override" \
        "Expected codex:login:browser script with --browser override"
fi

if grep -q '"codex:yolo"' "$PACKAGE_JSON" && grep -q 'codex-yolo\.sh' "$PACKAGE_JSON"; then
    pass "package.json exposes codex:yolo safe wrapper"
else
    fail "package.json exposes codex:yolo safe wrapper" \
        "Expected codex:yolo script mapped to scripts/codex-yolo.sh"
fi

if grep -q 'device-auth' "$CODEX_LOGIN_WRAPPER"; then
    fail "codex-login.sh avoids device-auth defaults" \
        "Wrapper should remain browser-first and not auto-select device auth"
else
    pass "codex-login.sh avoids device-auth defaults"
fi

echo -e "${BLUE}Checking codex-login wrapper behavior...${NC}"

tmp_bin_dir="$(mktemp -d)"
cat >"$tmp_bin_dir/codex" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' "$*" >"${CODEX_TEST_OUTPUT_FILE:?}"
EOF
chmod +x "$tmp_bin_dir/codex"

remote_out="$tmp_bin_dir/remote.out"
if CODEX_TEST_OUTPUT_FILE="$remote_out" PATH="$tmp_bin_dir:$PATH" REMOTE_CONTAINERS=1 \
    bash "$CODEX_LOGIN_WRAPPER" >/dev/null 2>&1; then
    if [[ "$(cat "$remote_out")" == "login" ]]; then
        pass "codex-login.sh keeps browser login default in remote context"
    else
        fail "codex-login.sh keeps browser login default in remote context" \
            "Expected 'login', got '$(cat "$remote_out")'"
    fi
else
    fail "codex-login.sh keeps browser login default in remote context" \
        "Wrapper command failed in remote simulation"
fi

browser_out="$tmp_bin_dir/browser.out"
if CODEX_TEST_OUTPUT_FILE="$browser_out" PATH="$tmp_bin_dir:$PATH" REMOTE_CONTAINERS=1 \
    bash "$CODEX_LOGIN_WRAPPER" --browser >/dev/null 2>&1; then
    if [[ "$(cat "$browser_out")" == "login" ]]; then
        pass "codex-login.sh honors --browser override"
    else
        fail "codex-login.sh honors --browser override" \
            "Expected 'login', got '$(cat "$browser_out")'"
    fi
else
    fail "codex-login.sh honors --browser override" \
        "Wrapper command failed with --browser override"
fi

status_out="$tmp_bin_dir/status.out"
if CODEX_TEST_OUTPUT_FILE="$status_out" PATH="$tmp_bin_dir:$PATH" REMOTE_CONTAINERS=1 \
    bash "$CODEX_LOGIN_WRAPPER" status >/dev/null 2>&1; then
    if [[ "$(cat "$status_out")" == "login status" ]]; then
        pass "codex-login.sh forwards explicit login subcommands"
    else
        fail "codex-login.sh forwards explicit login subcommands" \
            "Expected 'login status', got '$(cat "$status_out")'"
    fi
else
    fail "codex-login.sh forwards explicit login subcommands" \
        "Wrapper command failed for explicit subcommand"
fi

local_out="$tmp_bin_dir/local.out"
if CODEX_TEST_OUTPUT_FILE="$local_out" REMOTE_CONTAINERS='' CODESPACES='' VSCODE_IPC_HOOK_CLI='' PATH="$tmp_bin_dir:$PATH" \
    bash "$CODEX_LOGIN_WRAPPER" >/dev/null 2>&1; then
    if [[ "$(cat "$local_out")" == "login" ]]; then
        pass "codex-login.sh keeps browser login default in local context"
    else
        fail "codex-login.sh keeps browser login default in local context" \
            "Expected 'login', got '$(cat "$local_out")'"
    fi
else
    fail "codex-login.sh keeps browser login default in local context" \
        "Wrapper command failed in local simulation"
fi

rm -rf "$tmp_bin_dir"

if grep -qE 'timeout[[:space:]]+"\$\{AUTH_STATUS_TIMEOUT_SECONDS\}"[[:space:]]+codex login status' "$CODEX_YOLO_WRAPPER"; then
    pass "codex-yolo.sh checks auth status with timeout"
else
    fail "codex-yolo.sh checks auth status with timeout" \
        "Expected timeout-wrapped codex login status check"
fi

if grep -qE 'run_with_timeout[[:space:]]+"\$\{EXEC_TIMEOUT_SECONDS\}"[[:space:]]+codex exec --dangerously-bypass-approvals-and-sandbox' "$CODEX_YOLO_WRAPPER"; then
    pass "codex-yolo.sh bounds non-interactive exec duration with timeout"
else
    fail "codex-yolo.sh bounds non-interactive exec duration with timeout" \
        "Expected helper-based timeout wrapper for codex exec fallback"
fi

if grep -qE 'run_with_timeout[[:space:]]+"\$\{EXEC_TIMEOUT_SECONDS\}"[[:space:]]+codex --dangerously-bypass-approvals-and-sandbox' "$CODEX_YOLO_WRAPPER"; then
    pass "codex-yolo.sh bounds interactive yolo duration with timeout"
else
    fail "codex-yolo.sh bounds interactive yolo duration with timeout" \
        "Expected helper-based timeout wrapper for interactive codex yolo"
fi

if grep -qE '\-t 0' "$CODEX_YOLO_WRAPPER" && grep -qE '\-t 1' "$CODEX_YOLO_WRAPPER" && grep -qE '\-t 2' "$CODEX_YOLO_WRAPPER"; then
    pass "codex-yolo.sh detects interactive terminal via tty checks"
else
    fail "codex-yolo.sh detects interactive terminal via tty checks" \
        "Expected -t checks for stdin/stdout/stderr"
fi

if grep -q 'codex exec --dangerously-bypass-approvals-and-sandbox' "$CODEX_YOLO_WRAPPER"; then
    pass "codex-yolo.sh uses codex exec fallback for non-interactive mode"
else
    fail "codex-yolo.sh uses codex exec fallback for non-interactive mode" \
        "Expected codex exec fallback command"
fi

if grep -q 'codex --dangerously-bypass-approvals-and-sandbox' "$CODEX_YOLO_WRAPPER"; then
    pass "codex-yolo.sh keeps interactive yolo mode for tty terminals"
else
    fail "codex-yolo.sh keeps interactive yolo mode for tty terminals" \
        "Expected interactive codex --dangerously-bypass-approvals-and-sandbox command"
fi

if grep -q 'device-auth' "$CODEX_YOLO_WRAPPER"; then
    fail "codex-yolo.sh avoids device-auth defaults" \
        "Wrapper should remain browser-first and not auto-select device auth"
else
    pass "codex-yolo.sh avoids device-auth defaults"
fi

echo -e "${BLUE}Checking codex-yolo wrapper behavior...${NC}"

tmp_yolo_bin_dir="$(mktemp -d)"
cat >"$tmp_yolo_bin_dir/codex" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ge 2 && "$1" == "login" && "$2" == "status" ]]; then
    case "${CODEX_TEST_LOGIN_STATUS:-logged-in}" in
        logged-in)
            echo "Logged in"
            exit 0
            ;;
        *)
            echo "Not logged in"
            exit 1
            ;;
    esac
fi

printf '%s\n' "$*" >"${CODEX_TEST_OUTPUT_FILE:?}"
EOF
chmod +x "$tmp_yolo_bin_dir/codex"

yolo_exec_out="$tmp_yolo_bin_dir/yolo-exec.out"
if CODEX_TEST_LOGIN_STATUS="logged-in" CODEX_TEST_OUTPUT_FILE="$yolo_exec_out" PATH="$tmp_yolo_bin_dir:$PATH" \
    bash "$CODEX_YOLO_WRAPPER" "say hello" >/dev/null 2>&1; then
    if [[ "$(cat "$yolo_exec_out")" == "exec --dangerously-bypass-approvals-and-sandbox say hello" ]]; then
        pass "codex-yolo.sh routes non-tty prompts through codex exec"
    else
        fail "codex-yolo.sh routes non-tty prompts through codex exec" \
            "Expected codex exec fallback, got '$(cat "$yolo_exec_out")'"
    fi
else
    fail "codex-yolo.sh routes non-tty prompts through codex exec" \
        "Wrapper command failed in non-tty simulation"
fi

no_args_err="$tmp_yolo_bin_dir/no-args.err"
if CODEX_TEST_LOGIN_STATUS="logged-in" CODEX_TEST_OUTPUT_FILE="$tmp_yolo_bin_dir/no-args.out" PATH="$tmp_yolo_bin_dir:$PATH" \
    bash "$CODEX_YOLO_WRAPPER" >/dev/null 2>"$no_args_err"; then
    fail "codex-yolo.sh rejects non-tty invocation without prompt" \
        "Wrapper should fail when no prompt is provided in non-tty mode"
else
    if grep -qi 'non-interactive usage requires a prompt' "$no_args_err"; then
        pass "codex-yolo.sh rejects non-tty invocation without prompt"
    else
        fail "codex-yolo.sh rejects non-tty invocation without prompt" \
            "Expected prompt guidance, got '$(cat "$no_args_err")'"
    fi
fi

unauth_err="$tmp_yolo_bin_dir/unauth.err"
unauth_out="$tmp_yolo_bin_dir/unauth.out"
if CODEX_TEST_LOGIN_STATUS="not-logged-in" CODEX_TEST_OUTPUT_FILE="$unauth_out" PATH="$tmp_yolo_bin_dir:$PATH" \
    bash "$CODEX_YOLO_WRAPPER" "say hello" >/dev/null 2>"$unauth_err"; then
    fail "codex-yolo.sh fails fast when not authenticated" \
        "Wrapper should reject yolo invocation when not logged in"
else
    if grep -qi 'authentication is required' "$unauth_err" && [[ ! -s "$unauth_out" ]]; then
        pass "codex-yolo.sh fails fast when not authenticated"
    else
        fail "codex-yolo.sh fails fast when not authenticated" \
            "Expected auth guidance without codex execution"
    fi
fi

api_key_out="$tmp_yolo_bin_dir/api-key.out"
if OPENAI_API_KEY="test-key" CODEX_TEST_LOGIN_STATUS="not-logged-in" CODEX_TEST_OUTPUT_FILE="$api_key_out" PATH="$tmp_yolo_bin_dir:$PATH" \
    bash "$CODEX_YOLO_WRAPPER" "say hello" >/dev/null 2>&1; then
    if [[ "$(cat "$api_key_out")" == "exec --dangerously-bypass-approvals-and-sandbox say hello" ]]; then
        pass "codex-yolo.sh allows API key auth without login status"
    else
        fail "codex-yolo.sh allows API key auth without login status" \
            "Expected codex exec fallback with API key"
    fi
else
    fail "codex-yolo.sh allows API key auth without login status" \
        "Wrapper command failed with API key override"
fi

rm -rf "$tmp_yolo_bin_dir"

# ── Test 7: Data-driven volume mount cross-check ─────────────────────────────
# Extract volume mount targets from devcontainer.json and verify they are
# handled in both post-create.sh and Dockerfile.

echo -e "${BLUE}Cross-checking volume mount directories...${NC}"

# System-managed volumes that are intentionally root-owned and do NOT need
# pre-creation or chown. These are managed by devcontainer features (e.g.,
# Docker-in-Docker manages /var/lib/docker).
SYSTEM_MANAGED_PATHS=("/var/lib/docker")

is_system_managed() {
    local target="$1"
    for sys_path in "${SYSTEM_MANAGED_PATHS[@]}"; do
        if [[ "$target" == "$sys_path" ]]; then
            return 0
        fi
    done
    return 1
}

# Extract volume mount target paths from devcontainer.json
# Matches entries containing both type=volume and target=/some/path
VOLUME_TARGETS=()
while IFS= read -r line; do
    if [[ "$line" =~ type=volume ]] && [[ "$line" =~ target=([^,\"]+) ]]; then
        VOLUME_TARGETS+=("${BASH_REMATCH[1]}")
    fi
done < "$DEVCONTAINER_JSON"

if [[ ${#VOLUME_TARGETS[@]} -eq 0 ]]; then
    fail "devcontainer.json has volume mounts" "No volume mounts found"
else
    pass "devcontainer.json has ${#VOLUME_TARGETS[@]} volume mount(s)"

    post_create_body="$(grep -v '^[[:space:]]*#' "$POST_CREATE" || true)"
    dockerfile_body="$(grep -v '^[[:space:]]*#' "$DOCKERFILE" || true)"

    for target in "${VOLUME_TARGETS[@]}"; do
        # Skip system-managed volumes (e.g., /var/lib/docker from DinD feature)
        if is_system_managed "$target"; then
            pass "volume target is system-managed (skip chown check): $target"
            continue
        fi

        # The script may reference the target directly or its parent directory.
        # e.g., /home/vscode/.nuget/packages is covered by /home/vscode/.nuget

        # Check post-create.sh references this path in a chown/VOLUME_DIRS context
        # (not just in comments). Reuse precomputed non-comment bodies to avoid
        # writer -> grep -q pipelines under `set -o pipefail`; a successful match
        # must not be turned into a false negative by SIGPIPE.

        found_in_script=false
        check_path="$target"
        while [[ "$check_path" != "/" && "$check_path" != "/home/vscode" && "$check_path" != "/home" ]]; do
            if grep -qF -- "$check_path" <<<"$post_create_body"; then
                found_in_script=true
                break
            fi
            check_path="$(dirname "$check_path")"
        done

        if [[ "$found_in_script" == true ]]; then
            pass "post-create.sh handles volume target: $target"
        else
            fail "post-create.sh handles volume target: $target" \
                "Volume mount target not found in post-create.sh chown commands"
        fi

        # Check Dockerfile pre-creates this path (or a parent) in mkdir/chown context
        found_in_dockerfile=false
        check_path="$target"
        while [[ "$check_path" != "/" && "$check_path" != "/home/vscode" && "$check_path" != "/home" ]]; do
            if grep -qF -- "$check_path" <<<"$dockerfile_body"; then
                found_in_dockerfile=true
                break
            fi
            check_path="$(dirname "$check_path")"
        done

        if [[ "$found_in_dockerfile" == true ]]; then
            pass "Dockerfile pre-creates volume target: $target"
        else
            fail "Dockerfile pre-creates volume target: $target" \
                "Volume mount target not found in Dockerfile mkdir/chown commands"
        fi
    done
fi

# ── Test 8: chown runs before tool commands ──────────────────────────────────
# The chown must appear before dotnet/npm commands in the script.

echo -e "${BLUE}Checking command ordering...${NC}"

# Only check non-comment lines (skip lines starting with optional whitespace + #)
CHOWN_LINE=$(grep -n 'sudo chown' "$POST_CREATE" | grep -vE '^[[:space:]]*[0-9]*:[[:space:]]*#' | head -1 | cut -d: -f1 || true)
DOTNET_LINE=$(grep -n 'dotnet tool restore' "$POST_CREATE" | grep -vE '^[[:space:]]*[0-9]*:[[:space:]]*#' | head -1 | cut -d: -f1 || true)
NPM_LINE=$(grep -nE 'npm ci|npm i ' "$POST_CREATE" | grep -vE '^[[:space:]]*[0-9]*:[[:space:]]*#' | head -1 | cut -d: -f1 || true)

if [[ -n "$CHOWN_LINE" && -n "$DOTNET_LINE" ]]; then
    if [[ "$CHOWN_LINE" -lt "$DOTNET_LINE" ]]; then
        pass "chown runs before dotnet tool restore (line $CHOWN_LINE < $DOTNET_LINE)"
    else
        fail "chown runs before dotnet tool restore" \
            "chown on line $CHOWN_LINE, dotnet on line $DOTNET_LINE"
    fi
else
    # Each marker is reported by name. Before these lookups were guarded against a SIGPIPE from the
    # short-circuiting `head`, a missing marker made the command substitution non-zero and `set -e`
    # killed the whole suite with no message; guarding it without these branches would have traded
    # that for a silently skipped ordering assertion, which is worse than either.
    if [[ -z "$CHOWN_LINE" ]]; then
        fail "chown command exists in post-create.sh" "No sudo chown found"
    fi
    if [[ -z "$DOTNET_LINE" ]]; then
        fail "dotnet tool restore exists in post-create.sh" "No dotnet tool restore found"
    fi
fi

if [[ -n "$CHOWN_LINE" && -n "$NPM_LINE" ]]; then
    if [[ "$CHOWN_LINE" -lt "$NPM_LINE" ]]; then
        pass "chown runs before npm install (line $CHOWN_LINE < $NPM_LINE)"
    else
        fail "chown runs before npm install" \
            "chown on line $CHOWN_LINE, npm on line $NPM_LINE"
    fi
elif [[ -z "$NPM_LINE" ]]; then
    fail "npm install exists in post-create.sh" "No npm ci / npm i found"
fi

# ── Test 9: Script handles workspace folder detection ────────────────────────

echo -e "${BLUE}Checking workspace directory handling...${NC}"

if grep -qE 'git rev-parse --show-toplevel|CODESPACE_VSCODE_FOLDER' "$POST_CREATE"; then
    pass "post-create.sh detects workspace directory dynamically"
else
    fail "post-create.sh detects workspace directory dynamically" \
        "Should use git rev-parse or CODESPACE_VSCODE_FOLDER for detection"
fi

# ── Test 10: chown uses sudo ─────────────────────────────────────────────────

echo -e "${BLUE}Checking sudo usage...${NC}"

# chown on volume dirs requires sudo since we run as vscode user
if grep -q 'sudo chown' <<<"$post_create_body"; then
    pass "post-create.sh uses sudo with chown"
else
    fail "post-create.sh uses sudo with chown" \
        "chown on volume mount dirs requires sudo (running as non-root vscode user)"
fi

# ── Test 11: sudo availability guard ─────────────────────────────────────────

if grep -q 'command -v sudo' "$POST_CREATE"; then
    pass "post-create.sh checks for sudo availability"
else
    fail "post-create.sh checks for sudo availability" \
        "Script should verify sudo exists before using it"
fi

# ── Test 12: shellcheck (if available) ───────────────────────────────────────

echo -e "${BLUE}Checking shellcheck...${NC}"

if command -v shellcheck >/dev/null 2>&1; then
    if shellcheck_output=$(shellcheck "$POST_CREATE" 2>&1); then
        pass "post-create.sh passes shellcheck"
    else
        fail "post-create.sh passes shellcheck" "$shellcheck_output"
    fi
else
    pass "shellcheck not available (skipped)"
fi

# ── Summary ──────────────────────────────────────────────────────────────────

echo ""
echo -e "${BLUE}── Summary ──${NC}"
echo -e "  Tests run:    $tests_run"
echo -e "  ${GREEN}Passed${NC}:      $tests_passed"
echo -e "  ${RED}Failed${NC}:      $tests_failed"

if [[ $tests_failed -gt 0 ]]; then
    echo ""
    echo -e "${RED}Failed tests:${NC}"
    for t in "${failed_tests[@]}"; do
        echo -e "  ${RED}-${NC} $t"
    done
    exit 1
else
    echo ""
    echo -e "${GREEN}All post-create script checks passed!${NC}"
    exit 0
fi
