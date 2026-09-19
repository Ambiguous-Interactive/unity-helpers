# License Year Cache

The license audit (`scripts/audit-license-years.sh`) determines a creation year two ways, both
in `scripts/license-year-lib.sh`. A `--paths` run asks `git log --follow` per file, which is
O(N) git invocations for N files. A whole-tree run instead primes ONE history walk
([#674](https://github.com/Ambiguous-Interactive/unity-helpers/issues/674)).

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

**`--find-copies-harder` cannot be dropped:** 24 tracked files resolve to a later year without it,
and the audit would then reject headers nobody has touched.
`scripts/tests/test-license-year-copy-detection.sh` names all 24. Narrowing the walk to a `*.cs`
pathspec takes it from 1m42s to 37s on a cold cache here, with a byte-identical path-to-year map.
That narrowing restricts the **candidate source set** the copy detection searches, not just its
output, so it is free only while no `.cs` path was ever produced by renaming or copying a non-`.cs`
one — which the same test asserts, with the same detection flags the library uses.

**What that flag was blamed for is not its arithmetic.** It makes every file in the tree a
copy-source candidate, and git asks the filesystem for `.gitattributes` in every directory of every
candidate — 8,364 of the shipped walk's 8,705 file-system calls, all but one a miss, because this
repository keeps one `.gitattributes` at its root. No attribute can change a tree-against-tree
diff's records, so the walk now reads them from the empty tree: **33.52s to 3.77s on the 9p bind
mount, and no change at all on a native filesystem**, where the lookups are cache hits. The saving
is the container's, not CI's ([#680](https://github.com/Ambiguous-Interactive/unity-helpers/issues/680)).
