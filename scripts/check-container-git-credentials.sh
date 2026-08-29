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

# Applicability. The dangerous condition is specifically the Dev Containers helper being reachable:
# it answers by raising a dialog on the OWNER'S DESKTOP, and an unattended `git push` then hangs
# until its timeout. A machine with no helper at all falls through to GIT_ASKPASS, which this
# container answers with scripts/git-askpass-refuse.sh; a developer's own machine has its own
# credential manager and this script has no opinion about it.
#
# Signature rather than an environment variable: REMOTE_CONTAINERS is absent from a plain
# `docker exec`, from a cron job, and from a CI container, all of which can still push.
devcontainer_helper=''
while IFS= read -r helper_value; do
    case "$helper_value" in
        *vscode-remote-containers* | *git-credential-helper*)
            devcontainer_helper="$helper_value"
            break
            ;;
        *) ;;
    esac
done <<EOF
$(git config --get-all credential.helper 2>/dev/null || true)
EOF

if [ -z "$devcontainer_helper" ]; then
    note 'No Dev Containers credential helper is registered; nothing to normalize here.'
    exit 0
fi

if [ ! -x "$TOKEN_SCRIPT" ]; then
    log "The cached-token helper is missing or not executable: $TOKEN_SCRIPT"
    log 'A credential helper git cannot execute is the same failure as none at all.'
    exit 1
fi

# `git config --get-all` lists the URL-scoped values across every scope, in the order git reads
# them (system, then global, then local). git accumulates the generic credential.helper values
# FIRST -- the Dev Containers one lives in /etc/gitconfig -- so what matters for this URL is what
# survives the last EMPTY value, which resets the accumulated list. The surviving list must be
# exactly one entry, and it must be ours.
broken_urls=''
detail=''
while IFS= read -r github_url; do
    [ -n "$github_url" ] || continue
    helper_key="credential.${github_url}.helper"

    values=()
    while IFS= read -r value; do
        values+=("$value")
    done < <(git config --get-all "$helper_key" 2>/dev/null)

    effective=()
    saw_reset=0
    for value in ${values+"${values[@]}"}; do
        if [ -z "$value" ]; then
            effective=()
            saw_reset=1
            continue
        fi
        effective+=("$value")
    done

    if [ "$saw_reset" = "1" ] && [ "${#effective[@]}" = "1" ] && [ "${effective[0]}" = "$TOKEN_HELPER" ]; then
        continue
    fi

    broken_urls="${broken_urls}${broken_urls:+ }${github_url}"
    if [ "${#values[@]}" = "0" ]; then
        detail="${detail}  ${helper_key} is unset, so the Dev Containers helper answers it."$'\n'
    else
        # Joined explicitly: "${values[*]}" would separate on IFS and hide which entry is the
        # empty reset, which is the one whose absence causes this failure.
        joined=''
        for value in ${values+"${values[@]}"}; do
            joined="${joined}${joined:+ | }${value:-<empty reset>}"
        done
        detail="${detail}  ${helper_key} = ${joined}"$'\n'
    fi
done <<EOF
$(bash "$TOKEN_SCRIPT" --hosts)
EOF

if [ -n "$broken_urls" ]; then
    log 'github.com is still pointed at the Dev Containers credential helper.'
    printf '%s' "$detail" >&2
    log "Expected, for each URL: an empty reset value, then exactly ${TOKEN_HELPER}"
    log "That helper answers by raising a dialog on the OWNER'S DESKTOP, so an unattended"
    log '`git push` hangs until its timeout and pushes nothing. `github-token.sh` answering and'
    log 'API calls succeeding prove nothing: reads come from the same cache either way.'
    log 'Fix: bash scripts/normalize-container-git-config.sh'
    log '  or: npm run check:container-git-credentials -- --fix'
    exit 1
fi

note 'github.com resolves through the cached-token helper (scripts/github-token.sh).'
exit 0
