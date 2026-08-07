#!/usr/bin/env pwsh
# cspell:ignore Il2cpp ims msiexec Redist WindowsApps
# Contract test: a job skipped by a job-level `if:` before matrix expansion must
# not use `matrix.*` in the job display name. GitHub renders those skipped names
# literally, which hides the actual gated job behind unresolved expressions.
[CmdletBinding()]
param([switch]$VerboseOutput)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info($msg) {
    if ($VerboseOutput) { Write-Host "[test-unity-workflow-matrix-contract] $msg" -ForegroundColor Cyan }
}

function Test-PrCapableAcquireIdentityInputs {
    param(
        [Parameter(Mandatory = $true)][string]$WorkflowContent,
        [Parameter(Mandatory = $true)][hashtable]$Jobs
    )

    if ($WorkflowContent -notmatch '(?m)^  pull_request:\s*$') {
        return $true
    }

    foreach ($job in $Jobs.GetEnumerator()) {
        [string]$jobText = $job.Value
        $acquireActionCount = [regex]::Matches(
            $jobText,
            'Ambiguous-Interactive/ambiguous-organization-build-lock/\.github/actions/acquire-build-lock@'
        ).Count
        if ($acquireActionCount -eq 0) {
            continue
        }

        $acquireSteps = @([regex]::Matches(
                $jobText,
                '(?ms)^      - name: Acquire organization Unity lock\s*$.*?(?=^      - name:|\z)'
            ))
        if ($acquireSteps.Count -ne $acquireActionCount) {
            return $false
        }

        foreach ($acquireStep in $acquireSteps) {
            foreach ($expectedInput in @(
                    'github-token: ${{ github.token }}',
                    'pull-request-number: ${{ github.event.pull_request.number }}',
                    'expected-head-sha: ${{ github.event.pull_request.head.sha }}'
                )) {
                if ([regex]::Matches($acquireStep.Value, "(?m)^          $([regex]::Escape($expectedInput))\s*$").Count -ne 1) {
                    return $false
                }
            }
        }
    }

    return $true
}

function Test-RunnerBootstrapPassesMaintenanceForce {
    param([Parameter(Mandatory = $true)][string]$Content)

    $maintenanceArgsHashtablePrefixPattern = '\$maintenanceArgs\s*(?:=|\+=)\s*(?:\[[^\]\r\n]+\]\s*)?@\{'
    $maintenanceArgsBlocks = @(
        [regex]::Matches($Content, "(?im)$maintenanceArgsHashtablePrefixPattern(?<body>[^\r\n}]*)\}") +
        [regex]::Matches($Content, "(?ims)$maintenanceArgsHashtablePrefixPattern\s*\r?\n(?<body>.*?)(?:^\s*\}|\z)")
    )
    $maintenanceArgsForceExpressionPattern = '(?:(?:\[[^\]\r\n]+\]\s*)?[''"]Force[''"]|\(\s*(?:\[[^\]\r\n]+\]\s*)?[''"]Force[''"]\s*\))'
    $maintenanceArgsForceKeyPattern = '(?im)(?:^|;)\s*(?:Force|' + $maintenanceArgsForceExpressionPattern + ')\s*='
    $maintenanceArgsHasForceKey = @(
        $maintenanceArgsBlocks |
            Where-Object { $_.Groups['body'].Value -match $maintenanceArgsForceKeyPattern }
    ).Count -gt 0

    $maintenanceArgsDirectForceAssignment = (
        $Content -match ('(?im)\$maintenanceArgs(?:\.Force|\[\s*' + $maintenanceArgsForceExpressionPattern + '\s*\])\s*(?:[-+*/%]?=)') -or
        $Content -match ('(?im)\$maintenanceArgs\.Item\(\s*' + $maintenanceArgsForceExpressionPattern + '\s*\)\s*(?:[-+*/%]?=)') -or
        $Content -match ('(?im)\$maintenanceArgs\.(?:Add|Set_Item)\(\s*' + $maintenanceArgsForceExpressionPattern + '\s*,')
    )

    return $maintenanceArgsHasForceKey -or $maintenanceArgsDirectForceAssignment
}

function Get-BuildLockActionPins {
    param(
        [Parameter(Mandatory = $true)][string]$GitHubRoot,
        [Parameter(Mandatory = $true)][string[]]$RequiredActionNames
    )

    # Anchored to a live `uses:` line. Matching the action path anywhere would let a commented-out
    # reference satisfy the required-action check and then supply the SHA every downstream
    # assertion compares against -- the exact vacuous pass this function exists to prevent.
    $pattern = '(?m)^\s*(?:-\s*)?uses:\s*Ambiguous-Interactive/ambiguous-organization-build-lock/\.github/actions/(?<name>[A-Za-z0-9._-]+)@(?<ref>[^\s#]+)(?:[ \t]+#[ \t]*(?<comment>\S+))?'
    $observed = @{}

    $files = @(
        Get-ChildItem -LiteralPath $GitHubRoot -Recurse -File |
            Where-Object { $_.Extension -eq '.yml' -or $_.Extension -eq '.yaml' } |
            Sort-Object FullName
    )
    foreach ($file in $files) {
        $relativePath = $file.FullName.Substring($GitHubRoot.Length).TrimStart('\', '/').Replace('\', '/')
        foreach ($match in [regex]::Matches((Get-Content -LiteralPath $file.FullName -Raw), $pattern)) {
            $name = $match.Groups['name'].Value
            if (-not $observed.ContainsKey($name)) {
                $observed[$name] = @()
            }
            $observed[$name] += [pscustomobject]@{
                Reference = $match.Groups['ref'].Value
                Comment   = if ($match.Groups['comment'].Success) { $match.Groups['comment'].Value } else { '' }
                File      = ".github/$relativePath"
            }
        }
    }

    $failed = $false
    foreach ($name in $RequiredActionNames) {
        if (-not $observed.ContainsKey($name)) {
            Write-Host "::error file=scripts/tests/test-unity-workflow-matrix-contract.ps1::No workflow or composite action references the build-lock action '$name'. Every assertion about it would pass vacuously."
            $failed = $true
        }
    }

    $pins = @{}
    foreach ($name in ($observed.Keys | Sort-Object)) {
        $usages = @($observed[$name])

        $unpinned = @($usages | Where-Object { $_.Reference -cnotmatch '^[0-9a-f]{40}$' })
        if ($unpinned.Count -gt 0) {
            $detail = ($unpinned | ForEach-Object { "$($_.File) -> @$($_.Reference)" }) -join ', '
            Write-Host "::error file=scripts/tests/test-unity-workflow-matrix-contract.ps1::Build-lock action '$name' must be pinned to a full 40-character commit SHA, never a tag or branch. Offending: $detail."
            $failed = $true
            continue
        }

        $distinctReferences = @(
            [string[]]@($usages | Select-Object -ExpandProperty Reference) |
                Sort-Object -CaseSensitive -Unique
        )
        if ($distinctReferences.Count -ne 1) {
            $detail = ($usages | ForEach-Object { "$($_.File) -> @$($_.Reference)" }) -join ', '
            Write-Host "::error file=scripts/tests/test-unity-workflow-matrix-contract.ps1::Build-lock action '$name' is pinned to $($distinctReferences.Count) different commits. A partial bump leaves two versions live against one Unity seat. Usages: $detail."
            $failed = $true
            continue
        }

        $distinctComments = @(
            [string[]]@($usages | Select-Object -ExpandProperty Comment) |
                Sort-Object -CaseSensitive -Unique
        )
        if ($distinctComments.Count -ne 1) {
            $detail = ($usages | ForEach-Object { "$($_.File) -> '# $($_.Comment)'" }) -join ', '
            Write-Host "::error file=scripts/tests/test-unity-workflow-matrix-contract.ps1::Build-lock action '$name' carries $($distinctComments.Count) different version comments for one commit. Usages: $detail."
            $failed = $true
            continue
        }

        $pins[$name] = [pscustomobject]@{
            Sha     = $distinctReferences[0]
            Comment = $distinctComments[0]
            Count   = $usages.Count
        }
    }

    if ($failed) {
        exit 1
    }

    return $pins
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# The build-lock pins are derived from the workflows rather than restated here. Restating a SHA in
# this file never added a supply-chain control -- the `uses:` line IS the pin, and anyone able to
# edit it can edit a literal here in the same commit -- but it did guarantee that every Dependabot
# bump of those actions turned this test red for a reason no failure message named, on a bot PR
# nobody owns. What a test can protect is structure, so that is what Get-BuildLockActionPins
# asserts: every reference is a full commit SHA rather than a movable tag, and every job agrees on
# one SHA and one version comment per action, so a partial bump still fails loudly.
$buildLockPins = Get-BuildLockActionPins -GitHubRoot (Join-Path $repoRoot '.github') -RequiredActionNames @(
    'acquire-build-lock',
    'release-build-lock',
    'check-unity-runner-availability',
    'require-current-pr-head',
    'require-confirmed-unity-cleanup',
    'classify-unity-cleanup-evidence'
)
$acquireBuildLockActionCommit = $buildLockPins['acquire-build-lock'].Sha
$acquireBuildLockActionComment = $buildLockPins['acquire-build-lock'].Comment
$buildLockActionCommit = $buildLockPins['release-build-lock'].Sha
$buildLockActionVersion = $buildLockPins['release-build-lock'].Comment
$runnerAvailabilityActionCommit = $buildLockPins['check-unity-runner-availability'].Sha
$runnerAvailabilityActionVersion = $buildLockPins['check-unity-runner-availability'].Comment
$currentPrHeadGuardCommit = $buildLockPins['require-current-pr-head'].Sha
$centralCleanupGateCommit = $buildLockPins['require-confirmed-unity-cleanup'].Sha
$centralCleanupClassifierCommit = $buildLockPins['classify-unity-cleanup-evidence'].Sha
Write-Info "Derived build-lock pins: $((($buildLockPins.Keys | Sort-Object) | ForEach-Object { "$_@$($buildLockPins[$_].Sha.Substring(0, 8))" }) -join ' ')"

$workflowPath = Join-Path $repoRoot '.github/workflows/unity-tests.yml'
$benchmarksWorkflowPath = Join-Path $repoRoot '.github/workflows/unity-benchmarks.yml'
$releaseWorkflowPath = Join-Path $repoRoot '.github/workflows/release.yml'
$runnerBootstrapPath = Join-Path $repoRoot '.github/workflows/runner-bootstrap.yml'
$actionlintPath = Join-Path $repoRoot '.github/actionlint.yaml'
$runnerRunbookPath = Join-Path $repoRoot 'docs/runbooks/unity-runners-after-transfer.md'
$runnerDiagnosticsActionPath = Join-Path $repoRoot '.github/actions/print-self-hosted-runner-diagnostics/action.yml'
$returnUnityLicenseActionPath = Join-Path $repoRoot '.github/actions/return-unity-license/action.yml'
$unityVersionsPath = Join-Path $repoRoot '.github/unity-versions.json'
$integrationPackagesPath = Join-Path $repoRoot '.github/integration-packages.json'
$windowsRunnerBootstrapPath = Join-Path $repoRoot 'scripts/unity/bootstrap-windows-runner.ps1'
$windowsRunnerMaintenancePath = Join-Path $repoRoot 'scripts/unity/maintain-windows-runner.ps1'
$ensureEditorPath = Join-Path $repoRoot 'scripts/unity/ensure-editor.ps1'
$runCiTestsPath = Join-Path $repoRoot 'scripts/unity/run-ci-tests.ps1'
$runUnityDockerPath = Join-Path $repoRoot 'scripts/unity/run-unity-docker.sh'

if (-not (Test-Path -LiteralPath $workflowPath)) {
    Write-Host "::error::Unity workflow not found: $workflowPath"
    exit 1
}
if (-not (Test-Path -LiteralPath $benchmarksWorkflowPath)) {
    Write-Host "::error::Unity benchmarks workflow not found: $benchmarksWorkflowPath"
    exit 1
}
if (-not (Test-Path -LiteralPath $releaseWorkflowPath)) {
    Write-Host "::error::Release workflow not found: $releaseWorkflowPath"
    exit 1
}
if (-not (Test-Path -LiteralPath $runnerBootstrapPath)) {
    Write-Host "::error::Runner bootstrap workflow not found: $runnerBootstrapPath"
    exit 1
}
if (-not (Test-Path -LiteralPath $actionlintPath)) {
    Write-Host "::error::Actionlint config not found: $actionlintPath"
    exit 1
}
if (-not (Test-Path -LiteralPath $runnerRunbookPath)) {
    Write-Host "::error::Unity runner runbook not found: $runnerRunbookPath"
    exit 1
}
if (-not (Test-Path -LiteralPath $runnerDiagnosticsActionPath)) {
    Write-Host "::error::Self-hosted runner diagnostics action not found: $runnerDiagnosticsActionPath"
    exit 1
}
if (-not (Test-Path -LiteralPath $returnUnityLicenseActionPath)) {
    Write-Host "::error::Return Unity license action not found: $returnUnityLicenseActionPath"
    exit 1
}
if (-not (Test-Path -LiteralPath $unityVersionsPath)) {
    Write-Host "::error::Unity versions config not found: $unityVersionsPath"
    exit 1
}
if (-not (Test-Path -LiteralPath $integrationPackagesPath)) {
    Write-Host "::error::Integration package config not found: $integrationPackagesPath"
    exit 1
}
if (-not (Test-Path -LiteralPath $windowsRunnerBootstrapPath)) {
    Write-Host "::error::Windows runner bootstrap script not found: $windowsRunnerBootstrapPath"
    exit 1
}
if (-not (Test-Path -LiteralPath $windowsRunnerMaintenancePath)) {
    Write-Host "::error::Windows runner maintenance script not found: $windowsRunnerMaintenancePath"
    exit 1
}
if (-not (Test-Path -LiteralPath $ensureEditorPath)) {
    Write-Host "::error::Unity ensure-editor script not found: $ensureEditorPath"
    exit 1
}
if (-not (Test-Path -LiteralPath $runCiTestsPath)) {
    Write-Host "::error::Unity run-ci-tests script not found: $runCiTestsPath"
    exit 1
}

function Import-EnsureEditorWatchdogFunctions {
    param([Parameter(Mandatory = $true)][string]$ScriptPath)

    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref]$tokens, [ref]$errors)
    if ($errors -and $errors.Count -gt 0) {
        $details = @($errors | ForEach-Object { "$($_.Extent.StartLineNumber): $($_.Message)" })
        throw "ensure-editor.ps1 has parse errors: $($details -join '; ')"
    }

    foreach ($name in @(
        'ConvertTo-ProcessArgumentLine',
        'Get-EnsureEditorRetryDelaySeconds',
        'Get-EnsureEditorInstallTimeoutSeconds',
        'Get-EnsureEditorProbeTimeoutSeconds',
        'Get-EffectiveUnityCliTimeoutSeconds',
        'Get-RemainingUnityProvisioningBudgetSeconds',
        'Get-EnsureEditorProgressStallSeconds',
        'Get-EnsureEditorProgressNoticeIntervalSeconds',
        'Get-EnsureEditorQuarantineMoveRetryAttempts',
        'Invoke-WithRetry',
        'Test-IsPathInsideDirectory',
        'Get-UnityCiAlternateInstallRoot',
        'Get-UnityEditorCandidates',
        'Find-UnityEditor',
        'Get-MissingRequiredEditorPayloadPaths',
        'Test-UnityAtomicInstallFailureMayBePinnedToExistingEditor',
        'Install-UnityEditorModulesViaAtomicReinstall',
        'Get-CollapsedCliOutputTail',
        'Get-CliProgressTriple',
        'Get-LastCliProgressMessage',
        'Write-CiNotice',
        'Install-UnityEditorWithCiModules',
        'Invoke-UnityCliCaptureWithTimeout',
        'Invoke-UnityCliSafe',
        'Get-UnityCliOutput',
        'Move-UnityInstallDirectoryToQuarantine',
        'Get-UnityProvisioningProfile'
    )) {
        $functionAst = $ast.FindAll(
            {
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
            },
            $true
        ) | Select-Object -First 1
        if (-not $functionAst) {
            throw "Function '$name' not found in ensure-editor.ps1"
        }

        Invoke-Expression "function script:$name $($functionAst.Body.Extent.Text)"
    }
}

function Get-EnsureEditorInstallTimeoutForProfile {
    param([Parameter(Mandatory = $true)][string]$Profile)

    $script:UnityProvisioningProfile = $Profile
    return Get-EnsureEditorInstallTimeoutSeconds
}

function Invoke-EnsureEditorWatchdogProbe {
    param(
        [Parameter(Mandatory = $true)][string]$ChildCommand,
        [int]$StallSeconds = 1,
        [int]$TimeoutSeconds = 30
    )

    return Invoke-UnityCliCaptureWithTimeout `
        -Arguments @('-NoProfile', '-Command', $ChildCommand) `
        -TimeoutSeconds $TimeoutSeconds `
        -TimeoutKnob 'TEST_TIMEOUT_SECONDS' `
        -StallSeconds $StallSeconds `
        -StallKnob 'TEST_STALL_SECONDS'
}

function Import-RunCiTestsFunction {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [Parameter(Mandatory = $true)][string]$FunctionName
    )

    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref]$tokens, [ref]$errors)
    if ($errors -and $errors.Count -gt 0) {
        $details = @($errors | ForEach-Object { "$($_.Extent.StartLineNumber): $($_.Message)" })
        throw "run-ci-tests.ps1 has parse errors: $($details -join '; ')"
    }

    $functionAst = $ast.FindAll(
        {
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq $FunctionName
        },
        $true
    ) | Select-Object -First 1
    if (-not $functionAst) {
        throw "Function '$FunctionName' not found in run-ci-tests.ps1"
    }

    Invoke-Expression "function script:$FunctionName $($functionAst.Body.Extent.Text)"
}

function Get-WorkflowJobTexts {
    param([string[]]$WorkflowLines)

    $texts = @{}
    $insideWorkflowJobs = $false
    for ($lineIndex = 0; $lineIndex -lt $WorkflowLines.Count; $lineIndex++) {
        if ($WorkflowLines[$lineIndex] -match '^jobs:\s*$') {
            $insideWorkflowJobs = $true
            continue
        }

        if (-not $insideWorkflowJobs) {
            continue
        }

        if ($WorkflowLines[$lineIndex] -match '^[A-Za-z0-9_-]+:\s*$') {
            break
        }

        $jobMatch = [regex]::Match($WorkflowLines[$lineIndex], '^  ([A-Za-z0-9_-]+):\s*$')
        if (-not $jobMatch.Success) {
            continue
        }

        $jobId = $jobMatch.Groups[1].Value
        $start = $lineIndex
        $end = $WorkflowLines.Count
        for ($nextLineIndex = $lineIndex + 1; $nextLineIndex -lt $WorkflowLines.Count; $nextLineIndex++) {
            if ($WorkflowLines[$nextLineIndex] -match '^  [A-Za-z0-9_-]+:\s*$') {
                $end = $nextLineIndex
                break
            }
        }

        $texts[$jobId] = (@($WorkflowLines[$start..($end - 1)]) -join "`n")
        $lineIndex = $end - 1
    }

    return $texts
}

function Test-UnityLockCleanupIsGated {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Jobs,
        [Parameter(Mandatory = $true)][string]$WorkflowFile,
        [Parameter(Mandatory = $true)][hashtable]$LicensedWorkStepNames
    )

    $acquireUses = "Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/acquire-build-lock@$acquireBuildLockActionCommit"
    $releaseUses = "Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/release-build-lock@$buildLockActionCommit"
    $returnUses = './.github/actions/return-unity-license'
    $requiredCleanupGate = 'if: ${{ always() && steps.unity_lock.outcome == ''success'' }}'
    $requiredReleaseGate = 'if: ${{ always() && (steps.unity_lock.outcome == ''success'' || steps.unity_lock.outcome == ''failure'' || steps.unity_lock.outcome == ''cancelled'') }}'
    $acquireUsesLineSuffix = '[ \t]+# ' + [regex]::Escape($acquireBuildLockActionComment) + '[ \t]*\r?$'
    $buildLockUsesLineSuffix = '[ \t]+# ' + [regex]::Escape($buildLockActionVersion) + '[ \t]*\r?$'

    $acquirePattern = '(?m)- name: Acquire organization Unity lock\s*\r?\n\s+id:\s+unity_lock\s*\r?\n(?:[^\r\n]*\r?\n)*?\s+uses:\s+' + [regex]::Escape($acquireUses) + $acquireUsesLineSuffix
    $returnPattern = '(?ms)- name: Return Unity license\s*\r?\n\s+id:\s+return_unity_license\s*\r?\n\s+' + [regex]::Escape($requiredCleanupGate) + '\s*\r?\n\s+timeout-minutes:\s+5\s*\r?\n\s+continue-on-error:\s+true\s*\r?\n\s+uses:\s+' + [regex]::Escape($returnUses)
    $releasePattern = '(?m)- name: Release organization Unity lock\s*\r?\n\s+id:\s+release_unity_lock\s*\r?\n\s+' + [regex]::Escape($requiredReleaseGate) + '\s*\r?\n\s+timeout-minutes:\s+5\s*\r?\n\s+uses:\s+' + [regex]::Escape($releaseUses) + $buildLockUsesLineSuffix
    $failures = @()

    foreach ($job in $Jobs.GetEnumerator()) {
        $jobText = [string]$job.Value
        $usesUnityLock = (
            $jobText.Contains($acquireUses) -or
            $jobText.Contains($releaseUses) -or
            $jobText.Contains($returnUses)
        )
        if (-not $usesUnityLock) {
            continue
        }

        $acquireIndex = $jobText.IndexOf('- name: Acquire organization Unity lock', [StringComparison]::Ordinal)
        $returnIndex = $jobText.IndexOf('- name: Return Unity license', [StringComparison]::Ordinal)
        $releaseIndex = $jobText.IndexOf('- name: Release organization Unity lock', [StringComparison]::Ordinal)
        [string[]]$declaredLicensedWorkSteps = if ($LicensedWorkStepNames.ContainsKey($job.Key)) {
            @($LicensedWorkStepNames[$job.Key] | ForEach-Object { [string]$_ })
        } else {
            @()
        }
        $acquireStep = [regex]::Match($jobText, '(?ms)^\s+- name: Acquire organization Unity lock\s*$.*?(?=^\s+- name:|\z)')
        $releaseStep = [regex]::Match($jobText, '(?ms)^\s+- name: Release organization Unity lock\s*$.*?(?=^\s+- name:|\z)')
        $returnStep = [regex]::Match($jobText, '(?ms)^\s+- name: Return Unity license\s*$.*?(?=^\s+- name:|\z)')
        $acquireHolder = [regex]::Match($acquireStep.Value, '(?m)^\s+holder-id-suffix:\s*(?<value>[^\r\n]+)')
        $releaseHolder = [regex]::Match($releaseStep.Value, '(?m)^\s+holder-id-suffix:\s*(?<value>[^\r\n]+)')
        $acquireRunner = [regex]::Match($acquireStep.Value, '(?m)^\s+runner-id:\s*(?<value>[^\r\n]+)')
        $releaseRunner = [regex]::Match($releaseStep.Value, '(?m)^\s+runner-id:\s*(?<value>[^\r\n]+)')

        if ($jobText -notmatch $acquirePattern) {
            $failures += "$($job.Key): acquire step must have id unity_lock before uses"
        }
        if ($jobText -notmatch $returnPattern) {
            $failures += "$($job.Key): return-unity-license must be identified, success-gated, bounded to five minutes, and non-masking"
        }
        if ($returnStep.Success -and (
                $returnStep.Value -notmatch '(?m)^\s+prior-return-log-path:\s+\S.*$' -or
                $returnStep.Value -notmatch '(?ms)^\s+prior-command-succeeded:\s+(?:>-\s*\r?\n\s*)?\$\{\{\s+.+?\s+\}\}\s*(?=^\s+env:)'
            )) {
            $failures += "$($job.Key): return-unity-license must classify the licensed command's log and successful outcome"
        }
        if ($jobText -notmatch $releasePattern) {
            $failures += "$($job.Key): release-build-lock must be five-minute bounded and run after every non-skipped acquire outcome"
        }
        if ($declaredLicensedWorkSteps.Count -eq 0) {
            $failures += "$($job.Key): every lock-owning job must declare at least one licensed-work step"
        }
        foreach ($licensedWorkStepName in $declaredLicensedWorkSteps) {
            $licensedWorkIndex = $jobText.IndexOf("- name: $licensedWorkStepName", [StringComparison]::Ordinal)
            $licensedWorkStep = [regex]::Match(
                $jobText,
                '(?ms)^\s+- name: ' + [regex]::Escape($licensedWorkStepName) + '\s*$.*?(?=^\s+- name:|\z)'
            )
            if (-not (0 -le $acquireIndex -and $acquireIndex -lt $licensedWorkIndex -and $licensedWorkIndex -lt $returnIndex -and $returnIndex -lt $releaseIndex)) {
                $failures += "$($job.Key): lock lifecycle order must be acquire, licensed work '$licensedWorkStepName', identified cleanup, then release"
            }
            $timeoutMatch = [regex]::Match($licensedWorkStep.Value, '(?m)^\s+timeout-minutes:\s+(?<value>\S.*)$')
            $timeoutValue = if ($timeoutMatch.Success) { $timeoutMatch.Groups['value'].Value.Trim() } else { '' }
            $literalTimeout = 0
            $conditionalTimeoutMatch = [regex]::Match(
                $timeoutValue,
                '^\$\{\{\s*\(matrix\.test-mode == ''standalone'' && (?<standalone>\d+)\) \|\| (?<default>\d+)\s*\}\}$'
            )
            $hasPositiveTimeout = (
                ([int]::TryParse($timeoutValue, [ref]$literalTimeout) -and $literalTimeout -gt 0) -or
                ($conditionalTimeoutMatch.Success -and
                    [int]$conditionalTimeoutMatch.Groups['standalone'].Value -gt 0 -and
                    [int]$conditionalTimeoutMatch.Groups['default'].Value -gt 0)
            )
            if (-not $licensedWorkStep.Success -or -not $hasPositiveTimeout) {
                $failures += "$($job.Key): licensed work '$licensedWorkStepName' must have a positive literal or contract-evaluable step timeout so a hung Unity process cannot retain the shared seat until the job timeout"
            }
        }
        if (-not $acquireHolder.Success -or -not $releaseHolder.Success -or $acquireHolder.Groups['value'].Value.Trim() -ne $releaseHolder.Groups['value'].Value.Trim()) {
            $failures += "$($job.Key): acquire and release must use the same holder-id-suffix"
        }
        if (-not $acquireRunner.Success -or -not $releaseRunner.Success -or $acquireRunner.Groups['value'].Value.Trim() -ne $releaseRunner.Groups['value'].Value.Trim()) {
            $failures += "$($job.Key): acquire and release must use the same runner-id"
        }
        if (
            $releaseStep.Value -notmatch '(?m)^\s+resource-cleanup-status:\s+\$\{\{ steps\.return_unity_license\.outputs\.resource-cleanup-status \}\}\s*$' -or
            $releaseStep.Value -notmatch '(?m)^\s+resource-health:\s+\$\{\{ steps\.return_unity_license\.outputs\.resource-health \}\}\s*$' -or
            $releaseStep.Value -notmatch '(?m)^\s+resource-reason:\s+\$\{\{ steps\.return_unity_license\.outputs\.resource-reason \}\}\s*$'
        ) {
            $failures += "$($job.Key): release must pass the identified cleanup status, health, and reason outputs"
        }
    }

    if ($failures.Count -gt 0) {
        Write-Host "::error file=$WorkflowFile::Unity lock cleanup contract failed: $($failures -join '; ')"
        return $false
    }

    return $true
}

function Test-UnityLockAppConfiguration {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$WorkflowFile
    )

    $lockSteps = @(
        [regex]::Matches(
            $Content,
            '(?ms)^\s+- name: (?:Acquire|Release) organization Unity lock\s*$.*?(?=^\s+- name:|\z)'
        )
    )
    $failures = @()

    if ($lockSteps.Count -eq 0) {
        $failures += 'workflow must contain at least one Unity lock step'
    }

    foreach ($lockStep in $lockSteps) {
        $stepText = $lockStep.Value
        $isAcquireStep = $stepText.Contains('Acquire organization Unity lock')
        $stepKind = if ($isAcquireStep) { 'Acquire' } else { 'Release' }
        $expectedAction = if ($isAcquireStep) {
            "Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/acquire-build-lock@$acquireBuildLockActionCommit"
        } else {
            "Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/release-build-lock@$buildLockActionCommit"
        }

        $expectedComment = if ($isAcquireStep) { $acquireBuildLockActionComment } else { $buildLockActionVersion }
        $exactActionPattern = '(?m)^\s+uses:\s+' + [regex]::Escape($expectedAction) + '[ \t]+# ' + [regex]::Escape($expectedComment) + '[ \t]*\r?$'
        if ($stepText -notmatch $exactActionPattern) {
            $failures += "$stepKind lock step must use $expectedAction"
        }
        if ($stepText -notmatch '(?m)^\s+runner-id:\s+\$\{\{ runner\.name \}\}\s*$') {
            $failures += "$stepKind lock step must pass runner-id from runner.name"
        }
        if ($stepText -notmatch '(?m)^\s+BUILD_LOCK_APP_ID:\s+\$\{\{ secrets\.BUILD_LOCK_APP_ID \}\}\s*$') {
            $failures += "$stepKind lock step must pass the GitHub App ID secret"
        }
        if ($stepText -notmatch '(?m)^\s+BUILD_LOCK_APP_PRIVATE_KEY:\s+\$\{\{ secrets\.BUILD_LOCK_APP_PRIVATE_KEY \}\}\s*$') {
            $failures += "$stepKind lock step must pass the GitHub App private key secret"
        }
    }

    $legacyTokenPattern = '(?:ORG_)?BUILD_LOCK_' + 'TOKEN'
    if ($Content -match $legacyTokenPattern) {
        $failures += 'legacy build lock tokens must not be referenced'
    }

    if ($failures.Count -gt 0) {
        Write-Host "::error file=$WorkflowFile::Unity lock App configuration contract failed: $($failures -join '; ')"
        return $false
    }

    return $true
}

[string[]]$lines = Get-Content -LiteralPath $workflowPath
[string]$workflowContent = $lines -join "`n"
[string[]]$benchmarksWorkflowLines = Get-Content -LiteralPath $benchmarksWorkflowPath
[string]$benchmarksWorkflowContent = $benchmarksWorkflowLines -join "`n"
[string[]]$releaseWorkflowLines = Get-Content -LiteralPath $releaseWorkflowPath
[string[]]$runnerBootstrapLines = Get-Content -LiteralPath $runnerBootstrapPath
[string]$runnerBootstrapContent = Get-Content -LiteralPath $runnerBootstrapPath -Raw
[string]$actionlintContent = Get-Content -LiteralPath $actionlintPath -Raw
[string]$runnerRunbookContent = Get-Content -LiteralPath $runnerRunbookPath -Raw
[string]$runnerDiagnosticsActionContent = Get-Content -LiteralPath $runnerDiagnosticsActionPath -Raw
[string]$returnUnityLicenseActionContent = Get-Content -LiteralPath $returnUnityLicenseActionPath -Raw
[string]$windowsRunnerBootstrapContent = Get-Content -LiteralPath $windowsRunnerBootstrapPath -Raw
[string]$windowsRunnerMaintenanceContent = Get-Content -LiteralPath $windowsRunnerMaintenancePath -Raw
[string]$ensureEditorContent = Get-Content -LiteralPath $ensureEditorPath -Raw
[string]$runCiTestsContent = Get-Content -LiteralPath $runCiTestsPath -Raw
[string]$runUnityDockerContent = Get-Content -LiteralPath $runUnityDockerPath -Raw
$unityVersionsConfig = Get-Content -LiteralPath $unityVersionsPath -Raw | ConvertFrom-Json
$integrationPackagesConfig = Get-Content -LiteralPath $integrationPackagesPath -Raw | ConvertFrom-Json
[string[]]$unityVersions = @(
    $unityVersionsConfig.all |
        ForEach-Object { [string]$_ } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)
[bool]$failed = $false
[bool]$insideJobs = $false
$jobTexts = Get-WorkflowJobTexts -WorkflowLines $lines
$benchmarksJobTexts = Get-WorkflowJobTexts -WorkflowLines $benchmarksWorkflowLines
$releaseJobTexts = Get-WorkflowJobTexts -WorkflowLines $releaseWorkflowLines
$runnerBootstrapJobTexts = Get-WorkflowJobTexts -WorkflowLines $runnerBootstrapLines

$maintenanceTokens = $null
$maintenanceParseErrors = $null
$windowsRunnerMaintenanceAst = [System.Management.Automation.Language.Parser]::ParseFile(
    $windowsRunnerMaintenancePath,
    [ref]$maintenanceTokens,
    [ref]$maintenanceParseErrors
)
if ($maintenanceParseErrors -and $maintenanceParseErrors.Count -gt 0) {
    $details = @($maintenanceParseErrors | ForEach-Object { "$($_.Extent.StartLineNumber): $($_.Message)" })
    Write-Host "::error file=scripts/unity/maintain-windows-runner.ps1::Could not parse runner maintenance script: $($details -join '; ')"
    $failed = $true
}

$runnerMaintenanceScriptParameters = @()
if ($windowsRunnerMaintenanceAst.ParamBlock) {
    $runnerMaintenanceScriptParameters = @($windowsRunnerMaintenanceAst.ParamBlock.Parameters)
}
$runnerMaintenanceFunctionAst = $windowsRunnerMaintenanceAst.FindAll(
    {
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-WindowsRunnerMaintenance'
    },
    $true
) | Select-Object -First 1
if (-not $runnerMaintenanceFunctionAst) {
    Write-Host "::error file=scripts/unity/maintain-windows-runner.ps1::Runner maintenance script must define Invoke-WindowsRunnerMaintenance."
    $failed = $true
}
$runnerMaintenanceFunctionParameters = @()
if ($runnerMaintenanceFunctionAst -and $runnerMaintenanceFunctionAst.Body.ParamBlock) {
    $runnerMaintenanceFunctionParameters = @($runnerMaintenanceFunctionAst.Body.ParamBlock.Parameters)
}

$ensureEditorTokens = $null
$ensureEditorParseErrors = $null
$ensureEditorAst = [System.Management.Automation.Language.Parser]::ParseFile(
    $ensureEditorPath,
    [ref]$ensureEditorTokens,
    [ref]$ensureEditorParseErrors
)
if ($ensureEditorParseErrors -and $ensureEditorParseErrors.Count -gt 0) {
    $details = @($ensureEditorParseErrors | ForEach-Object { "$($_.Extent.StartLineNumber): $($_.Message)" })
    Write-Host "::error file=scripts/unity/ensure-editor.ps1::Could not parse ensure-editor script: $($details -join '; ')"
    $failed = $true
}

function Get-FunctionAstByName {
    param(
        [Parameter(Mandatory = $true)]$Ast,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $Ast.FindAll(
        {
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq $Name
        },
        $true
    ) | Select-Object -First 1
}

function Get-FunctionCommandNames {
    param([Parameter(Mandatory = $true)]$FunctionAst)

    @(
        $FunctionAst.Body.FindAll(
            {
                param($node)
                $node -is [System.Management.Automation.Language.CommandAst]
            },
            $true
        ) | ForEach-Object { $_.GetCommandName() } | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_)
        }
    )
}

function Get-CommandIndex {
    param(
        [Parameter(Mandatory = $true)][string[]]$Commands,
        [Parameter(Mandatory = $true)][string]$Name,
        [int]$StartIndex = 0
    )

    for ($index = [Math]::Max(0, $StartIndex); $index -lt $Commands.Count; $index++) {
        if ($Commands[$index] -eq $Name) {
            return $index
        }
    }

    return -1
}

if ($unityVersions.Count -lt 1) {
    Write-Host "::error file=.github/unity-versions.json::Unity CI version config must define at least one entry in all[]."
    $failed = $true
} elseif ($unityVersions[-1] -ne '6000.5.2f1') {
    Write-Host "::error file=.github/unity-versions.json::Unity 6000.5.2f1 must be the latest tracked Unity version so Unity 6000.5 regressions are caught in CI."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity version source of truth includes Unity 6000.5.2f1 as the latest version."
}

$integrationPackagesNode = $integrationPackagesConfig.PSObject.Properties['packages']
$reflexVersionNode = $null
if ($integrationPackagesNode -and $null -ne $integrationPackagesNode.Value) {
    $reflexVersionNode = $integrationPackagesNode.Value.PSObject.Properties['com.gustavopsantos.reflex']
}
$reflexVersionText = if ($reflexVersionNode) { [string]$reflexVersionNode.Value } else { $null }
if ([string]::IsNullOrWhiteSpace($reflexVersionText)) {
    Write-Host "::error file=.github/integration-packages.json::Integration package config must pin com.gustavopsantos.reflex so REFLEX_PRESENT integration legs are deterministic."
    $failed = $true
} else {
    $semverMatch = [regex]::Match($reflexVersionText, '^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)$')
    if (-not $semverMatch.Success) {
        Write-Host "::error file=.github/integration-packages.json::Reflex pin '$reflexVersionText' must be a plain MAJOR.MINOR.PATCH semantic version so Unity 6000.5 compatibility can be compared."
        $failed = $true
    } else {
        $reflexVersion = [version]::new(
            [int]$semverMatch.Groups['major'].Value,
            [int]$semverMatch.Groups['minor'].Value,
            [int]$semverMatch.Groups['patch'].Value
        )
        if ($reflexVersion -lt [version]'14.3.1') {
            Write-Host "::error file=.github/integration-packages.json::Reflex integration pin must stay at 14.3.1 or newer; older pins use non-generic TreeView editor APIs that fail to compile on Unity 6000.5."
            $failed = $true
        } elseif ($VerboseOutput) {
            Write-Info "Checked Reflex integration pin $reflexVersionText is compatible with Unity 6000.5 TreeView API changes."
        }
    }
}

$runnerUsesUnityVersionsConfig = (
    $runnerBootstrapContent.Contains('.github\unity-versions.json') -and
    $runnerBootstrapContent.Contains('ConvertFrom-Json') -and
    $runnerBootstrapContent.Contains('@($unityVersionsConfig.all)') -and
    $runnerBootstrapContent.Contains('Unity versions from .github/unity-versions.json')
)
if (-not $runnerUsesUnityVersionsConfig) {
    Write-Host "::error file=.github/workflows/runner-bootstrap.yml::Runner bootstrap must read .github/unity-versions.json through an array wrapper so self-hosted runner provisioning cannot drift from the Unity test matrix or split one-element arrays incorrectly."
    $failed = $true
} elseif ($runnerBootstrapContent -match "(?s)\`$unityVersions\s*=\s*@\(\s*'\d+\.\d+\.\d+f\d+'") {
    Write-Host "::error file=.github/workflows/runner-bootstrap.yml::Runner bootstrap must not hardcode a Unity version array; update .github/unity-versions.json instead."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked runner bootstrap uses .github/unity-versions.json instead of a hardcoded Unity version array."
}

$ensureEditorUsesNamedSplat = (
    (
        $windowsRunnerMaintenanceContent.Contains('$ensureEditorArgs = @{') -and
        $windowsRunnerMaintenanceContent.Contains('$ensureEditorOutput = @(& $ensureEditorScript @ensureEditorArgs 2>&1)')
    ) -or (
        $windowsRunnerMaintenanceContent.Contains('$ensureEditorArguments = @{') -and
        $windowsRunnerMaintenanceContent.Contains('$ensureEditorOutput = @(& $ensureEditorScript @ensureEditorArguments 2>&1)')
    )
)

$runnerBootstrapBackendPresent = (
    $runnerBootstrapContent.Contains('scripts\unity\maintain-windows-runner.ps1') -and
    -not $runnerBootstrapContent.Contains('has not been ported yet') -and
    $windowsRunnerBootstrapContent.Contains('function Invoke-WindowsRunnerBootstrap') -and
    $windowsRunnerBootstrapContent.Contains('VC++ 2010 SP1 x64 redistributable') -and
    $windowsRunnerBootstrapContent.Contains('VC++ 2015-2022 x64 redistributable') -and
    $windowsRunnerBootstrapContent.Contains('PowerShell 7') -and
    $windowsRunnerBootstrapContent.Contains('Assert-RunnerMicrosoftAuthenticodeSignature') -and
    $windowsRunnerBootstrapContent.Contains('$script:VcRedist2010X64Sha256') -and
    $windowsRunnerBootstrapContent.Contains('unity-runner-bootstrap-installers') -and
    $windowsRunnerBootstrapContent.Contains('function Test-RunnerPowerShell7Present') -and
    $windowsRunnerBootstrapContent.Contains('function Test-RunnerWindowsAppsPowerShellAliasPath') -and
    $windowsRunnerBootstrapContent.Contains('function Test-RunnerPowerShell7ExecutablePath') -and
    $windowsRunnerBootstrapContent.Contains('\Microsoft\WindowsApps\pwsh.exe') -and
    -not $windowsRunnerBootstrapContent.Contains("if (Test-RunnerCommandExists -Name 'pwsh')") -and
    $windowsRunnerBootstrapContent.Contains("[Alias('DetectOnly')]") -and
    $windowsRunnerBootstrapContent.Contains('$RunnerBootstrapDetectOnly') -and
    $windowsRunnerBootstrapContent.Contains('$wingetOutput = @(& winget @arguments 2>&1)') -and
    $windowsRunnerBootstrapContent.Contains('$wingetExitCode = $LASTEXITCODE') -and
    $windowsRunnerMaintenanceContent.Contains('function Invoke-WindowsRunnerMaintenance') -and
    $windowsRunnerMaintenanceContent.Contains('ensure-editor.ps1') -and
    $windowsRunnerMaintenanceContent.Contains('RequireHealthyExisting') -and
    $windowsRunnerMaintenanceContent.Contains("[Alias('DetectOnly')]") -and
    $windowsRunnerMaintenanceContent.Contains('$RunnerMaintenanceDetectOnly') -and
    $windowsRunnerMaintenanceContent.Contains('$maintenanceDetectOnly = Resolve-RunnerMaintenanceDetectOnly -DetectOnly ([bool]$DetectOnly)') -and
    $windowsRunnerMaintenanceContent.Contains('$bootstrapOutput = @(Invoke-WindowsRunnerBootstrap') -and
    $ensureEditorUsesNamedSplat -and
    $windowsRunnerMaintenanceContent.Contains('UnityVersion') -and
    $windowsRunnerMaintenanceContent.Contains('CiManagedOnly') -and
    $windowsRunnerMaintenanceContent.Contains('RequireHealthyExisting = $true') -and
    -not $windowsRunnerMaintenanceContent.Contains('$ensureEditorOutput = @(& $ensureEditorScript @arguments 2>&1)')
)
if (-not $runnerBootstrapBackendPresent) {
    Write-Host "::error file=.github/workflows/runner-bootstrap.yml::Runner bootstrap must have a real scripts/unity Windows maintenance backend that audits host prerequisites, verifies Microsoft installers before execution, keeps installers out of uploaded artifacts, preserves detect-only flags across script loading, captures child success streams before returning scalar exit codes, and verifies Unity editors with ensure-editor.ps1."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked runner bootstrap Windows maintenance backend contract."
}

$runnerBootstrapDocsCurrent = (
    $runnerRunbookContent.Contains('.github/workflows/runner-bootstrap.yml') -and
    $runnerRunbookContent.Contains('scripts/unity/bootstrap-windows-runner.ps1') -and
    $runnerRunbookContent.Contains('scripts/unity/maintain-windows-runner.ps1') -and
    $runnerRunbookContent.Contains('workflow_dispatch') -and
    $runnerRunbookContent.Contains('DAD-MACHINE') -and
    $runnerRunbookContent.Contains('ELI-MACHINE') -and
    $runnerDiagnosticsActionContent.Contains('runner-bootstrap.yml') -and
    $runnerDiagnosticsActionContent.Contains('ensure-editor.ps1') -and
    -not $runnerRunbookContent.Contains('was **not** ported') -and
    -not $runnerRunbookContent.Contains('When the backend scripts are ported') -and
    -not $runnerDiagnosticsActionContent.Contains('were NOT ported')
)
if (-not $runnerBootstrapDocsCurrent) {
    Write-Host "::error file=docs/runbooks/unity-runners-after-transfer.md::.github/workflows/runner-bootstrap.yml and the self-hosted diagnostics action comments must describe the current Windows maintenance backend, not stale manual-only TODO text."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked runner bootstrap runbook and diagnostics comments describe the current maintenance backend."
}

$runnerBootstrapInvokesMaintenanceFunction = (
    $runnerBootstrapContent.Contains('. $script') -and
    $runnerBootstrapContent.Contains('$maintenanceArgs = @{') -and
    $runnerBootstrapContent.Contains('UnityVersions = $unityVersions') -and
    $runnerBootstrapContent.Contains('$maintenanceArgs.DetectOnly = $true') -and
    $runnerBootstrapContent.Contains('$code = Invoke-WindowsRunnerMaintenance @maintenanceArgs') -and
    -not $runnerBootstrapContent.Contains('& $script @maintenanceArgs') -and
    -not $runnerBootstrapContent.Contains('$code = $LASTEXITCODE')
)
if (-not $runnerBootstrapInvokesMaintenanceFunction) {
    Write-Host "::error file=.github/workflows/runner-bootstrap.yml::Runner bootstrap workflow must dot-source maintain-windows-runner.ps1 and call Invoke-WindowsRunnerMaintenance so the script's top-level exit cannot skip transcript cleanup or summary reporting."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked runner bootstrap calls maintenance function without losing cleanup control."
}

$runnerMaintenanceForceParameters = @(
    @($runnerMaintenanceScriptParameters + $runnerMaintenanceFunctionParameters) |
        Where-Object {
            $parameterName = $_.Name.VariablePath.UserPath
            $hasForceSurface = $parameterName -match '(?i)Force'

            if (-not $hasForceSurface) {
                foreach ($attribute in @($_.Attributes)) {
                    $attributeTypeName = [string]$attribute.TypeName.FullName
                    if ($attributeTypeName -notmatch '(?i)(^|\.)(Alias|AliasAttribute)$') {
                        continue
                    }

                    foreach ($argument in @($attribute.PositionalArguments)) {
                        if ($argument -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
                            [string]::Equals($argument.Value, 'Force', [System.StringComparison]::OrdinalIgnoreCase)) {
                            $hasForceSurface = $true
                            break
                        }
                    }
                }
            }

            $hasForceSurface
        }
)
$runnerBootstrapPassesForceToMaintenance = Test-RunnerBootstrapPassesMaintenanceForce -Content $runnerBootstrapContent
$runnerMaintenanceHasNoDeadForceSurface = (
    $runnerMaintenanceForceParameters.Count -eq 0 -and
    -not $runnerBootstrapPassesForceToMaintenance
)
if (-not $runnerMaintenanceHasNoDeadForceSurface) {
    Write-Host "::error file=scripts/unity/maintain-windows-runner.ps1::Runner maintenance must not expose or pass a Force switch unless it changes provisioning behavior. Remove the dead Force surface to avoid misleading operators."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked runner maintenance exposes no dead Force switch."
}

$runCiTestsClearsStaleCompilationCache = (
    $runCiTestsContent.Contains('function Clear-StaleUnityCompilationCache') -and
    $runCiTestsContent.Contains('function Test-UnityCompilationCacheRepoRootMatch') -and
    $runCiTestsContent.Contains('[System.StringComparison]::OrdinalIgnoreCase') -and
    $runCiTestsContent.Contains('.unity-helpers-repo-root.txt') -and
    $runCiTestsContent.Contains('Clear-StaleUnityCompilationCache -Project $ProjectPath -RepoRoot $RepoRoot') -and
    $runCiTestsContent.Contains("'Bee'") -and
    $runCiTestsContent.Contains("'ScriptAssemblies'") -and
    $runCiTestsContent.Contains("'PlayerScriptAssemblies'") -and
    $runCiTestsContent.Contains("'Il2cppBuildCache'") -and
    $runCiTestsContent.Contains('Set-Content -LiteralPath $markerPath -Value $currentRepoRoot')
)
if (-not $runCiTestsClearsStaleCompilationCache) {
    Write-Host "::error file=scripts/unity/run-ci-tests.ps1::Unity CI must clear restored compilation caches when the cached Library was produced under a different repo root, otherwise Bee can reuse stale absolute precompiled-reference paths from another runner drive."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity CI clears stale compilation caches when the restored Library repo-root marker differs."
}

try {
    foreach ($runCiTestsFunctionName in @(
            'Get-UnityCompilationCacheRepoRootComparison',
            'Test-UnityCompilationCacheRepoRootMatch',
            'Clear-StaleUnityCompilationCache'
        )) {
        Import-RunCiTestsFunction -ScriptPath $runCiTestsPath -FunctionName $runCiTestsFunctionName
    }
} catch {
    Write-Host "::error file=scripts/unity/run-ci-tests.ps1::Could not import Clear-StaleUnityCompilationCache for behavioral tests: $($_.Exception.Message)"
    $failed = $true
}

$compilationCacheDirectories = @(
    'Bee',
    'ScriptAssemblies',
    'PlayerScriptAssemblies',
    'Il2cppBuildCache'
)

function New-UnityCompilationCacheFixture {
    param([string]$MarkerValue)

    $root = Join-Path ([System.IO.Path]::GetTempPath()) "unity-cache-contract-$PID-$(Get-Random)"
    $project = Join-Path $root 'project'
    $library = Join-Path $project 'Library'
    New-Item -ItemType Directory -Force -Path $library | Out-Null

    $sentinels = @{}
    foreach ($directory in $script:compilationCacheDirectories) {
        $path = Join-Path $library $directory
        New-Item -ItemType Directory -Force -Path $path | Out-Null
        $sentinel = Join-Path $path 'sentinel.txt'
        Set-Content -LiteralPath $sentinel -Value $directory -Encoding utf8
        $sentinels[$directory] = $sentinel
    }

    $packageCache = Join-Path $library 'PackageCache'
    New-Item -ItemType Directory -Force -Path $packageCache | Out-Null
    $packageCacheSentinel = Join-Path $packageCache 'sentinel.txt'
    Set-Content -LiteralPath $packageCacheSentinel -Value 'package-cache' -Encoding utf8

    $markerPath = Join-Path $library '.unity-helpers-repo-root.txt'
    if ($PSBoundParameters.ContainsKey('MarkerValue')) {
        Set-Content -LiteralPath $markerPath -Value $MarkerValue -Encoding utf8
    }

    [pscustomobject]@{
        Root = $root
        Project = $project
        Library = $library
        MarkerPath = $markerPath
        Sentinels = $sentinels
        PackageCacheSentinel = $packageCacheSentinel
    }
}

function Get-NormalizedContractRoot {
    param([Parameter(Mandatory = $true)][string]$Path)

    [System.IO.Path]::GetFullPath($Path).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    )
}

function Test-CompilationCacheDirsAbsent {
    param([Parameter(Mandatory = $true)]$Fixture)

    foreach ($directory in $script:compilationCacheDirectories) {
        $path = Join-Path $Fixture.Library $directory
        if (Test-Path -LiteralPath $path) {
            return $false
        }
    }

    return $true
}

function Test-CompilationCacheSentinelsPresent {
    param([Parameter(Mandatory = $true)]$Fixture)

    foreach ($directory in $script:compilationCacheDirectories) {
        if (-not (Test-Path -LiteralPath $Fixture.Sentinels[$directory] -PathType Leaf)) {
            return $false
        }
    }

    return $true
}

function Test-UnityCompilationCacheBehavior {
    $fixtures = @()
    $matchingMarkerRoot = ''
    try {
        if (-not (Test-UnityCompilationCacheRepoRootMatch `
                    -PreviousRepoRoot 'C:\Actions\_work\UnityHelpers' `
                    -CurrentRepoRoot 'c:\actions\_WORK\unityhelpers' `
                    -Comparison ([System.StringComparison]::OrdinalIgnoreCase))) {
            return 'Windows-style casing-only repo-root marker drift must not invalidate compilation caches'
        }
        if (Test-UnityCompilationCacheRepoRootMatch `
                -PreviousRepoRoot 'C:\Actions\_work\UnityHelpers' `
                -CurrentRepoRoot 'D:\Actions\_work\UnityHelpers' `
                -Comparison ([System.StringComparison]::OrdinalIgnoreCase)) {
            return 'different repo roots must still invalidate compilation caches under the Windows comparison'
        }

        $missingMarkerFixture = New-UnityCompilationCacheFixture
        $fixtures += $missingMarkerFixture
        $missingMarkerRepoRoot = Join-Path $missingMarkerFixture.Root 'repo'
        New-Item -ItemType Directory -Force -Path $missingMarkerRepoRoot | Out-Null
        Clear-StaleUnityCompilationCache -Project $missingMarkerFixture.Project -RepoRoot $missingMarkerRepoRoot
        $missingMarkerValue = if (Test-Path -LiteralPath $missingMarkerFixture.MarkerPath -PathType Leaf) {
            (Get-Content -LiteralPath $missingMarkerFixture.MarkerPath -Raw).Trim()
        } else {
            ''
        }
        $missingMarkerExpectedRoot = Get-NormalizedContractRoot -Path $missingMarkerRepoRoot
        if (-not (Test-CompilationCacheDirsAbsent -Fixture $missingMarkerFixture) -or
            -not (Test-Path -LiteralPath $missingMarkerFixture.PackageCacheSentinel -PathType Leaf) -or
            $missingMarkerValue -cne $missingMarkerExpectedRoot) {
            return 'missing marker must clear compilation outputs, preserve PackageCache, and write the normalized repo-root marker'
        }

        $changedMarkerFixture = New-UnityCompilationCacheFixture -MarkerValue 'E:/actions-runner/_work/unity-helpers/unity-helpers'
        $fixtures += $changedMarkerFixture
        $changedMarkerRepoRoot = Join-Path $changedMarkerFixture.Root 'repo'
        New-Item -ItemType Directory -Force -Path $changedMarkerRepoRoot | Out-Null
        Clear-StaleUnityCompilationCache -Project $changedMarkerFixture.Project -RepoRoot $changedMarkerRepoRoot
        $changedMarkerValue = (Get-Content -LiteralPath $changedMarkerFixture.MarkerPath -Raw).Trim()
        $changedMarkerExpectedRoot = Get-NormalizedContractRoot -Path $changedMarkerRepoRoot
        if (-not (Test-CompilationCacheDirsAbsent -Fixture $changedMarkerFixture) -or
            -not (Test-Path -LiteralPath $changedMarkerFixture.PackageCacheSentinel -PathType Leaf) -or
            $changedMarkerValue -cne $changedMarkerExpectedRoot) {
            return 'changed marker must clear compilation outputs, preserve PackageCache, and rewrite the normalized repo-root marker'
        }

        $matchingMarkerRoot = Join-Path ([System.IO.Path]::GetTempPath()) "unity-cache-contract-root-$PID-$(Get-Random)"
        New-Item -ItemType Directory -Force -Path $matchingMarkerRoot | Out-Null
        $matchingMarkerValue = Get-NormalizedContractRoot -Path $matchingMarkerRoot
        $matchingMarkerFixture = New-UnityCompilationCacheFixture -MarkerValue $matchingMarkerValue
        $fixtures += $matchingMarkerFixture
        Clear-StaleUnityCompilationCache -Project $matchingMarkerFixture.Project -RepoRoot $matchingMarkerRoot
        $matchingMarkerAfter = (Get-Content -LiteralPath $matchingMarkerFixture.MarkerPath -Raw).Trim()
        if (-not (Test-CompilationCacheSentinelsPresent -Fixture $matchingMarkerFixture) -or
            -not (Test-Path -LiteralPath $matchingMarkerFixture.PackageCacheSentinel -PathType Leaf) -or
            $matchingMarkerAfter -cne $matchingMarkerValue) {
            return 'matching marker must preserve compilation-cache sentinels, PackageCache, and marker contents'
        }

        return ''
    } finally {
        foreach ($fixture in $fixtures) {
            if ($fixture.Root -and (Test-Path -LiteralPath $fixture.Root)) {
                Remove-Item -LiteralPath $fixture.Root -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        if ($matchingMarkerRoot -and (Test-Path -LiteralPath $matchingMarkerRoot)) {
            Remove-Item -LiteralPath $matchingMarkerRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

$compilationCacheBehaviorFailure = Test-UnityCompilationCacheBehavior
if ($compilationCacheBehaviorFailure) {
    Write-Host "::error file=scripts/unity/run-ci-tests.ps1::Clear-StaleUnityCompilationCache behavior regression: $compilationCacheBehaviorFailure."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked stale Unity compilation cache cleanup behavior with no-Unity temp fixtures."
}

$maintenanceForceDetectorFixtures = @(
    @{
        Name = 'initial hashtable bare key'
        Content = '$maintenanceArgs = @{ Force = $true }'
        Expected = $true
    },
    @{
        Name = 'initial hashtable quoted key'
        Content = '$maintenanceArgs = @{ ''Force'' = $true }'
        Expected = $true
    },
    @{
        Name = 'initial hashtable parenthesized string key'
        Content = '$maintenanceArgs = @{ (''Force'') = $true }'
        Expected = $true
    },
    @{
        Name = 'initial hashtable cast string key'
        Content = '$maintenanceArgs = @{ ([string]''Force'') = $true }'
        Expected = $true
    },
    @{
        Name = 'initial hashtable unparenthesized cast key'
        Content = '$maintenanceArgs = @{ [string]"Force" = $true }'
        Expected = $true
    },
    @{
        Name = 'merged hashtable bare key'
        Content = '$maintenanceArgs += @{ Force = $true }'
        Expected = $true
    },
    @{
        Name = 'merged hashtable quoted key'
        Content = '$maintenanceArgs += @{ "Force" = $true }'
        Expected = $true
    },
    @{
        Name = 'typed hashtable bare key'
        Content = '$maintenanceArgs = [hashtable]@{ Force = $true }'
        Expected = $true
    },
    @{
        Name = 'ordered hashtable bare key'
        Content = '$maintenanceArgs = [ordered]@{ Force = $true }'
        Expected = $true
    },
    @{
        Name = 'same-line merge after previous statement'
        Content = '$maintenanceArgs = @{ DetectOnly = $true }; $maintenanceArgs += @{ Force = $true }'
        Expected = $true
    },
    @{
        Name = 'same-line merge inside conditional block'
        Content = 'if ($true) { $maintenanceArgs += @{ Force = $true } }'
        Expected = $true
    },
    @{
        Name = 'dot assignment'
        Content = '$maintenanceArgs.Force = $true'
        Expected = $true
    },
    @{
        Name = 'indexer assignment'
        Content = '$maintenanceArgs["Force"] = $true'
        Expected = $true
    },
    @{
        Name = 'parenthesized indexer assignment'
        Content = '$maintenanceArgs[("Force")] = $true'
        Expected = $true
    },
    @{
        Name = 'cast indexer assignment'
        Content = '$maintenanceArgs[[string]"Force"] = $true'
        Expected = $true
    },
    @{
        Name = 'Item property assignment'
        Content = '$maintenanceArgs.Item("Force") = $true'
        Expected = $true
    },
    @{
        Name = 'Add method'
        Content = '$maintenanceArgs.Add("Force", $true)'
        Expected = $true
    },
    @{
        Name = 'parenthesized Add method argument'
        Content = '$maintenanceArgs.Add(("Force"), $true)'
        Expected = $true
    },
    @{
        Name = 'cast Add method argument'
        Content = '$maintenanceArgs.Add([string]"Force", $true)'
        Expected = $true
    },
    @{
        Name = 'Set_Item method'
        Content = '$maintenanceArgs.Set_Item("Force", $true)'
        Expected = $true
    },
    @{
        Name = 'cast Set_Item method argument'
        Content = '$maintenanceArgs.Set_Item(([string]"Force"), $true)'
        Expected = $true
    },
    @{
        Name = 'unparenthesized cast Set_Item method argument'
        Content = '$maintenanceArgs.Set_Item([string]"Force", $true)'
        Expected = $true
    },
    @{
        Name = 'method call inside assignment'
        Content = '$null = $maintenanceArgs.Add("Force", $true)'
        Expected = $true
    },
    @{
        Name = 'safe detect-only pass-through'
        Content = '$maintenanceArgs = @{ DetectOnly = $true }'
        Expected = $false
    }
)
foreach ($fixture in $maintenanceForceDetectorFixtures) {
    $actual = Test-RunnerBootstrapPassesMaintenanceForce -Content $fixture.Content
    if ($actual -ne $fixture.Expected) {
        Write-Host "::error file=scripts/tests/test-unity-workflow-matrix-contract.ps1::Runner maintenance Force detector fixture '$($fixture.Name)' expected $($fixture.Expected) but got $actual."
        $failed = $true
    }
}
if ($VerboseOutput) {
    Write-Info "Checked runner maintenance Force pass-through detector fixtures."
}

$runnerPreflightJob = if ($runnerBootstrapJobTexts.ContainsKey('runner-preflight')) { $runnerBootstrapJobTexts['runner-preflight'] } else { '' }
$bootstrapJob = if ($runnerBootstrapJobTexts.ContainsKey('bootstrap')) { $runnerBootstrapJobTexts['bootstrap'] } else { '' }
$bootstrapRunsOnPattern = '(?m)^\s+runs-on:\s*\[self-hosted,\s*Windows,\s*RAM-64GB,\s*"\$\{\{\s*inputs\.runner-label\s*\}\}"\]\s*$'
$runnerPreflightAction = "Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/check-unity-runner-availability@$runnerAvailabilityActionCommit"
$readerAppCredentialsPattern = '(?ms)reader-app-id:\s*\$\{\{\s*secrets\.BUILD_LOCK_READER_APP_ID\s*\}\}.*reader-app-private-key:\s*\$\{\{\s*secrets\.BUILD_LOCK_READER_APP_PRIVATE_KEY\s*\}\}'
$runnerBootstrapPinsRequestedMachine = (
    $runnerBootstrapJobTexts.ContainsKey('runner-preflight') -and
    $runnerBootstrapJobTexts.ContainsKey('bootstrap') -and
    $runnerPreflightJob.Contains("uses: $runnerPreflightAction # $runnerAvailabilityActionVersion") -and
    $runnerPreflightJob -match $readerAppCredentialsPattern -and
    $runnerPreflightJob.Contains('required-label-sets: ''[["self-hosted","Windows","RAM-64GB","${{ inputs.runner-label }}"]]''') -and
    $bootstrapJob -match $bootstrapRunsOnPattern -and
    $bootstrapJob.Contains('custom ''$requested'' label') -and
    $actionlintContent.Contains('- DAD-MACHINE') -and
    $actionlintContent.Contains('- ELI-MACHINE') -and
    -not $runnerBootstrapContent.Contains('take the unwanted runner offline') -and
    -not $runnerBootstrapContent.Contains('take ``$actual`` offline')
)
if (-not $runnerBootstrapPinsRequestedMachine) {
    Write-Host "::error file=.github/workflows/runner-bootstrap.yml::Runner bootstrap must include the selected machine-name label in runs-on and preflight labels so operator-dispatched maintenance cannot silently run on the wrong self-hosted runner."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked runner bootstrap pins the requested machine with a machine-name label."
}

$unityTestsRunnerPreflightJob = if ($jobTexts.ContainsKey('runner-preflight')) { $jobTexts['runner-preflight'] } else { '' }
$benchmarksRunnerPreflightJob = if ($benchmarksJobTexts.ContainsKey('runner-preflight')) { $benchmarksJobTexts['runner-preflight'] } else { '' }
$unityWorkflowRunnerPreflightsFailClosed = (
    $jobTexts.ContainsKey('runner-preflight') -and
    $benchmarksJobTexts.ContainsKey('runner-preflight') -and
    $unityTestsRunnerPreflightJob.Contains("uses: $runnerPreflightAction # $runnerAvailabilityActionVersion") -and
    $benchmarksRunnerPreflightJob.Contains("uses: $runnerPreflightAction # $runnerAvailabilityActionVersion") -and
    $unityTestsRunnerPreflightJob -match $readerAppCredentialsPattern -and
    $benchmarksRunnerPreflightJob -match $readerAppCredentialsPattern -and
    $unityTestsRunnerPreflightJob.Contains('required-label-sets: ''[["self-hosted","Windows","RAM-64GB"]]''') -and
    $benchmarksRunnerPreflightJob.Contains('required-label-sets: ''[["self-hosted","Windows","RAM-64GB"]]''') -and
    -not $workflowContent.Contains('RUNNER_AUDIT_PAT') -and
    -not $benchmarksWorkflowContent.Contains('RUNNER_AUDIT_PAT') -and
    -not $runnerBootstrapContent.Contains('RUNNER_AUDIT_PAT') -and
    -not $workflowContent.Contains('Soft pass: skipping runner inventory check.') -and
    -not $benchmarksWorkflowContent.Contains('Soft pass: skipping runner inventory check.') -and
    -not $runnerBootstrapContent.Contains('Soft pass: skipping runner inventory check.')
)
if (-not $unityWorkflowRunnerPreflightsFailClosed) {
    Write-Host "::error file=.github/workflows/unity-tests.yml::Every self-hosted Unity runner preflight must use the pinned reader-App action, request the exact runs-on labels, and fail closed without PAT or soft-pass fallbacks."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity workflow runner preflights use the fail-closed reader-App action."
}

$unityCiSuccessJob = if ($jobTexts.ContainsKey('unity-ci-success')) { $jobTexts['unity-ci-success'] } else { '' }
$unityCiSuccessContract = (
    $jobTexts.ContainsKey('unity-ci-success') -and
    $unityCiSuccessJob -match '(?m)^\s+name:\s*Unity CI Success\s*$' -and
    $unityCiSuccessJob -match '(?m)^\s+if:\s*\$\{\{\s*always\(\)\s*\}\}\s*$' -and
    $unityCiSuccessJob.Contains('needs.runner-preflight.result') -and
    $unityCiSuccessJob.Contains('needs.unity-tests.result') -and
    $unityCiSuccessJob.Contains('needs.unity-tests-standalone.result') -and
    $unityCiSuccessJob.Contains('needs.unity-tests-single-threaded.result') -and
    $unityCiSuccessJob.Contains('needs.unitypackage-smoke.result') -and
    $unityCiSuccessJob.Contains('Unexpected Unity CI job result')
)
if (-not $unityCiSuccessContract) {
    Write-Host "::error file=.github/workflows/unity-tests.yml::Unity CI must end in an always-reporting Unity CI Success job that rejects runner-preflight failures and unexpected skipped licensed jobs."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity CI has an always-reporting fail-closed aggregate job."
}

function Get-UnityWorkflowStepText {
    param(
        [Parameter(Mandatory = $true)][string]$JobText,
        [Parameter(Mandatory = $true)][string]$StepName
    )

    $stepIndex = $JobText.IndexOf("- name: $StepName")
    if ($stepIndex -lt 0) {
        return ''
    }

    $remainingJobText = $JobText.Substring($stepIndex + 1)
    $nextStepMatch = [regex]::Match($remainingJobText, '(?m)^ {6}- name:\s+')
    $stepEndIndex = if ($nextStepMatch.Success) {
        $stepIndex + 1 + $nextStepMatch.Index
    } else {
        $JobText.Length
    }

    return $JobText.Substring($stepIndex, $stepEndIndex - $stepIndex)
}

function Test-UnityWorkflowStepHasEmptyAssemblyGate {
    param(
        [Parameter(Mandatory = $true)][string]$JobText,
        [Parameter(Mandatory = $true)][string]$StepName
    )

    $stepText = Get-UnityWorkflowStepText -JobText $JobText -StepName $StepName
    return $stepText -match 'if:\s*\$\{\{\s*steps\.compute\.outputs\.is-empty\s*!=\s*''true''\s*\}\}'
}

$computeUnityAssembliesActionPath = Join-Path $repoRoot '.github/actions/compute-unity-assemblies/action.yml'
$computeUnityAssembliesActionContent = Get-Content -Path $computeUnityAssembliesActionPath -Raw
$computeUnityAssembliesActionUsesBootstrapSafeShell = (
    $computeUnityAssembliesActionContent -match '(?m)^\s*shell:\s*powershell\s*$' -and
    -not ($computeUnityAssembliesActionContent -match '(?m)^\s*shell:\s*pwsh\s*$')
)
if (-not $computeUnityAssembliesActionUsesBootstrapSafeShell) {
    Write-Host "::error file=.github/actions/compute-unity-assemblies/action.yml::The compute-unity-assemblies action must use Windows PowerShell, not pwsh, so Unity jobs can skip empty matrix legs before runner maintenance installs or repairs PowerShell 7."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked compute-unity-assemblies can run before runner maintenance bootstraps PowerShell 7."
}

function Test-UnityJobMaintainsSelectedRunner {
    param([Parameter(Mandatory = $true)][string]$JobText)

    $maintenanceIndex = $JobText.IndexOf('- name: Maintain Unity editor on selected runner')
    $provisionIndex = $JobText.IndexOf('- name: Provision Unity Editor')
    $firstPwshShellIndex = $JobText.IndexOf('shell: pwsh')
    $runnerDiagnosticsIndex = $JobText.IndexOf('- name: Print runner diagnostics')
    $setupNodeIndex = $JobText.IndexOf('- name: Setup Node.js')
    $computeIndex = $JobText.IndexOf('- name: Compute')
    $licenseValidationIndex = $JobText.IndexOf('- name: Validate Unity license secrets')
    $maintenanceUsesWindowsPowerShell = $JobText -match '(?s)- name: Maintain Unity editor on selected runner.*?shell:\s*powershell'
    $programFilesPwshIndex = $JobText.IndexOf('PowerShell\7\pwsh.exe')
    $getCommandPwshIndex = $JobText.IndexOf('Get-Command pwsh')
    $maintenancePublishesPowerShell7Path = (
        $JobText -match '(?s)- name: Maintain Unity editor on selected runner.*?\$env:GITHUB_PATH' -and
        $JobText.Contains('PowerShell\7\pwsh.exe') -and
        $JobText.Contains('pwsh.exe was not found for later GitHub Actions steps')
    )
    $maintenancePrefersRealPowerShell7Install = (
        $programFilesPwshIndex -ge 0 -and
        $getCommandPwshIndex -ge 0 -and
        $programFilesPwshIndex -lt $getCommandPwshIndex -and
        $JobText.Contains('$pathPwsh -and $pathPwsh -notlike ''*\Microsoft\WindowsApps\pwsh.exe''') -and
        $JobText.Contains('Select-Object -Unique') -and
        -not $JobText.Contains('Join-Path $env:LocalAppData ''Microsoft\WindowsApps\pwsh.exe''')
    )
    $setupNodeAndAssemblyComputeRunBeforeMaintenance = (
        $setupNodeIndex -ge 0 -and
        $computeIndex -ge 0 -and
        $setupNodeIndex -lt $computeIndex -and
        $computeIndex -lt $maintenanceIndex
    )
    $unityExpensiveStepsSkipEmptyAssemblyLegs = (
        (Test-UnityWorkflowStepHasEmptyAssemblyGate -JobText $JobText -StepName 'Maintain Unity editor on selected runner') -and
        (Test-UnityWorkflowStepHasEmptyAssemblyGate -JobText $JobText -StepName 'Print runner diagnostics') -and
        (Test-UnityWorkflowStepHasEmptyAssemblyGate -JobText $JobText -StepName 'Validate Unity license secrets') -and
        (Test-UnityWorkflowStepHasEmptyAssemblyGate -JobText $JobText -StepName 'Provision Unity Editor') -and
        (Test-UnityWorkflowStepHasEmptyAssemblyGate -JobText $JobText -StepName 'Acquire organization Unity lock') -and
        (Test-UnityWorkflowStepHasEmptyAssemblyGate -JobText $JobText -StepName 'Run Unity Test Runner')
    )
    $jobTimeoutCoversMaintenanceBudget = $JobText -match '(?m)^\s+timeout-minutes:\s*1200\s*$'
    $maintenanceStepEndCandidates = @(
        $runnerDiagnosticsIndex,
        $licenseValidationIndex,
        $provisionIndex
    ) | Where-Object { $_ -gt $maintenanceIndex } | Sort-Object
    $maintenanceStepText = ''
    if ($maintenanceIndex -ge 0 -and $maintenanceStepEndCandidates.Count -gt 0) {
        $maintenanceStepEndIndex = $maintenanceStepEndCandidates[0]
        $maintenanceStepText = $JobText.Substring(
            $maintenanceIndex,
            $maintenanceStepEndIndex - $maintenanceIndex
        )
    }
    $maintenanceStepAllowsRepair = (
        $maintenanceStepText.Contains('scripts\unity\maintain-windows-runner.ps1') -and
        -not $maintenanceStepText.Contains('-RequireHealthyExisting') -and
        -not $maintenanceStepText.Contains('-RequireHealthyExistingEditors')
    )
    $maintenanceInvokesFunctionAfterDotSource = (
        $maintenanceStepText.Contains('$maintenanceScript = Join-Path $env:GITHUB_WORKSPACE ''scripts\unity\maintain-windows-runner.ps1''') -and
        $maintenanceStepText.Contains('. $maintenanceScript') -and
        $maintenanceStepText.Contains('$maintenanceExitCode = Invoke-WindowsRunnerMaintenance') -and
        $maintenanceStepText.Contains('if ($maintenanceExitCode -ne 0)') -and
        -not ($maintenanceStepText -match '(?m)^\s+\.\\scripts\\unity\\maintain-windows-runner\.ps1\s*`')
    )

    return (
        $maintenanceIndex -ge 0 -and
        $provisionIndex -ge 0 -and
        $maintenanceIndex -lt $provisionIndex -and
        $setupNodeAndAssemblyComputeRunBeforeMaintenance -and
        $unityExpensiveStepsSkipEmptyAssemblyLegs -and
        ($firstPwshShellIndex -lt 0 -or $maintenanceIndex -lt $firstPwshShellIndex) -and
        ($runnerDiagnosticsIndex -lt 0 -or $maintenanceIndex -lt $runnerDiagnosticsIndex) -and
        ($licenseValidationIndex -lt 0 -or $maintenanceIndex -lt $licenseValidationIndex) -and
        $maintenanceUsesWindowsPowerShell -and
        $maintenancePublishesPowerShell7Path -and
        $maintenancePrefersRealPowerShell7Install -and
        $jobTimeoutCoversMaintenanceBudget -and
        $maintenanceInvokesFunctionAfterDotSource -and
        $JobText.Contains('-UnityVersions ''${{ matrix.unity-version }}''') -and
        $JobText.Contains('-ProvisioningProfile $provisioningProfile') -and
        $maintenanceStepAllowsRepair -and
        $JobText.Contains('provisioning/runner-maintenance') -and
        -not $JobText.Contains('- runner-maintenance') -and
        -not $JobText.Contains('needs.runner-maintenance.result')
    )
}

$unityTestsMatrixJob = if ($jobTexts.ContainsKey('unity-tests')) { $jobTexts['unity-tests'] } else { '' }
$unityTestsStandaloneJob = if ($jobTexts.ContainsKey('unity-tests-standalone')) { $jobTexts['unity-tests-standalone'] } else { '' }
$unityTestsSingleThreadedJob = if ($jobTexts.ContainsKey('unity-tests-single-threaded')) { $jobTexts['unity-tests-single-threaded'] } else { '' }
$benchmarksMatrixJob = if ($benchmarksJobTexts.ContainsKey('benchmarks')) { $benchmarksJobTexts['benchmarks'] } else { '' }
$unityWorkflowsMaintainSelectedRunnerBeforeProvisioning = (
    -not $jobTexts.ContainsKey('runner-maintenance') -and
    -not $benchmarksJobTexts.ContainsKey('runner-maintenance') -and
    (Test-UnityJobMaintainsSelectedRunner -JobText $unityTestsMatrixJob) -and
    (Test-UnityJobMaintainsSelectedRunner -JobText $unityTestsStandaloneJob) -and
    (Test-UnityJobMaintainsSelectedRunner -JobText $unityTestsSingleThreadedJob) -and
    (Test-UnityJobMaintainsSelectedRunner -JobText $benchmarksMatrixJob)
)
if (-not $unityWorkflowsMaintainSelectedRunnerBeforeProvisioning) {
    Write-Host "::error file=.github/workflows/unity-tests.yml::Unity workflows must compute the test assembly list before runner maintenance; skip maintenance, diagnostics, license validation, provisioning, lock acquisition, and Unity test execution when the selected leg is empty; and still run scripts/unity/maintain-windows-runner.ps1 inside each non-empty self-hosted Unity job before Provision Unity Editor. Maintenance must use Windows PowerShell, publish the discovered PowerShell 7 directory through GITHUB_PATH, and remain the repair path. Job timeouts must also cover the in-job maintenance/provisioning/lock/test budget. Keep .github/workflows/unity-benchmarks.yml in sync."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity workflows skip empty legs before runner maintenance and maintain editors before provisioning."
}

$timeoutEventsPreserveReason = (
    $ensureEditorContent.Contains('reason         = $Reason') -and
    $ensureEditorContent.Contains('stallSeconds   = $StallSeconds') -and
    $ensureEditorContent.Contains("'no-output-stall'") -and
    $ensureEditorContent.Contains("-Reason `$timeoutReason -StallSeconds `$eventStallSeconds")
)
if (-not $timeoutEventsPreserveReason) {
    Write-Host "::error file=scripts/unity/ensure-editor.ps1::Ensure-editor timeout events must record whether the wrapper killed the Unity CLI for wall-clock timeout or no-output heartbeat stall, including the stall threshold for stall kills."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked ensure-editor timeout events preserve timeout reason."
}

$quarantineMoveUsesDedicatedRetryBudget = (
    $ensureEditorContent.Contains('function Get-EnsureEditorQuarantineMoveRetryAttempts') -and
    $ensureEditorContent.Contains('UH_ENSURE_EDITOR_QUARANTINE_MOVE_RETRY_ATTEMPTS') -and
    $ensureEditorContent.Contains('$quarantineMoveAttempts = Get-EnsureEditorQuarantineMoveRetryAttempts') -and
    $ensureEditorContent.Contains('Invoke-WithRetry -MaxAttempts $quarantineMoveAttempts')
)
if (-not $quarantineMoveUsesDedicatedRetryBudget) {
    Write-Host "::error file=scripts/unity/ensure-editor.ps1::Ensure-editor quarantine moves must use a dedicated retry-attempt budget so delayed Unity uninstaller/indexer/antivirus handles do not exhaust the old hardcoded three-attempt window."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked ensure-editor quarantine moves use the dedicated retry budget."
}

$installAtomicFunctionAst = Get-FunctionAstByName -Ast $ensureEditorAst -Name 'Install-UnityEditorModulesViaAtomicReinstall'
$ensureModulesFunctionAst = Get-FunctionAstByName -Ast $ensureEditorAst -Name 'Ensure-UnityCiModules'
$installAtomicCommands = if ($installAtomicFunctionAst) {
    Get-FunctionCommandNames -FunctionAst $installAtomicFunctionAst
} else {
    @()
}
$ensureModulesCommands = if ($ensureModulesFunctionAst) {
    Get-FunctionCommandNames -FunctionAst $ensureModulesFunctionAst
} else {
    @()
}
$atomicInPlaceInstallIndex = Get-CommandIndex `
    -Commands $installAtomicCommands `
    -Name 'Install-UnityEditorWithCiModules'
$alternateRootFallbackIndex = Get-CommandIndex `
    -Commands $installAtomicCommands `
    -Name 'Install-UnityEditorWithCiModulesInAlternateRoot' `
    -StartIndex ($atomicInPlaceInstallIndex + 1)
$quarantineFallbackIndex = Get-CommandIndex `
    -Commands $installAtomicCommands `
    -Name 'Repair-UnityEditorWithCiModules' `
    -StartIndex ($alternateRootFallbackIndex + 1)
$alternateEditorReuseIndex = Get-CommandIndex `
    -Commands $ensureModulesCommands `
    -Name 'Find-UnityCiAlternateEditorWithCiModules'
$moduleManageabilityProbeIndex = Get-CommandIndex `
    -Commands $ensureModulesCommands `
    -Name 'Test-UnityEditorModuleManageable'
$atomicRouteIndex = Get-CommandIndex `
    -Commands $ensureModulesCommands `
    -Name 'Install-UnityEditorModulesViaAtomicReinstall' `
    -StartIndex ($moduleManageabilityProbeIndex + 1)
$coreModuleRepairIndex = Get-CommandIndex `
    -Commands $ensureModulesCommands `
    -Name 'Repair-UnityEditorWithCiModules'
$ensureEditorPrefersAtomicModuleRepair = (
    $installAtomicFunctionAst -and
    $ensureModulesFunctionAst -and
    $alternateEditorReuseIndex -ge 0 -and
    $moduleManageabilityProbeIndex -ge 0 -and
    $alternateEditorReuseIndex -lt $moduleManageabilityProbeIndex -and
    $atomicRouteIndex -gt $moduleManageabilityProbeIndex -and
    ($coreModuleRepairIndex -lt 0 -or $coreModuleRepairIndex -gt $atomicRouteIndex) -and
    $atomicInPlaceInstallIndex -ge 0 -and
    $alternateRootFallbackIndex -gt $atomicInPlaceInstallIndex -and
    $quarantineFallbackIndex -gt $alternateRootFallbackIndex
)
if (-not $ensureEditorPrefersAtomicModuleRepair) {
    Write-Host "::error file=scripts/unity/ensure-editor.ps1::When an existing Unity editor is missing required CI modules and Unity CLI reports it is not module-manageable, ensure-editor.ps1 must first reuse a healthy alternate-root CI editor if present, then try the atomic in-place 'install -m' repair, then try an alternate-root atomic install, and only then fall back to quarantine. This avoids making a locked editor directory a hard prerequisite for 6000.5 standalone runners."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked ensure-editor prefers healthy alternate-root reuse and alternate-root atomic repair before quarantine fallback."
}

$nativeStartupFunctionAst = Get-FunctionAstByName -Ast $ensureEditorAst -Name 'Ensure-UnityNativeStartupHealthy'
$nativeStartupCommands = if ($nativeStartupFunctionAst) {
    Get-FunctionCommandNames -FunctionAst $nativeStartupFunctionAst
} else {
    @()
}
$nativeStartupRepairIndex = Get-CommandIndex `
    -Commands $nativeStartupCommands `
    -Name 'Repair-UnityEditorWithCiModules'
$nativeStartupPinnedFailureIndex = Get-CommandIndex `
    -Commands $nativeStartupCommands `
    -Name 'Test-UnityAtomicInstallFailureMayBePinnedToExistingEditor' `
    -StartIndex ($nativeStartupRepairIndex + 1)
$nativeStartupAlternateRootIndex = Get-CommandIndex `
    -Commands $nativeStartupCommands `
    -Name 'Install-UnityEditorWithCiModulesInAlternateRoot' `
    -StartIndex ($nativeStartupPinnedFailureIndex + 1)
if (
    -not $nativeStartupFunctionAst -or
    $nativeStartupRepairIndex -lt 0 -or
    $nativeStartupPinnedFailureIndex -le $nativeStartupRepairIndex -or
    $nativeStartupAlternateRootIndex -le $nativeStartupPinnedFailureIndex
) {
    Write-Host "::error file=scripts/unity/ensure-editor.ps1::Native-startup repair must fall back to an alternate CI-managed install root when quarantine/reinstall is blocked by a handle on the existing editor tree."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked native-startup repair uses alternate-root fallback for an existing-editor-pinned quarantine failure."
}

$detectOnly = $true
. $windowsRunnerMaintenancePath
if (-not $detectOnly) {
    Write-Host "::error file=scripts/unity/maintain-windows-runner.ps1::Dot-sourcing maintain-windows-runner.ps1 must not clobber a caller `$detectOnly variable."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked maintenance script dot-source does not clobber caller detect-only variable."
}

$detectOnlyOutput = & pwsh -NoProfile -File $windowsRunnerMaintenancePath -UnityVersions '2022.3.45f1' -DetectOnly 2>&1
$detectOnlyExitCode = $LASTEXITCODE
if ($detectOnlyExitCode -ne 2) {
    Write-Host "::error file=scripts/unity/maintain-windows-runner.ps1::Detect-only maintenance on a non-Windows host must exit 2 before remediation. Exit $detectOnlyExitCode. Output: $($detectOnlyOutput -join ' ')"
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked maintenance detect-only execution returns missing-prerequisite code 2 without remediation."
}

$bootstrapEnvDiagnostics = ''
$bootstrapEnvOutput = @()
$bootstrapEnvExitCode = 1
$oldDisableAutoBootstrap = $env:UH_RUNNER_DISABLE_AUTO_BOOTSTRAP
try {
    $env:UH_RUNNER_DISABLE_AUTO_BOOTSTRAP = '1'
    $bootstrapEnvDiagnostics = Join-Path ([System.IO.Path]::GetTempPath()) "unity-runner-bootstrap-env-$PID-$(Get-Random)"
    $bootstrapEnvOutput = & pwsh -NoProfile -File $windowsRunnerBootstrapPath -DiagnosticsRoot $bootstrapEnvDiagnostics 2>&1
    $bootstrapEnvExitCode = $LASTEXITCODE
} finally {
    if ($oldDisableAutoBootstrap) {
        $env:UH_RUNNER_DISABLE_AUTO_BOOTSTRAP = $oldDisableAutoBootstrap
    } else {
        Remove-Item Env:\UH_RUNNER_DISABLE_AUTO_BOOTSTRAP -ErrorAction SilentlyContinue
    }
    if ($bootstrapEnvDiagnostics -and (Test-Path -LiteralPath $bootstrapEnvDiagnostics -PathType Container)) {
        Remove-Item -LiteralPath $bootstrapEnvDiagnostics -Recurse -Force -ErrorAction SilentlyContinue
    }
}
$bootstrapEnvOutputText = $bootstrapEnvOutput -join ' '
if (
    $bootstrapEnvExitCode -notin @(0, 2) -or
    $bootstrapEnvOutputText -notmatch 'UH_RUNNER_DISABLE_AUTO_BOOTSTRAP=1 -> forcing DetectOnly'
) {
    Write-Host "::error file=scripts/unity/bootstrap-windows-runner.ps1::UH_RUNNER_DISABLE_AUTO_BOOTSTRAP=1 must force direct bootstrap script execution into detect-only mode. Healthy hosts return 0 and hosts missing prerequisites return 2. Exit $bootstrapEnvExitCode. Output: $bootstrapEnvOutputText"
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked direct bootstrap honors UH_RUNNER_DISABLE_AUTO_BOOTSTRAP=1."
}

$healthyBootstrapDetectOnlyScriptPath = ''
$healthyBootstrapDetectOnlyOutput = @()
$healthyBootstrapDetectOnlyExitCode = 1
$oldDisableAutoBootstrap = $env:UH_RUNNER_DISABLE_AUTO_BOOTSTRAP
try {
    $env:UH_RUNNER_DISABLE_AUTO_BOOTSTRAP = '1'
    $healthyBootstrapDetectOnlyScriptPath = Join-Path ([System.IO.Path]::GetTempPath()) "unity-runner-healthy-bootstrap-detect-only-$PID-$(Get-Random).ps1"
    @"
Set-StrictMode -Version Latest
`$ErrorActionPreference = 'Stop'
. '$($windowsRunnerBootstrapPath.Replace("'", "''"))'

function Get-WindowsRunnerPrerequisiteStatus {
    return @(
        [pscustomobject]@{
            Name        = 'Windows host'
            Present     = `$true
            Remediation = 'Run this script on the self-hosted Windows Unity runner.'
        }
    )
}

function Add-RunnerDefenderExclusions {
    param([string]`$UnityInstallRoot)
    throw "Defender exclusions should not run in detect-only mode. Root=`$UnityInstallRoot"
}

`$code = Invoke-WindowsRunnerBootstrap -UnityInstallRoot 'C:\Unity\Editors' -DiagnosticsRoot ''
Write-Output "healthy detect-only code: `$code"
exit `$code
"@ | Set-Content -LiteralPath $healthyBootstrapDetectOnlyScriptPath -Encoding UTF8
    $healthyBootstrapDetectOnlyOutput = & pwsh -NoProfile -File $healthyBootstrapDetectOnlyScriptPath 2>&1
    $healthyBootstrapDetectOnlyExitCode = $LASTEXITCODE
} finally {
    if ($oldDisableAutoBootstrap) {
        $env:UH_RUNNER_DISABLE_AUTO_BOOTSTRAP = $oldDisableAutoBootstrap
    } else {
        Remove-Item Env:\UH_RUNNER_DISABLE_AUTO_BOOTSTRAP -ErrorAction SilentlyContinue
    }
    if ($healthyBootstrapDetectOnlyScriptPath -and (Test-Path -LiteralPath $healthyBootstrapDetectOnlyScriptPath -PathType Leaf)) {
        Remove-Item -LiteralPath $healthyBootstrapDetectOnlyScriptPath -Force -ErrorAction SilentlyContinue
    }
}
if ($healthyBootstrapDetectOnlyExitCode -ne 0 -or (($healthyBootstrapDetectOnlyOutput -join ' ') -notmatch 'healthy detect-only code: 0')) {
    Write-Host "::error file=scripts/unity/bootstrap-windows-runner.ps1::Detect-only bootstrap on a healthy host must return success without mutating Defender exclusions. Exit $healthyBootstrapDetectOnlyExitCode. Output: $($healthyBootstrapDetectOnlyOutput -join ' ')"
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked healthy direct bootstrap detect-only avoids Defender mutation."
}

$windowsAppsPwshProbeScriptPath = ''
$windowsAppsPwshProbeOutput = @()
$windowsAppsPwshProbeExitCode = 1
try {
    $windowsAppsPwshProbeScriptPath = Join-Path ([System.IO.Path]::GetTempPath()) "unity-runner-windowsapps-pwsh-$PID-$(Get-Random).ps1"
    @"
Set-StrictMode -Version Latest
`$ErrorActionPreference = 'Stop'
. '$($windowsRunnerBootstrapPath.Replace("'", "''"))'

`$script:ProgramFilesRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'ProgramFiles'
`$script:LocalAppDataRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'Users/runneradmin/AppData/Local'
`$env:LOCALAPPDATA = `$script:LocalAppDataRoot
`$env:ProgramFiles = `$script:ProgramFilesRoot
`$script:CommandSource = Join-Path `$script:LocalAppDataRoot 'Microsoft/WindowsApps/pwsh.exe'
`$script:ExistingPaths = @(`$script:CommandSource)

function Get-Command {
    param(
        [string]`$Name,
        [object]`$ErrorAction
    )
    if (`$Name -eq 'pwsh') {
        return [pscustomobject]@{ Source = `$script:CommandSource }
    }

    return `$null
}

function Test-Path {
    param(
        [string]`$LiteralPath,
        [object]`$PathType,
        [object]`$ErrorAction
    )
    `$normalizedLiteralPath = `$LiteralPath.Replace('/', '\')
    `$normalizedExistingPaths = @(`$script:ExistingPaths | ForEach-Object { `$_.Replace('/', '\') })
    return `$normalizedExistingPaths -contains `$normalizedLiteralPath
}

if (Test-RunnerPowerShell7Present) {
    Write-Host 'WindowsApps pwsh alias was incorrectly treated as PowerShell 7.'
    exit 7
}

`$programFilesPwshPath = Join-Path `$env:ProgramFiles 'PowerShell\7\pwsh.exe'
`$script:ExistingPaths = @(`$programFilesPwshPath)
if (-not (Test-RunnerPowerShell7Present)) {
    Write-Host 'Real Program Files PowerShell 7 install was not detected after ignoring WindowsApps alias.'
    exit 8
}
"@ | Set-Content -LiteralPath $windowsAppsPwshProbeScriptPath -Encoding UTF8
    $windowsAppsPwshProbeOutput = & pwsh -NoProfile -File $windowsAppsPwshProbeScriptPath 2>&1
    $windowsAppsPwshProbeExitCode = $LASTEXITCODE
} finally {
    if ($windowsAppsPwshProbeScriptPath -and (Test-Path -LiteralPath $windowsAppsPwshProbeScriptPath -PathType Leaf)) {
        Remove-Item -LiteralPath $windowsAppsPwshProbeScriptPath -Force -ErrorAction SilentlyContinue
    }
}
if ($windowsAppsPwshProbeExitCode -ne 0) {
    Write-Host "::error file=scripts/unity/bootstrap-windows-runner.ps1::Windows runner bootstrap must ignore the WindowsApps pwsh.exe app execution alias and keep searching for a real PowerShell 7 install. Exit $windowsAppsPwshProbeExitCode. Output: $($windowsAppsPwshProbeOutput -join ' ')"
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Windows runner bootstrap ignores WindowsApps pwsh alias."
}

$workflowShapeScriptPath = ''
$workflowShapeOutput = @()
$workflowShapeExitCode = 1
try {
    $workflowShapeScriptPath = Join-Path ([System.IO.Path]::GetTempPath()) "unity-runner-workflow-shape-$PID-$(Get-Random).ps1"
    @"
`$script = '$($windowsRunnerMaintenancePath.Replace("'", "''"))'
`$maintenanceArgs = @{
    UnityVersions = @('2022.3.45f1')
    ProvisioningProfile = 'StandaloneWindowsIl2Cpp'
    InstallRoot = 'C:\Unity\Editors'
    DiagnosticsRoot = ''
    DetectOnly = `$true
}
. `$script
`$code = Invoke-WindowsRunnerMaintenance @maintenanceArgs
Write-Output "after-maintenance:`$code"
exit `$code
"@ | Set-Content -LiteralPath $workflowShapeScriptPath -Encoding UTF8
    $workflowShapeOutput = & pwsh -NoProfile -File $workflowShapeScriptPath 2>&1
    $workflowShapeExitCode = $LASTEXITCODE
} finally {
    if ($workflowShapeScriptPath -and (Test-Path -LiteralPath $workflowShapeScriptPath -PathType Leaf)) {
        Remove-Item -LiteralPath $workflowShapeScriptPath -Force -ErrorAction SilentlyContinue
    }
}
if ($workflowShapeExitCode -ne 2 -or (($workflowShapeOutput -join ' ') -notmatch 'after-maintenance:2')) {
    Write-Host "::error file=.github/workflows/runner-bootstrap.yml::Workflow-style hashtable splatting into maintain-windows-runner.ps1 must bind named parameters, return detect-only exit 2 on a non-Windows host, and continue after Invoke-WindowsRunnerMaintenance for cleanup/summary code. Exit $workflowShapeExitCode. Output: $($workflowShapeOutput -join ' ')"
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked workflow-style maintenance function invocation binds named parameters and returns control."
}

$ensureEditorShapeRoot = ''
$ensureEditorShapeOutput = @()
$ensureEditorShapeExitCode = 1
try {
    $ensureEditorShapeRoot = Join-Path ([System.IO.Path]::GetTempPath()) "unity-runner-ensure-shape-$PID-$(Get-Random)"
    New-Item -ItemType Directory -Force -Path $ensureEditorShapeRoot | Out-Null
    Copy-Item -LiteralPath $windowsRunnerMaintenancePath -Destination (Join-Path $ensureEditorShapeRoot 'maintain-windows-runner.ps1') -Force
    @"
function Invoke-WindowsRunnerBootstrap {
    param(
        [switch]`$DetectOnly,
        [string]`$UnityInstallRoot,
        [string]`$DiagnosticsRoot
    )

    return 0
}
"@ | Set-Content -LiteralPath (Join-Path $ensureEditorShapeRoot 'bootstrap-windows-runner.ps1') -Encoding UTF8
    @"
[CmdletBinding()]
param(
    [Parameter(Mandatory = `$true)]
    [ValidatePattern('^\d+\.\d+\.\d+f\d+`$')]
    [string]`$UnityVersion,

    [string]`$InstallRoot,
    [string]`$DiagnosticsPath,
    [switch]`$CiManagedOnly,

    [ValidateSet('EditorOnly', 'StandaloneWindowsIl2Cpp', 'Android', 'Full')]
    [string]`$ProvisioningProfile = 'Full',

    [switch]`$RequireHealthyExisting
)

Set-StrictMode -Version Latest
`$ErrorActionPreference = 'Stop'

if (`$UnityVersion -ne '2022.3.45f1') { throw "Bad UnityVersion: `$UnityVersion" }
if (`$InstallRoot -ne 'C:\Unity\Editors') { throw "Bad InstallRoot: `$InstallRoot" }
if (`$ProvisioningProfile -ne 'StandaloneWindowsIl2Cpp') { throw "Bad ProvisioningProfile: `$ProvisioningProfile" }
if (-not `$CiManagedOnly) { throw 'CiManagedOnly was not bound.' }
if (-not `$RequireHealthyExisting) { throw 'RequireHealthyExisting was not bound.' }
if (`$DiagnosticsPath -notmatch 'unity-2022\.3\.45f1`$') { throw "Bad DiagnosticsPath: `$DiagnosticsPath" }

Write-Output "fake ensure-editor ok: `$UnityVersion"
"@ | Set-Content -LiteralPath (Join-Path $ensureEditorShapeRoot 'ensure-editor.ps1') -Encoding UTF8

    $ensureEditorShapeDiagnostics = Join-Path $ensureEditorShapeRoot 'diagnostics'
    $ensureEditorShapeOutput = & pwsh -NoProfile -File (Join-Path $ensureEditorShapeRoot 'maintain-windows-runner.ps1') `
        -UnityVersions '2022.3.45f1' `
        -ProvisioningProfile 'StandaloneWindowsIl2Cpp' `
        -InstallRoot 'C:\Unity\Editors' `
        -DetectOnly `
        -DiagnosticsRoot $ensureEditorShapeDiagnostics 2>&1
    $ensureEditorShapeExitCode = $LASTEXITCODE
} finally {
    if ($ensureEditorShapeRoot -and (Test-Path -LiteralPath $ensureEditorShapeRoot -PathType Container)) {
        Remove-Item -LiteralPath $ensureEditorShapeRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
if ($ensureEditorShapeExitCode -ne 0 -or (($ensureEditorShapeOutput -join ' ') -notmatch 'fake ensure-editor ok: 2022\.3\.45f1')) {
    Write-Host "::error file=scripts/unity/maintain-windows-runner.ps1::Runner maintenance must pass named parameters to ensure-editor.ps1 so Windows PowerShell 5.1 does not bind '-UnityVersion' as the UnityVersion value. Exit $ensureEditorShapeExitCode. Output: $($ensureEditorShapeOutput -join ' ')"
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked maintenance passes named parameters to ensure-editor."
}

$manualDefaultsRoot = ''
$manualDefaultsOutput = @()
$manualDefaultsExitCode = 1
$oldDisableAutoBootstrap = $env:UH_RUNNER_DISABLE_AUTO_BOOTSTRAP
try {
    $env:UH_RUNNER_DISABLE_AUTO_BOOTSTRAP = '1'
    $manualDefaultsRoot = Join-Path ([System.IO.Path]::GetTempPath()) "unity-runner-manual-defaults-$PID-$(Get-Random)"
    $manualScriptsRoot = Join-Path $manualDefaultsRoot 'scripts/unity'
    $manualGithubRoot = Join-Path $manualDefaultsRoot '.github'
    New-Item -ItemType Directory -Force -Path $manualScriptsRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $manualGithubRoot | Out-Null
    Copy-Item -LiteralPath $windowsRunnerMaintenancePath -Destination (Join-Path $manualScriptsRoot 'maintain-windows-runner.ps1') -Force
    @'
function Invoke-WindowsRunnerBootstrap {
    param(
        [switch]$DetectOnly,
        [string]$UnityInstallRoot,
        [string]$DiagnosticsRoot
    )

    if (-not $DetectOnly) {
        throw 'UH_RUNNER_DISABLE_AUTO_BOOTSTRAP was not forwarded to bootstrap.'
    }
    if ([string]::IsNullOrWhiteSpace($DiagnosticsRoot)) {
        throw 'Manual maintenance did not pass a default DiagnosticsRoot to bootstrap.'
    }
    if ($DiagnosticsRoot -notmatch '\.artifacts[\\/]+runner-bootstrap$') {
        throw "Unexpected bootstrap DiagnosticsRoot: $DiagnosticsRoot"
    }

    Write-Output "fake bootstrap ok: detect=$([bool]$DetectOnly) diagnostics=$DiagnosticsRoot root=$UnityInstallRoot"
    return 0
}
'@ | Set-Content -LiteralPath (Join-Path $manualScriptsRoot 'bootstrap-windows-runner.ps1') -Encoding UTF8
    @'
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+f\d+$')]
    [string]$UnityVersion,

    [string]$InstallRoot,
    [string]$DiagnosticsPath,
    [switch]$CiManagedOnly,

    [ValidateSet('EditorOnly', 'StandaloneWindowsIl2Cpp', 'Android', 'Full')]
    [string]$ProvisioningProfile = 'Full',

    [switch]$RequireHealthyExisting
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($UnityVersion -notin @('2021.3.45f1', '6000.5.2f1')) {
    throw "Bad UnityVersion: $UnityVersion"
}
if ($InstallRoot -ne 'C:\Unity\Editors') {
    throw "Bad InstallRoot: $InstallRoot"
}
if ($ProvisioningProfile -ne 'StandaloneWindowsIl2Cpp') {
    throw "Bad ProvisioningProfile: $ProvisioningProfile"
}
if (-not $CiManagedOnly) {
    throw 'CiManagedOnly was not bound.'
}
if (-not $RequireHealthyExisting) {
    throw 'UH_RUNNER_DISABLE_AUTO_BOOTSTRAP did not force RequireHealthyExisting.'
}
if ($DiagnosticsPath -notmatch '\.artifacts[\\/]+runner-bootstrap[\\/]+unity-\d+\.\d+\.\d+f\d+$') {
    throw "Bad DiagnosticsPath: $DiagnosticsPath"
}

Write-Output "fake ensure-editor ok: $UnityVersion diagnostics=$DiagnosticsPath"
'@ | Set-Content -LiteralPath (Join-Path $manualScriptsRoot 'ensure-editor.ps1') -Encoding UTF8
    @'
{
  "all": [
    "2021.3.45f1",
    "6000.5.2f1"
  ]
}
'@ | Set-Content -LiteralPath (Join-Path $manualGithubRoot 'unity-versions.json') -Encoding UTF8

    $manualDefaultsOutput = & pwsh -NoProfile -File (Join-Path $manualScriptsRoot 'maintain-windows-runner.ps1') 2>&1
    $manualDefaultsExitCode = $LASTEXITCODE
} finally {
    if ($oldDisableAutoBootstrap) {
        $env:UH_RUNNER_DISABLE_AUTO_BOOTSTRAP = $oldDisableAutoBootstrap
    } else {
        Remove-Item Env:\UH_RUNNER_DISABLE_AUTO_BOOTSTRAP -ErrorAction SilentlyContinue
    }
    if ($manualDefaultsRoot -and (Test-Path -LiteralPath $manualDefaultsRoot -PathType Container)) {
        Remove-Item -LiteralPath $manualDefaultsRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
$manualDefaultsText = $manualDefaultsOutput -join ' '
if (
    $manualDefaultsExitCode -ne 0 -or
    $manualDefaultsText -notmatch 'Unity versions from \.github[\\/]unity-versions\.json: 2021\.3\.45f1, 6000\.5\.2f1' -or
    $manualDefaultsText -notmatch 'fake bootstrap ok: detect=True' -or
    $manualDefaultsText -notmatch 'fake ensure-editor ok: 2021\.3\.45f1' -or
    $manualDefaultsText -notmatch 'fake ensure-editor ok: 6000\.5\.2f1'
) {
    Write-Host "::error file=scripts/unity/maintain-windows-runner.ps1::Direct manual maintenance must load .github/unity-versions.json by default, use a repo-local diagnostics root, and honor UH_RUNNER_DISABLE_AUTO_BOOTSTRAP=1 without requiring YAML-supplied arguments. Exit $manualDefaultsExitCode. Output: $manualDefaultsText"
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked direct manual maintenance defaults match workflow provisioning inputs."
}

$ensureEditorWatchdogImported = $false
try {
    Import-EnsureEditorWatchdogFunctions -ScriptPath $ensureEditorPath
    $script:UnityCliPath = (Get-Command pwsh).Source
    $ensureEditorWatchdogImported = $true
} catch {
    Write-Host "::error file=scripts/unity/ensure-editor.ps1::Could not import ensure-editor watchdog functions for regression tests: $($_.Exception.Message)"
    $failed = $true
}

if ($ensureEditorWatchdogImported) {
    $alternateInstallFunctionAst = Get-FunctionAstByName -Ast $ensureEditorAst -Name 'Install-UnityEditorWithCiModulesInAlternateRoot'
    $alternateInstallContent = if ($alternateInstallFunctionAst) { $alternateInstallFunctionAst.Extent.Text } else { '' }
    $requiredPayloadRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("unity-required-payload-" + [guid]::NewGuid().ToString('N'))
    try {
        $editorPath = Join-Path $requiredPayloadRoot 'Editor\Unity.exe'
        $presentRelative = 'Data\Resources\present.meta'
        $missingRelative = 'Data\Resources\missing.meta'
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $editorPath) | Out-Null
        New-Item -ItemType File -Force -Path $editorPath | Out-Null
        New-Item -ItemType Directory -Force -Path (Join-Path (Split-Path -Parent $editorPath) 'Data\Resources') | Out-Null
        New-Item -ItemType File -Force -Path (Join-Path (Split-Path -Parent $editorPath) $presentRelative) | Out-Null

        $missingPayload = @(Get-MissingRequiredEditorPayloadPaths `
            -EditorPath $editorPath `
            -RelativePaths @($presentRelative, $missingRelative))
        $traversalRejected = $false
        try {
            Get-MissingRequiredEditorPayloadPaths -EditorPath $editorPath -RelativePaths @('..\outside.txt') | Out-Null
        } catch {
            $traversalRejected = $true
        }

        if (
            $missingPayload.Count -ne 1 -or
            $missingPayload[0] -ne $missingRelative -or
            -not $traversalRejected -or
            -not $ensureEditorContent.Contains('[string[]]$RequiredEditorPayloadRelativePath') -or
            -not $ensureEditorContent.Contains('required editor payload is missing') -or
            -not $ensureEditorContent.Contains('UH_UNITY_DISABLE_EDITOR_REPAIR=1 disabled required-payload auto-repair') -or
            -not $ensureEditorContent.Contains('Using reusable alternate-root CI editor with complete required payload') -or
            -not $alternateInstallContent.Contains('[string[]]$RequiredEditorPayloadRelativePath = @()') -or
            -not $alternateInstallContent.Contains('Quarantining payload-incomplete alternate-root Unity') -or
            -not $alternateInstallContent.Contains('Get-MissingRequiredEditorPayloadPaths') -or
            -not $ensureEditorContent.Contains('-RequiredEditorPayloadRelativePath $RequiredEditorPayloadRelativePath') -or
            -not $ensureEditorContent.Contains('Required-payload repair for Unity') -or
            -not $ensureEditorContent.Contains('Install-UnityEditorWithCiModulesInAlternateRoot')
        ) {
            Write-Host "::error file=scripts/unity/ensure-editor.ps1::Required editor payload validation must reject traversal, report only missing relative files, honor the repair-disable flag, and make alternate-root reuse and locked-tree fallback payload-aware. Missing='$($missingPayload -join ',')' TraversalRejected=$traversalRejected."
            $failed = $true
        } elseif ($VerboseOutput) {
            Write-Info 'Checked required editor payload validation and repair contract.'
        }
    } catch {
        Write-Host "::error file=scripts/unity/ensure-editor.ps1::Required editor payload validation regression failed: $($_.Exception.Message)"
        $failed = $true
    } finally {
        Remove-Item -LiteralPath $requiredPayloadRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    $repairRecoveryFunctionNames = @(
        'Assert-UnityProvisioningBudgetCanFit',
        'Get-UnityCiModuleIds',
        'Confirm-UnityCliManagedInstallRoot',
        'Write-CiNotice',
        'Get-UnityCliModuleInstallArguments',
        'Invoke-UnityCliCapture',
        'Resolve-InstalledEditor',
        'Get-MissingUnityCiModuleGroups'
    )
    $repairRecoveryOriginalFunctions = @{}
    $oldProvisioningEditorPathVariable = Get-Variable -Name ProvisioningEditorPath -Scope Script -ErrorAction SilentlyContinue
    $oldProvisioningEditorPath = if ($oldProvisioningEditorPathVariable) { $oldProvisioningEditorPathVariable.Value } else { $null }
    try {
        foreach ($functionName in $repairRecoveryFunctionNames) {
            $existingFunction = Get-Item "Function:\$functionName" -ErrorAction SilentlyContinue
            $repairRecoveryOriginalFunctions[$functionName] = if ($existingFunction) { $existingFunction.ScriptBlock } else { $null }
        }

        function script:Assert-UnityProvisioningBudgetCanFit { param([string]$Operation, [int]$MinimumSeconds) }
        function script:Get-UnityCiModuleIds { param([string]$Profile) return @('windows-mono') }
        function script:Confirm-UnityCliManagedInstallRoot { param([string]$Root) return $Root }
        function script:Write-CiNotice { param([string]$Message) $script:repairRecoveryNotices.Add($Message) | Out-Null }
        function script:Get-UnityCliModuleInstallArguments { param([string]$Verb, [string]$Version, [string[]]$ModuleIds) return @($Verb, $Version) }
        function script:Invoke-UnityCliCapture {
            param([string[]]$Arguments)
            return @{
                Success = $false
                ExitCode = 124
                Output = @('Progress: 50%')
                StallKilled = $false
                TimedOutWallClock = $true
            }
        }
        function script:Resolve-InstalledEditor { param([string]$Version, [string]$Root, [switch]$ManagedOnly) return 'D:\Unity\6000.5.2f1\Editor\Unity.exe' }
        function script:Get-MissingUnityCiModuleGroups { param([string]$EditorPath, [string]$Profile) return @($script:repairRecoveryMissingModules) }

        $script:repairRecoveryNotices = New-Object System.Collections.Generic.List[string]
        $script:repairRecoveryMissingModules = @()
        $resolvedRepairEditor = Install-UnityEditorWithCiModules `
            -Version '6000.5.2f1' `
            -InstallRoot 'D:\Unity' `
            -Reason 'live timeout regression' `
            -Profile 'StandaloneWindowsIl2Cpp' `
            -ManagedOnly
        $repairRecoveryNoticeText = @($script:repairRecoveryNotices.ToArray()) -join ' '
        $script:repairRecoveryMissingModules = @('windows-mono')
        $missingModuleFailure = ''
        try {
            Install-UnityEditorWithCiModules `
                -Version '6000.5.2f1' `
                -InstallRoot 'D:\Unity' `
                -Reason 'live timeout regression' `
                -Profile 'StandaloneWindowsIl2Cpp' `
                -ManagedOnly | Out-Null
        } catch {
            $missingModuleFailure = $_.Exception.Message
        }
        if (
            $resolvedRepairEditor -ne 'D:\Unity\6000.5.2f1\Editor\Unity.exe' -or
            $repairRecoveryNoticeText -notmatch 'failed with exit code 124' -or
            $repairRecoveryNoticeText -notmatch 'verifying modules against disk' -or
            $missingModuleFailure -notmatch 'required CI module groups.+still missing.+windows-mono'
        ) {
            Write-Host "::error file=scripts/unity/ensure-editor.ps1::A timed-out repair install must continue to disk module verification when Unity.exe is resolvable afterward and fail closed if required modules are absent. Resolved='$resolvedRepairEditor' Notices='$repairRecoveryNoticeText' MissingModuleFailure='$missingModuleFailure'."
            $failed = $true
        } elseif ($VerboseOutput) {
            Write-Info 'Checked repair installs recover a resolvable editor after a Unity CLI timeout and still verify modules on disk.'
        }
    } catch {
        Write-Host "::error file=scripts/unity/ensure-editor.ps1::Repair-install timeout recovery regression failed: $($_.Exception.Message)"
        $failed = $true
    } finally {
        foreach ($functionName in $repairRecoveryFunctionNames) {
            if ($repairRecoveryOriginalFunctions[$functionName]) {
                Set-Item "Function:\$functionName" -Value $repairRecoveryOriginalFunctions[$functionName]
            } else {
                Remove-Item "Function:\$functionName" -ErrorAction SilentlyContinue
            }
        }
        Remove-Variable -Name repairRecoveryNotices -Scope Script -ErrorAction SilentlyContinue
        Remove-Variable -Name repairRecoveryMissingModules -Scope Script -ErrorAction SilentlyContinue
        if ($oldProvisioningEditorPathVariable) {
            $script:ProvisioningEditorPath = $oldProvisioningEditorPath
        } else {
            Remove-Variable -Name ProvisioningEditorPath -Scope Script -ErrorAction SilentlyContinue
        }
    }

    $oldInstallTimeout = $env:UH_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS
    $oldProvisioningProfileVariable = Get-Variable -Name UnityProvisioningProfile -Scope Script -ErrorAction SilentlyContinue
    $oldProvisioningProfile = if ($oldProvisioningProfileVariable) { [string]$oldProvisioningProfileVariable.Value } else { $null }
    try {
        Remove-Item Env:\UH_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS -ErrorAction SilentlyContinue
        $editorOnlyInstallTimeout = Get-EnsureEditorInstallTimeoutForProfile -Profile 'EditorOnly'
        $standaloneInstallTimeout = Get-EnsureEditorInstallTimeoutForProfile -Profile 'StandaloneWindowsIl2Cpp'
        $androidInstallTimeout = Get-EnsureEditorInstallTimeoutForProfile -Profile 'Android'
        $fullInstallTimeout = Get-EnsureEditorInstallTimeoutForProfile -Profile 'Full'

        $env:UH_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS = '13'
        $overrideInstallTimeout = Get-EnsureEditorInstallTimeoutForProfile -Profile 'StandaloneWindowsIl2Cpp'

        $env:UH_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS = 'not-an-int'
        $invalidOverrideInstallTimeout = Get-EnsureEditorInstallTimeoutForProfile -Profile 'StandaloneWindowsIl2Cpp' 6>$null
    } finally {
        if ($oldInstallTimeout) {
            $env:UH_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS = $oldInstallTimeout
        } else {
            Remove-Item Env:\UH_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS -ErrorAction SilentlyContinue
        }
        if ($oldProvisioningProfile) {
            $script:UnityProvisioningProfile = $oldProvisioningProfile
        } else {
            Remove-Variable -Name UnityProvisioningProfile -Scope Script -ErrorAction SilentlyContinue
        }
    }

    $originalUnityCliCapture = ${function:Invoke-UnityCliCaptureWithTimeout}
    $oldProvisioningDeadlineVariable = Get-Variable -Name ProvisioningDeadlineUtc -Scope Script -ErrorAction SilentlyContinue
    $oldProvisioningDeadline = if ($oldProvisioningDeadlineVariable) { $oldProvisioningDeadlineVariable.Value } else { $null }
    try {
        $script:ProvisioningDeadlineUtc = [DateTime]::MaxValue
        $script:unityCliCaptureResult = @{
            Success           = $false
            ExitCode          = 124
            Output            = @('D:\actions-runner\_work\_tool\qora-unity-editors')
            StallKilled       = $false
            TimedOutWallClock = $true
        }
        function script:Invoke-UnityCliCaptureWithTimeout {
            param(
                [string[]]$Arguments,
                [int]$TimeoutSeconds,
                [string]$TimeoutKnob,
                [switch]$TimeoutAsWarning
            )
            return $script:unityCliCaptureResult
        }

        $discardedTimedOutOutput = Get-UnityCliOutput -Arguments @('install-path')
        $acceptedTimedOutOutput = @(Get-UnityCliOutput -Arguments @('install-path') -AcceptCapturedOutputOnTimeout)
        $acceptedTimedOutSetter = Invoke-UnityCliSafe `
            -Arguments @('install-path', '-s', 'D:\actions-runner\_work\_tool\qora-unity-editors') `
            -AcceptCapturedOutputPattern '^D:\\actions-runner\\_work\\_tool\\qora-unity-editors$'
        $script:unityCliCaptureResult.Output = @('D:\unexpected-root')
        $rejectedMismatchedSetter = Invoke-UnityCliSafe `
            -Arguments @('install-path', '-s', 'D:\actions-runner\_work\_tool\qora-unity-editors') `
            -AcceptCapturedOutputPattern '^D:\\actions-runner\\_work\\_tool\\qora-unity-editors$'
        $script:unityCliCaptureResult.Output = @('D:\actions-runner\_work\_tool\qora-unity-editors')
        $script:unityCliCaptureResult.TimedOutWallClock = $false
        $rejectedNativeExitSetter = Invoke-UnityCliSafe `
            -Arguments @('install-path', '-s', 'D:\actions-runner\_work\_tool\qora-unity-editors') `
            -AcceptCapturedOutputPattern '^D:\\actions-runner\\_work\\_tool\\qora-unity-editors$'
        if (
            $null -ne $discardedTimedOutOutput -or
            $acceptedTimedOutOutput.Count -ne 1 -or
            $acceptedTimedOutOutput[0] -ne 'D:\actions-runner\_work\_tool\qora-unity-editors' -or
            -not $acceptedTimedOutSetter -or
            $rejectedMismatchedSetter -or
            $rejectedNativeExitSetter
        ) {
            Write-Host "::error file=scripts/unity/ensure-editor.ps1::Install-path probes must accept exact positive output captured before a wrapper timeout, while ordinary getter calls must continue rejecting timed-out output."
            $failed = $true
        } elseif ($VerboseOutput) {
            Write-Info 'Checked install-path probes preserve exact output captured before wrapper timeout.'
        }

        if (
            $ensureEditorContent -notmatch "Get-UnityCliOutput\s+-Arguments\s+@\('install-path'\)\s+-AcceptCapturedOutputOnTimeout" -or
            $ensureEditorContent -notmatch 'Invoke-UnityCliSafe[^\r\n]+-AcceptCapturedOutputPattern'
        ) {
            Write-Host "::error file=scripts/unity/ensure-editor.ps1::Install-path getter and setter wiring must opt into exact output recovery after a wrapper timeout."
            $failed = $true
        }
    } catch {
        Write-Host "::error file=scripts/unity/ensure-editor.ps1::Install-path timeout-output regression failed: $($_.Exception.Message)"
        $failed = $true
    } finally {
        ${function:Invoke-UnityCliCaptureWithTimeout} = $originalUnityCliCapture
        if ($oldProvisioningDeadlineVariable) {
            $script:ProvisioningDeadlineUtc = $oldProvisioningDeadline
        } else {
            Remove-Variable -Name ProvisioningDeadlineUtc -Scope Script -ErrorAction SilentlyContinue
        }
        Remove-Variable -Name unityCliCaptureResult -Scope Script -ErrorAction SilentlyContinue
    }

    if (
        $editorOnlyInstallTimeout -ne 2700 -or
        $standaloneInstallTimeout -lt 7200 -or
        $androidInstallTimeout -lt 7200 -or
        $fullInstallTimeout -lt 7200 -or
        $overrideInstallTimeout -ne 13 -or
        $invalidOverrideInstallTimeout -lt 7200
    ) {
        Write-Host "::error file=scripts/unity/ensure-editor.ps1::Ensure-editor install wall-clock timeout must stay profile-aware: EditorOnly keeps 2700s, heavy module profiles need at least 7200s for cold Unity 6000.5 module installs, UH_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS must remain authoritative, and invalid overrides must fall back to the profile-aware default. Observed EditorOnly=$editorOnlyInstallTimeout Standalone=$standaloneInstallTimeout Android=$androidInstallTimeout Full=$fullInstallTimeout Override=$overrideInstallTimeout InvalidOverride=$invalidOverrideInstallTimeout."
        $failed = $true
    } elseif ($VerboseOutput) {
        Write-Info "Checked ensure-editor install timeout is profile-aware for heavy Unity module installs."
    }

    $repeatedProgressChild = @'
1..20 | ForEach-Object {
    Write-Host '{"type":"progress","pct":50,"msg":"Installing Unity (6000.5.2f1)...","phase":"install"}'
    Start-Sleep -Milliseconds 250
}
exit 0
'@

    $repeatedProgressStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $repeatedProgressResult = Invoke-EnsureEditorWatchdogProbe -ChildCommand $repeatedProgressChild -StallSeconds 4 -TimeoutSeconds 30 6>$null
    $repeatedProgressStopwatch.Stop()
    if ($repeatedProgressResult.StallKilled -or $repeatedProgressResult.TimedOutWallClock -or $repeatedProgressResult.ExitCode -ne 0 -or $repeatedProgressStopwatch.Elapsed.TotalSeconds -gt 20) {
        Write-Host "::error file=scripts/unity/ensure-editor.ps1::Ensure-editor watchdog must not heartbeat-stall repeated identical Unity progress output while the CLI is still emitting lines. Exit $($repeatedProgressResult.ExitCode). StallKilled=$($repeatedProgressResult.StallKilled). TimedOutWallClock=$($repeatedProgressResult.TimedOutWallClock). Elapsed=$([Math]::Round($repeatedProgressStopwatch.Elapsed.TotalSeconds, 2))s. Output: $(@($repeatedProgressResult.Output) -join ' ')"
        $failed = $true
    } elseif ($VerboseOutput) {
        Write-Info "Checked repeated identical Unity progress output resets the ensure-editor heartbeat."
    }

    $quietStallChild = @'
Write-Host '{"type":"progress","pct":50,"msg":"Installing Unity (6000.5.2f1)...","phase":"install"}'
Start-Sleep -Seconds 20
exit 0
'@

    $quietStallStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $quietStallResult = Invoke-EnsureEditorWatchdogProbe -ChildCommand $quietStallChild -StallSeconds 4 -TimeoutSeconds 30 6>$null
    $quietStallStopwatch.Stop()
    $quietCapturedProgress = ((@($quietStallResult.Output) -join "`n") -match '"type"\s*:\s*"progress"')
    if (-not $quietCapturedProgress -or -not $quietStallResult.StallKilled -or $quietStallResult.TimedOutWallClock -or $quietStallResult.ExitCode -ne 125 -or $quietStallStopwatch.Elapsed.TotalSeconds -gt 15) {
        Write-Host "::error file=scripts/unity/ensure-editor.ps1::Ensure-editor watchdog must still kill a quiet Unity CLI after the heartbeat stall window. Exit $($quietStallResult.ExitCode). StallKilled=$($quietStallResult.StallKilled). TimedOutWallClock=$($quietStallResult.TimedOutWallClock). Elapsed=$([Math]::Round($quietStallStopwatch.Elapsed.TotalSeconds, 2))s. Output: $(@($quietStallResult.Output) -join ' ')"
        $failed = $true
    } elseif ($VerboseOutput) {
        Write-Info "Checked quiet Unity CLI output still trips the ensure-editor heartbeat."
    }

    $chattyWallClockChild = @'
1..60 | ForEach-Object {
    Write-Host '{"type":"progress","pct":50,"msg":"Installing Unity (6000.5.2f1)...","phase":"install"}'
    Start-Sleep -Milliseconds 250
}
exit 0
'@

    $chattyWallClockStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $chattyWallClockResult = Invoke-EnsureEditorWatchdogProbe -ChildCommand $chattyWallClockChild -StallSeconds 4 -TimeoutSeconds 6 6>$null
    $chattyWallClockStopwatch.Stop()
    if ($chattyWallClockResult.StallKilled -or -not $chattyWallClockResult.TimedOutWallClock -or $chattyWallClockResult.ExitCode -ne 124 -or $chattyWallClockStopwatch.Elapsed.TotalSeconds -gt 15) {
        Write-Host "::error file=scripts/unity/ensure-editor.ps1::Ensure-editor watchdog must let wall-clock timeout, not heartbeat stall, bound a chatty no-advance Unity CLI. Exit $($chattyWallClockResult.ExitCode). StallKilled=$($chattyWallClockResult.StallKilled). TimedOutWallClock=$($chattyWallClockResult.TimedOutWallClock). Elapsed=$([Math]::Round($chattyWallClockStopwatch.Elapsed.TotalSeconds, 2))s. Output: $(@($chattyWallClockResult.Output) -join ' ')"
        $failed = $true
    } elseif ($VerboseOutput) {
        Write-Info "Checked chatty no-advance Unity CLI output is bounded by the wall-clock timeout."
    }

    $quarantineRetryRoot = ''
    $oldRetryDelay = $env:UH_ENSURE_EDITOR_RETRY_DELAY_SECONDS
    $oldQuarantineAttempts = $env:UH_ENSURE_EDITOR_QUARANTINE_MOVE_RETRY_ATTEMPTS
    $script:quarantineMoveRetryAttempts = 0
    try {
        $env:UH_ENSURE_EDITOR_RETRY_DELAY_SECONDS = '0'
        $env:UH_ENSURE_EDITOR_QUARANTINE_MOVE_RETRY_ATTEMPTS = '5'
        $quarantineRetryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "unity-quarantine-retry-$PID-$(Get-Random)"
        $version = '6000.5.2f1'
        $installDirectory = Join-Path $quarantineRetryRoot $version
        New-Item -ItemType Directory -Force -Path (Join-Path $installDirectory 'Editor') | Out-Null

        function script:Stop-StaleUnityProvisioningProcesses {
            param(
                [string]$InstallRoot,
                [string]$Version,
                [string]$Reason
            )
        }

        function script:Move-Item {
            param(
                [string]$LiteralPath,
                [string]$Destination,
                [switch]$Force
            )

            $script:quarantineMoveRetryAttempts++
            if ($script:quarantineMoveRetryAttempts -lt 5) {
                throw "simulated Windows file lock on attempt $script:quarantineMoveRetryAttempts"
            }

            Microsoft.PowerShell.Management\Move-Item -LiteralPath $LiteralPath -Destination $Destination -Force:$Force
        }

        Move-UnityInstallDirectoryToQuarantine -InstallDirectory $installDirectory -InstallRoot $quarantineRetryRoot -Version $version 6>$null
        $quarantinedDirectories = @(Get-ChildItem -LiteralPath (Join-Path $quarantineRetryRoot '_quarantine') -Directory -ErrorAction SilentlyContinue)
        if ($script:quarantineMoveRetryAttempts -ne 5 -or $quarantinedDirectories.Count -ne 1 -or (Test-Path -LiteralPath $installDirectory -PathType Container)) {
            Write-Host "::error file=scripts/unity/ensure-editor.ps1::Ensure-editor quarantine move retry must continue past the old three-attempt window when the dedicated retry budget allows it. Attempts=$script:quarantineMoveRetryAttempts. Quarantined=$($quarantinedDirectories.Count). SourceStillExists=$(Test-Path -LiteralPath $installDirectory -PathType Container)."
            $failed = $true
        } elseif ($VerboseOutput) {
            Write-Info "Checked quarantine move retry survives delayed file-lock release."
        }
    } catch {
        Write-Host "::error file=scripts/unity/ensure-editor.ps1::Ensure-editor quarantine move retry regression failed: $($_.Exception.Message)"
        $failed = $true
    } finally {
        if ($oldRetryDelay) { $env:UH_ENSURE_EDITOR_RETRY_DELAY_SECONDS = $oldRetryDelay } else { Remove-Item Env:\UH_ENSURE_EDITOR_RETRY_DELAY_SECONDS -ErrorAction SilentlyContinue }
        if ($oldQuarantineAttempts) { $env:UH_ENSURE_EDITOR_QUARANTINE_MOVE_RETRY_ATTEMPTS = $oldQuarantineAttempts } else { Remove-Item Env:\UH_ENSURE_EDITOR_QUARANTINE_MOVE_RETRY_ATTEMPTS -ErrorAction SilentlyContinue }
        Remove-Item Function:\Move-Item -ErrorAction SilentlyContinue
        Remove-Item Function:\Stop-StaleUnityProvisioningProcesses -ErrorAction SilentlyContinue
        Remove-Variable -Name quarantineMoveRetryAttempts -Scope Script -ErrorAction SilentlyContinue
        if ($quarantineRetryRoot -and (Test-Path -LiteralPath $quarantineRetryRoot -PathType Container)) {
            Remove-Item -LiteralPath $quarantineRetryRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    $atomicFlowRoot = ''
    try {
        $atomicFlowRoot = Join-Path ([System.IO.Path]::GetTempPath()) "unity-atomic-flow-$PID-$(Get-Random)"
        $atomicFlowVersion = '6000.5.2f1'
        $script:atomicFlowCalls = New-Object System.Collections.Generic.List[string]

        function script:Write-CiNotice {
            param([string]$Message)
        }

        function script:Invoke-WithUnityInstallLock {
            param(
                [string]$Version,
                [string]$InstallRoot,
                [scriptblock]$Action,
                [int]$TimeoutMinutes = 180
            )

            return & $Action
        }

        function script:Install-UnityEditorWithCiModules {
            param(
                [string]$Version,
                [string]$InstallRoot,
                [string]$Reason,
                [string]$Profile,
                [switch]$ManagedOnly
            )

            $script:atomicFlowCalls.Add('in-place') | Out-Null
            throw "Unity $Version repair install completed at '$InstallRoot\$Version\Editor\Unity.exe', but required CI module groups for provisioning profile '$Profile' are still missing on disk after the atomic install: windows-il2cpp."
        }

        function script:Install-UnityEditorWithCiModulesInAlternateRoot {
            param(
                [string]$Version,
                [string]$InstallRoot,
                [string]$Reason,
                [string]$Profile,
                [switch]$ManagedOnly
            )

            $script:atomicFlowCalls.Add('alternate-root') | Out-Null
            return (Join-Path (Join-Path (Join-Path (Join-Path $InstallRoot '_ci-managed-editors') $Version) 'Editor') 'Unity.exe')
        }

        function script:Repair-UnityEditorWithCiModules {
            param(
                [string]$Version,
                [string]$EditorPath,
                [string]$InstallRoot,
                [string]$Reason,
                [string]$Profile,
                [switch]$ManagedOnly
            )

            $script:atomicFlowCalls.Add('quarantine') | Out-Null
            throw 'quarantine must not run when alternate-root repair succeeds'
        }

        $expectedAlternateFlowEditor = Join-Path (Join-Path (Join-Path (Join-Path $atomicFlowRoot '_ci-managed-editors') $atomicFlowVersion) 'Editor') 'Unity.exe'
        $resolvedAtomicFlowEditor = Install-UnityEditorModulesViaAtomicReinstall `
            -Version $atomicFlowVersion `
            -EditorPath (Join-Path (Join-Path (Join-Path $atomicFlowRoot $atomicFlowVersion) 'Editor') 'Unity.exe') `
            -InstallRoot $atomicFlowRoot `
            -Reason 'contract test' `
            -Profile 'StandaloneWindowsIl2Cpp' `
            -ManagedOnly `
            6>$null
        $atomicFlowCallText = @($script:atomicFlowCalls.ToArray()) -join ','
        if ($resolvedAtomicFlowEditor -ne $expectedAlternateFlowEditor -or $atomicFlowCallText -ne 'in-place,alternate-root') {
            Write-Host "::error file=scripts/unity/ensure-editor.ps1::Atomic module repair must try alternate-root repair after an existing-editor-pinned in-place failure and must not quarantine when alternate-root repair succeeds. Calls='$atomicFlowCallText' Resolved='$resolvedAtomicFlowEditor' Expected='$expectedAlternateFlowEditor'."
            $failed = $true
        } elseif ($VerboseOutput) {
            Write-Info "Checked atomic module repair uses alternate-root fallback without quarantine when it succeeds."
        }
    } catch {
        Write-Host "::error file=scripts/unity/ensure-editor.ps1::Atomic module repair alternate-root flow regression failed: $($_.Exception.Message)"
        $failed = $true
    } finally {
        foreach ($functionName in @(
                'Write-CiNotice',
                'Invoke-WithUnityInstallLock',
                'Install-UnityEditorWithCiModules',
                'Install-UnityEditorWithCiModulesInAlternateRoot',
                'Repair-UnityEditorWithCiModules'
            )) {
            Remove-Item "Function:\$functionName" -ErrorAction SilentlyContinue
        }
        Remove-Variable -Name atomicFlowCalls -Scope Script -ErrorAction SilentlyContinue
        if ($atomicFlowRoot -and (Test-Path -LiteralPath $atomicFlowRoot -PathType Container)) {
            Remove-Item -LiteralPath $atomicFlowRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

$alternateInstallRootFixture = ''
try {
    $alternateInstallRootFixture = Join-Path ([System.IO.Path]::GetTempPath()) "unity-alternate-root-$PID-$(Get-Random)"
    $alternateInstallVersion = '6000.5.2f1'
    $alternateInstallRoot = Get-UnityCiAlternateInstallRoot -InstallRoot $alternateInstallRootFixture
    $alternateEditorDirectory = Join-Path (Join-Path $alternateInstallRoot $alternateInstallVersion) 'Editor'
    $alternateEditorPath = Join-Path $alternateEditorDirectory 'Unity.exe'
    New-Item -ItemType Directory -Force -Path $alternateEditorDirectory | Out-Null
    New-Item -ItemType File -Force -Path $alternateEditorPath | Out-Null

    $resolvedAlternateEditor = Find-UnityEditor -Version $alternateInstallVersion -Root $alternateInstallRootFixture
    $resolvedFullPath = if ($resolvedAlternateEditor) { [System.IO.Path]::GetFullPath($resolvedAlternateEditor) } else { '' }
    $expectedFullPath = [System.IO.Path]::GetFullPath($alternateEditorPath)
    $classifiesAlreadyInstalled = Test-UnityAtomicInstallFailureMayBePinnedToExistingEditor -Message 'Error: Editor already installed in this location.'
    $classifiesMissingModules = Test-UnityAtomicInstallFailureMayBePinnedToExistingEditor -Message "Unity 6000.5.2f1 repair install completed at 'C:\Unity\Editors\6000.5.2f1\Editor\Unity.exe', but required CI module groups for provisioning profile 'StandaloneWindowsIl2Cpp' are still missing on disk after the atomic install: windows-il2cpp."
    $classifiesCanonicalLock = Test-UnityAtomicInstallFailureMayBePinnedToExistingEditor `
        -Message "The process cannot access the file '$alternateInstallRootFixture\6000.5.2f1\Editor' because it is being used by another process." `
        -InstallRoot $alternateInstallRootFixture `
        -Version $alternateInstallVersion
    $classifiesCacheLock = Test-UnityAtomicInstallFailureMayBePinnedToExistingEditor `
        -Message "The process cannot access the file '$alternateInstallRootFixture\_downloads\6000.5.2f1.tmp' because it is being used by another process." `
        -InstallRoot $alternateInstallRootFixture `
        -Version $alternateInstallVersion
    $classifiesNetworkFailure = Test-UnityAtomicInstallFailureMayBePinnedToExistingEditor -Message 'Unity CDN request failed while downloading the editor archive.'

    if ($resolvedFullPath -ne $expectedFullPath -or -not $classifiesAlreadyInstalled -or -not $classifiesMissingModules -or -not $classifiesCanonicalLock -or $classifiesCacheLock -or $classifiesNetworkFailure) {
        Write-Host "::error file=scripts/unity/ensure-editor.ps1::Ensure-editor must discover reusable alternate-root CI editors and classify only existing-editor-pinned atomic install failures for alternate-root fallback. Resolved='$resolvedFullPath' Expected='$expectedFullPath' AlreadyInstalled=$classifiesAlreadyInstalled MissingModules=$classifiesMissingModules CanonicalLock=$classifiesCanonicalLock CacheLock=$classifiesCacheLock NetworkFailure=$classifiesNetworkFailure."
        $failed = $true
    } elseif ($VerboseOutput) {
        Write-Info "Checked alternate-root editor discovery and atomic-failure classification."
    }
} catch {
    Write-Host "::error file=scripts/unity/ensure-editor.ps1::Ensure-editor alternate-root fallback regression failed: $($_.Exception.Message)"
    $failed = $true
} finally {
    if ($alternateInstallRootFixture -and (Test-Path -LiteralPath $alternateInstallRootFixture -PathType Container)) {
        Remove-Item -LiteralPath $alternateInstallRootFixture -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$sparseRegistryScriptPath = ''
$sparseRegistryOutput = @()
$sparseRegistryExitCode = 1
try {
    $sparseRegistryScriptPath = Join-Path ([System.IO.Path]::GetTempPath()) "unity-runner-sparse-registry-$PID-$(Get-Random).ps1"
    @"
Set-StrictMode -Version Latest
`$ErrorActionPreference = 'Stop'
. '$($windowsRunnerBootstrapPath.Replace("'", "''"))'

function Test-Path {
    param(
        [string]`$LiteralPath,
        [object]`$PathType,
        [object]`$ErrorAction
    )
    return `$true
}

function Get-ChildItem {
    param(
        [string]`$LiteralPath,
        [object]`$ErrorAction
    )
    return @(
        [pscustomobject]@{ PSPath = 'registry-entry-without-display-name' },
        [pscustomobject]@{ PSPath = 'registry-entry-that-throws' },
        [pscustomobject]@{ PSPath = 'registry-entry-with-display-name' }
    )
}

function Get-ItemProperty {
    param(
        [string]`$LiteralPath,
        [object]`$ErrorAction
    )
    if (`$LiteralPath -eq 'registry-entry-that-throws') {
        throw 'Unreadable uninstall registry entry'
    }

    if (`$LiteralPath -eq 'registry-entry-with-display-name') {
        return [pscustomobject]@{ DisplayName = 'Microsoft Visual C++ 2022 Redistributable (x64)' }
    }

    return [pscustomobject]@{ QuietUninstallString = 'msiexec /x {FAKE}' }
}

if (-not (Test-RunnerUninstallDisplayName -Pattern 'Microsoft Visual C\+\+ 2022.*\(x64\)')) {
    Write-Host 'Expected sparse registry probe to find the later matching DisplayName.'
    exit 7
}
"@ | Set-Content -LiteralPath $sparseRegistryScriptPath -Encoding UTF8
    $sparseRegistryOutput = & pwsh -NoProfile -File $sparseRegistryScriptPath 2>&1
    $sparseRegistryExitCode = $LASTEXITCODE
} finally {
    if ($sparseRegistryScriptPath -and (Test-Path -LiteralPath $sparseRegistryScriptPath -PathType Leaf)) {
        Remove-Item -LiteralPath $sparseRegistryScriptPath -Force -ErrorAction SilentlyContinue
    }
}
if ($sparseRegistryExitCode -ne 0) {
    Write-Host "::error file=scripts/unity/bootstrap-windows-runner.ps1::Windows runner bootstrap must tolerate uninstall registry entries without DisplayName under StrictMode. Exit $sparseRegistryExitCode. Output: $($sparseRegistryOutput -join ' ')"
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Windows runner bootstrap sparse uninstall registry entries."
}

# Two properties, and they pull against each other. `cancel-in-progress: false` is mandatory --
# cancelling a licensed job can skip license return and lock release. But that alone makes every new
# run queue behind its predecessor, and a superseded predecessor holds the group for as long as its
# legs sit in the runner queue, where they cannot reach the head guard that would end them. On PR
# #351 the head carried no Unity check at all for two hours while every other check was green.
# Scoping the group to the head is what keeps the second property from costing the first.
$preservesLicensedPrRuns = (
    $workflowContent.Contains('group: unity-tests-${{ github.event.pull_request.number || github.ref }}-${{ github.event.pull_request.head.sha || github.sha }}') -and
    $workflowContent.Contains('cancel-in-progress: false')
)
if (-not $preservesLicensedPrRuns) {
    Write-Host "::error file=.github/workflows/unity-tests.yml::Unity Tests must not cancel an in-progress licensed run (cancellation can skip license return and lock release), and its concurrency group must be scoped to the head SHA so a superseded run cannot block the current head from being validated at all."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity Tests preserves in-progress licensed runs and scopes concurrency per head."
}

$currentPrHeadGuardUses = "Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/require-current-pr-head@$currentPrHeadGuardCommit"
$licensedJobIds = @(
    'unity-tests',
    'unity-tests-standalone',
    'unity-tests-single-threaded',
    'unitypackage-smoke'
)

# ---------------------------------------------------------------------------
# The generated Unity project must survive actions/checkout.
#
# actions/checkout runs `git clean -ffdx` at the top of every job, and `-x`
# means gitignored. While the project lived under .artifacts/, every job deleted
# the Library the previous job had just built on that same disk, and the repo
# paid an actions/cache round trip to put it back: measured across the 60 most
# recent Unity Tests runs, 109 min of restore and 119 min of save, all of it on
# the single serialized organization Unity seat.
#
# Two halves, both easy to undo by accident, so both are pinned:
#   1. Every leg that runs run-ci-tests.ps1 passes -ProjectRoot under
#      RUNNER_WORKSPACE -- the checkout's PARENT, which `git clean` cannot reach.
#   2. No Unity workflow caches a workspace-relative Library again.
# ---------------------------------------------------------------------------
$unityWorkflowFilesWithProjects = @(
    '.github/workflows/unity-tests.yml',
    '.github/workflows/unity-benchmarks.yml'
)
$persistentProjectRootArgument = "-ProjectRoot (Join-Path `$env:RUNNER_WORKSPACE 'unity-workspace')"
foreach ($unityWorkflowFile in $unityWorkflowFilesWithProjects) {
    $unityWorkflowPath = Join-Path $repoRoot $unityWorkflowFile
    if (-not (Test-Path -LiteralPath $unityWorkflowPath -PathType Leaf)) {
        Write-Host "::error file=$unityWorkflowFile::Missing Unity workflow while validating persistent project roots."
        $failed = $true
        continue
    }

    $unityWorkflowText = Get-Content -LiteralPath $unityWorkflowPath -Raw
    $runCiTestsInvocations = @([regex]::Matches($unityWorkflowText, [regex]::Escape('./scripts/unity/run-ci-tests.ps1'))).Count
    $persistentRootDeclarations = @([regex]::Matches(
            $unityWorkflowText,
            [regex]::Escape($persistentProjectRootArgument)
        )).Count

    if ($runCiTestsInvocations -eq 0) {
        Write-Host "::error file=$unityWorkflowFile::Expected at least one run-ci-tests.ps1 invocation while validating persistent project roots."
        $failed = $true
    } elseif ($persistentRootDeclarations -ne $runCiTestsInvocations) {
        Write-Host "::error file=$unityWorkflowFile::Every run-ci-tests.ps1 step must pass ``$persistentProjectRootArgument`` (found $persistentRootDeclarations for $runCiTestsInvocations invocations). Without it the generated project falls back under .artifacts/, where actions/checkout's ``git clean -ffdx`` deletes the Library before every job."
        $failed = $true
    } elseif ($VerboseOutput) {
        Write-Info "Checked $unityWorkflowFile pins the persistent Unity project root on all $runCiTestsInvocations run steps."
    }

    if ($unityWorkflowText -match '(?m)^\s*\.artifacts/unity/projects/.*?/Library\s*$') {
        Write-Host "::error file=$unityWorkflowFile::A workspace-relative '.artifacts/unity/projects/**/Library' path reappeared in an actions/cache step. The project now lives outside the workspace; caching the old path uploads an empty directory and re-adds the restore/save cost the persistent root removed."
        $failed = $true
    } elseif ($VerboseOutput) {
        Write-Info "Checked $unityWorkflowFile no longer caches a workspace-relative Library."
    }

    # Two legs may share a persistent project directory ONLY if they generate the
    # same project. -IncludeIntegrations changes the ephemeral manifest (three DI
    # packages) and therefore the compiled assembly set, so a leg that passes it and
    # a leg that does not must never land on the same directory -- they would rewrite
    # the manifest over each other and re-resolve packages on every alternation. The
    # rule is expressed per file because the split runs along workflow lines:
    # unity-tests.yml always integrates, unity-benchmarks.yml never does.
    # Anchored to a run-line, not the bare word: the workflows discuss
    # -IncludeIntegrations in comments, and a comment is not a flag.
    $integrationInvocations = @([regex]::Matches($unityWorkflowText, '(?m)^\s+-IncludeIntegrations\b')).Count
    $benchmarkScopes = @([regex]::Matches($unityWorkflowText, "-ProjectScope 'benchmarks'")).Count
    if ($unityWorkflowFile -eq '.github/workflows/unity-benchmarks.yml') {
        if ($integrationInvocations -ne 0) {
            Write-Host "::error file=$unityWorkflowFile::This workflow now passes -IncludeIntegrations. Either drop it or give these legs a project scope that cannot collide with unity-tests.yml's, which also integrates."
            $failed = $true
        }
        if ($benchmarkScopes -ne $runCiTestsInvocations) {
            Write-Host "::error file=$unityWorkflowFile::Every run-ci-tests.ps1 step must pass ``-ProjectScope 'benchmarks'`` (found $benchmarkScopes for $runCiTestsInvocations invocations). Without it these legs share a persistent project directory with unity-tests.yml, whose legs pass -IncludeIntegrations and therefore generate a different manifest and assembly set."
            $failed = $true
        } elseif ($VerboseOutput) {
            Write-Info "Checked $unityWorkflowFile keeps its own project scope."
        }
    }
}

# ---------------------------------------------------------------------------
# Step timeouts stay inside the measured budget.
#
# Across the 60 most recent Unity Tests runs (~430 leg instances) the slowest
# 'Run Unity Test Runner' has been 9.1 min, standalone topping out at 8.7 min.
# The caps below keep >4x headroom over that. The reason they must not drift
# back up: every leg serializes on ONE organization Unity seat, and a 91-minute
# lock wait has been measured behind a stuck leg -- an oversized step clock
# converts a single hang into org-wide starvation.
# ---------------------------------------------------------------------------
$unityRunTimeoutContracts = @(
    @{
        Name = 'default matrix + standalone run timeout'
        Pattern = 'timeout-minutes:\s*\$\{\{\s*\(matrix\.test-mode\s*==\s*''standalone''\s*&&\s*60\)\s*\|\|\s*40\s*\}\}'
        Message = "The default and standalone 'Run Unity Test Runner' steps must cap at 60 min (standalone) / 40 min (editmode, playmode). Measured worst case is 8.7-9.1 min; a larger cap only lengthens how long a hang holds the single organization Unity seat."
    },
    @{
        Name = 'single-threaded run timeout'
        Pattern = '(?ms)- name: Run Unity Test Runner.*?-ProjectScope ''single-threaded'''
        Message = "The SINGLE_THREADED 'Run Unity Test Runner' step must pass -ProjectScope 'single-threaded' so its differently-compiled Library never shares a directory with the default matrix's."
    }
)
foreach ($contract in $unityRunTimeoutContracts) {
    if ($workflowContent -notmatch $contract.Pattern) {
        Write-Host "::error file=.github/workflows/unity-tests.yml::Unity workflow contract failed ($($contract.Name)): $($contract.Message)"
        $failed = $true
    } elseif ($VerboseOutput) {
        Write-Info "Checked Unity run-step contract '$($contract.Name)'."
    }
}

# ---------------------------------------------------------------------------
# A superseded run must be a no-op, whichever side of dispatch the push landed.
#
# matrix-config resolves supersession before any leg is dispatched, so it cannot
# see a push that lands while legs are queued for the single Unity seat. Those
# legs then fail on their own require-current-pr-head guards and the gate reports
# the run red -- measured on run 31020762387: six legs failed, all six on
# "Stale pull request run", zero on a test. Unity CI Success therefore re-resolves
# the head itself, and the waiver covers both signals.
# ---------------------------------------------------------------------------
$lateSupersessionContracts = @(
    @{
        Name = 'Unity CI Success re-detects a late supersession'
        Pattern = '(?ms)- name: Re-detect superseded pull request head\s*\r?\n\s+id:\s+late_superseded\b'
        Message = 'Unity CI Success must re-resolve the pull request head itself. matrix-config answers before dispatch, so a push that lands while legs are queued leaves every leg failing its own head guard and the run red.'
    },
    @{
        Name = 'the late-supersession probe fails open'
        # Anchored two ways, because both are easy to get wrong. The wording
        # "reporting this run's real result" is unique to the LATE probe --
        # matrix-config's probe emits a near-identical warning, and a pattern that
        # matches either passes no matter what the late one does. And the
        # superseded=false must be the very next line, or the pattern also matches
        # the later "still at the expected sha" branch and passes when the
        # unresolvable case has been flipped to superseded=true -- which would waive
        # validation on every API hiccup, the one thing this must not do.
        Pattern = "reporting this run's real result[^\r\n]*\r?\n\s*echo `"superseded=false`""
        Message = 'The late-supersession probe must report superseded=false on the line right after it fails to resolve the head, so an API hiccup costs a redundant red rather than a waived validation.'
    },
    @{
        Name = 'the waiver honors both supersession signals'
        Pattern = '\[ "\$\{MATRIX_CONFIG_SUPERSEDED\}" = "true" \] \|\| \[ "\$\{LATE_SUPERSEDED\}" = "true" \]'
        Message = 'The supersession waiver must accept the late signal as well as matrix-config''s, or a push that lands after dispatch still reports the run red.'
    },
    @{
        Name = 'supersession still requires the hosted gates'
        Pattern = '(?ms)LATE_SUPERSEDED.*?Superseded run, but a hosted gate did not pass'
        Message = 'Supersession must waive only the four licensed results; matrix-config and runner-preflight run to completion regardless and must still pass.'
    }
)
foreach ($contract in $lateSupersessionContracts) {
    if ($workflowContent -notmatch $contract.Pattern) {
        Write-Host "::error file=.github/workflows/unity-tests.yml::Unity workflow contract failed ($($contract.Name)): $($contract.Message)"
        $failed = $true
    } elseif ($VerboseOutput) {
        Write-Info "Checked superseded-gate contract '$($contract.Name)'."
    }
}

$oversizedRunTimeouts = @([regex]::Matches(
        $workflowContent,
        '(?ms)- name: Run Unity Test Runner.*?timeout-minutes:\s*(\d+)\s*$'
    ))
foreach ($match in $oversizedRunTimeouts) {
    $declaredTimeout = [int]$match.Groups[1].Value
    if ($declaredTimeout -gt 60) {
        Write-Host "::error file=.github/workflows/unity-tests.yml::A 'Run Unity Test Runner' step declares timeout-minutes: $declaredTimeout. The measured worst case is 9.1 min; anything above 60 hands a hung leg more than an hour of the single organization Unity seat."
        $failed = $true
    }
}
foreach ($licensedJobId in $licensedJobIds) {
    if (-not $jobTexts.ContainsKey($licensedJobId)) {
        Write-Host "::error file=.github/workflows/unity-tests.yml::Missing licensed job '$licensedJobId' while validating current-PR-head guards."
        $failed = $true
        continue
    }

    $licensedJob = [string]$jobTexts[$licensedJobId]
    $guardSteps = @([regex]::Matches(
            $licensedJob,
            '(?ms)^\s+- name: Require current PR head before (?:setup|lock acquisition)\s*$.*?(?=^\s+- name:|\z)'
        ))
    $setupGuardIndex = $licensedJob.IndexOf('- name: Require current PR head before setup', [StringComparison]::Ordinal)
    $lockGuardIndex = $licensedJob.IndexOf('- name: Require current PR head before lock acquisition', [StringComparison]::Ordinal)
    $acquireIndex = $licensedJob.IndexOf('- name: Acquire organization Unity lock', [StringComparison]::Ordinal)
    $nextStepAfterLockGuard = if ($lockGuardIndex -ge 0) {
        $licensedJob.IndexOf('- name:', $lockGuardIndex + 1, [StringComparison]::Ordinal)
    } else {
        -1
    }
    $firstStepIndex = $licensedJob.IndexOf('- name:', [StringComparison]::Ordinal)
    $guardInputsAreExact = (
        $guardSteps.Count -eq 2 -and
        @($guardSteps | Where-Object {
                $_.Value.Contains("uses: $currentPrHeadGuardUses") -and
                $_.Value.Contains('github-token: ${{ github.token }}') -and
                $_.Value.Contains('pull-request-number: ${{ github.event.pull_request.number }}') -and
                $_.Value.Contains('expected-head-sha: ${{ github.event.pull_request.head.sha }}')
            }).Count -eq 2
    )

    if (
        $setupGuardIndex -lt 0 -or
        $setupGuardIndex -ne $firstStepIndex -or
        $lockGuardIndex -lt 0 -or
        $acquireIndex -lt 0 -or
        $nextStepAfterLockGuard -ne $acquireIndex -or
        -not $guardInputsAreExact
    ) {
        Write-Host "::error file=.github/workflows/unity-tests.yml::Licensed job '$licensedJobId' must use the exact pinned current-PR-head guard as its first step and again immediately before lock acquisition."
        $failed = $true
    } elseif ($VerboseOutput) {
        Write-Info "Checked current-PR-head guards for licensed job '$licensedJobId'."
    }
}

# The per-leg guards above fire only after a leg has been dispatched, which means
# waiting in line for the single self-hosted Unity seat. The concurrency group is
# scoped per head so that queue can no longer block the successor run outright,
# but a superseded iteration that dispatches its legs anyway still burns runner
# slots to fail eight guards. The hosted matrix-config job must resolve
# supersession once and skip the licensed tiers outright.
$matrixConfigJob = if ($jobTexts.ContainsKey('matrix-config')) { [string]$jobTexts['matrix-config'] } else { '' }
$supersededStep = [regex]::Match(
    $matrixConfigJob,
    '(?ms)^      - name: Detect superseded pull request head\s*$.*?(?=^      - name:|\z)'
)
$supersededDecisions = @(
    [regex]::Matches(
        $(if ($supersededStep.Success) { $supersededStep.Value } else { '' }),
        'superseded=(?<value>true|false)'
    ) | ForEach-Object { $_.Groups['value'].Value }
)
$supersededGateContracts = @(
    @{
        Name = 'matrix-config exposes the superseded output'
        Ok = $matrixConfigJob -match '(?m)^      superseded:\s*\$\{\{\s*steps\.superseded\.outputs\.superseded\s*\}\}\s*$'
        Message = 'matrix-config must expose a `superseded` output wired to the detection step so every licensed tier can gate on it.'
    },
    @{
        Name = 'matrix-config detects a superseded head on a hosted runner'
        Ok = $supersededStep.Success -and $supersededStep.Value -match '(?m)^        id: superseded\s*$'
        Message = 'matrix-config must carry a "Detect superseded pull request head" step with id `superseded`.'
    },
    @{
        Name = 'superseded detection compares the queued head against the live head'
        Ok = (
            $supersededStep.Success -and
            $supersededStep.Value.Contains('PR_NUMBER: ${{ github.event.pull_request.number }}') -and
            $supersededStep.Value.Contains('EXPECTED_HEAD_SHA: ${{ github.event.pull_request.head.sha }}') -and
            $supersededStep.Value.Contains('.head.sha')
        )
        Message = 'The superseded detection step must compare the run''s queued head SHA against the pull request''s live head SHA.'
    },
    @{
        # Counting the two values is not enough -- swapping which BRANCH writes
        # which value keeps the counts identical while inverting the whole
        # guarantee. Bind them to position instead: every early exit writes
        # false, and only the step's final, unconditional write says true. Any
        # branch that reports superseded before the comparison has run then
        # lands a `true` ahead of the last write and fails here.
        Name = 'superseded detection fails open'
        Ok = $supersededDecisions.Count -ge 4 -and
        $supersededDecisions[-1] -eq 'true' -and
        @($supersededDecisions[0..($supersededDecisions.Count - 2)] | Where-Object { $_ -ne 'false' }).Count -eq 0
        Message = 'The superseded detection step must fail OPEN: every early exit (not a pull request, unresolvable head, head unchanged) must write superseded=false, and only the final unconditional write may say true, so validation is never skipped by an API hiccup.'
    },
    @{
        # `gh api --jq '.head.sha'` prints the literal "null" and exits 0 when
        # the field is absent, and "null" compares unequal to any real SHA. An
        # emptiness check alone would turn that anomaly into a skipped run.
        Name = 'superseded detection validates the resolved head shape'
        Ok = $supersededStep.Success -and $supersededStep.Value -match '\[0-9a-f\]'
        Message = 'The superseded detection step must validate that the resolved head looks like a SHA, not merely that it is non-empty, so an API response without the field cannot skip the licensed tiers.'
    },
    @{
        Name = 'Unity CI Success still requires the hosted gates when superseded'
        Ok = (
            $workflowContent -match '(?m)^            if \[ "\$\{MATRIX_CONFIG_RESULT\}" != "success" \] \|\| \[ "\$\{RUNNER_PREFLIGHT_RESULT\}" != "success" \]; then\s*$'
        )
        Message = 'The superseded short-circuit must still require matrix-config and runner-preflight to have succeeded. Both jobs run to completion regardless of supersession -- matrix-config goes on to lint the test-project module manifest -- and a failed job still publishes its outputs, so waiving them would report green for a bogus UPM module id or an offline runner fleet.'
    },
    @{
        Name = 'Unity CI Success treats a superseded run as a clean no-op'
        Ok = (
            $workflowContent.Contains('MATRIX_CONFIG_SUPERSEDED: ${{ needs.matrix-config.outputs.superseded }}') -and
            $workflowContent -match '(?m)^          if \[ "\$\{MATRIX_CONFIG_SUPERSEDED\}" = "true" \] \|\| \[ "\$\{LATE_SUPERSEDED\}" = "true" \]; then\s*$'
        )
        Message = 'unity-ci-success must exit 0 for a superseded run: it never ran the licensed tiers and its check belongs to the head SHA it was queued for, so it can neither gate nor authorize the current head.'
    }
)
foreach ($licensedJobId in $licensedJobIds) {
    $supersededGateContracts += @{
        Name = "licensed job '$licensedJobId' skips when superseded"
        Ok = (
            $jobTexts.ContainsKey($licensedJobId) -and
            [string]$jobTexts[$licensedJobId] -match "needs\.matrix-config\.outputs\.superseded\s*!=\s*'true'"
        )
        Message = "Licensed job '$licensedJobId' must skip when matrix-config reports the pull request head has moved on, so a superseded run never queues for the self-hosted Unity seat."
    }
}
# The stuck-job watchdog reported success on every cycle from #315 through #328
# while never evaluating a single queued run: `orgs/{owner}/actions/runners`
# needs admin:org, GITHUB_TOKEN 403s, the repo-scoped fallback does not list
# org-level runners, and the handler exited 0. A permanently green workflow that
# is structurally blind is the same silent-failure class #328 was opened for.
$watchdogPath = Join-Path $repoRoot '.github/workflows/stuck-job-watchdog.yml'
if (-not (Test-Path -LiteralPath $watchdogPath)) {
    Write-Host "::error::Stuck job watchdog workflow not found: $watchdogPath"
    $failed = $true
} else {
    $watchdogContent = Get-Content -LiteralPath $watchdogPath -Raw
    $supersededGateContracts += @(
        @{
            Name = 'watchdog reads the runner inventory with the build-lock reader App'
            Ok = (
                $watchdogContent.Contains('RUNNER_INVENTORY_TOKEN: ${{ steps.reader-token.outputs.token }}') -and
                $watchdogContent.Contains('app-id: ${{ secrets.BUILD_LOCK_READER_APP_ID }}') -and
                $watchdogContent -match 'RUNNER_INVENTORY_TOKEN[^\n]*\n[^\n]*orgs/\$\{OWNER\}/actions/runners'
            )
            Message = 'The watchdog must query orgs/{owner}/actions/runners with the build-lock reader App token. GITHUB_TOKEN lacks admin:org and 403s, and the repo-scoped fallback does not list org-level runners, so without it the audit can never see a runner.'
            File = '.github/workflows/stuck-job-watchdog.yml'
        },
        @{
            Name = 'watchdog fails closed on an unreadable runner inventory'
            Ok = $watchdogContent -match '(?ms)could not read the runner inventory.*?flush_summary_and_exit 1'
            Message = 'The watchdog must exit non-zero when it cannot read the runner inventory. Exiting 0 is what let it report success on every cycle while evaluating nothing -- a blind watchdog must be red, because a green check is exactly what stopped anyone from noticing.'
            File = '.github/workflows/stuck-job-watchdog.yml'
        },
        @{
            Name = 'watchdog recognizes a cancelled run whose every step succeeded'
            Ok = (
                $watchdogContent -match '(?ms)select\(\.conclusion == "cancelled"\).*?select\(\(\(\.steps // \[\]\) \| length\) > 0\).*?select\(all\(\(\.steps // \[\]\)\[\]; \.conclusion == "success"\)\)' -and
                $watchdogContent.Contains('actions/runs/${run_id}/rerun')
            )
            Message = 'The watchdog must detect issue #342 by its signature -- a job with conclusion "cancelled" whose step list is non-empty and every step succeeded -- and recover it with POST actions/runs/{id}/rerun. Requiring a non-empty step list is what keeps a deliberate cancel, whose in-flight step is itself cancelled, from being re-run automatically.'
            File = '.github/workflows/stuck-job-watchdog.yml'
        },
        @{
            Name = 'watchdog checks for green-step cancels before the clean-queue early exit'
            Ok = (
                $watchdogContent.IndexOf('1b. Recover runs GitHub reported `cancelled`') -ge 0 -and
                $watchdogContent.IndexOf('1b. Recover runs GitHub reported `cancelled`') -lt $watchdogContent.IndexOf('Queue is clean. No action.')
            )
            Message = 'The green-step recovery must run before the queued-run scan. That scan exits the whole step as soon as the queue is clean, which is the normal state, so recovery placed after it would almost never execute.'
            File = '.github/workflows/stuck-job-watchdog.yml'
        },
        @{
            Name = 'watchdog bounds automatic re-runs'
            Ok = (
                $watchdogContent -match 'MAX_RERUNS_PER_DAY:\s*"\d+"' -and
                $watchdogContent -match 'MAX_RERUN_ATTEMPT:\s*"\d+"' -and
                $watchdogContent.Contains('(.run_attempt // 1) <= $maxAttempt') -and
                $watchdogContent.Contains('rerun-${run_id}.json')
            )
            Message = 'Automatic re-runs must be bounded twice: by run_attempt (a re-run that lands in the same cancelled state escalates to a human instead of looping every five minutes) and by a per-run daily cap in its own state file. The cap file must be distinct from the cancel state file, whose reader falls back to a .reruns key and would otherwise consume the cancel budget.'
            File = '.github/workflows/stuck-job-watchdog.yml'
        }
    )
}

foreach ($contract in $supersededGateContracts) {
    $contractFile = if ($contract.ContainsKey('File')) { $contract.File } else { '.github/workflows/unity-tests.yml' }
    if (-not $contract.Ok) {
        Write-Host "::error file=$contractFile::Workflow recovery contract failed ($($contract.Name)): $($contract.Message)"
        $failed = $true
    } elseif ($VerboseOutput) {
        Write-Info "Checked workflow recovery contract '$($contract.Name)'."
    }
}

$prAcquireIdentityInputsAreExact = Test-PrCapableAcquireIdentityInputs `
    -WorkflowContent $workflowContent `
    -Jobs $jobTexts
$prAcquireMutationProofsRejectDrift = $true
foreach ($expectedInput in @(
        'github-token: ${{ github.token }}',
        'pull-request-number: ${{ github.event.pull_request.number }}',
        'expected-head-sha: ${{ github.event.pull_request.head.sha }}'
    )) {
    $mutatedJobs = @{}
    foreach ($job in $jobTexts.GetEnumerator()) {
        $mutatedJobs[$job.Key] = [string]$job.Value
    }

    $mutationTarget = [string]$mutatedJobs['unity-tests']
    $acquireStep = [regex]::Match(
        $mutationTarget,
        '(?ms)^      - name: Acquire organization Unity lock\s*$.*?(?=^      - name:|\z)'
    )
    $inputLine = "          $expectedInput"
    $inputIndex = $acquireStep.Value.IndexOf($inputLine, [StringComparison]::Ordinal)
    if (-not $acquireStep.Success -or $inputIndex -lt 0) {
        $prAcquireMutationProofsRejectDrift = $false
        continue
    }

    $mutatedAcquireStep = $acquireStep.Value.Remove($inputIndex, $inputLine.Length)
    $mutatedJobs['unity-tests'] = $mutationTarget.Remove($acquireStep.Index, $acquireStep.Length).Insert(
        $acquireStep.Index,
        $mutatedAcquireStep
    )
    if (Test-PrCapableAcquireIdentityInputs -WorkflowContent $workflowContent -Jobs $mutatedJobs) {
        $prAcquireMutationProofsRejectDrift = $false
    }
}

if (-not $prAcquireIdentityInputsAreExact -or -not $prAcquireMutationProofsRejectDrift) {
    Write-Host '::error file=.github/workflows/unity-tests.yml::Every acquire step in a pull-request-capable workflow must pass the exact PR number, expected head SHA, and GitHub token; mutation proofs must reject each missing binding.'
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info 'Checked every PR-capable acquire step binds exact PR identity and each missing-input mutation is rejected.'
}

$licensedWorkflowJobSets = @(
    @{ File = '.github/workflows/unity-tests.yml'; Jobs = $jobTexts },
    @{ File = '.github/workflows/unity-benchmarks.yml'; Jobs = $benchmarksJobTexts },
    @{ File = '.github/workflows/release.yml'; Jobs = $releaseJobTexts }
)
foreach ($workflowJobSet in $licensedWorkflowJobSets) {
    foreach ($job in $workflowJobSet.Jobs.GetEnumerator()) {
        $jobText = [string]$job.Value
        $isLicensedMatrix = (
            $jobText.Contains('Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/acquire-build-lock@') -and
            $jobText -match '(?m)^    strategy:\s*$' -and
            $jobText -match '(?m)^      matrix:\s*$'
        )
        if (-not $isLicensedMatrix) {
            continue
        }

        $failFastFalseCount = [regex]::Matches($jobText, '(?m)^      fail-fast: false\s*$').Count
        if ($failFastFalseCount -ne 1) {
            Write-Host "::error file=$($workflowJobSet.File)::Licensed matrix job '$($job.Key)' must set exactly one literal strategy.fail-fast: false so a failing leg cannot cancel a sibling Unity license holder."
            $failed = $true
        } elseif ($VerboseOutput) {
            Write-Info "Checked licensed matrix job '$($job.Key)' in $($workflowJobSet.File) disables fail-fast cancellation."
        }
    }
}

$unityMatrixParallelismUsesRunnerSlots = (
    $jobTexts.ContainsKey('unity-tests') -and
    $jobTexts.ContainsKey('unity-tests-standalone') -and
    $jobTexts.ContainsKey('unity-tests-single-threaded') -and
    $benchmarksJobTexts.ContainsKey('benchmarks') -and
    $jobTexts['unity-tests'] -match '(?m)^\s+max-parallel:\s*2\s*$' -and
    $jobTexts['unity-tests-standalone'] -match '(?m)^\s+max-parallel:\s*2\s*$' -and
    $jobTexts['unity-tests-single-threaded'] -match '(?m)^\s+max-parallel:\s*2\s*$' -and
    $benchmarksJobTexts['benchmarks'] -match '(?m)^\s+max-parallel:\s*2\s*$'
)
if (-not $unityMatrixParallelismUsesRunnerSlots) {
    Write-Host "::error file=.github/workflows/unity-tests.yml::Unity self-hosted matrix jobs must use max-parallel: 2 so CI actually uses the two available Unity runner queue slots. Keep .github/workflows/unity-benchmarks.yml in sync."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity matrix jobs use the two available self-hosted runner slots."
}

$unityLockCleanupIsGated = (
    (Test-UnityLockCleanupIsGated `
            -Jobs $jobTexts `
            -WorkflowFile '.github/workflows/unity-tests.yml' `
            -LicensedWorkStepNames @{
                'unity-tests' = 'Run Unity Test Runner'
                'unity-tests-standalone' = 'Run Unity Test Runner'
                'unity-tests-single-threaded' = 'Run Unity Test Runner'
                'unitypackage-smoke' = 'Export Unity package smoke artifact'
            }) -and
    (Test-UnityLockCleanupIsGated `
            -Jobs $benchmarksJobTexts `
            -WorkflowFile '.github/workflows/unity-benchmarks.yml' `
            -LicensedWorkStepNames @{
                benchmarks = @(
                    'Run Unity Test Runner'
                    'Run Random suite at full sample count'
                )
            }) -and
    (Test-UnityLockCleanupIsGated `
            -Jobs $releaseJobTexts `
            -WorkflowFile '.github/workflows/release.yml' `
            -LicensedWorkStepNames @{ unitypackage = 'Export Unity package' })
)
if (-not $unityLockCleanupIsGated) {
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity lock cleanup runs only after acquisition and before release."
}

$runnerTempReturnLogInput = [regex]::Escape('prior-return-log-path: ${{ runner.temp }}/unity-return-${{ matrix.unity-version }}-${{ matrix.test-mode }}.log')
$testRunnerTempReturnLogs = [regex]::Matches($workflowContent, $runnerTempReturnLogInput).Count
$benchmarkRunnerTempReturnLogs = [regex]::Matches(($benchmarksWorkflowLines -join "`n"), $runnerTempReturnLogInput).Count
if ($testRunnerTempReturnLogs -ne 3 -or $benchmarkRunnerTempReturnLogs -ne 1) {
    Write-Host "::error file=scripts/tests/test-unity-workflow-matrix-contract.ps1::run-ci-tests.ps1 cleanup proof must come from its non-uploaded runner-temp return log (tests=$testRunnerTempReturnLogs, benchmarks=$benchmarkRunnerTempReturnLogs)."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info 'Checked run-ci-tests workflows classify the runner-temp Unity return log.'
}

$centralClassifierUses = "Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/classify-unity-cleanup-evidence@$centralCleanupClassifierCommit"
$returnActionResourceProofContract = (
    [regex]::Matches($returnUnityLicenseActionContent, [regex]::Escape("uses: $centralClassifierUses")).Count -eq 2 -and
    $returnUnityLicenseActionContent -match '(?ms)^outputs:\s*$.*?^\s+resource-safe:\s*$.*?^\s+value:\s+\$\{\{ steps\.classify_return\.outputs\.resource-safe \|\| steps\.classify_prior\.outputs\.resource-safe \}\}\s*$' -and
    $returnUnityLicenseActionContent -match '(?ms)^outputs:\s*$.*?^\s+resource-cleanup-status:\s*$.*?^\s+value:\s+\$\{\{ steps\.classify_return\.outputs\.resource-cleanup-status \|\| steps\.classify_prior\.outputs\.resource-cleanup-status \}\}\s*$' -and
    $returnUnityLicenseActionContent -match '(?ms)^outputs:\s*$.*?^\s+resource-health:\s*$.*?^\s+value:\s+\$\{\{ steps\.classify_return\.outputs\.resource-health \|\| steps\.classify_prior\.outputs\.resource-health \}\}\s*$' -and
    $returnUnityLicenseActionContent -match '(?ms)^outputs:\s*$.*?^\s+resource-reason:\s*$.*?^\s+value:\s+\$\{\{ steps\.classify_return\.outputs\.resource-reason \|\| steps\.classify_prior\.outputs\.resource-reason \}\}\s*$' -and
    $returnUnityLicenseActionContent -match '(?ms)^outputs:\s*$.*?^\s+classification-complete:\s*$.*?^\s+value:\s+\$\{\{ steps\.classify_return\.outputs\.classification-complete \|\| steps\.classify_prior\.outputs\.classification-complete \}\}\s*$' -and
    $returnUnityLicenseActionContent.Contains('Get-Content -LiteralPath $file.FullName -Tail 4') -and
    $returnUnityLicenseActionContent.Contains("if (`$file.Length -gt 25MB -or `$file.Extension -notin @('.log', '.txt'))") -and
    $returnUnityLicenseActionContent.Contains('& $editorPath @returnArgs 2>&1 | Out-File -FilePath $returnLog -Encoding utf8') -and
    $returnUnityLicenseActionContent.Contains('Add-Content -LiteralPath $returnLog -Value "exit_return_rc=$exitCode" -Encoding utf8') -and
    -not $returnUnityLicenseActionContent.Contains('Classify-UnityLicenseReturn.ps1') -and
    -not $returnUnityLicenseActionContent.Contains('Tee-Object')
)
if (-not $returnActionResourceProofContract) {
    Write-Host '::error file=.github/actions/return-unity-license/action.yml::Return action must capture bounded private metadata, preserve compatibility outputs, and delegate every cleanup decision to the exact central classifier.'
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info 'Checked return action delegates bounded private evidence to central policy.'
}

$dockerCompletionIndex = $runUnityDockerContent.IndexOf(
    'echo "==> Unity command finished with exit code: ${EXIT_CODE}"',
    [StringComparison]::Ordinal
)
$dockerReturnIndex = if ($dockerCompletionIndex -ge 0) {
    $runUnityDockerContent.IndexOf(
        'return_serial_license || true',
        $dockerCompletionIndex,
        [StringComparison]::Ordinal
    )
} else {
    -1
}
if (
    $dockerCompletionIndex -lt 0 -or
    $dockerReturnIndex -le $dockerCompletionIndex -or
    -not $runUnityDockerContent.Contains(
        'docker rm -f "${UNITY_CONTAINER_NAME}" >/dev/null || true',
        [StringComparison]::Ordinal
    )
) {
    Write-Host '::error file=scripts/unity/run-unity-docker.sh::Docker completion status must be emitted before serial return so exit_return_rc remains the final non-empty evidence line.'
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info 'Checked Docker return evidence ends with the exit_return_rc attestation, including silent EXIT-trap removal.'
}

$centralGateUses = "Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/require-confirmed-unity-cleanup@$centralCleanupGateCommit"
$centralLifecycleFailures = @()
$licensedLifecycleCount = 0
foreach ($workflowJobSet in $licensedWorkflowJobSets) {
    foreach ($job in $workflowJobSet.Jobs.GetEnumerator()) {
        [string]$jobText = $job.Value
        if (-not $jobText.Contains('Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/acquire-build-lock@')) {
            continue
        }
        $licensedLifecycleCount += 1
        $returnIndex = $jobText.IndexOf('- name: Return Unity license', [StringComparison]::Ordinal)
        $releaseIndex = $jobText.IndexOf('- name: Release organization Unity lock', [StringComparison]::Ordinal)
        $gateIndex = $jobText.IndexOf('- name: Require confirmed Unity cleanup', [StringComparison]::Ordinal)
        $deleteIndex = $jobText.IndexOf('- name: Delete private Unity cleanup evidence', [StringComparison]::Ordinal)
        $lifecycleIsOrdered = (
            $returnIndex -ge 0 -and
            $releaseIndex -gt $returnIndex -and
            $gateIndex -gt $releaseIndex -and
            $deleteIndex -gt $gateIndex
        )
        $releaseAndGateAreExact = (
            $jobText -match '(?ms)- name: Release organization Unity lock\s*\r?\n\s+id: release_unity_lock\s*\r?\n.*?resource-cleanup-status: \$\{\{ steps\.return_unity_license\.outputs\.resource-cleanup-status \}\}.*?resource-health: \$\{\{ steps\.return_unity_license\.outputs\.resource-health \}\}.*?resource-reason: \$\{\{ steps\.return_unity_license\.outputs\.resource-reason \}\}' -and
            $jobText.Contains("uses: $centralGateUses") -and
            $jobText.Contains("classification-complete: `${{ steps.return_unity_license.outputs.classification-complete }}") -and
            $jobText.Contains("release-outcome: `${{ steps.release_unity_lock.outcome }}") -and
            $jobText.Contains("cleanup-result: `${{ steps.release_unity_lock.outputs.cleanup-result }}") -and
            $jobText.Contains("reservation-state: `${{ steps.release_unity_lock.outputs.reservation-state }}") -and
            $jobText.Contains("incident-id: `${{ steps.release_unity_lock.outputs.incident-id }}")
        )
        $privateEvidenceIsDeleted = (
            $jobText -match '(?ms)- name: Delete private Unity cleanup evidence\s*\r?\n\s+if: \$\{\{ always\(\) && steps\.unity_lock\.outputs\.acquired == ''true'' \}\}.*?Remove-Item -LiteralPath \$evidencePath -Force'
        )
        if (-not $lifecycleIsOrdered -or -not $releaseAndGateAreExact -or -not $privateEvidenceIsDeleted) {
            $centralLifecycleFailures += "$($workflowJobSet.File):$($job.Key)"
        }
    }
}
if ($licensedLifecycleCount -ne 6 -or $centralLifecycleFailures.Count -gt 0) {
    Write-Host "::error file=scripts/tests/test-unity-workflow-matrix-contract.ps1::Every licensed job must preserve return -> central classify -> release -> central gate -> private evidence deletion. Count=$licensedLifecycleCount Failures=$($centralLifecycleFailures -join ', ')."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info 'Checked six licensed jobs use the central cleanup policy and fail-closed final gate.'
}

$sharedDiagnosticEvidenceFailures = @()
foreach ($sharedDiagnosticJob in @(
        @{
            File = '.github/workflows/release.yml'
            Text = [string]$releaseJobTexts['unitypackage']
            Upload = 'Upload .unitypackage export diagnostics'
        },
        @{
            File = '.github/workflows/unity-tests.yml'
            Text = [string]$jobTexts['unitypackage-smoke']
            Upload = 'Upload Unity package export smoke diagnostics'
        }
    )) {
    $gateIndex = $sharedDiagnosticJob.Text.IndexOf('- name: Require confirmed Unity cleanup', [StringComparison]::Ordinal)
    $dumpIndex = $sharedDiagnosticJob.Text.IndexOf('- name: Dump Unity export log tail on failure or cancellation', [StringComparison]::Ordinal)
    $uploadIndex = $sharedDiagnosticJob.Text.IndexOf("- name: $($sharedDiagnosticJob.Upload)", [StringComparison]::Ordinal)
    $deleteIndex = $sharedDiagnosticJob.Text.IndexOf('- name: Delete private Unity cleanup evidence', [StringComparison]::Ordinal)
    if (
        $gateIndex -lt 0 -or
        $dumpIndex -le $gateIndex -or
        $uploadIndex -le $dumpIndex -or
        $deleteIndex -le $uploadIndex
    ) {
        $sharedDiagnosticEvidenceFailures += $sharedDiagnosticJob.File
    }
}
if ($sharedDiagnosticEvidenceFailures.Count -gt 0) {
    Write-Host "::error file=scripts/tests/test-unity-workflow-matrix-contract.ps1::Shared Unity export logs must remain available for failure diagnostics until after the final gate, dump, and upload steps. Failures=$($sharedDiagnosticEvidenceFailures -join ', ')."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info 'Checked shared Unity export evidence is deleted only after failure diagnostics.'
}

# The central cleanup policy is also *checked out* so its runtime can be diffed against ours. That
# ref is a `with:` input, which Dependabot never bumps, so a literal SHA there survives a bump and
# quietly makes the parity test validate the version we stopped using -- a silent pass, not a red
# check. Both the workflow and scripts/tests/test-portable-cleanup-classifier.js must derive it.
$policyCheckoutWorkflowPath = Join-Path $repoRoot '.github/workflows/pwsh-invocations-lint.yml'
if (-not (Test-Path -LiteralPath $policyCheckoutWorkflowPath)) {
    Write-Host "::error::PowerShell invocations lint workflow not found: $policyCheckoutWorkflowPath"
    exit 1
}
$policyCheckoutWorkflowContent = Get-Content -LiteralPath $policyCheckoutWorkflowPath -Raw
$policyPinIsDerived = (
    $policyCheckoutWorkflowContent -match '(?m)^\s+run:\s*\|\s*$[\s\S]*?node scripts/resolve-build-lock-pin\.js require-confirmed-unity-cleanup' -and
    $policyCheckoutWorkflowContent -match '(?m)^\s+ref:\s+\$\{\{ steps\.policy_pin\.outputs\.sha \}\}\s*$' -and
    $policyCheckoutWorkflowContent -notmatch '(?m)^\s+ref:\s+[0-9a-f]{40}\s*$'
)
if (-not $policyPinIsDerived) {
    Write-Host "::error file=.github/workflows/pwsh-invocations-lint.yml::The central cleanup policy checkout must take its ref from scripts/resolve-build-lock-pin.js via steps.policy_pin.outputs.sha, never a literal commit SHA."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info 'Checked the central cleanup policy checkout derives its commit from the workflow pins.'
}

$classifierParityTestPath = Join-Path $repoRoot 'scripts/tests/test-portable-cleanup-classifier.js'
if (-not (Test-Path -LiteralPath $classifierParityTestPath)) {
    Write-Host "::error::Central cleanup parity test not found: $classifierParityTestPath"
    exit 1
}
$classifierParityTestContent = Get-Content -LiteralPath $classifierParityTestPath -Raw
$classifierParityDerivesPin = (
    $classifierParityTestContent.Contains('resolveBuildLockPin("require-confirmed-unity-cleanup", root)') -and
    $classifierParityTestContent -notmatch '[0-9a-f]{40}'
)
if (-not $classifierParityDerivesPin) {
    Write-Host "::error file=scripts/tests/test-portable-cleanup-classifier.js::The central cleanup parity test must derive the pinned policy commit through resolveBuildLockPin, never restate a literal commit SHA."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info 'Checked the central cleanup parity test derives the pinned policy commit.'
}

$unityLockUsesAppCredentials = (
    (Test-UnityLockAppConfiguration -Content $workflowContent -WorkflowFile '.github/workflows/unity-tests.yml') -and
    (Test-UnityLockAppConfiguration -Content ($benchmarksWorkflowLines -join "`n") -WorkflowFile '.github/workflows/unity-benchmarks.yml') -and
    (Test-UnityLockAppConfiguration -Content ($releaseWorkflowLines -join "`n") -WorkflowFile '.github/workflows/release.yml')
)
if (-not $unityLockUsesAppCredentials) {
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity lock steps use matching runner identity and GitHub App credentials."
}

$legacyOrganizationLockToken = 'ORG_BUILD_LOCK_' + 'TOKEN'
$unityLockRunbookUsesAppCredentials = (
    $runnerRunbookContent.Contains('`BUILD_LOCK_APP_ID`') -and
    $runnerRunbookContent.Contains('`BUILD_LOCK_APP_PRIVATE_KEY`') -and
    -not $runnerRunbookContent.Contains($legacyOrganizationLockToken)
)
if (-not $unityLockRunbookUsesAppCredentials) {
    Write-Host "::error file=docs/runbooks/unity-runners-after-transfer.md::Unity runner runbook must provision both build-lock GitHub App secrets and must not reference the legacy organization PAT."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity runner runbook provisions GitHub App credentials for the organization build lock."
}

$slowReportBudgetCount = ([regex]::Matches($workflowContent, [regex]::Escape('-FixtureBudgetSeconds 120'))).Count
if ($slowReportBudgetCount -lt 3) {
    Write-Host "::error file=.github/workflows/unity-tests.yml::Unity slow-test reports must include a warn-only 120s fixture budget for main, standalone, and single-threaded legs."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity slow-test warn-only fixture budget contract."
}

for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^jobs:\s*$') {
        $insideJobs = $true
        continue
    }

    if (-not $insideJobs) {
        continue
    }

    $jobMatch = [regex]::Match($lines[$i], '^  ([A-Za-z0-9_-]+):\s*$')
    if (-not $jobMatch.Success) { continue }

    $jobId = $jobMatch.Groups[1].Value
    $start = $i
    $end = $lines.Count
    for ($j = $i + 1; $j -lt $lines.Count; $j++) {
        if ($lines[$j] -match '^  [A-Za-z0-9_-]+:\s*$') {
            $end = $j
            break
        }
    }

    [string[]]$jobLines = @($lines[$start..($end - 1)])
    [string]$jobText = $jobLines -join "`n"
    $jobTexts[$jobId] = $jobText
    [bool]$hasJobIf = $jobText -match '(?m)^    if:\s*'
    [bool]$hasMatrixPresenceGate = $hasJobIf -and $jobText -match "matrix-include[^`n]+!=\s*'\[\]'"
    [bool]$hasDynamicMatrixInclude = $jobText -match 'fromJSON\(needs\.[^)]+\.outputs\.matrix-include'
    [string[]]$jobNameLines = @($jobLines | Where-Object { $_ -match '^    name:\s*' })

    foreach ($jobNameLine in $jobNameLines) {
        if ($hasMatrixPresenceGate -and $hasDynamicMatrixInclude -and $jobNameLine -match '\$\{\{\s*matrix\.') {
            Write-Host "::error file=.github/workflows/unity-tests.yml,line=$($start + 1)::Job '$jobId' has a job-level if, a needs-derived dynamic matrix, and a matrix expression in its job name. Use a static job name; keep matrix values in step names, artifacts, or action labels."
            $failed = $true
        }
    }

    if ($VerboseOutput) {
        Write-Info "Checked job '$jobId' (matrix-presence-gate=$hasMatrixPresenceGate, dynamic-matrix=$hasDynamicMatrixInclude, job-name-lines=$($jobNameLines.Count))."
    }

    $i = $end - 1
}

if (-not $jobTexts.ContainsKey('unity-tests-single-threaded')) {
    Write-Host "::error file=.github/workflows/unity-tests.yml::Missing unity-tests-single-threaded job."
    $failed = $true
} else {
    $singleThreadedJob = $jobTexts['unity-tests-single-threaded']
    $requiredSingleThreadedContracts = @(
        @{
            Name = 'needs main Unity matrix'
            Pattern = '(?m)^      - unity-tests\s*$'
            Message = 'unity-tests-single-threaded must wait for unity-tests so same-workflow jobs do not contend for the org Unity lock.'
        },
        @{
            Name = 'needs standalone Unity tier'
            Pattern = '(?m)^      - unity-tests-standalone\s*$'
            Message = 'unity-tests-single-threaded must wait for unity-tests-standalone so same-workflow jobs do not contend for the org Unity lock after the fast tier.'
        },
        @{
            Name = 'uses always for skipped standalone'
            Pattern = 'always\(\)'
            Message = 'unity-tests-single-threaded must use always() so workflow_dispatch runs with a skipped standalone tier can still evaluate its result gate.'
        },
        @{
            Name = 'requires successful main Unity matrix'
            Pattern = "needs\.unity-tests\.result\s*==\s*'success'"
            Message = 'unity-tests-single-threaded must run only after unity-tests succeeds.'
        },
        @{
            Name = 'accepts skipped standalone tier'
            Pattern = "needs\.unity-tests-standalone\.result\s*==\s*'skipped'"
            Message = 'unity-tests-single-threaded must allow unity-tests-standalone to be skipped for single-mode dispatch pins.'
        }
    )

    foreach ($contract in $requiredSingleThreadedContracts) {
        if ($singleThreadedJob -notmatch $contract.Pattern) {
            Write-Host "::error file=.github/workflows/unity-tests.yml::Unity workflow contract failed ($($contract.Name)): $($contract.Message)"
            $failed = $true
        } elseif ($VerboseOutput) {
            Write-Info "Checked unity-tests-single-threaded contract '$($contract.Name)'."
        }
    }
}

if (-not $jobTexts.ContainsKey('unitypackage-smoke')) {
    Write-Host "::error file=.github/workflows/unity-tests.yml::Missing unitypackage-smoke job."
    $failed = $true
} else {
    $unitypackageSmokeJob = $jobTexts['unitypackage-smoke']
    $requiredUnitypackageSmokeContracts = @(
        @{
            Name = 'needs main Unity matrix'
            Pattern = '(?m)^      - unity-tests\s*$'
            Message = 'unitypackage-smoke must wait for unity-tests so package export smoke runs only after the standard matrix is green.'
        },
        @{
            Name = 'needs standalone Unity tier'
            Pattern = '(?m)^      - unity-tests-standalone\s*$'
            Message = 'unitypackage-smoke must wait for unity-tests-standalone so the export smoke does not race the standalone tier for the org Unity lock.'
        },
        @{
            Name = 'needs single-threaded Unity tier'
            Pattern = '(?m)^      - unity-tests-single-threaded\s*$'
            Message = 'unitypackage-smoke must wait for unity-tests-single-threaded so release payload smoke is the final Unity gate.'
        },
        @{
            Name = 'requires successful single-threaded Unity tier'
            Pattern = "needs\.unity-tests-single-threaded\.result\s*==\s*'success'"
            Message = 'unitypackage-smoke must run only after the single-threaded Unity tier succeeds.'
        },
        @{
            Name = 'runs the release exporter'
            Pattern = 'bash scripts/unity/export-unitypackage\.sh'
            Message = 'unitypackage-smoke must run scripts/unity/export-unitypackage.sh so Samples~ are staged as the release .unitypackage payload.'
        },
        @{
            Name = 'uses release Unity version'
            Pattern = [regex]::Escape('UNITY_VERSION="$(jq -r ''.release'' .github/unity-versions.json)"')
            Message = 'unitypackage-smoke must use the release Unity version source of truth.'
        },
        @{
            Name = 'uploads export diagnostics'
            Pattern = [regex]::Escape('unitypackage-smoke-diagnostics-${{ github.run_id }}-${{ github.run_attempt }}')
            Message = 'unitypackage-smoke must upload export diagnostics when the smoke export fails.'
        }
    )

    foreach ($contract in $requiredUnitypackageSmokeContracts) {
        if ($unitypackageSmokeJob -notmatch $contract.Pattern) {
            Write-Host "::error file=.github/workflows/unity-tests.yml::Unity workflow contract failed ($($contract.Name)): $($contract.Message)"
            $failed = $true
        } elseif ($VerboseOutput) {
            Write-Info "Checked unitypackage-smoke contract '$($contract.Name)'."
        }
    }
}

if ($failed) {
    exit 1
}

Write-Host "[test-unity-workflow-matrix-contract] OK: Unity workflow and runner contracts passed." -ForegroundColor Green
exit 0
