# Skill: Unity MCP Configuration

<!-- trigger: mcp, unity mcp, mcp.json, bridge, endpoint, agent tools, wrong project | Configure/verify the per-developer Unity MCP client config; configs are gitignored and validated | Feature -->

**Trigger**: When configuring the Unity MCP server for an agent (Claude Code, Cursor, Codex, VS Code/Copilot), when the `Unity_*` tools are missing from an agent, or when editing anything under `scripts/mcp/` or the MCP client config files.

---

## What this is

Unity runs on a Windows host; agents run in a Linux devcontainer. The Windows
relay speaks stdio, which cannot cross into the container, so `scripts/mcp/unity-mcp.mjs`
bridges it to authenticated streamable HTTP and the container's agents point at that
endpoint. Full setup: [MCP local setup guide](../../docs/guides/mcp-local-setup.md);
script details: [MCP helper README](../../scripts/mcp/README.md).

```text
Unity (Windows, stdio) → unity-mcp bridge → http://<host>:9007/mcp → agent clients (Linux container)
```

Three npm commands, no PowerShell: `unity:mcp:bridge` on the host,
`unity:mcp:configure` and `unity:mcp:probe` in the container.

## The config files are machine-local (never commit)

The bridge `host:port` is per-developer, so all four generated client configs are
**gitignored** and regenerated locally:

| Client            | File                 | Ignored by     |
| ----------------- | -------------------- | -------------- |
| Claude Code       | `.mcp.json`          | explicit entry |
| Cursor            | `.cursor/mcp.json`   | explicit entry |
| VS Code / Copilot | `.vscode/mcp.json`   | `.vscode/**`   |
| Codex             | `.codex/config.toml` | `.codex/*`     |

`.env.local` holds the values they are generated from and is ignored too.

Generate/refresh them all:

```bash
npm run unity:mcp:configure
```

## ALWAYS confirm which project answered

An endpoint is a host and port; a bridge is bound to one editor. A successful
connection proves nothing about which project is open, and checking that
`Packages/com.wallstop-studios.unity-helpers` exists proves nothing either — a
consumer project has it too. That is issue #333, and it cost a full session of
compile-gating the wrong project.

```bash
npm run unity:mcp:probe   # prints "Unity project: <root>"
```

`configure` pins that root into `.env.local` as `UNITY_MCP_PROJECT_ROOT` on first
success and every later run verifies against it, failing loudly on a mismatch.
When calling the MCP tools directly, `Unity_ManageEditor GetProjectRoot` is the
same one-request check — make it the first call of any session that is about to
trust the editor.

## "My agent has no `Unity_*` tools"

Most often the server is reachable but the agent session is stale. Order of checks:

1. **Reachability and identity** — `npm run unity:mcp:probe`. A healthy bridge
   completes an MCP `initialize` and names the project it serves.
2. **Config present + valid** — `npm run validate:mcp-config`.
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

- `UNH-MCP-PORT` — the bridge's default port drifted off this repository's own
  (9007) or adopted a sibling project's, which is how a config lands on another
  project's editor.

When you add a new MCP client config path, update `clientConfigPaths` in
`scripts/mcp/unity-mcp.mjs`, `.gitignore`, and the `$localConfigs` list in
`scripts/validate-mcp-config.ps1` together.

## Related Skills

- [Unity devcontainer testing](./unity-devcontainer-testing.md) — running Unity from the devcontainer.
