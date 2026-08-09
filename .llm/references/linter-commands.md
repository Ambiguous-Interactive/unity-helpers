# Linter Commands Reference

This document provides a comprehensive reference for all formatting and linting commands used in the project.

---

## Quick Reference

| Tool         | File Types                | Check Command                                  | Fix Command                                      |
| ------------ | ------------------------- | ---------------------------------------------- | ------------------------------------------------ |
| CSharpier    | `.cs`                     | `npm run format:csharp:check`                  | `npm run format:csharp`                          |
| Prettier     | `.md`, `.json`, `.yml`    | `node scripts/run-prettier.js --check -- .`    | `node scripts/run-prettier.js --write -- <file>` |
| markdownlint | `.md`                     | `npm run lint:markdown`                        | Manual fixes required                            |
| yamllint     | `.yml`, `.yaml`           | `npm run lint:yaml`                            | Manual fixes required                            |
| actionlint   | `.github/workflows/*.yml` | `actionlint`                                   | Manual fixes required                            |
| cspell       | All text files            | `npm run lint:spelling`                        | Add terms to `cspell.json`                       |
| Test Linter  | `Tests/**/*.cs`           | `pwsh -NoProfile -File scripts/lint-tests.ps1` | Manual fixes required                            |
| Doc Links    | `.md`                     | `npm run lint:docs`                            | Manual fixes required                            |
| C# Naming    | `.cs`                     | `npm run lint:csharp-naming`                   | Rename methods manually                          |
| EOL Check    | All files                 | `npm run eol:check`                            | `npm run eol:fix`                                |

---

## C# Formatting (CSharpier)

**File Types**: `.cs`

**When to Run**: IMMEDIATELY after editing ANY C# file — not at the end of a task.

### Commands

```bash
# Check formatting (exits with error if changes needed)
dotnet tool run csharpier check .

# Auto-fix formatting
dotnet tool run csharpier format .

# Format specific file
dotnet tool run csharpier format Runtime/Core/Helper/Buffers.cs
```

### What It Fixes

- Indentation and spacing
- Brace placement
- Line breaks and line length
- Blank line normalization
- Trailing whitespace

### Troubleshooting

```bash
# If tool not found
dotnet tool restore
```

---

## Prettier (Markdown, JSON, YAML)

**File Types**: `.md`, `.json`, `.asmdef`, `.asmref`, `.yml`, `.yaml`, config files

**When to Run**: IMMEDIATELY after editing ANY non-C# file.

### Commands

```bash
# Format a specific file (RECOMMENDED)
node scripts/run-prettier.js --write -- <file>

# Verify a specific file
node scripts/run-prettier.js --check -- <file>

# Check all files for formatting issues
node scripts/run-prettier.js --check -- .

# Format all files (use only if needed)
node scripts/run-prettier.js --write -- .
```

### Examples

```bash
node scripts/run-prettier.js --write -- .llm/skills/create-test.md
node scripts/run-prettier.js --write -- package.json
node scripts/run-prettier.js --write -- .github/workflows/ci.yml
```

### Line Endings

Prettier uses settings from `.prettierrc.json`:

| File Type                           | Line Ending |
| ----------------------------------- | ----------- |
| Markdown files (`.md`)              | LF (unix)   |
| YAML files (`.yml`, `.yaml`)        | LF (unix)   |
| Shell scripts (`.sh`)               | LF (unix)   |
| `package.json`, `package-lock.json` | LF (unix)   |
| Most other files                    | CRLF        |

---

## Markdownlint

**File Types**: `.md`, `.markdown`

**When to Run**: After editing markdown files.

### Commands

```bash
# Check markdown lint rules
npm run lint:markdown
```

### Common Rules and Fixes

| Rule  | Issue                        | Fix                                    |
| ----- | ---------------------------- | -------------------------------------- |
| MD009 | Trailing spaces              | Remove spaces at end of lines          |
| MD022 | Headings need blank line     | Add blank line after headings          |
| MD028 | Blank line in blockquote     | Remove blank line or merge blockquotes |
| MD031 | Code blocks need blank lines | Add blank line around fenced code      |
| MD032 | Lists need blank lines       | Add blank line before and after lists  |
| MD036 | Emphasis used as heading     | Use proper `#` headings                |
| MD040 | Code fence without language  | Add language specifier                 |

### Language Specifiers for Code Blocks

| Content Type      | Specifier    |
| ----------------- | ------------ |
| C# code           | `csharp`     |
| Shell commands    | `bash`       |
| PowerShell        | `powershell` |
| JSON              | `json`       |
| YAML              | `yaml`       |
| XML               | `xml`        |
| Plain text/output | `text`       |
| Markdown examples | `markdown`   |

---

## YAML Lint (yamllint)

**File Types**: `.yml`, `.yaml`

**When to Run**: After editing ANY YAML file.

### Commands

```bash
# Run yamllint on all YAML files
npm run lint:yaml
```

### Common Issues

| Issue                  | Error Message          | Fix                                |
| ---------------------- | ---------------------- | ---------------------------------- |
| Trailing spaces        | `trailing spaces`      | Remove spaces at end of lines      |
| Inconsistent indent    | `wrong indentation`    | Use consistent 2-space indentation |
| Line too long          | `line too long`        | Break long lines (max 200 chars)   |
| Missing newline at EOF | `no new line at end`   | Add empty line at end of file      |
| Too many blank lines   | `too many blank lines` | Maximum 1 consecutive blank line   |

### Workflow for YAML Files

```bash
# Step 1: Format with Prettier
node scripts/run-prettier.js --write -- <file>

# Step 2: Run yamllint
npm run lint:yaml

# Step 3: For workflow files, also run actionlint
actionlint .github/workflows/<file>
```

---

## actionlint (GitHub Actions)

**File Types**: `.github/workflows/*.yml`

**When to Run**: After ANY change to workflow files.

### Commands

```bash
# Lint all workflow files
actionlint

# Lint specific workflow
actionlint .github/workflows/ci.yml

# With shellcheck integration
actionlint -shellcheck=/usr/bin/shellcheck
```

### Installation (if not in dev container)

```bash
curl -sfL https://raw.githubusercontent.com/rhysd/actionlint/main/scripts/download-bash.sh | bash -s -- -b /usr/local/bin
```

### Common Errors

| Error Code | Description                      | Fix                                             |
| ---------- | -------------------------------- | ----------------------------------------------- |
| SC2129     | Multiple redirects to same file  | Use grouped commands: `{ cmd1; cmd2; } >> file` |
| SC2034     | Variable declared but not used   | Remove unused variable or use it                |
| SC2086     | Double quote to prevent globbing | Use `"$variable"` instead of `$variable`        |
| SC2046     | Quote to prevent word splitting  | Use `"$(command)"` instead of `$(command)`      |
| SC2155     | Declare and assign separately    | Use `local var; var=$(cmd)`                     |

---

## Spelling Check (cspell)

**File Types**: All text files (markdown, C# comments, XML docs)

**When to Run**: IMMEDIATELY after editing documentation or code comments.

### Commands

```bash
# Run spelling check
npm run lint:spelling
```

### Dictionary Categories in cspell.json

| Dictionary       | Use For                                           |
| ---------------- | ------------------------------------------------- |
| `unity-terms`    | Unity Engine API names (`MonoBehaviour`, `OnGUI`) |
| `csharp-terms`   | C# language features, .NET types                  |
| `package-terms`  | This package's custom types and class names       |
| `tech-terms`     | General programming terms, tools, libraries       |
| `words` (global) | General words, proper nouns, acronyms             |

### Workflow for Spelling Fixes

```bash
# 1. Run spelling check
npm run lint:spelling

# 2. If errors found:
#    - Fix actual typos
#    - Add valid terms to cspell.json

# 3. Format cspell.json
node scripts/run-prettier.js --write -- cspell.json

# 4. Re-run to verify
npm run lint:spelling
```

---

## Test Lifecycle Linter

**File Types**: `Tests/**/*.cs`

**When to Run**: After ANY change to test files.

### Commands

```bash
# Run test lifecycle linter
pwsh -NoProfile -File scripts/lint-tests.ps1
```

### Rules

| Rule     | Description                                                            |
| -------- | ---------------------------------------------------------------------- |
| `UNH001` | No manual `DestroyImmediate`/`Destroy` — use `Track()` for cleanup     |
| `UNH002` | All Unity object allocations must be wrapped with `Track()`            |
| `UNH003` | Test classes creating Unity objects must inherit from `CommonTestBase` |

### Suppression

For intentional destroy tests, add `UNH-SUPPRESS` comment on the SAME line:

```csharp
UnityEngine.Object.DestroyImmediate(target); // UNH-SUPPRESS: Test verifies destroy behavior
```

---

## Documentation Link Linter

**File Types**: `.md`

**When to Run**: After editing ANY markdown file.

### Commands

```bash
# Check for broken links
npm run lint:docs

# Verbose output
pwsh ./scripts/lint-doc-links.ps1 -VerboseOutput
```

### What It Catches

- Broken links to non-existent files
- Backtick-wrapped markdown file references (FORBIDDEN)
- Links missing `./` or `../` prefix
- Invalid anchor links

### Link Format Requirements

```markdown
<!-- WRONG -->

See [context](context.md)
See `context.md`

<!-- CORRECT -->

See [context](./context.md)
See [context](../context.md)
```

---

## C# Naming Convention Linter

**File Types**: `.cs`

**When to Run**: After modifying C# method names.

### Commands

```bash
# Check for naming violations
npm run lint:csharp-naming
```

### Rules

- NO underscores in method names (including tests)
- Use PascalCase for all methods

```csharp
// WRONG
public void When_Input_Is_Null_Returns_Default() { }

// CORRECT
public void WhenInputIsNullReturnsDefault() { }
```

---

## End-of-Line Character Check

**File Types**: All files

**When to Run**: After creating new files or if CI fails with EOL errors.

### Commands

```bash
# Check EOL characters
npm run eol:check

# Auto-fix EOL characters
npm run eol:fix
```

### Expected Line Endings

| File Type             | Line Ending |
| --------------------- | ----------- |
| Most files            | CRLF        |
| Shell scripts (`.sh`) | LF          |
| Markdown (`.md`)      | LF          |
| YAML (`.yml`)         | LF          |

---

## Full Validation

### Pre-Push Validation (MANDATORY)

```bash
# Run ALL validations before completing any task
npm run validate:prepush
```

This runs:

1. **validate:content** — Documentation and formatting checks
2. **eol:check** — Line ending validation
3. **validate:tests** — Test lifecycle lint
4. **lint:csharp-naming** — C# naming conventions

### Full Formatting Workflow

```bash
# 1. Format C# code
dotnet tool run csharpier format .

# 2. Format markdown
npm run format:md

# 3. Format JSON
npm run format:json

# 4. Format YAML
npm run format:yaml

# 5. Run full validation
npm run validate:prepush
```

---

## npm Script Reference

| Script                       | Description                           |
| ---------------------------- | ------------------------------------- |
| `npm run validate:prepush`   | Run all pre-push validations          |
| `npm run validate:content`   | Validate documentation and formatting |
| `npm run lint:markdown`      | Run markdownlint                      |
| `npm run lint:docs`          | Validate documentation links          |
| `npm run lint:yaml`          | Run yamllint                          |
| `npm run lint:spelling`      | Run cspell spelling check             |
| `npm run lint:csharp-naming` | Check C# naming conventions           |
| `npm run format:md`          | Format markdown with Prettier         |
| `npm run format:md:check`    | Check markdown formatting             |
| `npm run format:json`        | Format JSON with Prettier             |
| `npm run format:json:check`  | Check JSON formatting                 |
| `npm run format:yaml`        | Format YAML with Prettier             |
| `npm run format:yaml:check`  | Check YAML formatting                 |
| `npm run eol:check`          | Check end-of-line characters          |
| `npm run eol:fix`            | Fix end-of-line characters            |

---

## Related Documentation

- [formatting](../skills/formatting.md) — CSharpier, Prettier, markdownlint workflow
- [markdown-reference](../skills/markdown-reference.md) — Link formatting, structural rules
- [validate-before-commit](../skills/validate-before-commit.md) — Complete validation checklist
