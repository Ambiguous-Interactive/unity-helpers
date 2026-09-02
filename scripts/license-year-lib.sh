#!/usr/bin/env bash
# license-year-lib.sh - Shared copyright-year resolution for the MIT license header tools.
#
# Sourced by scripts/update-license-headers.sh (the fixer) and scripts/audit-license-years.sh
# (the auditor) so the two cannot answer the same question two different ways.
#
# Before this library existed they each ran their own `git log --follow --diff-filter=A` and
# issue #668 fell straight through the gap. `git log` never consults the index, so for a STAGED
# rename (`git mv old.cs new.cs`) that query returns nothing: the new path exists only in the
# index. Both scripts then fell back to the current year -- the fixer rewrote a 2024 header to
# 2026, and the auditor, asking the identical question, agreed and reported success. The
# disagreement only appeared once the rename was committed, when the auditor's repository-wide
# scan followed the rename back through the `R` status, expected 2024, and reddened CI.
#
# Resolution order for a repository-relative path:
#   1. Committed history for the path (`git log --follow --diff-filter=A`).
#   2. The source of a STAGED rename onto that path, resolved recursively.
#   3. The current year.
# The answer is then clamped up to the repository's start year.
#
# Step 1 is the expensive one: one `git log` process per path. Over the 2,163 tracked .cs files
# here a full run of the fixer took 9m49s, ~95% of it inside that loop (#674).
# `license_year_prime` answers step 1 for every path in HEAD with ONE history walk, and a caller
# about to ask about the whole tree calls it first. Priming never replaces steps 2 and 3: a
# staged rename's new path is not in HEAD, so the walk has nothing to say about it.
#
# Only a STAGED rename is visible here, and that is a real limit rather than an oversight. An
# unstaged rename is a deletion plus an untracked file; `git diff -M` cannot pair those, because
# the new file is not in the index for it to compare against. Such a file still resolves to the
# current year. `git mv` stages, and staging before validating is the flow
# .llm/skills/validate-before-commit.md already prescribes.
#
# Usage:
#   source "$SCRIPT_DIR/license-year-lib.sh"
#   license_year_init "$REPO_ROOT" "$REPO_START_YEAR" "$CURRENT_YEAR"
#   license_year_prime                     # optional; worth it for a whole-tree scan
#   license_year_resolve "Runtime/Foo.cs"
#   printf '%s\n' "$LICENSE_YEAR_RESULT"   # clamped year the header should carry
#   printf '%s\n' "$LICENSE_YEAR_SOURCE"   # history | staged-rename | current-year
#
# Every function reports through globals rather than stdout so callers can invoke them without a
# subshell: the staged-rename map is built once per process, and a command substitution would
# throw it away and rebuild it per file.

LICENSE_YEAR_REPO_ROOT=""
LICENSE_YEAR_START_YEAR="2023"
LICENSE_YEAR_CURRENT_YEAR=""
LICENSE_YEAR_RESULT=""
LICENSE_YEAR_SOURCE=""

declare -A _license_year_staged_rename_sources=()
_license_year_staged_renames_loaded=false
_license_year_history_result=""

# Committed-history year per repository path, filled by license_year_prime. Empty until a caller
# primes: a whole-tree scan does, a --paths run deliberately does not, because one history walk
# costs more than the handful of per-path queries it would save.
declare -A LICENSE_YEAR_HISTORY_YEARS=()
_license_year_primed=false

# Renames chain only inside a single index entry, which git already collapses to one R record, so
# the recursion below normally runs once. The cap exists so a malformed map can never spin.
_LICENSE_YEAR_MAX_RENAME_DEPTH=16

# Point the resolver at a repository.
# Args:
#   $1 - repository root (absolute path)
#   $2 - repository start year (optional; defaults to 2023)
#   $3 - current year (optional; defaults to `date +%Y`)
license_year_init() {
    LICENSE_YEAR_REPO_ROOT="$1"

    if [[ $# -gt 1 && -n "${2:-}" ]]; then
        LICENSE_YEAR_START_YEAR="$2"
    fi

    if [[ $# -gt 2 && -n "${3:-}" ]]; then
        LICENSE_YEAR_CURRENT_YEAR="$3"
    else
        LICENSE_YEAR_CURRENT_YEAR=$(date +%Y)
    fi

    unset '_license_year_staged_rename_sources'
    declare -gA _license_year_staged_rename_sources=()
    _license_year_staged_renames_loaded=false
    unset 'LICENSE_YEAR_HISTORY_YEARS'
    declare -gA LICENSE_YEAR_HISTORY_YEARS=()
    _license_year_primed=false
    LICENSE_YEAR_RESULT=""
    LICENSE_YEAR_SOURCE=""
}

# Clamp a year to the repository start year, in the one place both scripts read it from.
# Args:
#   $1 - candidate year
# Sets:
#   LICENSE_YEAR_RESULT
license_year_clamp() {
    local year="$1"

    if [[ ! "$year" =~ ^[0-9]{4}$ ]]; then
        LICENSE_YEAR_RESULT="$LICENSE_YEAR_CURRENT_YEAR"
        return
    fi

    # 10# keeps a leading zero from being read as octal, which under `set -e` aborts the run.
    if [[ $((10#$year)) -lt $((10#$LICENSE_YEAR_START_YEAR)) ]]; then
        LICENSE_YEAR_RESULT="$LICENSE_YEAR_START_YEAR"
    else
        LICENSE_YEAR_RESULT="$year"
    fi
}

# Read the year a path was added in COMMITTED history. Empty when git has no such commit.
# Args:
#   $1 - repository-relative path
# Sets:
#   _license_year_history_result
_license_year_committed_year() {
    local rel="$1"

    # A primed map answers for every path in HEAD without forking git. A path it does not carry
    # is NOT proof of absence -- a staged rename's new path is never in there, and neither is a
    # staged addition -- so fall through to the per-path query rather than concluding "no
    # history". That fall-through is also what keeps a primed run and an unprimed one answering
    # alike: each asks git the same question, one of them in bulk.
    if [[ -n "${LICENSE_YEAR_HISTORY_YEARS[$rel]+_}" ]]; then
        _license_year_history_result="${LICENSE_YEAR_HISTORY_YEARS[$rel]}"
        return
    fi

    _license_year_history_result=$(
        git -C "$LICENSE_YEAR_REPO_ROOT" log --follow --diff-filter=A \
            --format=%ad --date=format:%Y -- "$rel" 2>/dev/null | tail -1 || true
    )
}

# Bulk-prime the committed-history year for every path in HEAD with ONE history walk.
#
# The alternative is one `git log --follow --diff-filter=A -- <path>` process per file, and that is
# what made a full run of the fixer take 9m49s (#674). Measured on 100 files while diagnosing it:
# the fixer 32.2s, of which a bare `git log --follow` loop alone was 30.8s. One walk answers for
# all 2,163 tracked .cs files instead, and the same full run then takes seconds.
#
# --reverse plays history oldest-first, so the FIRST year seen for a path is its creation year and
# a later modification never overwrites it. A/C/R/D are folded the way the audit's cache always
# folded them: a copy inherits its source's year, a rename carries the year to the new path and
# releases the old one, and a delete drops the path so a later re-add is a genuinely new file.
#
# The walk answers for TRACKED paths only. A staged rename's new path is not in HEAD, so
# license_year_resolve still falls back to the per-file query and then to the staged-rename map for
# those. Priming is an optimization for the common case, never a replacement for that resolution.
#
# Idempotent: the walk runs at most once per process, so a caller may prime unconditionally.
license_year_prime() {
    if [[ "$_license_year_primed" == true ]]; then
        return
    fi
    _license_year_primed=true

    # `git log` needs at least one commit; a fresh repository has none.
    if ! git -C "$LICENSE_YEAR_REPO_ROOT" rev-parse --verify --quiet HEAD >/dev/null 2>&1; then
        return
    fi

    local history_year=""
    local line=""
    local status=""
    local first_path=""
    local second_path=""

    while IFS= read -r line; do
        if [[ "$line" =~ ^YEAR:([0-9]{4})$ ]]; then
            history_year="${BASH_REMATCH[1]}"
            continue
        fi

        if [[ -z "$line" ]]; then
            continue
        fi

        IFS=$'\t' read -r status first_path second_path <<< "$line"
        case "$status" in
            A*)
                LICENSE_YEAR_HISTORY_YEARS["$first_path"]="$history_year"
                ;;
            C*)
                if [[ -n "${LICENSE_YEAR_HISTORY_YEARS[$first_path]+_}" ]]; then
                    LICENSE_YEAR_HISTORY_YEARS["$second_path"]="${LICENSE_YEAR_HISTORY_YEARS[$first_path]}"
                else
                    LICENSE_YEAR_HISTORY_YEARS["$second_path"]="$history_year"
                fi
                ;;
            R*)
                if [[ -n "${LICENSE_YEAR_HISTORY_YEARS[$first_path]+_}" ]]; then
                    LICENSE_YEAR_HISTORY_YEARS["$second_path"]="${LICENSE_YEAR_HISTORY_YEARS[$first_path]}"
                    unset "LICENSE_YEAR_HISTORY_YEARS[$first_path]"
                else
                    LICENSE_YEAR_HISTORY_YEARS["$second_path"]="$history_year"
                fi
                ;;
            D*)
                unset "LICENSE_YEAR_HISTORY_YEARS[$first_path]"
                ;;
        esac
    done < <(
        git -C "$LICENSE_YEAR_REPO_ROOT" -c diff.renameLimit=999999 log \
            --reverse \
            --name-status \
            --diff-filter=ACRD \
            --format='YEAR:%ad' \
            --date=format:%Y \
            --find-renames \
            --find-copies-harder
    )
}

# Build the staged rename/copy map once per process: new path -> source path.
_license_year_load_staged_renames() {
    if [[ "$_license_year_staged_renames_loaded" == true ]]; then
        return
    fi
    _license_year_staged_renames_loaded=true

    # `git diff --cached` needs a commit to compare against.
    if ! git -C "$LICENSE_YEAR_REPO_ROOT" rev-parse --verify --quiet HEAD >/dev/null 2>&1; then
        return
    fi

    local record=""
    local source_path=""
    local target_path=""

    # The diff MUST be unfiltered. Measured on git 2.51.1 while diagnosing #668:
    # `git diff --cached -M --name-status` reports `R100<TAB>old<TAB>new`, but adding
    # `-- <new path>` hides the deletion half of the pair, so git reports a plain `A<TAB>new` and
    # rename detection becomes impossible. Look the new path up in the whole-index result instead.
    # -z is what makes an unusual path safe: without it git quotes such a path and the lookup key
    # would no longer be the path the caller asked about.
    #
    # -C --find-copies-harder, and the rename limit, match the auditor's repository-wide walk --
    # which is the only answer CI enforces, so the fixer has to give the same one. Measured on the
    # 326-file commit that extracted 146 test types: without the limit git prints `only found
    # copies from modified paths due to too many files` and reports ZERO copies, which reads as
    # "no copies" rather than "gave up". With it, 24, in 1.1s, including every file whose header
    # the auditor then rejected. Copy detection is noisy at the margin -- one pairing was two
    # unrelated 50%-similar test types -- but agreeing with the gate beats being independently
    # right, which is the whole lesson of #668.
    while IFS= read -r -d '' record; do
        case "$record" in
            R*|C*)
                IFS= read -r -d '' source_path || break
                IFS= read -r -d '' target_path || break
                # A copy propagates the source's year the same way, matching how the auditor's
                # repository-wide history walk already treats a `C` status.
                _license_year_staged_rename_sources["$target_path"]="$source_path"
                ;;
            *)
                IFS= read -r -d '' source_path || break
                ;;
        esac
    done < <(
        git -C "$LICENSE_YEAR_REPO_ROOT" -c diff.renameLimit=999999 diff --cached \
            -M -C --find-copies-harder --name-status -z 2>/dev/null || true
    )
}

# Resolve the copyright year for a repository-relative path.
# Args:
#   $1 - repository-relative path
# Sets:
#   LICENSE_YEAR_RESULT - the clamped year the header should carry
#   LICENSE_YEAR_SOURCE - history | staged-rename | current-year
#
# These two globals are the library's return channel and every reader lives in another file, which
# is exactly the shape SC2034 cannot see.
# shellcheck disable=SC2034
license_year_resolve() {
    local current="$1"
    local depth=0
    local followed_rename=false

    while true; do
        _license_year_committed_year "$current"
        if [[ -n "$_license_year_history_result" ]]; then
            license_year_clamp "$_license_year_history_result"
            if [[ "$followed_rename" == true ]]; then
                LICENSE_YEAR_SOURCE="staged-rename"
            else
                LICENSE_YEAR_SOURCE="history"
            fi
            return
        fi

        _license_year_load_staged_renames
        if [[ -z "${_license_year_staged_rename_sources[$current]+set}" ]]; then
            break
        fi

        depth=$((depth + 1))
        if [[ "$_LICENSE_YEAR_MAX_RENAME_DEPTH" -lt "$depth" ]]; then
            break
        fi

        current="${_license_year_staged_rename_sources[$current]}"
        followed_rename=true
    done

    LICENSE_YEAR_RESULT="$LICENSE_YEAR_CURRENT_YEAR"
    LICENSE_YEAR_SOURCE="current-year"
}
