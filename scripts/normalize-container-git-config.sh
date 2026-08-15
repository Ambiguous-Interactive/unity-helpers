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

log "Container git config normalized."
