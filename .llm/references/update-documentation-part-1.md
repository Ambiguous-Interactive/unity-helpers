# update-documentation - Part 1

## Split Content

**Trigger**: **MANDATORY** after ANY feature addition, bug fix, API change, or user-facing modification.

---

## When to Use

This skill applies **every time** you make changes to the codebase. Documentation is a first-class deliverable—incomplete documentation = incomplete work.

**CHANGELOG is for USER-FACING changes ONLY.** Internal changes like CI/CD workflows, build scripts, dev tooling, GitHub Actions, and infrastructure do NOT belong in the CHANGELOG.

| Trigger                       | Required?    | Documentation Scope                               |
| ----------------------------- | ------------ | ------------------------------------------------- |
| **After ANY feature add**     | **REQUIRED** | Docs, XML docs, code samples, CHANGELOG           |
| **After ANY bug fix**         | **REQUIRED** | CHANGELOG, fix any docs describing wrong behavior |
| **After ANY API change**      | **REQUIRED** | All affected docs, XML docs, samples, CHANGELOG   |
| **After ANY behavior change** | **REQUIRED** | All affected docs, migration notes if needed      |
| **CI/CD, build scripts**      | N/A          | **NO CHANGELOG** — internal, not user-facing      |
| **Dev tooling, workflows**    | N/A          | **NO CHANGELOG** — internal infrastructure        |

For markdown formatting and link rules, see [markdown-reference](../skills/markdown-reference.md).

**Docs prose uses Simplified Technical English** -- the same bar as pull
requests ([ship-changes](../skills/ship-changes.md#step-9b-open-the-pull-request-yourself)):
common words, short sentences (one idea each), active voice, no filler. A docs
sentence exists to answer **why** or **how**, and the shortest true sentence
wins. When touching a page, cut the filler you find around your change; do not
rewrite the whole page in the same pass.

---

## Documentation Types

### 1. Markdown Documentation (`docs/` folder)

| Content Type              | Location                    | When to Update                      |
| ------------------------- | --------------------------- | ----------------------------------- |
| Feature documentation     | `docs/features/<category>/` | New features, API changes           |
| Usage guides              | `docs/guides/`              | New features, workflow changes      |
| API reference             | `docs/features/`            | API additions, modifications        |
| Performance documentation | `docs/performance/`         | Performance changes, new benchmarks |

### 2. XML Documentation Comments (inline `///`)

Required on ALL public types, methods, properties, and fields.

**Required elements**:

- `<summary>` — **Minimal, on every public member.** One short sentence. This is a public
  library, so a consumer reads the API surface without the source; keep it brief rather than
  dropping it. `<remarks>` is the part that is only when needed
- `<param>` — Every parameter documented
- `<returns>` — Return value documented
- `<exception>` — All thrown exceptions listed
- `<remarks>` — Additional details, caveats (when needed)
- `<example>` — Usage example (at least one per public API)

### 3. Code Samples

- **MUST be compilable** — Test before committing
- **MUST demonstrate typical usage** — Show common patterns
- **Include error handling** — Where appropriate
- **Self-contained** — No undeclared dependencies

### 4. Other Documentation

| Location                   | Purpose                       | When to Update               |
| -------------------------- | ----------------------------- | ---------------------------- |
| [llms.txt](../../llms.txt) | LLM-friendly package overview | New capabilities, major APIs |
| [README](../../README.md)  | Quickstart and overview       | Significant new features     |
| [Skills](../skills)               | Agent workflow procedures     | Workflow-affecting changes   |

---

## CHANGELOG Format

The CHANGELOG follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format.

### User-Facing Changes ONLY

**CRITICAL**: The CHANGELOG documents changes that affect USERS of the package.

| ❌ Exclude from CHANGELOG           | Why                                |
| ----------------------------------- | ---------------------------------- |
| CI/CD workflows (GitHub Actions)    | Internal build/test infrastructure |
| Build scripts, dev tooling          | Users don't interact with these    |
| Documentation deployment automation | Internal infrastructure            |
| Test infrastructure                 | Users don't run the test suite     |
| Internal implementation details     | Users don't care about internals   |

| ✅ Include in CHANGELOG              | Why                                   |
| ------------------------------------ | ------------------------------------- |
| New runtime features/classes/methods | Users can use these in their projects |
| Bug fixes in runtime/editor code     | Affects user experience               |
| API changes, breaking changes        | Users need to update their code       |
| Performance improvements             | Users benefit from faster execution   |
| New inspector attributes/drawers     | Users see these in Unity Editor       |

### NEVER Modify Released Notes

**CRITICAL**: Once a version is released, its CHANGELOG entries are **immutable**.

- ✅ **DO**: Add new entries to `## [Unreleased]` section only
- ❌ **NEVER**: Edit entries under versioned headings like `## [3.0.5]`
- ❌ **NEVER**: "Clean up" or "improve" wording in released notes

### Unreleased Features: Edit In Place

**CRITICAL**: If a feature is still in the `[Unreleased]` section and you're modifying it, **edit the existing entry directly** rather than creating new "Fixed" or "Changed" entries.

**Why?** From the user's perspective, unreleased features don't exist yet. There's nothing to "fix" or "change" — it's all part of the same new feature that hasn't shipped.

| Scenario                                      | Correct Action                                        |
| --------------------------------------------- | ----------------------------------------------------- |
| Bug in unreleased feature                     | Edit the feature's entry to describe correct behavior |
| Behavior change in unreleased feature         | Update the feature's entry with new behavior          |
| Performance improvement in unreleased feature | Update the feature's entry to reflect final perf      |
| API change in unreleased feature              | Update the feature's entry with correct API           |

**Example**:

```markdown
## [Unreleased]

### Added

- **Sprite Sheet Extractor**: New tool for extracting individual sprites from sprite sheets
  - Auto-detection algorithm strongly prefers transparent boundaries ← Edit this line when fixing algorithm
  - Preview size changes update immediately without breaking ← Edit this line when fixing preview
```

❌ **WRONG** — Creating separate entries for unreleased feature issues:

```markdown
## [Unreleased]

### Added

- **Sprite Sheet Extractor**: New tool for extracting individual sprites

### Fixed

- Fixed Sprite Sheet Extractor algorithm selecting non-transparent boundaries ← NO! Feature isn't released yet
- Fixed preview breaking when changing size ← NO! Nothing to "fix" for users
```

**Exception**: If a _released_ version introduced a bug and the fix is in `[Unreleased]`, then a "Fixed" entry is appropriate because users experienced the bug.

**A fix for a defect that has never been in a release is not a `Fixed` entry.** The feature ships
correct, so fold whatever the fix guarantees a user (a limit, an API to call, a promise) into that
feature's `Added` entry, and drop the bug narration. The same test applies to a `Changed`,
`Improved` or `Security` entry whose "before" is behavior no release ever had.

**Decide with git, never memory** — the subject shipped only if it existed at the last release tag:

```bash
git tag --sort=-v:refname | head -1                       # last release tag
git ls-tree -r --name-only <tag> -- <path>                # did the file exist?
git show <tag>:<path> | rg <symbol>                       # did the symbol exist?
```
