# Skill: Validation Troubleshooting

<!-- trigger: error, ci, failure, troubleshoot, fix, dead link, lychee, broken link, transient, network, timeout, exclusion | Common validation errors, CI failures, fixes | Core -->

**Trigger**: When you encounter validation errors, CI failures, or linting issues.

For the quick validation workflow, see [validate-before-commit](./validate-before-commit.md).
For detailed linter commands, see [linter-reference](./linter-reference.md).

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

**Mixed endings after newline fix**: If a script appended LF to a CRLF file, detect existing endings first. See [`crlf_aware_append_newline`](../code-samples/patterns/ValidationFixPatterns.sh) and [git-hook-syntax-portability](./git-hook-syntax-portability.md#crlf-aware-newline-handling) for patterns.

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

**Prevention**: After creating ANY new file or folder in the Unity package directories (`Runtime/`, `Editor/`, `Tests/`, `Samples~/`), immediately run `./scripts/generate-meta.sh <path>`. Create parent folder meta files first, then child file meta files. See [create-unity-meta](./create-unity-meta.md).

### 12. Meta Lint False Positives From Tooling Artifacts

**Symptom**: `lint-meta-files.ps1` reports missing `.meta` files for directories like `.pytest_cache`, `__pycache__`, `.mypy_cache`, or files like `.DS_Store`, `Thumbs.db`, `.gitkeep`, `*.pyc`, `*.swp`.

**Cause**: New tooling was added that creates cache/artifact directories inside scanned source roots (`Runtime/`, `Editor/`, `Tests/`, `docs/`, `scripts/`, etc.), but the exclusion list in [lint-meta-files.ps1](../../scripts/lint-meta-files.ps1) was not updated.

**Fix**: Add the directory or file pattern to the appropriate exclusion array at the top of [lint-meta-files.ps1](../../scripts/lint-meta-files.ps1):

- `$excludeDirs` — for cache/artifact directories (excludes dir and all contents)
- `$excludeFilePatterns` — for file name patterns (glob-style)
- `$excludeDirPatterns` — for directory name patterns

**After fixing**: Add test cases to [test-lint-meta-exclusions.sh](../../scripts/tests/test-lint-meta-exclusions.sh) and run `bash scripts/tests/test-lint-meta-exclusions.sh`.

**Note on `Test-ShouldExclude`**: The function must match both the excluded directory itself AND its contents. Patterns like `$dir/*` alone are insufficient — you also need `$relativePath -eq $dir` and `$relativePath -like "*/$dir"` to match the directory entry itself. Without this, orphaned `.meta` files for excluded directories won't be detected.

### 13. Script Passes All Checks but CI Reports Exit Code 1

**Symptom**: A PowerShell lint script logs success messages and all checks pass, but the CI step or pre-commit hook reports a non-zero exit code.

**Cause**: `$LASTEXITCODE` leaking from a native command (git, npx, dotnet). PowerShell uses `$LASTEXITCODE` from the last native command as the process exit code when no explicit `exit` is given. Common culprit: `git check-ignore -q` returns exit code 1 when a file is NOT ignored (which is the success case for linters checking tracked files).

**Fix**: Add explicit `exit 0` on the success path of every PowerShell script. See [git-hook-lifecycle-debugging](./git-hook-lifecycle-debugging.md#powershell-lastexitcode-leaking-critical) for the full pattern.

**Prevention**: Every PowerShell script must end with explicit `exit 0` (success) or `exit 1` (failure) on all code paths. Never let a script fall through without an explicit exit.

### 13a. PowerShell Rejects Extensionless Git Hook

**Symptom**: Hook output contains:

```text
Processing -File '.githooks/pre-commit' failed because the file does not have a '.ps1' extension.
```

**Cause**: Something invoked an extensionless git hook launcher with
`pwsh -File`. Git hooks are named without extensions; PowerShell `-File` should
target the companion `.ps1` implementation.

**Fix**: Let Git run the hook during `git commit`, run `.githooks/pre-commit`
directly as an executable, or debug the implementation with
`pwsh -NoProfile -File .githooks/pre-commit.ps1`. Run
`npm run agent:preflight:fix` first so routine repairs happen before the hook.

**Prevention**: `scripts/lint-pwsh-invocations.ps1` emits `PWS004` for
automation that calls `pwsh -File .githooks/<hook>`.

### 14. Missing cspell Dictionary Entry for Valid Abbreviation

**Symptom**: `npm run lint:spelling` fails on a technical abbreviation or domain term that is valid.

**Fix**: Add the word to the appropriate dictionary in `cspell.json`. See the [cspell Dictionary Quick Reference](../context.md#cspell-dictionary-quick-reference) for which dictionary to use.

**Shell keywords in markdown code blocks**: When writing bash/shell code examples in `.md` files, non-English keywords like `esac`, `elif`, `getopts`, `mapfile`, and `printf` may trigger spelling failures. Common bash keywords (`if`, `then`, `else`, `case`, `done`, `fi`) are standard English words and are recognized by cspell, but language-specific terms are not. Add these to the `tech-terms` dictionary in `cspell.json`.

### 15. Documentation Link Points to File Not Yet Created

**Symptom**: `npm run lint:docs` fails with "file not found" for a link that references a documentation page being created as part of the same change.

**Fix**: Create all referenced documentation files before running the link linter. When adding cross-references between new docs, create the files in dependency order (referenced files first, then files that link to them).

### 16. Pre-Commit Hooks Not Catching CI Failures

**Symptom**: CI fails on issues hooks should have caught locally.

**Cause**: Extensionless hook entrypoints in `.githooks/` are not executable.

**Fix**: See [`fix_hook_permissions`](../code-samples/patterns/ValidationFixPatterns.sh) for the full sequence, or run:
`chmod +x .githooks/pre-commit .githooks/pre-merge-commit .githooks/pre-push .githooks/post-rewrite && git update-index --chmod=+x .githooks/pre-commit .githooks/pre-merge-commit .githooks/pre-push .githooks/post-rewrite`

### 17. Dead Link Failures (External URLs)

**Symptom**: `Check dead links (lychee)` step fails in CI

**Important**: Lychee only scans `.md` files, not `.cs` source files.

#### Diagnosing Link Failures

1. **Check CI output** for the failing URL and HTTP status code
2. **Verify manually** — open the URL in a browser from different networks
3. **Determine failure type** — transient (retry works) or permanent (site down/moved)

| Symptom                                | Type      | Evidence                                               |
| -------------------------------------- | --------- | ------------------------------------------------------ |
| Works in browser, fails in CI          | Transient | GitHub Actions runners have network restrictions       |
| 5xx errors that succeed on retry       | Transient | Server overload, temporary outage                      |
| Timeout with no response               | Transient | Network routing issues from specific datacenters       |
| 403/404 consistently across networks   | Permanent | Bot protection or content removed                      |
| Domain no longer resolves              | Permanent | Site shut down                                         |
| Redirects to different domain/homepage | Permanent | Content restructured, URL changed                      |
| Root domain works, specific paths fail | Transient | Academic/research sites with inconsistent availability |

#### Fix Strategies

| Failure Type                       | Fix                                              |
| ---------------------------------- | ------------------------------------------------ |
| HTTP to HTTPS redirect             | Update URL to use `https://`                     |
| Domain migration                   | Update to new domain                             |
| Permanently defunct site           | Add regex to `.lychee.toml` exclude list         |
| Bot protection / 403               | Add to `.lychee.toml` exclude list               |
| Transient 5xx error                | Already handled in `.lychee.toml` accept ranges  |
| Transient timeout (academic sites) | Add specific path to `.lychee.toml` exclude list |
| URL in source code only            | No CI action needed (consider updating docs)     |

#### When to Use Exclusions vs Update Links

**Use exclusions** when:

- Link is valid but site has bot protection (returns 403 to automated checks)
- Site is flaky but content is correct (academic sites, small servers)
- Transient network issues from GitHub Actions runners specifically
- Site returns errors but link is the canonical/correct reference

**Update documentation links** when:

- Content has moved to a new URL
- A better/more authoritative source exists
- Original site is permanently offline

#### Adding Exclusions to .lychee.toml

The configuration file is at repository root. Use regex patterns:

```toml
exclude = [
  # Academic sites with intermittent connectivity from GitHub Actions runners
  # Root domains work but specific paths timeout inconsistently
  "^https?://www\\.example-academic\\.org/paper\\.html",

  # Site permanently offline (reason)
  "^https?://defunct-site\\.example\\.com",

  # Bot protection (403 but link is valid)
  "^https?://protected-site\\.com"
]
```

**Best practices for exclusions**:

- Add a comment explaining WHY the exclusion is needed
- Use domain-level exclusions for network/connectivity issues (timeouts, unreachable)
- Use specific paths when only certain content paths fail consistently
- Escape dots in domain names (`\\.`)
- Use `^https?://` to match both HTTP and HTTPS
- Consider referencing the GitHub issue where the failure was investigated

#### Network Tuning in .lychee.toml

For transient failures, the config already includes:

```toml
timeout = 30            # seconds per request (increased for slow servers)
max_retries = 5         # retry transient failures
retry_wait_time = 3     # seconds between retries
accept = ["200..=299", "429", "500..=599"]  # Accept server errors as transient
```

If a site fails despite these settings, it likely needs an exclusion.

When updating URLs, check consistency between source code metadata (`.cs` files) and documentation (`.md` files).

### 18. CS0012/CS0311: Missing Optional Dependency DLL in Assembly Definition

**Symptom**: Compilation fails with:

```text
error CS0012: The type 'SerializedMonoBehaviour' is defined in an assembly that is not referenced.
You must add a reference to assembly 'Sirenix.Serialization, Version=1.0.0.0, ...'
```

or:

```text
error CS0311: The type 'X' cannot be used as type parameter 'T' in the generic type or method 'Y'.
There is no implicit reference conversion from 'X' to 'UnityEngine.MonoBehaviour'.
```

**Cause**: The assembly definition has `overrideReferences: true` and directly compiles source that references Odin/Sirenix types, but the matching Sirenix DLL is missing from `precompiledReferences`. Runtime conditional Odin base aliases are package-owned and guarded; do not add Sirenix DLLs merely because another assembly references `WallstopStudios.UnityHelpers`.

**Fix**: Add the exact Sirenix DLL used by the affected source to the `precompiledReferences` array in that `.asmdef` file:

```json
"precompiledReferences": [
  "nunit.framework.dll",
  "Sirenix.Serialization.dll"
]
```

**Prevention**: The [manage-assembly-definitions](./manage-assembly-definitions.md) skill documents this requirement in detail. Add Sirenix DLLs only to assemblies that directly compile Odin-specific source, and gate those files with `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR`.

**Linter**: `pwsh -NoProfile -File scripts/tests/test-sync-script-contracts.ps1` verifies optional Odin guard and asmdef contracts, including guarded runtime Odin base aliases with Unity fallbacks and Odin test/editor asmdefs defining `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR` from `odininspector`.

---

## Debugging Failed CI Runs

1. **Check which check failed** in GitHub Actions output:
   - `validate:content` — Documentation/formatting
   - `lint:csharp-naming` — C# naming convention
   - `eol:check` — Line endings
   - `validate:tests` — Test lifecycle

2. **Reproduce locally**: `npm run validate:prepush`

3. **Fix and verify**: Fix the issue, run the specific command, then run `npm run validate:prepush`

---

## Special Cases

### Pre-Existing Warnings

Check if a warning exists on main before fixing (see [`check_preexisting`](../code-samples/patterns/ValidationFixPatterns.sh)). If it exists on main, it's pre-existing and doesn't block your PR.

### Conflicts Between Linters

Resolution priority: **Prettier** (formatting) > **Markdownlint** (structure) > **CSpell** (spelling — always fix or add to dictionary).

### Files That Should Be Ignored

Add files to `.prettierignore`, `.markdownlintignore`, or `cspell.json` `ignorePaths` as appropriate.

---

## PowerShell Exit Code Linter (Potential Safeguard)

Several `scripts/*.ps1` files end without an explicit `exit 0` on their success paths (e.g., `lint-staged-markdown.ps1`, `format-staged-csharp.ps1`, `format-staged-prettier.ps1`). A static linter could check that every script-level `.ps1` file (excluding libraries like `git-staging-helpers.ps1`) has an explicit `exit` as the last statement.

**Simple heuristic**: Parse each `.ps1`, skip trailing whitespace/comments/closing braces, and verify the last meaningful statement is `exit $something` or `exit <number>`. Scripts ending with a closing `}` from an `if` block that contains `exit` on both branches pass, but scripts that fall through after a conditional exit fail.

**Current state**: The extensionless pre-commit launcher delegates to `.githooks/pre-commit.ps1`, which treats non-zero child script exits as hook failures. The primary remaining risk is direct CI or developer invocation of `.ps1` scripts that fall through without an explicit success exit. Most lint scripts already have proper `exit 0/1` on all paths. Monitor for future regressions.

---

## Quick Recovery Commands

See [`quick_recovery`](../code-samples/patterns/ValidationFixPatterns.sh) for the full script, or run individually:
`node scripts/run-prettier.js --write -- .` | `npm run eol:fix` | `dotnet tool run csharpier format .` | `npm run validate:prepush`

---

## Related Skills

- [validate-before-commit](./validate-before-commit.md) — Quick validation workflow
- [linter-reference](./linter-reference.md) — Detailed linter commands
- [formatting](./formatting.md) — CSharpier, Prettier, markdownlint workflow
- [markdown-reference](./markdown-reference.md) — Link formatting, structural rules
