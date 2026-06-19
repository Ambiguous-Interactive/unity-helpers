#!/usr/bin/env bash
# =============================================================================
# Test: agent-preflight spell-check coverage and hook speed boundary
# =============================================================================
# Ensures spelling stays out of last-resort hooks and that
# scripts/agent-preflight.ps1 ($spellingTargets) still covers every file
# extension that hooks used to spell-check. pre-commit and pre-push must stay
# fast; agent-preflight, validate:prepush, and CI catch spelling.
#
# Run: bash scripts/tests/test-hook-spell-parity.sh
# Exit codes: 0 = parity, 1 = drift detected
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PRE_COMMIT="$REPO_ROOT/.githooks/pre-commit"
PRE_COMMIT_IMPL="$REPO_ROOT/.githooks/pre-commit.ps1"
PRE_PUSH="$REPO_ROOT/.githooks/pre-push"
AGENT_PREFLIGHT="$REPO_ROOT/scripts/agent-preflight.ps1"

if [ ! -f "$PRE_COMMIT" ]; then
    echo "FAIL: $PRE_COMMIT not found" >&2
    exit 1
fi
if [ ! -f "$PRE_COMMIT_IMPL" ]; then
    echo "FAIL: $PRE_COMMIT_IMPL not found" >&2
    exit 1
fi
if [ ! -f "$PRE_PUSH" ]; then
    echo "FAIL: $PRE_PUSH not found" >&2
    exit 1
fi
if [ ! -f "$AGENT_PREFLIGHT" ]; then
    echo "FAIL: $AGENT_PREFLIGHT not found" >&2
    exit 1
fi

extract_agent_preflight_exts() {
    awk '
        /\$spellingTargets = @\(/ { in_block = 1 }
        in_block {
            print
            if ($0 ~ /^[[:space:]]*\)[[:space:]]*$/) { exit }
        }
    ' "$AGENT_PREFLIGHT" \
        | grep -oE '\*\.[A-Za-z0-9]+' \
        | sed 's/^\*\.//' \
        | sort -u
}

AGENT_PREFLIGHT_EXTS=$(extract_agent_preflight_exts)

echo "agent-preflight spell-check extensions:"
# shellcheck disable=SC2086
printf '  %s\n' $AGENT_PREFLIGHT_EXTS

if grep -Eq 'CHANGED_SPELL=|SPELL_FILES_ARRAY|cspell[[:space:]]+(lint|--no-progress)' "$PRE_COMMIT" "$PRE_COMMIT_IMPL" "$PRE_PUSH"; then
    echo "FAIL: last-resort hooks must not run cspell; keep spelling in agent-preflight/validate:prepush." >&2
    exit 1
fi

REQUIRED=("md" "markdown" "json" "jsonc" "asmdef" "asmref" "yml" "yaml" "js" "cs")
for ext in "${REQUIRED[@]}"; do
    if ! printf '%s\n' "$AGENT_PREFLIGHT_EXTS" | grep -qx "$ext"; then
        echo "FAIL: required extension '$ext' missing from agent-preflight spell-check set" >&2
        exit 1
    fi
done

echo ""
echo "PASS: spelling is covered by agent-preflight and absent from last-resort hooks."
exit 0
