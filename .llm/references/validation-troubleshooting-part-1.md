# validation-troubleshooting - Part 1

## Split Content

**Trigger**: When you encounter validation errors, CI failures, or linting issues.

For the quick validation workflow, see [validate-before-commit](../skills/validate-before-commit.md).
For detailed linter commands, see [linter-reference](../skills/linter-reference.md).

---

## Most Common CI Failures

### 1. Spelling Errors (Most Frequent)

**Symptom**: `npm run lint:spelling` fails

**Fix Options**:

1. **Correct the spelling** if it's actually wrong
2. **Add to dictionary** — add the word to `cspell.json` `"words"` array
3. **Inline ignore** for single occurrences: `<!-- cspell:ignore someword -->`

### 2. Prettier Formatting Failures

**Symptom**: `format:md:check`, `format:json:check`, or `format:yaml:check` fails

**Fix**: Run Prettier to auto-fix:

```bash
node scripts/run-prettier.js --write -- <file>
# Or fix all:
node scripts/run-prettier.js --write -- .
```

**Common gotchas**:

- **Missing final newline**: Prettier requires files to end with a newline. Fix with `node scripts/run-prettier.js --write -- <file>` or `printf '\n' >> <file>`
- **devcontainer.json**: Arrays within `printWidth: 100` get collapsed. Fix with `node scripts/run-prettier.js --write -- .devcontainer/devcontainer.json`
- **dotnet-tools.json**: LF line endings from Linux. Fix with `npm run format:json -- .config/dotnet-tools.json`; if persists, run `pwsh -NoProfile -File scripts/normalize-eol.ps1 -VerboseOutput`

### 3. Markdownlint Violations

**Symptom**: `npm run lint:markdown` fails

**Common fixes**:

| Error Code | Issue                    | Fix                                  |
| ---------- | ------------------------ | ------------------------------------ |
| MD007      | Wrong list indentation   | Use 2 spaces for nested lists        |
| MD009      | Trailing whitespace      | Remove trailing spaces               |
| MD012      | Multiple blank lines     | Reduce to single blank line          |
| MD022      | No blank around headings | Add blank line before/after headings |
| MD032      | No blank around lists    | Add blank line before/after lists    |

### 4. Backtick File Reference Errors

**Symptom**: `npm run lint:docs` fails with backtick reference warning

**Fix**: Use proper links instead of backtick-wrapped filenames:

```markdown
<!-- ❌ WRONG -->

See `context.md` for guidelines.

<!-- ✅ CORRECT -->

See [context](./context.md) for guidelines.
```

### 5. Link Without Relative Prefix

**Symptom**: `npm run lint:docs` fails with relative path warning

```markdown
<!-- ❌ WRONG -->

[create-test](create-test.md)

<!-- ✅ CORRECT -->

[create-test](./create-test.md)
```

### 6. Broken Internal Links

**Symptom**: `npm run lint:docs` fails with "file not found"

**Fix**: Verify file exists and path is correct — check for typos, moved/renamed files, or wrong prefix.

### 7. Missing Track() in Tests

**Symptom**: `npm run validate:tests` fails

**Fix**: Wrap Unity object creation with `Track()`. See [UnityObjectLifecycleTests.cs](../code-samples/testing/UnityObjectLifecycleTests.cs) for complete examples.

### 7a. Stale Allowlisted Helper Path in lint-tests.ps1

**Symptom**: `npm run validate:tests` or `pwsh -NoProfile -File scripts/lint-tests.ps1` fails immediately with:

```text
ERROR: Allowlisted helper file not found: Tests/Path/To/OldFile.cs
```

**Cause**: A test helper file listed in `$allowedHelperFiles` in [lint-tests.ps1](../../scripts/lint-tests.ps1) was moved, renamed, or deleted, but the allowlist was not updated to match. The script validates all allowlisted paths exist on startup and exits with code 1 if any are missing.

**Fix**: Update the `$allowedHelperFiles` array in [lint-tests.ps1](../../scripts/lint-tests.ps1) to reflect the file's new path (or remove the entry if the file was deleted).

**After fixing**: Run `pwsh -NoProfile -File scripts/tests/test-lint-tests.ps1` to verify the allowlist is self-consistent.

**Prevention**: When moving or renaming test helper files, always update `$allowedHelperFiles` in [lint-tests.ps1](../../scripts/lint-tests.ps1) in the same commit.

### 8. C# Naming Convention Violations

**Symptom**: `npm run lint:csharp-naming` fails

**Fix**: Methods use PascalCase (`ProcessData`), private fields use underscore prefix (`_count`), public members use PascalCase without underscore.

### 9. Line Ending Issues

**Symptom**: `npm run eol:check` fails

**Fix**: `npm run eol:fix`

**Mixed endings after newline fix**: If a script appended LF to a CRLF file, detect existing endings first. See [`crlf_aware_append_newline`](../code-samples/patterns/ValidationFixPatterns.sh) and [git-hook-syntax-portability](../skills/git-hook-syntax-portability.md#crlf-aware-newline-handling) for patterns.

**PowerShell `-NoNewline`**: Avoid raw `Set-Content -NoNewline` writes. Normalize first with:

```powershell
$content = $content.TrimEnd() + "`n"
Set-Content -Path $filePath -Value $content -NoNewline -Encoding UTF8
```

Run `npm run test:sync-script-contracts` after editing sync scripts to catch regressions early.

### 10. Gitignore Wildcard Too Broad

**Symptom**: Files in `docs/`, `.llm/`, or other important directories are missing from git / not tracked.

**Cause**: A wildcard pattern in `.gitignore` accidentally matches files in protected directories. For example, `failed-tests-*` matches [Failed Tests Exporter](../../docs/features/editor-tools/failed-tests-exporter.md).

**Fix**:

1. Narrow the pattern with a file extension (e.g., `failed-tests-*.txt` instead of `failed-tests-*`)
2. Verify with `git check-ignore -v docs/ .llm/` to confirm no important paths are excluded
3. Run `pwsh -NoProfile -File scripts/lint-gitignore-docs.ps1` to validate gitignore safety for docs

**Prevention**: When editing `.gitignore`, always validate that wildcard patterns don't accidentally exclude files in `docs/`, `.llm/`, or other important directories.

### 11. Missing .meta File for New Script or Asset

**Symptom**: Unity CI build fails with missing `.meta` file errors, or `git status` shows an untracked `.meta` file after someone else opens the project.

**Cause**: A new file was added to the repo but its corresponding `.meta` file was not generated and committed.

**Fix**: Generate the missing meta file:

```bash
./scripts/generate-meta.sh <path-to-file-or-folder>
```

**Prevention**: After creating ANY new file or folder in the Unity package directories (`Runtime/`, `Editor/`, `Tests/`, `Samples~/`), immediately run `./scripts/generate-meta.sh <path>`. Create parent folder meta files first, then child file meta files. See [create-unity-meta](../skills/create-unity-meta.md).

### 12. Meta Lint False Positives From Tooling Artifacts

**Symptom**: `lint-meta-files.ps1` reports missing `.meta` files for directories like `.pytest_cache`, `__pycache__`, `.mypy_cache`, or files like `.DS_Store`, `Thumbs.db`, `.gitkeep`, `*.pyc`, `*.swp`.

**Cause**: New tooling was added that creates cache/artifact directories inside scanned source roots (`Runtime/`, `Editor/`, `Tests/`, `docs/`, `scripts/`, etc.), but the exclusion list in [lint-meta-files.ps1](../../scripts/lint-meta-files.ps1) was not updated.

**Fix**: Add the directory or file pattern to the appropriate exclusion array at the top of [lint-meta-files.ps1](../../scripts/lint-meta-files.ps1):

- `$excludeDirs` — for cache/artifact directories (excludes dir and all contents)
- `$excludeFilePatterns` — for file name patterns (glob-style)
- `$excludeDirPatterns` — for directory name patterns

**After fixing**: Add test cases to [test-lint-meta-exclusions.sh](../../scripts/tests/test-lint-meta-exclusions.sh) and run `bash scripts/tests/test-lint-meta-exclusions.sh`.

**Note on `Test-ShouldExclude`**: The function must match both the excluded directory itself AND its contents. Patterns like `$dir/*` alone are insufficient — you also need `$relativePath -eq $dir` and `$relativePath -like "*/$dir"` to match the directory entry itself. Without this, orphaned `.meta` files for excluded directories won't be detected.
