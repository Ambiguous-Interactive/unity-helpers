# Linter Commands Part 2

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
