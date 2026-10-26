#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MCP_REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

if ! node "$MCP_REPO_ROOT/scripts/mcp/unity-mcp.mjs" configure-shared; then
    echo "WARN: Shared MCP configuration failed; see diagnostics above." >&2
fi
if ! node "$MCP_REPO_ROOT/scripts/mcp/unity-mcp.mjs" configure; then
    echo "WARN: Unity MCP configuration failed; run npm run unity:mcp:configure after repairing the bridge." >&2
fi

if ! python3 "$SCRIPT_DIR/agent-state.py" link-mcp; then
    echo "WARN: Copilot MCP link repair failed; personal configuration was kept." >&2
fi
