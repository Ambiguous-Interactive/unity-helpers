# Contract fixture PowerShell launch audit

This reference tracks the remaining work for [#869](https://github.com/Ambiguous-Interactive/unity-helpers/issues/869).
The audit was inspected on 2026-10-03. It does not certify that the issue is complete.

## Scope and method

Parse `scripts/tests/*.ps1` and inspect executable `CommandAst` nodes named `pwsh` or `powershell`, including `.exe` forms.
Also search variable executable paths, `ProcessStartInfo`, and process runner helpers. Comments and here-strings are fixture input, not launches.

After the skills-generator fixture migration, the direct-command
scan found **35 launch sites in 22 PowerShell test files**. A site inside a helper can execute several times, so this is
not a process count. The scan also found `ProcessStartInfo` launchers in the error-code
and skills-generator suites, plus one process-runner helper in the watchdog suite. The path-binding function in
`test-sync-script-contracts.ps1` is a source inspection, not a child launch.

Classify a retained launch by its actual assertion. `CLI` means real `-File` argument
binding, host exit behavior, or process behavior is part of the assertion. `Candidate`
means the content or function result can potentially use a fresh runspace; its status,
error, environment, and working-directory behavior still need controls before migration.
Single integration smoke checks are retained even when they are not repeated text fixtures.

## PowerShell classifications

All paths in this table are under `scripts/tests/`. Counts are executable source sites.

| File                                                                                                   | Sites | Classification and reason                                                                                                                                                                                                                                                               |
| ------------------------------------------------------------------------------------------------------ | ----: | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [isolated-fixture-runspace.ps1](../../scripts/tests/isolated-fixture-runspace.ps1)                     |     1 | CLI: native implicit-success observation, exercised by the six migrated suites; the strict runspace harness rejects the same script without an explicit terminal exit.                                                                                                                  |
| [test-agent-preflight.ps1](../../scripts/tests/test-agent-preflight.ps1)                               |     4 | CLI: helper's explicit CLI branch; loaded-function isolation probe; repository and non-repository push configuration entrypoints. Content cases already use runspaces.                                                                                                                  |
| [test-check-eol.ps1](../../scripts/tests/test-check-eol.ps1)                                           |     1 | CLI: explicit CLI branch preserves verbose and multiple-path binding. Content cases already use runspaces.                                                                                                                                                                              |
| [test-empty-corpus-gates.ps1](../../scripts/tests/test-empty-corpus-gates.ps1)                         |     1 | CLI: retained smoke and native implicit-status parity controls. Content cases already use runspaces.                                                                                                                                                                                    |
| [test-gitignore-docs.ps1](../../scripts/tests/test-gitignore-docs.ps1)                                 |     1 | CLI: explicit CLI branch preserves verbose binding and host behavior. Content cases already use runspaces.                                                                                                                                                                              |
| [test-lint-csharp-naming.ps1](../../scripts/tests/test-lint-csharp-naming.ps1)                         |     1 | CLI: parameterless smoke. Content cases already use runspaces.                                                                                                                                                                                                                          |
| [test-lint-dependabot.ps1](../../scripts/tests/test-lint-dependabot.ps1)                               |     2 | CLI: single and multiple `-Paths` binding, including the malformed invocation. Content cases already use runspaces.                                                                                                                                                                     |
| [test-lint-doc-counts.ps1](../../scripts/tests/test-lint-doc-counts.ps1)                               |     1 | CLI: wrapper delegates to a native child; exact 0/1/42 status propagation, missing-script termination, and unexpected CLI argument rejection. Keep its five assertions.                                                                                                                 |
| [test-lint-doc-links.ps1](../../scripts/tests/test-lint-doc-links.ps1)                                 |     1 | CLI: subdirectory invocation, multiple `-Paths`, and `-Mode` binding. Content cases already use runspaces.                                                                                                                                                                              |
| [test-lint-skill-sizes.ps1](../../scripts/tests/test-lint-skill-sizes.ps1)                             |     1 | CLI: additional-argument branch tests CLI rejection and verbose binding. Content cases already use runspaces.                                                                                                                                                                           |
| [test-lint-unity-test-modules.ps1](../../scripts/tests/test-lint-unity-test-modules.ps1)               |     1 | CLI: real manifest `-Path` binding smoke. Content cases already use runspaces.                                                                                                                                                                                                          |
| [test-llm-instructions-lint.ps1](../../scripts/tests/test-llm-instructions-lint.ps1)                   |     1 | CLI: real linter smoke. Two generator calls now use fresh runspaces; the separate ProcessStartInfo helper preserves real CLI file/stdout bytes and missing-skills failure.                                                                                                              |
| [test-npm-package-changelog.ps1](../../scripts/tests/test-npm-package-changelog.ps1)                   |     1 | CLI: retained real `-Check` integration smoke. The repeated package-validator helpers use fresh runspaces, preserve npm/git subprocesses, package canaries and cleanup, and require exact exit 1 plus the existing diagnostics.                                                         |
| [test-release-tools.ps1](../../scripts/tests/test-release-tools.ps1)                                   |     1 | CLI: empty explicit `-Version ''` binding from the workflow is the assertion.                                                                                                                                                                                                           |
| [test-report-slow-tests.ps1](../../scripts/tests/test-report-slow-tests.ps1)                           |     1 | CLI: real results-path and `-Top` binding plus native-status parity. Six content invocations migrated to fresh runspaces.                                                                                                                                                               |
| [test-sync-issue-template-versions.ps1](../../scripts/tests/test-sync-issue-template-versions.ps1)     |     1 | CLI: retained `-AddPackageVersion` smoke. Copied-tree update and idempotency use fresh runspaces at the fixture location and require exit 0.                                                                                                                                            |
| [test-unity-workflow-matrix-contract.ps1](../../scripts/tests/test-unity-workflow-matrix-contract.ps1) |    10 | Six CLI entrypoint/profile/environment/default checks retained. Four generated function-probe candidates: healthy bootstrap, WindowsApps alias, workflow-style function splatting, sparse registry. Preserve environment isolation; two success probes currently have no explicit exit. |
| [test-validate-devcontainer-config.ps1](../../scripts/tests/test-validate-devcontainer-config.ps1)     |     1 | CLI: explicit branch preserves real `-RepoRoot` binding. Content cases already use runspaces.                                                                                                                                                                                           |
| [test-validate-git-push-config.ps1](../../scripts/tests/test-validate-git-push-config.ps1)             |     1 | CLI: retained copied-repository smoke compares exit and complete output with the runspace result. Content checks use fresh runspaces at the requested location and require exact 0/1 statuses.                                                                                          |
| [test-validate-hook-sync-calls.ps1](../../scripts/tests/test-validate-hook-sync-calls.ps1)             |     1 | CLI: explicit branch preserves real `-RepoRoot` binding. Content cases already use runspaces.                                                                                                                                                                                           |
| [test-validate-mcp-config.ps1](../../scripts/tests/test-validate-mcp-config.ps1)                       |     1 | CLI: retained real `-RepoRoot` binding. Content cases already use runspaces.                                                                                                                                                                                                            |
| [test-verify-release-tag.ps1](../../scripts/tests/test-verify-release-tag.ps1)                         |     1 | CLI: retained real tag/source/path binding, empty tag/source ref, and unexpected argument controls. Repeated package/version content checks use fresh runspaces with exact exit codes and preserve GitHub output-file environment and diagnostics.                                      |

The additional `ProcessStartInfo` launcher in
[test-validate-lint-error-codes.ps1](../../scripts/tests/test-validate-lint-error-codes.ps1)
retains three CLI executions: the real repository control and spaced-path success/failure
parity with verbose binding. Eight synthetic content fixtures now use overlapping fresh
runspaces. Node/cspell still run natively; every original scenario and diagnostic assertion remains.

The helper in [test-process-watchdog.ps1](../../scripts/tests/test-process-watchdog.ps1)
requires real child processes: timeout, sentinel detection, grace termination, and descendant
cleanup are process-boundary assertions. Keep these launches.

## Other test languages

The direct-command inventory above covers PowerShell fixtures. Searches of JavaScript and
shell tests additionally identify the following launch families. These need separate migration
measurements and are not counted in the 35 PowerShell command sites.

- [test-unity-grouped-modes.js](../../scripts/tests/test-unity-grouped-modes.js): generated
  PowerShell function harnesses are candidates for a batched PowerShell host with isolated
  runspaces. Preserve the process-inspection failures and environment cases.
- [test-redact-unity-artifacts.js](../../scripts/tests/test-redact-unity-artifacts.js): generated
  text-redaction function harnesses are candidates. The process-watchdog and CLI log-redaction
  paths require real processes and log files.
- [test-run-repo-lint.js](../../scripts/tests/test-run-repo-lint.js): generated shell/PowerShell
  CLI execution tests require the runner's real spawn, exit, stream, and cancellation boundaries.
- [test-postinstall-hooks.js](../../scripts/tests/test-postinstall-hooks.js): fake PowerShell
  executables test discovery and installation behavior; they are not repeated PowerShell content
  launches.
- [test-precommit-integration.sh](../../scripts/tests/test-precommit-integration.sh): the three
  `Get-Help` parse probes at lines 151, 332, and 339 are content candidates for a batched
  PowerShell host with isolated runspaces. Its hook execution and `-File` binding checks
  require real entrypoints.
- [test-pre-push-changed-files.sh](../../scripts/tests/test-pre-push-changed-files.sh): the
  `Parser.ParseFile` probe at line 665 is a content candidate for that batched host. Its
  hook execution and changed-file integration checks require real entrypoints.
- [test-git-staging-helpers.sh](../../scripts/tests/test-git-staging-helpers.sh): staging
  integration boundaries require real shell/PowerShell entrypoints.
- PowerShell mentions in source checks, fixture text or tool discovery do not add executable launch sites.

## Slow-test reporter migration evidence

All 12 original assertions remain. Six of the original seven reporter calls now use fresh
runspaces; the ranking call retains real `pwsh -File` binding. Ten controls prove exact
0/1/7 statuses, absent-status rejection, error-stream rejection with exits 0 and 1, throw
rejection, warning capture, global-state isolation, and native implicit-status parity with CLI.
Missing-file and malformed-XML tests require exit 1 and their specific diagnostic.

Sequential devcontainer observations passed **12/12 at 1.978 seconds** before and **22/22 at 1.846 seconds** after.
The baseline copy resolved the repository from the working directory. An earlier revised sample took 7.466 seconds;
variation leaves broad speed and hosted timing acceptance open.

`HadErrors` alone is not an error-stream check: PowerShell sets it for an intentional nonzero
script exit without an error record. The harness rejects actual error records and thrown
invocations, then reads the exact returned status. `LASTEXITCODE` may come from native git;
it does **not** prove an explicit script `exit` statement. The native-status parity control
keeps that limit visible. Preserve `PSScriptRoot` by using `AddCommand(script path)` instead
of evaluating raw script content.

## Template, git configuration, and npm package migration evidence

The three suites share [isolated-fixture-runspace.ps1](../../scripts/tests/isolated-fixture-runspace.ps1).
Each suite runs twenty-three controls for exact 0/1/7 status and host/warning output capture,
missing-status rejection, error-stream rejection for exits 0 and 1, throw rejection,
fresh global state, native-success-without-exit rejection, unreachable exits after script returns,
conditional missing exits, unscoped loop escapes, and a CLI observation of native implicit success. Each invocation owns and
disposes a fresh runspace, sets its location before loading the script by path, and rejects
actual PowerShell error records. Git configuration and npm package negative cases now
require exact exit 1 while preserving their existing diagnostic assertions. Environment
configuration, native npm/git execution, package-canary cleanup, and alternate working
directory coverage remain in the original fixtures.

The template suite preserves 24 original assertions and adds twenty-three harness controls plus
one CLI parity assertion (48 total). The git configuration suite preserves 14 original
assertions and adds the same twenty-four controls (38 total). The npm package suite preserves 18
original assertions and adds twenty-three harness controls (41 total); its real `-Check` integration
smoke remains. No production gate policy changed.

Before invocation, the shared harness parses the unchanged file and requires an explicit
terminal exit, complete terminal if/else exit branches, or a terminal try body and catch
branches ending in exits. It refuses script-level returns and unscoped or labeled loop escapes that could bypass those exits;
returns inside functions or local script blocks remain valid. Break/continue inside those
boundaries require a containing loop or switch within that same function or script block;
parent loops outside the boundary cannot justify the escape. Labeled escapes are rejected.
Six negative controls cover unscoped function/block break and continue plus labeled escapes;
four positive controls preserve function returns and scoped function/block loops and switches. This is a bounded structural
contract for the selected scripts, not a universal PowerShell control-flow proof.
`LASTEXITCODE` supplies the exact observed status only after this structural check succeeds.
The native-only control exits 0 under real CLI execution and fails the runspace harness,
so native success cannot substitute for the required explicit terminal exit.
The skills generator and tag verifier now end with explicit exit 0. Real CLI controls preserve
file output, Console.Out bytes and failure status; missing status is never inferred as success.

Sequential same-host devcontainer measurements preserved every original assertion:

| Suite                  | Original before | Original after  | Final suite with controls |
| ---------------------- | --------------- | --------------- | ------------------------- |
| Template versions      | 24/24, 1.504 s  | 24/24, 1.576 s  | 48/48, 1.641 s            |
| Git push configuration | 14/14, 3.513 s  | 14/14, 1.173 s  | 38/38, 1.720 s            |
| Npm package changelog  | 18/18, 25.968 s | 18/18, 19.849 s | 41/41, 19.294 s           |

Temporary revised timing copies omitted only new controls and adjusted test-directory resolution.
Final suites include all 23 shared controls; independent review reproduced rejection of all four
unscoped function/block escapes. Earlier partial-control totals are superseded. Local sample
variation, npm packing and the shared tree leave hosted performance acceptance open.

## Release tag verifier migration evidence

The verifier preserves 24 original assertions and adds 23 shared strict-runspace controls plus four CLI controls (51 total).
CLI controls prove matching tag/source/path binding and GitHub outputs, empty tag/source ref diagnostics, and unexpected-argument exit 64.
Original negative cases retain exact exit 1 and their diagnostics. Explicit terminal `exit 0` preserves CLI success while satisfying the shared contract;
missing status is never inferred as success. The fixture restores `GITHUB_OUTPUT` in `finally` and removes temporary output files.
Sequential same-host devcontainer runs passed **24/24 in 3.888 seconds** before and **51/51 in 1.918 seconds** after, including every added control.
These are local single-sample observations, not hosted acceptance. The retained CLI helper remains one source launch site; fewer executions do not change that source count.

## Error-code validator migration evidence

All nine original scenarios remain with exact 0/1 status and specific prefix/source-line/JSON-patch diagnostics.
Overlapping controls preserve globals, locations, script paths and cleanup. Hosted cancellation exit 134 was
reproduced and fixed by synchronous setup before one async fixture command. The final suite has 46 controls,
including 20 active and 32 immediate cancellations and setup rejection; 100 active cancellations also passed.
Same-host original-only observations passed **9/9 in 9.164 seconds** before and **9/9 in 8.495 seconds** after.
These local samples do not prove hosted or total-suite speed gains.

## Skills-generator migration evidence

All 60 original assertions remain; the revised suite passes 95/95 with 23 shared and 12 generator controls.
Both file generators start in fresh runspaces before completion. A separate barrier observes both controls
running before release and checks independent globals, locations and script paths; cleanup closes owned runspaces.

Explicit success exit 0 preserves the CI status/drift gates. Actual CLI stdout bytes and spaced-path file output
match the runspace-generated file. Missing-skills controls require exit 1, the diagnostic and no output file.
The real linter, Node/cspell and offline feedback boundaries remain.

Sequential devcontainer observations passed **60/60 in 33.445 seconds** before and **60/60 in 30.967 seconds** after,
with only added controls omitted from the revised timing copy and exact original case identities retained.
The full revision passed **95/95 in 36.150 seconds**; PowerShell 7.6.4 on Windows passed **95/95**,
with empty stderr and all owned processes absent. These local single samples do not prove hosted speed gains.
Three generator CLI calls plus the shared implicit-status CLI control remain in the expanded suite;
removing two original launches does not prove a lower total process count.

## Remaining acceptance work

Migrate the candidates above only after proving their isolation and status contracts. Preserve
real CLI checks, every existing diagnostic assertion, and production gate policy. Record original
and revised case counts and sequential timings for each changed suite on the same runner class.
Hosted timing evidence remains outstanding. The inventory does not make existing harnesses'
explicit-exit claims authoritative; those require separate controls or correction.
