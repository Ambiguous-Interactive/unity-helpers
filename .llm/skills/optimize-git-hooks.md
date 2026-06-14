# Skill: Optimize Git Hooks

<!-- trigger: git hook performance, hook speed, slow hooks, hook optimization, pre-push latency | How to keep git hooks fast | Core -->

## Purpose

Patterns and techniques for keeping git hooks fast (<1s warm path). Covers changed-file
detection, caching, batching, parallel execution, and incremental checking.

## When to Use This Skill

- A hook takes more than 1 second on a typical warm push/commit
- Adding a new check to an existing hook
- Debugging slow hook performance
- Deciding whether a check belongs in hook vs CI

---

## Core Principle: CI Catches Repo-Wide, Hooks Catch Change-Local

Hooks should be **fast** — validate only the files being committed/pushed. CI runs
the full-repo validation as a safety net. This means:

- Hooks operate on changed files only (not all files)
- Hooks skip checks when no relevant files changed
- Hooks cache expensive computations
- CI runs the same lint scripts without `--paths` for full coverage

---

## Changed-File Detection via Pre-Push Stdin

See [git-hook-patterns](./git-hook-patterns.md)
for the full stdin-parsing pattern.

**Key optimization:** Collect changed files once using null-delimited git output, then classify them into arrays and reuse those arrays across checks:

```bash
ALL_CHANGED_FILES=()
while IFS= read -r -d '' file; do
    ALL_CHANGED_FILES+=("$file")
done < <(git diff --name-only -z ...)

CHANGED_MD=()
CHANGED_CS=()
for file in "${ALL_CHANGED_FILES[@]}"; do
    [[ "$file" =~ \.(md|markdown)$ ]] && CHANGED_MD+=("$file")
    [[ "$file" =~ \.cs$ ]] && CHANGED_CS+=("$file")
done

if [ ${#CHANGED_MD[@]} -gt 0 ]; then
    node scripts/run-node-bin.js markdownlint --config .markdownlint.json -- "${CHANGED_MD[@]}"
fi
```

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

**Performance:** Full uncached scans are too slow for hooks. Hook and preflight paths must pass
changed files through `--paths`; warm cache checks for a few changed files should stay sub-second.

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

## Parallel Execution

See [git-hook-patterns](./git-hook-patterns.md)
for the full pattern. Key considerations:

- **Group by runtime:** node checks (prettier, markdownlint, cspell), PowerShell
  checks (lint-gitignore, lint-meta, check-eol), bash checks (license audit)
- **No shared state:** Each group writes to its own stdout/stderr
- **Error propagation:** Collect PIDs, `wait` each, set a failure flag
- **Cleanup:** `trap cleanup EXIT INT TERM` kills background processes + removes temp files

---

## Performance Budget for Hooks

| Category                  | Target  | Technique                          |
| ------------------------- | ------- | ---------------------------------- |
| Changed-file detection    | <100ms  | Parse stdin, `git diff`            |
| Node.js checks (group)    | <500ms  | Repo-local tools, changed files    |
| PowerShell checks (group) | <500ms  | Batched git ops, `-Paths`          |
| License audit             | <250ms  | Cache + `--paths` incremental      |
| Bash checks (group)       | <250ms  | Regex, no broad filesystem scans   |
| **Total pre-push**        | **<1s** | Parallel groups, warm cache target |

---

## Adding New Checks to Pre-Push

When adding a new check to the pre-push hook:

1. **Classify changed files into a dedicated array** while walking the collected file list
2. **Skip when empty:** `if [ ${#CHANGED_SET[@]} -gt 0 ]; then ... fi`
3. **Add to the appropriate parallel group** (node/pwsh/bash)
4. **Return non-zero** on failure from within the group function
5. **Update the hook header comment** listing all checks
6. **Add a test** in `scripts/tests/test-pre-push-changed-files.sh`

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
