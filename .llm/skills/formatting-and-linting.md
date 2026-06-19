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

The repository uses git hooks in `.githooks/` as fast last-resort guards. Run
`npm run agent:preflight:fix` before committing for routine formatting,
spelling, Markdown, and documentation repairs.

### Installation

```bash
# One-time setup (configures git to use .githooks directory)
npm run hooks:install

# Or use the full installation script
pwsh -NoProfile -File scripts/install-hooks.ps1
```

### What the Hook Does

1. Delegates from the extensionless POSIX hook launcher to `.githooks/pre-commit.ps1`
2. Removes gitignored stray hook-output artifacts such as `pre-commit.txt`
3. Syncs version metadata when version-bearing files are staged
4. Adds missing final newlines on staged text files, then re-stages exact fixes
5. Runs LLM instruction and skill-size checks when `.llm/` files are staged
6. Checks staged C# files for forbidden `#region` directives
7. Checks for missing `.meta` files on staged files and auto-stages dirty existing `.meta` companions

Pre-commit intentionally does not run Prettier, CSharpier, cspell,
markdownlint, documentation link lint, EOL normalization, test lint,
duplicate-using lint, or broad license audits. Those checks belong in
`npm run agent:preflight:fix`,
`npm run validate:prepush`, and CI so the hook stays fast and reliable on native
Linux, macOS, and Windows hosts.

The repository also installs a `pre-merge-commit` hook that delegates to `pre-commit`. Git does NOT run `pre-commit` on merge commits by default, so without this delegation any file introduced through a merge (including manual conflict resolution) would bypass every validation. The April 2026 `PWS001` regression is the concrete incident this guards against.

Do not invoke extensionless hooks with `pwsh -File`; PowerShell `-File` targets
must be `.ps1` scripts on every supported host. For debugging, run
`.githooks/pre-commit.ps1` with PowerShell, or let Git execute
`.githooks/pre-commit` directly.

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
