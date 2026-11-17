#!/usr/bin/env bash
# Compatibility entry point; GitHub Pages CSS validation lives in typed Python.
set -euo pipefail
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec uv run --quiet --project "${SCRIPT_DIR}/python~" --locked --no-dev python "${SCRIPT_DIR}/validate_github_pages_css.py" "$@"
