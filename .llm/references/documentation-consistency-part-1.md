# documentation-consistency - Part 1

## Split Content

**Trigger**: When writing or reviewing documentation across multiple files (root README, docs readme, docs index, etc.)

---

## When to Use

This skill applies when creating or modifying documentation that appears in multiple locations or makes claims that must be consistent across the codebase.

---

## When NOT to Use

This skill is NOT needed for:

- **Single-file documentation changes** — No cross-file consistency to maintain
- **Internal code comments** — Not user-facing, no consistency requirement
- **Test files or examples** — Isolated documentation, doesn't need to match other files
- **CHANGELOG entries** — Already have their own format rules (see [update-documentation](../skills/update-documentation.md))
- **XML documentation comments** — Per-member docs, not cross-file claims

---

## Key Principles

### 1. Performance Claims Must Be Consistent

Performance numbers MUST match across all documentation files. If a benchmark shows a range, use the same range everywhere.

| ❌ Inconsistent                                          | ✅ Consistent                                              |
| -------------------------------------------------------- | ---------------------------------------------------------- |
| README says "100x faster", docs/readme says "12x faster" | All files say "10-100x faster (varies by operation)"       |
| One file says "up to 15x", another says "10-15x"         | All files use the same phrasing: "10-15x faster"           |
| Vague claim without context                              | Specific claim with context: "~12x for method invocations" |

**Best Practice**: When performance varies by scenario, document the range AND explain what affects it:

```markdown
<!-- ✅ GOOD: Clear, consistent, explains variance -->

Cached delegates are 10-100x faster than raw `System.Reflection`
(method invocations ~12x; boxed scenarios up to 100x)

<!-- ❌ BAD: Inconsistent claims in different files -->

File A: "100x faster than System.Reflection"
File B: "up to 12x faster than System.Reflection"
```

### 2. Time Estimates Use Tilde (~) Prefix

All time estimates should use the tilde (~) prefix to indicate approximation.

| ❌ Without tilde | ✅ With tilde |
| ---------------- | ------------- |
| 2 minutes        | ~2 minutes    |
| 5 minutes        | ~5 minutes    |
| 10 minutes       | ~10 minutes   |
| 1 minute         | ~1 minute     |

**Example table**:

```markdown
| Task                    | Time to Value |
| ----------------------- | ------------- |
| Inspector Tooling setup | ~2 minutes    |
| Component wiring        | ~2 minutes    |
| Effects system          | ~5 minutes    |
```

### 3. Avoid Redundant Prefixes When Emoji Conveys Meaning

When using emoji to signal meaning (⏱️ for time, ✅ for success, etc.), do NOT add text prefixes that repeat the same information.

| ❌ Redundant                      | ✅ Clean            | Why                                          |
| --------------------------------- | ------------------- | -------------------------------------------- |
| ⏱️ **Estimated Example:** 2 min   | ⏱️ 2 min            | "Estimated Example:" is redundant with emoji |
| ⏱️ **Estimated:** ~5 minutes      | ⏱️ ~5 minutes       | ⏱️ already means "time/duration"             |
| ⏱️ **Time Estimate:** ~10 minutes | ⏱️ ~10 minutes      | Emoji is self-explanatory                    |
| ✅ **Success:** Tests passed      | ✅ Tests passed     | ✅ already signals success                   |
| ⚠️ **Warning:** Be careful        | ⚠️ Be careful       | ⚠️ already signals warning                   |
| 🎯 **Target:** High performance   | 🎯 High performance | Emoji already conveys "target/goal"          |

**Correct Patterns**:

```markdown
<!-- ✅ GOOD: Emoji alone signals meaning -->

⏱️ ~2 minutes
⏱️ ~5 minutes setup
🚀 10-15x faster than alternatives

<!-- ❌ BAD: Redundant text repeats what emoji shows -->

⏱️ **Estimated:** ~2 minutes
⏱️ **Time to complete:** ~5 minutes
🚀 **Performance:** 10-15x faster than alternatives
```

**Rule**: If an emoji clearly conveys the category (time, warning, success), the following text should be the VALUE, not a label describing the category.

**Exception — Distinct Metric Names**: When the label describes a _specific metric_ rather than the generic category, include it for clarity:

```markdown
<!-- ✅ OK: "Time Saved" is a specific metric, not just "time" -->

**⏱️ Time Saved:** 10-20 lines × hundreds of components = weeks

<!-- ✅ OK: "Time to Value" is a specific metric -->

**⏱️ Time to Value:** ~2 minutes

<!-- ❌ BAD: "Estimated" just restates "time" (the emoji's meaning) -->

⏱️ **Estimated:** ~2 minutes
```

The distinction: "Time Saved" and "Time to Value" are _metric names_ that add meaning beyond what ⏱️ conveys. "Estimated" or "Duration" merely restate the emoji's meaning.

### 4. Consistent Performance Claim Phrasing

Use consistent phrasing patterns for performance claims. Pick ONE pattern and use it everywhere.

| ❌ Inconsistent (same doc)                         | ✅ Consistent (same doc)                 |
| -------------------------------------------------- | ---------------------------------------- |
| "10-15x faster" here, "up to 15x" there            | "10-15x faster" everywhere               |
| "100x speedup" here, "up to 100x faster" there     | "10-100x faster" everywhere (with range) |
| "outperforms by 12x" here, "12x improvement" there | "~12x faster" everywhere                 |

**Standard Phrasing Patterns**:

```markdown
<!-- ✅ PREFERRED: Use these patterns consistently -->

Range format: 10-15x faster than [baseline]
Single value: ~12x faster than [baseline]
Comparison: X compared to [baseline] (NOT "vs" or "versus")

<!-- ❌ AVOID: These create inconsistency -->

"up to 15x faster" → Use "10-15x faster" (shows full range)
"as much as 100x speedup" → Use "10-100x faster" (consistent verb)
"12x improvement" → Use "~12x faster" (consistent structure)
"outperforms by X" → Use "X faster than" (clearer comparison)
```

**Why avoid "up to X"?** It implies the lower bound is unknown or insignificant. If you know the range, state it explicitly (e.g., "10-15x faster" instead of "up to 15x faster").

### 5. Avoid Run-On Sentences

Don't combine multiple distinct claims in a single sentence. Break them into clear, scannable points.

```markdown
<!-- ❌ BAD: Run-on sentence combining multiple claims -->

Random generation is 10-15x faster than Unity.Random (see benchmarks),
spatial queries use O(log n) algorithms for efficient large dataset handling,
and declarative inspector attributes can reduce custom editor code.

<!-- ✅ GOOD: Separated into clear points -->

Key performance highlights:

- 10-15x faster random generation than Unity.Random (see benchmarks)
- O(log n) spatial queries for efficient large dataset handling
- Declarative inspector attributes to reduce custom editor code
```
