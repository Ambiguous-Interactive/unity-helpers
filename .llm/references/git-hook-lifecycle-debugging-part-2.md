# git-hook-lifecycle-debugging - Part 2

## Split Content

## Error Handling in Hooks

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

## Debugging Hook Lock Issues

If you still see `index.lock` errors:

1. **Enable verbose logging**: `export GIT_STAGING_VERBOSE=1` before running git commands
2. **Check for stale locks**: `ls -la .git/index.lock` - if present when no git operation running, delete it
3. **Increase timeouts**: Set `GIT_LOCK_INITIAL_WAIT_MS=20000` for slower tools
4. **Check for external tools**: IDE git integrations, file watchers, etc.
5. **Verify `require_serial`**: Ensure it's set for hooks that call `git add`
6. **Check `ensure_no_index_lock`**: Verify hooks call this at the start
7. **Check flock availability**: On non-Linux systems, cross-process coordination may be limited

### Example Debug Session

```bash
# Enable verbose logging
export GIT_STAGING_VERBOSE=1

# Increase initial wait to 20 seconds (useful for slow external tools)
export GIT_LOCK_INITIAL_WAIT_MS=20000

# Now commit and watch the verbose output
git commit -m "test commit"
```

The verbose output will show exactly when locks are detected, how long waits take,
and what git operations are attempted.

---

## Related Skills

- [git-hook-patterns](../skills/git-hook-patterns.md) - Hub: all hook pattern categories
- [git-hook-safety](../skills/git-hook-safety.md) - Index safety, permissions, templates
- [git-hook-syntax-portability](../skills/git-hook-syntax-portability.md) - Regex, case patterns, CLI safety, CRLF
- [git-safe-operations](../skills/git-safe-operations.md) - Core git safety patterns and critical rules
- [git-staging-helpers](../skills/git-staging-helpers.md) - PowerShell/Bash helper functions reference
- [formatting-and-linting](../skills/formatting-and-linting.md) - Hook step descriptions

## Related Files

- [.githooks/pre-commit](../../.githooks/pre-commit) - Local pre-commit hook
- [scripts/sync-banner-version.ps1](../../scripts/sync-banner-version.ps1) - Banner and [LLM context](../context.md) version sync
- [scripts/sync-issue-template-versions.ps1](../../scripts/sync-issue-template-versions.ps1) - Issue template version sync
