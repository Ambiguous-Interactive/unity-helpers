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

# The bulk walk's pathspec. Both callers -- the fixer and the auditor -- only ever ask about .cs
# paths, and the walk's cost is dominated by the copy detection's candidate set, so narrowing it to
# the extension the license headers live on is a straight saving (#680). See license_year_prime.
_LICENSE_YEAR_PRIME_PATHSPEC='*.cs'

# The tree the walk reads gitattributes from, and the reason it is not the working tree.
#
# `git log --name-status` diffs tree against tree: both sides are blobs out of the object
# database, and no attribute can change which A/C/R/D records come out of that. Git looks them up
# anyway -- once per candidate the copy detection considers -- by asking the filesystem for
# `.gitattributes` in every directory of every candidate path, and `--find-copies-harder` is
# precisely the flag that makes every file in the tree a candidate. Measured with strace over 25
# commits of this history: 8,364 of the shipped walk's 8,705 file-system calls are those lookups,
# and all but one MISS, because this repository keeps a single `.gitattributes` at its root.
#
# Pointing `attr.tree` at the EMPTY tree answers every lookup from an empty attribute set without
# touching the filesystem. It replaces only the in-tree `.gitattributes`; `$GIT_DIR/info/attributes`
# and the global and system files are read by both variants, so the one source this removes is the
# one no tracked path uses: `git check-attr diff` reports `unspecified` for every tracked path, and
# `diff` is the only attribute rename and copy detection can reach. test-license-year-copy-detection
# asserts that over the whole tree rather than over the .cs paths this walk narrows to, because a
# .cs file copied from a non-.cs one is exactly what the narrowing already cannot see.
#
# It is a literal rather than a computed object id so that it degrades into the shipped behavior:
# git before 2.40 ignores the unknown config key, and a value this repository's hash algorithm
# cannot resolve falls back to the working tree. Either way the answer is the one below, arrived at
# more slowly -- never a different one.
_LICENSE_YEAR_ATTRIBUTES_TREE='4b825dc642cb6eb9a060e54bf8d69288fbee4904'

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
# --find-copies-harder is ~77x of the walk and cannot be split off (#680). Measured 2026-09-02 over
# this checkout: the fold with the flag 1m40s, without it 1.3s, and the two folds hold the IDENTICAL
# path set -- 6,011 paths each, none present in one and absent in the other. So there is no "the
# cheap walk did not answer" bucket to route to the expensive one; the cheap walk answers for every
# path and simply answers 2,514 of them differently, 24 of those being .cs files whose committed
# headers agree with the flag. The flag is load-bearing, and a cheap-first split is refuted rather
# than untried.
#
# What IS free is narrowing the walk's pathspec, and that is what the flag's cost turned out to be
# most sensitive to. Measured over the 2,186 tracked .cs files in this checkout, git 2.51.1, each
# variant against the same history:
#
#   walk variant                              worktree on the 9p mount   worktree on a native FS
#   --find-renames                                               1.09s                     0.20s
#   --find-renames -C                                           15.30s                     0.97s
#   --find-renames --find-copies-harder                         92.45s                    11.70s
#   the same, narrowed by `-- '*.cs'`                            40.05s                     3.45s
#
# End to end with a cold .git/license-year-cache: audit-license-years.sh --summary 1m42.2s -> 37.5s,
# update-license-headers.sh --dry-run 1m44.4s -> 37.7s. The folded path -> year map is BYTE-IDENTICAL
# with and without the pathspec over all 2,186 paths, and no rename or copy record in this history
# has ever paired a .cs path with a non-.cs one. That last property is the load-bearing one: the
# pathspec restricts the CANDIDATE SOURCE set the copy detection searches, not only its output, so a
# .cs file produced by renaming or copying a non-.cs path would silently resolve to a later year.
# test-license-year-copy-detection.sh asserts it with the same detection flags this walk uses.
# Both callers ask only about .cs files; any other path still falls through to the per-file query.
#
# The cost is also not where #680 guessed. Pointing --git-dir at this checkout's bind-mounted .git
# while putting the WORKTREE on a native filesystem runs the identical walk in 11.5s, and copying
# .git to tmpfs while leaving the worktree on the mount changed nothing -- so the packfiles are not
# the problem, per-path worktree access from the copy detection is.
#
# That access has a name, and _LICENSE_YEAR_ATTRIBUTES_TREE removes it: every one of those per-path
# reads is a `.gitattributes` lookup that cannot change the walk's answer. What is left after it is
# the copy detection's real arithmetic, and that is the same on both filesystems. Measured
# 2026-09-02 over the 2,190 tracked .cs files here, the narrowed walk:
#
#   worktree                            in-tree .gitattributes   attr.tree=<the empty tree>
#   9p bind mount from the host                         33.52s                        3.77s
#   native filesystem (a tmpfs clone)                    3.57s                        3.56s
#
# So it is worth 8.9x here and NOTHING on a native filesystem, where the lookups are cache hits.
# The saving is the container's, not CI's, and #680 rightly asks for a CI number before optimizing:
# Contract Suites runs test-license-year-copy-detection, which primes this walk, in 36.3s on
# ubuntu-latest (run 33683074776). The change is taken because it costs nothing to take, not
# because CI needed it -- the folded path -> year map is byte-identical both ways over all 2,190
# .cs paths and, with the pathspec removed, over all 6,040 tracked ones.
#
# Only the walk gets this. _license_year_load_staged_renames compares the index against HEAD, which
# is where `text`/`eol` DO mean something, and it costs about a second; a saving that small does not
# buy a change in what git considers staged. The native column is a tmpfs stand-in for a CI runner's
# local disk, NOT a measurement on one.
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
        git -C "$LICENSE_YEAR_REPO_ROOT" \
            -c "attr.tree=$_LICENSE_YEAR_ATTRIBUTES_TREE" \
            -c diff.renameLimit=999999 log \
            --reverse \
            --name-status \
            --diff-filter=ACRD \
            --format='YEAR:%ad' \
            --date=format:%Y \
            --find-renames \
            --find-copies-harder \
            -- "$_LICENSE_YEAR_PRIME_PATHSPEC"
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

# Direct execution: resolve a batch of paths through the SAME resolver the shell callers source.
#
# This exists so that PowerShell has somewhere to ask. scripts/agent-preflight.ps1 used to run its
# own `git log --follow --diff-filter=A` per changed file, which is a third resolver in a language
# the shell contract test could not see, and it disagreed with this library for every path whose
# year only `--find-copies-harder` recovers (#681). One fork per preflight run replaces it.
#
# Reads NUL-separated repository-relative paths on stdin and writes one NUL-separated record per
# path, `path<TAB>year<TAB>source`. NUL rather than newline because a path may legally contain one.
#
# Usage: bash scripts/license-year-lib.sh --repo-root <dir> [--start-year Y] [--current-year Y] [--prime]
#
# --prime runs the repository-wide walk first. Callers resolving a handful of changed files must NOT
# pass it: the walk answers for every tracked path at a fixed cost, which is a win for a full sweep
# and pure loss for five files.
_license_year_main() {
    local repo_root=""
    local start_year=""
    local current_year=""
    local prime=false

    while [[ $# -gt 0 ]]; do
        case "$1" in
            --repo-root)
                repo_root="${2:-}"
                shift 2
                ;;
            --start-year)
                start_year="${2:-}"
                shift 2
                ;;
            --current-year)
                current_year="${2:-}"
                shift 2
                ;;
            --prime)
                prime=true
                shift
                ;;
            *)
                printf 'license-year-lib: unknown argument %s\n' "$1" >&2
                return 2
                ;;
        esac
    done

    if [[ -z "$repo_root" ]]; then
        printf 'license-year-lib: --repo-root is required\n' >&2
        return 2
    fi

    license_year_init "$repo_root" "$start_year" "$current_year"
    if [[ "$prime" == true ]]; then
        license_year_prime
    fi

    local rel
    while IFS= read -r -d '' rel; do
        [[ -z "$rel" ]] && continue
        license_year_resolve "$rel"
        printf '%s\t%s\t%s\0' "$rel" "$LICENSE_YEAR_RESULT" "$LICENSE_YEAR_SOURCE"
    done
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
    set -euo pipefail
    _license_year_main "$@"
fi
