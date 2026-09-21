# git-hook-lifecycle-debugging - Part 1

## Split Content

**Trigger**: When deciding whether hooks should validate vs auto-fix, configuring the pre-commit framework, debugging hook failures, or fixing PowerShell exit code issues.

---

## When to Use

- Deciding whether a hook should validate-only or auto-fix
- Debugging `$LASTEXITCODE` leaking from PowerShell hook scripts
- Investigating `index.lock` errors or other hook failures
- Understanding CI/CD differences for hooks

---

## When NOT to Use

- For index safety or hook templates → see [git-hook-safety](../skills/git-hook-safety.md)
- For regex/syntax patterns → see [git-hook-syntax-portability](../skills/git-hook-syntax-portability.md)

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
2. Update [formatting-and-linting](../skills/formatting-and-linting.md) "What the Hook Does" list
3. Update any other skill files that describe the hook steps
4. Verify the hook description matches all script calls in that step

### Current Pre-Commit Step 0: Version Syncing

Step 0 runs two PowerShell scripts on every commit:

- [sync-banner-version.ps1](../../scripts/sync-banner-version.ps1) — Syncs banner SVG + [LLM context](../context.md) from `package.json`
- [sync-issue-template-versions.ps1](../../scripts/sync-issue-template-versions.ps1) — Syncs issue template dropdowns from the tracked [.github/issue-template-versions.json](../../.github/issue-template-versions.json) manifest; release preparation adds the current package version

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
