# git-hook-syntax-portability - Part 2

## Split Content

## Portable Grep & Stderr Hygiene (CRITICAL)

Enforced by `test-shell-portability.sh` (`npm run test:shell-portability`).

**Grep portability rules:**

- `\|` alternation is a GNU BRE extension — always use `grep -E` with `|` instead
- `\s` is Perl-compatible, not POSIX — always use `[[:space:]]`

```bash
# WRONG - GNU-only patterns
grep -q 'error\|warning' file.txt
grep -E '^\s*#' file.txt

# CORRECT - POSIX-portable
grep -qE 'error|warning' file.txt
grep -E '^[[:space:]]*#' file.txt
```

**Stderr suppression (`2>/dev/null`) rules:**

- **Allowed**: tool detection (`command -v`), process cleanup (`kill`), grep no-match, git exploratory calls, version checks
- **Forbidden on lint/validation tools**: cspell, prettier, markdownlint, lint-cspell-config.js — their stderr contains actionable diagnostic output

### Git Path Parsing Must Not Assume Single-Field Paths

Structured git output often places the path after several whitespace-delimited fields. Do not parse paths with fixed-field extraction like `awk '{print $4}'`, because valid git paths can contain spaces.

```bash
# WRONG - truncates paths containing spaces
file_path=$(echo "$line" | awk '{print $4}')

# CORRECT - strip the first three metadata fields, keep the remainder verbatim
mode="${line%% *}"
file_path="${line#* }"
file_path="${file_path#* }"
file_path="${file_path#* }"
```

**Rule**: When parsing `git ls-files -s` or similar output, treat the path as the remainder after the metadata fields, not as a fixed single field.

### Repo-Root Anchoring for `git ls-files` and Similar Commands

If a script derives `REPO_ROOT` / `$repoRoot` from its own location, every git command that enumerates repo-relative paths must be anchored to that root as well. Otherwise, invoking the script from a subdirectory silently changes the meaning of the returned paths.

```bash
# WRONG - output becomes relative to the caller's cwd
git ls-files -z -- '*.md'

# CORRECT - anchor git's working directory at the repository root
git -C "$REPO_ROOT" ls-files -z -- '*.md'

# ALSO CORRECT - cd once at script startup, then run relative git commands
cd "$REPO_ROOT"
git ls-files -z -- '*.md'
```

```powershell
# WRONG - relative paths depend on the caller's cwd
$gitFiles = git ls-files --cached --others --exclude-standard

# CORRECT - anchor git and any filesystem fallback at the repository root
$gitFiles = & git -C $repoRoot ls-files --cached --others --exclude-standard
$fallback = Get-ChildItem -Path $repoRoot -Recurse -File
```

**Rule**: If the script later resolves those paths with `Join-Path $repoRoot ...` or `"$REPO_ROOT/$path"`, you MUST either `cd` to the repo root first or pass `-C $repoRoot` on the git command. Do not mix repo-root-derived filesystem paths with caller-cwd-derived git output.

---

## `git check-ignore` Requires Repo-Relative POSIX Paths (CRITICAL)

> **CRITICAL**: `git check-ignore` silently **misclassifies** absolute paths — especially on Windows — by returning "not ignored" (exit 1) for files that ARE gitignored.

This matters because safety gates (e.g., `agent-preflight.ps1`'s auto-delete guard) refuse to remove files that appear "not gitignored." A silent misclassification turns a cleanable stray artifact into a permanent manual-review task.

**The rule**: always normalize to **repo-relative POSIX** (forward-slash) before invoking `git check-ignore`. The same rule applies to any git plumbing command documented as repo-relative (`git ls-files -- <path>`, `git diff --relative`, etc.).

```powershell
# WRONG - absolute Windows-style path; git check-ignore may misclassify.
& git -C $repoRoot check-ignore -q -- "C:\repo\scripts\pre-commit.log"

# CORRECT - normalize first via scripts/git-path-helpers.ps1.
. (Join-Path $PSScriptRoot 'git-path-helpers.ps1')
$relative = ConvertTo-GitRelativePosixPath -Path $absPath -RepoRoot $repoRoot
if ($null -eq $relative) {
    # Path is outside the repo root — refuse the auto-delete gate.
    $unignoredFiles.Add($absPath) | Out-Null
    continue
}
& git -C $repoRoot check-ignore -q -- "$relative"
$checkExit = $LASTEXITCODE
# Exit codes: 0 = ignored, 1 = not ignored, 128 = error.
```

Reference helper: `scripts/git-path-helpers.ps1` — exposes `ConvertTo-GitRelativePosixPath -Path <abs-or-rel> -RepoRoot <abs>`:

- Absolute path inside repo → repo-relative POSIX (`scripts/foo.ps1`).
- Absolute path outside repo → `$null` (caller must refuse the gate).
- Relative path with `\` separators → forward-slash POSIX.
- Repo root itself → `.`.
- Case-insensitive prefix match on Windows (NTFS).

Regression tests: `scripts/tests/test-git-path-helpers.ps1` covers the helper in isolation; `scripts/tests/test-agent-preflight.ps1`'s `PathNormalize_*` cases exercise the end-to-end call path with a gitignored-stray fixture.

---

## Related Skills

- [git-hook-patterns](../skills/git-hook-patterns.md) - Hub: all hook pattern categories
- [git-hook-safety](../skills/git-hook-safety.md) - Index safety, permissions, templates
- [git-hook-lifecycle-debugging](../skills/git-hook-lifecycle-debugging.md) - Validation, config, errors, debugging
- [validate-before-commit](../skills/validate-before-commit.md) - Pre-commit validation commands
- [formatting](../skills/formatting.md) - CSharpier, Prettier, markdownlint workflow
