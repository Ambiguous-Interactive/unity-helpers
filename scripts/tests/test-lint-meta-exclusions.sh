#!/usr/bin/env bash
# Compatibility entry point; executable metadata controls live in typed Python.
set -euo pipefail
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec uv run --project "${SCRIPT_DIR}/../python~" --locked pytest "${SCRIPT_DIR}/test_meta_lint.py" -q
