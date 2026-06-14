# Skill: Formatting and Linting

<!-- trigger: format, lint, prettier, pre-commit, markdown-table | Before committing, after editing files | Core -->

**Trigger**: Before committing any changes or when CI formatting checks fail.

---

## Quick Fix for Formatting Failures

```bash
# Fix ALL Prettier formatting issues at once
npm run format:fix

# Or use the validation script with auto-fix
bash scripts/validate-formatting.sh --fix

# Check everything passes
npm run format:check
```

---

## Pre-Commit Hook Setup

The repository uses git hooks in `.githooks/` to auto-format staged files on commit.

### Installation

```bash
# One-time setup (configures git to use .githooks directory)
npm run hooks:install

# Or use the full installation script
bash scripts/install-hooks.sh
```

### What the Hook Does

1. Syncs versions (banner SVG + [LLM context](../context.md) from `package.json`; issue template dropdowns from `package.json`, the [CHANGELOG](../../CHANGELOG.md), and git tags)
2. Normalizes line endings (CRLF/LF per file type)
3. Runs path-scoped documentation link lint on staged Markdown/docs/source files
4. Formats staged files with Prettier (Markdown, JSON, YAML, JS)
5. Formats staged C# files with CSharpier
6. Audits and auto-fixes staged C# license year headers
7. Adds missing Markdown fence languages where inferable, then runs markdownlint on staged Markdown files
8. Runs CHANGELOG lint when [CHANGELOG](../../CHANGELOG.md) is staged, plus YAML lint, Dependabot config schema lint, spell check (with copy-pasteable cspell.json patch for unregistered lint-error-code prefixes), LLM instruction lint, and test lint
9. Checks staged C# files for duplicate using directives (`UNH007`)
10. Checks for forbidden `#region` directives
11. Checks drawer/editor files for missing multi-object editing support (GenericMenu without `hasMultipleDifferentValues`)
12. Checks Odin drawer Undo safety (WeakTargets null-filtering before `Undo.RecordObjects`)
13. Checks for missing `.meta` files on staged files (auto-stages existing `.meta` companions)

The repository also installs a `pre-merge-commit` hook that delegates to `pre-commit`. Git does NOT run `pre-commit` on merge commits by default, so without this delegation any file introduced through a merge (including manual conflict resolution) would bypass every validation. The April 2026 `PWS001` regression is the concrete incident this guards against.

### If the Hook Wasn't Active

If a commit bypassed the hook (e.g., `--no-verify`, hook not installed), fix locally:

```bash
# Fix formatting
npm run format:fix

# Run full validation
npm run validate:prepush
```

---

## Markdown File References

When referencing markdown files in documentation, always use proper markdown link syntax with a relative path prefix. Never use bare filenames or inline-code-wrapped filenames. The [lint-doc-links.ps1](../../scripts/lint-doc-links.ps1) script enforces this in CI and supports `-Paths` for fast hook checks.

```markdown
<!-- Wrong: bare or backtick-wrapped references -->

See `formatting-and-linting.md` for details.
See formatting-and-linting.md for details.

<!-- Correct: proper markdown link with relative prefix -->

See [formatting-and-linting](./formatting-and-linting.md) for details.
```

---

## How to Fix Formatting Issues

### Markdown Files

```bash
node scripts/run-prettier.js --write -- "path/to/file.md"
```

### JSON / Assembly Definition Files

```bash
node scripts/run-prettier.js --write -- "path/to/file.json"
node scripts/run-prettier.js --write -- "path/to/file.asmdef"
```

### All Files at Once

```bash
npm run format:fix
```

---

## Common Formatting Pitfalls with Markdown Tables

Markdown tables are the most common source of Prettier failures. Prettier enforces:

### 1. Consistent Column Padding

Prettier pads all cells to the width of the widest entry in each column:

```markdown
<!-- Wrong: inconsistent padding -->

| Short | Description |
| ----- | ----------- |
| x     | A value     |

<!-- Right: Prettier-formatted -->

| Short | Description |
| ----- | ----------- |
| x     | A value     |
```

### 2. Pipe-Escaped Characters

Literal pipe characters inside table cells must be escaped as `\|`:

```markdown
<!-- Wrong -->

| Method | Signature  |
| ------ | ---------- | --- |
| Foo    | void Foo(A | B)  |

<!-- Right -->

| Method | Signature      |
| ------ | -------------- |
| Foo    | void Foo(A\|B) |
```

### 3. Inline Code in Tables

Backtick-wrapped code in tables is fine, but watch for:

- Long inline code stretching columns (Prettier preserves it but realigns padding)
- Backticks containing pipes don't need escaping: `` `A|B` `` is OK inside backticks

### 4. Multi-line Content

Markdown tables don't support multi-line cells. If you need complex content, consider using a definition list or separate sections instead of a table.

---

## Verification Commands

| What to Check         | Command                               |
| --------------------- | ------------------------------------- |
| All formatting        | `npm run format:check`                |
| Markdown only         | `npm run format:md:check`             |
| JSON/asmdef only      | `npm run format:json:check`           |
| YAML only             | `npm run format:yaml:check`           |
| JavaScript only       | `npm run format:js:check`             |
| Full CI-like check    | `npm run validate:content`            |
| Everything (pre-push) | `npm run validate:prepush`            |
| Standalone script     | `bash scripts/validate-formatting.sh` |

---

## Related Skills

- [formatting](./formatting.md) - Detailed formatter usage (CSharpier, Prettier, markdownlint)
- [validate-before-commit](./validate-before-commit.md) - Full pre-commit validation workflow
- [linter-reference](./linter-reference.md) - All linter commands and configurations
- [validation-troubleshooting](./validation-troubleshooting.md) - Common errors and fixes
