# documentation-consistency - Part 3

## Split Content

## Cross-File Consistency Checklist

When updating documentation, verify these items are consistent across ALL documentation files:

### Performance Claims

- [ ] Random generation speed (e.g., "10-15x faster than Unity.Random")
- [ ] Reflection speedup (e.g., "10-100x faster, varies by operation")
- [ ] Spatial query complexity (e.g., "O(log n)")
- [ ] Test count — **auto-synced** via `npm run sync:doc-counts`
- [ ] PRNG count — **auto-synced** via `npm run sync:doc-counts`
- [ ] Editor tool count — **auto-synced** via `npm run sync:doc-counts`

### Feature Descriptions

- [ ] Dependency status (zero/bundled/external)
- [ ] Platform support claims (IL2CPP, WebGL, etc.)
- [ ] Compatibility statements (Unity versions)

### Formatting

- [ ] Time estimates use tilde (~) prefix
- [ ] No redundant prefixes when emoji conveys meaning
- [ ] Consistent performance claim phrasing (prefer "X-Y faster" ranges)
- [ ] Consistent use of bold/emphasis
- [ ] Consistent bullet point style
- [ ] 3+ parallel items use bullet lists, not run-on sentences
- [ ] Anchor links use lowercase-hyphenated format

---

## Files to Cross-Reference

When making documentation changes, check these files for consistency. Do NOT edit
[docs/readme](../../docs/readme.md): it is generated from [README](../../README.md) by
`npm run sync:readme-mirror` and `npm run lint:readme-mirror` fails on drift
([#593](https://github.com/Ambiguous-Interactive/unity-helpers/issues/593)).

| File                                                                    | Purpose                     |
| ----------------------------------------------------------------------- | --------------------------- |
| [README](../../README.md)                                               | Root project readme         |
| [docs/readme](../../docs/readme.md)                                     | Generated from README       |
| [docs/index](../../docs/index.md)                                       | Documentation site homepage |
| [docs/overview/getting-started](../../docs/overview/getting-started.md) | Onboarding guide            |
| [llms.txt](../../llms.txt)                                              | LLM-friendly summary        |

---

## Common Mistakes

| Mistake                                | Impact                               | Fix                                         |
| -------------------------------------- | ------------------------------------ | ------------------------------------------- |
| Different performance numbers in files | Undermines credibility               | Use exact same numbers everywhere           |
| Missing tilde on time estimates        | Implies false precision              | Add ~ prefix to all estimates               |
| Run-on sentences with multiple claims  | Hard to scan, reduces comprehension  | Break into bullet points or short sentences |
| Redundant statements                   | Wastes reader time, looks unpolished | Consolidate to single clear statement       |
| Contradictory dependency claims        | Confuses users about requirements    | Clarify bundled vs external status          |
| Redundant prefix with emoji            | Cluttered, unprofessional            | Remove prefix when emoji conveys meaning    |
| "Up to X" instead of range             | Hides lower bound, inconsistent      | Use "X-Y faster" range format               |
| Wrong anchor format                    | Broken links, 404 errors             | Use lowercase-hyphenated format             |
| Inline list in sentence                | Hard to scan                         | Convert 3+ items to bullet list             |

---

## Related Skills

- [update-documentation](../skills/update-documentation.md) — When and how to update docs
- [markdown-reference](../skills/markdown-reference.md) — Markdown formatting rules
- [validate-before-commit](../skills/validate-before-commit.md) — Pre-commit validation
