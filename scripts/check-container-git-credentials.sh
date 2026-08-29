#!/usr/bin/env bash
# Answers, in milliseconds, the question whose only other answer is a five-minute hang:
# will `git push` to github.com reach the cached-token helper, or the Dev Containers helper?
#
# Why this exists (#600). scripts/normalize-container-git-config.sh points github.com at
# scripts/github-token.sh and resets the inherited helper list, and .devcontainer/post-start.sh runs
# it on every attach -- non-fatally. When that step is skipped or fails, NOTHING downstream notices:
# `github-token.sh` still answers, `curl` against the API still works, `git fetch` and
# `git ls-remote` still work. The first thing that breaks is a `push`, minutes or hours later, as an
# unexplained hang plus a dialog on a human's desktop that nobody is watching.
#
# So the postcondition is asserted here rather than assumed. This never invokes a credential helper
# and never asks for a credential -- it reads git config only.
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
TOKEN_SCRIPT="${SCRIPT_DIR}/github-token.sh"
TOKEN_HELPER="!${TOKEN_SCRIPT}"

quiet=0
fix=0

log() { printf '[check-container-git-credentials] %s\n' "$1" >&2; }
note() { [ "$quiet" = "1" ] || printf '[check-container-git-credentials] %s\n' "$1"; }

usage() {
    cat >&2 <<'EOF'
Usage: bash scripts/check-container-git-credentials.sh [--quiet] [--fix]

  (no flags)   Report whether github.com resolves through the cached-token helper.
  --quiet      Print nothing when healthy or not applicable. Failures still report.
  --fix        Run scripts/normalize-container-git-config.sh first, then re-check.

Exit 0 when the postcondition holds or this is not a Dev Containers environment,
1 when github.com would still reach the helper that raises a desktop dialog.
EOF
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --quiet | -q) quiet=1 ;;
        --fix) fix=1 ;;
        -h | --help | help)
            usage
            exit 0
            ;;
        *)
            log "Unknown argument: $1"
            usage
            exit 64
            ;;
    esac
    shift
done

if [ "$fix" = "1" ]; then
    if ! bash "${SCRIPT_DIR}/normalize-container-git-config.sh"; then
        log 'scripts/normalize-container-git-config.sh failed; re-checking anyway.'
    fi
fi

# One ordered read of every scope, anchored at this script's own repository.
#
# ORDER is the whole answer, so it is read once and never re-queried per key. Git builds the helper
# list for a URL by walking the WHOLE config in read order -- system, then global, then local --
# collecting `credential.helper` and every `credential.<scope>.helper` whose scope matches, with an
# empty value resetting the accumulated list. `git config --get-all credential.<url>.helper` cannot
# answer that question: it reports the URL-scoped section alone, so it says "only our helper is
# configured here" for a config where git would still run an inherited generic one. That is the
# trap scripts/tests/test-normalize-container-git-config.sh has warned about since #592, and it is
# exactly how a generic helper registered AFTER the URL block -- `.git/config` is read after
# `~/.gitconfig` -- stays invisible while it hangs every push.
#
# Anchored at REPO_ROOT rather than the caller's cwd, per the repo rule that a script deriving its
# own root must anchor its git calls there: the answer must be about THIS repository's push.
#
# Record format of `--show-origin --null` is `<origin>NUL<key>LF<value>NUL`. `--get-regexp` filters
# during the same ordered walk as `--list`, so order is preserved -- it is here because this
# repository's `.git/config` alone is 44 KB of branch bookkeeping, and reading the whole of it into
# bash cost more than every other line of this script put together.
config_keys=()
config_values=()
config_origins=()
while IFS= read -r -d '' origin; do
    IFS= read -r -d '' record || break
    key="${record%%$'\n'*}"
    if [ "$key" = "$record" ]; then
        # A key with no value at all. Git refuses to run such a helper, and it is emphatically NOT
        # the empty reset, so it is recorded as something that can never be our helper.
        value='<no value>'
    else
        value="${record#*$'\n'}"
    fi
    case "$key" in
        credential.helper | credential.*.helper) ;;
        *) continue ;;
    esac
    config_keys+=("$key")
    config_values+=("$value")
    config_origins+=("$origin")
done < <(git -C "$REPO_ROOT" config --get-regexp --show-origin --null '^credential\.' 2>/dev/null)

# Does a `credential.<scope>.helper` entry apply to a bare `protocol://host` URL?
#
# Git matches scope against the request by protocol, host (with `*` wildcards allowed in the
# leading label) and path prefix, and a scope carrying a username matches only a request with the
# same username. The URLs asked about here have no username and no path, so a scope with either
# cannot apply.
scope_applies() {
    local scope="$1"
    local url="$2"
    local scope_proto=''
    local scope_rest="$scope"
    case "$scope" in
        *://*)
            scope_proto="${scope%%://*}"
            scope_rest="${scope#*://}"
            ;;
    esac
    scope_rest="${scope_rest%/}"
    case "$scope_rest" in
        *@* | */*) return 1 ;;
    esac
    local url_proto="${url%%://*}"
    local url_host="${url#*://}"
    url_host="${url_host%/}"
    if [ -n "$scope_proto" ] && [ "${scope_proto,,}" != "${url_proto,,}" ]; then
        return 1
    fi
    # Unquoted on purpose: the expansion is used as a glob so `*.github.com` matches the way git
    # matches it. A value expanded into a case pattern is one pattern -- `|` in it stays literal.
    case "${url_host,,}" in
        ${scope_rest,,}) return 0 ;;
    esac
    return 1
}

# Applicability. The dangerous condition is specifically the Dev Containers helper being reachable:
# it answers by raising a dialog on the OWNER'S DESKTOP, and an unattended `git push` then hangs
# until its timeout. A machine with no helper at all falls through to GIT_ASKPASS, which this
# container answers with scripts/git-askpass-refuse.sh; a developer's own machine has its own
# credential manager and this script has no opinion about it.
#
# Signature rather than an environment variable: REMOTE_CONTAINERS is absent from a plain
# `docker exec`, from a cron job, and from a CI container, all of which can still push.
devcontainer_helper=''
for value in ${config_values+"${config_values[@]}"}; do
    case "$value" in
        *vscode-remote-containers* | *git-credential-helper*)
            devcontainer_helper="$value"
            break
            ;;
        *) ;;
    esac
done

if [ -z "$devcontainer_helper" ]; then
    note 'No Dev Containers credential helper is registered; nothing to normalize here.'
    exit 0
fi

if [ ! -x "$TOKEN_SCRIPT" ]; then
    log "The cached-token helper is missing or not executable: $TOKEN_SCRIPT"
    log 'A credential helper git cannot execute is the same failure as none at all.'
    exit 1
fi

# The URL list comes from the helper, and a list that fails to arrive is a FAILURE, never an empty
# audit. `while read` over a command substitution whose status nobody checks turns "the helper is
# broken" into "no URL was found to be broken", which prints the healthy message (#600 follow-up).
if ! hosts_output="$(bash "$TOKEN_SCRIPT" --hosts 2>&1)"; then
    log "scripts/github-token.sh --hosts failed, so there is nothing to verify against:"
    printf '%s\n' "$hosts_output" >&2
    exit 1
fi

urls=()
while IFS= read -r github_url; do
    [ -n "$github_url" ] || continue
    urls+=("$github_url")
done <<EOF
$hosts_output
EOF

if [ "${#urls[@]}" = "0" ]; then
    log 'scripts/github-token.sh --hosts declared no URLs, so this check verified nothing.'
    log 'An empty audit is a failure here: every URL it claims resets the inherited helper list,'
    log 'and a URL nobody claims is the one whose push hangs.'
    exit 1
fi

broken_urls=''
detail=''
for github_url in "${urls[@]}"; do
    effective=()
    effective_sources=()
    index=0
    while [ "$index" -lt "${#config_keys[@]}" ]; do
        key="${config_keys[$index]}"
        value="${config_values[$index]}"
        origin="${config_origins[$index]}"
        index=$((index + 1))

        if [ "$key" != 'credential.helper' ]; then
            scope="${key#credential.}"
            scope="${scope%.helper}"
            scope_applies "$scope" "$github_url" || continue
        fi

        if [ -z "$value" ]; then
            effective=()
            effective_sources=()
            continue
        fi
        effective+=("$value")
        effective_sources+=("${key} @ ${origin}")
    done

    if [ "${#effective[@]}" = "1" ] && [ "${effective[0]}" = "$TOKEN_HELPER" ]; then
        continue
    fi

    broken_urls="${broken_urls}${broken_urls:+ }${github_url}"
    detail="${detail}  credential.${github_url}.helper does not win. git would run, in order:"$'\n'
    if [ "${#effective[@]}" = "0" ]; then
        detail="${detail}    (nothing -- every helper was reset, so git falls through to GIT_ASKPASS)"$'\n'
    else
        position=0
        while [ "$position" -lt "${#effective[@]}" ]; do
            detail="${detail}    $((position + 1)). ${effective[$position]}  [${effective_sources[$position]}]"$'\n'
            position=$((position + 1))
        done
    fi
done

if [ -n "$broken_urls" ]; then
    log 'github.com is still pointed at the Dev Containers credential helper.'
    printf '%s' "$detail" >&2
    log "Expected, for each URL: exactly one helper, and it is ${TOKEN_HELPER}"
    log "That helper answers by raising a dialog on the OWNER'S DESKTOP, so an unattended"
    log '`git push` hangs until its timeout and pushes nothing. `github-token.sh` answering and'
    log 'API calls succeeding prove nothing: reads come from the same cache either way.'
    log 'Fix: bash scripts/normalize-container-git-config.sh'
    log '  or: npm run check:container-git-credentials -- --fix'
    exit 1
fi

note 'github.com resolves through the cached-token helper (scripts/github-token.sh).'
exit 0
