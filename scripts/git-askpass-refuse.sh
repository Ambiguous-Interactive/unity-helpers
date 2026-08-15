#!/usr/bin/env bash
# GIT_ASKPASS for this devcontainer: refuse, legibly, instead of opening a window.
#
# Why it exists (#450). Credentials here resolve from a 0600 cache through
# scripts/github-token.sh, installed as the only credential helper for github.com. When that cache
# is EMPTY git does not fail -- it falls back to GIT_ASKPASS, which the editor sets to its own
# askpass.sh, and that raises a dialog ON THE OWNER'S DESKTOP. A credential helper cannot suppress
# it: git's precedence is GIT_ASKPASS -> core.askPass -> SSH_ASKPASS, and the environment variable
# wins over anything git config can say. GIT_TERMINAL_PROMPT=0 does not cover askpass either.
#
# So the only lever is to BE the askpass. An unattended `git push` now ends in a one-line error
# naming the command that fixes it, rather than in a window nobody is watching.
#
# The editor's own Git UI is unaffected: the Git extension sets GIT_ASKPASS explicitly for the git
# processes it launches, because the value carries a per-session IPC handle it has to route back to.
# What this replaces is the copy inherited by terminals and by anything an agent runs there.
set -uo pipefail

# stderr ONLY. git reads this program's STDOUT as the answer, so a single stray character would be
# handed to the remote as a username or a password.
{
    printf 'git-askpass-refuse: refusing to prompt for "%s".\n' "${1:-a credential}"
    printf '\n'
    printf 'This container answers github.com from a 0600 credential cache, never from a dialog --\n'
    printf 'a dialog here opens on the owner'"'"'s desktop, and nothing is watching it. The cache is\n'
    printf 'empty, or this host is not github.com and has no cached credential.\n'
    printf '\n'
    printf 'From a terminal a human is watching:\n'
    printf '  npm run github:token:bootstrap   # one deliberate dialog, once per container\n'
    printf '  npm run github:token:store       # paste a token on stdin, no dialog at all\n'
} >&2

exit 1
