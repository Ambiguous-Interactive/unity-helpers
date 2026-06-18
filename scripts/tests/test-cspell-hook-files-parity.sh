#!/usr/bin/env bash
# =============================================================================
# Test: cspell.json `files` glob vs agent-preflight spell-check extension parity
# =============================================================================
# Ensures that every file extension spell-checked by scripts/agent-preflight.ps1
# ($spellingTargets) is ALSO covered by cspell.json's top-level `files` glob.
# pre-push intentionally does not run cspell; spelling belongs in agent
# preflight, validate:prepush, and CI.
#
# Run: bash scripts/tests/test-cspell-hook-files-parity.sh
# Exit codes: 0 = parity, 1 = drift detected
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
AGENT_PREFLIGHT="$REPO_ROOT/scripts/agent-preflight.ps1"
CSPELL_JSON="$REPO_ROOT/cspell.json"

if [ ! -f "$AGENT_PREFLIGHT" ]; then
    echo "FAIL: $AGENT_PREFLIGHT not found" >&2
    exit 1
fi
if [ ! -f "$CSPELL_JSON" ]; then
    echo "FAIL: $CSPELL_JSON not found" >&2
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

extract_cspell_files_exts() {
    node -e '
        const fs = require("fs");
        const cfg = JSON.parse(fs.readFileSync(process.argv[1], "utf8"));
        const files = Array.isArray(cfg.files) ? cfg.files : [];
        const out = new Set();
        for (const pat of files) {
            const expand = (g) => {
                const m = g.match(/\{([^{}]+)\}/);
                if (!m) return [g];
                return m[1].split(",").map((p) => g.slice(0, m.index) + p + g.slice(m.index + m[0].length));
            };
            for (const expanded of expand(pat)) {
                const base = expanded.replace(/.*\//, "");
                const dot = base.lastIndexOf(".");
                if (dot < 0) continue;
                const ext = base.slice(dot + 1).toLowerCase();
                if (ext && !ext.includes("*")) out.add(ext);
            }
        }
        for (const ext of [...out].sort()) console.log(ext);
    ' "$CSPELL_JSON"
}

AGENT_PREFLIGHT_EXTS=$(extract_agent_preflight_exts)
CSPELL_FILES_EXTS=$(extract_cspell_files_exts)

echo "agent-preflight spell-check extensions:"
# shellcheck disable=SC2086
printf '  %s\n' $AGENT_PREFLIGHT_EXTS
echo "cspell.json files-glob extensions:"
# shellcheck disable=SC2086
printf '  %s\n' $CSPELL_FILES_EXTS

MISSING=()
while IFS= read -r ext; do
    [ -z "$ext" ] && continue
    if ! printf '%s\n' "$CSPELL_FILES_EXTS" | grep -qx "$ext"; then
        MISSING+=("$ext")
    fi
done <<< "$AGENT_PREFLIGHT_EXTS"

if [ ${#MISSING[@]} -gt 0 ]; then
    echo "" >&2
    echo "FAIL: cspell.json 'files' glob does NOT cover every extension agent-preflight checks." >&2
    echo "Missing from cspell.json:" >&2
    for ext in "${MISSING[@]}"; do
        echo "  .$ext" >&2
    done
    echo "" >&2
    echo "Resolution:" >&2
    echo "  Broaden cspell.json 'files' so each agent-preflight extension is covered." >&2
    echo "" >&2
    exit 1
fi

REQUIRED=("md" "markdown" "json" "jsonc" "asmdef" "asmref" "yml" "yaml" "js" "cs")
for ext in "${REQUIRED[@]}"; do
    if ! printf '%s\n' "$AGENT_PREFLIGHT_EXTS" | grep -qx "$ext"; then
        echo "FAIL: required extension '$ext' missing from agent-preflight spell-check set" >&2
        exit 1
    fi
    if ! printf '%s\n' "$CSPELL_FILES_EXTS" | grep -qx "$ext"; then
        echo "FAIL: required extension '$ext' missing from cspell.json files glob" >&2
        exit 1
    fi
done

echo ""
echo "PASS: cspell.json files glob covers every agent-preflight spell-check extension."
exit 0
