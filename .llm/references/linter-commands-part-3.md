# Linter Commands Part 3

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
# Run repository-wide lint and fast contract validation before pushing
npm run validate:local
```

This runs:

1. **validate:content** — Documentation and formatting checks
2. **eol:check** — Line ending validation
3. **validate:tests:fast** — Fast repository contract suites
4. **lint:csharp-naming** — C# naming conventions

CI additionally runs `validate:tests:hook-regressions` through the complete `validate:tests`
aggregate. Run that exhaustive subset locally when hook or agent-preflight behavior changes.

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
npm run validate:local
```

---

## npm Script Reference

| Script                       | Description                           |
| ---------------------------- | ------------------------------------- |
| `npm run validate:prepush`   | Run the fast last-resort push check   |
| `npm run validate:local`     | Run the complete local validation set |
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
