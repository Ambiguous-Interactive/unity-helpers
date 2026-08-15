#!/usr/bin/env bash
# Post-start setup script for the devcontainer.
# Runs as the remoteUser (vscode) after each container start.
#
# This script keeps OpenAI Codex CLI availability healthy across restarts,
# while avoiding unnecessary npm registry calls on every start.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NPM_PREFIX="${NPM_CONFIG_PREFIX:-${HOME}/.local}"
STATE_DIR="${HOME}/.cache/unity-helpers-devcontainer"
FAIL_STATE_FILE="${STATE_DIR}/codex-install-failure-state"
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
        log_warn "Skipping Codex retry for ${remaining}s due to previous failures."
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
    log_warn "Codex verification failed (consecutive failures: ${failure_count}). Next retry in ${backoff}s."
}

# Runs on EVERY start, and deliberately before the Codex block: Dev Containers copies the host's
# git config in at attach time, so the duplicate credential.helper and the host's Windows
# safe.directory entries come back on every attach, not just at create. It is also above the
# `retry_is_deferred` early exit below -- placed after it, a deferred Codex retry would skip this
# and leave the container prompting twice for every credential.
log_step "Normalizing container git config"
bash "$SCRIPT_DIR/../scripts/normalize-container-git-config.sh" \
    || log_warn "Could not normalize container git config (non-fatal)"

log_step "Verifying OpenAI Codex CLI"

mkdir -p "$STATE_DIR"
load_failure_state

if retry_is_deferred; then
    exit 0
fi

# Installer is non-fatal by design; verify command availability explicitly.
if bash "$SCRIPT_DIR/install-codex.sh" && command -v codex >/dev/null 2>&1 && timeout "${CODEX_VERSION_TIMEOUT_SECONDS}" codex --version >/dev/null 2>&1; then
    clear_failure_state
    log_ok "OpenAI Codex CLI is available"
else
    record_failure_and_backoff
    log_warn "OpenAI Codex CLI verification failed (non-fatal). Re-run: bash .devcontainer/install-codex.sh --force-latest-check"
fi

exit 0
