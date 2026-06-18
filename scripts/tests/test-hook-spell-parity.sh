#!/usr/bin/env bash
# =============================================================================
# Test: pre-commit / agent-preflight spell-check extension parity
# =============================================================================
# Ensures that .githooks/pre-commit (SPELL_FILES_ARRAY bucket building loop)
# and scripts/agent-preflight.ps1 ($spellingTargets) lint the same file
# extensions. pre-push intentionally does NOT run cspell; it must stay a fast
# last-resort hook, while agent-preflight and validate:prepush catch spelling.
#
# Run: bash scripts/tests/test-hook-spell-parity.sh
# Exit codes: 0 = parity, 1 = drift detected
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PRE_COMMIT="$REPO_ROOT/.githooks/pre-commit"
PRE_PUSH="$REPO_ROOT/.githooks/pre-push"
AGENT_PREFLIGHT="$REPO_ROOT/scripts/agent-preflight.ps1"

if [ ! -f "$PRE_COMMIT" ]; then
    echo "FAIL: $PRE_COMMIT not found" >&2
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

extract_precommit_exts() {
    awk '
        /SPELL_FILES_ARRAY=\(\)/ { in_block = 1 }
        in_block {
            print
            if ($0 ~ /^[[:space:]]*done[[:space:]]*$/) { in_block = 0 }
        }
    ' "$PRE_COMMIT" \
        | grep -oE '\*\.[A-Za-z0-9]+' \
        | sed 's/^\*\.//' \
        | sort -u
}

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

PRE_COMMIT_EXTS=$(extract_precommit_exts)
AGENT_PREFLIGHT_EXTS=$(extract_agent_preflight_exts)

echo "pre-commit      spell-check extensions:"
# shellcheck disable=SC2086
printf '  %s\n' $PRE_COMMIT_EXTS
echo "agent-preflight spell-check extensions:"
# shellcheck disable=SC2086
printf '  %s\n' $AGENT_PREFLIGHT_EXTS

DIFF=$(diff <(printf '%s\n' "$PRE_COMMIT_EXTS") <(printf '%s\n' "$AGENT_PREFLIGHT_EXTS") || true)

if [ -n "$DIFF" ]; then
    echo ""
    echo "FAIL: pre-commit and agent-preflight spell-check extension lists differ." >&2
    echo "  (< pre-commit only, > agent-preflight only)" >&2
    echo "$DIFF" >&2
    echo "" >&2
    echo "Resolution:" >&2
    echo "  Update SPELL_FILES_ARRAY in .githooks/pre-commit and/or" >&2
    echo "  \$spellingTargets in scripts/agent-preflight.ps1 so both match." >&2
    echo "" >&2
    exit 1
fi

if grep -Eq 'CHANGED_SPELL=|cspell[[:space:]]+lint' "$PRE_PUSH"; then
    echo "FAIL: pre-push must not run cspell; keep spelling in agent-preflight/validate:prepush." >&2
    exit 1
fi

REQUIRED=("md" "markdown" "json" "jsonc" "asmdef" "asmref" "yml" "yaml" "js" "cs")
for ext in "${REQUIRED[@]}"; do
    if ! printf '%s\n' "$PRE_COMMIT_EXTS" | grep -qx "$ext"; then
        echo "FAIL: required extension '$ext' missing from pre-commit spell-check set" >&2
        exit 1
    fi
    if ! printf '%s\n' "$AGENT_PREFLIGHT_EXTS" | grep -qx "$ext"; then
        echo "FAIL: required extension '$ext' missing from agent-preflight spell-check set" >&2
        exit 1
    fi
done

echo ""
echo "PASS: pre-commit and agent-preflight spell-check extension sets match; pre-push stays cspell-free."
exit 0
