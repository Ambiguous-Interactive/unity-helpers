# Contract fixture PowerShell launch audit

This reference tracks the remaining work for [#869](https://github.com/Ambiguous-Interactive/unity-helpers/issues/869).
The audit was inspected on 2026-10-02. It does not certify that the issue is complete.

## Scope and method

Parse each `scripts/tests/*.ps1` file with the PowerShell parser and inspect executable
`CommandAst` nodes whose command name is `pwsh` or `powershell`, including their `.exe`
forms. Also search for variable executable paths, `ProcessStartInfo`, and process runner
helpers. Text inside comments and here-strings is fixture input, not an executable launch.

After the template, git configuration, and npm package fixture migrations, the direct-command
scan found **37 launch sites in 22 PowerShell test files**. A site inside a helper can execute several times, so this is
not a process count. The scan also found one `ProcessStartInfo` launcher in the error-code
suite and one process-runner helper in the watchdog suite. The path-binding function in
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
| [isolated-fixture-runspace.ps1](../../scripts/tests/isolated-fixture-runspace.ps1)                     |     1 | CLI: native implicit-success observation, exercised by the three migrated suites; the strict runspace harness rejects the same script without an explicit terminal exit.                                                                                                                |
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
| [test-llm-instructions-lint.ps1](../../scripts/tests/test-llm-instructions-lint.ps1)                   |     3 | One CLI linter smoke; two generator output/determinism candidates. Generator status semantics must be inspected before changing those two launches.                                                                                                                                     |
| [test-npm-package-changelog.ps1](../../scripts/tests/test-npm-package-changelog.ps1)                   |     1 | CLI: retained real `-Check` integration smoke. The repeated package-validator helpers use fresh runspaces, preserve npm/git subprocesses, package canaries and cleanup, and require exact exit 1 plus the existing diagnostics.                                                         |
| [test-release-tools.ps1](../../scripts/tests/test-release-tools.ps1)                                   |     1 | CLI: empty explicit `-Version ''` binding from the workflow is the assertion.                                                                                                                                                                                                           |
| [test-report-slow-tests.ps1](../../scripts/tests/test-report-slow-tests.ps1)                           |     1 | CLI: real results-path and `-Top` binding plus native-status parity. Six content invocations migrated to fresh runspaces.                                                                                                                                                               |
| [test-sync-issue-template-versions.ps1](../../scripts/tests/test-sync-issue-template-versions.ps1)     |     1 | CLI: retained `-AddPackageVersion` smoke. Copied-tree update and idempotency use fresh runspaces at the fixture location and require exit 0.                                                                                                                                            |
| [test-unity-workflow-matrix-contract.ps1](../../scripts/tests/test-unity-workflow-matrix-contract.ps1) |    10 | Six CLI entrypoint/profile/environment/default checks retained. Four generated function-probe candidates: healthy bootstrap, WindowsApps alias, workflow-style function splatting, sparse registry. Preserve environment isolation; two success probes currently have no explicit exit. |
| [test-validate-devcontainer-config.ps1](../../scripts/tests/test-validate-devcontainer-config.ps1)     |     1 | CLI: explicit branch preserves real `-RepoRoot` binding. Content cases already use runspaces.                                                                                                                                                                                           |
| [test-validate-git-push-config.ps1](../../scripts/tests/test-validate-git-push-config.ps1)             |     1 | CLI: retained copied-repository smoke compares exit and complete output with the runspace result. Content checks use fresh runspaces at the requested location and require exact 0/1 statuses.                                                                                          |
| [test-validate-hook-sync-calls.ps1](../../scripts/tests/test-validate-hook-sync-calls.ps1)             |     1 | CLI: explicit branch preserves real `-RepoRoot` binding. Content cases already use runspaces.                                                                                                                                                                                           |
| [test-validate-mcp-config.ps1](../../scripts/tests/test-validate-mcp-config.ps1)                       |     1 | CLI: retained real `-RepoRoot` binding. Content cases already use runspaces.                                                                                                                                                                                                            |
| [test-verify-release-tag.ps1](../../scripts/tests/test-verify-release-tag.ps1)                         |     1 | Candidate: repeated package/version content checks. Preserve GitHub output-file environment and exact diagnostics; keep one real tag/source/path binding check.                                                                                                                         |

The additional `ProcessStartInfo` launcher in
[test-validate-lint-error-codes.ps1](../../scripts/tests/test-validate-lint-error-codes.ps1)
runs content fixtures concurrently. Classify the fixture executions as candidates, retaining
one real verbose CLI check. A migration must preserve concurrent isolation, drain output,
and keep the real repository control; replacing parallel children with serial runspaces
requires measurement rather than an assumed speed gain.

The helper in [test-process-watchdog.ps1](../../scripts/tests/test-process-watchdog.ps1)
requires real child processes: timeout, sentinel detection, grace termination, and descendant
cleanup are process-boundary assertions. Keep these launches.

## Other test languages

The direct-command inventory above covers PowerShell fixtures. Searches of JavaScript and
shell tests additionally identify the following launch families. These need separate migration
measurements and are not counted in the 37 PowerShell command sites.

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
- PowerShell mentions in `test-lint-meta-exclusions.sh`, `test-validate-devcontainer-urls.sh`,
  `test-lint-comment-block-form.js`, `test-unity-artifact-redaction.js`,
  `test-unity-acceptance-workflow.js`, and `test-shell-portability.sh` are source checks, fixture
  text, or tool discovery. They do not add repeated content launches to this inventory.

## Slow-test reporter migration evidence

All 12 original assertions remain. Six of the original seven reporter calls now use fresh
runspaces; the ranking call retains real `pwsh -File` binding. Ten controls prove exact
0/1/7 statuses, absent-status rejection, error-stream rejection with exits 0 and 1, throw
rejection, warning capture, global-state isolation, and native implicit-status parity with CLI.
Missing-file and malformed-XML tests require exit 1 and their specific diagnostic.

A sequential comparison on the same devcontainer host measured the original 12/12 suite at
**1.978 seconds** and the revised 22/22 suite at **1.846 seconds**. The baseline came from
`HEAD`; its temporary copy resolved the repository from the working directory to preserve
access to the unchanged reporter. These are local observations, not hosted timing claims.
A separate initial revised run took 7.466 seconds; that variation prevents a broad speed claim.

`HadErrors` alone is not an error-stream check: PowerShell sets it for an intentional nonzero
script exit without an error record. The harness rejects actual error records and thrown
invocations, then reads the exact returned status. `LASTEXITCODE` may come from native git;
it does **not** prove an explicit script `exit` statement. The native-status parity control
keeps that limit visible. Preserve `PSScriptRoot` by using `AddCommand(script path)` instead
of evaluating raw script content.

## Template, git configuration, and npm package migration evidence

The three suites share [isolated-fixture-runspace.ps1](../../scripts/tests/isolated-fixture-runspace.ps1).
Each suite runs thirteen controls for exact 0/1/7 status and host/warning output capture,
missing-status rejection, error-stream rejection for exits 0 and 1, throw rejection,
fresh global state, native-success-without-exit rejection, unreachable exits after script returns,
conditional missing exits, unscoped loop escapes, and a CLI observation of native implicit success. Each invocation owns and
disposes a fresh runspace, sets its location before loading the script by path, and rejects
actual PowerShell error records. Git configuration and npm package negative cases now
require exact exit 1 while preserving their existing diagnostic assertions. Environment
configuration, native npm/git execution, package-canary cleanup, and alternate working
directory coverage remain in the original fixtures.

The template suite preserves 24 original assertions and adds thirteen harness controls plus
one CLI parity assertion (38 total). The git configuration suite preserves 14 original
assertions and adds the same fourteen controls (28 total). The npm package suite preserves 18
original assertions and adds thirteen harness controls (31 total); its real `-Check` integration
smoke remains. No production gate policy changed.

Before invocation, the shared harness parses the unchanged file and requires an explicit
terminal exit, complete terminal if/else exit branches, or a terminal try body and catch
branches ending in exits. It refuses script-level returns and unscoped or labeled loop escapes that could bypass those exits;
returns inside functions or local script blocks remain valid. This is a bounded structural
contract for the three selected scripts, not a universal PowerShell control-flow proof.
`LASTEXITCODE` supplies the exact observed status only after this structural check succeeds.
The native-only control exits 0 under real CLI execution and fails the runspace harness,
so native success cannot substitute for the required explicit terminal exit.
The tag verifier and skills generator end successfully without an explicit exit, so they
still require an honest implicit-success contract before migration.

A sequential comparison on the same devcontainer host measured all original assertions
before and after, then the initial complete revised suites before the stricter exit-contract controls:

| Suite                  | Original assertions before | Original assertions after | Revised suite with controls |
| ---------------------- | -------------------------- | ------------------------- | --------------------------- |
| Template versions      | 24/24, 1.504 s             | 24/24, 1.576 s            | 34/34, 1.972 s              |
| Git push configuration | 14/14, 3.513 s             | 14/14, 1.173 s            | 24/24, 2.211 s              |
| Npm package changelog  | 18/18, 25.968 s            | 18/18, 19.849 s           | 27/27, 22.181 s             |

The original-case comparison used temporary copies with repository resolution fixed to the
original test directory. The revised comparison omitted only the newly added harness and
CLI parity assertions; the original assertions and content calls remained. The full revised
suite includes every CLI parity call. The subsequent strict-contract runs are recorded below. These local observations show a git/npm improvement,
not a speed gain for the template suite or a hosted-runner claim. Earlier local runs varied
(1.776/3.579/19.742 s before and 2.735/3.356/23.973 s after with controls); npm packing and
the shared working tree contribute to the limits of a single sample.

After the explicit-terminal-exit refinement, final full-suite verification passed
**38/38 template**, **28/28 git configuration**, and **31/31 npm changelog** assertions.
Sequential local timings were 6.314, 2.696, and 27.042 seconds respectively. These runs
include four more rejection cases than the initial suite: native status without exit,
return before an unreachable exit, missing conditional exit, and unscoped break before
exit. The larger sample variation reinforces that hosted performance acceptance remains open.

## Remaining acceptance work

Migrate the candidates above only after proving their isolation and status contracts. Preserve
real CLI checks, every existing diagnostic assertion, and production gate policy. Record original
and revised case counts and sequential timings for each changed suite on the same runner class.
Hosted timing evidence remains outstanding. The inventory does not make existing harnesses'
explicit-exit claims authoritative; those require separate controls or correction.
