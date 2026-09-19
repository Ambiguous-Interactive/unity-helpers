# formatting - Part 1

## Split Content

**Trigger**: **MANDATORY** after creating or modifying ANY file.

---

## The Golden Rules

1. **Format IMMEDIATELY** after each file change (never batch)
2. **Run the correct formatter** for the file type
3. **Verify formatting worked** before moving on

---

## Quick Reference

| File Type                | Formatter | Command                                          |
| ------------------------ | --------- | ------------------------------------------------ |
| C# (`.cs`)               | CSharpier | `dotnet tool run csharpier format .`             |
| Markdown (`.md`)         | Prettier  | `node scripts/run-prettier.js --write -- <file>` |
| JSON (`.json`,`.asmdef`) | Prettier  | `node scripts/run-prettier.js --write -- <file>` |
| YAML (`.yml`,`.yaml`)    | Prettier  | `node scripts/run-prettier.js --write -- <file>` |
| Config files             | Prettier  | `node scripts/run-prettier.js --write -- <file>` |

---

## CSharpier (C# Files)

> **CRITICAL**: `npm run agent:preflight`, `npm run validate:local`, and CI/CD will REJECT commits with CSharpier formatting issues. The local pre-push hook stays fast and does not run formatters.

### When to Run

Run **IMMEDIATELY** after:

- Creating a new `.cs` file
- Modifying an existing `.cs` file (even a single line)
- ANY edit to ANY `.cs` file - no exceptions

**NEVER:**

- Batch multiple C# file edits before running CSharpier
- Wait until "before commit" to format
- Assume the code is already formatted correctly

### Commands

```bash
# Format all C# files
dotnet tool run csharpier format .

# Format specific file
dotnet tool run csharpier format Runtime/Core/Helper/Buffers.cs

# Check without modifying
dotnet tool run csharpier check .

# The same check, wired into the local gates (fails with remediation if tools are not restored)
npm run format:csharp:check   # whole repo; also runs inside npm run validate:local
npm run format:csharp         # whole repo, formatting in place
```

`npm run agent:preflight` checks only the **changed** C# files, and `npm run agent:preflight:fix`
formats them and re-stages the staged ones. Formatting a file and then editing it again is the
common way to push unformatted C#, so the check runs after the fix rather than instead of it.

### Troubleshooting

```bash
# If "tool not found" error:
dotnet tool restore
```

---

## Prettier (Non-C# Files)

> **CRITICAL**: `npm run agent:preflight`, `npm run validate:local`, and CI/CD REJECT commits with Prettier issues. The local pre-push hook stays fast and does not run formatters. Run Prettier IMMEDIATELY after editing ANY non-C# file.

### When to Run

Run **IMMEDIATELY** after editing:

- `.md` - Markdown documentation
- `.json`, `.asmdef`, `.asmref` - JSON and Unity assembly definitions
- `.yaml`, `.yml` - YAML configuration
- `.js`, `.ts`, `.jsx`, `.tsx` - JavaScript/TypeScript
- `.css`, `.scss` - Stylesheets
- `.html` - HTML files
- Config files (`.prettierrc`, `.eslintrc`)
- **`.devcontainer/devcontainer.json`** - Dev container configuration (often missed!)

### Commands

```bash
# Format a single file (RECOMMENDED)
node scripts/run-prettier.js --write -- <file>

# Verify formatting
node scripts/run-prettier.js --check -- <file>

# Check all files
node scripts/run-prettier.js --check -- .

# Fix all files (emergency only)
node scripts/run-prettier.js --write -- .
```

### Workflow Pattern

```text
1. Edit the file
2. Run: node scripts/run-prettier.js --write -- <path/to/file>
3. Verify: node scripts/run-prettier.js --check -- <path/to/file>
4. Move to next file
5. Repeat for each file
```

---

## Markdownlint (Structural Rules)

> **CRITICAL**: Prettier handles formatting but does NOT fix structural markdown issues. Run BOTH Prettier AND markdownlint on markdown files.

### What Each Tool Catches

| Tool         | Catches                                          | Misses                          |
| ------------ | ------------------------------------------------ | ------------------------------- |
| Prettier     | Spacing, indentation, line wrapping              | Structural rules (MD028, MD031) |
| markdownlint | Heading hierarchy, blank lines, code block rules | Formatting/spacing issues       |

### Required Workflow for Markdown

```bash
# STEP 1: Format with Prettier IMMEDIATELY after editing
node scripts/run-prettier.js --write -- <file>

# STEP 2: Check structural rules
npm run lint:markdown

# STEP 3: Fix any errors, then re-run Prettier if you made changes

# STEP 4: Verify both pass
node scripts/run-prettier.js --check -- <file>
npm run lint:markdown
```

### Common Markdownlint Rules

| Rule  | Issue                        | Fix                                          |
| ----- | ---------------------------- | -------------------------------------------- |
| MD028 | Blank line inside blockquote | Remove blank line between consecutive quotes |
| MD031 | No blank line around fences  | Add blank line before and after code blocks  |
| MD032 | No blank line around lists   | Add blank line before and after lists        |
| MD022 | No blank line after headings | Add blank line after `#` headings            |
| MD040 | Fenced code without language | Add language specifier (`csharp`, `bash`)    |

For complete markdown rules, see [markdown-reference](../skills/markdown-reference.md).

---
