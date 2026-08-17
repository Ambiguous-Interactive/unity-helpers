# Skill: Git Hook Lifecycle & Debugging

<!-- trigger: hook validation, hook auto-fix, pre-commit config, LASTEXITCODE, hook debugging, hook lock | Hook validation philosophy, framework config, PowerShell exit codes, debugging | Core -->

**Trigger**: When deciding whether hooks should validate vs auto-fix, configuring the pre-commit framework, debugging hook failures, or fixing PowerShell exit code issues.

---

## When to Use

- Deciding whether a hook should validate-only or auto-fix
- Debugging `$LASTEXITCODE` leaking from PowerShell hook scripts
- Investigating `index.lock` errors or other hook failures
- Understanding CI/CD differences for hooks

---

## When NOT to Use

- For index safety or hook templates → see [git-hook-safety](./git-hook-safety.md)
- For regex/syntax patterns → see [git-hook-syntax-portability](./git-hook-syntax-portability.md)

---

## Validation vs Modification

### Validation-Only Hooks (Recommended)

Pre-commit hooks should ideally only validate, never modify. If validation fails,
exit non-zero and let the user fix issues.

```bash
# GOOD: Report issues and fail
npm run format:check
if [ $? -ne 0 ]; then
  echo "Run 'npm run format' to fix formatting"
  exit 1
fi
```

### Auto-Fix Hooks (Use with Caution)

If you must auto-fix and re-stage, use the staging helpers:

```bash
# BAD: Auto-fixing without helpers
npm run format
git add .  # DON'T DO THIS - race conditions!

# GOOD: Auto-fixing with helpers
source "$SCRIPT_DIR/git-staging-helpers.sh"
ensure_no_index_lock
npm run format
git_add_with_retry $modified_files
```

---

## Pre-Commit Framework Configuration

This repository runs one hook path: `.githooks/`, installed by `npm run hooks:install`. Its steps
run in sequence in a single process, so a step that re-stages files cannot race another one. The
pre-commit framework's config was retired in #453 because nothing installed it, which is the failure
this note used to guard against, arriving one level up: a second configuration nobody runs is a
second set of rules nobody applies.

### Why `require_serial: true` Matters

Even with `require_serial: true` in pre-commit, external tools like lazygit may still
hold the lock when hooks begin. The `ensure_no_index_lock` function handles this case.

---

## Hook Description Accuracy

**CRITICAL**: When adding or removing script invocations from hooks, ALWAYS update
the corresponding comments/descriptions in the hook file AND in related documentation
(skill files, README, etc.). Stale descriptions mislead developers and AI agents.

### Checklist for Hook Changes

1. Update the step comment in the hook file itself
2. Update [formatting-and-linting](./formatting-and-linting.md) "What the Hook Does" list
3. Update any other skill files that describe the hook steps
4. Verify the hook description matches all script calls in that step

### Current Pre-Commit Step 0: Version Syncing

Step 0 runs two PowerShell scripts on every commit:

- [sync-banner-version.ps1](../../scripts/sync-banner-version.ps1) — Syncs banner SVG + [LLM context](../context.md) from `package.json`
- [sync-issue-template-versions.ps1](../../scripts/sync-issue-template-versions.ps1) — Syncs issue template dropdowns from `package.json`, the [CHANGELOG](../../CHANGELOG.md), and git tags

Both scripts auto-stage modified files.

## `core.hooksPath` Idempotency Normalization

When checking whether hooks are already installed, normalize `core.hooksPath` before comparison:

- trim leading/trailing whitespace and `\r`
- normalize `\` to `/`
- strip leading `./`
- strip trailing `/`

This keeps installers idempotent for equivalent values like `.githooks/`, `./.githooks`, or `.\.githooks\` while still preserving truly custom paths.

---

## CI/CD Environments

GitHub Actions and other CI environments generally **do not require** the retry helpers because:

1. **Single-process execution** - Only one git operation runs at a time
2. **No interactive tools** - No lazygit, GitKraken, or IDE integrations competing
3. **Ephemeral environments** - Fresh container per run, no stale locks

However, for consistency, you may still use the helpers in CI scripts. The overhead is negligible.

---

## PowerShell `$LASTEXITCODE` Leaking (CRITICAL)

PowerShell sets `$LASTEXITCODE` after every native command (git, npx, dotnet, etc.).
If a script does not end with an explicit `exit 0` on its success path, PowerShell
uses `$LASTEXITCODE` from the **last native command** as the process exit code.

### The `git check-ignore` Trap

`git check-ignore -q <path>` returns exit code **1** when the file is NOT ignored.
For linters, "not ignored" is the **success case** -- the file should be tracked.
But that exit code 1 stays in `$LASTEXITCODE` and leaks as the script exit code
if no explicit `exit` follows.

```powershell
# BAD - If the last file checked is NOT ignored, $LASTEXITCODE is 1
# and the script "fails" even though all checks passed
foreach ($file in $files) {
    $checkResult = & git check-ignore -q $relativePath 2>&1
    if ($LASTEXITCODE -eq 0) {
        $ignoredFiles += $relativePath
    }
}
# Script ends here - $LASTEXITCODE may be 1 from the last check-ignore call

# GOOD - Explicit exit 0 on success path
if ($hasErrors) {
    exit 1
} else {
    exit 0  # REQUIRED: prevents $LASTEXITCODE leaking
}
```

### Rule: All PowerShell Scripts MUST Have Explicit Exit Codes

Every PowerShell lint/hook script MUST end with explicit `exit` on **all** code paths:

- `exit 0` on success
- `exit 1` (or non-zero) on failure

Native commands that commonly set `$LASTEXITCODE` to non-zero on "success" cases:

| Command                        | Behavior                                    |
| ------------------------------ | ------------------------------------------- |
| `git check-ignore -q`          | Returns 1 when file is NOT ignored (normal) |
| `git diff --quiet`             | Returns 1 when there ARE differences        |
| `git merge-base --is-ancestor` | Returns 1 when NOT an ancestor              |

---

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

- [git-hook-patterns](./git-hook-patterns.md) - Hub: all hook pattern categories
- [git-hook-safety](./git-hook-safety.md) - Index safety, permissions, templates
- [git-hook-syntax-portability](./git-hook-syntax-portability.md) - Regex, case patterns, CLI safety, CRLF
- [git-safe-operations](./git-safe-operations.md) - Core git safety patterns and critical rules
- [git-staging-helpers](./git-staging-helpers.md) - PowerShell/Bash helper functions reference
- [formatting-and-linting](./formatting-and-linting.md) - Hook step descriptions

## Related Files

- [.githooks/pre-commit](../../.githooks/pre-commit) - Local pre-commit hook
- [scripts/sync-banner-version.ps1](../../scripts/sync-banner-version.ps1) - Banner and [LLM context](../context.md) version sync
- [scripts/sync-issue-template-versions.ps1](../../scripts/sync-issue-template-versions.ps1) - Issue template version sync
