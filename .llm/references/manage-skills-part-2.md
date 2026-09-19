# manage-skills - Part 2

## Split Content

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
2. Add required structure (see [Skill File Structure](../skills/manage-skills.md#skill-file-structure))
3. Add trigger comment with keywords, description, category
4. Add "When to Use" section
5. Add "When NOT to Use" section (recommended)
6. Add main content sections
7. Add "Related Skills" section
8. Keep the canonical file below 200 lines; use routed references for longer content.
9. Regenerate discovery stubs: `node scripts/generate-agent-skill-stubs.mjs --write`
10. Regenerate index: `pwsh -NoProfile -File scripts/generate-skills-index.ps1`
11. Run `npm run lint:llm`, `npm run lint:markdown`, and `npm run lint:docs`.

---

## Splitting a Large Skill

When a skill reaches 200 lines:

1. Identify distinct subtopics
2. Create focused skills for each subtopic
3. Update original to be an overview with links
4. Update all cross-references
5. Regenerate index

For an existing skill with a router and `*-part-*.md` references, edit its reference files in `.llm/references/` and run `node scripts/migrate-agent-skills.mjs --refresh`. This updates the router's section links and the reconstruction digest. Edit the router's title or trigger directly if needed; the refresh preserves them. The command rejects other router edits so content cannot disappear silently. Use `--check` to verify the refreshed split.

For a new oversized skill, run `node scripts/migrate-agent-skills.mjs --write` to create routed references, then regenerate the discovery stubs and skills index.

Example split:

- test-patterns (600 lines) split into:
  - [create-test](../skills/create-test.md) - Test file creation
  - [test-odin-drawers](../skills/test-odin-drawers.md) - Odin drawer testing
  - [test-unity-lifecycle](../skills/test-unity-lifecycle.md) - Unity object lifecycle
  - [investigate-test-failures](../skills/investigate-test-failures.md) - Failure analysis

---

## Removing a Skill

1. Search for all references: `rg "skill-name" .llm/`
2. Update or remove all references
3. Delete the skill file
4. Regenerate index
5. Regenerate discovery stubs with `node scripts/generate-agent-skill-stubs.mjs --write`; it removes obsolete generated directories.
6. Verify no broken links: `npm run lint:docs`

---

## Skill Editing Workflow (MANDATORY)

**CRITICAL**: Always follow this workflow when editing skill files. Size issues discovered at commit time require human judgment to fix.

```text
1. Edit the canonical skill or its routed references
2. Run size linter IMMEDIATELY: pwsh -NoProfile -File scripts/lint-skill-sizes.ps1
3. At 200 lines, split before continuing; use `--write` for a new split
4. After editing split references, run `node scripts/migrate-agent-skills.mjs --refresh`
5. Regenerate discovery stubs and the skills index
6. Format canonical files; generated references retain exact reconstruction bytes
7. Run `npm run lint:llm`, `npm run lint:markdown`, and `npm run lint:docs`
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
2. If a file has 200 or more lines, **MUST split before committing** — pre-commit hook will reject
3. Run `node scripts/migrate-agent-skills.mjs --check` and `node scripts/generate-agent-skill-stubs.mjs --check`

---

## Related Skills

- [update-documentation](../skills/update-documentation.md) - Documentation requirements
- [validate-before-commit](../skills/validate-before-commit.md) - Pre-commit validation workflow
- [formatting](../skills/formatting.md) - Prettier formatting for markdown files
- [markdown-reference](../skills/markdown-reference.md) - Link formatting, structural rules
