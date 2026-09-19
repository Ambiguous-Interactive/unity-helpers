# Linter Commands Part 1

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
