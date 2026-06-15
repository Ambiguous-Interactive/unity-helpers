# Skill: Manage Skills

<!-- trigger: skill, skills, create-skill, update-skill, meta | Creating, updating, splitting, consolidating, or removing skills | Core -->

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
- Creating documentation outside `.llm/` (use [update-documentation](./update-documentation.md))
- Writing code samples (use [create-csharp-file](./create-csharp-file.md) in `code-samples/`)

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

This rewrites the generated [Skills Index](./index.md) catalog (linked from the
[LLM context file](../context.md)). The file is byte-for-byte deterministic across every OS
(ordinal sort, UTF-8 without BOM, LF line endings) and is excluded from Prettier — never
hand-edit it; always regenerate. Trigger descriptions MUST be ASCII (use `-`, not an
em-dash, and straight quotes): a non-ASCII description is the cross-OS drift class, and
`scripts/lint-llm-instructions.ps1` rejects it.

---

## Cross-Reference Checklist

When creating or modifying a skill:

- [ ] Add/update trigger comment with keywords, description, category
- [ ] Add "Related Skills" section linking to related skills
- [ ] Update related skills to link back (bidirectional)
- [ ] If skill scope changes, verify context references are accurate
- [ ] Run index generator after changes

---

## AI Agent Compatibility

Skills should work with GitHub Copilot, Claude, Codex, and similar tools.

### Best Practices

| Practice               | Why                                        |
| ---------------------- | ------------------------------------------ |
| Clear trigger keywords | Helps semantic search find relevant skills |
| Explicit "When to Use" | Agents can match task to skill             |
| Actionable guidance    | Commands agents can execute directly       |
| Code examples          | Agents can pattern-match and adapt         |

### Avoid

- Ambiguous language ("sometimes", "maybe", "consider")
- Instructions requiring human judgment only
- References to external resources without context

---

## Creating a New Skill

1. Create file in `.llm/skills/` with verb-noun naming pattern
2. Add required structure (see [Skill File Structure](#skill-file-structure))
3. Add trigger comment with keywords, description, category
4. Add "When to Use" section
5. Add "When NOT to Use" section (recommended)
6. Add main content sections
7. Add "Related Skills" section
8. Format: `node scripts/run-prettier.js --write -- .llm/skills/<skill-name>`
9. Lint: `npm run lint:markdown` and `npm run lint:docs`
10. Regenerate index: `pwsh -NoProfile -File scripts/generate-skills-index.ps1`
11. Run strict changed-file preflight: `npm run agent:preflight`

---

## Splitting a Large Skill

When a skill exceeds 500 lines:

1. Identify distinct subtopics
2. Create focused skills for each subtopic
3. Update original to be an overview with links
4. Update all cross-references
5. Regenerate index

Example split:

- test-patterns (600 lines) split into:
  - [create-test](./create-test.md) - Test file creation
  - [test-odin-drawers](./test-odin-drawers.md) - Odin drawer testing
  - [test-unity-lifecycle](./test-unity-lifecycle.md) - Unity object lifecycle
  - [investigate-test-failures](./investigate-test-failures.md) - Failure analysis

---

## Removing a Skill

1. Search for all references: `rg "skill-name" .llm/`
2. Update or remove all references
3. Delete the skill file
4. Regenerate index
5. Verify no broken links: `npm run lint:docs`

---

## Skill Editing Workflow (MANDATORY)

**CRITICAL**: Always follow this workflow when editing skill files. Size issues discovered at commit time require human judgment to fix and cannot be auto-resolved.

```text
1. Edit the skill file
2. Run size linter IMMEDIATELY: pwsh -NoProfile -File scripts/lint-skill-sizes.ps1
3. If >300 lines: Consider splitting now (easier than at commit time)
4. If >500 lines: STOP — must split before any other work
5. Format: node scripts/run-prettier.js --write -- <file>
6. Lint: npm run lint:markdown && npm run lint:docs
7. Regenerate index: pwsh -NoProfile -File scripts/generate-skills-index.ps1
8. Move to next file
```

**Why check size immediately?** Unlike formatting issues (auto-fixable), oversized skill files require human decisions about:

- How to split content into logical topics
- Which cross-references need updating
- What the new skill files should be named

Discovering this at commit time blocks your entire commit with no quick fix.

---

## Validation Checklist

After creating or modifying any skill file:

1. **MANDATORY**: Run `pwsh -NoProfile -File scripts/lint-skill-sizes.ps1 -VerboseOutput`
2. If >500 lines, **MUST split before committing** — pre-commit hook will reject
3. If >300 lines, consider splitting for readability and future-proofing

---

## Related Skills

- [update-documentation](./update-documentation.md) - Documentation requirements
- [validate-before-commit](./validate-before-commit.md) - Pre-commit validation workflow
- [formatting](./formatting.md) - Prettier formatting for markdown files
- [markdown-reference](./markdown-reference.md) - Link formatting, structural rules
