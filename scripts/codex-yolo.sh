#!/usr/bin/env bash
# Safe wrapper for Codex "yolo" mode.
#
# Interactive TTY terminals use `codex --dangerously-bypass-approvals-and-sandbox`.
# Non-TTY contexts switch to `codex exec --dangerously-bypass-approvals-and-sandbox`
# so automation does not hang on TUI startup.

set -euo pipefail

AUTH_STATUS_TIMEOUT_SECONDS="${CODEX_AUTH_STATUS_TIMEOUT_SECONDS:-5}"
EXEC_TIMEOUT_SECONDS="${CODEX_EXEC_TIMEOUT_SECONDS:-1800}"
AUTH_STATUS_MESSAGE=""

usage() {
    cat <<'EOF'
Usage: codex-yolo.sh [codex-args...]

Examples:
  bash scripts/codex-yolo.sh "summarize this repository"
  printf 'extra context' | bash scripts/codex-yolo.sh "analyze stdin"

Behavior:
  - Interactive TTY: runs `codex --dangerously-bypass-approvals-and-sandbox ...`
  - Non-TTY: runs `codex exec --dangerously-bypass-approvals-and-sandbox ...`
  - Requires authentication (Codex login or API key env)
EOF
}

log_warn() {
    echo "Warning: $1" >&2
}

run_with_timeout() {
    local timeout_seconds="$1"
    shift

    if ! command -v timeout >/dev/null 2>&1; then
        "$@"
        return $?
    fi

    set +e
    timeout "${timeout_seconds}" "$@"
    local status=$?
    set -e

    if [[ "$status" -eq 124 ]]; then
        echo "Error: codex command timed out after ${timeout_seconds}s. Adjust CODEX_EXEC_TIMEOUT_SECONDS if needed." >&2
    fi

    return "$status"
}

is_authenticated() {
    if [[ -n "${OPENAI_API_KEY:-}" || -n "${CODEX_API_KEY:-}" ]]; then
        AUTH_STATUS_MESSAGE="Using API key environment variable."
        return 0
    fi

    local output
    local status

    set +e
    output="$(timeout "${AUTH_STATUS_TIMEOUT_SECONDS}" codex login status 2>&1)"
    status=$?
    set -e

    if [[ "$status" -eq 0 ]]; then
        AUTH_STATUS_MESSAGE="${output:-Logged in.}"
        return 0
    fi

    if [[ "$status" -eq 124 ]]; then
        AUTH_STATUS_MESSAGE="Timed out while checking login status."
    else
        AUTH_STATUS_MESSAGE="${output:-Not logged in.}"
    fi

    return 1
}

is_interactive_terminal() {
    [[ -t 0 && -t 1 && -t 2 ]]
}

case "${1:-}" in
    --help|-h)
        usage
        exit 0
        ;;
esac

if ! command -v codex >/dev/null 2>&1; then
    echo "Error: codex is not available in PATH. Run: bash .devcontainer/install-agent-clis.sh" >&2
    exit 1
fi

if ! is_authenticated; then
    log_warn "${AUTH_STATUS_MESSAGE}"
    echo "Codex authentication is required before using yolo mode." >&2
    echo "Run: npm run codex:login" >&2
    exit 1
fi

if is_interactive_terminal; then
    set +e
    run_with_timeout "${EXEC_TIMEOUT_SECONDS}" codex --dangerously-bypass-approvals-and-sandbox "$@"
    status=$?
    set -e

    if [[ "$status" -eq 124 ]]; then
        exit 1
    fi

    exit "$status"
fi

if [[ "$#" -eq 0 ]]; then
    echo "Error: non-interactive usage requires a prompt or arguments." >&2
    echo "Example: npm run codex:yolo -- ""summarize the repo""" >&2
    exit 1
fi

set +e
run_with_timeout "${EXEC_TIMEOUT_SECONDS}" codex exec --dangerously-bypass-approvals-and-sandbox "$@"
status=$?
set -e

if [[ "$status" -eq 124 ]]; then
    echo "Error: codex exec timed out after ${EXEC_TIMEOUT_SECONDS}s. Adjust CODEX_EXEC_TIMEOUT_SECONDS if needed." >&2
    exit 1
fi

exit "$status"
