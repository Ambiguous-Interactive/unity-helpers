#!/usr/bin/env bash
set -euo pipefail

unity_scripts="$(CDPATH='' cd -- "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec uv run --quiet --project "${unity_scripts}/../python~" --locked --no-dev \
    python "${unity_scripts}/create_test_project.py" "$@"
