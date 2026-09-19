# linter-reference - Part 1

## Split Content

**Trigger**: When you need detailed linter commands, configurations, or to understand what each linter checks.

## When to Use

Use this reference when you need:

- Complete linter command documentation
- Configuration file locations and settings
- Understanding what specific rules check

For the quick validation workflow, see [validate-before-commit](../skills/validate-before-commit.md).

## All Linter Commands

### Master Validation Command

```bash
# Run ALL checks at once (recommended before commit/push)
npm run validate:local
```

### Individual Commands

| Command                                         | Description                                  |
| ----------------------------------------------- | -------------------------------------------- |
| `npm run lint:spelling`                         | Spell check all documentation (CSpell)       |
| `npm run lint:spelling:config`                  | Lint cspell.json for config issues           |
| `npm run lint:spelling:config:fix`              | Auto-fix cspell.json config issues           |
| `npm run lint:docs`                             | Check markdown links and backtick refs       |
| `npm run lint:markdown`                         | Markdownlint structural rules                |
| `npm run lint:yaml`                             | YAML syntax validation                       |
| `npm run lint:dependabot`                       | Dependabot config schema validation          |
| `npm run lint:pwsh-invocations`                 | Bash->PowerShell invocation anti-patterns    |
| `npm run validate:lint-error-codes`             | cspell coverage for lint-error-code prefixes |
| `npm run lint:csharp-naming`                    | C# naming conventions (method casing, etc.)  |
| `npm run format:md:check`                       | Check markdown formatting (Prettier)         |
| `npm run format:json:check`                     | Check JSON/ASMDEF formatting (Prettier)      |
| `npm run format:yaml:check`                     | Check YAML formatting (Prettier)             |
| `npm run eol:check`                             | Line endings (CRLF) and BOM check            |
| `npm run lint:tests`                            | Test lifecycle lint (Track() usage)          |
| `npm run validate:tests:fast`                   | Fast repository contract suites              |
| `npm run validate:tests:hook-regressions`       | Exhaustive synthetic hook fixtures           |
| `npm run validate:tests`                        | Complete CI contract suite                   |
| `npm run test:sync-script-contracts`            | Sync newline + cspell contract regressions   |
| `bash scripts/audit-license-years.sh --summary` | License year header audit                    |

## CSpell Spell Checker

### Command

```bash
npm run lint:spelling
```

### Configuration

Located at `cspell.json` in the project root.

### Adding Words to Dictionary

Add words to the appropriate categorized dictionary in `cspell.json`, not the root `words` array:

| Dictionary      | Purpose                                  | Examples                                |
| --------------- | ---------------------------------------- | --------------------------------------- |
| `unity-terms`   | Unity Engine APIs, components, lifecycle | MonoBehaviour, GetComponent, OnValidate |
| `csharp-terms`  | C# language features, .NET types         | readonly, nullable, IVT, StringBuilder  |
| `package-terms` | This package's public API and type names | WallstopStudios, IRandom, SpatialHash   |
| `tech-terms`    | General programming/tooling terms        | async, config, JSON, IL2CPP             |

When adding technical abbreviations (e.g., IVT for InternalsVisibleTo), place them in the matching category (`csharp-terms` for C# concepts, `tech-terms` for general tooling). Only use the root `words` array for project-specific words that don't fit any category.

**Lint-error-code prefixes** (2 or more uppercase letters used in codes like
`UNH001`, `PWS002`) belong in the root `words` array. Whenever a new lint
script emits a new prefix, register the prefix in cspell and run
`npm run validate:lint-error-codes` to confirm the contract still holds.

Since `caseSensitive` is `false` in this project, only ONE case variant per word is needed (e.g., `ulf` covers `ULF`, `Ulf`, etc.).

### Config Lint

```bash
npm run lint:spelling:config       # Check for case-redundant entries
npm run lint:spelling:config:fix   # Auto-fix case-redundant entries
```

The config linter catches case-redundant dictionary entries (error, blocking) and cross-dictionary duplicates (warning, non-blocking). It does not auto-classify root words into categorized dictionaries. That remains a review-time policy decision.

### Lint-Error-Code Coverage (Contract)

`npm run validate:lint-error-codes` enforces that every `^[A-Z]{2,}\d{3}$` token emitted by `scripts/lint-*.{ps1,js}`, `scripts/tests/test-lint-*.{ps1,js,sh}`, or `.githooks/*` has its prefix registered with cspell; on drift it prints a copy-pasteable JSON patch. Regression test: `scripts/tests/test-validate-lint-error-codes.ps1`. Wired into `validate:content`, `validate:tests`, and CI; run it before pushing when any file in those scan roots, `cspell.json`, the validator, or its test changes.

### Inline Ignores

For single occurrences, use CSpell ignore comments:

```markdown
<!-- cspell:ignore someword -->
```

Or in code:

```csharp
// cspell:ignore someword
```

## Markdownlint

### Command

```bash
npm run lint:markdown
```

### Configuration

Located at `.markdownlint.json` in the project root.

### Disabled Rules

These rules are disabled in this project:

| Rule    | Description                  | Why Disabled           |
| ------- | ---------------------------- | ---------------------- |
| `MD013` | Line length                  | Prettier handles this  |
| `MD041` | First line should be heading | Some files have badges |
| `MD033` | Inline HTML                  | Needed for formatting  |
| `MD024` | Duplicate headings           | Allowed in this repo   |

### Common Rule Violations

| Rule    | Issue                                   | Fix                             |
| ------- | --------------------------------------- | ------------------------------- |
| `MD007` | Wrong list indentation                  | Use 2 spaces for nested lists   |
| `MD009` | Trailing spaces                         | Remove trailing whitespace      |
| `MD012` | Multiple consecutive blank lines        | Reduce to single blank line     |
| `MD022` | Headings should be surrounded by blanks | Add blank lines around headings |
| `MD032` | Lists should be surrounded by blanks    | Add blank lines around lists    |

## Link Linter (lint:docs)

### Command

```bash
npm run lint:docs # full scan; use scripts/lint-doc-links.ps1 -Paths <files> for scoped checks
```

### What It Checks

1. **Broken internal links** — Links to non-existent files
2. **Missing anchors** — Links to non-existent headings
3. **Backtick file references** — Using backtick-wrapped filenames instead of proper links

### Link Format Requirements

```markdown
<!-- ✅ CORRECT: Use relative paths with ./ or ../ -->

[create-test](./create-test.md)
[context](../context.md)

<!-- ❌ WRONG: No relative prefix -->

[create-test](create-test.md)
```
