# Skill: Optimize Git Hooks

<!-- trigger: git hook performance, hook speed, slow hooks, hook optimization, pre-push latency | How to keep git hooks fast | Core -->

## Purpose

Patterns and techniques for keeping git hooks fast (<1s warm path). Covers changed-file
detection, caching, batching, and incremental checking.

## When to Use This Skill

- A hook takes more than 1 second on a typical warm push/commit
- Adding a new check to an existing hook
- Debugging slow hook performance
- Deciding whether a check belongs in hook vs CI

---

## Core Principle: CI Catches Repo-Wide, Hooks Catch Change-Local

Hooks should be **fast** and last-resort. Agent workflows, `agent:preflight`,
`validate:prepush`, and CI should catch normal lint/test/doc/spelling failures
before Git invokes a hook. Local hooks validate only the files being
committed/pushed and only the tiny set of checks that are cheap and safety
critical.

This means:

- Hooks operate on changed files only (not all files)
- Hooks skip checks when no relevant files changed
- Hooks cache expensive computations
- CI runs the same lint scripts without `--paths` for full coverage
- Pre-push must not start Node or child PowerShell processes for ordinary
  linting; route those checks through `npm run agent:preflight`,
  `npm run validate:prepush`, and CI

---

## Changed-File Detection via Pre-Push Stdin

See [git-hook-patterns](./git-hook-patterns.md)
for the full stdin-parsing pattern.

**Key optimization:** Let Git identify relevant changed content using
null-delimited output and pickaxe patterns instead of reading each changed blob
in a loop:

```powershell
$regionPattern = '^[[:space:]]*#[[:space:]]*(region|endregion)'
$changedRegionFiles = & git diff `
  --name-only `
  -z `
  --diff-filter=ACMRTUXB `
  -G $regionPattern `
  "$remoteSha..$localSha" `
  -- '*.cs'
```

This catches pushed changes that add, remove, or edit `#region`/`#endregion`
directives. If a new branch has no discoverable merge base, a single
`git grep <sha> -- '*.cs'` fallback is acceptable; avoid per-file `git show`
loops.

---

## License Year Cache

The license audit (`scripts/audit-license-years.sh`) uses `git log --follow` per file
to determine creation year. This is O(N) git invocations for N files.

**Cache design:**

- Location: `.git/license-year-cache` (inside `.git/`, never tracked)
- Format: `<relative-path>\t<creation-year>` (tab-separated, one per line)
- Loaded into bash associative array at startup for O(1) lookups
- Written atomically via `mktemp` + `mv` with `trap EXIT` safety
- Invalidated by `.githooks/post-rewrite` on history rewrite (rebase, amend)

**Usage:**

```bash
# Full audit (CI mode) — uses cache, audits all files
bash scripts/audit-license-years.sh --summary

# Incremental audit (hook mode) — only audit changed .cs files
bash scripts/audit-license-years.sh --summary --paths file1.cs file2.cs

# Force fresh scan (cache debugging)
bash scripts/audit-license-years.sh --summary --no-cache
```

**Performance:** Full uncached scans are too slow for pre-push. Pre-commit,
agent preflight, and CI paths must pass changed files through `--paths`; warm
cache checks for a few changed files should stay sub-second.

---

## Batch Git Operations (Avoid N+1)

### `git check-ignore --stdin` instead of per-file calls

```powershell
# BAD: N subprocess calls
foreach ($file in $files) {
    & git check-ignore -q $file 2>&1
}

# GOOD: 1 subprocess call
function Get-GitIgnoredPaths([string[]]$paths) {
    $ignoredSet = [System.Collections.Generic.HashSet[string]]::new()
    $result = ($paths -join "`n") | & git check-ignore --stdin 2>$null
    # parse result into $ignoredSet
    return $ignoredSet
}
```

### `git ls-files` instead of filesystem traversal

```powershell
# BAD: 20 recursive Get-ChildItem calls
foreach ($root in $sourceRoots) {
    $items += Get-ChildItem -Recurse -File -Path $root
}

# GOOD: 1 git call, process in memory
$allFiles = (& git ls-files -z -- @sourceRoots) -split [char]0 | Where-Object { $_ }
```

### `-Paths` parameter for incremental checking

Scripts that normally scan all tracked files should accept `-Paths` to check a
specific set:

```powershell
param(
    [string[]]$Paths  # When provided, check only these files
)

function Get-TrackedFiles {
    if ($Paths -and $Paths.Count -gt 0) {
        return $Paths
    }
    return (& git ls-files -z) -split [char]0 | Where-Object { $_ }
}
```

---

## Performance Budget for Hooks

| Category               | Target  | Technique                                |
| ---------------------- | ------- | ---------------------------------------- |
| Changed-file detection | <100ms  | Parse stdin, `git diff`                  |
| Native checks (group)  | <250ms  | Git pickaxe/grep, no per-file blob reads |
| Artifact auto-cleanup  | <100ms  | Hook-name patterns + `git check-ignore`  |
| **Total pre-push**     | **<1s** | Last-resort checks only                  |

---

## Adding New Checks to Pre-Push

When adding a new check to the pre-push hook:

1. First prove the check cannot live in `agent:preflight`, `validate:prepush`,
   pre-commit, or CI. That is the default home for formatting, spelling, docs,
   EOL, meta, and regression-suite validation.
2. Measure the hook before and after. Any total pre-push time above 1 second is
   a failure to investigate, not an accepted tradeoff.
3. Prefer Git-native changed-content filters such as `git diff -G` over
   collecting broad path lists and scanning each blob.
4. **Skip when empty:** `if [ ${#CHANGED_SET[@]} -gt 0 ]; then ... fi`.
5. Keep the check dependency-light and native. Avoid Node/PowerShell startup in
   pre-push unless there is no viable last-resort alternative.
6. **Return non-zero** on failure from within the check function.
7. **Update the hook header comment** listing all checks.
8. **Add a test** in `scripts/tests/test-pre-push-changed-files.sh`.

---

## Related Skills

- [git-hook-patterns](./git-hook-patterns.md) - Hook safety, stdin reading, POSIX compat
- [git-safe-operations](./git-safe-operations.md) - Core git safety patterns

## Related Files

- [.githooks/pre-push](../../.githooks/pre-push) - Optimized pre-push hook
- [.githooks/post-rewrite](../../.githooks/post-rewrite) - Cache invalidation
- [scripts/audit-license-years.sh](../../scripts/audit-license-years.sh) - License cache implementation
- [scripts/tests/test-pre-push-changed-files.sh](../../scripts/tests/test-pre-push-changed-files.sh) - Pre-push structure tests
- [scripts/tests/test-license-cache.sh](../../scripts/tests/test-license-cache.sh) - License cache tests
