#!/usr/bin/env bash
# Compatibility entry point; local configuration policy runs through locked Python.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEFAULT_REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
exec uv run --quiet --project "$SCRIPT_DIR/python~" --locked --no-dev \
    python "$SCRIPT_DIR/configure_git_defaults.py" \
    "--repo-root=${1:-$DEFAULT_REPO_ROOT}" --profile=bash
