#!/usr/bin/env bash

parse_nunit_results() {
    local unity_scripts
    unity_scripts="$(CDPATH='' cd -- "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)" || return 1
    uv run --quiet --project "${unity_scripts}/../python~" --locked --no-dev \
        python "${unity_scripts}/nunit_results.py" "$1" "$2"
}
