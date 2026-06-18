# Skill: License Headers

<!-- trigger: license, copyright, header, MIT, year | Maintaining MIT license headers in C# files | Core -->

**Trigger**: When creating or modifying `.cs` files, or when license year audit fails in CI.

---

## When to Use

- Creating any new `.cs` file (header is mandatory)
- Fixing CI failures from `license-year-audit.yml`
- Bulk-updating license headers after year boundary
- Reviewing files with mismatched copyright years

---

## When NOT to Use

- Non-C# files (only `.cs` files require headers)
- Files that already have correct headers (audit script confirms)

---

## Required Header Format

Every `.cs` file MUST start with exactly these two lines:

```csharp
// MIT License - Copyright (c) {YEAR} wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
```

### Year Rules

| Scenario               | Year to Use                                  |
| ---------------------- | -------------------------------------------- |
| New file               | Current calendar year                        |
| Existing file          | Year the file was **first committed** to git |
| File not yet committed | Current calendar year                        |

The audit script determines the git creation year via:

```bash
git log --follow --diff-filter=A --format=%ad --date=format:%Y -- <file>
```

### Common Mistakes

- Using a hardcoded past year (e.g., `2025`) when creating a file in 2026
- Copy-pasting a header from another file without updating the year
- Using the project start year instead of the file creation year
- Using `Eli Pinkerton` instead of `wallstop` as the copyright holder

---

## Validation

### Local Check

```bash
# Quick summary (pass/fail)
bash scripts/audit-license-years.sh --summary

# Detailed CSV output showing each file
bash scripts/audit-license-years.sh --csv

# Auto-fix all mismatched headers
bash scripts/update-license-headers.sh

# Dry-run auto-fix (shows what would change)
bash scripts/update-license-headers.sh --dry-run
```

### CI Enforcement

- **Workflow**: `license-year-audit.yml` runs on any push/PR touching `.cs` or `LICENSE` files
- **Local validation**: `npm run agent:preflight`, `npm run validate:prepush`, and CI provide the license-header safety net before push

### What the Audit Checks

1. **Missing headers**: Files with no `// MIT License - Copyright (c)` on line 1
2. **Mismatched years**: Header year doesn't match the git creation year
3. **Wrong author**: Header says anything other than `wallstop`

---

## Fixing Failures

### Auto-Fix (Recommended)

```bash
# Fix all files at once
bash scripts/update-license-headers.sh

# Then verify
bash scripts/audit-license-years.sh --summary
```

The update script:

- Corrects copyright years based on git creation date
- Replaces `Eli Pinkerton` with `wallstop`
- Adds missing two-line headers to files that lack them
- Supports `--dry-run` to preview changes

### Manual Fix

For individual files, edit line 1:

```csharp
// MIT License - Copyright (c) 2026 wallstop
```

Use the git creation year:

```bash
git log --follow --diff-filter=A --format=%ad --date=format:%Y -- path/to/File.cs
```

---

## Prevention

1. **Always use the file template** from [create-csharp-file](./create-csharp-file.md) — it includes the correct header with `{CURRENT_YEAR}`
2. **Pre-commit hook** runs the staged C# license audit and scoped auto-fix before commit
3. **`npm run agent:preflight`** checks changed C# files before push-time last-resort hooks are involved
4. **`npm run validate:prepush` / CI** provide full-repo safety nets

---

## Related Skills

- [create-csharp-file](./create-csharp-file.md) — File template with license header
- [validate-before-commit](./validate-before-commit.md) — Pre-commit validation checklist
- [formatting-and-linting](./formatting-and-linting.md) — Overall formatting workflow
