#!/usr/bin/env bash
# Launches the official GitHub MCP server for every AI client this repository configures.
#
# Why a launcher rather than the docker command inline: an MCP client config cannot run a shell
# substitution, so the alternatives are a token literal in five gitignored files, or an exported
# environment variable that every client's parent process happens to carry. The first duplicates a
# secret; the second silently yields an UNAUTHENTICATED server whenever the variable is missing,
# which looks like "GitHub is down" rather than "you are not logged in".
#
# The Node launcher resolves GITHUB_PERSONAL_ACCESS_TOKEN from the process environment first, the
# repository's gitignored .env.local second, and scripts/github-token.sh's prompt-free cache last.
#
# The token is passed to `docker run` through the environment, never on the command line, because a
# command line is visible in `ps` to every process on the machine.
set -euo pipefail

REPO_ROOT="$(CDPATH= cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
exec node "$REPO_ROOT/scripts/mcp/github-mcp.mjs" "$@"
