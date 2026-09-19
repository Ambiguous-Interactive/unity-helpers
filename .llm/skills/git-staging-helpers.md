# Skill: Git Staging Helpers Reference

<!-- trigger: git staging, git add, index.lock, retry, staging helpers | PowerShell/Bash helpers for safe git staging | Core -->

## Reference Parts

- [Part 1](../references/git-staging-helpers-part-1.md)
- [Part 2](../references/git-staging-helpers-part-2.md)

## Purpose

[Read section](../references/git-staging-helpers-part-1.md#purpose)

## When to Use This Skill

[Read section](../references/git-staging-helpers-part-1.md#when-to-use-this-skill)

## PowerShell Helper Functions

[Read section](../references/git-staging-helpers-part-1.md#powershell-helper-functions)

### [Loading the Helpers](../references/git-staging-helpers-part-1.md#loading-the-helpers)

### [Getting Repository Info](../references/git-staging-helpers-part-1.md#getting-repository-info)

### [Staging Files with Retry](../references/git-staging-helpers-part-1.md#staging-files-with-retry)

## PowerShell Functions Reference

[Read section](../references/git-staging-helpers-part-1.md#powershell-functions-reference)

### [`Assert-GitAvailable`](../references/git-staging-helpers-part-1.md#assert-gitavailable)

### [`Get-GitRepositoryInfo`](../references/git-staging-helpers-part-1.md#get-gitrepositoryinfo)

### [`Wait-ForGitIndexLock`](../references/git-staging-helpers-part-1.md#wait-forgitindexlock)

### [`Invoke-EnsureNoIndexLock`](../references/git-staging-helpers-part-1.md#invoke-ensurenoindexlock)

### [`Invoke-GitAddWithRetry`](../references/git-staging-helpers-part-1.md#invoke-gitaddwithretry)

### [`Invoke-GitAddSingleFile`](../references/git-staging-helpers-part-1.md#invoke-gitaddsinglefile)

### [`Get-StagedPathsForGlobs`](../references/git-staging-helpers-part-1.md#get-stagedpathsforglobs)

### [`Get-ExistingPaths`](../references/git-staging-helpers-part-1.md#get-existingpaths)

## Bash Helper Functions

[Read section](../references/git-staging-helpers-part-1.md#bash-helper-functions)

### [Sourcing the Helpers](../references/git-staging-helpers-part-1.md#sourcing-the-helpers)

### [Staging Files with Retry](../references/git-staging-helpers-part-1.md#staging-files-with-retry-1)

## Bash Functions Reference

[Read section](../references/git-staging-helpers-part-2.md#bash-functions-reference)

### [`ensure_no_index_lock` (Bash)](../references/git-staging-helpers-part-2.md#ensure_no_index_lock-bash)

## Retry Algorithm

[Read section](../references/git-staging-helpers-part-2.md#retry-algorithm)

## Environment Variables

[Read section](../references/git-staging-helpers-part-2.md#environment-variables)

### [Enabling Verbose Debug Logging](../references/git-staging-helpers-part-2.md#enabling-verbose-debug-logging)

## Forbidden Patterns

[Read section](../references/git-staging-helpers-part-2.md#forbidden-patterns)

### [Never Use Raw `git add`](../references/git-staging-helpers-part-2.md#never-use-raw-git-add)

### [Never Ignore Exit Codes](../references/git-staging-helpers-part-2.md#never-ignore-exit-codes)

### [Never Use `git add` in a Loop Without Batching](../references/git-staging-helpers-part-2.md#never-use-git-add-in-a-loop-without-batching)

### [Correct Pattern](../references/git-staging-helpers-part-2.md#correct-pattern)

## Checklists

[Read section](../references/git-staging-helpers-part-2.md#checklists)

### [PowerShell Scripts](../references/git-staging-helpers-part-2.md#powershell-scripts)

### [Bash Scripts](../references/git-staging-helpers-part-2.md#bash-scripts)

## Related Skills

[Read section](../references/git-staging-helpers-part-2.md#related-skills)

## Related Files

[Read section](../references/git-staging-helpers-part-2.md#related-files)
