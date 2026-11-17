#!/usr/bin/env bash
# Compatibility entry point for devcontainer download validation.
set -euo pipefail
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
export UH_DEVCONTAINER_URL_PROGRAM="$0"
exec uv run --quiet --project "${SCRIPT_DIR}/python~" --locked --no-dev python "${SCRIPT_DIR}/validate_devcontainer_urls.py" "$@"
