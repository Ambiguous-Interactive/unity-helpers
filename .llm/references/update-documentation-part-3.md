# update-documentation - Part 3

## Split Content

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

### A sample that stands alone says so, and a compiler checks it

`npm run lint:doc-samples` extracts every `csharp` block carrying an opt-in marker and compiles it
against the real `Runtime/**`, so a sample naming a type or a member that does not exist fails a
gate rather than reading as correct forever
([#611](https://github.com/Ambiguous-Interactive/unity-helpers/issues/611)). It runs inside
`npm run typecheck:unity`.

````markdown
<!-- doc-sample: compiles -->

```csharp
[WProtoContract]
public partial class Player
{
    [WProtoMember(1)]
    public int Level;
}
```
````

Mark a sample when it stands alone. Two shapes work: a block declaring a type of its own, and a
block that is a set of MEMBERS, which is wrapped in a `MonoBehaviour` because that is what its prose
says it decorates. Leave a continuation unmarked -- a subtype whose base was declared in the block
above, an example that uses a type the reader supplies -- because it cannot compile on its own and
saying it can is the only way this gate lies. **Measured: 103 of the tree's 280 declaration-shaped
blocks stand alone**, which is why the marker is opt-in rather than an opt-out.

A marked block that carries an elision (`...`), or an `[assembly: ...]` attribute, is a
contradiction of the claim and is reported by name. The checked count is printed on every run and
asserted by the self-test, so a corpus that stops being scanned is visible rather than silent.

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
| [create-csharp-file](../skills/create-csharp-file.md)                       | CHANGELOG, XML docs, feature docs            |
| [create-scriptable-object](../skills/create-scriptable-object.md)           | CHANGELOG, XML docs, asset type docs         |
| [create-editor-tool](../skills/create-editor-tool.md)                       | CHANGELOG, tool docs, screenshots            |
| [create-property-drawer](../skills/create-property-drawer.md)               | CHANGELOG, attribute docs                    |
| [add-inspector-attribute](../skills/add-inspector-attribute.md)             | CHANGELOG, attribute docs, usage examples    |
| [use-effects-system](../skills/use-effects-system.md) (when extending)      | CHANGELOG, effects system docs               |
| [use-serializable-types](../skills/use-serializable-types.md) (new type)    | CHANGELOG, type docs, serialization examples |
| [use-spatial-structure](../skills/use-spatial-structure.md) (new struct)    | CHANGELOG, data structure docs               |
| [integrate-optional-dependency](../skills/integrate-optional-dependency.md) | CHANGELOG, integration docs                  |

**Rule**: If your change affects what users see, use, or configure, it needs documentation.

---

## Related Skills

- [documentation-consistency](../skills/documentation-consistency.md) — Performance claims, time estimates, emoji prefixes, bullet lists
- [markdown-reference](../skills/markdown-reference.md) — Link formatting, escaping, linting rules
- [validate-before-commit](../skills/validate-before-commit.md) — Pre-commit validation workflow
- [create-csharp-file](../skills/create-csharp-file.md) — New files need XML docs
- [create-test](../skills/create-test.md) — Test files serve as documentation
- [manage-skills](../skills/manage-skills.md) — Creating and maintaining skill files
