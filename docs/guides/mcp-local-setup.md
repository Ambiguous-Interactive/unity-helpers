# MCP Local Setup

The devcontainer configures the repository's shared MCP servers for Claude Code, Cursor, VS Code
and GitHub Copilot, Codex, OpenCode, and nanocoder. A full rebuild or fresh clone installs the local
runtimes and regenerates every machine-local client config automatically; every later container
start repairs missing or stale configs.

The shared catalog is:

- GitHub's official MCP server, running from its Docker image.
- Z.AI Vision for screenshot, image, diagram, chart, and video understanding.
- Z.AI Web Search for current search results.
- Z.AI Web Reader for structured webpage extraction.
- Z.AI Zread for public GitHub repository documentation and source exploration.

Set `Z_AI_API_KEY` on the host before opening the devcontainer, or add it to the repository's
gitignored `.env.local` file:

```bash
Z_AI_API_KEY=<Z.AI API key>
```

The key is resolved only when a Z.AI server starts. It is never copied into an MCP client config or
placed on a process command line. Remote services receive it through a mode-0600 temporary header
file that is deleted when the server exits. If the key is absent, the launcher exits with a direct
setup instruction instead of starting an unauthenticated server.

Run `npm run mcp:configure-shared` to repair only the shared GitHub and Z.AI entries without a live
Unity bridge. The devcontainer runs this independent path before attempting Unity configuration,
so a wrong or unavailable Unity editor cannot suppress the other servers.

GitHub resolves `GITHUB_PERSONAL_ACCESS_TOKEN` from the process environment first and the
gitignored `.env.local` file second. Add an existing token there with no client-config changes:

```bash
GITHUB_PERSONAL_ACCESS_TOKEN=<GitHub personal access token>
```

The prompt-free Git and GitHub MCP credential cache remains the final fallback. Populate it with
`npm run github:token:bootstrap` or `npm run github:token:store`. The devcontainer keeps that
mode-0600 cache in a named volume so it survives full container rebuilds. Agent guidance requires
GitHub MCP for remote GitHub operations whenever the server exposes the needed capability; plain
`git` remains the transport for fetch and push.

## Unity bridge

Unity runs on a Windows host; agents run in a Linux devcontainer. Unity's relay speaks stdio, which
cannot cross into the container, so a small Node bridge serves it over authenticated HTTP and the
container's agents point at that endpoint.

```text
Unity (Windows) → relay (stdio) → unity-mcp bridge (HTTP + bearer) → agent clients (container)
```

One command does each side.

## Host: start the bridge

```powershell
npm run unity:mcp:bridge
```

It finds Unity's relay under `~/.unity/relay`, generates a bearer token into `.env.local` if there
is not one already, and pins the relay to a named Unity project by passing `--project-path`. That
pin is what stops the relay attaching to whichever editor it discovers first when several are open.

This repository is a **package**, not a Unity project, so there is nothing to pin at the repo root.
`bridge` walks up from the script's location to the first directory holding both `Assets` and
`ProjectSettings` — normally `<project>/` two levels above
`<project>/Packages/com.wallstop-studios.unity-helpers`. If your layout differs, or the walk finds
nothing, pass `--project <unity project root>` (or set `UNITY_PROJECT_PATH`); it fails with that
instruction rather than guessing.

See the [Unity MCP documentation](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.9/manual/integration/unity-mcp-get-started.html)
for installing the relay.

## Container: configure the clients

```bash
npm run unity:mcp:configure
```

This discovers the endpoint and merges `unity-mcp-remote` into every machine-local client config —
`.mcp.json` (Claude Code **and** nanocoder), `.cursor/mcp.json`, `.vscode/mcp.json`,
`.codex/config.toml`, and `opencode.json` (OpenCode) — with its bearer token. Claude Code and
nanocoder share `.mcp.json` but select the HTTP transport with different keys (`type` vs
`transport`), so the generated entry carries both. Writes are transactional: a failure part-way
rolls every already-written file back.

Then check it:

```bash
npm run unity:mcp:probe
```

## The endpoint is a port; the editor behind it is not

This is the failure that [#333](https://github.com/Ambiguous-Interactive/unity-helpers/issues/333)
was filed for, and it is worth understanding rather than just avoiding.

A bridge is bound to **one** Unity editor, but a client config names a **host and port**. Whichever
editor claimed that port answers. Nothing about "the connection succeeded" tells you the editor on
the other end has _this_ project open — and because a consumer project contains
`Packages/com.wallstop-studios.unity-helpers` too, even checking that the package is present does
not distinguish them.

So `probe` and `configure` ask. After the MCP handshake they call
`Unity_ManageEditor GetProjectRoot`, which answers in one request with no code compilation, and:

- **`probe` always prints the project** it is talking to.
- **`configure` pins that project** into `.env.local` as `UNITY_MCP_PROJECT_ROOT` the first time it
  succeeds. Every later run verifies against it instead of trusting the port.
- **A mismatch is a hard failure** that names both projects, and nothing is written.

```text
A Unity MCP bridge answered at http://192.168.1.33:9003/mcp but has a different project open
(serves D:/Code/IshoBoy, expected D:/Code/Packages).
```

Pass `--any-project` to accept whatever answers, when that is genuinely what you want.

Two habits keep this from arising at all: every studio project owns a **distinct port** — this one
uses **9007**, DxMessaging 9003, IshoBoy 9004, DoxReloaded 9010, qora-redux 9020 — and every bridge
has its **own bearer token**, so a config aimed at a neighbor's port gets a `401` rather than a
quietly wrong editor.

## Optional configuration

Everything has a default. To override, create `.env.local` in the repo root (gitignored):

```bash
UNITY_MCP_BRIDGE_HOST=host.docker.internal
UNITY_MCP_BRIDGE_PORT=9007
UNITY_MCP_BRIDGE_PATH=/mcp
UNITY_MCP_BEARER_TOKEN=<64 hex characters>
UNITY_MCP_PROJECT_ROOT=D:/Code/YourUnityProject
UNITY_PROJECT_PATH=D:\Path\To\HostUnityProject
Z_AI_API_KEY=<Z.AI API key>
```

`UNITY_MCP_BEARER_TOKEN` and `UNITY_MCP_PROJECT_ROOT` are written for you on first success; the rest
are only needed when a default is wrong. Command-line flags beat the environment, which beats
`.env.local`, which beats the built-in defaults. Run `node scripts/mcp/unity-mcp.mjs --help` for the
full flag list.

## Never commit these

`.mcp.json`, `.cursor/mcp.json`, `.vscode/mcp.json`, `.codex/config.toml`, `opencode.json`, and
`.env.local` hold per-developer endpoints and credentials. All six are gitignored, and
`npm run validate:mcp-config` fails if that ever stops being true.

## Binding the server to your agent

Generating the config does not retroactively connect an agent that is already running. After
`configure`, **reload the agent** — restart the editor/CLI, or in Claude Code re-approve the project
MCP server — and the `Unity_*` tools attach. A reachable endpoint and a stale agent session look
identical from the outside; reloading is what binds them.
