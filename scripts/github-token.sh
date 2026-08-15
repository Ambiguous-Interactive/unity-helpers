#!/usr/bin/env bash
# The ONLY supported way to obtain a GitHub credential inside this devcontainer.
#
# Why it exists: the Dev Containers credential helper answers by raising a dialog on the OWNER'S
# DESKTOP. Every invocation raises another one, and `git push`, `git fetch` and `git credential fill`
# all invoke it -- so guidance that said "run `git credential fill`" spent an owner interruption per
# agent, per session, and a retry loop spent several. This script never contacts that helper except
# on the explicit `--bootstrap` path, which a human runs once.
#
# Resolution order (none of it prompts):
#   1. $GITHUB_TOKEN / $GH_TOKEN, when non-empty. They are exported-but-EMPTY in this container
#      (`remoteEnv` forwards a host variable the host does not set), so a `-z` test passes `set -u`
#      and yields no credential -- which is why emptiness, not definedness, is what is tested here.
#   2. The cache file, mode 0600, written by --bootstrap or --store-stdin.
#   3. Nothing: exit 3 with the command that fixes it. A missing credential is reported, never
#      prompted for.
#
# It is also a git credential helper (`get`/`store`/`erase`), so `git push` reads the same cache
# instead of raising a dialog. scripts/normalize-container-git-config.sh installs it for
# github.com in the container's ~/.gitconfig.
set -euo pipefail

CACHE_HOME="${HOME:-}"
if [ -z "$CACHE_HOME" ]; then
    # Reported rather than defaulted: a cache written somewhere the caller did not expect is a
    # secret in a surprising place, and `set -u` would otherwise fail here with "unbound variable".
    printf '[github-token] HOME is not set, so there is nowhere to cache a credential.\n' >&2
    printf '[github-token] Set HOME, or point UNITY_HELPERS_GITHUB_TOKEN_CACHE at a file.\n' >&2
    exit 78
fi

CACHE_FILE="${UNITY_HELPERS_GITHUB_TOKEN_CACHE:-${CACHE_HOME}/.cache/unity-helpers-devcontainer/github-token}"
BOOTSTRAP_TIMEOUT_SECONDS="${UNITY_HELPERS_GITHUB_TOKEN_TIMEOUT:-120}"

log() { printf '[github-token] %s\n' "$1" >&2; }

usage() {
    cat >&2 <<'EOF'
Usage: scripts/github-token.sh [command]

  (no command)     Print the cached token on stdout. Exit 3 when there is none.
  --check          Exit 0 when a token is available, 1 when not. Prints nothing.
  --path           Print the cache file path.
  --store-stdin    Read a token from stdin and cache it. The prompt-free way to
                   supply a personal access token.
  --bootstrap      Ask the Dev Containers credential helper ONCE and cache the
                   answer. This raises a dialog on the owner's desktop, so it is
                   a deliberate human action and never something an agent runs.
  --erase          Delete the cached token.
  get|store|erase  git credential helper protocol (reads key=value on stdin).

Never echo the value: redirect it into a variable or a 0600 file.
EOF
}

# Trailing whitespace and newlines are what a pasted token carries, and they produce an
# Authorization header the API rejects with a message that says nothing about whitespace.
sanitize_token() {
    printf '%s' "$1" | tr -d '\r\n' | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//'
}

read_cache() {
    [ -f "$CACHE_FILE" ] || return 1
    local cached
    cached="$(sanitize_token "$(cat "$CACHE_FILE")")"
    [ -n "$cached" ] || return 1
    printf '%s' "$cached"
}

write_cache() {
    local token="$1"
    local dir
    dir="$(dirname "$CACHE_FILE")"
    mkdir -p "$dir"
    chmod 700 "$dir" 2>/dev/null || true
    # Create the file empty and lock it down BEFORE the secret lands in it: writing first and
    # chmod-ing after leaves a window where the token is world-readable.
    : > "$CACHE_FILE"
    chmod 600 "$CACHE_FILE"
    printf '%s' "$token" > "$CACHE_FILE"
}

# Environment first, cache second. Emptiness is the test, not definedness.
resolve_token() {
    local candidate
    for candidate in "${GITHUB_TOKEN:-}" "${GH_TOKEN:-}"; do
        candidate="$(sanitize_token "$candidate")"
        if [ -n "$candidate" ]; then
            printf '%s' "$candidate"
            return 0
        fi
    done
    read_cache
}

missing_token_message() {
    log 'No GitHub credential is available, and this script never prompts for one.'
    log 'A human runs ONE of these, once per container:'
    log '  npm run github:token:bootstrap        # asks the Dev Containers helper (one desktop dialog)'
    log '  npm run github:token:store            # paste a personal access token on stdin'
}

command="${1:-}"

case "$command" in
    -h | --help | help)
        usage
        exit 0
        ;;
    --path)
        printf '%s\n' "$CACHE_FILE"
        exit 0
        ;;
    --check)
        resolve_token > /dev/null 2>&1 && exit 0
        exit 1
        ;;
    --erase | erase)
        rm -f "$CACHE_FILE"
        exit 0
        ;;
    --store-stdin)
        supplied="$(sanitize_token "$(cat)")"
        if [ -z "$supplied" ]; then
            log 'Nothing was supplied on stdin; the cache is unchanged.'
            exit 2
        fi
        write_cache "$supplied"
        log "Cached a credential at $CACHE_FILE (mode 0600)."
        exit 0
        ;;
    --bootstrap)
        if existing="$(resolve_token)"; then
            unset existing
            log 'A credential is already available; not asking the helper. Use --erase first to replace it.'
            exit 0
        fi
        # The helper this reaches is the one registered in --system by Dev Containers. It is named
        # explicitly, and the github.com-scoped list is reset first, because ~/.gitconfig now points
        # that list at THIS script -- left in place, bootstrap would ask itself and find nothing.
        system_helper="$(git config --system --get-all credential.helper 2>/dev/null | head -n 1 || true)"
        if [ -z "$system_helper" ]; then
            log 'No credential helper is registered in /etc/gitconfig, so there is nothing to ask.'
            log 'Use: npm run github:token:store'
            exit 4
        fi
        log 'Asking the Dev Containers credential helper once. Answer the dialog on your desktop.'
        started="$(date +%s)"
        filled=''
        set +e
        filled="$(printf 'protocol=https\nhost=github.com\n\n' \
            | GIT_TERMINAL_PROMPT=0 timeout "$BOOTSTRAP_TIMEOUT_SECONDS" \
                git -c 'credential.https://github.com.helper=' \
                    -c "credential.https://github.com.helper=$system_helper" \
                    credential fill 2>/dev/null \
            | sed -n 's/^password=//p' | head -n 1)"
        fill_status=$?
        set -e
        elapsed=$(($(date +%s) - started))
        filled="$(sanitize_token "$filled")"
        if [ -z "$filled" ]; then
            # Hang versus immediate error is the discriminator, never empty output: a blocked helper
            # is a dialog nobody answered, and reading that as "no credential exists" is how three
            # sessions concluded the container had none.
            if [ "$elapsed" -ge "$BOOTSTRAP_TIMEOUT_SECONDS" ] || [ "$fill_status" = "124" ]; then
                log "The helper blocked for ${elapsed}s: a dialog is waiting on the owner's desktop."
                log 'Answer it and re-run, or supply a token with: npm run github:token:store'
                exit 5
            fi
            log "The helper answered in ${elapsed}s with no credential."
            log 'Supply one with: npm run github:token:store'
            exit 3
        fi
        write_cache "$filled"
        log "Cached a credential at $CACHE_FILE (mode 0600) after ${elapsed}s. Nothing will ask again."
        exit 0
        ;;
    get)
        # git credential protocol. Answer only for github.com; anything else is not ours to serve,
        # and claiming it would break the host's other remotes.
        #
        # `url=` is read as well as `host=`. Git normally decomposes a URL before calling a helper,
        # but a caller may feed one straight through -- and a helper that ignored it would fall back
        # to its "no host stated" branch and hand a GitHub token to whatever host was named.
        host=''
        while IFS= read -r line; do
            [ -n "$line" ] || break
            case "$line" in
                host=*) host="${line#host=}" ;;
                url=*)
                    stripped="${line#url=}"
                    stripped="${stripped#*://}"
                    stripped="${stripped#*@}"
                    host="${stripped%%/*}"
                    ;;
                *) ;;
            esac
        done
        # A port is part of the host field git sends; github.com:443 is still github.com.
        host="${host%%:*}"
        if [ -n "$host" ] && [ "$host" != "github.com" ]; then
            exit 0
        fi
        if token="$(resolve_token)"; then
            printf 'username=x-access-token\n'
            printf 'password=%s\n' "$token"
            exit 0
        fi
        missing_token_message
        exit 0
        ;;
    store)
        # Cache a credential git obtained elsewhere, so it is asked for at most once ever.
        host=''
        password=''
        while IFS= read -r line; do
            [ -n "$line" ] || break
            case "$line" in
                host=*) host="${line#host=}" ;;
                url=*)
                    stripped="${line#url=}"
                    stripped="${stripped#*://}"
                    stripped="${stripped#*@}"
                    host="${stripped%%/*}"
                    ;;
                password=*) password="${line#password=}" ;;
                *) ;;
            esac
        done
        host="${host%%:*}"
        password="$(sanitize_token "$password")"
        if [ "$host" = "github.com" ] && [ -n "$password" ]; then
            write_cache "$password"
        fi
        exit 0
        ;;
    '')
        if token="$(resolve_token)"; then
            printf '%s\n' "$token"
            exit 0
        fi
        missing_token_message
        exit 3
        ;;
    *)
        log "Unknown command: $command"
        usage
        exit 64
        ;;
esac
