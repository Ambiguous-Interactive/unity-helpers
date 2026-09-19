# validate-before-commit - Part 2

## Split Content

## Workflow by File Type

### Rule 4: Spell-Check EVERY Change cspell Covers

**MANDATORY, NOT just for docs.** cspell's `files` glob in [cspell.json](../../cspell.json) covers every file extension that agent preflight and full validation spell-check:

- Markdown: `**/*.{md,markdown}` (docs tree, root README/CHANGELOG/PLAN/AGENTS/CLAUDE, LLM instruction tree, GitHub templates)
- C#: `**/*.cs` (every source file under `Runtime/`, `Editor/`, `Tests/`, samples, and scripts)
- YAML: `**/*.{yml,yaml}` (workflows, yamllint config, any config YAML)
- JSON-family: `**/*.{json,jsonc,asmdef,asmref}` (package.json, `.asmdef`/`.asmref`, tool configs)
- JavaScript: `**/*.js` (scripts/ helpers, tests, hook scripts)

The `cspell.json` `files` glob and agent-preflight's pass-through list are kept in lock-step by `scripts/tests/test-cspell-hook-files-parity.sh` (run via `npm run validate:cspell-files-parity`). If you see drift, fix `cspell.json`'s `files` glob -- never narrow agent-preflight's pass-through.

If you modified ANY file in that set -- C# sources, tests, CHANGELOG, skill files, docs, YAML, JSON, `.asmdef`/`.asmref`, `.js` scripts -- you MUST run `npm run lint:spelling` before declaring work complete. `npm run agent:preflight` checks the same changed-file set before hooks are involved, and `npm run validate:local`/CI run full spelling validation. Do NOT mentally gate "this is a code change, no spelling matters" -- cspell lints identifiers in comments, XML docs, and log strings, which is where most typos actually land.

Run `npm run lint:spelling` manually before declaring work complete. Agent preflight and CI check the
same files, but they are final gates rather than a substitute for checking each change promptly.

Failure-recovery decision tree (when cspell reports `Unknown word`):

1. Is it a typo? Fix the source file. Done.
2. Is it a valid term already in a dictionary, just in a different case? cspell is case-insensitive here, so this should not happen — re-read the error.
3. Is it a valid term missing from the dictionary? Pick the right bucket using [linter-reference](../skills/linter-reference.md#adding-words-to-dictionary):
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

Also verify license headers on new or modified files — see [license-headers](../skills/license-headers.md).

### Documentation Changes

```bash
# After EVERY .md file modification:
node scripts/run-prettier.js --write -- <file>
npm run lint:spelling    # 🚨 #1 CI failure cause!
npm run lint:docs         # Validates links
npm run lint:markdown     # Structural rules
```

**A link from `docs/` to a file OUTSIDE `docs/` needs the MkDocs build, not `lint:docs`.**
`lint:docs` walks markdown-to-markdown links and passes a link to a `.sh`, `.ps1` or any other repo
file — while `Validate Documentation` fails the whole run on it, because `mkdocs build --strict`
turns "target not found among documentation files" into an error. Reference such a file in
backticks, or link the GitHub blob URL the way
[the llms.txt page](../../docs/project/llms-txt.md) does. The strict build is runnable in this
devcontainer and takes about 40 seconds, which is far cheaper than finding out on a pull request:

```bash
.venv/bin/mkdocs build --strict    # writes to the gitignored site/
```

### CHANGELOG or Project JSON Changes

```bash
# After EVERY CHANGELOG.md / package.json / asmdef / asmref edit:
node scripts/run-prettier.js --write -- <file>
npm run lint:spelling    # 🚨 validate:local/CI spell-check CHANGELOG + JSON
```

### YAML Changes

```bash
# After EVERY .yml/.yaml file modification:
node scripts/run-prettier.js --write -- <file>
pwsh -NoProfile -File scripts/lint-yaml.ps1 -Paths <changed files>

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

**Why this matters**: Test lifecycle lint failures block `agent:preflight`, `validate:local`, and CI. Catching and fixing these issues early (after each file change) prevents frustrating failures when you prepare to push.

### Assembly Definition Changes (`.asmdef`)

```bash
# 🚨 MANDATORY: After EVERY .asmdef file creation or modification:
pwsh -NoProfile -File scripts/lint-asmdef.ps1

# Also run standard JSON formatting:
node scripts/run-prettier.js --write -- <file>
```

**CRITICAL**: The asmdef linter checks JSON shape, references, and Unity version-define grammar. For optional Odin code, also run `pwsh -NoProfile -File scripts/tests/test-sync-script-contracts.ps1`; it verifies that Sirenix references stay behind `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR` and that runtime Odin base aliases keep Unity fallbacks. See [manage-assembly-definitions](../skills/manage-assembly-definitions.md).

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

**CRITICAL**: The skills index is the generated [Skills Index](../skills/index.md) file (linked from the [LLM context file](../context.md)), NOT an embedded block. It is byte-for-byte deterministic across OS (ordinal sort, UTF-8 no BOM, LF) and Prettier-ignored — regenerate it with the generator, never hand-edit it. Trigger descriptions MUST be ASCII (use `-`, not an em-dash); a non-ASCII trigger is the cross-OS drift class the lint rejects. The lint also verifies the context file keeps exactly one H1 and links to the index.

**Tests**: Run `pwsh -NoProfile -File scripts/tests/test-llm-instructions-lint.ps1` to verify the lint script itself (test cases covering generator output validation, lint correctness, H1/H2 detection, and pattern matching).

---
