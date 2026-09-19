# manage-skills - Part 1

## Split Content

**Trigger**: When creating, modifying, splitting, consolidating, or removing skills in the `.llm/skills/` directory.

---

## When to Use

- Creating a new skill file
- Updating an existing skill's content or structure
- Splitting a large skill into focused skills
- Consolidating related skills
- Removing obsolete skills
- Verifying skill index is current

---

## When NOT to Use

- Adding content to the context file (that's general agent instructions)
- Creating documentation outside `.llm/` (use [update-documentation](../skills/update-documentation.md))
- Writing code samples (use [create-csharp-file](../skills/create-csharp-file.md) in `code-samples/`)

---

## Skill File Structure

Every skill file MUST follow this structure:

```markdown
# Skill: [Title Case Name]

<!-- trigger: keyword1, keyword2 | Short description | Category -->

**Trigger**: When to invoke this skill (one sentence).

---

## When to Use

- Bullet list of situations
- Be specific and actionable

---

## When NOT to Use (recommended)

- Situations where this skill is NOT appropriate
- Helps agents avoid misapplication

---

## [Main Content Sections]

...guidance, examples, rules...

---

## Related Skills

- [related-skill](./related-skill.md) - Brief description
```

---

## Trigger Comment Format

The trigger comment is REQUIRED and parsed by `generate-skills-index.ps1`:

```text
<!-- trigger: keywords | description | category -->
```

| Field         | Purpose                                  | Examples                          |
| ------------- | ---------------------------------------- | --------------------------------- |
| `keywords`    | Comma-separated search terms             | `test, testing, nunit, testcase`  |
| `description` | Brief description for index table        | `Writing or modifying test files` |
| `category`    | One of: `Core`, `Performance`, `Feature` | `Core`                            |

### Category Guidelines

| Category      | Use For                                                           |
| ------------- | ----------------------------------------------------------------- |
| `Core`        | Skills agents should consider for most tasks                      |
| `Performance` | Optimization, profiling, allocation-related skills                |
| `Feature`     | Feature-specific skills (serialization, effects, data structures) |

---

## Size Guidelines

| Lines   | Status   | Action                                          |
| ------- | -------- | ----------------------------------------------- |
| <200    | Ideal    | Focused, easy to consume                        |
| 200-300 | Good     | Acceptable for complex topics                   |
| 300-479 | Large    | Consider splitting if possible                  |
| 480-500 | Critical | Split soon — one edit from hard limit           |
| >500    | Split    | MUST split into focused skills with clear scope |

---

## Content Rules

### No Duplication

Reference other skills instead of duplicating content:

```markdown
<!-- WRONG -->

All code must follow zero-allocation patterns:

- Use Buffers<T>.List for temporary collections
- Use ArrayPool for temporary arrays
  ...100 lines of detail...

<!-- CORRECT -->

All code must follow [high-performance-csharp](./high-performance-csharp.md).
```

### No Conflicts with Context

Skills provide procedural detail; the context file provides project rules. If a skill contradicts the context, update the skill.

### Code Examples

- **Inline**: Short examples (under 20 lines) can stay in the skill
- **External**: Longer examples go in `code-samples/` folder

```markdown
See [patterns/zero-alloc-iteration.cs](../code-samples/patterns/zero-alloc-iteration.cs).
```

### Reference Tables

Reusable tables go in `references/` folder:

```markdown
See [forbidden-patterns reference](../references/forbidden-patterns.md).
```

---

## Naming Conventions

| Rule                 | Example                              |
| -------------------- | ------------------------------------ |
| lowercase-kebab-case | create-test, use-pooling             |
| verb-noun pattern    | create-X, use-X, avoid-X             |
| Descriptive verbs    | create, use, avoid, debug, integrate |

### Common Verb Prefixes

| Prefix       | Use For                             |
| ------------ | ----------------------------------- |
| `create-`    | Creating new files, types, assets   |
| `use-`       | Using existing features or patterns |
| `avoid-`     | Anti-patterns, forbidden practices  |
| `debug-`     | Debugging specific issues           |
| `integrate-` | Third-party integration patterns    |
| `test-`      | Testing-specific procedures         |

---

## Index Maintenance

After ANY skill change, regenerate the standalone index:

```bash
pwsh -NoProfile -File scripts/generate-skills-index.ps1
```

This rewrites the generated [Skills Index](../skills/index.md) catalog (linked from the
[LLM context file](../context.md)). The file is byte-for-byte deterministic across every OS
(ordinal sort, UTF-8 without BOM, LF line endings) and is excluded from Prettier — never
hand-edit it; always regenerate. Trigger descriptions MUST be ASCII (use `-`, not an
em-dash, and straight quotes): a non-ASCII description is the cross-OS drift class, and
`scripts/lint-llm-instructions.ps1` rejects it.

---
