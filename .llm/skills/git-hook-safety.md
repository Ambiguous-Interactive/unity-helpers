# Skill: Git Hook Safety

<!-- trigger: hook safety, index.lock, hook template, hook permissions, staged files | Hook index safety, permissions, and execution templates | Core -->

**Trigger**: When writing hooks that interact with the git index, debugging lock errors, or setting up hook infrastructure.

---

## When to Use

- Writing a new pre-commit or post-commit hook from scratch
- Debugging `index.lock` errors during hook execution
- Ensuring hooks handle partial staging correctly
- Verifying hook file permissions are executable

---

## When NOT to Use

- For regex/syntax patterns in hooks → see [git-hook-syntax-portability](./git-hook-syntax-portability.md)
- For validation philosophy or framework config → see [git-hook-lifecycle-debugging](./git-hook-lifecycle-debugging.md)
- For general git safety (not hooks) → see [git-safe-operations](./git-safe-operations.md)

---

## Critical: Wait for External Tools at Hook Start

**CRITICAL**: At the start of any git hook, wait for external tools (lazygit, IDE, etc.)
to release the index.lock before performing any operations:

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

---

## Capture State Early

```bash
#!/bin/bash
# Capture staged files ONCE at the start
STAGED_FILES=$(git diff --cached --name-only --diff-filter=ACMR)

# Process files without re-querying git
for file in $STAGED_FILES; do
  # validate file
done
```

---

## Handle Partial Staging

Users may stage only part of a file. Validate against staged content, not working directory:

```bash
# Get staged content of a file
git show :path/to/file.cs

# Or use git stash for complex validations
git stash --keep-index --include-untracked
# run validation
git stash pop
```

---

## Safe Patterns for Hook Scripts

### Checking for Changes

```bash
# Check if working tree is clean
if git diff --quiet && git diff --cached --quiet; then
  echo "No changes"
fi

# Check for specific file changes (use rg, not grep)
if git diff --name-only | rg -q "\.cs$"; then
  echo "C# files modified"
fi
```

### Getting File Lists

```bash
# All tracked files
git ls-files

# Modified files (unstaged)
git diff --name-only

# Staged files
git diff --cached --name-only

# Untracked files
git ls-files --others --exclude-standard
```

### Comparing Versions

```bash
# Changes since last commit
git diff HEAD

# Changes between branches
git diff main..feature-branch --name-only

# Changes in specific directory
git diff --name-only -- "path/to/dir/"
```

---

## Hook File Permissions (Critical)

Git silently skips hook entrypoints that are not executable. In this repository,
entrypoints are the extensionless `.githooks/<hook>` files; companion
`.githooks/<hook>.ps1` implementations are invoked through PowerShell and do
not need executable git metadata.

- The `hooks:install` script sets executable permissions automatically
- If hooks are not running, check permissions: `git ls-files -s .githooks/`
- Fix tracked permissions: `git update-index --chmod=+x .githooks/pre-commit .githooks/pre-merge-commit .githooks/pre-push .githooks/post-rewrite`
- Fix local permissions: `chmod +x .githooks/pre-commit .githooks/pre-merge-commit .githooks/pre-push .githooks/post-rewrite`
- Use `npm run validate:hook-perms` to verify hook file permissions

---

## Hook Template

```bash
#!/usr/bin/env bash
set -e
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
source "$SCRIPT_DIR/../scripts/git-staging-helpers.sh"
ensure_no_index_lock || { echo "Warning: index.lock still held." >&2; }

STAGED_FILES=$(git diff --cached --name-only --diff-filter=ACMR)
[ -z "$STAGED_FILES" ] && exit 0

# Filter for specific file types (SINGLE backslash!)
CS_FILES=$(echo "$STAGED_FILES" | grep -E '\.(cs)$' || true)
if [ -n "$CS_FILES" ]; then
    echo "Validating C# files..."
    # your validation here
    # If auto-fixing and re-staging: git_add_with_retry $CS_FILES
fi
exit 0
```

---

## Related Skills

- [git-hook-patterns](./git-hook-patterns.md) - Hub: all hook pattern categories
- [git-hook-syntax-portability](./git-hook-syntax-portability.md) - Regex, case patterns, CLI safety, CRLF
- [git-hook-lifecycle-debugging](./git-hook-lifecycle-debugging.md) - Validation, config, errors, debugging
- [git-safe-operations](./git-safe-operations.md) - Core git safety patterns and critical rules
- [git-staging-helpers](./git-staging-helpers.md) - PowerShell/Bash helper functions reference

## Related Files

- [.githooks/pre-commit](../../.githooks/pre-commit) - Local pre-commit hook (uses helpers)
- [.pre-commit-config.yaml](../../.pre-commit-config.yaml) - Pre-commit framework configuration
- [scripts/format-staged-csharp.ps1](../../scripts/format-staged-csharp.ps1) - Example formatter script
