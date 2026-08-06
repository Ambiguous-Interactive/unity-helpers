# Unity MCP in a Linux devcontainer with a Windows host

Unity runs on Windows; agents run inside a Linux devcontainer. Unity's relay binary speaks stdio and
cannot run in the container, so `unity-mcp.mjs` bridges it to authenticated HTTP on the host and
writes the container's client configs to match.

```text
Unity (Windows) → relay (stdio) → unity-mcp bridge (HTTP + bearer) → agent clients (container)
```

One script, three subcommands:

| Command                       | Runs on   | Does                                                           |
| ----------------------------- | --------- | -------------------------------------------------------------- |
| `npm run unity:mcp:bridge`    | host      | Serves Unity's relay over authenticated streamable HTTP        |
| `npm run unity:mcp:configure` | container | Discovers the endpoint and writes every MCP client config      |
| `npm run unity:mcp:probe`     | container | Handshakes with the endpoint and reports which project answers |

`node scripts/mcp/unity-mcp.mjs --help` lists every flag.

## Which Unity, not just which port

A bridge is bound to one editor; a client config names a host and port. Whichever editor claimed the
port answers, and a successful connection says nothing about which project is open on the other end.
That is [#333](https://github.com/Ambiguous-Interactive/unity-helpers/issues/333): agents
compile-gated a completely different project while every check reported success, and the obvious
sanity check did not help, because a consumer project contains
`Packages/com.wallstop-studios.unity-helpers` too.

Three layers keep it pinned, and none of them is sufficient alone:

1. **The relay is told the project.** `bridge` passes `--project-path`, so the relay opens a named
   project instead of whichever editor it discovers first. `--project` overrides deliberately.

   **This repository is a package, not a Unity project**, which is where the sibling studio repos
   differ: they _are_ projects, so they can pass their own repo root. Here the repo lives at
   `<project>/Packages/com.wallstop-studios.unity-helpers` and has no `Assets`/`ProjectSettings` of
   its own, so `bridge` walks **up** from the script's location to the first directory containing
   **both** markers. Both are required because the repo root carries a stray, untracked `Assets/`;
   matching on that alone would stop at the package and hand the relay a non-project.

2. **Each project owns a port.** This repository uses **9007**; DxMessaging 9003, IshoBoy 9004,
   DoxReloaded 9010, qora-redux 9020. Discovery probes 9007 only — probing a neighbor's port is
   exactly how a config ends up aimed at another project's editor.
3. **The endpoint is asked who it is.** After the MCP handshake, `probe` and `configure` call
   `Unity_ManageEditor GetProjectRoot`. `probe` always prints the answer; `configure` records it in
   `.env.local` as `UNITY_MCP_PROJECT_ROOT` on first success, and every later run verifies against
   it. A mismatch is a hard failure that names both projects and writes nothing.

Comparison is normalized for separator direction, trailing separators, and case, because the
container sees `/workspaces/...` while the editor reports `D:\Code\...`.

Use `--any-project` to skip layer 3 when connecting to something else is the point.

## Failure statuses

`probe` classifies every attempt rather than reporting a single yes/no:

| Status             | Meaning                                                                |
| ------------------ | ---------------------------------------------------------------------- |
| `unreachable`      | Nothing accepted a TCP connection                                      |
| `unauthorized`     | A bridge is running but rejected the bearer token                      |
| `http-error`       | Something answered that is not a streamable-HTTP MCP endpoint          |
| `malformed`        | Answered, but produced no valid JSON-RPC `initialize` result           |
| `no-editor`        | Bridge is up and authenticating, but no Unity editor is attached       |
| `unidentified`     | Handshook, but would not say which project it has open                 |
| `project-mismatch` | Healthy bridge, wrong editor — it names the project it actually serves |

`configure` refuses to write on `unauthorized`, `no-editor`, or `project-mismatch`. Both mean a real bridge is
there and only the pairing is wrong, so overwriting the config would bake in the wrong answer.

## Machine-local files

These carry a per-developer endpoint and a bearer token and are all gitignored:

- `.mcp.json` (Claude Code)
- `.cursor/mcp.json`
- `.vscode/mcp.json`
- `.codex/config.toml`
- `.env.local` (the source of the values above)

`npm run validate:mcp-config` fails if any of them stops being ignored, if a present config is
malformed or does not target `/mcp`, or if these docs reference a script that does not exist.

Writes are transactional: files are staged as `0600` temporaries and renamed, and a failure part-way
rolls every already-committed file back. Only the `unity-mcp-remote` entry is touched, so sibling MCP
servers in the same file survive. JSON configs are read as JSONC, since VS Code's own "MCP: Add
Server" scaffolding writes comments into `.vscode/mcp.json`; comments are not preserved on rewrite.

## Setup

Full walkthrough: [MCP local setup guide](../../docs/guides/mcp-local-setup.md). Install the relay
per the
[Unity MCP documentation](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.9/manual/integration/unity-mcp-get-started.html);
`bridge` looks for it under `~/.unity/relay` and `--relay <path>` overrides.

## "My agent has no `Unity_*` tools"

1. **Is a bridge there, and is it the right one?** `npm run unity:mcp:probe`. It prints the project.
2. **Is a config present and valid?** `npm run validate:mcp-config`.
3. **Is the agent bound?** Generating a config does not attach the server to an already-running
   agent. Restart the editor/CLI, or in Claude Code re-approve the project MCP server. A reachable
   endpoint and a stale agent session look identical from the outside.

`probe` diagnoses this directly: an endpoint that handshakes but exposes no `Unity_*` tools reports
`no-editor` rather than "reachable". Unity registers those tools from inside the editor, so an empty
`tools/list` means the editor is closed, still importing, or has not had the connection approved in
its Unity MCP Server settings.
