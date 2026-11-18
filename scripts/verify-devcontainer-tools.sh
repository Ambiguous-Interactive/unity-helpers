#!/usr/bin/env bash
set -euo pipefail
script_directory="$(cd "$(dirname "$0")" && pwd)"
export UH_VERIFY_NATIVE_PATH="$PATH"
exec uv run --quiet --project "$script_directory/python~" --locked --no-dev python \
    "$script_directory/verify_devcontainer_tools.py" "$@"
