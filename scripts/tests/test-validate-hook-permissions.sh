#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec uv run --quiet --project "$SCRIPT_DIR/../python~" --locked pytest "$SCRIPT_DIR/test_hook_permissions.py" -q "$@"
