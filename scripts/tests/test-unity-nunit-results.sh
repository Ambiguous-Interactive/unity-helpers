#!/usr/bin/env bash
set -euo pipefail

script_directory="$(CDPATH='' cd -- "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec uv run --quiet --project "${script_directory}/../python~" --locked \
    pytest "${script_directory}/test_nunit_results.py" "$@"
