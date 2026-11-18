#!/usr/bin/env bash
set -euo pipefail
script_directory="$(cd "$(dirname "$0")/.." && pwd)"
exec uv run --quiet --project "$script_directory/python~" --locked --no-dev python \
    "$script_directory/validate_formatting.py" newline "$@"
