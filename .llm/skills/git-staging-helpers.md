# Skill: Git Staging Helpers Reference

<!-- trigger: git staging, git add, index.lock, retry, staging helpers | PowerShell/Bash helpers for safe git staging | Core -->

## Purpose

Reference documentation for the shared git staging helper functions in PowerShell and Bash.
These helpers provide lock-aware, retry-capable staging operations to prevent `index.lock` errors.

## When to Use This Skill

- Writing scripts that need to stage files with `git add`
- Creating pre-commit hooks that modify and re-stage files
- Building automation that needs to interact with the git index
- Troubleshooting `index.lock` errors in scripts or hooks
- Understanding the retry algorithm and environment variable configuration

---

## PowerShell Helper Functions

**All PowerShell scripts that interact with git MUST use the shared helpers.**

### Loading the Helpers

```powershell
$helpersPath = Join-Path -Path $PSScriptRoot -ChildPath 'git-staging-helpers.ps1'
. $helpersPath
```

### Getting Repository Info

```powershell
try {
    Assert-GitAvailable | Out-Null
    $repositoryInfo = Get-GitRepositoryInfo
} catch {
    Write-Error $_.Exception.Message
    exit 1
}
```

### Staging Files with Retry

```powershell
# Single file
Invoke-GitAddWithRetry -Items @($filePath) -IndexLockPath $repositoryInfo.IndexLockPath

# Multiple files
Invoke-GitAddWithRetry -Items $filePaths -IndexLockPath $repositoryInfo.IndexLockPath

# Quiet mode (suppress warnings in loops)
Invoke-GitAddWithRetry -Items @($filePath) -IndexLockPath $repositoryInfo.IndexLockPath -Quiet
```

---

## PowerShell Functions Reference

### `Assert-GitAvailable`

Verifies git is on PATH. Throws if not found.

### `Get-GitRepositoryInfo`

Returns an object with:

- `Directory` - Path to `.git` directory
- `IndexLockPath` - Full path to `.git/index.lock`
- `RepositoryRoot` - Root of the repository

### `Wait-ForGitIndexLock`

Polls until `index.lock` is released or timeout (default 30s).

```powershell
Wait-ForGitIndexLock -IndexLockPath $repositoryInfo.IndexLockPath -MaxWaitMilliseconds 5000
```

### `Invoke-EnsureNoIndexLock`

**Call at the start of hooks/scripts.** Waits for external tools (lazygit, IDE) to release the lock before proceeding.

```powershell
# At the start of your script, after loading helpers
if (-not (Invoke-EnsureNoIndexLock)) {
    Write-Warning "index.lock still held after waiting."
}
```

Parameters:

| Parameter                  | Type | Default | Description       |
| -------------------------- | ---- | ------- | ----------------- |
| `MaxWaitMilliseconds`      | int  | 10000   | Maximum wait time |
| `PollIntervalMilliseconds` | int  | 50      | Poll frequency    |

### `Invoke-GitAddWithRetry`

**Primary function for staging files.** Features:

- Acquires a global mutex to coordinate across processes
- Waits for existing lock before attempting
- Exponential backoff with jitter on failure
- Up to 30 retry attempts by default

Parameters:

| Parameter                  | Type     | Default | Description                    |
| -------------------------- | -------- | ------- | ------------------------------ |
| `Items`                    | string[] | -       | Files to stage                 |
| `IndexLockPath`            | string   | -       | Path to `.git/index.lock`      |
| `MaxAttempts`              | int      | 30      | Maximum retry attempts         |
| `InitialDelayMilliseconds` | int      | 50      | Starting delay between retries |
| `MaxDelayMilliseconds`     | int      | 3000    | Maximum delay cap              |
| `Quiet`                    | switch   | false   | Suppress warning messages      |

### `Invoke-GitAddSingleFile`

Convenience wrapper for staging a single file quietly:

```powershell
Invoke-GitAddSingleFile -FilePath $path -IndexLockPath $repositoryInfo.IndexLockPath
```

### `Get-StagedPathsForGlobs`

Get staged files matching glob patterns:

```powershell
$paths = Get-StagedPathsForGlobs -Globs @('*.cs', '*.md')
```

### `Get-ExistingPaths`

Filter a list to only paths that exist on disk:

```powershell
$existing = Get-ExistingPaths -Candidates $paths
```

---

## Bash Helper Functions

**All bash scripts that interact with git MUST use the shared helpers.**

### Sourcing the Helpers

```bash
#!/usr/bin/env bash
set -e

# Source the shared git helpers
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
source "$SCRIPT_DIR/git-staging-helpers.sh"
```

### Staging Files with Retry

```bash
# Single file
git_add_with_retry "path/to/file.cs"

# Multiple files
git_add_with_retry file1.txt file2.txt file3.md

# From a list (word-split)
FILES="file1.txt file2.txt"
git_add_with_retry $FILES
```

---

## Bash Functions Reference

| Function                     | Description                                                       |
| ---------------------------- | ----------------------------------------------------------------- |
| `assert_git_available`       | Verify git is on PATH                                             |
| `get_git_dir`                | Get `.git` directory path (cached)                                |
| `get_index_lock_path`        | Get path to `.git/index.lock`                                     |
| `get_repository_root`        | Get repository root path                                          |
| `wait_for_git_index_lock`    | Poll until lock released (default 30s)                            |
| `ensure_no_index_lock`       | **Call at hook start** - waits for external tools to release lock |
| `git_add_with_retry`         | **Primary staging function** with exponential backoff and flock   |
| `git_add_single_file`        | Convenience wrapper for single file                               |
| `get_staged_paths_for_globs` | Get staged files matching patterns                                |
| `get_existing_paths`         | Filter stdin to existing paths                                    |

### `ensure_no_index_lock` (Bash)

**Call at the start of hooks/scripts.** Waits for external tools (lazygit, IDE) to release the lock.

```bash
# At the start of your hook, after sourcing helpers
ensure_no_index_lock || {
    echo "Warning: index.lock still held after waiting." >&2
}
```

Arguments:

| Argument | Default | Description            |
| -------- | ------- | ---------------------- |
| `$1`     | 10000   | Maximum wait time (ms) |
| `$2`     | 50      | Poll interval (ms)     |

---

## Retry Algorithm

Both PowerShell and Bash implementations use identical retry parameters:

- **Max attempts**: 30
- **Initial delay**: 50ms
- **Max delay cap**: 3000ms
- **Backoff multiplier**: 1.4x per attempt
- **Jitter**: 0-40% of base delay
- **Lock wait**: 5 seconds before each attempt
- **Initial wait** (at hook start): 10 seconds (via `ensure_no_index_lock`)
- **Cross-process coordination**: Named mutex (PowerShell) / flock (Bash)

---

## Environment Variables

Configure the git staging helpers via environment variables:

| Variable                    | Default                               | Description                                |
| --------------------------- | ------------------------------------- | ------------------------------------------ |
| `GIT_STAGING_VERBOSE`       | `0`                                   | Set to `1` to enable verbose debug logging |
| `GIT_LOCK_MAX_ATTEMPTS`     | `30`                                  | Maximum retry attempts                     |
| `GIT_LOCK_INITIAL_DELAY_MS` | `50`                                  | Initial backoff delay (ms)                 |
| `GIT_LOCK_MAX_DELAY_MS`     | `3000`                                | Maximum backoff delay cap (ms)             |
| `GIT_LOCK_WAIT_TIMEOUT_MS`  | `30000`                               | Max wait for lock per attempt (ms)         |
| `GIT_LOCK_POLL_INTERVAL_MS` | `50`                                  | Lock polling frequency (ms)                |
| `GIT_LOCK_INITIAL_WAIT_MS`  | `10000`                               | Initial wait at hook start (ms)            |
| `GIT_HELPERS_LOCK_FILE`     | `/tmp/unity-helpers-git-staging.lock` | Bash flock file path                       |

### Enabling Verbose Debug Logging

When troubleshooting index.lock issues, enable verbose logging:

```bash
# Bash
export GIT_STAGING_VERBOSE=1
git commit -m "test"
```

```powershell
# PowerShell
$env:GIT_STAGING_VERBOSE = "1"
git commit -m "test"
```

Verbose output includes:

- Lock presence checks
- Wait times and progress
- Retry attempts and delays
- Mutex/flock acquisition status
- Actual git stderr on failures

---

## Forbidden Patterns

### Never Use Raw `git add`

```powershell
# BAD - will fail under concurrent access
git add $filePath
& git add -- $files
```

### Never Ignore Exit Codes

```powershell
# BAD - hides failures
git add $file 2>$null
```

### Never Use `git add` in a Loop Without Batching

```powershell
# BAD - many separate git operations
foreach ($file in $files) {
    git add $file  # Race condition on each iteration
}
```

### Correct Pattern

```powershell
# GOOD - single batched operation with retries
Invoke-GitAddWithRetry -Items $files -IndexLockPath $repositoryInfo.IndexLockPath
```

---

## Checklists

### PowerShell Scripts

1. [ ] Load `git-staging-helpers.ps1` at the top
2. [ ] Call `Assert-GitAvailable` and `Get-GitRepositoryInfo`
3. [ ] **Call `Invoke-EnsureNoIndexLock` immediately after getting repository info**
4. [ ] Use `Invoke-GitAddWithRetry` for ALL staging operations
5. [ ] Batch files together in a single call when possible

### Bash Scripts

1. [ ] Source `git-staging-helpers.sh` at the top
2. [ ] **Call `ensure_no_index_lock` at the start (before any git operations)**
3. [ ] Use `git_add_with_retry` for ALL staging operations
4. [ ] Batch files together in a single call when possible
5. [ ] Test with lazygit to verify no `index.lock` errors
6. [ ] Set `GIT_STAGING_VERBOSE=1` during testing to verify lock handling

---

## Related Skills

- [git-safe-operations](./git-safe-operations.md) - Core git safety patterns and critical rules
- [git-hook-patterns](./git-hook-patterns.md) - Pre-commit hook safety and configuration
- [validate-before-commit](./validate-before-commit.md) - Pre-commit validation commands

## Related Files

- [scripts/git-staging-helpers.ps1](../../scripts/git-staging-helpers.ps1) - PowerShell shared helper module
- [scripts/git-staging-helpers.sh](../../scripts/git-staging-helpers.sh) - Bash shared helper module
