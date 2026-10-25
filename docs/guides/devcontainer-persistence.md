# Persistent agent data in the devcontainer

Agent logins, conversations, personal configuration, and MCP OAuth tokens live in Docker named
volumes. Tool executables remain in the container image and are refreshed independently. Normal
stopped containers are reused when reopening the workspace; host initialization only removes dead
containers or running containers with confirmed namespace failures.

## First migration

Before rebuilding an older container, finish active agent work and stop other agent sessions.
Run from the repository root inside the existing container:

```bash
npm run agents:state:backup
```

This creates `progress/devcontainer-state/migration.tar.gz` and its SHA-256 checksum file. Both
files are private, gitignored workspace files. The helper refuses to overwrite an existing backup.
It copies application state, takes consistent SQLite snapshots, and verifies file checksums.
Transcripts can still change while an agent is running, so a quiet session gives the best migration
point. The archive includes credentials; keep it private and copy it to secure host storage before
replacing the workspace itself.

Then select **Dev Containers: Rebuild Container** in VS Code. Lifecycle setup restores the archive
before installing or configuring tools. It checks archive and file checksums and refuses to restore
into populated destination directories. A marker in the Codex volume prevents the same migration
from overwriting later changes on subsequent starts. The archive remains available after migration.

If automatic validation finds a missing checksum, damaged archive, invalid manifest, or populated
destination, setup reports a warning and continues without restoring files or marking the migration
as applied. The archive and existing state are retained. Resolve the warning before expecting old
sessions to appear. Explicit restore commands still fail on invalid input; errors while writing a
validated restore also stop setup.

After rebuilding, verify `codex login status` and resume a previous conversation. Also check the
other agents you use. Host-injected API keys still need to exist on the host or in your normal secret
source. Expired or revoked credentials may require signing in again.

If restoration reports a conflict, preserve the current destination in a separate backup first.
Restore into empty volumes, or use the helper's `--home` option to inspect a restore in a private
scratch directory. The helper does not merge conflicting state or overwrite it.

## What persists

Each directory below has a separate `unity-helpers-${devcontainerId}-...` named volume. The Dev
Containers identifier isolates workspaces and stays stable across rebuilds with the same workspace
and configuration location. Moving a checkout, changing Docker contexts, or creating a new
Codespace may select different volumes; use backup and restore to migrate.

| Tool                               | Directories under `/home/vscode`                                     |
| ---------------------------------- | -------------------------------------------------------------------- |
| Codex                              | `.codex`                                                             |
| Claude Code and provider launchers | `.claude`, `.claude-zai`, `.claude-openrouter`                       |
| OpenCode                           | `.config/opencode`, `.local/share/opencode`, `.local/state/opencode` |
| nanocoder                          | `.config/nanocoder`, `.local/share/nanocoder`                        |
| Copilot CLI                        | `.copilot`                                                           |
| GitHub CLI                         | `.config/gh`                                                         |
| Remote MCP OAuth                   | `.mcp-auth`                                                          |

The existing GitHub token cache and package caches retain their previous volumes. The whole home
directory and VS Code server profile are not persisted by this configuration. Application retention
settings still control how long histories remain available.

`CODEX_HOME`, `CLAUDE_CONFIG_DIR`, and `COPILOT_HOME` select the persistent directories for container
processes as well as VS Code terminals. Migration moves legacy `~/.claude.json` into the explicit
Claude configuration directory and moves workspace-local Copilot state into its persistent home.
Copilot's MCP configuration links to the generated workspace file when no conflicting personal
configuration exists. A conflicting personal MCP configuration is kept and reported.

Fresh Codex profiles use file-based credential storage. Existing user configuration is preserved;
keyring and ephemeral modes are reported because a directory volume cannot preserve them. With
`auto` storage, verify that credentials actually use the file fallback in this headless container.
See [OpenAI authentication](https://developers.openai.com/codex/auth/) and
[Codex state locations](https://developers.openai.com/codex/config-advanced/).

## Tool updates

On container creation and every start, the existing installer checks npm's stable `latest` versions
for the installed coding agents and MCP runtimes. It skips matching versions and retains installed
tools when the registry is unavailable. Reconnecting to a running container does not trigger another
update. Refresh manually at any time:

```bash
npm run agents:update
```

This covers the packages managed by the installer. OS packages, GitHub CLI, editor extensions, and
agent-managed plugins follow their own update mechanisms. Rebuilding applies image changes without
hiding newer image executables beneath a persistent home or npm installation directory.

## Back up and restore later

Choose a new archive name for each backup:

```bash
npm run agents:state:backup -- progress/devcontainer-state/backup-2026-10-20.tar.gz
```

Stop agent sessions before restoring. For an explicit restore into empty state directories:

```bash
npm run agents:state:restore -- progress/devcontainer-state/backup-2026-10-20.tar.gz
```

Preserve both the archive and its adjacent `.sha256` file. Image-provided symlinks are recorded
separately and recreated after regular files; their targets must still exist in the rebuilt image
or workspace. Socket files are omitted. Custom state paths outside the documented directories need
a separate backup and mount.

Docker volumes survive container removal on the same Docker host. They do not survive Docker
factory resets, removal of those volumes, or loss of the host. Avoid pruning these volumes unless
you intend to delete their data. See [Docker volume backup and lifecycle guidance](https://docs.docker.com/engine/storage/volumes/).

## Validation

```bash
npm run test:devcontainer-persistence
npm run validate:devcontainer
```

To include real Docker replacement and ownership checks, select a local image built from this
devcontainer (the test creates and removes its own derived image, containers, and volumes):

```bash
DEVCONTAINER_PERSISTENCE_IMAGE=your-local-devcontainer-image npm run test:devcontainer-persistence
```

The regression suite uses synthetic credentials and isolated temporary directories. It covers
container recovery, volume declarations, archive verification, migration conflicts, SQLite
integrity, and repeated setup. A real host reopen and rebuild remains the final acceptance check;
a nested Docker daemon cannot establish persistence on the host daemon.
