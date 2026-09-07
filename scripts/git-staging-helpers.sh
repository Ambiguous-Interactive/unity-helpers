#!/usr/bin/env bash
# git-staging-helpers.sh - Shared helpers for git-aware shell scripts
#
# IMPORTANT: All git index operations (git add, git reset, etc.) MUST use
# the retry helpers from this script to handle index.lock contention properly.
# Never call `git add` directly in scripts - always use git_add_with_retry.
#
# The index.lock file is created by git during any operation that modifies the index.
# When using tools like lazygit or running multiple hooks, concurrent operations can
# cause "fatal: Unable to create '.git/index.lock': File exists" errors.
#
# Usage:
#   source "$(dirname "$0")/git-staging-helpers.sh"
#   ensure_no_index_lock  # Call at start of hook to wait for external tools
#   git_add_with_retry file1.txt file2.txt
#
# Environment variables:
#   GIT_STAGING_VERBOSE=1   - Enable verbose logging for debugging
#   GIT_LOCK_MAX_ATTEMPTS   - Max retry attempts (default: 30)
#   GIT_LOCK_INITIAL_DELAY_MS - Initial backoff delay (default: 50)
#   GIT_LOCK_MAX_DELAY_MS   - Max backoff delay (default: 3000)
#   GIT_LOCK_WAIT_TIMEOUT_MS - Max wait for external lock (default: 30000)
#   GIT_LOCK_POLL_INTERVAL_MS - Lock polling interval (default: 50)
#   GIT_LOCK_INITIAL_WAIT_MS - Initial wait at hook start (default: 10000)
#
# This script is the bash equivalent of git-staging-helpers.ps1 for PowerShell.

set -euo pipefail

# Configuration constants matching PowerShell implementation
readonly GIT_LOCK_MAX_ATTEMPTS="${GIT_LOCK_MAX_ATTEMPTS:-30}"
readonly GIT_LOCK_INITIAL_DELAY_MS="${GIT_LOCK_INITIAL_DELAY_MS:-50}"
readonly GIT_LOCK_MAX_DELAY_MS="${GIT_LOCK_MAX_DELAY_MS:-3000}"
readonly GIT_LOCK_WAIT_TIMEOUT_MS="${GIT_LOCK_WAIT_TIMEOUT_MS:-30000}"
readonly GIT_LOCK_POLL_INTERVAL_MS="${GIT_LOCK_POLL_INTERVAL_MS:-50}"
readonly GIT_LOCK_INITIAL_WAIT_MS="${GIT_LOCK_INITIAL_WAIT_MS:-10000}"
readonly GIT_STAGING_VERBOSE="${GIT_STAGING_VERBOSE:-0}"

# Cross-process lock file path for flock coordination
readonly GIT_HELPERS_LOCK_FILE="${GIT_HELPERS_LOCK_FILE:-/tmp/unity-helpers-git-staging.lock}"

# Track whether we hold the flock
_GIT_FLOCK_HELD=0

# Cache for git directory path (computed once per script run)
_GIT_DIR=""

# Log a message if verbose mode is enabled
# Args:
#   $@ - Message to log
log_verbose() {
    if [[ "$GIT_STAGING_VERBOSE" == "1" ]]; then
        echo "[git-staging] $*" >&2
    fi
}

# Log an error message (always shown)
# Args:
#   $@ - Message to log
log_error() {
    echo "[git-staging] ERROR: $*" >&2
}

# Log a warning message (always shown)
# Args:
#   $@ - Message to log
log_warning() {
    echo "[git-staging] WARNING: $*" >&2
}

# Get the .git directory path (cached)
get_git_dir() {
    if [[ -z "$_GIT_DIR" ]]; then
        _GIT_DIR=$(git rev-parse --git-dir 2>/dev/null) || {
            echo "Error: Not a git repository" >&2
            return 1
        }
    fi
    echo "$_GIT_DIR"
}

# Partial commits give hooks a separate index while Git holds the main index lock.
get_index_lock_path() {
    local index_path
    index_path=$(git rev-parse --git-path index) || return 1
    printf '%s.lock\n' "$index_path"
}

# Check if git is available
assert_git_available() {
    if ! command -v git >/dev/null 2>&1; then
        echo "Error: git not found; cannot inspect or stage files." >&2
        return 1
    fi
    return 0
}

# Get repository root path
get_repository_root() {
    git rev-parse --show-toplevel 2>/dev/null || {
        local git_dir
        git_dir=$(get_git_dir) || return 1
        dirname "$git_dir"
    }
}

# Sleep for a given number of milliseconds
# This handles fractional seconds properly across different platforms
sleep_ms() {
    local ms="$1"
    if [[ $ms -ge 1000 ]]; then
        # For >= 1 second, use awk for precise calculation
        local secs
        secs=$(awk "BEGIN {printf \"%.3f\", $ms/1000}")
        sleep "$secs"
    else
        # For < 1 second, format as 0.XXX
        local padded
        padded=$(printf "%03d" "$ms")
        sleep "0.$padded" 2>/dev/null || sleep 1
    fi
}

# Wait for the git index.lock file to be released
# Args:
#   $1 - max wait time in milliseconds (default: 30000)
#   $2 - poll interval in milliseconds (default: 50)
# Returns:
#   0 if lock was released or never existed
#   1 if lock still exists after timeout
wait_for_git_index_lock() {
    local max_wait_ms="${1:-$GIT_LOCK_WAIT_TIMEOUT_MS}"
    local poll_interval_ms="${2:-$GIT_LOCK_POLL_INTERVAL_MS}"
    local index_lock

    index_lock=$(get_index_lock_path) || return 0

    local elapsed=0
    while [[ -f "$index_lock" ]] && [[ $elapsed -lt $max_wait_ms ]]; do
        sleep_ms "$poll_interval_ms"
        elapsed=$((elapsed + poll_interval_ms))
    done

    # Return success if lock doesn't exist
    [[ ! -f "$index_lock" ]]
}

# Ensure no git index.lock exists before starting hook operations.
# This function should be called at the START of any git hook to wait for
# external tools (like lazygit, GitKraken, IDE integrations) to finish.
#
# When lazygit stages files, it holds index.lock during the staging operation.
# If a hook starts before lazygit releases the lock, git add operations will fail.
# This function waits for that lock to be released before proceeding.
#
# Args:
#   $1 - max wait time in milliseconds (default: GIT_LOCK_INITIAL_WAIT_MS, 10000)
#   $2 - poll interval in milliseconds (default: GIT_LOCK_POLL_INTERVAL_MS, 50)
#
# Returns:
#   0 if lock was released or never existed
#   1 if lock still exists after timeout (hook should likely abort or warn)
#
# Usage:
#   # At the start of your hook:
#   ensure_no_index_lock || {
#       echo "Warning: git index.lock still held by another process" >&2
#       # Optionally exit 1 or continue with caution
#   }
ensure_no_index_lock() {
    local max_wait_ms="${1:-$GIT_LOCK_INITIAL_WAIT_MS}"
    local poll_interval_ms="${2:-$GIT_LOCK_POLL_INTERVAL_MS}"
    local index_lock

    index_lock=$(get_index_lock_path) || return 0

    if [[ ! -f "$index_lock" ]]; then
        log_verbose "No index.lock present, proceeding immediately"
        return 0
    fi

    log_verbose "index.lock exists, waiting up to ${max_wait_ms}ms for external tool to finish..."

    local elapsed=0
    local last_log=0
    while [[ -f "$index_lock" ]] && [[ $elapsed -lt $max_wait_ms ]]; do
        sleep_ms "$poll_interval_ms"
        elapsed=$((elapsed + poll_interval_ms))

        # Log progress every second in verbose mode
        if [[ "$GIT_STAGING_VERBOSE" == "1" ]] && [[ $((elapsed - last_log)) -ge 1000 ]]; then
            log_verbose "Still waiting for index.lock... (${elapsed}ms/${max_wait_ms}ms)"
            last_log=$elapsed
        fi
    done

    if [[ -f "$index_lock" ]]; then
        log_warning "index.lock still exists after waiting ${max_wait_ms}ms"
        log_warning "Another process may be holding the lock. Operations may fail."
        log_warning "Lock file: $index_lock"

        # Try to identify what's holding the lock (for debugging)
        if [[ "$GIT_STAGING_VERBOSE" == "1" ]]; then
            # On some systems, we can try to find the holding process
            if command -v lsof >/dev/null 2>&1; then
                local holder
                holder=$(lsof "$index_lock" 2>/dev/null | tail -n +2 | head -1 || true)
                if [[ -n "$holder" ]]; then
                    log_verbose "Lock holder: $holder"
                fi
            fi
        fi
        return 1
    fi

    log_verbose "index.lock released after ${elapsed}ms, proceeding"
    return 0
}

# Add files to git staging with exponential backoff retry
# This is the primary function for staging files safely.
#
# Args:
#   $@ - Files to stage (passed to git add --)
#
# Returns:
#   0 on success
#   128 on failure after max attempts
#
# Features:
#   - Waits for existing lock before attempting
#   - Exponential backoff with jitter on failure
#   - Up to 30 retry attempts by default
#   - Matches PowerShell implementation exactly
git_add_with_retry() {
    local files=("$@")

    # No files to add
    if [[ ${#files[@]} -eq 0 ]]; then
        log_verbose "git_add_with_retry: no files to add, returning success"
        return 0
    fi

    local index_lock
    index_lock=$(get_index_lock_path) || return 1

    local attempt=1
    local delay_ms=$GIT_LOCK_INITIAL_DELAY_MS

    log_verbose "git_add_with_retry: staging ${#files[@]} file(s)"

    # Try to acquire cross-process flock if available (prevents parallel hook execution)
    local flock_acquired=0
    if command -v flock >/dev/null 2>&1 && [[ -n "$GIT_HELPERS_LOCK_FILE" ]]; then
        # Ensure lock file exists
        touch "$GIT_HELPERS_LOCK_FILE" 2>/dev/null || true
        if [[ -f "$GIT_HELPERS_LOCK_FILE" ]]; then
            # Open file descriptor for flock
            exec 200>"$GIT_HELPERS_LOCK_FILE"
            if flock -w 10 200 2>/dev/null; then
                flock_acquired=1
                log_verbose "Acquired cross-process flock"
            else
                log_verbose "Could not acquire flock (timeout or unavailable), proceeding without"
            fi
        fi
    fi

    # Cleanup function to release flock.
    # IMPORTANT: avoid "exec ... 2>/dev/null" on the current shell, which can
    # permanently redirect stderr for the caller and hide downstream errors.
    cleanup_flock() {
        if [[ $flock_acquired -eq 1 ]]; then
            { exec 200>&-; } 2>/dev/null || true
            flock_acquired=0
            log_verbose "Released cross-process flock"
        fi
        return 0
    }

    while [[ $attempt -le $GIT_LOCK_MAX_ATTEMPTS ]]; do
        # Wait for any existing lock to be released before attempting
        if [[ -f "$index_lock" ]]; then
            log_verbose "index.lock exists before attempt $attempt, waiting..."
            if ! wait_for_git_index_lock 5000 50; then
                log_warning "index.lock still exists after waiting; attempting git add anyway (attempt $attempt/$GIT_LOCK_MAX_ATTEMPTS)"
            else
                log_verbose "index.lock released, proceeding with attempt $attempt"
            fi
        fi

        # Attempt to add files - capture both stdout and stderr for debugging
        local git_stderr_file
        git_stderr_file=$(mktemp 2>/dev/null || echo "/tmp/git_add_stderr_$$")

        if git add -- "${files[@]}" 2>"$git_stderr_file"; then
            log_verbose "git add succeeded on attempt $attempt"
            rm -f "$git_stderr_file" 2>/dev/null || true
            cleanup_flock
            return 0
        fi

        local exit_code=$?
        local git_stderr
        git_stderr=$(cat "$git_stderr_file" 2>/dev/null || true)
        rm -f "$git_stderr_file" 2>/dev/null || true

        log_verbose "git add failed with exit code $exit_code on attempt $attempt"
        if [[ -n "$git_stderr" ]]; then
            log_verbose "git stderr: $git_stderr"
        fi

        # Only retry on exit code 128 (lock contention) or if stderr mentions index.lock
        local should_retry=0
        if [[ $exit_code -eq 128 ]]; then
            should_retry=1
        elif [[ "$git_stderr" == *"index.lock"* ]] || [[ "$git_stderr" == *"Unable to create"* ]]; then
            should_retry=1
        fi

        if [[ $should_retry -eq 1 ]] && [[ $attempt -lt $GIT_LOCK_MAX_ATTEMPTS ]]; then
            # Exponential backoff: multiply by 1.4 each attempt, cap at max delay
            # Using integer math: delay = delay * 14 / 10
            delay_ms=$((delay_ms * 14 / 10))
            [[ $delay_ms -gt $GIT_LOCK_MAX_DELAY_MS ]] && delay_ms=$GIT_LOCK_MAX_DELAY_MS

            # Add jitter (0-40% of delay) to prevent thundering herd
            local max_jitter=$((delay_ms * 4 / 10))
            local jitter=$((RANDOM % (max_jitter + 1)))
            local total_delay_ms=$((delay_ms + jitter))

            log_warning "git add failed (exit code $exit_code), retrying in ${total_delay_ms}ms (attempt $attempt/$GIT_LOCK_MAX_ATTEMPTS)..."

            sleep_ms "$total_delay_ms"
            ((attempt++))
            continue
        fi

        # Non-retryable failure - show the actual error
        if [[ -n "$git_stderr" ]]; then
            log_error "git add failed with non-retryable error: $git_stderr"
        fi
        cleanup_flock
        return $exit_code
    done

    # Exhausted all attempts
    log_error "git add failed after $GIT_LOCK_MAX_ATTEMPTS attempts"
    cleanup_flock
    return 128
}

# Convenience function to add a single file with retry
# Args:
#   $1 - File path to stage
# Returns:
#   0 on success, non-zero on failure
git_add_single_file() {
    local file="$1"
    git_add_with_retry "$file"
}

# Get staged file paths matching glob patterns
# Args:
#   $@ - Glob patterns (e.g., "*.cs" "*.md")
# Output:
#   Newline-separated list of staged file paths
get_staged_paths_for_globs() {
    local globs=("$@")

    if [[ ${#globs[@]} -eq 0 ]]; then
        return 0
    fi

    git diff --cached --name-only --diff-filter=ACM -- "${globs[@]}" 2>/dev/null || true
}

# Filter a list of paths to only those that exist on disk
# Input: Newline-separated paths on stdin
# Output: Newline-separated existing paths
get_existing_paths() {
    while IFS= read -r path; do
        [[ -n "$path" ]] && [[ -e "$path" ]] && echo "$path"
    done
}

# Batch add files from a newline-separated list
# This is useful for piping from get_staged_paths_for_globs
# Input: Newline-separated paths on stdin
# Returns:
#   0 on success
#   Non-zero on failure
git_add_from_stdin_with_retry() {
    local files=()
    while IFS= read -r file; do
        [[ -n "$file" ]] && files+=("$file")
    done

    if [[ ${#files[@]} -gt 0 ]]; then
        git_add_with_retry "${files[@]}"
    fi
}

# Lock acquisition helper using flock (if available)
# This provides process-level coordination similar to PowerShell's Mutex
# Args:
#   $1 - Lock file path
#   $2 - Timeout in seconds
# Returns:
#   0 if lock acquired, 1 if timeout or unavailable
acquire_git_lock() {
    local lock_file="${1:-/tmp/unity-helpers-git.lock}"
    local timeout="${2:-10}"

    # Check if flock is available
    if ! command -v flock >/dev/null 2>&1; then
        # flock not available, fall back to simple file-based locking
        return 0
    fi

    # Create lock file if it doesn't exist
    touch "$lock_file" 2>/dev/null || return 0

    # Try to acquire exclusive lock with timeout
    exec 200>"$lock_file"
    if flock -w "$timeout" 200 2>/dev/null; then
        return 0
    else
        return 1
    fi
}

# Release the git lock
release_git_lock() {
    # Release is automatic when the file descriptor is closed
    # or when the script exits
    { exec 200>&-; } 2>/dev/null || true
    return 0
}

# Example usage (uncomment to test):
# source "$(dirname "$0")/git-staging-helpers.sh"
# assert_git_available || exit 1
# git_add_with_retry file1.txt file2.txt
