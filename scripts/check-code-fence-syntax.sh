#!/usr/bin/env bash
# Compatibility entry point; code-fence validation lives in typed Python.
set -euo pipefail
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec uv run --quiet --project "${SCRIPT_DIR}/python~" --locked --no-dev python "${SCRIPT_DIR}/check_code_fence_syntax.py" "$@"
