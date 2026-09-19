# bash-pwsh-invocation - Part 1

## Split Content

**Trigger**: When a bash script, git hook, GitHub Actions step, or test harness invokes a `.ps1` script through `pwsh -File` or `powershell -File`.

---

## The Rule

When calling a `.ps1` script from bash using `pwsh -File` or `powershell -File`, **always use explicit named parameters** (e.g. `-Paths "${ARR[@]}"`).

PowerShell `-File` targets must be `.ps1` scripts. Extensionless git hook
launchers such as `.githooks/pre-commit` run directly through Git/shell; for
PowerShell debugging, invoke `.githooks/pre-commit.ps1`.

**NEVER** use the POSIX `--` end-of-options separator:

```bash
# WRONG - PowerShell -File does NOT honor `--` and fails at parse time with:
#   Parameter cannot be processed because the parameter name '' is ambiguous.
pwsh -NoProfile -File scripts/lint-foo.ps1 -- "${FILES[@]}"

# CORRECT - explicit named parameter
pwsh -NoProfile -File scripts/lint-foo.ps1 -Paths "${FILES[@]}"
```

These rules are enforced by:

1. `scripts/lint-pwsh-invocations.ps1` — scans `*.sh`, `.githooks/*`, `.github/workflows/*.yml`, `scripts/**/*.ps1`, and `package.json` for `-File`/`-f <script> --` (code `PWS001`) and extensionless hook `-File`/`-f` targets (code `PWS004`), and reads `scripts/**/*.ps1` plus `.githooks/*.ps1` as an AST for `PWS005` and `PWS006`.
2. `.github/workflows/pwsh-invocations-lint.yml` — runs the lint on every PR that touches hook/workflow/script files.
3. `scripts/tests/test-precommit-integration.sh` — smoke-tests that each pwsh-invoked hook branch works.
4. `scripts/validate-lint-error-codes.ps1` — enforces that the `PWS` prefix (and any other lint-error-code prefix introduced by a new lint script) is registered in `cspell.json`, so the skill/doc tokens `PWS001`/`PWS002` do not trip the spell checker.

---

## Why `--` Fails Under `-File`

PowerShell has two CLI modes:

| Mode       | Behavior                                                                                                          |
| ---------- | ----------------------------------------------------------------------------------------------------------------- |
| `-Command` | Parses the rest as PowerShell syntax; `--` is a literal token.                                                    |
| `-File`    | Parses the rest as script parameters; `--` is treated as `-<empty-name>` and matches every parameter ambiguously. |

The in-process call operator `&` is a third path: it tolerates `--` because `ValueFromRemainingArguments` swallows it. This is the trap — **tests that use `& $script -- $path` pass even while production (using `pwsh -File`) fails.**

---

## Test Invocation Rule

**Tests for `.ps1` scripts MUST shell out via `pwsh -NoProfile -File`**, not the in-process `&` operator.

```powershell
# WRONG - masks CLI-binding bugs (PWS002)
$output = & $lintScriptPath -- $fixturePath *>&1

# CORRECT - same code path as production
$output = & pwsh -NoProfile -File $lintScriptPath -Paths $fixturePath *>&1
$exitCode = $LASTEXITCODE
```

The [lint-dependabot](../../scripts/lint-dependabot.ps1) regression (2026) shipped because tests used `&` and CLI-level binding was never exercised.

---

## `-Paths` Parameter Declaration Pattern

`pwsh -File` CLI mode binds the first token after `-Paths` to `-Paths` and leaves the rest for
positional binding. **Two things are needed, and the sibling alone is not enough.**

```powershell
# PositionalBinding = $false is what routes a stray value to the catch-all. Without it, the
# remainder is offered to every other named parameter positionally FIRST.
[CmdletBinding(PositionalBinding = $false)]
param(
    [switch]$VerboseOutput,
    [string[]]$Paths,
    # Catches what -File CLI mode does not bind to -Paths when multiple values follow.
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPaths
)

$allPaths = @()
if ($Paths) { $allPaths += $Paths }
if ($AdditionalPaths) { $allPaths += $AdditionalPaths }
```

### Why the sibling alone is not enough

Measured, not reasoned about. With positional binding left on (the default), the remainder is
offered to the other named parameters before it ever reaches the catch-all:

```text
param([string[]]$Paths, [string]$OutputDir = 'default', [VFRA][string[]]$AdditionalPaths)
  pwsh -File s.ps1 -Paths a b c
  ->  Paths=[a]  OutputDir=[b]  Additional=[c]       # 'b' silently became -OutputDir

param([switch]$VerboseOutput, [string[]]$Paths, [switch]$FixNullChecks, [VFRA][string[]]$AdditionalPaths)
  pwsh -File s.ps1 -Paths a b c
  ->  Paths=[a]  Additional=[b,c]                    # works -- but only because the neighbours are switches
```

So a script whose other parameters happen to all be `[switch]` is correct **by accident of its
parameter list**, and adding one `[string]` parameter later silently reintroduces the drop. A real
case: `ensure-editor.ps1 -RequiredEditorPayloadRelativePath a b` put `b` in `-InstallRoot`, so the
editor would have been installed to a directory named `b`. `PositionalBinding = $false` makes the
property structural instead of incidental.

**PWS005 enforces both halves.** It flags any script-level array parameter (`[string[]]`, `[int[]]`,
anything) that lacks either the `ValueFromRemainingArguments` sibling or
`[CmdletBinding(PositionalBinding = $false)]`, and it reports a script it cannot parse rather than
skipping it.

### What to do with the remainder

Merging it into the array parameter is right when there is exactly one array parameter. With two
there is no safe guess, so **refuse**: print the unbound values and `exit 64` (sysexits `EX_USAGE`).
Use `[Console]::Error.WriteLine` rather than `Write-Error` — under
`$ErrorActionPreference = 'Stop'` the latter terminates with exit 1, so the code would depend on
where in the file the guard sits. See [`ensure-editor.ps1`](../../scripts/unity/ensure-editor.ps1).

See [`lint-skill-sizes.ps1`](../../scripts/lint-skill-sizes.ps1) and [`lint-dependabot.ps1`](../../scripts/lint-dependabot.ps1) for the canonical shape.

**`PWS005` enforces this**, because writing it down was not enough: six scripts were missing it at
once, `lint-tests.ps1` among them, and [context](../context.md) documented a multi-file invocation of that
very script. The failure is silent -- `-Paths a b c` lints `a`, skips `b` and `c`, and prints
"No issues found in test code", which reads exactly like a pass.

---

## What The Lint Catches

- `PWS001`: `pwsh[.exe] -File|-f <script> --` or `powershell[.exe] -File|-f ... --`, including quoted `"--"` / `'--'`, in `*.sh`, `.githooks/*`, workflows, `scripts/**/*.ps1`, and `package.json`.
- `PWS001`: `"${PWSH_CMD[@]}" <script>.ps1 --` bash array indirection for PowerShell-named arrays such as `PWSH_CMD` or `POWERSHELL_CMD` in `*.sh`, `.githooks/*`, workflows, and `package.json`.
- `PWS002`: `& <script-var-or-path>.ps1 --` in `scripts/tests/*.ps1`.
- `PWS003`: top-level `scripts/*.ps1` invokes `pwsh[.exe]|powershell[.exe] -File|-f <sibling>`. Nested scripts and tests are excluded for this rule.
- `PWS004`: `pwsh[.exe]|powershell[.exe] -File|-f .githooks/<extensionless-hook>` in `*.sh`, `.githooks/*`, workflows, `scripts/**/*.ps1`, and `package.json`.
- `PWS005`: a script-level array parameter without BOTH a `ValueFromRemainingArguments` sibling and `[CmdletBinding(PositionalBinding = $false)]`.
- `PWS006`: `Start-Process -ArgumentList|-Args <array|@()|variable>`.

`PWS005` and `PWS006` are AST rules rather than line scans, and they read `scripts/**/*.ps1` plus
`.githooks/*.ps1`. Both skip files `git` reports as ignored: CI checks out tracked content only, so
a violation in a developer's scratch script is one the repository cannot fix and CI can never see.
An untracked file that is NOT ignored is still scanned -- a new script you forgot to stage is
exactly the one a rule should catch -- and where `git` cannot answer, nothing is excluded.

Detection beyond a single physical line:

- **Multi-line `\` continuation** — a pwsh invocation split across lines with trailing `\` is rejoined per bash/YAML semantics, then re-scanned. Comment lines (`^\s*#`) are NOT absorbed as continuations — bash ends a comment at EOL regardless of a trailing `\`.
- **YAML folded scalars (`run: >`)** — indented block bodies are folded into one command and scanned.
- **Comment exclusions** — a physical line whose first non-whitespace char is `#` (in `.sh`, `.yml`, `.yaml`, `.ps1`) is skipped.

Strings inside `<# ... #>` comment-based help blocks and here-strings in `.ps1` files are exempt (so documentation can still show historical bad patterns). PWS001, PWS002, PWS003, and PWS004 skip matches inside `"..."` / `'...'` PowerShell string literals so `Write-Host` help text that references an invocation is not flagged. PWS001 and PWS004 also skip shell/YAML `echo` and `printf` help text.

---
