#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec uv run --quiet --project "$SCRIPT_DIR/python~" --locked --no-dev python "$SCRIPT_DIR/hook_permissions.py" "$@"
