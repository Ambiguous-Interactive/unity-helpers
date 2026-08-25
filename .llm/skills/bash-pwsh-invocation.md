# Skill: Bash to PowerShell Invocation

<!-- trigger: pwsh, powershell, -File, bash, --, invocation, end-of-options | Calling .ps1 scripts from bash/hooks/workflows | Core -->

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

1. `scripts/lint-pwsh-invocations.ps1` — scans `*.sh`, `.githooks/*`, `.github/workflows/*.yml`, `scripts/**/*.ps1`, and `package.json` for `-File`/`-f <script> --` (code `PWS001`) and extensionless hook `-File`/`-f` targets (code `PWS004`).
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

Detection beyond a single physical line:

- **Multi-line `\` continuation** — a pwsh invocation split across lines with trailing `\` is rejoined per bash/YAML semantics, then re-scanned. Comment lines (`^\s*#`) are NOT absorbed as continuations — bash ends a comment at EOL regardless of a trailing `\`.
- **YAML folded scalars (`run: >`)** — indented block bodies are folded into one command and scanned.
- **Comment exclusions** — a physical line whose first non-whitespace char is `#` (in `.sh`, `.yml`, `.yaml`, `.ps1`) is skipped.

Strings inside `<# ... #>` comment-based help blocks and here-strings in `.ps1` files are exempt (so documentation can still show historical bad patterns). PWS001, PWS002, PWS003, and PWS004 skip matches inside `"..."` / `'...'` PowerShell string literals so `Write-Host` help text that references an invocation is not flagged. PWS001 and PWS004 also skip shell/YAML `echo` and `printf` help text.

---

## PWS003: Prefer Dot-Source Over Subprocess Pwsh Inside `scripts/*.ps1`

When one `scripts/<name>.ps1` script needs behavior from a sibling, the Windows-portable choice is to **dot-source a shared helper module**, not to spawn a `pwsh -NoProfile -File` subprocess. Windows PowerShell 5.1 hosts (the default `powershell.exe`) do not ship with `pwsh` on PATH; a subprocess call with the `pwsh` executable silently fails on those hosts. Even where both hosts are present, the subprocess boundary drops the parent session's variables, doubles startup cost, and makes dependency graphs harder to reason about.

```powershell
# WRONG - PWS003. Breaks on Windows PowerShell 5.1 (no pwsh on PATH).
& pwsh -NoProfile -File $PSScriptRoot\configure-git-defaults.ps1 -RepoRoot $repoRoot

# CORRECT - dot-source a helper module that exports a function.
. (Join-Path $PSScriptRoot 'git-push-defaults-helpers.ps1')
$result = Set-RepoGitPushDefaults -RepoRoot $repoRoot
if (-not $result.Success) { # handle errors }
```

The refactoring recipe:

1. Extract the reusable logic into `scripts/<name>-helpers.ps1` that exposes one or more functions (never calling `exit` itself).
2. Keep the original CLI script as a thin wrapper that dot-sources the helper and translates function results to process exit codes.
3. Replace every `& pwsh -NoProfile -File <sibling>.ps1 ...` call in `scripts/*.ps1` with `. (Join-Path $PSScriptRoot '<sibling>-helpers.ps1')` + function call.
4. Keep tests invoking the CLI wrapper via subprocess (tests belong under `scripts/tests/` and are exempt from PWS003 by design — they need to exercise the production CLI surface).

**Allowlist**: when subprocess isolation is genuinely required (e.g., the callee writes structured JSON to stdout and must not be polluted by the parent host's ambient `Write-Host` output, or the callee uses `exit` extensively and cannot be refactored cheaply), opt out with a top-of-file marker:

```powershell
# lint-pwsh-invocations: allow-subprocess-pwsh <one-line rationale>
```

The rationale is required — the marker without an explanation is a maintenance hazard.

---

## `Start-Process -ArgumentList` joins an array without quoting

`Start-Process -FilePath pwsh -ArgumentList @('-NoProfile', '-File', $path)` concatenates the array
with spaces and **adds no quoting**. A `$path` containing a space is split into separate arguments.

The failure is not a clean error. Measured on this devcontainer with a fixture under
`.../dir with space/probe.ps1`:

```text
exit   = 64
stderr = The argument '/tmp/.../scratchpad/dir' is not recognized as the name of a script file.
stdout = Usage: pwsh[.exe] [-Login] [[-File] <filePath> [args]] ...
```

**A non-zero exit and a usage banner.** Any caller asserting "the child failed" is satisfied by that,
so a test harness reports green having launched nothing — the
[#556](https://github.com/Ambiguous-Interactive/unity-helpers/issues/556) shape, found by Bugbot on
PR #571 in a gate written for #556.

Use `ProcessStartInfo.ArgumentList`, which escapes each argument individually:

```powershell
$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = 'pwsh'
foreach ($argument in $argumentList) { [void]$startInfo.ArgumentList.Add($argument) }
$startInfo.WorkingDirectory = $workingDirectory
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.UseShellExecute = $false
$process = [System.Diagnostics.Process]::Start($startInfo)
# Drain BOTH streams from the moment it starts, or a full pipe deadlocks against WaitForExit.
$standardOutput = $process.StandardOutput.ReadToEndAsync()
$standardError = $process.StandardError.ReadToEndAsync()
$process.WaitForExit()
```

Two caveats before converting a call site:

- **`Start-Process -Wait` is not `Process.WaitForExit()`.** `-Wait` also waits on descendants, which
  matters for an installer that hands off to a child. `scripts/unity/bootstrap-windows-runner.ps1`
  is deliberately left on `Start-Process` for that reason, and because every caller passes fixed
  switches (`/q`, `/install`, `/quiet`, `/norestart`) with no space to truncate.
- **A test whose fixtures live under a spaceless path cannot see this.** Put the space in the
  fixture root so the regression cannot come back quietly:
  `Join-Path $tempBase "my-test $(Get-Random)"`. Reverting the launch mechanism under that root
  reddens six of nine scenarios in `test-validate-lint-error-codes.ps1`; the two that stay green are
  the ones asserting only a non-zero exit.

**Not currently linted.** `PWS006` is the obvious home and is filed as
[#572](https://github.com/Ambiguous-Interactive/unity-helpers/issues/572); until it exists this rule
is documentation. The repository has exactly **two** tracked `Start-Process -ArgumentList` sites --
`scripts/tests/test-validate-lint-error-codes.ps1`, which no longer uses it, and the installer
above -- so check with `git ls-files` rather than a working-tree grep: `scripts/run-unity*` is
gitignored, and an untracked local copy is not a call site this repository ships.

## Quick Reference

| Context                     | Correct form                                                                                                              |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| Single file, hook           | `pwsh -NoProfile -File scripts/lint-foo.ps1 -Paths "$file"`                                                               |
| Bash array, hook            | `pwsh -NoProfile -File scripts/lint-foo.ps1 -Paths "${ARR[@]}"`                                                           |
| Git hook launcher           | `.githooks/pre-commit` or `.githooks/pre-push`                                                                            |
| Git hook PowerShell debug   | `pwsh -NoProfile -File .githooks/pre-commit.ps1`                                                                          |
| Windows powershell fallback | `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/lint-foo.ps1 -Paths "${ARR[@]}"`                             |
| Test harness                | `& pwsh -NoProfile -File $lintScriptPath -Paths $fixturePath *>&1`                                                        |
| Positional flag-style       | `pwsh -NoProfile -File scripts/format-staged-csharp.ps1 "${ARR[@]}"` (if the script declares a positional string[] param) |

---

## Related Skills

- [git-hook-syntax-portability](./git-hook-syntax-portability.md) — hook regex, case patterns, CLI safety.
- [git-hook-lifecycle-debugging](./git-hook-lifecycle-debugging.md) — PowerShell exit codes from hooks.
- [linter-reference](./linter-reference.md) — where `lint-pwsh-invocations` sits in the lint matrix.

---

## History

- **2026-04-19**: Skill created after the `-- "${DEPENDABOT_FILES_ARRAY[@]}"` regression in `.githooks/pre-commit`. The bug reached production because `scripts/tests/test-lint-dependabot.ps1` used the in-process `&` operator (which tolerates `--`), while the hook used `pwsh -File` (which does not). Fix + prevention infrastructure: `scripts/lint-pwsh-invocations.ps1`, `.github/workflows/pwsh-invocations-lint.yml`, `scripts/tests/test-precommit-integration.sh`.
- **2026-04-23**: Added **PWS003** — flags `scripts/*.ps1` that shell out to sibling scripts via `pwsh -NoProfile -File`. Motivated by Copilot feedback on `scripts/install-hooks.ps1` and `scripts/agent-preflight.ps1`: both ran `& pwsh -NoProfile -File scripts/configure-git-defaults.ps1`, which fails on Windows PowerShell 5.1 hosts (no `pwsh` on PATH). Fix: extracted `Set-RepoGitPushDefaults` into `scripts/git-push-defaults-helpers.ps1` and switched both callers to dot-source. Allowlist marker added for the three scripts whose callees legitimately need subprocess isolation (structured stdout / heavy `exit` use). Regression coverage in `scripts/tests/test-lint-pwsh-invocations.ps1`.
- **2026-06-19**: Added **PWS004** — flags `pwsh -File .githooks/<hook>` because extensionless hook launchers are not portable PowerShell `-File` targets. Fix: invoke the hook directly through Git/shell, or invoke `.githooks/<hook>.ps1` for PowerShell debugging. Coverage includes nested `scripts/**/*.ps1`, Windows `pwsh.exe`/`powershell.exe`, `-f`, and JSON-escaped package scripts.
