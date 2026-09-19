# Skill: Git-Safe Operations in Scripts

<!-- trigger: git, hook, script, index, lock | Scripts or hooks that interact with git index | Core -->

## Reference Parts

- [Part 1](../references/git-safe-operations-part-1.md)
- [Part 2](../references/git-safe-operations-part-2.md)

## Purpose

[Read section](../references/git-safe-operations-part-1.md#purpose)

## When to Use

[Read section](../references/git-safe-operations-part-1.md#when-to-use)

## Background: The `index.lock` Problem

[Read section](../references/git-safe-operations-part-1.md#background-the-indexlock-problem)

## Critical Rules

[Read section](../references/git-safe-operations-part-1.md#critical-rules)

### [1. Never Parallel Git Operations](../references/git-safe-operations-part-1.md#1-never-parallel-git-operations)

### [2. Use Porcelain Output for Parsing](../references/git-safe-operations-part-1.md#2-use-porcelain-output-for-parsing)

### [3. Lock-Aware Operations](../references/git-safe-operations-part-1.md#3-lock-aware-operations)

### [4. Wait for External Tools at Hook Start](../references/git-safe-operations-part-1.md#4-wait-for-external-tools-at-hook-start)

### [5. Agent-Specific: Publish Completed Work Safely](../references/git-safe-operations-part-1.md#5-agent-specific-publish-completed-work-safely)

## Quick Start: Using the Staging Helpers

[Read section](../references/git-safe-operations-part-1.md#quick-start-using-the-staging-helpers)

### [PowerShell](../references/git-safe-operations-part-1.md#powershell)

### [Bash](../references/git-safe-operations-part-1.md#bash)

## Forbidden Patterns

[Read section](../references/git-safe-operations-part-1.md#forbidden-patterns)

### [Never Use Raw `git add`](../references/git-safe-operations-part-1.md#never-use-raw-git-add)

### [Never Ignore Exit Codes](../references/git-safe-operations-part-1.md#never-ignore-exit-codes)

### [Never Use `git add` in a Loop Without Batching](../references/git-safe-operations-part-1.md#never-use-git-add-in-a-loop-without-batching)

### [Correct Pattern](../references/git-safe-operations-part-1.md#correct-pattern)

## Pre-Commit Hook Configuration

[Read section](../references/git-safe-operations-part-2.md#pre-commit-hook-configuration)

## Git Hook Regex Patterns (CRITICAL)

[Read section](../references/git-safe-operations-part-2.md#git-hook-regex-patterns-critical)

## Safe Patterns for Scripts

[Read section](../references/git-safe-operations-part-2.md#safe-patterns-for-scripts)

### [Checking for Changes](../references/git-safe-operations-part-2.md#checking-for-changes)

### [Getting File Lists](../references/git-safe-operations-part-2.md#getting-file-lists)

### [Comparing Versions](../references/git-safe-operations-part-2.md#comparing-versions)

## Error Handling

[Read section](../references/git-safe-operations-part-2.md#error-handling)

## Debugging Lock Issues

[Read section](../references/git-safe-operations-part-2.md#debugging-lock-issues)

## CI/CD Environments

[Read section](../references/git-safe-operations-part-2.md#cicd-environments)

## Related Skills

[Read section](../references/git-safe-operations-part-2.md#related-skills)

## Related Files

[Read section](../references/git-safe-operations-part-2.md#related-files)
