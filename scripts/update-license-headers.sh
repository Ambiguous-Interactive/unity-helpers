#!/usr/bin/env bash
# update-license-headers.sh - Update MIT license headers in .cs files
#
# Usage:
#   ./scripts/update-license-headers.sh [options]
#
# This script updates all .cs files to use consistent license headers:
#   - Replaces "Eli Pinkerton" with "wallstop"
#   - Corrects copyright years based on git creation date
#   - Adds standard two-line header to files missing it
#
# Options:
#   --dry-run    Show what would be changed without modifying files
#   --verbose    Show all files processed, not just changes
#   --paths      Update only the listed .cs files (all args after --paths)
#   --help       Show this help message
#
# Standard header format:
#   // MIT License - Copyright (c) <year> wallstop
#   // Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

set -euo pipefail

# Configuration
REPO_START_YEAR=2023
CURRENT_YEAR=$(date +%Y)
COPYRIGHT_HOLDER="wallstop"
LICENSE_URL="https://github.com/wallstop/unity-helpers/blob/main/LICENSE"

# Parse arguments
DRY_RUN=false
VERBOSE=false
PATHS_MODE=false
declare -a PATH_ARGS=()

while [[ $# -gt 0 ]]; do
    case "$1" in
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        --verbose)
            VERBOSE=true
            shift
            ;;
        --paths)
            PATHS_MODE=true
            shift
            while [[ $# -gt 0 ]]; do
                PATH_ARGS+=("$1")
                shift
            done
            ;;
        --help|-h)
            head -22 "$0" | tail -20
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            exit 1
            ;;
    esac
done

# Get script directory and repo root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$REPO_ROOT"

# Copyright-year resolution lives in one sourced library so this fixer and
# scripts/audit-license-years.sh cannot answer the same question two different ways (#668).
# shellcheck source=scripts/license-year-lib.sh
source "$SCRIPT_DIR/license-year-lib.sh"
license_year_init "$REPO_ROOT" "$REPO_START_YEAR" "$CURRENT_YEAR"

# Counters
total_files=0
updated_files=0
added_header_files=0
skipped_files=0

# First two lines of the file under inspection, read with the builtin instead of a `head` and a
# `sed` per predicate. Four forks per file over 2,163 tracked files was ~30s of a full run, which
# only became visible once the per-file `git log` was gone (#674). The audit's own header reader
# made the same trade already.
HEADER_LINE_1=""
HEADER_LINE_2=""
read_header_lines() {
    local file="$1"
    HEADER_LINE_1=""
    HEADER_LINE_2=""
    {
        IFS= read -r HEADER_LINE_1 || true
        IFS= read -r HEADER_LINE_2 || true
    } < "$file" 2>/dev/null || true
}

# Check if the file has a standard MIT header (first line contains MIT License)
has_mit_header() {
    [[ "$HEADER_LINE_1" == *"MIT License"* ]]
}

# Check if the file has the license URL line
has_license_url() {
    [[ "$HEADER_LINE_2" == *"Full license text:"* ]]
}

normalize_repo_path() {
    local path="$1"
    local rel

    path="${path//\\//}"
    if [[ "$path" =~ ^[A-Za-z]:/ ]]; then
        if command -v cygpath >/dev/null 2>&1; then
            path=$(cygpath -u "$path")
        else
            return 1
        fi
    fi

    if [[ "$path" = /* ]]; then
        case "$path" in
            "$REPO_ROOT"/*)
                rel="${path#"$REPO_ROOT"/}"
                ;;
            *)
                return 1
                ;;
        esac
    else
        rel="$path"
    fi

    rel="${rel#./}"
    printf '%s\n' "$rel"
}

# Extract the current year from the header lines read by read_header_lines
get_header_year() {
    if [[ "$HEADER_LINE_1" =~ Copyright\ \(c\)\ ([0-9]{4}) ]]; then
        echo "${BASH_REMATCH[1]}"
    else
        echo ""
    fi
}

# Update a file with correct header
update_file() {
    local file="$1"
    local rel_path="$2"
    local target_year="$3"

    local header_line1="// MIT License - Copyright (c) $target_year $COPYRIGHT_HOLDER"
    local header_line2="// Full license text: $LICENSE_URL"

    read_header_lines "$file"

    if has_mit_header; then
        # File has MIT header - update it
        local current_year
        current_year=$(get_header_year)
        local first_line="$HEADER_LINE_1"

        local needs_update=false
        local changes=""

        # Check if we need to update the first line
        if [[ "$first_line" == *"Eli Pinkerton"* ]] || [[ "$current_year" != "$target_year" ]]; then
            needs_update=true
            if [[ "$first_line" == *"Eli Pinkerton"* ]]; then
                changes="${changes}holder "
            fi
            if [[ "$current_year" != "$target_year" ]]; then
                changes="${changes}year($current_year->$target_year) "
            fi
        fi

        # Check if we need to add the second line
        if ! has_license_url; then
            needs_update=true
            changes="${changes}add_url "
        fi

        if [[ "$needs_update" == true ]]; then
            ((updated_files++)) || true
            echo "UPDATE: $rel_path [${changes% }]"

            if [[ "$DRY_RUN" == false ]]; then
                local temp_file
                temp_file=$(mktemp)

                # Write new header
                echo "$header_line1" > "$temp_file"

                # Check if second line exists and is license URL
                if has_license_url; then
                    # Keep existing second line format (update if needed)
                    echo "$header_line2" >> "$temp_file"
                    # Skip first two lines of original
                    tail -n +3 "$file" >> "$temp_file"
                else
                    # Add license URL line
                    echo "$header_line2" >> "$temp_file"
                    # Skip only first line of original
                    tail -n +2 "$file" >> "$temp_file"
                fi

                mv "$temp_file" "$file"
            fi
        elif [[ "$VERBOSE" == true ]]; then
            echo "OK: $rel_path"
        fi
    else
        # File lacks MIT header - add it
        ((added_header_files++)) || true
        echo "ADD HEADER: $rel_path [year=$target_year]"

        if [[ "$DRY_RUN" == false ]]; then
            local temp_file
            temp_file=$(mktemp)

            echo "$header_line1" > "$temp_file"
            echo "$header_line2" >> "$temp_file"
            echo "" >> "$temp_file"
            cat "$file" >> "$temp_file"

            mv "$temp_file" "$file"
        fi
    fi
}

# Print mode
if [[ "$DRY_RUN" == true ]]; then
    echo "=== DRY RUN MODE - No files will be modified ==="
    echo ""
fi

echo "Updating license headers..."
echo "Copyright holder: $COPYRIGHT_HOLDER"
echo "License URL: $LICENSE_URL"
echo ""

process_relative_path() {
    local rel_path="$1"
    local file="$REPO_ROOT/$rel_path"

    if [[ ! -f "$file" || "$rel_path" != *.cs ]]; then
        return
    fi

    ((total_files++)) || true

    # Determine target year. Called without a subshell so the resolver's staged-rename map is
    # built once for the whole run rather than once per file.
    license_year_resolve "$rel_path"
    target_year="$LICENSE_YEAR_RESULT"

    # Update the file
    update_file "$file" "$rel_path" "$target_year"
}

if [[ "$PATHS_MODE" == true ]]; then
    for path in "${PATH_ARGS[@]}"; do
        rel_path=$(normalize_repo_path "$path" || true)
        if [[ -z "${rel_path:-}" ]]; then
            echo "WARNING: File outside repository skipped: $path" >&2
            continue
        fi
        process_relative_path "$rel_path"
    done
else
    # Full mode updates only tracked .cs files. Ignored local worktrees and
    # generated directories must never be mutated by a repository fixer.
    #
    # One repository-wide history walk instead of one `git log --follow` per file, which was
    # ~95% of a ten-minute full run (#674). The --paths branch above deliberately does not
    # prime: a changed-file set is a handful of paths, and the walk costs more than they do.
    #
    # Nothing here reads or writes .git/license-year-cache. That cache is the audit's, it is
    # keyed by path, and it outlives the index -- #668 concluded a staged-rename answer must
    # never be written there, and a second writer is one more chance to write one. Priming in
    # memory costs one walk per run and leaves the fixer read-only with respect to that state.
    license_year_prime

    while IFS= read -r -d '' file; do
        process_relative_path "$file"
    done < <(git ls-files -z -- '*.cs' | sort -z)
fi

# Print summary
echo ""
echo "=== License Header Update Summary ==="
echo "Total .cs files:     $total_files"
echo "Updated headers:     $updated_files"
echo "Added headers:       $added_header_files"
echo "Unchanged:           $((total_files - updated_files - added_header_files))"

if [[ "$DRY_RUN" == true ]]; then
    echo ""
    echo "This was a dry run. Run without --dry-run to apply changes."
fi
