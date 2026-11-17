#!/usr/bin/env bash
# Compatibility entry point; GitHub Pages CSS controls live in typed Python.
set -euo pipefail
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec uv run --quiet --project "${SCRIPT_DIR}/../python~" --locked pytest "${SCRIPT_DIR}/test_github_pages_css.py" -q "$@"
