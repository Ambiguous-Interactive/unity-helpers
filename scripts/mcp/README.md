# Unity MCP in a Linux devcontainer with a Windows host

When Unity runs on Windows and your agents run inside a Linux devcontainer, the
Windows relay binary cannot run inside the container. The working pattern is:

1. Start the Unity relay on Windows (stdio).
2. Bridge stdio to HTTP on Windows.
3. Point container MCP clients at that HTTP endpoint.

This repository uses `supergateway` for step 2.

The generated MCP client config files are machine-local and gitignored:

- `.vscode/mcp.json`
- `.mcp.json`
- `.cursor/mcp.json`
- `.codex/config.toml`

Set your local endpoint in `.env.local` at repo root:

```bash
export UNITY_MCP_BRIDGE_HOST=YOUR_WINDOWS_HOST_IP
export UNITY_MCP_BRIDGE_PORT=9003
export UNITY_MCP_BRIDGE_PATH=/mcp
```

## Start the bridge on Windows

From the repository root on the Windows host:

```powershell
$env:UNITY_MCP_RELAY_COMMAND = '<relay command from Unity MCP docs>'
pwsh -File scripts/mcp/start-unity-mcp-bridge.ps1 -Port 9003
```

The relay command is installation-specific. Use the exact relay invocation shown
in your Unity MCP integration docs for your machine.

Optional flags:

- `-McpPath /mcp` (default `/mcp`)
- `-Stateful` if your MCP client requires stateful streamable HTTP sessions
- `-LogLevel info|debug|none`

If needed, add a Windows firewall rule for the selected port.

## Validate from the Linux devcontainer

```bash
bash scripts/mcp/probe-unity-mcp-endpoint.sh YOUR_WINDOWS_HOST_IP 9003
```

Use your actual Windows host LAN IP and bridge port.

## Sync all workspace client configs

Run this inside the Linux devcontainer whenever host/port/path changes:

```bash
UNITY_MCP_BRIDGE_HOST=YOUR_WINDOWS_HOST_IP UNITY_MCP_BRIDGE_PORT=9003 \
  bash scripts/mcp/configure-unity-mcp-endpoint.sh
```

This updates all local client config files to the same endpoint:

- `.vscode/mcp.json`
- `.mcp.json`
- `.cursor/mcp.json`
- `.codex/config.toml`

## Client configs in this repository

- VS Code/Copilot: `.vscode/mcp.json`
- Claude Code: `.mcp.json`
- Cursor: `.cursor/mcp.json`
- Codex: `.codex/config.toml`

All are configured to target a host bridge endpoint similar to
`http://<host>:<port>/mcp`.

## Binding the server to your agent

`configure-unity-mcp-endpoint.sh` only writes the config files. An agent that was
already running when the file appeared will not see the server until it reloads —
restart the agent (or, in Claude Code, re-approve the project MCP server) so the
`Unity_*` tools attach. Confirm reachability first with
`probe-unity-mcp-endpoint.sh`.
