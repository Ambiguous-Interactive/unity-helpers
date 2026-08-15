#!/usr/bin/env bash
# Removes the two things Dev Containers leaves behind in ~/.gitconfig that break this container.
#
# Both come from one behaviour: Dev Containers copies the HOST's ~/.gitconfig into the container at
# attach time. It writes its credential helper into whichever git config it is managing, and
# post-create.sh has already created ~/.gitconfig (`git config --global --add safe.directory`), so
# the helper lands there as well as in the /etc/gitconfig copy written at container start.
#
#   1. A DUPLICATE credential.helper. Git runs EVERY configured helper on EVERY credential request,
#      so two identical registrations mean two host dialogs per operation and two `password=`
#      records in one response. A caller that reads the response with `sed -n 's/^password=//p'`
#      gets two concatenated tokens and builds a malformed Authorization header from them -- which
#      still authenticates GETs and silently breaks request bodies on POST.
#
#   2. The host's WINDOWS safe.directory entries (`D:\`, `E:/Ambiguous Legal`). Git rejects each one
#      as "not absolute" and prints a warning per entry on EVERY git invocation, so every command in
#      the container carries six lines of noise that hide real output.
#
# Idempotent, and safe to run when neither problem is present. It never removes the LAST credential
# helper: /etc/gitconfig's copy is the one Dev Containers manages, and dropping both would break
# authentication entirely.
set -euo pipefail

log() { printf '[normalize-git-config] %s\n' "$1"; }

# A helper is only redundant in --global if the same value is already in --system.
system_helpers="$(git config --system --get-all credential.helper 2>/dev/null || true)"
global_helpers="$(git config --global --get-all credential.helper 2>/dev/null || true)"

if [ -n "$system_helpers" ] && [ -n "$global_helpers" ]; then
    if [ "$system_helpers" = "$global_helpers" ]; then
        git config --global --unset-all credential.helper 2>/dev/null || true
        log "Removed duplicate credential.helper from ~/.gitconfig (kept the /etc/gitconfig copy)."
    else
        # Different values are a deliberate override, not the Dev Containers duplicate.
        log "Left credential.helper alone: --global differs from --system."
    fi
fi

# Git's own rule: an entry that is not an absolute POSIX path can never match a repository here.
# Reading the values back through `git config` rather than editing the file keeps quoting correct.
removed_directories=0
while IFS= read -r directory; do
    [ -n "$directory" ] || continue
    case "$directory" in
        /*) continue ;;
        *) ;;
    esac
    # --fixed-value matches the literal string, so a path containing regex metacharacters
    # (`D:\`, `E:/Ambiguous Legal`) is removed by value rather than by pattern. It must precede the
    # name; passed after the value-pattern git treats it as a second pattern and silently matches
    # nothing, which is a removal that reports success and deletes no entry.
    if git config --global --fixed-value --unset-all safe.directory "$directory" 2>/dev/null; then
        removed_directories=$((removed_directories + 1))
    fi
done <<EOF
$(git config --global --get-all safe.directory 2>/dev/null || true)
EOF

if [ "$removed_directories" -gt 0 ]; then
    log "Removed $removed_directories non-absolute safe.directory entr(ies) copied from the host."
fi

# Point github.com at the cached-token helper, and at nothing else.
#
# Dev Containers' helper answers by raising a dialog on the OWNER'S DESKTOP, and `git push` /
# `git fetch` invoke it on every operation -- so a container that authenticates through it
# interrupts a human for work nobody is watching. `credential.helper` is multi-valued and git runs
# EVERY value, so adding this script is not enough: an EMPTY value RESETS the accumulated list, and
# writing the reset before the script is what discards the inherited /etc/gitconfig helper for this
# host. Other hosts keep it untouched, because this is not a claim about them.
#
# The one deliberate prompt lives behind `scripts/github-token.sh --bootstrap`, which a human runs
# once and which resets this list again to reach the Dev Containers helper directly.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TOKEN_HELPER="!${SCRIPT_DIR}/github-token.sh"
GITHUB_HELPER_KEY='credential.https://github.com.helper'

current_github_helpers="$(git config --global --get-all "$GITHUB_HELPER_KEY" 2>/dev/null || true)"
expected_github_helpers="$(printf '\n%s' "$TOKEN_HELPER")"

if [ "$current_github_helpers" != "$expected_github_helpers" ]; then
    git config --global --unset-all "$GITHUB_HELPER_KEY" 2>/dev/null || true
    git config --global --add "$GITHUB_HELPER_KEY" '' 2>/dev/null || true
    git config --global --add "$GITHUB_HELPER_KEY" "$TOKEN_HELPER" 2>/dev/null || true
    log "github.com credentials now come from the cache only (scripts/github-token.sh)."
fi

log "Container git config normalized."
