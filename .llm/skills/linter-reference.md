# Skill: Linter Reference

<!-- trigger: lint, linter, csharpier, prettier, cspell | Detailed linter commands, configurations | Core -->

**Trigger**: When you need detailed linter commands, configurations, or to understand what each linter checks.

---

## When to Use

Use this reference when you need:

- Complete linter command documentation
- Configuration file locations and settings
- Understanding what specific rules check
- Details about npm script internals

For the quick validation workflow, see [validate-before-commit](./validate-before-commit.md).
For fixing common errors, see [validation-troubleshooting](./validation-troubleshooting.md).

> **Note:** This is a comprehensive reference for linter details. For the quick pre-commit workflow,
> see [validate-before-commit](./validate-before-commit.md).

---

## All Linter Commands

### Master Validation Command

```bash
# Run ALL checks at once (recommended before commit/push)
npm run validate:prepush
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
| `npm run validate:tests`                        | Test lifecycle lint (Track() usage)          |
| `npm run test:sync-script-contracts`            | Sync newline + cspell contract regressions   |
| `bash scripts/audit-license-years.sh --summary` | License year header audit                    |

---

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

`npm run validate:lint-error-codes` enforces that every `^[A-Z]{2,}\d{3}$` token emitted by `scripts/lint-*.{ps1,js}`, `scripts/tests/test-lint-*.{ps1,js,sh}`, or `.githooks/*` has its prefix registered with cspell; on drift it prints a copy-pasteable JSON patch. Regression test: `scripts/tests/test-validate-lint-error-codes.ps1`. Wired into `validate:content`, `validate:tests`, and pre-push (when any file in those three scan roots, `cspell.json`, the validator, or its test changes).

### Inline Ignores

For single occurrences, use CSpell ignore comments:

```markdown
<!-- cspell:ignore someword -->
```

Or in code:

```csharp
// cspell:ignore someword
```

---

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

---

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

---

## Prettier

### Commands

```bash
# Check formatting
node scripts/run-prettier.js --check -- .
node scripts/run-prettier.js --check -- <file>

# Fix formatting
node scripts/run-prettier.js --write -- .
node scripts/run-prettier.js --write -- <file>
```

### Configuration

Located at `.prettierrc.json`:

```json
{
  "proseWrap": "always",
  "printWidth": 100,
  "tabWidth": 2,
  "trailingComma": "es5"
}
```

### File Coverage

Prettier handles these file types:

- Markdown (`.md`)
- JSON (`.json`, `.asmdef`)
- YAML (`.yml`, `.yaml`)
- JavaScript (`.js`)

### Ignored Files

See `.prettierignore` for files excluded from formatting.

---

## CSharpier

### Command

```bash
dotnet tool run csharpier format .
```

### Configuration

Located at `.csharpierrc.json`:

```json
{
  "printWidth": 100,
  "useTabs": false,
  "tabWidth": 4
}
```

### Important Notes

- CSharpier only formats C# files
- Always run after ANY `.cs` file modification
- Cannot fix naming convention violations (use manual fixes)

---

## C# Naming Linter

### Command

```bash
npm run lint:csharp-naming
```

### What It Checks

| Pattern           | Requirement              | Example         |
| ----------------- | ------------------------ | --------------- |
| Public methods    | PascalCase               | `GetValue()`    |
| Private methods   | PascalCase               | `ProcessData()` |
| Public properties | PascalCase               | `Count`         |
| Private fields    | `_camelCase` with prefix | `_myField`      |
| Constants         | PascalCase or UPPER_CASE | `MaxValue`      |
| Parameters        | camelCase                | `itemCount`     |
| Type parameters   | `T` or `T` + PascalCase  | `T`, `TValue`   |

### Common Violations

```csharp
// ❌ WRONG: lowercase method name
public void processData() { }

// ✅ CORRECT: PascalCase method name
public void ProcessData() { }

// ❌ WRONG: missing underscore prefix
private int count;

// ✅ CORRECT: underscore prefix
private int _count;
```

---

## YAML Linter

### Command

```bash
npm run lint:yaml
```

### What It Checks

- YAML syntax validity
- Proper indentation
- Duplicate keys

### For GitHub Workflow Files

```bash
# Additional check for workflow files
actionlint
```

---

## Test Lifecycle Linter

### Command

```bash
npm run validate:tests
pwsh -NoProfile -File scripts/lint-tests.ps1
```

### What It Checks

1. **Allowlist path validation** (on startup): All paths in `$allowedHelperFiles` must exist on disk. Fails immediately with exit code 1 if any path is stale (file moved/renamed/deleted).
2. **UNH001**: Direct `Destroy`/`DestroyImmediate` calls without `Track()`
3. **UNH002**: Untracked Unity object allocation (`new GameObject(...)` etc.)
4. **UNH003**: Test classes missing `CommonTestBase` inheritance
5. **UNH004**: Underscores in test names
6. **UNH005**: `Assert.IsNull`/`Assert.IsNotNull` (should use `Assert.IsTrue` for Unity null checks)
7. **UNH011**: Editor-only references (`UnityEditor`, `WallstopStudios.UnityHelpers.Editor`) in
   player-compiled test code (anything under `Tests/` except `Tests/Editor/`) must be inside
   `#if UNITY_EDITOR`. Otherwise the editor assembly is stripped from the standalone player build
   and the leg fails with `CS0234` before any test runs. Add `// UNH-SUPPRESS UNH011` to opt out.

All Unity object creation in tests must use `Track()`:

```csharp
// ✅ CORRECT: Track() ensures cleanup
GameObject obj = Track(new GameObject("Test"));
MyComponent comp = Track(obj.AddComponent<MyComponent>());

// ❌ WRONG: Untracked objects may leak
GameObject obj = new GameObject("Test");
```

### Tests

```bash
pwsh -NoProfile -File scripts/tests/test-lint-tests.ps1
```

Tests cover allowlist path existence, UNH error detection, clean file acceptance, and helper file allowlisting.

---

## Line Ending Linter

### Command

```bash
npm run eol:check
```

### Requirements

- All files must use CRLF line endings
- No UTF-8 BOM (Byte Order Mark)

### Fixing Line Endings

```bash
npm run eol:fix
```

---

## Meta File Linter

### Command

```bash
pwsh -NoProfile -File scripts/lint-meta-files.ps1
pwsh -NoProfile -File scripts/lint-meta-files.ps1 -VerboseOutput  # detailed output
```

### What It Checks

Every file and directory under scanned source roots (`Runtime`, `Editor`, `Tests`, `Samples~`, `Shaders`, `Styles`, `URP`, `docs`, `scripts`) has a corresponding `.meta` file, and every `.meta` file has a corresponding source file/directory.

### Exclusion Configuration

The script excludes certain paths from requiring `.meta` files. Exclusions are defined in three arrays at the top of [lint-meta-files.ps1](../../scripts/lint-meta-files.ps1):

| Array                  | Purpose                                          | Examples                                                      |
| ---------------------- | ------------------------------------------------ | ------------------------------------------------------------- |
| `$excludeDirs`         | Directories excluded entirely (and all contents) | `node_modules`, `.pytest_cache`, `__pycache__`, `.mypy_cache` |
| `$excludeFilePatterns` | File name/glob patterns excluded                 | `.gitkeep`, `.DS_Store`, `Thumbs.db`, `*.pyc`, `*.swp`        |
| `$excludeDirPatterns`  | Directory name patterns excluded                 | `Samples~`                                                    |

### Adding New Exclusions

When introducing new tooling that creates cache or artifact directories inside source roots:

1. Add the directory name to `$excludeDirs` (for directories) or file pattern to `$excludeFilePatterns` (for files)
2. Add test cases to [test-lint-meta-exclusions.sh](../../scripts/tests/test-lint-meta-exclusions.sh)
3. Run the tests: `bash scripts/tests/test-lint-meta-exclusions.sh`

### Test-ShouldExclude Function

The `Test-ShouldExclude` function checks whether a path should be excluded. For `$excludeDirs` entries, it matches:

- The directory itself: `$relativePath -eq $dir` or `$relativePath -like "*/$dir"`
- Contents at root level: `$relativePath -like "$dir/*"`
- Nested contents: `$relativePath -like "*/$dir/*"`

Patterns must match **both** the excluded directory itself **and** its contents. If only contents are matched (e.g., `$dir/*` without `$dir`), orphaned `.meta` files for the directory itself won't be detected correctly.

### Tests

```bash
bash scripts/tests/test-lint-meta-exclusions.sh
```

Tests cover all exclusion categories: tooling cache dirs, OS metadata, git placeholders, compiled bytecode, and editor temp files.

---

## NPM Script Breakdown

### validate:prepush

Runs these in sequence:

1. `validate:content` (docs + formatting)
2. `eol:check`
3. `validate:tests`
4. `lint:csharp-naming`

### validate:content

Runs these in sequence:

1. `lint:docs`
2. `lint:markdown`
3. `format:md:check`
4. `format:json:check`
5. `format:yaml:check`

---

## Shared Helpers

| Helper                          | Purpose                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| ------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `scripts/comment-stripping.ps1` | Language-aware comment masker used by `lint-doc-links`, `lint-csharp-naming`, `lint-odin-undo-safety`, `lint-drawer-multiobject`, `lint-tests`. Replaces comment characters with spaces while preserving line/column offsets so downstream regex scans don't false-positive on commented-out code. Public API: `Get-LanguageFromExtension`, `Get-CommentMaskedLines`, `Get-CommentRanges`. Pinned by `scripts/tests/test-comment-stripping.ps1`. |
| `scripts/git-path-helpers.ps1`  | Normalizes filesystem paths to repo-relative POSIX form for safe use with `git check-ignore` and related plumbing.                                                                                                                                                                                                                                                                                                                               |

When adding a new lint script that scans source-code text, prefer dot-sourcing `comment-stripping.ps1` over hand-rolling a comment scrubber.

---

## Configuration File Locations

| Tool         | Config File          | Purpose                |
| ------------ | -------------------- | ---------------------- |
| CSpell       | `cspell.json`        | Spell check dictionary |
| Markdownlint | `.markdownlint.json` | Markdown rules         |
| Prettier     | `.prettierrc.json`   | Formatting options     |
| Prettier     | `.prettierignore`    | Ignored files          |
| CSharpier    | `.csharpierrc.json`  | C# formatting options  |
| ESLint       | `.eslintrc.json`     | JavaScript linting     |
| EditorConfig | `.editorconfig`      | Editor settings        |

---

## Related Skills

- [validate-before-commit](./validate-before-commit.md) — Quick validation workflow
- [validation-troubleshooting](./validation-troubleshooting.md) — Common errors and fixes
- [formatting](./formatting.md) — CSharpier, Prettier, markdownlint workflow
- [markdown-reference](./markdown-reference.md) — Link formatting, structural rules
- [license-headers](./license-headers.md) — License header year rules and auto-fix
