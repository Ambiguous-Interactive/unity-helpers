#!/usr/bin/env bash
# Compatibility entry point for the devcontainer URL contracts.
set -euo pipefail
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec uv run --quiet --project "${SCRIPT_DIR}/../python~" --locked pytest "${SCRIPT_DIR}/test_devcontainer_urls.py" -q "$@"
