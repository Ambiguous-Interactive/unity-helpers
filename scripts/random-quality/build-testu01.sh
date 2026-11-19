#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/../.." && pwd)"

exec uv run --quiet --project "${repo_root}/scripts/python~" --locked --no-dev python \
    "${script_dir}/build_testu01.py" "$@"
