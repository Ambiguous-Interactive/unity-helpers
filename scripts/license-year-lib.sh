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
# Only a STAGED rename is visible here, and that is a real limit rather than an oversight. An
# unstaged rename is a deletion plus an untracked file; `git diff -M` cannot pair those, because
# the new file is not in the index for it to compare against. Such a file still resolves to the
# current year. `git mv` stages, and staging before validating is the flow
# .llm/skills/validate-before-commit.md already prescribes.
#
# Usage:
#   source "$SCRIPT_DIR/license-year-lib.sh"
#   license_year_init "$REPO_ROOT" "$REPO_START_YEAR" "$CURRENT_YEAR"
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

    _license_year_history_result=$(
        git -C "$LICENSE_YEAR_REPO_ROOT" log --follow --diff-filter=A \
            --format=%ad --date=format:%Y -- "$rel" 2>/dev/null | tail -1 || true
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
    done < <(git -C "$LICENSE_YEAR_REPO_ROOT" diff --cached -M --name-status -z 2>/dev/null || true)
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
