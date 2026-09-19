# documentation-consistency - Part 2

## Split Content

### 6. When to Use Bullet Lists vs Sentences

Use bullet lists for **multiple parallel items**. Use sentences for **single concepts or narrative flow**.

| Situation                          | Use           | Why                                    |
| ---------------------------------- | ------------- | -------------------------------------- |
| 3+ related features/capabilities   | Bullet list   | Easier to scan, each item stands alone |
| 2 items that are closely related   | Sentence OK   | "X and Y" is natural                   |
| Sequential steps or instructions   | Numbered list | Order matters                          |
| Single claim with explanation      | Sentence      | Context flows naturally                |
| Comparison of alternatives         | Table         | Side-by-side comparison                |
| Key-value pairs (feature: benefit) | Bullet list   | Clear association                      |

**Convert to bullets when:**

```markdown
<!-- ❌ BAD: Run-on list disguised as a sentence -->

This package provides PRNGs that are 10-15x faster, spatial trees with O(log n) queries,
zero-allocation pooling for collections and arrays, and thread-safe singletons.

<!-- ✅ GOOD: Proper bullet list -->

This package provides:

- PRNGs that are 10-15x faster than Unity.Random
- Spatial trees with O(log n) queries
- Zero-allocation pooling for collections and arrays
- Thread-safe singletons
```

**Keep as sentence when:**

```markdown
<!-- ✅ GOOD: Single claim with context, flows naturally -->

The spatial hash provides O(1) average-case lookups for grid-aligned queries.

<!-- ❌ UNNECESSARY: Over-formatted for simple content -->

Key features:

- O(1) average-case lookups for grid-aligned queries
```

### 7. Eliminate Redundancy

Don't repeat the same concept with different wording in the same section.

```markdown
<!-- ❌ BAD: Redundant statements -->

### Schema Evolution

Schema evolution support for backward-compatible serialization.
Forward and backward compatible serialization.

<!-- ✅ GOOD: Single clear statement -->

### Schema Evolution

Forward and backward compatible serialization — add new fields
without breaking existing saves.
```

### 8. Use Clear, Unambiguous Phrasing

Avoid phrasing that could be misread or misunderstood.

```markdown
<!-- ❌ BAD: Awkward, could be misread -->

Benchmarks show 10-15x faster random generation than Unity.Random

<!-- ✅ GOOD: Clear comparison structure -->

Benchmarks demonstrate 10-15x faster random generation compared to Unity.Random
```

### 9. Dependency Claims Must Be Accurate

Dependency descriptions must accurately reflect the package structure.

| Scenario                           | Correct Description                              |
| ---------------------------------- | ------------------------------------------------ |
| Dependency is bundled with package | "Zero external dependencies — [name] is bundled" |
| Dependency must be installed       | "Requires [name] for [feature]"                  |
| Optional dependency                | "Optional: [name] for [feature]"                 |

```markdown
<!-- ✅ GOOD: Accurate for bundled dependency -->

- ✅ **Zero external dependencies** — protobuf-net is bundled for binary serialization

<!-- ❌ BAD: Confusing/contradictory -->

- ✅ **Minimal external dependencies** - depends on protobuf-net for binary serialization
```

### 10. Anchor Link Format Rules

When creating anchor links (links to headings within a document), use consistent format.

**Standard Anchor Format** (GitHub/MkDocs compatible):

```markdown
<!-- ✅ CORRECT: Lowercase, hyphens for spaces, no special chars -->

[Link Text](#heading-name)
[Performance Section](#performance-claims)
[Time Estimates](#2-time-estimates-use-tilde--prefix)

<!-- ❌ WRONG: Various incorrect formats -->

[Link](#HeadingName) ← Wrong: Keep lowercase
[Link](#heading_name) ← Wrong: Use hyphens, not underscores
[Link](#Heading Name) ← Wrong: No spaces allowed
[Link](#heading-name-) ← Wrong: No trailing hyphens
```

**Anchor Generation Rules** (GitHub Flavored Markdown):

| Heading Text                | Correct Anchor                             |
| --------------------------- | ------------------------------------------ |
| `## Quick Start`            | `#quick-start`                             |
| `## 2. Time Estimates`      | `#2-time-estimates`                        |
| `## C# Code Examples`       | `#c-code-examples` (special chars removed) |
| `## What's New in v3.0`     | `#whats-new-in-v30`                        |
| `## PRNGs (Random Numbers)` | `#prngs-random-numbers`                    |
| `## ⚡ Top Time-Savers`     | `#-top-time-savers` (emoji → leading `-`)  |
| `### 2. 🔌 Auto-Wire`       | `#2--auto-wire` (period + emoji → `--`)    |

**Rules**:

1. Convert to lowercase
2. Replace spaces with hyphens (`-`)
3. Remove special characters (including emoji) except hyphens
4. Collapse multiple hyphens into one (but see emoji note below)
5. Remove trailing hyphens

**Emoji in Headings**: When a heading starts with emoji (e.g., `## ⚡ Top`), the emoji is removed but the resulting space becomes a leading hyphen. Markdownlint preserves this leading hyphen (e.g., `#-top`), even though some platforms may strip it. **Use markdownlint's format** (`#-top-time-savers`) for consistency with CI validation.

**Cross-Platform Compatibility**: If targeting both GitHub and MkDocs, test anchors on both platforms. MkDocs may handle some edge cases differently.

---

## Automated Count Synchronization

Counts for tests, PRNGs, and editor tools are auto-synced from the codebase. **Never hardcode these counts manually.**

| Metric       | Single Source of Truth                                     |
| ------------ | ---------------------------------------------------------- |
| Test count   | `[Test]`, `[TestCase]`, `[UnityTest]` attributes in Tests/ |
| PRNG count   | IRandom/AbstractRandom implementations in Runtime/Random/  |
| Editor tools | EditorWindow/ScriptableWizard/MenuItem in Editor/          |

**How it works:**

1. `scripts/generate-doc-metadata.ps1` — counts items deterministically from source code
2. `scripts/sync-doc-counts.ps1` — updates all documentation files with correct "X+" display values
3. `scripts/lint-doc-counts.ps1` — validates docs match reality (use `-Check` mode in CI)

**After adding tests, PRNGs, or editor tools**, run: `npm run sync:doc-counts`

---
