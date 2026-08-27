#!/usr/bin/env bash
# shellcheck shell=bash
# =============================================================================
# install-agent-clis.sh
# -----------------------------------------------------------------------------
# Idempotently install the latest AI coding agent CLIs as user-global npm
# packages. This script is safe to run on every container start.
#
# Agents installed:
#   * @openai/codex                (bin: codex)
#   * opencode-ai                  (bin: opencode)
#   * @nanocollective/nanocoder    (bin: nanocoder)
#
# Behavior:
#   * Resolves each package's npm latest dist-tag with a bounded timeout.
#   * Skips install when the current version already matches latest.
#   * Installs into NPM_CONFIG_PREFIX (default: $HOME/.local) without sudo.
#   * Retries transient failures with small backoff.
#   * Never fails callers; offline and registry errors are non-fatal.
#   * One missing agent never blocks installing the others.
# =============================================================================

set -euo pipefail

AGENTS=(
    "@openai/codex|codex"
    "opencode-ai|opencode"
    "@nanocollective/nanocoder|nanocoder"
)
NPM_PREFIX="${NPM_CONFIG_PREFIX:-${HOME}/.local}"
VIEW_TIMEOUT_SECONDS="${CODEX_NPM_VIEW_TIMEOUT_SECONDS:-20}"
INSTALL_TIMEOUT_SECONDS="${CODEX_NPM_INSTALL_TIMEOUT_SECONDS:-300}"
LOG_PREFIX="[install-agent-clis]"

log() {
    echo "${LOG_PREFIX} $*"
}

warn() {
    echo "${LOG_PREFIX} WARN: $*" >&2
}

usage() {
    cat <<'EOF'
Usage: install-agent-clis.sh [--force-latest-check]

Options:
  --force-latest-check   Accepted for compatibility. Latest is always checked.
EOF
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --force-latest-check)
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            warn "Unknown argument '$1'; ignoring."
            ;;
    esac
    shift
done

if ! command -v npm >/dev/null 2>&1; then
    warn "npm not found; skipping agent CLI installs."
    exit 0
fi

export NPM_CONFIG_PREFIX="${NPM_PREFIX}"
export PATH="${NPM_PREFIX}/bin:${PATH}"

if ! mkdir -p "${NPM_PREFIX}"; then
    warn "Unable to create npm prefix directory: ${NPM_PREFIX}."
    exit 0
fi

if [[ ! -w "${NPM_PREFIX}" ]]; then
    warn "npm prefix '${NPM_PREFIX}' is not writable; skipping install."
    warn "Run: sudo chown -R \"$(id -u):$(id -g)\" '${NPM_PREFIX}'"
    exit 0
fi

# Read the currently installed version from the user-global install location.
installed_version() {
    local pkg="$1"
    local pkg_json="${NPM_PREFIX}/lib/node_modules/${pkg}/package.json"
    if [[ -f "${pkg_json}" ]]; then
        if command -v jq >/dev/null 2>&1; then
            jq -r '.version // empty' "${pkg_json}" 2>/dev/null || true
        else
            grep -m1 '"version"' "${pkg_json}" | sed -E 's/.*"version"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/' || true
        fi
    fi
}

latest_version() {
    local pkg="$1"
    timeout "${VIEW_TIMEOUT_SECONDS}" npm view "${pkg}" version 2>/dev/null | tr -d '[:space:]' || true
}

# Returns 0 when the agent is installed and its binary resolves.
verify_agent() {
    local bin="$1"
    command -v "${bin}" >/dev/null 2>&1
}

install_agent() {
    local pkg="$1" bin="$2" latest="$3"
    local attempt
    for attempt in 1 2 3; do
        if timeout "${INSTALL_TIMEOUT_SECONDS}" npm install -g "${pkg}@${latest}" --silent --no-fund --no-audit; then
            hash -r 2>/dev/null || true
            if verify_agent "${bin}"; then
                log "${pkg} ready: $(${bin} --version 2>/dev/null | head -1 || echo "${latest}")"
                return 0
            fi
            warn "${bin} binary missing from PATH after install attempt ${attempt}/3."
        else
            warn "npm install failed for ${pkg} (attempt ${attempt}/3)."
        fi
        sleep "$((2 ** (attempt - 1)))"
    done
    warn "Failed to install ${pkg} after 3 attempts; continuing without it."
    return 1
}

failures=0

for agent in "${AGENTS[@]}"; do
    pkg="${agent%%|*}"
    bin="${agent##*|}"

    installed="$(installed_version "${pkg}")"
    latest="$(latest_version "${pkg}")"

    if [[ -z "${latest}" ]]; then
        if [[ -n "${installed}" ]] && verify_agent "${bin}"; then
            log "Registry unreachable; keeping installed ${pkg}@${installed}."
        else
            warn "Registry unreachable and ${bin} is not installed; will retry later."
            failures=$((failures + 1))
        fi
        continue
    fi

    if [[ "${installed}" == "${latest}" ]]; then
        if verify_agent "${bin}"; then
            log "${pkg}@${installed} already up-to-date."
            continue
        fi
        warn "${pkg}@${installed} is registered but ${bin} is not on PATH; reinstalling."
    fi

    log "Installing ${pkg}@${latest} (previously: ${installed:-not installed})"
    if ! install_agent "${pkg}" "${bin}" "${latest}"; then
        failures=$((failures + 1))
    fi
done

if [ "${failures}" -gt 0 ]; then
    warn "${failures} agent CLI(s) could not be verified; continuing without them."
fi

exit 0
