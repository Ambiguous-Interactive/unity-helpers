# Skill: Update Documentation

<!-- trigger: docs, documentation, changelog, readme, api | After ANY feature/bug fix/API change | Core -->

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

For markdown formatting and link rules, see [markdown-reference](./markdown-reference.md).

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

- `<summary>` — Brief description (one sentence)
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
| [Skills](./)               | Agent workflow procedures     | Workflow-affecting changes   |

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

### Required Format

**CRITICAL**: Every CHANGELOG entry MUST follow this exact format:

```text
- **Title**: Description text here
        ^
        └── REQUIRED colon after the bold title
```

| ✅ Correct                             | ❌ Wrong                              |
| -------------------------------------- | ------------------------------------- |
| `- **WButton Odin Support**: Added...` | `- **WButton Odin Support** Added...` |
| `- **Cache API**: New cache with...`   | `- **Cache API** New cache with...`   |
| `- **Breaking:** Relational...`        | `- **Breaking** Relational...`        |

**Why this matters**: Consistent formatting makes the CHANGELOG scannable and parseable. The colon separates the feature name from its description.

```markdown
## [Unreleased]

### Added

- **Feature Name**: Brief description of what was added
  - Sub-bullet for additional details

### Fixed

- Fixed [description of fix] in [component/area]

### Changed

- **Breaking**: [description] (if breaking change)
- [description of non-breaking change]

### Improved

- Improved [what] by [how/metric]

### Deprecated

- [feature] is deprecated, use [alternative] instead

### Removed

- Removed [feature/capability]
```

### Section Order

1. Added — New features (user-facing additions)
2. Fixed — Bug fixes
3. Improved — Enhancements to existing features
4. Changed — Changes to existing functionality
5. Deprecated — Features marked for future removal
6. Removed — Features removed in this version
7. Security — Security-related fixes

### Writing Good CHANGELOG Entries

**Keep entries SHORT. One or two sentences. No fluff.**

A CHANGELOG entry answers one question for a user skimming a release: _what changed for me, and
do I need to do anything?_ It is not a design document, not an RCA, and not a commit message.

Hard rules:

- **One or two sentences per entry.** If it needs three, the extra detail belongs in `docs/`.
- **Lead with the user-visible effect**, not the mechanism. "Values are no longer dropped"
  beats "the backing array is now serialized as…".
- **No root-cause narration.** Never explain Unity internals, dedup order, why the old code was
  wrong, or what you measured. That is what the commit body and `docs/` are for.
- **No process detail.** No run IDs, version probes, file paths, editor internals, or
  "verified on…".
- **Link, don't inline.** Anything longer goes in a `docs/` guide with a link.
- **Plain language.** Write for a user of the package, not for a maintainer of it.

| ✅ GOOD                                                                                                                                            | ❌ BAD                                                                                                                                                                                    |
| -------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **WButton Odin Support**: WButton now works with Odin Inspector types                                                                              | Added Odin support                                                                                                                                                                        |
| Fixed null reference in SerializableDictionary drawer on Unity 2021                                                                                | Fixed bug                                                                                                                                                                                 |
| Improved QuadTree query performance by 40% for large datasets                                                                                      | Made QuadTree faster                                                                                                                                                                      |
| **Dictionary values no longer silently dropped**: collection-valued dictionaries now report the problem in the Inspector instead of saving nothing | Unity does not serialize a nested collection, so `SerializableDictionary<TKey, List<TValue>>` wrote its keys array and no values array at all, leaving assets that looked authored while… |

The bad example in the last row is accurate and well written. It is still wrong for a CHANGELOG,
because a user skimming a release does not need the mechanism — only the effect and the remedy.

---

## XML Documentation Standards

### Required Elements

```csharp
/// <summary>
/// Brief description of the type or member (one sentence).
/// </summary>
/// <remarks>
/// Additional details, usage notes, or important caveats.
/// </remarks>
/// <typeparam name="T">Description of type parameter.</typeparam>
/// <param name="paramName">Description of parameter.</param>
/// <returns>Description of return value.</returns>
/// <exception cref="ArgumentNullException">When <paramref name="paramName"/> is null.</exception>
/// <example>
/// <code>
/// var result = MyMethod(input);
/// </code>
/// </example>
public T MyMethod<T>(string paramName) { }
```

### XML Doc Guidelines

1. **Summary is mandatory** for all public types and members
2. **Keep summary concise** — One sentence, ideally under 100 characters
3. **Document exceptions** — List all exceptions that can be thrown
4. **Use `<paramref>` and `<typeparamref>`** — For referencing parameters
5. **Avoid redundancy** — Don't repeat the method name in the summary
6. **Use `<inheritdoc/>`** — When implementing interfaces or overriding

---

## Quality Requirements

### MANDATORY Standards

1. **Accuracy** — All code samples MUST compile and run correctly
2. **Clarity** — Clear, direct language; no unnecessary jargon
3. **Conciseness** — Say what needs to be said, nothing more
4. **Front-loaded** — Important information comes first
5. **Completeness** — Cover all parameters, return values, edge cases
6. **Examples** — Every public API needs at least one usage example
7. **Versioning** — New features include "Added in vX.Y.Z"

### Code Sample Requirements

```csharp
// ✅ GOOD: Correct, complete, compilable
using WallstopStudios.UnityHelpers.Core.Random;

IRandom random = PRNG.Instance;
int value = random.NextInt(0, 100);

// ❌ BAD: Incomplete, wrong namespace, won't compile
var random = new PRNG();  // Wrong: PRNG.Instance is correct
int value = random.Next();  // Wrong: Method is NextInt()
```

---

## Documentation Checklist

### Master Checklist (All Changes)

- [ ] All affected markdown docs updated
- [ ] XML docs on all public API members
- [ ] Code samples compile and run correctly
- [ ] CHANGELOG entry added
- [ ] Version info included for new features
- [ ] No broken links
- [ ] Technical terms defined when first used

### For New Features

- [ ] Feature documentation in `docs/features/<category>/`
- [ ] XML documentation on all public types/members
- [ ] At least one working code sample in docs
- [ ] CHANGELOG entry in `### Added` section
- [ ] "Added in vX.Y.Z" version annotation included

### For Bug Fixes

- [ ] CHANGELOG entry in `### Fixed` section
- [ ] Documentation corrected if it described wrong behavior
- [ ] Code samples fixed if they demonstrated the bug

### For API Changes

- [ ] All documentation referencing old API updated
- [ ] Migration notes if breaking change
- [ ] CHANGELOG entry (in `### Changed` or `### Breaking Changes`)
- [ ] **Breaking**: prefix used in CHANGELOG for breaking changes

---

## Common Documentation Mistakes

| Mistake                             | Why It's Wrong                                |
| ----------------------------------- | --------------------------------------------- |
| Copy-paste code without testing     | Leads to broken examples users can't run      |
| "See code for details"              | Users shouldn't need to read source           |
| Outdated parameter names            | Causes confusion when code doesn't match docs |
| Missing edge case documentation     | Users hit unexpected behavior                 |
| Version info missing                | Users don't know if feature exists            |
| Absolute paths (`/unity-helpers/…`) | Breaks CI validation and local preview        |

---

## Quick Reference Commands

```bash
# Validate documentation links
npm run lint:docs

# Format markdown
npm run format:md

# Check all documentation formatting
npm run validate:content

# Check spelling
npm run lint:spelling
```

---

## Skills That MUST Trigger Documentation Updates

The following skills involve customer-visible changes and MUST be followed by documentation updates:

| Skill                                                               | Documentation Required                       |
| ------------------------------------------------------------------- | -------------------------------------------- |
| [create-csharp-file](./create-csharp-file.md)                       | CHANGELOG, XML docs, feature docs            |
| [create-scriptable-object](./create-scriptable-object.md)           | CHANGELOG, XML docs, asset type docs         |
| [create-editor-tool](./create-editor-tool.md)                       | CHANGELOG, tool docs, screenshots            |
| [create-property-drawer](./create-property-drawer.md)               | CHANGELOG, attribute docs                    |
| [add-inspector-attribute](./add-inspector-attribute.md)             | CHANGELOG, attribute docs, usage examples    |
| [use-effects-system](./use-effects-system.md) (when extending)      | CHANGELOG, effects system docs               |
| [use-serializable-types](./use-serializable-types.md) (new type)    | CHANGELOG, type docs, serialization examples |
| [use-spatial-structure](./use-spatial-structure.md) (new struct)    | CHANGELOG, data structure docs               |
| [integrate-optional-dependency](./integrate-optional-dependency.md) | CHANGELOG, integration docs                  |

**Rule**: If your change affects what users see, use, or configure, it needs documentation.

---

## Related Skills

- [documentation-consistency](./documentation-consistency.md) — Performance claims, time estimates, emoji prefixes, bullet lists
- [markdown-reference](./markdown-reference.md) — Link formatting, escaping, linting rules
- [validate-before-commit](./validate-before-commit.md) — Pre-commit validation workflow
- [create-csharp-file](./create-csharp-file.md) — New files need XML docs
- [create-test](./create-test.md) — Test files serve as documentation
- [manage-skills](./manage-skills.md) — Creating and maintaining skill files
