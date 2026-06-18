# Skill: Validate Before Commit

<!-- trigger: validate, commit, lint, check, verify, spell, cspell, spelling | Before completing any task (run linters!) | Core -->

**Trigger**: **MANDATORY** before completing any task that modifies code or documentation.

---

## When to Use

Use this skill for pre-commit validation:

- Before completing any coding task
- Before asking user to review changes
- Before any discussion of "done" or "complete"
- After making ANY modifications to files

For detailed linter commands and configurations, see [linter-reference](./linter-reference.md).
For troubleshooting common errors, see [validation-troubleshooting](./validation-troubleshooting.md).

---

## Quick Reference

```bash
# Fast changed-file preflight (MANDATORY before marking task complete)
npm run agent:preflight:fix

# Run all validations before pushing
npm run validate:prepush
```

Use `agent:preflight:fix` continuously while working to catch hook-class failures early on changed files.
Use `validate:prepush` for full CI parity before push.

**C#/tests/JSON/YAML/skill/CHANGELOG edits: run `npm run lint:spelling`** — cspell covers every file matching its `files` glob, not just Markdown. See [Rule 4: Spell-Check EVERY Change cspell Covers](#rule-4-spell-check-every-change-cspell-covers) for the failure-recovery decision tree. To add a new word: `npm run lint:spelling:add -- <bucket> <word>`.

---

## The Golden Rules

### Rule 0: Preflight Before Completion

Run `npm run agent:preflight:fix` before declaring a task complete.

Hooks are a last-resort safety net. Do not rely on hook-time auto-fixes as the normal workflow.

This catches hook-class failures early for changed files:

- Missing Unity `.meta` files on changed paths
- Unstaged Unity `.meta` companions for currently staged source files
- Spelling regressions in changed markdown files (`.md`, `.markdown`)
- Skill/context files approaching hard size limits
- LLM index/trigger drift when `.llm/` files changed
- Test-lint regressions with auto-fix for Unity null assertions

After creating any file/folder under Unity meta-required roots (`Runtime/`, `Editor/`, `Tests/`, `Samples~/`, `Shaders/`, `Styles/`, `URP/`, `docs/`, `scripts/`):

1. Generate `.meta` immediately with `./scripts/generate-meta.sh <path>`.
2. Run `npm run agent:preflight:fix` before continuing work.

Run `agent:preflight:fix` after staging candidate files (right before commit prep) so staged `.meta` companion drift is corrected before hooks run.
By default (no `-Paths`), preflight validates all changed files from git; passing `-Paths` scopes checks to those targets.

Preferred commit prep order:

1. Stage candidate files.
2. Run `npm run agent:preflight:fix`.
3. Resolve any reported issues.
4. Commit (hooks should only catch unexpected regressions).

### Rule 1: Format IMMEDIATELY After Every Change

**Do NOT batch formatting at the end of a task.** Format immediately after each file modification.

| File Type       | Formatter | Command                                          |
| --------------- | --------- | ------------------------------------------------ |
| C# (`.cs`)      | CSharpier | `dotnet tool run csharpier format .`             |
| Everything else | Prettier  | `node scripts/run-prettier.js --write -- <file>` |

### Rule 2: Run Linters IMMEDIATELY After Every Change

**Do NOT wait until task completion.** Run the appropriate linter after each file modification and fix issues before proceeding.

### Rule 3: Fix Before Moving On

1. Make a change to a file
2. Run the appropriate linter(s) for that file type
3. Fix any issues found
4. Only then move to the next file or task

---

## Common Mistakes

**Wrong** (batching until end):

1. Edit markdown file
2. Edit C# file
3. Edit YAML file
4. ... more edits ...
5. Run all formatters/linters at the end

**Correct** (format immediately after each):

1. Edit markdown -> `node scripts/run-prettier.js --write -- <file>` -> `npm run lint:markdown`
2. Edit C# -> `dotnet tool run csharpier format .`
3. Edit YAML -> `node scripts/run-prettier.js --write -- <file>` -> `npm run lint:yaml`
4. Edit test file -> `pwsh -NoProfile -File scripts/lint-tests.ps1` -> `dotnet tool run csharpier format .`

For detailed workflow patterns and more examples, see [formatting](./formatting.md).

---

## Workflow by File Type

### Rule 4: Spell-Check EVERY Change cspell Covers

**MANDATORY, NOT just for docs.** cspell's `files` glob in [cspell.json](../../cspell.json) covers every file extension that pre-commit and agent preflight spell-check:

- Markdown: `**/*.{md,markdown}` (docs tree, root README/CHANGELOG/PLAN/AGENTS/CLAUDE, LLM instruction tree, GitHub templates)
- C#: `**/*.cs` (every source file under `Runtime/`, `Editor/`, `Tests/`, samples, and scripts)
- YAML: `**/*.{yml,yaml}` (workflows, yamllint config, any config YAML)
- JSON-family: `**/*.{json,jsonc,asmdef,asmref}` (package.json, `.asmdef`/`.asmref`, tool configs)
- JavaScript: `**/*.js` (scripts/ helpers, tests, hook scripts)

The `cspell.json` `files` glob and agent-preflight's pass-through list are kept in lock-step by `scripts/tests/test-cspell-hook-files-parity.sh` (run via `npm run validate:cspell-files-parity`). If you see drift, fix `cspell.json`'s `files` glob -- never narrow agent-preflight's pass-through.

If you modified ANY file in that set -- C# sources, tests, CHANGELOG, skill files, docs, YAML, JSON, `.asmdef`/`.asmref`, `.js` scripts -- you MUST run `npm run lint:spelling` before declaring work complete. `npm run agent:preflight` checks the same changed-file set before hooks are involved, and `npm run validate:prepush`/CI run full spelling validation. Do NOT mentally gate "this is a code change, no spelling matters" -- cspell lints identifiers in comments, XML docs, and log strings, which is where most typos actually land.

A Claude Code PostToolUse hook (`scripts/hooks/cspell-post-edit.js`, registered in the tracked [`.claude/settings.json`](../../.claude/settings.json)) auto-runs cspell after every Edit/Write/MultiEdit/NotebookEdit. The hook ships with the repo via `$CLAUDE_PROJECT_DIR`, so teammates and fresh clones inherit it automatically -- there is no per-dev setup to forget. If you skip running `npm run lint:spelling` manually, the PostToolUse hook surfaces the feedback immediately instead of waiting for validation at push prep.

PostToolUse semantics: the edit has ALREADY happened when the hook fires. Claude Code's docs ([hooks reference](https://code.claude.com/docs/en/hooks)) say exit 2 on PostToolUse surfaces stderr to Claude (the model sees it and can fix in a follow-up edit) -- it does NOT undo the edit. The hook therefore acts as fast feedback, not a gate. Fix reported issues before moving to the next file, just as you would if you had run the linter manually.

Treat the hook as a SAFETY NET, not a substitute for manual validation. It does NOT fire when:

- CI runs (the hook is Claude Code specific).
- You edit files outside Claude Code (another IDE, scripted edits, `git rebase -i` edits).
- Node or the repo-local cspell package is missing (fresh clones before `npm install` degrade silently -- run `npm install` to activate).
- The hook itself is disabled locally (see below).

For those scenarios -- and as a defense-in-depth check before declaring work complete -- run `npm run lint:spelling` manually. Manual invocation before completion remains the expectation.

To disable the hook temporarily (for noisy refactors, debugging the hook itself, or cspell upgrades), create `.claude/settings.local.json` (gitignored) with:

```json
{ "hooks": { "PostToolUse": [] } }
```

The local file overrides the shared one. Delete it when done to re-enable.

Failure-recovery decision tree (when cspell reports `Unknown word`):

1. Is it a typo? Fix the source file. Done.
2. Is it a valid term already in a dictionary, just in a different case? cspell is case-insensitive here, so this should not happen — re-read the error.
3. Is it a valid term missing from the dictionary? Pick the right bucket using [linter-reference](./linter-reference.md#adding-words-to-dictionary):
   - Unity engine API → `unity-terms`
   - C# language / BCL type → `csharp-terms`
   - This package's public symbol → `package-terms`
   - General programming/tooling → `tech-terms`
   - Lint-error-code prefix (e.g. `UNH`, `PWS`) → root `words`
   - Project-specific, none of the above → root `words`
4. To add a word, prefer the helper script over editing `cspell.json` by hand:

   ```bash
   npm run lint:spelling:add -- <bucket> <word> [<word>...]
   # Example: npm run lint:spelling:add -- tech-terms reentrant reentrantly
   ```

   Buckets: `unity-terms`, `csharp-terms`, `package-terms`, `tech-terms`, `words` (root). The helper deduplicates, validates JSON round-trip, and rejects cross-bucket duplicates.

5. After editing `cspell.json`, re-run `npm run lint:spelling` AND `npm run lint:spelling:config` to catch case-redundant and cross-dictionary duplicates.

### C# Changes

```bash
# After EVERY .cs file modification (even single-line edits):
dotnet tool run csharpier format .
npm run lint:csharp-naming
npm run lint:spelling    # 🚨 MANDATORY — cspell lints C# comments/XML-doc/log-strings
```

Also verify license headers on new or modified files — see [license-headers](./license-headers.md).

### Documentation Changes

```bash
# After EVERY .md file modification:
node scripts/run-prettier.js --write -- <file>
npm run lint:spelling    # 🚨 #1 CI failure cause!
npm run lint:docs         # Validates links
npm run lint:markdown     # Structural rules
```

### CHANGELOG or Project JSON Changes

```bash
# After EVERY CHANGELOG.md / package.json / asmdef / asmref edit:
node scripts/run-prettier.js --write -- <file>
npm run lint:spelling    # 🚨 validate:prepush/CI spell-check CHANGELOG + JSON
```

### YAML Changes

```bash
# After EVERY .yml/.yaml file modification:
node scripts/run-prettier.js --write -- <file>
npm run lint:yaml

# For workflow files (.github/workflows/*.yml), also run:
actionlint
```

### Test File Changes

```bash
# 🚨 MANDATORY: After EVERY test file modification:
pwsh -NoProfile -File scripts/lint-tests.ps1

# Recommended fast-path (runs test lint + safe auto-fixes on changed files):
npm run agent:preflight:fix

# Also run standard C# formatting:
dotnet tool run csharpier format .
npm run lint:csharp-naming
npm run lint:spelling    # 🚨 MANDATORY — cspell lints test comments + strings
```

**CRITICAL**: The test linter is **MANDATORY** for any test file changes (files in `Tests/` directory). You **MUST** run it **IMMEDIATELY** after each test file modification — do NOT batch these checks at the end of your task.

**Why this matters**: Test lifecycle lint failures block `agent:preflight`, `validate:prepush`, and CI. Catching and fixing these issues early (after each file change) prevents frustrating failures when you prepare to push.

### Assembly Definition Changes (`.asmdef`)

```bash
# 🚨 MANDATORY: After EVERY .asmdef file creation or modification:
pwsh -NoProfile -File scripts/lint-asmdef.ps1

# Also run standard JSON formatting:
node scripts/run-prettier.js --write -- <file>
```

**CRITICAL**: The asmdef linter checks that assemblies with `overrideReferences: true` referencing `WallstopStudios.UnityHelpers` include `Sirenix.Serialization.dll` in `precompiledReferences`. Missing this causes CS0012/CS0311 compilation errors when Odin Inspector is installed. See [manage-assembly-definitions](./manage-assembly-definitions.md) for the standard template.

### Skill File and Context Changes (`.llm/skills/*.md`, [context](../context.md))

```bash
# 🚨 MANDATORY: After EVERY skill file or context.md modification:
npm run lint:spelling
pwsh -NoProfile -File scripts/lint-skill-sizes.ps1

# Recommended strict changed-file check (fails on critical near-limit sizes):
npm run agent:preflight

# Also run standard markdown formatting:
node scripts/run-prettier.js --write -- <file>
npm run lint:markdown
```

**CRITICAL**: Skill files and [context](../context.md) have a **500-line hard limit** enforced by the pre-commit hook. Files exceeding this limit **CANNOT be committed** and require human judgment to split or reduce.

`agent:preflight` treats critical near-limit sizes as failures for changed files, so growth pressure is addressed before the pre-commit hook becomes the final stop.

| Lines   | Action Required                                          |
| ------- | -------------------------------------------------------- |
| <300    | No action needed                                         |
| 300-500 | Consider splitting preemptively to avoid future blockers |
| >500    | **MUST split before commit** — hook will reject the file |

**Why this matters**: Splitting large skill files requires human judgment (deciding topic boundaries, updating cross-references). Catching size issues early prevents blocking commits when you've completed all other work.

### LLM Instructions Changes ([LLM context](../context.md), skills index)

```bash
# 🚨 MANDATORY: After ANY change to .llm/context.md, a skill trigger, or the index:
pwsh -NoProfile -File scripts/lint-llm-instructions.ps1

# Auto-fix mode (regenerates .llm/skills/index.md):
pwsh -NoProfile -File scripts/lint-llm-instructions.ps1 -Fix
```

**CRITICAL**: The skills index is the generated [Skills Index](./index.md) file (linked from the [LLM context file](../context.md)), NOT an embedded block. It is byte-for-byte deterministic across OS (ordinal sort, UTF-8 no BOM, LF) and Prettier-ignored — regenerate it with the generator, never hand-edit it. Trigger descriptions MUST be ASCII (use `-`, not an em-dash); a non-ASCII trigger is the cross-OS drift class the lint rejects. The lint also verifies the context file keeps exactly one H1 and links to the index.

**Tests**: Run `pwsh -NoProfile -File scripts/tests/test-llm-instructions-lint.ps1` to verify the lint script itself (test cases covering generator output validation, lint correctness, H1/H2 detection, and pattern matching).

---

## What Gets Validated

The `npm run validate:prepush` command runs these checks:

1. **validate:content** — Documentation and formatting
   - `lint:docs` — Markdown links (no backtick `.md` refs)
   - `lint:markdown` — Markdownlint rules
   - `format:md:check` — Prettier markdown formatting
   - `format:json:check` — Prettier JSON/asmdef formatting
   - `format:yaml:check` — Prettier YAML formatting
   - `validate:lint-error-codes` — cspell coverage for every `^[A-Z]{2,}\d{3}$` prefix emitted by `scripts/lint-*.{ps1,js}`, `scripts/tests/test-lint-*.{ps1,js,sh}`, or `.githooks/*`

2. **lint:spelling** — CSpell validation on the repository

3. **eol:check** — Line endings (CRLF, no BOM)

4. **validate:tests** — Test lifecycle lint (Track() usage) and cspell lint-error-code contract regression test

5. **lint:csharp-naming** — C# naming conventions

---

## Critical Link Formatting Rules

For complete link formatting rules, escaping patterns, and linting rules, see [markdown-reference](./markdown-reference.md).

Key requirements:

- **ALL internal links MUST use `./` or `../` prefix** — no bare paths
- **NEVER use backtick-wrapped file references** — use proper markdown links
- **NEVER use absolute GitHub Pages paths** — no `/unity-helpers/...` paths

---

## Documentation Checklist

Before completing ANY task:

### Prettier Self-Check (MANDATORY)

- [ ] Did I run `node scripts/run-prettier.js --write -- <file>` IMMEDIATELY after EVERY non-C# file?
- [ ] Did I verify each file with `node scripts/run-prettier.js --check -- <file>`?
- [ ] Did I check config files too? (`.devcontainer/devcontainer.json`, `package.json`, etc.)
- [ ] Final check: `node scripts/run-prettier.js --check -- .` passes?

### For New Features

- [ ] Feature documentation added/updated
- [ ] XML documentation on all public types/members
- [ ] At least one working code sample
- [ ] CHANGELOG entry in `### Added` section
- [ ] llms.txt updated if feature adds new capabilities

### For Bug Fixes

- [ ] CHANGELOG entry in `### Fixed` section
- [ ] Documentation corrected if it described wrong behavior

### For API Changes

- [ ] All documentation referencing old API updated
- [ ] CHANGELOG entry (in `### Changed`, marked Breaking if applicable)
- [ ] XML docs updated
- [ ] Code samples updated

---

## Pre-Existing Warnings

Some lint warnings may exist in the main branch. Focus on:

1. **New warnings** introduced by your changes
2. **Failing checks** (exit code 1)

If `validate:content` and `lint:csharp-naming` pass, your changes are ready.

---

## CLI Argument Safety

When passing file lists to CLI tools (prettier, markdownlint, yamllint, etc.), ALWAYS use a `--` end-of-options separator before the file arguments.

### Why

Without `--`, a staged filename like `--plugin=./evil.js` or `--config=malicious.yml` would be interpreted as a CLI option, not a filename. This is an option injection vulnerability.

### Pattern

```bash
# WRONG - filenames can be interpreted as options
node scripts/run-prettier.js --write "${FILES[@]}"

# CORRECT - `--` prevents filenames from being treated as options
node scripts/run-prettier.js --write -- "${FILES[@]}"
```

This applies to ALL tools that accept file arguments:

- `node scripts/run-prettier.js --write -- "${FILES[@]}"`
- `node scripts/run-node-bin.js markdownlint --fix --config X -- "${FILES[@]}"`
- `yamllint -c config.yaml -- "${FILES[@]}"`

In PowerShell scripts, add `'--'` to argument arrays before file paths:

```powershell
$cmdArgs = @((Join-Path $repoRoot 'scripts/run-node-bin.js'), 'markdownlint', '--fix', '--config', '.markdownlint.json', '--') + $filePaths
```

---

## Related Skills

- [linter-reference](./linter-reference.md) — Detailed linter commands and configurations
- [validation-troubleshooting](./validation-troubleshooting.md) — Common errors and fixes
- [update-documentation](./update-documentation.md) — Documentation requirements
- [formatting](./formatting.md) — CSharpier, Prettier, markdownlint workflow
- [markdown-reference](./markdown-reference.md) — Link formatting, structural rules
- [create-test](./create-test.md) — Test file requirements
- [test-data-driven](./test-data-driven.md) — Data-driven testing patterns
- [test-naming-conventions](./test-naming-conventions.md) — Naming rules and legacy test migration
- [manage-skills](./manage-skills.md) — Skill file maintenance and index regeneration
- [license-headers](./license-headers.md) — License header requirements and year validation
