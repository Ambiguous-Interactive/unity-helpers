# validate-before-commit - Part 3

## Split Content

## What Gets Validated

The `npm run validate:local` command runs these checks:

> **Agents do not run this aggregate by default.** CI runs the same gates. Use it only when the user
> explicitly requests a full local aggregate, or when the aggregate runner itself changed and
> targeted integration evidence cannot answer the question. Do not start it while another external
> repository aggregate, whole-tree linter, build, or interrupted child is alive. Concurrency managed
> by a single runner invocation is expected.
> The
> edit loop is `npm run agent:preflight` (2.9 s) plus the one targeted check for what you touched
> (`node scripts/run-contract-tests.js --only <id>`, `node scripts/run-repo-lint.js --only <id>`,
> `dotnet test --filter`). Reach for the cheapest instrument that answers the question -- a `rg` for
> the shape beats a whole-tree rebuild -- and when you skip a gate, **name what is unverified**
> rather than reporting it as clean.

1. **validate:content** — Documentation and formatting
   - `lint:docs` — Markdown links (no backtick `.md` refs)
   - `lint:markdown` — Markdownlint rules
   - `format:md:check` — Prettier markdown formatting
   - `format:json:check` — Prettier JSON/asmdef formatting
   - `format:yaml:check` — Prettier YAML formatting
   - `validate:lint-error-codes` — cspell coverage for every `^[A-Z]{2,}\d{3}$` prefix emitted by `scripts/lint-*.{ps1,js}`, `scripts/tests/test-lint-*.{ps1,js,sh}`, or `.githooks/*`

2. **lint:spelling** — CSpell validation on the repository

3. **eol:check** — Line endings (CRLF, no BOM)

4. **validate:tests:fast** — Fast repository contract tests, run concurrently by
   `scripts/run-contract-tests.js` (~2.5 min, was ~10 as a serial chain); exhaustive synthetic hook
   fixtures stay in CI. **Adding a check means adding a registry entry, not appending `&&` to the npm
   script** — a contract test fails if the chain comes back. If a check mutates the working tree
   (rewrites a tracked file, drops a canary), mark it `exclusive: true` or it will flake every other
   check intermittently. **Never pipe the runner's output through `tail`**: it captures each check's
   full output into a fold, and that fold is the only thing that names a failing assertion

5. **lint:csharp-naming** — C# naming conventions

---

## Critical Link Formatting Rules

For complete link formatting rules, escaping patterns, and linting rules, see [markdown-reference](../skills/markdown-reference.md).

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

- [linter-reference](../skills/linter-reference.md) — Detailed linter commands and configurations
- [validation-troubleshooting](../skills/validation-troubleshooting.md) — Common errors and fixes
- [update-documentation](../skills/update-documentation.md) — Documentation requirements
- [formatting](../skills/formatting.md) — CSharpier, Prettier, markdownlint workflow
- [markdown-reference](../skills/markdown-reference.md) — Link formatting, structural rules
- [create-test](../skills/create-test.md) — Test file requirements
- [test-data-driven](../skills/test-data-driven.md) — Data-driven testing patterns
- [test-naming-conventions](../skills/test-naming-conventions.md) — Naming rules and legacy test migration
- [manage-skills](../skills/manage-skills.md) — Skill file maintenance and index regeneration
- [license-headers](../skills/license-headers.md) — License header requirements and year validation
