#!/usr/bin/env bash
# Launches the official GitHub MCP server for every AI client this repository configures.
#
# Why a launcher rather than the docker command inline: an MCP client config cannot run a shell
# substitution, so the alternatives are a token literal in five gitignored files, or an exported
# environment variable that every client's parent process happens to carry. The first duplicates a
# secret; the second silently yields an UNAUTHENTICATED server whenever the variable is missing,
# which looks like "GitHub is down" rather than "you are not logged in".
#
# This reads the credential the same way everything else in the repository does -- through
# scripts/github-token.sh, which never prompts -- so the token lives in exactly one 0600 file and
# refreshing it fixes every client at once.
#
# The token is passed to `docker run` through the environment, never on the command line, because a
# command line is visible in `ps` to every process on the machine.
set -euo pipefail

REPO_ROOT="$(CDPATH= cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
IMAGE="${GITHUB_MCP_IMAGE:-ghcr.io/github/github-mcp-server:latest}"

if ! command -v docker >/dev/null 2>&1; then
    printf 'github-mcp: docker is not installed, so the GitHub MCP server cannot start.\n' >&2
    exit 127
fi

# Exit 3 means "no credential cached", which is a request for a human rather than a reason to
# start an unauthenticated server that fails one tool call at a time.
if ! GITHUB_PERSONAL_ACCESS_TOKEN="$("$REPO_ROOT/scripts/github-token.sh")"; then
    printf 'github-mcp: no GitHub credential is cached; the server would answer nothing.\n' >&2
    printf 'github-mcp: store one with `npm run github:token:store`, then restart your client.\n' >&2
    exit 3
fi
export GITHUB_PERSONAL_ACCESS_TOKEN

exec docker run -i --rm -e GITHUB_PERSONAL_ACCESS_TOKEN "$IMAGE" "$@"
