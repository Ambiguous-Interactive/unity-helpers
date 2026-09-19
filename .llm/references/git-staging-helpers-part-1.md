# git-staging-helpers - Part 1

## Split Content

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
- `IndexLockPath` - Absolute effective index path returned by `git rev-parse --git-path index`, plus `.lock`
- `RepositoryRoot` - Root of the repository

Resolve the effective index each time repository information is requested. `git commit --only`
gives hooks a temporary `GIT_INDEX_FILE` while the parent holds the main index lock. Check the
temporary index's lock; never remove or wait on the parent's lock. The same rule supports alternate
indexes and linked worktrees while retaining contention checks for their actual index writers.
PowerShell resolves that path without requiring the index to exist, so a later `Set-Location`
cannot redirect a captured lock path. Temporary-repository regression suites clear Git's local
environment variables before setup: a hook's inherited `GIT_INDEX_FILE`, `GIT_DIR`, or
`GIT_WORK_TREE` must never make a fixture stage into its caller's repository. Tests that exercise
an alternate index set their own variables after isolation.

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
