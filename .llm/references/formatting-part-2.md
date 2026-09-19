# formatting - Part 2

## Split Content

## EOL Normalization

**All files must have CRLF line endings** (except `.sh` files which use LF).

### Commands

```bash
# Check for EOL issues
npm run eol:check

# Auto-fix EOL issues
npm run eol:fix
```

### When to Run

- After creating ANY new file
- Before committing (`npm run agent:preflight:fix`; pre-commit intentionally does not run EOL normalization)
- When CI fails with "LF issues" error

> **Why CRLF?** Unity projects require consistent line endings. Linux dev containers create files with LF by default, causing diffs and CI failures.

---

## Line Ending Configuration

Line endings are configured across multiple files that must stay synchronized:

| File               | Purpose                      |
| ------------------ | ---------------------------- |
| `.gitattributes`   | Controls git checkout        |
| `.prettierrc.json` | Controls Prettier formatting |
| `.yamllint.yaml`   | Controls YAML linting        |
| `.editorconfig`    | Controls IDE behavior        |

### LF Exceptions

These file types use LF (unix) endings:

- `.md` - Markdown files
- `.yml`, `.yaml` - YAML files
- `.sh` - Shell scripts
- `package.json`, `package-lock.json`

If you see line ending errors, verify all config files are synchronized. See [validation-troubleshooting](../skills/validation-troubleshooting.md) for details.

---

## Full Workflow

After making changes:

```bash
# 1. Format non-C# files with Prettier
node scripts/run-prettier.js --write -- <file>

# 2. Format C# code with CSharpier
dotnet tool run csharpier format .

# 3. Normalize line endings
npm run eol:fix

# 4. Generate meta files for any new files
./scripts/generate-meta.sh <new-file-path>

# 5. Spell-check any file covered by cspell (C#, tests, JSON, YAML, CHANGELOG, skills, docs)
npm run lint:spelling

# 6. Run all validations
npm run validate:local
```

See [Rule 4: Spell-Check Every Change cspell Covers](../skills/validate-before-commit.md#rule-4-spell-check-every-change-cspell-covers) for the spell-check failure-recovery decision tree.

---

## Common Mistakes

### Wrong: Missing Final Newline

Files must end with a newline character. Prettier will reject files without one. The pre-commit hook may add a missing final newline to staged text files, but run the normal checks before relying on hooks:

```bash
# Check for missing final newlines
npm run test:final-newline

# Or use validate-formatting.sh
./scripts/validate-formatting.sh

# Auto-fix all missing newlines
./scripts/validate-formatting.sh --fix
```

### Wrong: Batching Formatting Until End

```text
1. Edit file1.md
2. Edit file2.json
3. Edit file3.cs
4. Run formatters at the end  <-- TOO LATE! You will forget files.
```

### Correct: Format Immediately After Each

```text
1. Edit file1.md -> node scripts/run-prettier.js --write -- file1.md
2. Edit file2.json -> node scripts/run-prettier.js --write -- file2.json
3. Edit file3.cs -> dotnet tool run csharpier format .
```

### Wrong: Only Formatting One Type

Prettier formats JSON, YAML, and JavaScript too, not just markdown.

### Wrong: Forgetting Config Files

Files like `.devcontainer/devcontainer.json`, `.config/dotnet-tools.json`, and `package.json` are all checked by prettier. When these files are updated (by tooling, CI, or manual edits), they must be formatted before committing.

### Wrong: Skipping Verification

Always verify with `--check` to confirm formatting succeeded.

---

## Push-Prep Enforcement

`npm run agent:preflight`, `npm run validate:local`, and CI enforce formatting. The local pre-push hook intentionally stays fast and does not run formatters.

If validation fails:

1. Run `node scripts/run-prettier.js --write -- .` to fix non-C# files
2. Run `dotnet tool run csharpier format .` to fix C# files
3. Run `npm run eol:fix` to fix line endings
4. Commit the formatting changes
5. Validate again

---

## Integration with Editor

The repository's `.editorconfig` defines all formatting rules. CSharpier reads these settings automatically. Key rules enforced:

- 4-space indentation for `.cs` files
- `using` directives inside namespace
- Braces on new lines
- No trailing whitespace trimming (preserved)
- CRLF line endings for most files

---

## Skill File and Context Additional Requirements

Skill files (`.llm/skills/*.md`) and [context](../context.md) have additional size constraints beyond formatting:

```bash
# After editing ANY skill file or .llm/context.md, also run:
pwsh -NoProfile -File scripts/lint-skill-sizes.ps1
```

Files exceeding 500 lines will be rejected by the pre-commit hook. See [manage-skills](../skills/manage-skills.md) for the complete skill editing workflow.

---

## Related Skills

- [markdown-reference](../skills/markdown-reference.md) - Link formatting, structural rules
- [linter-reference](../skills/linter-reference.md) - Detailed linter commands
- [validate-before-commit](../skills/validate-before-commit.md) - Pre-commit validation workflow
- [validation-troubleshooting](../skills/validation-troubleshooting.md) - Common errors and fixes
- [manage-skills](../skills/manage-skills.md) - Skill file maintenance and size limits
