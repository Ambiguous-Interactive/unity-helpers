<!-- cspell:ignore winget pwsh prereqs redist UCRT WSL Redistributables MSVCP MSVCR VCRUNTIME NDK -->

# Unity Runners After Repository Transfer Runbook

This runbook explains how to restore self-hosted Unity runner access after a repository is transferred between GitHub organizations (or when a freshly provisioned runner does not pick up queued Unity jobs). Keep execution notes local. Do not paste secrets, screenshots of organization settings, or other private account metadata into this file or any tracked follow-up.

It is referenced by the `runner-preflight` job in `.github/workflows/unity-tests.yml`, `.github/workflows/unity-benchmarks.yml`, and `.github/workflows/runner-bootstrap.yml`, and by `scripts/unity/ensure-editor.ps1` and `.github/actions/print-self-hosted-runner-diagnostics`.

## Symptom

- A queued Unity workflow run (for example **Unity Tests** or **Unity Benchmarks**) stays queued indefinitely.
- The GitHub Actions UI shows the job waiting for a runner. There is no error, no warning, and the run never starts.
- The organization's self-hosted runners report Online and Idle in the GitHub UI, with labels that exactly match the workflow's `runs-on` request (`self-hosted`, `Windows`, `RAM-64GB`).
- The watchdog defined in `.github/workflows/stuck-job-watchdog.yml` does not recover the run because no idle runner is visible to the repository, so the watchdog's label-matching rule never fires.

## Root cause

After a repository transfer between GitHub organizations, the destination organization's runner groups do not automatically include the transferred repository in their repository-access list. When a runner group is configured as "Selected repositories", any repository that is not explicitly listed cannot dispatch jobs to that group's runners. The dispatcher does not log an error in this state; the job simply stays queued.

This is a configuration-state issue, not the intermittent dispatcher bug tracked upstream as [GitHub Community Discussion #186811](https://github.com/orgs/community/discussions/186811). The dispatcher bug applies when an idle matching runner _is_ visible to the repository through the GitHub API but never receives the job. If the API does not list the runner at all for the repository, this runbook applies instead.

## Diagnose with the GitHub CLI

Run the following from any workstation with `gh auth login` already completed. Replace `<org>` with the destination organization that owns the runners.

List the organization's runner groups, including each group's visibility setting:

```bash
gh api orgs/<org>/actions/runner-groups \
  -q '.runner_groups[] | {id, name, visibility, allows_public_repositories}'
```

For a runner group whose visibility is `selected`, list the repositories that currently have access:

```bash
gh api orgs/<org>/actions/runner-groups/<group-id>/repositories \
  -q '.repositories[] | {id, name, full_name}'
```

If `Ambiguous-Interactive/unity-helpers` does not appear in that list, the dispatcher has no path to the group's runners from this repository, which matches the symptom above.

Cross-check by listing runners that the repository itself can see:

```bash
gh api repos/Ambiguous-Interactive/unity-helpers/actions/runners \
  -q '.runners[] | {id, name, status, busy, labels: [.labels[].name]}'
```

When this list is empty or omits the expected runner names while the organization-level inventory shows them online, the access list is the cause.

## Resolution

Choose one of the following inside the destination organization. Either restores dispatch; pick the one that matches the organization's security model.

Add the transferred repository to the selected list:

1. Organization Settings.
2. Actions.
3. Runner groups.
4. Default (or the relevant group).
5. Repository access.
6. Add `wallstop/unity-helpers` to the list.
7. Save.

Change the group's visibility to all repositories:

1. Organization Settings.
2. Actions.
3. Runner groups.
4. Default (or the relevant group).
5. Repository access.
6. Set visibility to all repositories.
7. Save.

The second resolution avoids future per-transfer maintenance but exposes the runners to every repository in the organization. Use it only when that exposure is acceptable for the runner group's security posture.

After applying the chosen resolution, re-run the queued workflow from the Actions tab. The `runner-preflight` job in each licensed Unity test, benchmark, or bootstrap workflow validates runner access from `ubuntu-latest` before any matrix entry attempts to dispatch onto self-hosted; a green preflight confirms the fix.

## Preflight diagnostic in this repository

Unity workflows run a `runner-preflight` job on `ubuntu-latest` before the self-hosted matrix. The preflight uses the organization reader GitHub App and requests only organization self-hosted-runner read permission. It asks GitHub for runner groups visible to `Ambiguous-Interactive/unity-helpers`, then considers only runners inside those groups when matching the exact `runs-on` labels.

The preflight fails closed. Missing reader credentials, an App authentication or API failure, no runner group visible to this repository, malformed or truncated inventory, or no accessible online runner with every required label all make the check red. It never emits a green soft pass. Busy online runners remain eligible because GitHub can queue the licensed job until one becomes idle.

The `Unity CI Success` aggregate job runs with `always()` and is the required branch-protection check. It rejects a failed or cancelled preflight and rejects an unexpected skipped licensed job, so a runner outage cannot produce a green Unity check merely because dependent jobs were skipped.

The reader App must be installed for the organization and expose the organization secrets `BUILD_LOCK_READER_APP_ID` and `BUILD_LOCK_READER_APP_PRIVATE_KEY` to this repository. Its organization permission is Self-hosted runners: read. No PAT or repository-level environment is required.

The watchdog uses the `client-id` input of `actions/create-github-app-token` pinned at `bcd2ba49218906704ab6c1aa796996da409d3eb1`, while retaining `BUILD_LOCK_READER_APP_ID` and its existing private key. That revision forwards the identity unchanged into the JWT issuer; [GitHub accepts either the application ID or client ID](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-json-web-token-jwt-for-a-github-app). The retained secret is an application ID, not a newly provisioned client ID. The release workflow uses the same supported input with `AUTO_COMMIT_APP_ID`. No secret replacement or additional permission is required for this input migration. Missing or expired reader credentials still follow the inventory fallback and unreadable-inventory failure path.

If the preflight passes but the matrix job still stays queued, the cause is more likely the dispatcher bug (see [GitHub Community Discussion #186811](https://github.com/orgs/community/discussions/186811)) than the access list. Use the recovery workflows in this repository: `.github/workflows/unstick-run.yml` for manual recovery of a single run, and `.github/workflows/stuck-job-watchdog.yml` for the automated 5-minute scan.

### The watchdog's second recovery mode

The same watchdog also recovers a different symptom: a completed run reported `cancelled` whose jobs all succeeded step by step. GitHub occasionally marks a job `cancelled` even though every one of its steps, `Complete job` included, reported success; dependent jobs then skip on the unmet `needs:` and `Unity CI Success` reports red with no diff to explain it. A plain re-run of the same run id goes green.

The watchdog detects that signature (a job with `conclusion: cancelled` whose step list is non-empty and whose every step succeeded) and re-runs the run through `POST /repos/{owner}/{repo}/actions/runs/{id}/rerun`. A deliberate cancel never matches, because its in-flight step is itself cancelled rather than successful.

Two bounds keep it from looping. The run's `run_attempt` must be at most `MAX_RERUN_ATTEMPT`, so a re-run that lands in the same state escalates to a human, and re-runs are capped at `MAX_RERUNS_PER_DAY` per run id in a state file on the `watchdog-state` branch. Both appear in the workflow's `env:` block.

This re-run uses the workflow's own `GITHUB_TOKEN`, whose job-level `actions: write` grant already covers it. It does **not** use the build-lock reader App, which is read-only by design; do not widen that App to enable this.

## Machine-name labels for runner bootstrap

The manual `.github/workflows/runner-bootstrap.yml` workflow performs host maintenance on one specific Windows runner. Each runner must therefore have a custom label that exactly matches its runner name:

- `DAD-MACHINE`
- `ELI-MACHINE`

The bootstrap job requests `self-hosted`, `Windows`, `RAM-64GB`, and the selected machine-name label. GitHub schedules a self-hosted job only on runners that have every requested label, so this prevents an ELI bootstrap from silently running on DAD, or the reverse. The workflow still keeps a hard runner-identity check as a final guard against label drift.

If a bootstrap dispatch stays queued after selecting a runner, verify the runner is online and that the matching machine-name label is present in Settings -> Actions -> Runners. Do not work around the queue by removing the machine-name label from the workflow; that reintroduces wrong-runner maintenance.

## Install a new Windows runner agent

The workflow bootstrap can maintain only an agent that is already online. For a new machine, `scripts/unity/install-windows-actions-runner.ps1` separates the work that requires elevation from the package download that should run as the ordinary runner owner. Obtain the current Windows x64 runner version, its published SHA-256, and a short-lived registration token from **Settings -> Actions -> Runners -> New self-hosted runner**. Never commit or transcribe the token into logs.

First, open Windows PowerShell 5.1 as administrator and prepare the service directory. Replace the account and machine name examples with the actual runner owner and one of the repository's expected runner names:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\unity\install-windows-actions-runner.ps1 `
  -Phase AdminPrepare -InstallRoot C:\actions-runner -RunnerUser runner-owner
```

Then sign in as `runner-owner` and install the checksum-verified runner package without elevation:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\unity\install-windows-actions-runner.ps1 `
  -Phase UserInstall -InstallRoot C:\actions-runner `
  -RunnerVersion <version-from-github> -ArchiveSha256 <sha256-from-github>
```

Finally, return to an administrator prompt and register the agent as a Windows service. Pass the token only at invocation time; the script deliberately does not print it. The runner-name label is added automatically alongside `RAM-64GB`, which makes the machine eligible for both Unity jobs and targeted maintenance:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\unity\install-windows-actions-runner.ps1 `
  -Phase AdminConfigure -InstallRoot C:\actions-runner `
  -RegistrationUrl https://github.com/Ambiguous-Interactive `
  -RegistrationToken <short-lived-token> -RunnerName DAD-MACHINE
```

Once the agent reports online, dispatch **Runner Bootstrap (Windows)** in maintenance mode. That installs the host prerequisites and every Unity editor from `.github/unity-versions.json`, including all supported build-target components. Host preparation runs once, then each editor runs in its own serialized six-hour job. A slow editor therefore cannot consume the timeout for later versions, and GitHub can rerun only the failed version leg.

## Run maintenance directly on a Windows runner

When you are already on the runner host, you do not need to run YAML. From a checkout of this repository, run the same maintenance backend directly:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\unity\maintain-windows-runner.ps1
```

The script reads `.github\unity-versions.json` when `-UnityVersions` is omitted and writes diagnostics under `.artifacts\runner-bootstrap`. Its Unity install-root precedence is explicit `-InstallRoot`, `UNITY_EDITOR_INSTALL_ROOT`, the Actions runner tool-cache environment, the runner service's `.env` tool-cache setting, the Actions default `<runner>\_work\_tool\u6-v3`, then `Unity\Editors` on the checkout's drive. A runner whose `.env` sets `RUNNER_TOOL_CACHE=<runner>\_tool` therefore uses `<runner>\_tool\u6-v3`, exactly like its workflow jobs. This keeps large editor payloads off `C:` when the runner lives on another drive and makes interactive maintenance reuse the same editors as workflow jobs. Its default `Full` profile installs and verifies Windows IL2CPP, Android (including SDK/NDK/OpenJDK), WebGL, iOS, Linux Mono/IL2CPP, and macOS Mono build support; the workflow uses the same profile. Maintenance installs the standalone Unity CLI when absent and otherwise runs its current `self-update` command before provisioning. All CLI operations are non-interactive, use built-in download retries, stream progress, and have bounded wall-clock and no-output timeouts. `UH_UNITY_CLI_UPDATE_TIMEOUT_SECONDS`, `UH_UNITY_CLI_INSTALL_STALL_SECONDS`, `UH_UNITY_CLI_UPDATE_STALL_SECONDS`, and `UH_UNITY_CLI_UPDATE_RETRY_ATTEMPTS` override the install/update defaults for diagnosis.

Unity CLI `1.0.0-beta.9` has a [confirmed upstream Windows compatibility defect](https://discussions.unity.com/t/unity-cli-1-0-0-beta-9-is-rolling-out/1736105/7) where an older installation database can fail with `table installs has no column named writer_kind`. Maintenance recognizes only that exact signature, rolls back to `1.0.0-beta.8`, retries the interrupted command, and writes a 12-hour compatibility marker beside the CLI executable so subsequent editor legs do not immediately reinstall the broken build. The hold expires automatically, allowing a fixed newer beta to be adopted without operator cleanup. If rollback fails, maintenance refuses to uninstall, quarantine, or clear payloads because the failure is CLI metadata—not editor corruption.

For an audit that never installs or repairs anything:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\unity\maintain-windows-runner.ps1 -DetectOnly
```

`UH_RUNNER_DISABLE_AUTO_BOOTSTRAP=1` also forces detect-only mode for both `maintain-windows-runner.ps1` and `bootstrap-windows-runner.ps1`, matching the workflow override.

## PowerShell 7 prerequisite on self-hosted runners

Self-hosted Windows Unity runners require **PowerShell 7 (`pwsh`)** in addition to Git Bash. Every Unity workflow consumes the `print-self-hosted-runner-diagnostics` composite action (`.github/actions/print-self-hosted-runner-diagnostics/action.yml`) before its own steps, and that action plus the Unity run/provision steps run with `shell: pwsh`. PowerShell 7 is _not_ the Windows-built-in PowerShell 5.1 (`powershell`); it is a separate install that provides the `pwsh` executable.

### Symptom

- A self-hosted Unity job fails almost immediately with `##[error]pwsh: command not found`.
- The failure originates from the first `shell: pwsh` step the agent reaches.
- Git Bash and the runner agent are otherwise healthy.

The diagnostics composite action fails fast with a clear, actionable error annotation (`pwsh missing on self-hosted runner`) when `pwsh` is absent, so this state no longer surfaces only as the cryptic `pwsh: command not found`. The preflight step that emits that error runs under Windows PowerShell 5.1, which is always present, so it executes even when PowerShell 7 is missing.

### Install PowerShell 7

On a machine with winget:

```powershell
winget install --id Microsoft.PowerShell --source winget
```

For machines without winget, download and run the latest MSI installer from the official releases page: <https://github.com/PowerShell/PowerShell/releases>.

### Verify

Open a **new** shell (so the updated PATH is picked up) and confirm:

```powershell
pwsh -v
Get-Command pwsh
```

`pwsh -v` should print the installed PowerShell 7 version, and `Get-Command pwsh` should resolve to the installed executable's path.

### Restart the runner agent

After installing PowerShell 7, restart the self-hosted runner service/agent (or refresh the machine's PATH and restart the runner) so the agent process sees `pwsh` on its PATH. The runner agent inherits its environment at start time; until it is restarted it keeps reporting `pwsh: command not found` even though a fresh interactive shell can find `pwsh`. Re-run the queued Unity workflow once the agent is back online.

## The persistent Unity project root

Each Unity leg generates a throwaway Unity project whose `Library` holds the import
database and the compiled assemblies. That project lives **outside the checkout**, at:

```text
<RUNNER_WORKSPACE>\unity-workspace\projects\<unity-version>-<mode>[-<scope>]
<RUNNER_WORKSPACE>\unity-workspace\cache\<unity-version>
```

The Unity test workflow runs EditMode and PlayMode serially with
`-ShareEditorProject`, so both use the existing
`<unity-version>-editmode[-<scope>]` project. The `single-threaded` scope keeps
its different scripting defines separate from the default editor project.
Standalone uses `<unity-version>-standalone[-<scope>]`
because its player build settings differ. Each mode still writes its own logs and
`results.xml` under `.artifacts/unity/`. If EditMode fails, PlayMode uses its
separate `<unity-version>-playmode[-<scope>]` project so the second result remains
independent of assets left by the failed run.

EditMode runs compiler-isolation fixtures in a fresh editor process before the broader suite.
These cases carry `CompilationIsolation` and must all pass without skips. Their results and log
remain under the mode's `compilation/` artifact directory. The source API, backup recovery,
import-verification, source/data apply, and recovery-record/scene fixtures each run in a separate fresh editor process
with `MigrationIsolation`; their artifacts remain under `migration/<fixture-name>/`.
The broader process excludes both categories, so each case runs once. All processes share the
installed editor, project and acquired license context. A failed isolated phase prevents later
phases and fails the mode. Verification requires passing cases from every named isolated fixture.
Earlier ruleset-import tests can leave pending compilation and `EditorApplication.isUpdating`
that cannot drain during the same batch run; each migration fixture needs an idle editor to test
its real production guard without weakening it. Recovery-record scene controls also need a clean
initial scene; ordinary broad-suite editor tests can leave an untitled scene dirty. The isolated
process preserves this precondition without discarding authoring changes or skipping those controls.
Verification requires the mode's root `results.xml` independently of its isolated fixture XML.
A passing nested result cannot replace a missing broad-suite result. Failure dumps include
root and nested editor logs.

It has to. `actions/checkout` runs `git clean -ffdx` at the top of every job and `-x`
means gitignored, so anything under the repository's `.artifacts/` tree is deleted
before the job starts, including the `Library` the previous leg had just built on
that same disk. Keeping the project one directory up puts it out of `git clean`'s
reach, so a leg reuses its own local copy instead of downloading one.

A nonzero editor exit and an accepted result artifact are separate observations. The runner reports the actual exit code, accepted NUnit counts and root result, and captured editor completion text. Known native status codes retain their descriptions; ordinary exits remain unresolved exit/artifact discrepancies. A crash marker does not establish shutdown timing. Inconclusive cases remain inconclusive, and missing, malformed, empty or failed results still fail their existing gates.

A missing configure marker also attempts to save `configure.process-diagnostics.json` beside the configure log. It records the child process identity, UTC launch and observation boundaries, raw and returned exits, and watchdog termination provenance. On Windows, it queries Application Error and Windows Error Reporting events once, from two seconds before launch through the earlier of capture time or fifteen seconds after the process observation ended. It examines at most 64 candidates and retains at most eight matches, with explicit truncation flags. Events require the child's executable path and process ID, or a report ID joined to an event with that identity. Unrelated events and full event messages are excluded. The report distinguishes unavailable evidence and no observed matches; a missing or delayed event does not rule out a crash. The existing Package Manager cancellation retry also attempts to preserve its first attempt in `configure.first-attempt.process-diagnostics.json`.

Configure, in-editor test and Standalone Editor-build invocations also attempt to save
a `*.execution-diagnostics.json` receipt beside the invocation log (`unity.execution-diagnostics.json`
for tests and Standalone builds), including an invocation that exits zero,
later supplies passing NUnit XML, or produces a fresh player accepted by the existing build policy.
Standalone builds retain their wall-clock timeout without a completion sentinel or silence guard;
IL2CPP compilation can legitimately produce no output for minutes. The caller receives
the full watchdog result, including timeout and stall state.
The receipt uses the same bounded Windows event matcher and records process identity, raw and
returned exits, watchdog provenance, and UTC capture time. Its native-log summary counts
`Native Crash Reporting` headers and `Got a UNKNOWN while executing native code` lines separately,
retaining at most eight line numbers for each. These are two observations of a report and must
not be added to estimate an event count. Complete counters and a truncation flag accompany the
bounded locations; missing or unreadable logs have explicit states. The watchdog records whether
it saved the current log; failed or unobserved persistence leaves native-log evidence unavailable,
so an older readable file cannot certify a new attempt. Raw log lines and full
Windows event messages are excluded from the receipt, and credential redaction still runs before
writing it. Before a Package Manager retry overwrites the log and receipt, both first-attempt
files are preserved with `.first-attempt` in their names.

On Windows, these invocations also receive an isolated `-crash-report-folder` and a unique
`-upmLogFile`. Both write outside uploaded artifacts. The receipt's `Process.NativeEvidence`
links the raw Package Manager log, its redacted text snapshot, and the private
crash directory to that process observation. Each attempt has a new capture ID, so a retry cannot
reuse the preceding attempt's Package Manager log or crash files. Late Package Manager output
cannot append credentials to the published snapshot. This uses Unity's documented
[crash-folder override](https://docs.unity3d.com/2021.3/Documentation/Manual/LogFiles.html) and
[Package Manager log option](https://docs.unity.com/en-us/engine/6000.6/manual/unity-editor/command-line-arguments/editor).

Raw crash files remain in the runner account's temporary directory under `unity-helpers-native`,
outside uploaded artifacts. The inventory records up to 64 files within two directory levels,
their observed lengths and times, and SHA-256 hashes for files no larger than 256 MiB.
Readers permit active writers. Hashes cover at most the initially observed length, report the
number of bytes hashed, and flag length changes; a live-file hash is a bounded observation.
Only `.log` and `.txt` files are copied to artifacts, with credentials redacted and a
262,144-character limit per file. File-count limits, text truncation, unavailable hashes, failed
setup, and missing files are explicit. Review the recorded raw directory on the named runner
when a dump is needed for native stack and symbol analysis; remove it after preserving the
required evidence. Capture observes files available at that time, so a delayed crash writer may
finish afterward. An empty folder does not establish that the process was safe.

These receipts add evidence; they do not establish a native report's cause or change the existing
result gates. A receipt write failure warns without replacing the original process outcome.

Use those observations to investigate the original process on the named runner before selecting a repair. A successful later launch does not explain an earlier failure, and the diagnostic report does not replace the fresh configure marker or required player results.

### Historical native evidence readback

After timing collection finishes, recover delayed records on the machine named in the
original execution receipt:

```powershell
pwsh -NoProfile -File scripts/unity/capture-native-fault.ps1 `
  -ExecutionDiagnosticsPath 'D:\diagnostics\original\unity.execution-diagnostics.json' `
  -ExpectedMachine 'ELI-MACHINE' `
  -ArtifactsPath 'D:\diagnostics\readback-22536'
```

The expected, saved and actual machine names must agree before collection starts. The
output directory must be fresh. Readback preserves the original receipt bytes and hash,
queries the original process identity and event window, and inventories the original
private capture folder using the existing file, depth, hash and text limits. The old
Editor and generated project need not exist. It launches no Unity or debugger process
and changes no host settings.

The separate receipt records collection times and remains `EvidenceOnly`. Newly available
events can be recovered only within the original window, ending fifteen seconds after
the observed process exit; later event timestamps remain outside that query. Unavailable
queries or files and no matching observations remain distinct. Missing evidence does not
explain the original failure or establish test or performance acceptance.

For an existing **Unity Tests** run, dispatch that same workflow with
`native-readback-run-id` set to its exact numeric run ID, `acceptance: none`,
`native-diagnostics: false`, and `native-loader-snaps-comparison: false`. Wait for
all timing campaigns on the selected host to finish before dispatch. The job
checks for active Unity Editors; that check does not detect a standalone player
that continues after its Editor exits. Separate diagnostic concurrency prevents
cancelling qualification work, but does not serialize execution with it.

This route requests `[self-hosted, Windows, RAM-64GB, fast]` in both runner-access
preflight and job scheduling. The registered ELI-MACHINE runner carries `fast`;
its runner name is not itself a scheduling label. The job separately requires
`[Environment]::MachineName` to equal `ELI-MACHINE` before reading evidence.
It selects a completed
run from the current repository with exactly one attempt, verifies unique latest
acceptance and standalone artifact identities against that run and its source
revision, and downloads by artifact ID. Repeated attempts and missing, expired,
duplicate or truncated selections fail explicitly. Normal Unity jobs and success
aggregates skip in this diagnostic mode. Failure diagnostics remain available in
a separate artifact; they do not qualify the original run.

The standalone readback also passes `-TestFrameworkLogPath` with its saved
`unity.log`. For the observed test-framework 1.8.0 cache, it can copy the manifest
and two launcher source files from the persistent project named in that log.
Copies retain byte counts and hashes and are explicitly current cache observations;
they do not prove the removed measurement project's original build-source bytes.
Unavailable or rejected sources remain distinct. Readback writes an unfinished
receipt before querying events so an interrupted collection preserves its original
process identity. This route performs no new Unity, debugger or timing invocation.

### Opt-in native debugger capture

<!-- cspell:ignore gflag -->

For a recurring Unity 6000.6 access violation, dispatch **Unity Tests** with
`unity-version: 6000.6.0f1`, `test-mode: editmode`, and `native-diagnostics: true`.
The workflow retains its static version matrix: the version input does not restrict that matrix.
Regular jobs select EditMode; the separate `SINGLE_THREADED` job still runs all three modes.
After the regular EditMode step succeeds, the licensed runner executes one existing migration
test directly and then under Microsoft's CDB debugger in the same prepared project. The selected
case, editor hash, machine, process IDs, UTC boundaries, actual process exits, target exit events,
XML outcomes, and watchdog actions are recorded separately. These are diagnostic observations;
they cannot satisfy acceptance or explain a different earlier failure. Normal tests, license
return, lock release, and cleanup checks still run.

For the WProto Standalone build boundary, use `test-mode: standalone`,
`acceptance: none`, and `native-diagnostics: true`. After ordinary latest-version
Standalone succeeds, the existing acceptance driver creates a fresh owned WProto
project and wraps its one existing IL2CPP build in CDB by default. IL2CPP diagnostics use
`High` managed stripping, matching the ordinary Standalone lane. Mono diagnostics
retain `Disabled`; Unity documents that setting as
[Mono-only](https://docs.unity3d.com/6000.0/Documentation/Manual/managed-code-stripping-configure.html).
This selects the backend of the ordinary build fault; a clean Mono capture does not characterize an IL2CPP
failure. The exact build arguments,
caller working directory, inherited symbol-path environment, safe build identity
variables, debugger process outcome and unique target exit event are retained.
The dump stays private even though the debugger retains the caller's working directory.

To test whether the debugger's heap behavior affects a fault, separately opt into
`native-debugger-standard-heap: true`. This requires the Standalone debugger arm,
`native-diagnostics: true`, `native-build-direct-control: false`, and `acceptance: none`.
The collector adds only CDB's `-hd` launch option; Unity arguments, symbols, working directory
and ordinary test execution stay the same. Microsoft documents the
[standard-heap option](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/debugging-a-user-mode-process-using-cdb).
The receipt records the requested policy and inherited `_NO_DEBUG_HEAP` presence without
changing the environment or registry. It does not infer the target's actual heap state.
Compare separate captures on the same source before attributing a fault to this difference.
Missing or rejected captures and later successful exits retain their original dispositions.

For a fault that disappears when CDB launches Unity, separately select
`native-build-attach-control: true`. Keep `native-diagnostics: true`,
`test-mode: standalone`, `acceptance: none`, `native-build-direct-control: false`
and `native-debugger-standard-heap: false`. Unity starts directly in an isolated
collector worker. Before attaching CDB to its PID, the collector verifies a fresh
capture identity, machine, executable path/hash, actual creation time and process
parent ownership. It holds process handles while attaching; missing, exited,
reused or mismatched identities fail the capture without a replacement launch.

Target, worker and debugger outcomes remain separate. The receipt records the
attachment request and observed target PID. Bounded first/last debugger output
retains read-only global-flag and heap-summary commands at attachment; command
errors must be interpreted explicitly. This describes the attachment boundary,
not the startup heap. Attachment itself can change behavior. A zero exit cannot
establish a repaired cause or qualify timing. Target output is retained separately
and included in the build log; minidumps stay private. Timeouts and cancellation
record termination origin rather than presenting cleanup as a natural fault.
The existing static matrix, strict provenance and deliberate no-player stop still apply.

To preserve the reported domain-reload startup phase before attachment, additionally set
`native-build-attach-after-startup: true`. This requires the admitted attachment arm.
The collector waits for the first complete stdout line beginning
`Domain Reload Profiling:` under the original deadline. It records that line, observation time,
nonce and target PID before requesting CDB. Stderr, partial lines, an exited target or a
missing/mismatched observation cannot trigger attachment. This labels a reported reload
boundary; it does not prove every startup phase or the initial heap state.
The OS-observed command line and bounded first/last target output are retained separately.
An exact unique heap-end output line is required; the debugger's printed command text alone
cannot satisfy that check.

For a later boundary, use `native-build-attach-after-shutdown: true` with attachment enabled
and after-startup disabled. It waits for the first complete stdout line beginning
`##utp:{"type":"MemoryLeaks",`. This labels Unity's reported final telemetry, after the
shutdown messages in the retained Direct fault log. It does not prove shutdown completion
or a guaranteed interval before exit. The same nonce/PID/time, ownership, deadline and
no-player checks apply. A target that exits before attachment yields rejected evidence.
The historical `Attach.Startup` receipt member retains the observation; `Attach.Boundary`
labels it `reported-memory-leak-telemetry`.

Every debugger arm records the actual exception filters before continuing. It keeps
AV handling at second-chance break, not handled, and logs the exception record,
registers and up to 32 stack frames for the first eight first-chance AV events.
Later first-chance events retain the debugger's ordinary notification behavior;
the private second-chance fault dump remains enabled. The collector also records
calls to `ntdll!NtTerminateProcess`, including the x64 process-handle/status
registers and up to 32 caller frames. These observations can distinguish an
observed handled exception from an unhandled fault or an explicit termination
request. An AV-coded exit alone does not prove an exception, and missing trace
records do not prove that no exception or external termination occurred.
The first-chance command uses
[`sx-`](https://learn.microsoft.com/en-us/windows-hardware/drivers/debuggercmds/sx--sxd--sxe--sxi--sxn--sxr--sx---set-exceptions-)
to preserve the existing break and handling policy.

From an existing licensed, lock-owning context, pass
`-StandaloneBuildCapture Debugger -StandaloneBuildAttachToDirect` to the same
WProto acceptance command. The attach option requires an explicit evidence-only
debugger build and refuses a simultaneous standard-heap launch request.
Add `-StandaloneBuildAttachAfterStartup` to select the reported reload boundary, or
`-StandaloneBuildAttachAfterShutdown` for final telemetry. These options are mutually exclusive.

This build-only route deliberately throws after the post-build inventory, before
player launch. The diagnostic job and workflow therefore fail even when the
capture completes naturally; inspect `build-only-diagnostics/wproto/native-build-diagnostics/receipt.json`
and the frozen provenance separately. The receipt must be evidence-only with one
completed build case. Missing, ambiguous, timed-out or failed debugger outcomes do
not establish a completed capture. A target fault remains visible independently
of a zero debugger exit. A nonzero debugger return must numerically match the unique
observed target exit; both values remain distinct in the receipt. The ordinary player XML cannot qualify this separate build.
No performance fixture or timing campaign runs in this route. Normal dispatches
and their acceptance policy are unchanged; this route does not alter registry flags.

For a matching Direct workflow arm, set `native-build-direct-control: true` with
`native-diagnostics: true`, `test-mode: standalone`, and `acceptance: none`.
The hosted matrix job rejects other combinations before licensed work starts.
The default remains Debugger. Both arms retain the same IL2CPP/High/Release
settings and stop before player launch; neither qualifies timing or adoption.
Use separate source-bound runs and compare their recorded inputs and outcomes.

For a paired direct control from an existing licensed, lock-owning runner context,
invoke the same `run-acceptance.ps1` with `-Acceptance wproto`, the selected pinned
version, fresh artifact/temporary paths, `-StandaloneBuildCapture Direct`, and
`-StandaloneBuildCaptureBackend IL2CPP`.
Use a separate fresh invocation with `Debugger` and the same explicit backend for
the other arm. The driver defaults to Mono when the backend option is omitted.
An IL2CPP backend request without explicit build-only capture is refused; ordinary
WProto timing acceptance remains Mono-only. Both capture modes
stop with the explicit evidence-only error; neither returns player acceptance.
Keep source, editor, backend, settings and caller environment matched. These
controls characterize debugger effects and cannot by themselves resolve an older fault.

The driver downloads only twelve x64 debugger files from a versioned Microsoft bundle using
bounded HTTP ranges. Each file must match its committed size and SHA-256 digest, and CDB must
have a valid Microsoft signature. It does not register a system debugger or install tools globally.
CDB captures second-chance access violations with exception details, registers, stack, module
list, and a private minidump. It then continues the exception as unhandled. A separate breakpoint
records requests to exit; the target's exit event remains distinct from the debugger's exit code.
That breakpoint also runs read-only `!gflag` to observe the target's effective global flags.
Control programs must preserve normal exit zero and access-violation exit `0xC0000005` before
interpreting a new debugger configuration.

Runner startup diagnostics and the capture receipt read the 64-bit local-machine `GlobalFlag`
settings for `Unity.exe` under Image File Execution Options and for the system under Session
Manager. Each read reports observed, absent, unavailable, or invalid. Only observed numeric values
report whether the loader-snaps bit is set; absent or unreadable settings do not establish the
target's effective flags. These observations do not change the registry or enable debugging.
Loader tracing in debugger output is a lead to investigate, not proof of a Unity fault's cause.

To compare the same direct test with loader snaps enabled, disabled, and restored, also set
`native-loader-snaps-comparison: true`. This opt-in requires `native-diagnostics: true`,
`test-mode: editmode`, and `acceptance: none`. It temporarily changes only the image-specific
`Unity.exe` loader-snaps bit under the acquired organization Unity lock. It preserves other bits
and restores the exact original value and registry type. The comparison is off by default;
normal captures leave Unity's flags unchanged.
Use this mode only when a runner configuration change is authorized; the regular workflow matrix
and the separate `SINGLE_THREADED` job still run as described above.

Comparison requires a present supported value with loader snaps enabled and permission to write
that image key. It saves a new checksummed `loader-flag-lease.json` before any write. Each phase
requires the same editor and package-lock hashes, exactly one selected passing NUnit case, and a
natural process exit. Each phase records its registry snapshot and actual exit separately.
The driver saves a checksummed `.prepared` receipt through a read-only handle before requesting
registry write access, debugger setup, or the first direct phase. A denied write-handle request
therefore retains the original evidence. If capture fails or is cancelled before the write lease
exists, recovery reads that receipt
and verifies the exact original value and type through read-only access. An unchanged value does
not require write permission, including when an active lease already matches the original. A real
restoration still requires write permission. Missing or damaged evidence and
changed values fail recovery. An active write lease takes precedence; corrupt active evidence
cannot fall back to the prepared receipt.

The driver restores its active change in `finally`; an `always()` workflow step repeats recovery
before later steps. Automatic cleanup preserves external edits made before its write or after
restoration. Recovery refuses a current value or type that conflicts with both saved states.

After an interrupted run, preview recovery with
`scripts/unity/restore-native-loader-snaps.ps1 -LeasePath <saved-lease>` on the original machine;
pass `-Apply` to restore. Recovery validates the bounded receipt, checksum, machine, exact key,
original and disabled values, and current value. Keep the original receipt until restoration is
confirmed. A completed comparison is diagnostic evidence; it does not satisfy test acceptance.
Windows runner startup also runs the capture controls in a fresh temporary current-user registry
key and removes that key afterward. Those controls do not write Unity's image or system settings.

Raw dumps and logs remain under the runner account's local application data in
`unity-helpers-native-debugger/<capture-id>`, whose exact path is in the receipt. Only the receipt
and redacted console, editor, and Package Manager tails enter the EditMode artifacts under
`native-diagnostics/`. Console retention is limited to 524,288 characters; each file tail reads
at most 524,288 bytes of its initially observed file. Dump hashes are attempted only up to
256 MiB. Full debugger console output is not retained beyond its tail. A missing dump or exit
event is explicit. Retrieve available private files on the named runner
for further analysis, then remove them after preserving the required evidence. The command can
also be run locally with `-DebuggerDirectory` pointing to the same verified files and
`-TestFilter` selecting a single existing case. Each artifact path must be new.

Tool files and per-process capture directories occupy separate paths. Missing, incomplete, or
unexpected NUnit XML is recorded as diagnostic data after log tails and dump metadata are saved;
it does not interrupt fault-evidence collection or count as test acceptance.

Operator notes:

- **Confirm reuse.** Every run logs `Library: warm (reused)` or
  `Library: cold (first run on this runner)` in the `Ephemeral Unity project` group.
  A leg that reports `cold` on every run means the root is not surviving between
  jobs; check whether something is wiping `RUNNER_WORKSPACE`.
- **Disk is the trade.** This buys back cache transfer time by keeping the projects
  on local disk. `run-ci-tests.ps1` prunes least-recently-used sibling projects when
  free space drops below its floor (`-ProjectRootMinimumFreeGb`, default 60 GB) and
  never prunes the leg that is about to run. The runner diagnostics block prints
  `Workspace volume: <drive> N GB free of M GB` every run.
- **Force a cold rebuild** for one leg by deleting its directory under
  `unity-workspace\projects`; the next run regenerates it.
- A leg that moves between the two runners is handled automatically:
  `Clear-StaleUnityCompilationCache` notices the repo-root marker changed and clears
  `Library\Bee`, `ScriptAssemblies`, `PlayerScriptAssemblies`, and `Il2cppBuildCache`.

## Git compression tools for Actions cache

Self-hosted Windows Unity runners also need Git for Windows' Unix tools available to GitHub Actions cache steps. `actions/cache` restores and saves archives through `tar` and `gzip`; when the runner PATH exposes Git Bash but omits `C:\Program Files\Git\usr\bin`, cache post steps can warn with `gzip: command not found` and fail to save a cache entry. The Unity `Library` is no longer among them (see the section above), but the prepend stays because it is cheap and any future cache step on these runners needs it.

The `print-self-hosted-runner-diagnostics` composite action prepends Git's `usr\bin` directory to `$GITHUB_PATH` when it finds both `gzip.exe` and `tar.exe`, and emits a warning when that directory is absent. To verify locally on the runner:

```powershell
Get-Command gzip.exe
Get-Command tar.exe
```

If either command is missing, install Git for Windows or add `C:\Program Files\Git\usr\bin` to the runner service PATH, then restart the runner agent.

## Never use plain `shell: bash` on self-hosted Windows runners

On a self-hosted Windows runner, `shell: bash` can resolve to the WSL stub at `C:\Windows\System32\bash.exe`, which tries to launch a WSL distro that is usually not installed and fails with "Windows Subsystem for Linux has no installed distributions." The diagnostics composite warns when the runner PATH resolves bash to that stub (Git Bash must precede `System32` in PATH). Unity workflow steps therefore use `shell: pwsh` (or `shell: powershell` for steps that must run before PowerShell 7 is installed) rather than `shell: bash`.

## Windows host prerequisites (0xC0000135 / STATUS_DLL_NOT_FOUND)

If `Unity.exe` fails at startup with `-1073741515` / `0xC0000135` (STATUS_DLL_NOT_FOUND), the host is missing an OS-level dependency Unity imports, most commonly the Microsoft Visual C++ Redistributables (both the 2010 SP1 and the 2015-2022 x64 generations). This is an OS-level fix; `ensure-editor.ps1`'s Unity-reinstall retry loop cannot repair it (the missing DLL is on the OS, not in the Unity install). `ensure-editor.ps1` detects this case and short-circuits with a clear error rather than retrying futilely.

The manual `workflow_dispatch` workflow `.github/workflows/runner-bootstrap.yml` is the supported remote remediation path for this state. Dispatch it with the affected machine-name label (`DAD-MACHINE` or `ELI-MACHINE`) and leave `detect-only` disabled to run host maintenance. The workflow calls `scripts/unity/maintain-windows-runner.ps1`, which first runs `scripts/unity/bootstrap-windows-runner.ps1` for OS prerequisites and then verifies every Unity editor listed in `.github/unity-versions.json` through `ensure-editor.ps1`. If you are logged into the runner host directly, run `scripts\unity\maintain-windows-runner.ps1` from the checkout instead.

For emergency manual repair, apply the same host prerequisites that the bootstrap backend manages:

1. Install the Microsoft Visual C++ 2010 SP1 x64 Redistributable (provides `MSVCP100.dll` / `MSVCR100.dll`).
2. Install the Microsoft Visual C++ 2015-2022 x64 Redistributable (provides `VCRUNTIME140.dll`, `VCRUNTIME140_1.dll`, `MSVCP140.dll`).
3. Enable Windows long paths (`git config --system core.longpaths true` and the `LongPathsEnabled` registry value).
4. Add Windows Defender exclusions for the Unity install root and the runner work directory to avoid scan-induced timeouts.
5. Install PowerShell 7 (see above).

Re-run the queued Unity workflow once the bootstrap run completes successfully or the host is prepared manually. If the bootstrap dispatch stays queued, first verify that the target runner is online and carries the matching machine-name label; do not remove that label from `runner-bootstrap.yml` to force scheduling.

## Required secrets

The Unity workflows expect the following repository (or organization) secrets. They are NOT provisioned by this batch; a maintainer must add them before the first self-hosted run:

- `UNITY_SERIAL`, `UNITY_EMAIL`, `UNITY_PASSWORD`: classic serial Unity activation (all three required together).
- `BUILD_LOCK_APP_ID`, `BUILD_LOCK_APP_PRIVATE_KEY`: dedicated GitHub App credentials for the `wallstop-organization-builds` organization build lock (`Ambiguous-Interactive/ambiguous-organization-build-lock`); both are required together and should be provisioned as organization secrets with access to this repository.
- `BUILD_LOCK_READER_APP_ID`, `BUILD_LOCK_READER_APP_PRIVATE_KEY`: read-only GitHub App credentials used by the hosted runner preflight; both are required together and should be provisioned as organization secrets with access to this repository.
- `UNITY_ACCELERATOR_ENDPOINT`: optional; enables the Unity Accelerator cache namespace when set.

Provision the required Unity and build-lock credentials as organization secrets selected for this repository. The licensed workflows intentionally do not bind jobs to a per-repository environment, so trusted pull requests from branches in this repository validate automatically without an environment approval. Pull requests from forks remain ineligible for licensed jobs and do not receive these secrets.

## Focused native acceptance

Manually dispatch **Unity Tests** with `acceptance` set to `sentinel`, `intmap`, or `all`.
The default is `none`. The selected normal test modes still run; acceptance adds work under the
same organization license lease. The licensed workflow retains its static four-version matrix;
`unity-version` validates the requested version but does not reduce those jobs.
Artifacts named `unity-<version>-acceptance` contain separate result directories and a summary.
Each requested test, verification, credential redaction and upload must succeed. These optional
runs are separate from the benchmark dispatch and release export workflows.

For the frozen seven-family WProto synthetic baseline, dispatch **Unity Tests** on the feature
branch with `acceptance: wproto` and `test-mode: standalone`. No pull request is required. This
opt-in selection is excluded from `all`; its additional player runs execute only on
2021.3.45f1 and 6000.6.0f1. Both use Mono, Release code optimization, a non-development player,
and the generated-first `WALLSTOP_PROTO` facade. The exact corpus method selects ten read/write
API cases; a missing, skipped, duplicated or unexpected case cannot establish baseline evidence.
Normal selected correctness jobs, runner access checks, the organization lock and license cleanup
remain in place.

The workflow installs the pinned Node.js runtime before WProto acceptance. Direct script runs
also require a working `node` executable on `PATH`; the launcher checks it before creating a
campaign or starting Unity. A failed Node setup prevents the workflow from starting that campaign.

The additional WProto step allows 150 minutes. Its scoped configure timeout is 900 seconds per
attempt (at most one existing package-resolution retry), build timeout is 1800 seconds, and player
timeout is 3600 seconds. Previous timeout environment values are restored after success or failure.
A terminated or unsuccessful process cannot qualify the capture even if it wrote passing XML.
The player is not automatically rerun to obtain a more favorable timing result.

The `wproto` directory inside each `unity-<version>-acceptance` artifact retains the declaration,
source and player provenance, result XML and player log, plus `corpus-summary.json` when verification
succeeds. Before deleting the owned temporary project, the runner retains the actual player payload
and generated `Assets`, `Packages` and `ProjectSettings` inputs under the frozen report directory. It also retains
exact result XML and every original line containing `WPROTO_CORPUS_`, including malformed records.
The WProto build modifier writes a declaration-bound `build-preparation.json` receipt after the test
framework prepares its scene and the modifier saves settings and assets. Version 2 also copies the
actual `ProjectSettings.asset` to `build-player-settings.asset` before the framework restores it.
The copy must match the preparation inventory and is retained separately for replay.

The framework may temporarily change only the background, fullscreen, resizable-window, splash-screen
and splash-logo scalars. Before Unity 6000 it also sets the documented AOT trampoline option.
The verifier reconstructs those exact byte changes from the configured settings. It rejects every
other change, missing or duplicate scalar keys, invalid text, and files larger than 1 MiB.
Post-build settings must match the configured bytes. Every other configured input stays frozen;
only the generated test scene and its metadata, `ProjectSettings/SceneTemplateSettings.json` and
`ProjectSettings/UnityHelpersSettings.asset` may be added. All other prepared inputs must match the
build inventory, and every later snapshot must match the restored build inventory.

On Unity 6000.6 and newer, the WProto configurator saves an explicit Release managed-code variant
before the build, including for the IL2CPP build-only diagnostic. This prevents restoration from
adding an entry to an initially empty map. After the launcher temporarily selects Checked, the
WProto callback selects Release again for Mono; IL2CPP keeps Checked, matching ordinary IL2CPP test
builds. The verifier requires exactly this backend-specific preparation and byte-exact restoration
of the configured settings. Ordinary IL2CPP configuration and older Editors omit the new pin.
Version 1 receipts keep the original strict equality rule; existing rejected captures are not
reclassified.

Seven generated anchor files and four sealed evidence files are required for new WProto captures,
including the separately retained prepared settings. Late changes to the preparation receipt or
settings reject before retention completes. The separate borrowed-buffer route keeps its five-file
anchor, and legacy WProto receipts retain their three-file evidence set.
Full support logs may be redacted; their original hashes describe the observed invocation, not the
uploaded redacted bytes. The same verifier runs again after redaction and checks the retained
measurement records, XML and build inputs. After successful redaction, rejected diagnostics are also
uploaded; unsuccessful or skipped readback excludes `corpus-summary.json` so an earlier successful
summary cannot be published as current evidence. Readback failure still fails the acceptance gate.
The declaration binds the `wproto-seven-families-v1` campaign to
`scripts/unity/wproto-corpus.v1.json`, the source revision, and the compiler inputs. Artifacts retain
the existing 30-day lifetime; preserve accepted and rejected campaign evidence before expiry. Verification distinguishes complete raw capture from
stable timing and the twofold target. Unstable or below-target results remain visible; a successful
baseline capture does not claim representative consumer coverage, allocation or retained-memory
qualification, code-size qualification, or release acceptance.

For fresh IL2CPP conversion evidence, set `fresh-conversion=true` on a manual **Unity Tests**
dispatch. The default is `false`, preserving normal warm CI. This clears only `Bee`,
`ScriptAssemblies`, `PlayerScriptAssemblies`, and `Il2cppBuildCache` in each selected Standalone
project before configuration, inside the existing organization license lease. Regular,
`SINGLE_THREADED`, and WProto-only Standalone builds receive the request; EditMode and PlayMode
keep their caches. Package and import caches remain available. The script records the explicit
fresh-compilation reason in the runner job log; acceptance still requires the actual converter
invocation, source bindings, and passing native results. Direct script callers can pass
`-ForceFreshCompilationCache` only with Standalone IL2CPP mode.

The runner creates fresh projects and removes them after results are copied outside the project.
If an editor remains active, cleanup retains the project and fails with its path for diagnosis.
There are no automatic measurement retries. An inconclusive result is evidence to inspect, not a
reason to repeat runs until a favorable number appears.

`run-ci-tests.ps1 -TestFilter 'Namespace.Fixture.Method'` forwards Unity's test-name filter
in EditMode, PlayMode and standalone builds. It preserves the assembly and category filters.
The script no longer resolves an editor itself: set `UNITY_EDITOR_PATH` to a CI-managed editor
first (in CI the central `ensure-unity-editor` gate publishes it; locally run the repository's
`ensure-editor.ps1` helper with `-CiManagedOnly -RequireHealthyExisting`). Without it the script
fails closed. A filtered run requires at least one passing test: zero matches, all skipped, and
entirely inconclusive results cannot establish acceptance. The NUnit XML and logs remain
available when this check fails, including an IntMap measurement rejected for unstable timing.

The explicit `ValidationWorkspaceInteractionTests.NativePanelCallbacksRetainDraftAndPersistSettings`
fixture exercises native field-change and button-submit callbacks. It requires batchmode and a fresh,
expendable project whose directory name begins `sentinel-interaction-`. Before starting Unity, set
`WALLSTOP_SENTINEL_INTERACTION_PROJECT` to that exact absolute project root, set
`WALLSTOP_SENTINEL_INTERACTION_TOKEN` to a new GUID in 32-character `N` format, and write the same token
to `.sentinel-interaction-disposable` in the project root. Select the full test name in namespace
`WallstopStudios.UnityHelpers.Tests.Editor.Validation`, using its validation test assembly and a
fresh `-ProjectPath`. The fixture checks every marker before changing settings, Undo or native objects.

An exclusive CI checkout with no other active editor may supply the package through the runner's
normal `file:` dependency. A second package copy is unnecessary in that case: this fixture writes
only its disposable project's settings and creates objects it destroys afterward. A local shared
checkout requires an isolated source copy. The existing Library provenance markers do not authorize
this test in a developer project, and batchmode alone is insufficient.

Retain the NUnit XML and editor log, then discard the marked project. The fixture restores its
recorded settings and Undo group as cleanup, but it does not promise to preserve arbitrary preexisting
Undo/redo history or global subscribers. A pass proves callback delivery, draft retention and saved
settings; it does not prove pixels, mouse hit testing, focus, graph dragging or domain-reload recovery.

## Licensed benchmarks

**Unity Benchmarks** accepts manual dispatch. The hosted weekly scheduler dispatches the same
workflow on protected main; both workflows must be enabled in repository settings. The benchmark
job runs the Performance and Random assemblies in PlayMode on the latest supported editor.
It acquires the organization lock, returns the license, classifies cleanup, and releases the lock
before reporting and artifact uploads. Hosted fallback and an always-reporting aggregate reject
incomplete cleanup or missing execution.

The benchmark lane streams each test start and completion. Its no-output guard allows 35 minutes
of silence because individual performance cases allow up to 30 minutes. The overall Unity pass
remains bounded at 110 minutes, with cleanup time reserved before the step limit. Compatibility
tests retain their shorter default silence guard.

Superseded runs cancel within their ref group. Unity Tests uses separate groups for distinct
pushed commits, preserving the exact validation required by automatic release publication.
The central enrollment audit requires literal cancellation settings for the licensed queues.
A benchmark branch dispatch records diagnostic results without replacing the canonical baseline;
only complete successful measurements on main can refresh the published report.

The metric extractor retains dataset and operation labels for repeated tables. It accepts
Markdown headers followed by HTML data rows, as emitted by the sorting benchmarks, and keeps
each measured cell distinct. Duplicate identities still prevent publication.

## Package exports

Release Publish creates the `.unitypackage` on an `ubuntu-latest` runner without opening Unity or
using a license. `scripts/unity/create-unitypackage.js` stages the validated npm payload, renames
`Samples~` to `Samples`, preserves every tracked asset GUID, and writes the Unity archive plus its
SHA256 sidecar. Generated package-root and renamed-sample folder GUIDs are stable hashes of their
paths instead of fresh values from each temporary Unity project.

The Unity Tests smoke job remains on the `self-hosted`, `Windows`, `RAM-64GB` fleet. It imports and
compiles the same staged Assets payload with release optimization before using Unity's native
export API. This retains sample compilation and native import/export coverage. Release Publish
requires the latest Unity Tests run attempt at the selected source commit to have completed this
native smoke job, including successful acquire, export, return, classification, lock release, and
confirmed-cleanup steps in order. Missing, skipped, stale, or failed evidence prevents tag mutation
and publication. The smoke job acquires, activates, returns, classifies, and releases in the same
physical runner context, with a final cleanup gate.

The release gate script comes from the workflow commit. Its evidence query remains bound to the
selected source SHA, allowing an older validated source to use the current release controls.

The shell exporter remains available for local Docker comparison and delegates staging to the same
Node implementation. Compare the portable and native archives by logical path, asset bytes, and
metadata; Unity may rewrite text assets while importing them, so archive-byte equality is not a
valid compatibility check.

For a release-export canary, dispatch Release Publish with `export_only: true`, the package's current
version, and the candidate commit as `source_ref`. It validates metadata and produces both package
artifacts without waiting for a Unity runner, then skips tag mutation and publication. The selected
source SHA is retained in the job outputs so the canary can be compared with the smoke run on the
same revision.
