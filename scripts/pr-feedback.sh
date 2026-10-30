#!/usr/bin/env bash
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec uv run --project "$REPO_ROOT/scripts/python~" --locked --no-dev python "$REPO_ROOT/scripts/pr_feedback.py" "$@"
