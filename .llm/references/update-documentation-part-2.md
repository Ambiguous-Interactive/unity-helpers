# update-documentation - Part 2

## Split Content

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

- **One or two sentences per entry**, and at most **300 rendered characters**. `npm run lint:changelog`
  fails a longer one; issue references and link targets do not count toward the limit. If it needs
  three sentences, the extra detail belongs in `docs/`.
- **Start with the verb its section names** — `Add` under `### Added`, `Fix` under `### Fixed`.
  A bold title is optional; when you use one it must be followed by a colon (see
  [Required Format](../skills/update-documentation.md#required-format)).
- The limit is checked in `[Unreleased]` only, because released notes are immutable
  (see [NEVER Modify Released Notes](../skills/update-documentation.md#never-modify-released-notes)). Every entry passes through it
  while unreleased, so a released section is already compliant when it is frozen.
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
