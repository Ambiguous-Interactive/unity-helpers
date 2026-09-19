# Skill: Git Hook Lifecycle & Debugging

<!-- trigger: hook validation, hook auto-fix, pre-commit config, LASTEXITCODE, hook debugging, hook lock | Hook validation philosophy, framework config, PowerShell exit codes, debugging | Core -->

## Reference Parts

- [Part 1](../references/git-hook-lifecycle-debugging-part-1.md)
- [Part 2](../references/git-hook-lifecycle-debugging-part-2.md)

## When to Use

[Read section](../references/git-hook-lifecycle-debugging-part-1.md#when-to-use)

## When NOT to Use

[Read section](../references/git-hook-lifecycle-debugging-part-1.md#when-not-to-use)

## Validation vs Modification

[Read section](../references/git-hook-lifecycle-debugging-part-1.md#validation-vs-modification)

### [Validation-Only Hooks (Recommended)](../references/git-hook-lifecycle-debugging-part-1.md#validation-only-hooks-recommended)

### [Auto-Fix Hooks (Use with Caution)](../references/git-hook-lifecycle-debugging-part-1.md#auto-fix-hooks-use-with-caution)

## Pre-Commit Framework Configuration

[Read section](../references/git-hook-lifecycle-debugging-part-1.md#pre-commit-framework-configuration)

### [Why `require_serial: true` Matters](../references/git-hook-lifecycle-debugging-part-1.md#why-require_serial-true-matters)

## Hook Description Accuracy

[Read section](../references/git-hook-lifecycle-debugging-part-1.md#hook-description-accuracy)

### [Checklist for Hook Changes](../references/git-hook-lifecycle-debugging-part-1.md#checklist-for-hook-changes)

### [Current Pre-Commit Step 0: Version Syncing](../references/git-hook-lifecycle-debugging-part-1.md#current-pre-commit-step-0-version-syncing)

## `core.hooksPath` Idempotency Normalization

[Read section](../references/git-hook-lifecycle-debugging-part-1.md#corehookspath-idempotency-normalization)

## CI/CD Environments

[Read section](../references/git-hook-lifecycle-debugging-part-1.md#cicd-environments)

## PowerShell `$LASTEXITCODE` Leaking (CRITICAL)

[Read section](../references/git-hook-lifecycle-debugging-part-1.md#powershell-lastexitcode-leaking-critical)

### [The `git check-ignore` Trap](../references/git-hook-lifecycle-debugging-part-1.md#the-git-check-ignore-trap)

### [Rule: All PowerShell Scripts MUST Have Explicit Exit Codes](../references/git-hook-lifecycle-debugging-part-1.md#rule-all-powershell-scripts-must-have-explicit-exit-codes)

## Error Handling in Hooks

[Read section](../references/git-hook-lifecycle-debugging-part-2.md#error-handling-in-hooks)

## Debugging Hook Lock Issues

[Read section](../references/git-hook-lifecycle-debugging-part-2.md#debugging-hook-lock-issues)

### [Example Debug Session](../references/git-hook-lifecycle-debugging-part-2.md#example-debug-session)

## Related Skills

[Read section](../references/git-hook-lifecycle-debugging-part-2.md#related-skills)

## Related Files

[Read section](../references/git-hook-lifecycle-debugging-part-2.md#related-files)
