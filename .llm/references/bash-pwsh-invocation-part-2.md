# bash-pwsh-invocation - Part 2

## Split Content

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
  reddens six of nine scenarios in `test-validate-lint-error-codes.ps1`; the three that stay green
  are the ones asserting only a non-zero exit or the absence of a crash marker.

**`PWS006` enforces this** (#572). It matches the command and its `-ArgumentList` as one thing in
the AST -- matching them independently over the file is the mistake #556's CSS gate made -- and
reports only the three shapes that can hold more than one argument: an array literal, an `@()`
expression, and a variable, whose contents the linter cannot know. A string literal is quoted by
its author and is left alone.

Opt out **per site**, not per file, when the arguments really are fixed switches:

```powershell
# lint-pwsh-invocations: allow-start-process-argument-list every caller passes fixed switches
# and -Wait waits on installer descendants in a way Process.WaitForExit does not.
$process = Start-Process -FilePath $installerPath -ArgumentList $Arguments -Wait -PassThru
```

The marker may be a trailing comment on the invocation or a line of the comment block directly
above it; a blank line ends that block, and a marker with no rationale is not an opt-out. The
installer above is the one live site that carries it.

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

- [git-hook-syntax-portability](../skills/git-hook-syntax-portability.md) — hook regex, case patterns, CLI safety.
- [git-hook-lifecycle-debugging](../skills/git-hook-lifecycle-debugging.md) — PowerShell exit codes from hooks.
- [linter-reference](../skills/linter-reference.md) — where `lint-pwsh-invocations` sits in the lint matrix.

---

## History

- **2026-04-19**: Skill created after the `-- "${DEPENDABOT_FILES_ARRAY[@]}"` regression in `.githooks/pre-commit`. The bug reached production because `scripts/tests/test-lint-dependabot.ps1` used the in-process `&` operator (which tolerates `--`), while the hook used `pwsh -File` (which does not). Fix + prevention infrastructure: `scripts/lint-pwsh-invocations.ps1`, `.github/workflows/pwsh-invocations-lint.yml`, `scripts/tests/test-precommit-integration.sh`.
- **2026-04-23**: Added **PWS003** — flags `scripts/*.ps1` that shell out to sibling scripts via `pwsh -NoProfile -File`. Motivated by Copilot feedback on `scripts/install-hooks.ps1` and `scripts/agent-preflight.ps1`: both ran `& pwsh -NoProfile -File scripts/configure-git-defaults.ps1`, which fails on Windows PowerShell 5.1 hosts (no `pwsh` on PATH). Fix: extracted `Set-RepoGitPushDefaults` into `scripts/git-push-defaults-helpers.ps1` and switched both callers to dot-source. Allowlist marker added for the three scripts whose callees legitimately need subprocess isolation (structured stdout / heavy `exit` use). Regression coverage in `scripts/tests/test-lint-pwsh-invocations.ps1`.
- **2026-06-19**: Added **PWS004** — flags `pwsh -File .githooks/<hook>` because extensionless hook launchers are not portable PowerShell `-File` targets. Fix: invoke the hook directly through Git/shell, or invoke `.githooks/<hook>.ps1` for PowerShell debugging. Coverage includes nested `scripts/**/*.ps1`, Windows `pwsh.exe`/`powershell.exe`, `-f`, and JSON-escaped package scripts.
