#!/usr/bin/env bash
# Post-start setup script for the devcontainer.
# Runs as the remoteUser (vscode) after each container start.
#
# This script keeps the AI coding agent CLIs and MCP runtimes available across
# restarts, repairs volume mount ownership that Docker may have reset, and avoids
# unnecessary npm registry calls on every start.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NPM_PREFIX="${NPM_CONFIG_PREFIX:-${HOME}/.local}"
STATE_DIR="${HOME}/.cache/unity-helpers-devcontainer"
FAIL_STATE_FILE="${STATE_DIR}/agent-clis-install-failure-state"
BASE_BACKOFF_SECONDS="${CODEX_RETRY_BASE_BACKOFF_SECONDS:-60}"
MAX_BACKOFF_SECONDS="${CODEX_RETRY_MAX_BACKOFF_SECONDS:-21600}"
CODEX_VERSION_TIMEOUT_SECONDS="${CODEX_VERSION_TIMEOUT_SECONDS:-10}"

export PATH="${NPM_PREFIX}/bin:${PATH}"

log_step() {
    echo ""
    echo "==> $1"
}

log_ok() {
    echo "    OK: $1"
}

log_warn() {
    echo "    WARN: $1" >&2
}

# ── Volume ownership self-heal ───────────────────────────────────────────────
# Docker named volumes only inherit image-layer ownership when they are EMPTY.
# A volume recreated by Docker Desktop (prune, factory reset, disk pressure)
# comes back root:root, and every npm/dotnet/pip write then fails with EACCES.
# Left unfixed, that is how developers end up "fixing" npm with sudo, which
# makes the cache root-owned and keeps the cycle going. Repairing it here keeps
# plain `npm install` working on every start, sudo-free.

log_step "Verifying volume mount ownership"

USER_OWNED_DIRS=(
    "${HOME}/.npm"
    "${HOME}/.local"
    "${HOME}/.nuget/packages"
    "${HOME}/.cache/pip"
    "${HOME}/.cache/unity-helpers-devcontainer"
    "${HOME}/.unity-test-project"
)

for dir in "${USER_OWNED_DIRS[@]}"; do
    if [ -d "$dir" ] && [ ! -w "$dir" ]; then
        if sudo chown -R "$(id -u):$(id -g)" "$dir" 2>/dev/null; then
            log_ok "Repaired ownership: $dir"
        else
            log_warn "Could not repair ownership of $dir (non-fatal)."
        fi
    fi
done
log_ok "Volume mount ownership verified"

load_failure_state() {
    failure_count=0
    next_retry_epoch=0

    if [ ! -f "$FAIL_STATE_FILE" ]; then
        return 0
    fi

    local saved_count saved_next
    saved_count=""
    saved_next=""
    IFS=' ' read -r saved_count saved_next <"$FAIL_STATE_FILE" || true

    if [[ "$saved_count" =~ ^[0-9]+$ ]]; then
        failure_count="$saved_count"
    fi

    if [[ "$saved_next" =~ ^[0-9]+$ ]]; then
        next_retry_epoch="$saved_next"
    fi
}

save_failure_state() {
    printf '%s %s\n' "$failure_count" "$next_retry_epoch" >"$FAIL_STATE_FILE"
}

clear_failure_state() {
    rm -f "$FAIL_STATE_FILE"
}

retry_is_deferred() {
    local now
    now="$(date +%s)"

    if [ "$now" -lt "$next_retry_epoch" ]; then
        local remaining
        remaining=$((next_retry_epoch - now))
        log_warn "Skipping agent CLI retry for ${remaining}s due to previous failures."
        return 0
    fi

    return 1
}

record_failure_and_backoff() {
    local now backoff exponent
    now="$(date +%s)"

    failure_count=$((failure_count + 1))
    exponent=$((failure_count - 1))
    if [ "$exponent" -gt 16 ]; then
        exponent=16
    fi

    backoff=$((BASE_BACKOFF_SECONDS * (2 ** exponent)))
    if [ "$backoff" -gt "$MAX_BACKOFF_SECONDS" ]; then
        backoff="$MAX_BACKOFF_SECONDS"
    fi

    next_retry_epoch=$((now + backoff))
    save_failure_state
    log_warn "Agent CLI verification failed (consecutive failures: ${failure_count}). Next retry in ${backoff}s."
}

# Runs on EVERY start, and deliberately before the agent CLI block: Dev Containers copies the host's
# git config in at attach time, so the duplicate credential.helper and the host's Windows
# safe.directory entries come back on every attach, not just at create. It is also above the
# `retry_is_deferred` early exit below -- placed after it, a deferred retry would skip this
# and leave the container prompting twice for every credential.
# The postcondition is verified rather than assumed (#600). A swallowed failure here is invisible
# for the rest of the session -- github-token.sh still answers, the API still works, `git fetch`
# still works -- and surfaces hours later as a `git push` that hangs for its full timeout while a
# dialog waits on the owner's desktop. The three states are reported separately, because "the step
# was skipped" and "the step ran and did not take" need different fixes.
log_step "Normalizing container git config"
if bash "$SCRIPT_DIR/../scripts/normalize-container-git-config.sh"; then
    normalization_ran=true
else
    normalization_ran=false
    log_warn "scripts/normalize-container-git-config.sh failed (non-fatal); verifying anyway."
fi

if bash "$SCRIPT_DIR/../scripts/check-container-git-credentials.sh" --quiet; then
    if [ "$normalization_ran" = true ]; then
        log_ok "Container git config normalized; github.com resolves through scripts/github-token.sh"
    else
        log_warn "Normalization failed but github.com still resolves through scripts/github-token.sh."
    fi
else
    log_warn "github.com does NOT resolve through scripts/github-token.sh; \`git push\` will hang."
    log_warn "Run: bash scripts/check-container-git-credentials.sh --fix"
fi

log_step "Verifying AI coding agent CLIs and MCP runtimes"

mkdir -p "$STATE_DIR"
load_failure_state

if ! retry_is_deferred; then
    # Installer is non-fatal by design; verify command availability explicitly.
    bash "$SCRIPT_DIR/install-agent-clis.sh" || true
    all_packages_available=true
    for agent_bin in codex opencode nanocoder; do
        if command -v "$agent_bin" >/dev/null 2>&1 && timeout "${CODEX_VERSION_TIMEOUT_SECONDS}" "$agent_bin" --version >/dev/null 2>&1; then
            log_ok "$agent_bin CLI is available"
        else
            all_packages_available=false
            log_warn "$agent_bin CLI verification failed (non-fatal)."
        fi
    done
    for mcp_bin in zai-mcp-server mcp-remote; do
        if command -v "$mcp_bin" >/dev/null 2>&1; then
            log_ok "$mcp_bin MCP runtime is available"
        else
            all_packages_available=false
            log_warn "$mcp_bin MCP runtime verification failed (non-fatal)."
        fi
    done

    if [ "$all_packages_available" = true ]; then
        clear_failure_state
        log_ok "All agent CLIs and MCP runtimes are available"
    else
        record_failure_and_backoff
        log_warn "Package verification failed (non-fatal). Re-run: bash .devcontainer/install-agent-clis.sh --force-latest-check"
    fi
fi

log_step "Syncing MCP client configs"

MCP_REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
if node "${MCP_REPO_ROOT}/scripts/mcp/unity-mcp.mjs" configure-shared >/dev/null 2>&1; then
    log_ok "GitHub and Z.AI MCP client configs written"
else
    log_warn "Could not sync shared MCP client configs (non-fatal). Run: node scripts/mcp/unity-mcp.mjs configure-shared"
fi
if node "${MCP_REPO_ROOT}/scripts/mcp/unity-mcp.mjs" configure --no-discover >/dev/null 2>&1; then
    log_ok "Unity MCP client configs written"
else
    log_warn "Could not sync Unity MCP client configs (non-fatal). Run: npm run unity:mcp:configure"
fi

exit 0
