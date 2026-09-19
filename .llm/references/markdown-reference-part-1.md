# markdown-reference - Part 1

## Split Content

**Trigger**: When writing or editing markdown files, especially for link formatting and linting rules.

---

## When to Use

Use this reference when:

- Adding or editing markdown links
- Encountering link linting errors
- Working with code blocks in documentation
- Fixing markdownlint violations
- Understanding markdown quality requirements

For core documentation requirements, see [update-documentation](../skills/update-documentation.md).
For validation workflow, see [validate-before-commit](../skills/validate-before-commit.md).

---

## CRITICAL: Link Formatting Rules

### Internal Links MUST Use Relative Prefixes

**ALL internal markdown links MUST use `./` or `../` prefix for relative paths.**

```text
❌ WRONG                                        ✅ CORRECT
────────────────────────────────────────────────────────────────────────────────
[text](file.md)                                 [text](./file.md)
[text](docs/guide.md)                           [text](./docs/guide.md)
[create-test](create-test.md)                   [create-test](./create-test.md)
[context](../context.md)                        [context](../context.md)  ← ../ is OK
```

**Rule**: Every relative link MUST start with either:

- `./` — for files in the same directory or subdirectories
- `../` — for files in parent directories

**Why this matters:**

- Links without `./` prefix WILL fail the doc link linter
- CI will reject PRs with improperly formatted links
- Some Markdown renderers fail to resolve links without explicit relative paths

### NEVER Use Backtick-Wrapped File References

```text
❌ WRONG                                         ✅ CORRECT
────────────────────────────────────────────────────────────────────────────────
See `some-file` for details                      See [some-file](./some-file.md) for details
Refer to `skills/create-test` for guidelines     Refer to [create-test](./create-test.md) for guidelines
Check `context` for rules                        Check [context](../context.md) for rules
```

### NEVER Use Absolute GitHub Pages Paths

```text
❌ WRONG: Absolute GitHub Pages paths
────────────────────────────────────────────────────────────────────────────────
[guide](/unity-helpers/docs/guide.md)           ← Breaks in CI
[features](/unity-helpers/docs/features/)       ← Cannot be validated
[API ref](/unity-helpers/docs/api/core.md)      ← Fails lint-doc-links.ps1

✅ CORRECT: Relative paths
────────────────────────────────────────────────────────────────────────────────
[guide](./docs/guide.md)                        ← Works everywhere
[features](./docs/features/)                    ← Validated by linter
[API ref](../api/core.md)                       ← Portable and correct
```

---

## Required Commands After Markdown Changes

> **RUN IMMEDIATELY**: Execute `npm run lint:docs` right after ANY markdown edit.

```bash
# STEP 1: IMMEDIATELY after ANY markdown change:
npm run lint:docs         # ← RUN THIS FIRST! Catches link errors early

# STEP 2: Then run remaining linters:
npm run lint:markdown     # Check markdownlint rules
npm run lint:spelling     # Check spelling (MUST PASS)
npm run format:md:check   # Check Prettier formatting

# Or run full content validation (includes all above):
npm run validate:content
```

**STOP**: Do NOT mark documentation work complete until `npm run lint:docs` passes with zero errors.

---

## Code Block Language Specifiers

**ALL fenced code blocks MUST have a language specifier.**

| Language   | Specifier    | Example Use Case                          |
| ---------- | ------------ | ----------------------------------------- |
| C#         | `csharp`     | All C# code examples                      |
| Bash       | `bash`       | Terminal commands, shell scripts          |
| PowerShell | `powershell` | Windows/PowerShell commands               |
| JSON       | `json`       | Configuration files, API responses        |
| YAML       | `yaml`       | Unity manifests, GitHub Actions           |
| XML        | `xml`        | XML documentation, config files           |
| Markdown   | `markdown`   | Markdown syntax examples                  |
| Plain text | `text`       | File structures, command output, diagrams |

````markdown
<!-- ✅ CORRECT: Language specifier present -->

```csharp
public void Example() { }
```

<!-- ❌ WRONG: Missing language specifier -->

```
public void Example() { }
```
````

---

## Heading Rules

**NEVER use emphasis (bold/italic) as a substitute for headings.**

```markdown
<!-- ✅ CORRECT: Proper heading -->

## Button Configuration

The button supports...

<!-- ❌ WRONG: Bold text used as heading -->

**Button Configuration**

The button supports...
```

**Why this matters:**

- Proper headings create document structure for navigation
- Screen readers and accessibility tools rely on heading hierarchy
- Table of contents generation requires proper headings

---

## Pipe Characters in Markdown Tables

> **CRITICAL**: Pipe characters (`|`) inside markdown tables MUST be escaped with `\|`, even when inside backticks.

In GitHub Flavored Markdown tables, the pipe character `|` is the column separator. Backticks do NOT prevent pipes from being interpreted as separators.

**Common Patterns Requiring Escape**:

| Pattern in Code         | How to Write in Table Cell |
| ----------------------- | -------------------------- |
| `cmd \| while read`     | Pipe in shell pipeline     |
| `expr \|\| fallback`    | Logical OR operator        |
| `grep -E 'a\|b'`        | Regex alternation          |
| `2>/dev/null \|\| true` | Error suppression          |

---
