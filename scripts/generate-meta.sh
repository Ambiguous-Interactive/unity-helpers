#!/usr/bin/env bash
# Compatibility entry point; importer generation lives in typed Python.
set -euo pipefail
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec uv run --project "${SCRIPT_DIR}/python~" --locked python "${SCRIPT_DIR}/generate_meta.py" "$@"
