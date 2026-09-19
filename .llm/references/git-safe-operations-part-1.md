# git-safe-operations - Part 1

## Split Content

## Purpose

Ensure all scripts and hooks that interact with the git index use proper locking,
retries, and coordination to prevent `index.lock` errors during concurrent operations.

## When to Use

- Creating or modifying any script that runs `git add`, `git reset`, or similar index commands
- Writing pre-commit, post-commit, or other git hooks
- Building automation that stages files after modification
- Any code that modifies files and re-stages them (formatters, linters with auto-fix)

---

## Background: The `index.lock` Problem

Git creates an `index.lock` file in `.git/` during any operation that modifies the index
(staging area). If a second git operation tries to run while the lock exists, it fails:

```text
fatal: Unable to create '/path/to/repo/.git/index.lock': File exists.
```

This commonly happens when:

1. **Multiple hooks run concurrently** - even with `require_serial: true` in pre-commit
2. **Interactive git tools (lazygit, GitKraken)** perform rapid operations
3. **Auto-fix scripts** call `git add` multiple times in quick succession
4. **File watchers** trigger git operations on save
5. **Pre-commit hooks start before external tools finish** - lazygit may still hold the lock when hooks begin

---

## Critical Rules

### 1. Never Parallel Git Operations

Git's index file (`/.git/index`) doesn't support concurrent access. Running multiple git commands in parallel can corrupt the index.

```bash
# BAD: Parallel git operations
git status & git diff &

# GOOD: Sequential operations
git status
git diff
```

### 2. Use Porcelain Output for Parsing

Use `--porcelain` flags when parsing git output programmatically. Human-readable output changes between git versions.

```bash
# BAD: Parsing human-readable output (also: never use grep, use rg)
git status | grep "modified"

# GOOD: Porcelain format (machine-parseable)
git status --porcelain
git diff --name-status
```

### 3. Lock-Aware Operations

For long-running scripts that may conflict with IDE git integrations:

```bash
# Wait for index lock
while [ -f ".git/index.lock" ]; do
  sleep 0.1
done
```

### 4. Wait for External Tools at Hook Start

**CRITICAL**: At the start of any git hook, wait for external tools (lazygit, IDE, etc.) to release the index.lock before performing any operations:

```bash
# Bash: At the start of your hook
source "$SCRIPT_DIR/scripts/git-staging-helpers.sh"
ensure_no_index_lock || {
    echo "Warning: index.lock still held. Proceeding anyway, but operations may fail." >&2
}
```

```powershell
# PowerShell: At the start of your script
. $PSScriptRoot/git-staging-helpers.ps1
if (-not (Invoke-EnsureNoIndexLock)) {
    Write-Warning "index.lock still held after waiting. Proceeding anyway."
}
```

### 5. Agent-Specific: Publish Completed Work Safely

AI agents may stage, commit, and push completed work when publication is part of the task. Use the
staging retry helpers below, inspect the exact staged diff before committing, and keep commits
focused. Do not rewrite history, discard user changes, force-push, reset, merge, rebase, or stash
without explicit authorization.

Read-only commands such as `git status`, `git log`, `git diff`, `git show`, `git blame`, and
`git ls-files` remain safe for routine inspection.

---

## Quick Start: Using the Staging Helpers

### PowerShell

```powershell
# 1. Load helpers
$helpersPath = Join-Path -Path $PSScriptRoot -ChildPath 'git-staging-helpers.ps1'
. $helpersPath

# 2. Get repository info
Assert-GitAvailable | Out-Null
$repositoryInfo = Get-GitRepositoryInfo

# 3. Wait for external tools
Invoke-EnsureNoIndexLock | Out-Null

# 4. Stage files with retry
Invoke-GitAddWithRetry -Items $filePaths -IndexLockPath $repositoryInfo.IndexLockPath
```

### Bash

```bash
# 1. Source helpers
source "$SCRIPT_DIR/git-staging-helpers.sh"

# 2. Wait for external tools
ensure_no_index_lock

# 3. Stage files with retry
git_add_with_retry file1.txt file2.txt
```

For full function reference, see [git-staging-helpers](../skills/git-staging-helpers.md).

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
