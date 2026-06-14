# Skill: Unity MCP Configuration

<!-- trigger: mcp, unity mcp, mcp.json, bridge, supergateway, endpoint, agent tools | Configure/verify the per-developer Unity MCP client config; configs are gitignored and validated | Feature -->

**Trigger**: When configuring the Unity MCP server for an agent (Claude Code, Cursor, Codex, VS Code/Copilot), when the `Unity_*` tools are missing from an agent, or when editing anything under `scripts/mcp/` or the MCP client config files.

---

## What this is

Unity runs on a Windows host; agents run in a Linux devcontainer. The Windows
relay speaks stdio, which cannot cross into the container, so `supergateway`
bridges it to streamable HTTP and the container's agents point at that HTTP
endpoint. Full setup: [MCP local setup guide](../../docs/guides/mcp-local-setup.md);
script details: [MCP helper README](../../scripts/mcp/README.md).

```text
Unity (Windows, stdio) → supergateway bridge → http://<host>:<port>/mcp → agent clients (Linux container)
```

## The config files are machine-local (never commit)

The bridge `host:port` is per-developer, so all four generated client configs are
**gitignored** and regenerated locally:

| Client            | File                 | Ignored by     |
| ----------------- | -------------------- | -------------- |
| Claude Code       | `.mcp.json`          | explicit entry |
| Cursor            | `.cursor/mcp.json`   | explicit entry |
| VS Code / Copilot | `.vscode/mcp.json`   | `.vscode/**`   |
| Codex             | `.codex/config.toml` | `.codex/*`     |

Generate/refresh them all from one endpoint:

```bash
UNITY_MCP_BRIDGE_HOST=YOUR_WINDOWS_HOST_IP UNITY_MCP_BRIDGE_PORT=9003 \
  bash scripts/mcp/configure-unity-mcp-endpoint.sh
```

## "My agent has no `Unity_*` tools"

Most often the server is reachable but the agent session is stale. Order of checks:

1. **Reachability** — `bash scripts/mcp/probe-unity-mcp-endpoint.sh <host> 9003`.
   A healthy bridge answers `POST /mcp → 200` with an MCP `initialize` result.
2. **Config present + valid** — `pwsh scripts/validate-mcp-config.ps1`.
3. **Bind to the agent** — generating the config does NOT attach the server to an
   already-running agent. Reload it: restart the editor/CLI, or in Claude Code
   re-approve the project MCP server. Only then do the `Unity_*` tools appear.

## Enforced forever

`scripts/validate-mcp-config.ps1` (CI: `.github/workflows/validate-mcp-config.yml`)
fails the build on:

- `UNH-MCP-TRACKED` — a machine-local config path is not gitignored.
- `UNH-MCP-INVALID` — a present config is malformed or its `unity-mcp-remote` URL
  does not end with `/mcp`.
- `UNH-MCP-MISSINGREF` — an MCP doc references a `scripts/mcp/*.sh|*.ps1` that does
  not exist (the dangling-helper-script class of bug).

When you add a new MCP client config path or helper script, update
`scripts/mcp/configure-unity-mcp-endpoint.sh`, `.gitignore`, and the
`$localConfigs` list in `scripts/validate-mcp-config.ps1` together.

## Related Skills

- [Unity devcontainer testing](./unity-devcontainer-testing.md) — running Unity from the devcontainer.
