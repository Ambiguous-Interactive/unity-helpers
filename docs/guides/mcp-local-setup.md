# MCP Local Setup

This page covers machine-local MCP configuration for a Linux devcontainer with a
Windows host relay.

## Why this is local-only

These files contain machine-specific host and port values and are gitignored:

- `.vscode/mcp.json`
- `.mcp.json`
- `.cursor/mcp.json`
- `.codex/config.toml`

Do not commit these files.

## 1. Define local endpoint values

Create `.env.local` in repo root:

```bash
export UNITY_MCP_BRIDGE_HOST=YOUR_WINDOWS_HOST_IP
export UNITY_MCP_BRIDGE_PORT=9003
export UNITY_MCP_BRIDGE_PATH=/mcp
```

Optional defaults used by scripts:

- `UNITY_MCP_DEFAULT_HOST`
- `UNITY_MCP_DEFAULT_PORT`
- `UNITY_MCP_DEFAULT_PATH`

## 2. Generate all local client configs

Run from the devcontainer:

```bash
bash scripts/mcp/configure-unity-mcp-endpoint.sh
```

This updates local MCP config files for VS Code, Claude Code, Cursor, and Codex.

## 3. Start bridge on Windows host

See the [official Unity Docs](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.9/manual/integration/unity-mcp-get-started.html) for more details.

```powershell
$env:UNITY_MCP_RELAY_COMMAND = '<relay command from Unity MCP docs>'
pwsh -File scripts/mcp/start-unity-mcp-bridge.ps1 -Port 9003
```

## 4. Probe from the devcontainer

```bash
bash scripts/mcp/probe-unity-mcp-endpoint.sh YOUR_WINDOWS_HOST_IP 9003
```

## Notes

- `9003` is the default fallback port in MCP helper scripts.
- If your host uses a different port, set it in `.env.local` or pass it as a
  script argument.
- See the [MCP helper README](../../scripts/mcp/README.md) for script-level details.

## Binding the server to your agent

Generating the config files does not retroactively connect an agent that was
already running. After step 2, **reload the agent** so it picks up the new
server — restart the editor/CLI, or in Claude Code re-approve the project MCP
server — then the `Unity_*` tools attach. The MCP server can be running and
reachable (step 4 returns HTTP 200) while a stale agent session still shows no
Unity tools; reloading is what binds them.
