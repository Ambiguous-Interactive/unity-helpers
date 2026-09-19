# linter-reference - Part 3

## Split Content

## Meta File Linter

### Command

```bash
pwsh -NoProfile -File scripts/lint-meta-files.ps1
pwsh -NoProfile -File scripts/lint-meta-files.ps1 -VerboseOutput  # detailed output
```

### What It Checks

Every file and directory under scanned source roots (`Runtime`, `Editor`, `Tests`, `Samples~`, `Shaders`, `Styles`, `URP`, `docs`, `scripts`) has a corresponding `.meta` file, and every `.meta` file has a corresponding source file/directory.

### Exclusion Configuration

The script excludes certain paths from requiring `.meta` files. Exclusions are defined in three arrays at
the top of [lint-meta-files.ps1](../../scripts/lint-meta-files.ps1):

| Array                  | Purpose                                          | Examples                                                      |
| ---------------------- | ------------------------------------------------ | ------------------------------------------------------------- |
| `$excludeDirs`         | Directories excluded entirely (and all contents) | `node_modules`, `.pytest_cache`, `__pycache__`, `.mypy_cache` |
| `$excludeFilePatterns` | File name/glob patterns excluded                 | `.gitkeep`, `.DS_Store`, `Thumbs.db`, `*.pyc`, `*.swp`        |
| `$excludeDirPatterns`  | Directory name patterns excluded                 | `Samples~`                                                    |

### Adding New Exclusions

When introducing new tooling that creates cache or artifact directories inside source roots:

1. Add the directory name to `$excludeDirs` (for directories) or file pattern to `$excludeFilePatterns`
   (for files)
2. Add test cases to [test-lint-meta-exclusions.sh](../../scripts/tests/test-lint-meta-exclusions.sh)
3. Run the tests: `bash scripts/tests/test-lint-meta-exclusions.sh`

### Test-ShouldExclude Function

The `Test-ShouldExclude` function checks whether a path should be excluded. For `$excludeDirs` entries,
it matches:

- The directory itself: `$relativePath -eq $dir` or `$relativePath -like "*/$dir"`
- Contents at root level: `$relativePath -like "$dir/*"`
- Nested contents: `$relativePath -like "*/$dir/*"`

Patterns must match **both** the excluded directory itself **and** its contents. If only contents are
matched (e.g., `$dir/*` without `$dir`), orphaned `.meta` files for the directory itself won't be
detected correctly.

### Tests

```bash
bash scripts/tests/test-lint-meta-exclusions.sh
```

Tests cover all exclusion categories: tooling cache dirs, OS metadata, git placeholders, compiled bytecode, and editor temp files.

## NPM Script Breakdown

### validate:local

Runs these in sequence:

1. `validate:content` (docs + formatting)
2. `eol:check`
3. `validate:tests:fast` (CI's full `validate:tests` also runs synthetic hook regressions)
4. `lint:csharp-naming`

### validate:prepush

Runs only the fast `validate:git-push-config` safety check. Repository-wide lint and contract
suites belong in targeted developer commands, `validate:local`, and CI—not in the push path.

### validate:content

Runs these in sequence:

1. `lint:docs`
2. `lint:markdown`
3. `format:md:check`
4. `format:json:check`
5. `format:yaml:check`

## Shared Helpers

| Helper                          | Purpose                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| ------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `scripts/comment-stripping.ps1` | Language-aware comment masker used by `lint-doc-links`, `lint-csharp-naming`, `lint-odin-undo-safety`, `lint-drawer-multiobject`, `lint-tests`. Replaces comment characters with spaces while preserving line/column offsets so downstream regex scans don't false-positive on commented-out code. Public API: `Get-LanguageFromExtension`, `Get-CommentMaskedLines`, `Get-CommentRanges`. Pinned by `scripts/tests/test-comment-stripping.ps1`. |
| `scripts/git-path-helpers.ps1`  | Normalizes filesystem paths to repo-relative POSIX form for safe use with `git check-ignore` and related plumbing.                                                                                                                                                                                                                                                                                                                               |

When adding a new lint script that scans source-code text, prefer dot-sourcing `comment-stripping.ps1` over hand-rolling a comment scrubber.

## Configuration File Locations

| Tool         | Config File          | Purpose                |
| ------------ | -------------------- | ---------------------- |
| CSpell       | `cspell.json`        | Spell check dictionary |
| Markdownlint | `.markdownlint.json` | Markdown rules         |
| Prettier     | `.prettierrc.json`   | Formatting options     |
| Prettier     | `.prettierignore`    | Ignored files          |
| CSharpier    | `.csharpierrc.json`  | C# formatting options  |
| ESLint       | `.eslintrc.json`     | JavaScript linting     |
| EditorConfig | `.editorconfig`      | Editor settings        |

## Related Skills

- [validate-before-commit](../skills/validate-before-commit.md) — Quick validation workflow
- [validation-troubleshooting](../skills/validation-troubleshooting.md) — Common errors and fixes
- [formatting](../skills/formatting.md) — CSharpier, Prettier, markdownlint workflow
- [markdown-reference](../skills/markdown-reference.md) — Link formatting, structural rules
- [license-headers](../skills/license-headers.md) — License header year rules and auto-fix
