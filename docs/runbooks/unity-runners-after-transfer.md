<!-- cspell:ignore winget pwsh prereqs redist UCRT WSL Redistributables MSVCP MSVCR VCRUNTIME -->

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

After applying the chosen resolution, re-run the queued workflow from the Actions tab. The `runner-preflight` job added to each Unity workflow validates runner access from `ubuntu-latest` before any matrix entry attempts to dispatch onto self-hosted; a green preflight confirms the fix.

## Preflight diagnostic in this repository

Unity workflows run a `runner-preflight` job on `ubuntu-latest` before the self-hosted matrix. The preflight uses the organization reader GitHub App and requests only organization self-hosted-runner read permission. It asks GitHub for runner groups visible to `Ambiguous-Interactive/unity-helpers`, then considers only runners inside those groups when matching the exact `runs-on` labels.

The preflight fails closed. Missing reader credentials, an App authentication or API failure, no runner group visible to this repository, malformed or truncated inventory, or no accessible online runner with every required label all make the check red. It never emits a green soft pass. Busy online runners remain eligible because GitHub can queue the licensed job until one becomes idle.

The `Unity CI Success` aggregate job runs with `always()` and is the required branch-protection check. It rejects a failed or cancelled preflight and rejects an unexpected skipped licensed job, so a runner outage cannot produce a green Unity check merely because dependent jobs were skipped.

The reader App must be installed for the organization and expose the organization secrets `BUILD_LOCK_READER_APP_ID` and `BUILD_LOCK_READER_APP_PRIVATE_KEY` to this repository. Its organization permission is Self-hosted runners: read. No PAT or repository-level environment is required.

If the preflight passes but the matrix job still stays queued, the cause is more likely the dispatcher bug (see [GitHub Community Discussion #186811](https://github.com/orgs/community/discussions/186811)) than the access list. Use the recovery workflows in this repository: `.github/workflows/unstick-run.yml` for manual recovery of a single run, and `.github/workflows/stuck-job-watchdog.yml` for the automated 5-minute scan.

## Machine-name labels for runner bootstrap

The manual `.github/workflows/runner-bootstrap.yml` workflow performs host maintenance on one specific Windows runner. Each runner must therefore have a custom label that exactly matches its runner name:

- `DAD-MACHINE`
- `ELI-MACHINE`

The bootstrap job requests `self-hosted`, `Windows`, `RAM-64GB`, and the selected machine-name label. GitHub schedules a self-hosted job only on runners that have every requested label, so this prevents an ELI bootstrap from silently running on DAD, or the reverse. The workflow still keeps a hard runner-identity check as a final guard against label drift.

If a bootstrap dispatch stays queued after selecting a runner, verify the runner is online and that the matching machine-name label is present in Settings -> Actions -> Runners. Do not work around the queue by removing the machine-name label from the workflow; that reintroduces wrong-runner maintenance.

## Run maintenance directly on a Windows runner

When you are already on the runner host, you do not need to run YAML. From a checkout of this repository, run the same maintenance backend directly:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\unity\maintain-windows-runner.ps1
```

The script reads `.github\unity-versions.json` when `-UnityVersions` is omitted, uses `C:\Unity\Editors` unless `UNITY_EDITOR_INSTALL_ROOT` is set, provisions the `StandaloneWindowsIl2Cpp` profile, and writes diagnostics under `.artifacts\runner-bootstrap`.

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

## Git compression tools for Actions cache

Self-hosted Windows Unity runners also need Git for Windows' Unix tools available to GitHub Actions cache steps. `actions/cache` restores and saves archives through `tar` and `gzip`; when the runner PATH exposes Git Bash but omits `C:\Program Files\Git\usr\bin`, cache post steps can warn with `gzip: command not found` and fail to save the Unity Library cache.

The `print-self-hosted-runner-diagnostics` composite action prepends Git's `usr\bin` directory to `$GITHUB_PATH` when it finds both `gzip.exe` and `tar.exe`, and emits a warning when that directory is absent. To verify locally on the runner:

```powershell
Get-Command gzip.exe
Get-Command tar.exe
```

If either command is missing, install Git for Windows or add `C:\Program Files\Git\usr\bin` to the runner service PATH, then restart the runner agent.

## Never use plain `shell: bash` on self-hosted Windows runners

On a self-hosted Windows runner, `shell: bash` can resolve to the WSL stub at `C:\Windows\System32\bash.exe`, which tries to launch a WSL distro that is usually not installed and fails with "Windows Subsystem for Linux has no installed distributions." The diagnostics composite warns when the runner PATH resolves bash to that stub (Git Bash must precede `System32` in PATH). Unity workflow steps therefore use `shell: pwsh` (or `shell: powershell` for steps that must run before PowerShell 7 is installed) rather than `shell: bash`.

## Windows host prerequisites (0xC0000135 / STATUS_DLL_NOT_FOUND)

If `Unity.exe` fails at startup with `-1073741515` / `0xC0000135` (STATUS_DLL_NOT_FOUND), the host is missing an OS-level dependency Unity imports — most commonly the Microsoft Visual C++ Redistributables (both the 2010 SP1 and the 2015-2022 x64 generations). This is an OS-level fix; `ensure-editor.ps1`'s Unity-reinstall retry loop cannot repair it (the missing DLL is on the OS, not in the Unity install). `ensure-editor.ps1` detects this case and short-circuits with a clear error rather than retrying futilely.

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

- `UNITY_SERIAL`, `UNITY_EMAIL`, `UNITY_PASSWORD` — classic serial Unity activation (all three required together).
- `BUILD_LOCK_APP_ID`, `BUILD_LOCK_APP_PRIVATE_KEY` — dedicated GitHub App credentials for the `wallstop-organization-builds` organization build lock (`Ambiguous-Interactive/ambiguous-organization-build-lock`); both are required together and should be provisioned as organization secrets with access to this repository.
- `BUILD_LOCK_READER_APP_ID`, `BUILD_LOCK_READER_APP_PRIVATE_KEY` — read-only GitHub App credentials used by the hosted runner preflight; both are required together and should be provisioned as organization secrets with access to this repository.
- `UNITY_ACCELERATOR_ENDPOINT` — optional; enables the Unity Accelerator cache namespace when set.

Provision the required Unity and build-lock credentials as organization secrets selected for this repository. The licensed workflows intentionally do not bind jobs to a per-repository environment, so trusted pull requests from branches in this repository validate automatically without an environment approval. Pull requests from forks remain ineligible for licensed jobs and do not receive these secrets.
