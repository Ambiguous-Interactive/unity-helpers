#!/usr/bin/env bash
# audit-license-years.sh - Audit .cs files for mismatched copyright years
#
# Usage:
#   ./scripts/audit-license-years.sh
#   ./scripts/audit-license-years.sh --csv
#   ./scripts/audit-license-years.sh --summary
#   ./scripts/audit-license-years.sh --paths file1.cs file2.cs
#   ./scripts/audit-license-years.sh --summary --paths file1.cs file2.cs
#
# This script compares the copyright year in .cs file headers against the
# git creation year to identify files with mismatched years.
#
# Options:
#   --csv      Output results in CSV format (file,current_year,git_year,match)
#   --summary  Only show summary statistics
#   --no-cache Disable cache reading (forces full git log scan, still writes)
#   --paths    Audit only the listed files (all args after --paths are paths)
#   --help     Show this help message

set -euo pipefail

# Configuration
REPO_START_YEAR=2023
CURRENT_YEAR=$(date +%Y)

# Parse arguments
OUTPUT_MODE="default"
USE_CACHE=true
declare -a PATH_ARGS=()
PATHS_MODE=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --csv)
            OUTPUT_MODE="csv"
            shift
            ;;
        --summary)
            OUTPUT_MODE="summary"
            shift
            ;;
        --no-cache)
            USE_CACHE=false
            shift
            ;;
        --paths)
            PATHS_MODE=true
            shift
            # All remaining args are file paths
            while [[ $# -gt 0 ]]; do
                PATH_ARGS+=("$1")
                shift
            done
            ;;
        --help|-h)
            head -19 "$0" | tail -18
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

# --- Cache setup ---
CACHE_FILE=$(git rev-parse --git-path license-year-cache 2>/dev/null || true)
if [[ -z "$CACHE_FILE" ]]; then
    CACHE_FILE="$REPO_ROOT/.git/license-year-cache"
elif [[ "$CACHE_FILE" != /* ]]; then
    CACHE_FILE="$REPO_ROOT/$CACHE_FILE"
fi
declare -A year_cache=()
cache_dirty=false

# Load cache into associative array
load_cache() {
    if [[ "$USE_CACHE" == true && -f "$CACHE_FILE" ]]; then
        while IFS=$'\t' read -r cached_path cached_year; do
            if [[ -n "$cached_path" && -n "$cached_year" ]]; then
                year_cache["$cached_path"]="$cached_year"
            fi
        done < "$CACHE_FILE"
    fi
}

# Write cache atomically (temp file + mv)
save_cache() {
    if [[ "$cache_dirty" == true ]]; then
        local tmp_cache
        tmp_cache=$(mktemp "$CACHE_FILE.XXXXXX")
        for key in "${!year_cache[@]}"; do
            printf '%s\t%s\n' "$key" "${year_cache[$key]}"
        done | LC_ALL=C sort > "$tmp_cache"
        mv -f "$tmp_cache" "$CACHE_FILE"
    fi
}

# Ensure cache is saved on exit
trap save_cache EXIT

load_cache

# Counters
total_files=0
matched_files=0
mismatched_files=0
missing_header_files=0
no_git_history_files=0

# Arrays for mismatches
declare -a mismatch_list=()

# Extract year from copyright header
get_header_year() {
    local file="$1"
    local first_line
    first_line=$(head -1 -- "$file" 2>/dev/null || echo "")

    # Match pattern: // MIT License - Copyright (c) YYYY ...
    if [[ "$first_line" =~ Copyright\ \(c\)\ ([0-9]{4}) ]]; then
        echo "${BASH_REMATCH[1]}"
    else
        echo ""
    fi
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

# Get git creation year for a file (with cache)
# Sets global _git_year to avoid subshell (cache writes must stay in main shell)
_git_year=""
get_git_creation_year() {
    local rel="$1"

    # Check cache first
    if [[ "$USE_CACHE" == true && -n "${year_cache[$rel]+_}" ]]; then
        _git_year="${year_cache[$rel]}"
        return
    fi

    # Use --follow to track across renames, --diff-filter=A for additions only
    _git_year=$(git log --follow --diff-filter=A --format=%ad --date=format:%Y -- "$rel" 2>/dev/null | tail -1)

    if [[ -n "$_git_year" ]]; then
        # Store in cache
        year_cache["$rel"]="$_git_year"
        cache_dirty=true
    fi
}

# Print CSV header if in CSV mode
if [[ "$OUTPUT_MODE" == "csv" ]]; then
    echo "file,current_year,git_year,status"
fi

# Audit a single file (repo-relative path)
audit_file() {
    local rel_path="$1"
    local file="$REPO_ROOT/$rel_path"
    ((total_files++)) || true

    # Get header year
    header_year=$(get_header_year "$file")

    if [[ -z "$header_year" ]]; then
        ((missing_header_files++)) || true
        if [[ "$OUTPUT_MODE" == "csv" ]]; then
            echo "$rel_path,MISSING,N/A,missing_header"
        elif [[ "$OUTPUT_MODE" == "default" ]]; then
            echo "MISSING HEADER: $rel_path"
        fi
        return
    fi

    # Get git creation year (sets _git_year global, no subshell)
    get_git_creation_year "$rel_path"
    git_year="$_git_year"

    if [[ -z "$git_year" ]]; then
        ((no_git_history_files++)) || true
        # File has no git history (untracked), should use current year
        if [[ "$header_year" == "$CURRENT_YEAR" ]]; then
            ((matched_files++)) || true
            if [[ "$OUTPUT_MODE" == "csv" ]]; then
                echo "$rel_path,$header_year,UNTRACKED,ok"
            fi
        else
            ((mismatched_files++)) || true
            mismatch_list+=("$rel_path: has $header_year, expected $CURRENT_YEAR (untracked)")
            if [[ "$OUTPUT_MODE" == "csv" ]]; then
                echo "$rel_path,$header_year,UNTRACKED,mismatch"
            elif [[ "$OUTPUT_MODE" == "default" ]]; then
                echo "MISMATCH: $rel_path - has $header_year, expected $CURRENT_YEAR (untracked)"
            fi
        fi
        return
    fi

    # Handle files created before repo start year
    if [[ "$git_year" -lt "$REPO_START_YEAR" ]]; then
        git_year="$REPO_START_YEAR"
    fi

    # Compare years
    if [[ "$header_year" == "$git_year" ]]; then
        ((matched_files++)) || true
        if [[ "$OUTPUT_MODE" == "csv" ]]; then
            echo "$rel_path,$header_year,$git_year,ok"
        fi
    else
        ((mismatched_files++)) || true
        mismatch_list+=("$rel_path: has $header_year, expected $git_year")
        if [[ "$OUTPUT_MODE" == "csv" ]]; then
            echo "$rel_path,$header_year,$git_year,mismatch"
        elif [[ "$OUTPUT_MODE" == "default" ]]; then
            echo "MISMATCH: $rel_path - has $header_year, expected $git_year"
        fi
    fi
}

if [[ "$PATHS_MODE" == true ]]; then
    # Incremental mode: audit only specified files
    for p in "${PATH_ARGS[@]}"; do
        rel_path=$(normalize_repo_path "$p" || true)
        if [[ -z "${rel_path:-}" ]]; then
            echo "WARNING: File outside repository skipped: $p" >&2
            continue
        fi

        if [[ -f "$REPO_ROOT/$rel_path" && "$rel_path" == *.cs ]]; then
            audit_file "$rel_path"
        else
            echo "WARNING: File not found: $p" >&2
        fi
    done
else
    # Full scan: only tracked .cs files. Ignored local worktrees must never
    # affect repository validation.
    while IFS= read -r -d '' file; do
        audit_file "$file"
    done < <(git ls-files -z -- '*.cs' | sort -z)
fi

# Print summary
if [[ "$OUTPUT_MODE" != "csv" ]]; then
    echo ""
    echo "=== License Year Audit Summary ==="
    echo "Total .cs files:        $total_files"
    echo "Matched years:          $matched_files"
    echo "Mismatched years:       $mismatched_files"
    echo "Missing headers:        $missing_header_files"
    echo "No git history:         $no_git_history_files"
    echo ""

    if [[ $mismatched_files -gt 0 || $missing_header_files -gt 0 ]]; then
        needs_update=$((mismatched_files + missing_header_files))
        echo "Files needing update: $needs_update"
        exit 1
    else
        echo "All files have correct copyright years!"
        exit 0
    fi
fi

# CSV mode: exit with appropriate code based on results
if [[ $mismatched_files -gt 0 || $missing_header_files -gt 0 ]]; then
    exit 1
fi
exit 0
