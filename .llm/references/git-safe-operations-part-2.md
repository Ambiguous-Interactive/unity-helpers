# git-safe-operations - Part 2

## Split Content

## Pre-Commit Hook Configuration

This repository runs one hook path: `.githooks/`, installed by `npm run hooks:install`. Its steps
run in sequence in a single process, so a step that re-stages files cannot race another one. The
pre-commit framework's config was retired in #453 because nothing installed it, which is the failure
this note used to guard against, arriving one level up: a second configuration nobody runs is a
second set of rules nobody applies.

For detailed hook patterns, see [git-hook-patterns](../skills/git-hook-patterns.md).

---

## Git Hook Regex Patterns (CRITICAL)

> **CRITICAL**: Regex patterns in bash git hooks require SINGLE backslashes.
> Double escaping causes patterns to match NOTHING silently!

```bash
# CORRECT - Single backslash
git diff --cached --name-only | grep -E '\.(md|markdown)$'

# WRONG - Double-escaped (matches nothing!)
git diff --cached --name-only | grep -E '\\.(md|markdown)$'
```

---

## Safe Patterns for Scripts

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

## Error Handling

Always handle git command failures:

```bash
# Check if in git repository
if ! git rev-parse --git-dir > /dev/null 2>&1; then
  echo "Not a git repository"
  exit 1
fi

# Handle command failure
if ! git status --porcelain > /tmp/status.txt; then
  echo "Git status failed"
  exit 1
fi
```

---

## Debugging Lock Issues

If you still see `index.lock` errors:

1. **Enable verbose logging**: `export GIT_STAGING_VERBOSE=1` before running git commands
2. **Check for stale locks**: `ls -la .git/index.lock` - if present when no git operation running, delete it
3. **Increase timeouts**: Set `GIT_LOCK_INITIAL_WAIT_MS=20000` for slower tools
4. **Check for external tools**: IDE git integrations, file watchers, etc.
5. **Verify `require_serial`**: Ensure it's set for hooks that call `git add`
6. **Check `ensure_no_index_lock`**: Verify hooks call this at the start

For detailed debugging steps, see [git-staging-helpers](../skills/git-staging-helpers.md#enabling-verbose-debug-logging).

---

## CI/CD Environments

GitHub Actions and other CI environments generally **do not require** the retry helpers because:

1. **Single-process execution** - Only one git operation runs at a time
2. **No interactive tools** - No lazygit, GitKraken, or IDE integrations competing
3. **Ephemeral environments** - Fresh container per run, no stale locks

However, for consistency, you may still use the helpers in CI scripts. The overhead is negligible.

---

## Related Skills

- [git-staging-helpers](../skills/git-staging-helpers.md) - PowerShell/Bash helper functions reference
- [git-hook-patterns](../skills/git-hook-patterns.md) - Pre-commit hook safety and configuration
- [validate-before-commit](../skills/validate-before-commit.md) - Pre-commit validation commands
- [formatting](../skills/formatting.md) - CSharpier, Prettier, markdownlint workflow
- [ship-changes Step 9: Push to Remote](../skills/ship-changes.md#step-9-push-to-remote) - push.autoSetupRemote policy, forbidden output redirection, hook bypass ban

## Related Files

- [scripts/git-staging-helpers.ps1](../../scripts/git-staging-helpers.ps1) - PowerShell shared helper module
- [scripts/git-staging-helpers.sh](../../scripts/git-staging-helpers.sh) - Bash shared helper module
- [.githooks/pre-commit](../../.githooks/pre-commit) - Local pre-commit hook (uses helpers)
