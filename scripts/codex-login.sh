#!/usr/bin/env bash
# Wrapper for Codex browser login.
#
# This script intentionally keeps login browser-first and does not switch to
# device auth automatically.

set -euo pipefail

usage() {
    cat <<'EOF'
Usage: codex-login.sh [--browser] [codex-login-args...]

Examples:
  bash scripts/codex-login.sh
  bash scripts/codex-login.sh --browser
  bash scripts/codex-login.sh status

Behavior:
  - Forwards args to `codex login ...`
  - `--browser` is accepted for backward compatibility and ignored
EOF
}

case "${1:-}" in
    --browser)
        shift
        ;;
    --help|-h)
        usage
        exit 0
        ;;
esac

if ! command -v codex >/dev/null 2>&1; then
  echo "Error: codex is not available in PATH. Run: bash .devcontainer/install-agent-clis.sh" >&2
  exit 1
fi

exec codex login "$@"
