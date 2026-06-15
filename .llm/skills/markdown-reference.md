# Skill: Markdown Reference

<!-- trigger: markdown, link, md, format, lint | Link formatting, escaping, linting rules | Core -->

**Trigger**: When writing or editing markdown files, especially for link formatting and linting rules.

---

## When to Use

Use this reference when:

- Adding or editing markdown links
- Encountering link linting errors
- Working with code blocks in documentation
- Fixing markdownlint violations
- Understanding markdown quality requirements

For core documentation requirements, see [update-documentation](./update-documentation.md).
For validation workflow, see [validate-before-commit](./validate-before-commit.md).

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

## Prettier vs Markdownlint

> **CRITICAL**: Prettier and markdownlint catch DIFFERENT issues. You MUST run BOTH.

| Tool         | Catches                                                  | Misses                             |
| ------------ | -------------------------------------------------------- | ---------------------------------- |
| Prettier     | Formatting: spacing, indentation, line wrapping          | Structural rules like MD028, MD031 |
| markdownlint | Structural: heading hierarchy, blank lines, code context | Formatting/spacing issues          |

**Workflow for ALL markdown changes:**

```bash
# STEP 1: Format with Prettier
node scripts/run-prettier.js --write -- <file>

# STEP 2: Check structural rules with markdownlint
npm run lint:markdown

# STEP 3: Fix any markdownlint errors, then re-run Prettier if you made changes
```

---

## Common Structural Mistakes (Prettier Won't Fix)

### MD028: Blank Line Inside Blockquote

```markdown
<!-- ❌ WRONG (MD028) -->

> First quote.

> Second quote.

<!-- ✅ CORRECT: Continuous blockquote -->

> First quote.
> Second quote.
```

### MD031: Fenced Code Blocks Need Blank Lines

Code fences must have blank lines before and after:

```markdown
<!-- ❌ WRONG (MD031) -->

Some text:
\`\`\`csharp
code here
\`\`\`
More text.

<!-- ✅ CORRECT -->

Some text:

\`\`\`csharp
code here
\`\`\`

More text.
```

---

## Common Markdownlint Rules

| Rule  | Issue                        | Fix                                                |
| ----- | ---------------------------- | -------------------------------------------------- |
| MD028 | Blank line inside blockquote | Remove blank line between consecutive quotes       |
| MD031 | No blank line around fences  | Add blank line before and after code blocks        |
| MD032 | No blank line around lists   | Add blank line before and after lists              |
| MD022 | No blank line after headings | Add blank line after `#` headings                  |
| MD040 | Fenced code without language | Add language specifier (`csharp`, `bash`, etc.)    |
| MD025 | Multiple top-level headings  | Only one `#` heading per document (see note below) |
| MD009 | Trailing spaces              | Remove trailing whitespace                         |

> **MD025 — Single Title**: Each document has exactly one `#` title. The [LLM context file](../context.md) has one `#`, and the generated [skills index](./index.md) is its own file with its own single `#` heading (it is no longer embedded in that file). The LLM instructions lint (`scripts/lint-llm-instructions.ps1`) verifies the context file keeps exactly one H1 and that the index matches the generator; run with `-Fix` to regenerate the index.

Plain ASCII/Unicode flow diagrams or command output examples use `text`.
Use `mermaid` only for blocks that contain valid Mermaid syntax such as
`graph`, `flowchart`, `sequenceDiagram`, or `classDiagram`.

---

## Escaping Example Links in Documentation

> **CRITICAL**: When showing link syntax examples, ALL examples MUST be escaped so the linter doesn't parse them as real links.

### Escaping Methods

#### Fenced Code Blocks (Recommended)

Use `text` language specifier with escaped brackets:

```text
<!-- Examples with escaped brackets are NOT parsed -->
Correct: ]\(./file)
Wrong: ]\(file) -- missing prefix
```

#### Inline Backticks

For brief inline mentions, escape the brackets:

```text
Use `]\(./file)` format not `]\(file)` format.
```

#### Text Tables

For comparison tables, use `text` code blocks:

```text
❌ WRONG: [link](file.md)     →  ✅ CORRECT: [link](./file.md)
```

---

## Spelling Validation

### Required Command

```bash
# MANDATORY: Run after ANY markdown or code comment changes:
npm run lint:spelling
```

### Handling Spelling Errors

1. **If it's a typo**: Fix the spelling
2. **If it's a valid technical term**: Add it to the appropriate dictionary in `cspell.json`

### cspell.json Dictionary Categories

| Dictionary      | Purpose                           | Examples                                       |
| --------------- | --------------------------------- | ---------------------------------------------- |
| `unity-terms`   | Unity API names and types         | MonoBehaviour, ScriptableObject, GetComponent  |
| `csharp-terms`  | C# language features and keywords | async, ValueTask, Nullable, stackalloc         |
| `package-terms` | Package-specific types and names  | WGroup, SerializableDictionary, WButton, KGuid |
| `tech-terms`    | Technical/industry terminology    | IL2CPP, PRNG, SEO, SSE, SIMD, OAuth            |
| `words`         | General words not fitting above   | cancelable, performant, unoptimized            |

---

## Markdown Quality Checklist

**Before committing ANY markdown changes:**

- [ ] **ALL internal links use `./` or `../` prefix**
- [ ] **NO backtick-wrapped markdown file references**
- [ ] All fenced code blocks have language specifiers
- [ ] No emphasis (bold/italic) used as headings
- [ ] Blank lines before and after code blocks
- [ ] Blank lines before and after lists
- [ ] Blank lines after headings
- [ ] Proper heading hierarchy (no skipping levels)
- [ ] **`npm run lint:docs` passes**
- [ ] `npm run lint:markdown` passes
- [ ] `npm run format:md:check` passes

---

## Auto-Fix Commands

```bash
# Auto-fix Prettier formatting issues
npm run format:md

# Markdownlint issues usually require manual fixes
# Review the error message and fix the specific issue
```

---

## Related Skills

- [update-documentation](./update-documentation.md) — Core documentation requirements
- [validate-before-commit](./validate-before-commit.md) — Pre-commit validation workflow
- [linter-reference](./linter-reference.md) — Detailed linter commands and configurations
- [manage-skills](./manage-skills.md) — Skill file maintenance and formatting
