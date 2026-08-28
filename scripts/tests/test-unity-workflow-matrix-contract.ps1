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

function Test-WatchdogCadenceFitsRecoveryWindow {
    <#
    .SYNOPSIS
        Asserts the watchdog is delivered often enough to still repair what it repairs.
    .DESCRIPTION
        The #342 recovery -- a run GitHub reported cancelled with every step green -- is only
        available inside MAX_RERUN_AGE_SECONDS of the run finishing. The cron is a REQUEST, not a
        cadence: measured over 300 runs (#448), GitHub delivered 65.5 of 288 requested triggers a
        day, a median gap of 21.3 minutes against a five-minute cron. So the interval that matters
        is the requested one multiplied by the throttle factor, and comparing the cron alone to the
        window would pass a schedule whose real gaps swallow it.

        Fails closed on a schedule this cannot read, because an unparsed cron is an unchecked one.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][int]$ThrottleFactor
    )

    # EVERY schedule, not the first: a workflow with two crons is delivered on the union, and the
    # slowest one is what a reader would reason from. Quoting is accepted in all three YAML spellings
    # so a purely stylistic edit cannot redden this with a message about intervals.
    $cronMatches = [regex]::Matches($Content, '(?m)^\s*-\s*cron:\s*(?<quote>["'']?)(?<schedule>[^"''\r\n]+)\k<quote>\s*$')
    $windowMatch = [regex]::Match($Content, '(?m)^\s*MAX_RERUN_AGE_SECONDS:\s*["'']?(?<seconds>\d+)["'']?\s*$')
    if ($cronMatches.Count -eq 0 -or -not $windowMatch.Success) {
        return $false
    }

    $slowestSeconds = 0
    foreach ($cronMatch in $cronMatches) {
        $everyMatch = [regex]::Match($cronMatch.Groups['schedule'].Value.Trim(), '^\*/(?<minutes>\d+) \* \* \* \*$')
        if (-not $everyMatch.Success) {
            return $false
        }

        $minutes = [int]$everyMatch.Groups['minutes'].Value
        if ($minutes -lt 1) {
            return $false
        }

        $seconds = $minutes * 60
        if ($seconds -gt $slowestSeconds) {
            $slowestSeconds = $seconds
        }
    }

    $windowSeconds = [int]$windowMatch.Groups['seconds'].Value
    return ($slowestSeconds * $ThrottleFactor) -lt $windowSeconds
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
    $splitCleanupActions = @('classify-unity-cleanup-evidence')
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
        if ($name -in $splitCleanupActions -and $distinctReferences.Count -eq 2) {
            $versionedUsages = @($usages | Where-Object { $_.Comment -match '^v\d+\.\d+\.\d+$' })
            $legacyUsages = @($usages | Where-Object { [string]::IsNullOrWhiteSpace($_.Comment) })
            $versionedReferences = @(
                [string[]]@($versionedUsages | Select-Object -ExpandProperty Reference) |
                    Sort-Object -CaseSensitive -Unique
            )
            $legacyReferences = @(
                [string[]]@($legacyUsages | Select-Object -ExpandProperty Reference) |
                    Sort-Object -CaseSensitive -Unique
            )
            if (
                $versionedUsages.Count -gt 0 -and
                $legacyUsages.Count -gt 0 -and
                ($versionedUsages.Count + $legacyUsages.Count) -eq $usages.Count -and
                $versionedReferences.Count -eq 1 -and
                $legacyReferences.Count -eq 1
            ) {
                $pins[$name] = [pscustomobject]@{
                    Sha     = $versionedReferences[0]
                    Comment = $versionedUsages[0].Comment
                    Count   = $versionedUsages.Count
                }
                continue
            }
        }
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
    'ensure-unity-editor',
    'return-unity-license',
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
$centralEditorActionCommit = $buildLockPins['ensure-unity-editor'].Sha
$centralReturnActionCommit = $buildLockPins['return-unity-license'].Sha
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
$exportUnityPackagePath = Join-Path $repoRoot 'scripts/unity/export-unitypackage.sh'

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
if (-not (Test-Path -LiteralPath $exportUnityPackagePath)) {
    Write-Host "::error::Unity package export script not found: $exportUnityPackagePath"
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
    $legacyReturnUses = './.github/actions/return-unity-license'
    $centralReturnUses = "Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/return-unity-license@$centralReturnActionCommit"
    $centralClassifierUses = "Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/classify-unity-cleanup-evidence@$centralCleanupClassifierCommit"
    $legacyCleanupGate = 'if: ${{ always() && steps.unity_lock.outcome == ''success'' }}'
    $centralCleanupGate = 'if: ${{ always() && steps.unity_lock.outputs.acquired == ''true'' }}'
    $legacyReleaseGate = 'if: ${{ always() && (steps.unity_lock.outcome == ''success'' || steps.unity_lock.outcome == ''failure'' || steps.unity_lock.outcome == ''cancelled'') }}'
    $centralReleaseGate = 'if: always()'
    $acquireUsesLineSuffix = '[ \t]+# ' + [regex]::Escape($acquireBuildLockActionComment) + '[ \t]*\r?$'
    $buildLockUsesLineSuffix = '[ \t]+# ' + [regex]::Escape($buildLockActionVersion) + '[ \t]*\r?$'

    $acquirePattern = '(?m)- name: Acquire organization Unity lock\s*\r?\n\s+id:\s+unity_lock\s*\r?\n(?:[^\r\n]*\r?\n)*?\s+uses:\s+' + [regex]::Escape($acquireUses) + $acquireUsesLineSuffix
    $failures = @()

    foreach ($job in $Jobs.GetEnumerator()) {
        $jobText = [string]$job.Value
        $usesUnityLock = (
            $jobText.Contains($acquireUses) -or
            $jobText.Contains($releaseUses) -or
            $jobText.Contains($legacyReturnUses) -or
            $jobText.Contains($centralReturnUses)
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
        $usesCentralCleanup = $returnStep.Value.Contains($centralReturnUses)
        $requiredCleanupGate = if ($usesCentralCleanup) { $centralCleanupGate } else { $legacyCleanupGate }
        $requiredReleaseGate = if ($usesCentralCleanup) { $centralReleaseGate } else { $legacyReleaseGate }
        $returnPattern = if ($usesCentralCleanup) {
            '(?ms)- name: Return Unity license\s*\r?\n\s+id:\s+return_unity_license\s*\r?\n\s+' + [regex]::Escape($requiredCleanupGate) + '\s*\r?\n\s+timeout-minutes:\s+5\s*\r?\n\s+uses:\s+' + [regex]::Escape($centralReturnUses) + $buildLockUsesLineSuffix
        } else {
            '(?ms)- name: Return Unity license\s*\r?\n\s+id:\s+return_unity_license\s*\r?\n\s+' + [regex]::Escape($requiredCleanupGate) + '\s*\r?\n\s+timeout-minutes:\s+5\s*\r?\n\s+continue-on-error:\s+true\s*\r?\n\s+uses:\s+' + [regex]::Escape($legacyReturnUses)
        }
        $releasePattern = '(?m)- name: Release organization Unity lock\s*\r?\n\s+id:\s+release_unity_lock\s*\r?\n\s+' + [regex]::Escape($requiredReleaseGate) + '\s*\r?\n\s+timeout-minutes:\s+5\s*\r?\n\s+uses:\s+' + [regex]::Escape($releaseUses) + $buildLockUsesLineSuffix
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
        if (-not $usesCentralCleanup -and $returnStep.Success -and (
                $returnStep.Value -notmatch '(?m)^\s+prior-return-log-path:\s+\S.*$' -or
                $returnStep.Value -notmatch '(?ms)^\s+prior-command-succeeded:\s+(?:>-\s*\r?\n\s*)?\$\{\{\s+.+?\s+\}\}\s*(?=^\s+env:)'
            )) {
            $failures += "$($job.Key): return-unity-license must classify the licensed command's log and successful outcome"
        }
        if ($usesCentralCleanup -and (
                $returnStep.Value -notmatch '(?m)^\s+unity-version:\s+\$\{\{ matrix\.unity-version \}\}\s*$' -or
                $returnStep.Value -notmatch '(?m)^\s+tool-cache:\s+\$\{\{ runner\.tool_cache \}\}\s*$' -or
                $jobText -notmatch ('(?ms)- name: Classify Unity cleanup evidence\s*\r?\n\s+id:\s+cleanup_classification\s*\r?\n\s+' + [regex]::Escape($centralCleanupGate) + '.*?uses:\s+' + [regex]::Escape($centralClassifierUses) + $buildLockUsesLineSuffix + '.*?return-log-digest:\s+\$\{\{ steps\.return_unity_license\.outputs\.return-log-digest \}\}')
            )) {
            $failures += "$($job.Key): central Windows cleanup must bind the canonical editor version/tool cache and digest-bound classifier"
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
        $cleanupOutputStep = if ($usesCentralCleanup) { 'cleanup_classification' } else { 'return_unity_license' }
        if (
            $releaseStep.Value -notmatch "(?m)^\s+resource-cleanup-status:\s+\`$\{\{ steps\.$cleanupOutputStep\.outputs\.resource-cleanup-status \}\}\s*`$" -or
            $releaseStep.Value -notmatch "(?m)^\s+resource-health:\s+\`$\{\{ steps\.$cleanupOutputStep\.outputs\.resource-health \}\}\s*`$" -or
            $releaseStep.Value -notmatch "(?m)^\s+resource-reason:\s+\`$\{\{ steps\.$cleanupOutputStep\.outputs\.resource-reason \}\}\s*`$"
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
[string]$exportUnityPackageContent = Get-Content -LiteralPath $exportUnityPackagePath -Raw
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

$unityTestWorkflowsAvoidUnusedDependencies = (
    -not $workflowContent.Contains('lfs: true') -and
    -not $benchmarksWorkflowContent.Contains('lfs: true') -and
    -not $runCiTestsContent.Contains('com.unity.test-framework.performance') -and
    -not $runCiTestsContent.Contains('PerformanceFrameworkVersion')
)
if (-not $unityTestWorkflowsAvoidUnusedDependencies) {
    Write-Host '::error file=.github/workflows/unity-tests.yml::Unity test workflows must not fetch absent Git LFS objects or install the unused Unity performance-testing package; benchmarks use raw Stopwatch measurements.'
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info 'Checked Unity test workflows avoid unused Git LFS and performance-package dependencies.'
}

$benchmarkJob = if ($benchmarksJobTexts.ContainsKey('benchmarks')) { [string]$benchmarksJobTexts['benchmarks'] } else { '' }
$benchmarkInvocationCount = [regex]::Matches(
    $benchmarkJob,
    [regex]::Escape('./scripts/unity/run-ci-tests.ps1')
).Count
$benchmarkRunsThoroughRandomInSharedInvocation = (
    $benchmarkInvocationCount -eq 1 -and
    -not $benchmarkJob.Contains('Run Random suite at full sample count') -and
    -not $benchmarkJob.Contains('benchmarks-random') -and
    $benchmarkJob.Contains('UH_BENCHMARK_ASSEMBLIES: WallstopStudios.UnityHelpers.Tests.Runtime.Performance;WallstopStudios.UnityHelpers.Tests.Runtime.Random') -and
    $benchmarkJob.Contains('UH_UNITY_TEST_CATEGORY: "Performance;Stress;Fast"') -and
    $benchmarkJob.Contains('UH_RANDOM_SAMPLE_COUNT: "12750000"') -and
    $benchmarkJob.Contains('UH_RANDOM_NOISE_MAP_ITERATIONS: "1000"') -and
    $benchmarkJob.Contains('UH_EDITOR_TEST_TIMEOUT_SECONDS: "6600"') -and
    -not $benchmarkJob.Contains("matrix.test-mode == 'editmode'") -and
    $benchmarkJob.Contains('-AssemblyNames $env:UH_BENCHMARK_ASSEMBLIES')
)
if (-not $benchmarkRunsThoroughRandomInSharedInvocation) {
    Write-Host '::error file=.github/workflows/unity-benchmarks.yml::The benchmark leg must run Performance/Stress/Fast and the full-sample Random assembly in one Unity invocation with explicit assembly scoping, and must not branch its configuration on a test-mode that no longer exists.'
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info 'Checked weekly performance and thorough Random coverage share one Unity invocation.'
}

$benchmarkAssemblyDiscoveryIsCentralized = (
    $benchmarksJobTexts.ContainsKey('matrix-config') -and
    $benchmarksJobTexts['matrix-config'].Contains('- name: Setup Node.js for assembly discovery') -and
    $benchmarksJobTexts['matrix-config'].Contains('require("./scripts/unity/lib/asmdef-discovery.js")') -and
    $benchmarksJobTexts['matrix-config'].Contains('const performanceAssembly = "WallstopStudios.UnityHelpers.Tests.Runtime.Performance"') -and
    $benchmarksJobTexts['matrix-config'].Contains('const randomAssembly = "WallstopStudios.UnityHelpers.Tests.Runtime.Random"') -and
    $benchmarksJobTexts['matrix-config'].Contains('for (const required of [performanceAssembly, randomAssembly])') -and
    # PlayMode only, and asserted rather than assumed. Both benchmark assemblies are
    # platform-neutral, and Unity's EditMode runner takes only assemblies flagged
    # EditorAssembly -- so an editmode benchmark leg is handed two assembly names, runs zero
    # tests, and fails "Verify tests actually ran" on every scheduled run (#570).
    $benchmarksJobTexts['matrix-config'].Contains('for (const mode of ["playmode"])') -and
    -not $benchmarksJobTexts['matrix-config'].Contains('editmode: benchmarkProfile("editmode")') -and
    $benchmarksWorkflowContent.Contains('matrix-include: ${{ steps.resolve.outputs.matrix-include }}') -and
    $benchmarksWorkflowContent.Contains('expected-result-files: ${{ steps.resolve.outputs.expected-result-files }}') -and
    $benchmarksWorkflowContent.Contains('allow-baseline-refresh: ${{ steps.resolve.outputs.allow-baseline-refresh }}') -and
    $benchmarkJob.Contains('unity-version:') -and
    $benchmarkJob.Contains('- 2021.3.45f1') -and
    $benchmarkJob.Contains('- 2022.3.45f1') -and
    $benchmarkJob.Contains('- 6000.3.16f1') -and
    $benchmarkJob.Contains('- 6000.5.2f1') -and
    $benchmarkJob.Contains('- playmode') -and
    $benchmarksWorkflowContent.Contains('matrix-exclude: ${{ steps.resolve.outputs.matrix-exclude }}') -and
    $benchmarkJob.Contains('exclude: ${{ fromJSON(needs.matrix-config.outputs.matrix-exclude) }}') -and
    -not $benchmarkJob.Contains('include: ${{ fromJSON(needs.matrix-config.outputs.matrix-include) }}') -and
    $benchmarkJob.Contains('expected-empty: false') -and
    -not $benchmarkJob.Contains('actions/setup-node@') -and
    -not $benchmarkJob.Contains('./.github/actions/compute-unity-assemblies') -and
    -not $benchmarkJob.Contains('steps.compute')
)
if (-not $benchmarkAssemblyDiscoveryIsCentralized) {
    Write-Host '::error file=.github/workflows/unity-benchmarks.yml::Resolve and validate the exact non-empty Performance and Random benchmark profile in hosted matrix-config, use the reviewed static Unity-version/playmode matrix required by the central editor authority, and do not install Node or rediscover asmdefs on self-hosted legs.'
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info 'Checked benchmark assembly discovery is authoritative and centralized.'
}

$expectedResultFilesLines = @(
    $benchmarksWorkflowContent -split "`r?`n" |
        Where-Object { $_.Contains('echo "expected-result-files=') }
)
$expectedResultFilesFilter = ''
if ($expectedResultFilesLines.Count -eq 1) {
    $expectedResultFilesFilterMatch = [regex]::Match(
        $expectedResultFilesLines[0],
        "jq -c '([^']+)'"
    )
    if ($expectedResultFilesFilterMatch.Success) {
        $expectedResultFilesFilter = $expectedResultFilesFilterMatch.Groups[1].Value
    }
}
$expectedResultFilesFixture = '[{"result-file":"results-b.xml"},{"result-file":"results-a.xml"}]'
$expectedResultFilesOutput = if ([string]::IsNullOrWhiteSpace($expectedResultFilesFilter)) {
    @()
}
else {
    @($expectedResultFilesFixture | & jq -c $expectedResultFilesFilter 2>&1)
}
$expectedResultFilesFilterExitCode = if ([string]::IsNullOrWhiteSpace($expectedResultFilesFilter)) {
    -1
}
else {
    $LASTEXITCODE
}
$benchmarkExpectedResultFilesFilterIsExecutable = (
    $expectedResultFilesFilter -eq 'map(."result-file") | sort' -and
    $expectedResultFilesFilterExitCode -eq 0 -and
    ($expectedResultFilesOutput -join "`n").Trim() -eq '["results-a.xml","results-b.xml"]'
)
if (-not $benchmarkExpectedResultFilesFilterIsExecutable) {
    Write-Host '::error file=.github/workflows/unity-benchmarks.yml::The expected-result-files jq program must be extracted exactly once, execute successfully with a hyphenated result-file key, and return sorted identities. Do not backslash-escape double quotes inside its single-quoted jq program.'
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info 'Executed the benchmark expected-result-files jq program against a hyphenated-key fixture.'
}

$benchmarkBaselineRequiresCompleteFreshMatrix = (
    $benchmarksWorkflowContent.Contains('expected-result-count: ${{ steps.resolve.outputs.expected-result-count }}') -and
    $benchmarksWorkflowContent.Contains('BENCHMARKS_RESULT: ${{ needs.benchmarks.result }}') -and
    $benchmarksWorkflowContent.Contains('ALLOW_BASELINE_REFRESH: ${{ needs.matrix-config.outputs.allow-baseline-refresh }}') -and
    $benchmarksWorkflowContent.Contains('EXPECTED_RESULT_FILES: ${{ needs.matrix-config.outputs.expected-result-files }}') -and
    $benchmarksWorkflowContent.Contains('scripts/unity/lib/evaluate-perf-refresh.js') -and
    $benchmarksWorkflowContent.Contains('--benchmark-result "${BENCHMARKS_RESULT}"') -and
    $benchmarksWorkflowContent.Contains('--allow-baseline-refresh "${ALLOW_BASELINE_REFRESH}"') -and
    $benchmarksWorkflowContent.Contains('--expected-files "${EXPECTED_RESULT_FILES}"') -and
    $benchmarksWorkflowContent.Contains('complete_matrix="$(jq -r ''.complete'' "${decision}")"') -and
    $benchmarksWorkflowContent.Contains('complete-matrix=true') -and
    $benchmarksWorkflowContent.Contains('complete-matrix=false') -and
    $benchmarksWorkflowContent.Contains('partial-${result_file}') -and
    $benchmarksWorkflowContent.Contains('invalid_successful_matrix') -and
    $benchmarksWorkflowContent.Contains('- name: Upload invalid matrix diagnostics') -and
    $benchmarksWorkflowContent.Contains("if: `${{ always() && steps.assemble.outcome == 'failure' }}") -and
    $benchmarksWorkflowContent.Contains('path: perf-results/partial-results-*.xml') -and
    $benchmarksWorkflowContent.Contains("if: `${{ steps.assemble.outputs.complete-matrix == 'true' }}") -and
    $benchmarksWorkflowContent.Contains('--require-complete-baseline') -and
    $benchmarksWorkflowContent.Contains('--update-baseline perf-results/baseline.json')
)
if (-not $benchmarkBaselineRequiresCompleteFreshMatrix) {
    Write-Host '::error file=.github/workflows/unity-benchmarks.yml::Advance the canonical result/report/baseline only after a successful benchmark aggregate with every exact expected result identity, per-file metrics, and no removed baseline keys; retain failures separately as partial diagnostics.'
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info 'Checked partial benchmark runs cannot mix stale XML into a new rolling baseline.'
}

if ($exportUnityPackageContent -notmatch '(?m)^\s+-releaseCodeOptimization\s+\\\s*$') {
    Write-Host '::error file=scripts/unity/export-unitypackage.sh::The release payload compile must pass -releaseCodeOptimization so the unitypackage smoke covers the same optimized assembly contract as Unity CI.'
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info 'Checked unitypackage export compiles optimized release assemblies.'
}

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

# Every Unity version CI actually tests has to be selectable when someone files a bug against it.
# The dropdowns are hand-maintained and drifted: 6000.1 and 6000.2 were offered while 6000.3 and
# 6000.5 -- both in the matrix -- were not, so a report against a tested version had to say "Other"
# and lost the one field that makes a repro reproducible (#283).
foreach ($templateName in @('bug_report.yml', 'feature_request.yml')) {
    $templatePath = Join-Path $repoRoot ".github/ISSUE_TEMPLATE/$templateName"
    if (-not (Test-Path -LiteralPath $templatePath)) {
        Write-Host "::error file=.github/ISSUE_TEMPLATE/$templateName::Issue template is missing."
        $failed = $true
        continue
    }

    $templateText = Get-Content -LiteralPath $templatePath -Raw
    foreach ($unityVersion in $unityVersions) {
        # "6000.5.2f1" is offered to users as "Unity 6.5 (6000.5)"; match on the stream, which is
        # what the label carries, rather than on the patch the matrix pins.
        $stream = ($unityVersion -split '\.')[0..1] -join '.'
        if ($templateText -notmatch [regex]::Escape($stream)) {
            Write-Host "::error file=.github/ISSUE_TEMPLATE/$templateName::Unity $stream is in .github/unity-versions.json but is not selectable in the Unity Version dropdown, so a report against a version CI tests cannot name it."
            $failed = $true
        }
    }
}

if ($VerboseOutput -and -not $failed) {
    Write-Info "Checked every Unity version in the source of truth is selectable in both issue templates."
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
    $runCiTestsContent.Contains('function Get-UnityCompilationSourceInventory') -and
    $runCiTestsContent.Contains('function Write-UnityCompilationSourceInventoryMarker') -and
    $runCiTestsContent.Contains("foreach (`$relativeRoot in @('Runtime', 'Editor', 'Styles', 'Tests'))") -and
    $runCiTestsContent.Contains('function Test-UnityCompilationCacheRepoRootMatch') -and
    $runCiTestsContent.Contains('[System.StringComparison]::OrdinalIgnoreCase') -and
    $runCiTestsContent.Contains('.unity-helpers-repo-root.txt') -and
    $runCiTestsContent.Contains('.unity-helpers-source-inventory.txt') -and
    $runCiTestsContent.Contains('Clear-StaleUnityCompilationCache -Project $ProjectPath -RepoRoot $RepoRoot') -and
    $runCiTestsContent.Contains('Write-UnityCompilationSourceInventoryMarker -Project $ProjectPath -RepoRoot $RepoRoot') -and
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
            'Get-UnityCompilationSourceInventory',
            'Write-UnityCompilationSourceInventoryMarker',
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
    param(
        [string]$MarkerValue,
        [string]$SourceMarkerValue
    )

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

    $sourceMarkerPath = Join-Path $library '.unity-helpers-source-inventory.txt'
    if ($PSBoundParameters.ContainsKey('SourceMarkerValue')) {
        Set-Content -LiteralPath $sourceMarkerPath -Value $SourceMarkerValue -Encoding utf8
    }

    [pscustomobject]@{
        Root = $root
        Project = $project
        Library = $library
        MarkerPath = $markerPath
        SourceMarkerPath = $sourceMarkerPath
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
        $matchingRuntimeRoot = Join-Path $matchingMarkerRoot 'Runtime'
        New-Item -ItemType Directory -Force -Path $matchingRuntimeRoot | Out-Null
        Set-Content -LiteralPath (Join-Path $matchingRuntimeRoot 'Existing.cs') -Value 'class Existing {}' -Encoding utf8
        $matchingMarkerValue = Get-NormalizedContractRoot -Path $matchingMarkerRoot
        $matchingSourceInventory = Get-UnityCompilationSourceInventory -RepoRoot $matchingMarkerRoot
        $matchingMarkerFixture = New-UnityCompilationCacheFixture `
            -MarkerValue $matchingMarkerValue `
            -SourceMarkerValue $matchingSourceInventory
        $fixtures += $matchingMarkerFixture
        Clear-StaleUnityCompilationCache -Project $matchingMarkerFixture.Project -RepoRoot $matchingMarkerRoot
        $matchingMarkerAfter = (Get-Content -LiteralPath $matchingMarkerFixture.MarkerPath -Raw).Trim()
        if (-not (Test-CompilationCacheSentinelsPresent -Fixture $matchingMarkerFixture) -or
            -not (Test-Path -LiteralPath $matchingMarkerFixture.PackageCacheSentinel -PathType Leaf) -or
            $matchingMarkerAfter -cne $matchingMarkerValue) {
            return 'matching marker must preserve compilation-cache sentinels, PackageCache, and marker contents'
        }

        # Content-only changes are normal on every commit and Unity tracks them itself. Preserve
        # the warm compilation cache when the source path set is unchanged.
        Set-Content -LiteralPath (Join-Path $matchingRuntimeRoot 'Existing.cs') -Value 'class Existing { int Value; }' -Encoding utf8
        Clear-StaleUnityCompilationCache -Project $matchingMarkerFixture.Project -RepoRoot $matchingMarkerRoot
        if (-not (Test-CompilationCacheSentinelsPresent -Fixture $matchingMarkerFixture)) {
            return 'content-only source changes must preserve compilation caches to retain normal warm-build performance'
        }

        # A newly added source path is different: Unity 2022 can begin Bee compilation before a
        # warm persistent project's AssetDatabase includes the new package file, leaving sibling
        # sources to compile against a missing type. Invalidate only compilation outputs so the
        # source graph is rebuilt while PackageCache remains warm.
        Set-Content -LiteralPath (Join-Path $matchingRuntimeRoot 'Added.cs') -Value 'class Added {}' -Encoding utf8
        Clear-StaleUnityCompilationCache -Project $matchingMarkerFixture.Project -RepoRoot $matchingMarkerRoot
        if (-not (Test-CompilationCacheDirsAbsent -Fixture $matchingMarkerFixture) -or
            -not (Test-Path -LiteralPath $matchingMarkerFixture.PackageCacheSentinel -PathType Leaf)) {
            return 'an added package source path must invalidate compilation outputs while preserving PackageCache'
        }

        $storedInventoryBeforeSuccess = (Get-Content -LiteralPath $matchingMarkerFixture.SourceMarkerPath -Raw).Trim()
        if ($storedInventoryBeforeSuccess -cne $matchingSourceInventory) {
            return 'source-inventory invalidation must retain the last successful inventory until Unity succeeds'
        }

        # Model a failed Unity attempt recreating compilation outputs without valid NUnit
        # results. Because the marker is still old, the retry must invalidate them again.
        foreach ($directory in $script:compilationCacheDirectories) {
            $retryCachePath = Join-Path $matchingMarkerFixture.Library $directory
            New-Item -ItemType Directory -Force -Path $retryCachePath | Out-Null
            Set-Content -LiteralPath (Join-Path $retryCachePath 'failed-attempt.txt') -Value 'stale' -Encoding utf8
        }
        Clear-StaleUnityCompilationCache -Project $matchingMarkerFixture.Project -RepoRoot $matchingMarkerRoot
        if (-not (Test-CompilationCacheDirsAbsent -Fixture $matchingMarkerFixture)) {
            return 'a failed Unity attempt must leave the old inventory so the retry invalidates compilation outputs again'
        }

        Write-UnityCompilationSourceInventoryMarker `
            -Project $matchingMarkerFixture.Project `
            -RepoRoot $matchingMarkerRoot
        $updatedInventory = Get-UnityCompilationSourceInventory -RepoRoot $matchingMarkerRoot
        $storedInventoryAfterSuccess = (Get-Content -LiteralPath $matchingMarkerFixture.SourceMarkerPath -Raw).Trim()
        if ($storedInventoryAfterSuccess -cne $updatedInventory) {
            return 'a successful Unity run must persist the new normalized source inventory'
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
    return (
        $stepText -match 'if:\s*\$\{\{\s*steps\.compute\.outputs\.is-empty\s*!=\s*''true''\s*\}\}' -or
        $stepText -match 'if:\s*\$\{\{\s*matrix\.is-empty\s*!=\s*true\s*\}\}'
    )
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

function Test-UnityJobUsesCentralEditorGate {
    param(
        [Parameter(Mandatory = $true)][string]$JobText,
        [Parameter(Mandatory = $true)][string]$ProvisioningProfile
    )

    $editorUses = "Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/ensure-unity-editor@$centralEditorActionCommit"
    $editorIndex = $JobText.IndexOf('- name: Require manually installed Unity editor', [StringComparison]::Ordinal)
    $checkoutIndex = $JobText.IndexOf('- name: Checkout', [StringComparison]::Ordinal)
    $acquireIndex = $JobText.IndexOf('- name: Acquire organization Unity lock', [StringComparison]::Ordinal)
    $licenseValidationIndex = $JobText.IndexOf('- name: Validate Unity license secrets', [StringComparison]::Ordinal)
    $firstStep = [regex]::Match($JobText, '(?m)^      - name: (?<name>[^\r\n]+)')
    $secondStep = if ($firstStep.Success) {
        [regex]::Match($JobText.Substring($firstStep.Index + $firstStep.Length), '(?m)^      - name: (?<name>[^\r\n]+)')
    } else {
        [System.Text.RegularExpressions.Match]::Empty
    }
    $trustedPrefixIsClosed = (
        $firstStep.Success -and
        (
            $firstStep.Groups['name'].Value -eq 'Require manually installed Unity editor' -or
            (
                $firstStep.Groups['name'].Value -eq 'Require current PR head before setup' -and
                $secondStep.Success -and
                $secondStep.Groups['name'].Value -eq 'Require manually installed Unity editor'
            )
        )
    )
    $editorStep = [regex]::Match(
        $JobText,
        '(?ms)^\s+- name: Require manually installed Unity editor\s*$.*?(?=^\s+- name:|\z)'
    )

    return (
        $editorIndex -ge 0 -and
        $trustedPrefixIsClosed -and
        ($checkoutIndex -lt 0 -or $editorIndex -lt $checkoutIndex) -and
        ($licenseValidationIndex -lt 0 -or $editorIndex -lt $licenseValidationIndex) -and
        ($acquireIndex -lt 0 -or $editorIndex -lt $acquireIndex) -and
        $editorStep.Value -match '(?m)^\s+id:\s+ensure_unity_editor\s*$' -and
        $editorStep.Value -match '(?m)^\s+timeout-minutes:\s+10\s*$' -and
        $editorStep.Value.Contains("uses: $editorUses") -and
        $editorStep.Value -match '(?m)^\s+unity-version:\s+\$\{\{ matrix\.unity-version \}\}\s*$' -and
        $editorStep.Value -match '(?m)^\s+install-root:\s+\$\{\{ runner\.tool_cache \}\}\\u6-v3\s*$' -and
        $editorStep.Value -match ('(?m)^\s+provisioning-profile:\s+' + [regex]::Escape($ProvisioningProfile) + '\s*$') -and
        $editorStep.Value -match '(?m)^\s+diagnostics-path:\s+unity-editor-check\.json\s*$' -and
        $editorStep.Value -match '(?m)^\s+ci-managed-only:\s+true\s*$' -and
        $editorStep.Value -match '(?m)^\s+require-healthy-existing:\s+true\s*$' -and
        $editorStep.Value -notmatch '(?m)^\s+if:' -and
        $JobText.Contains('UNITY_EDITOR_PATH: ${{ steps.ensure_unity_editor.outputs.editor-path }}') -and
        -not $JobText.Contains('- name: Maintain Unity editor on selected runner') -and
        -not $JobText.Contains('- name: Provision Unity Editor') -and
        -not $JobText.Contains('UNITY_EDITOR_PATH=$editor') -and
        $JobText -match '(?m)^\s+timeout-minutes:\s*1200\s*$'
    )
}

$unityTestsMatrixJob = if ($jobTexts.ContainsKey('unity-tests')) { $jobTexts['unity-tests'] } else { '' }
$unityTestsStandaloneJob = if ($jobTexts.ContainsKey('unity-tests-standalone')) { $jobTexts['unity-tests-standalone'] } else { '' }
$unityTestsSingleThreadedJob = if ($jobTexts.ContainsKey('unity-tests-single-threaded')) { $jobTexts['unity-tests-single-threaded'] } else { '' }
$benchmarksMatrixJob = if ($benchmarksJobTexts.ContainsKey('benchmarks')) { $benchmarksJobTexts['benchmarks'] } else { '' }
$matrixConfigAssemblyDiscoveryIsCentralized = (
    $jobTexts.ContainsKey('matrix-config') -and
    $jobTexts['matrix-config'].Contains('- name: Setup Node.js for assembly discovery') -and
    $jobTexts['matrix-config'].Contains('- name: Resolve Unity test assembly lists') -and
    $jobTexts['matrix-config'].Contains('require("./scripts/unity/lib/asmdef-discovery.js")') -and
    $jobTexts['matrix-config'].Contains('editmode_integrations') -and
    $jobTexts['matrix-config'].Contains('playmode_integrations') -and
    $jobTexts['matrix-config'].Contains('standalone_integrations') -and
    $jobTexts['matrix-config'].Contains('editmode_core: { target: "editmode", editorOnly: true }') -and
    $jobTexts['matrix-config'].Contains('playmode_core') -and
    $workflowContent.Contains('editmode-integration-assemblies: ${{ steps.assemblies.outputs.editmode_integrations }}') -and
    $workflowContent.Contains('playmode-integration-assemblies: ${{ steps.assemblies.outputs.playmode_integrations }}') -and
    $workflowContent.Contains('standalone-integration-assemblies: ${{ steps.assemblies.outputs.standalone_integrations }}') -and
    $workflowContent.Contains('editmode-core-assemblies: ${{ steps.assemblies.outputs.editmode_core }}') -and
    $workflowContent.Contains('playmode-core-assemblies: ${{ steps.assemblies.outputs.playmode_core }}') -and
    $workflowContent.Contains('integration-assembly-profiles: ${{ steps.assemblies.outputs.integration_profiles }}') -and
    $workflowContent.Contains('core-assembly-profiles: ${{ steps.assemblies.outputs.core_profiles }}') -and
    $workflowContent.Contains('matrix-exclude: ${{ steps.resolve.outputs.matrix-exclude }}') -and
    $workflowContent.Contains('matrix-exclude-standalone: ${{ steps.resolve.outputs.matrix-exclude-standalone }}') -and
    $unityTestsMatrixJob.Contains('exclude: ${{ fromJSON(needs.matrix-config.outputs.matrix-exclude) }}') -and
    $unityTestsStandaloneJob.Contains('exclude: ${{ fromJSON(needs.matrix-config.outputs.matrix-exclude-standalone) }}') -and
    $unityTestsMatrixJob.Contains('- standalone') -and
    $unityTestsMatrixJob.Contains('UH_TEST_ASSEMBLIES: ${{ fromJSON(needs.matrix-config.outputs.integration-assembly-profiles)[matrix.test-mode] }}') -and
    $unityTestsStandaloneJob.Contains('UH_TEST_ASSEMBLIES: ${{ fromJSON(needs.matrix-config.outputs.integration-assembly-profiles)[matrix.test-mode] }}') -and
    $unityTestsSingleThreadedJob.Contains('UH_TEST_ASSEMBLIES: ${{ fromJSON(needs.matrix-config.outputs.core-assembly-profiles)[matrix.test-mode] }}') -and
    -not $unityTestsMatrixJob.Contains('actions/setup-node@') -and
    -not $unityTestsStandaloneJob.Contains('actions/setup-node@') -and
    -not $unityTestsSingleThreadedJob.Contains('actions/setup-node@') -and
    -not $unityTestsMatrixJob.Contains('./.github/actions/compute-unity-assemblies') -and
    -not $unityTestsStandaloneJob.Contains('./.github/actions/compute-unity-assemblies') -and
    -not $unityTestsSingleThreadedJob.Contains('./.github/actions/compute-unity-assemblies')
)
if (-not $matrixConfigAssemblyDiscoveryIsCentralized) {
    Write-Host "::error file=.github/workflows/unity-tests.yml::Compute integration and core assembly profiles once in hosted matrix-config, bind those outputs into the reviewed static Unity-version/test-mode matrices, and do not install Node or run asmdef discovery again on self-hosted Unity test legs."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity test assembly discovery is centralized on the hosted matrix job."
}

$trustedEditorMatrixProfile = '${{ fromJSON(''{"editmode":"EditorOnly","playmode":"EditorOnly","standalone":"StandaloneWindowsIl2Cpp"}'')[matrix.test-mode] }}'
$unityWorkflowsUseCentralEditorAuthority = (
    -not $jobTexts.ContainsKey('runner-maintenance') -and
    -not $benchmarksJobTexts.ContainsKey('runner-maintenance') -and
    $runnerBootstrapContent -match '(?m)^\s+UNITY_EDITOR_INSTALL_ROOT:\s+\$\{\{ runner\.tool_cache \}\}\\u6-v3\s*$' -and
    (Test-UnityJobUsesCentralEditorGate -JobText $unityTestsMatrixJob -ProvisioningProfile $trustedEditorMatrixProfile) -and
    (Test-UnityJobUsesCentralEditorGate -JobText $unityTestsStandaloneJob -ProvisioningProfile $trustedEditorMatrixProfile) -and
    (Test-UnityJobUsesCentralEditorGate -JobText $unityTestsSingleThreadedJob -ProvisioningProfile 'EditorOnly') -and
    (Test-UnityJobUsesCentralEditorGate -JobText $benchmarksMatrixJob -ProvisioningProfile 'EditorOnly')
)
if (-not $unityWorkflowsUseCentralEditorAuthority) {
    Write-Host "::error file=.github/workflows/unity-tests.yml::Every Windows licensed job must run the exact central ensure-unity-editor action first, or immediately after the immutable current-head guard, with a ten-minute fail-closed health check under the runner tool cache. CI must not maintain or provision editors, the Unity command must consume the action's bound editor-path output, and the operator bootstrap must provision the same runner.tool_cache\\u6-v3 root. Keep .github/workflows/unity-benchmarks.yml in sync."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Windows Unity workflows use the central editor authority before repository-controlled code."
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
# recent Unity Tests runs, 109 min of restore and 119 min of licensed self-hosted
# runner time.
#
# Two halves, both easy to undo by accident, so both are pinned:
#   1. Every leg that runs run-ci-tests.ps1 passes -ProjectRoot under
#      RUNNER_WORKSPACE -- the checkout's PARENT, which `git clean` cannot reach.
#   2. No Unity workflow caches a workspace-relative Library again.
#
# The workflows are DISCOVERED, not listed (#445). Naming unity-tests.yml and
# unity-benchmarks.yml stated where the requirement lives rather than what it is,
# and the quiet failure that invites is a THIRD workflow generating a Unity
# project that nothing checks. Comments are stripped first, because the slimmed
# workflows now explain in prose where their jobs went, and a workflow that
# mentions run-ci-tests.ps1 in a comment does not run it.
# ---------------------------------------------------------------------------
$unityWorkflowFilesWithProjects = @(
    Get-ChildItem -LiteralPath (Join-Path $repoRoot '.github/workflows') -Filter '*.yml' -File |
        Sort-Object -Property Name |
        Where-Object {
            $uncommented = ((Get-Content -LiteralPath $_.FullName) |
                    Where-Object { $_ -notmatch '^\s*#' }) -join "`n"
            $uncommented.Contains('./scripts/unity/run-ci-tests.ps1')
        } |
        ForEach-Object { ".github/workflows/$($_.Name)" }
)
if ($unityWorkflowFilesWithProjects.Count -eq 0) {
    # Without this the contract passes by protecting nothing the moment the invocation is spelled
    # differently, which is the failure mode discovery is supposed to close rather than introduce.
    Write-Host '::error file=.github/workflows/unity-tests.yml::No workflow invokes ./scripts/unity/run-ci-tests.ps1, so the persistent-project-root contract is checking nothing. If the invocation was renamed, update this discovery.'
    $failed = $true
}
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
# back up: a 91-minute wait has been measured behind a stuck licensed leg, so an
# oversized step clock strands its runner and delays queued organization work.
# ---------------------------------------------------------------------------
$unityRunTimeoutContracts = @(
    @{
        Name = 'default matrix + standalone run timeout'
        Pattern = 'timeout-minutes:\s*\$\{\{\s*\(matrix\.test-mode\s*==\s*''standalone''\s*&&\s*60\)\s*\|\|\s*40\s*\}\}'
        Message = "The default and standalone 'Run Unity Test Runner' steps must cap at 60 min (standalone) / 40 min (editmode, playmode). Measured worst case is 8.7-9.1 min; a larger cap only lengthens how long a hang strands a licensed runner and delays queued organization work."
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
        Write-Host "::error file=.github/workflows/unity-tests.yml::A 'Run Unity Test Runner' step declares timeout-minutes: $declaredTimeout. The measured worst case is 9.1 min; anything above 60 lets a hung leg strand a licensed runner and delay queued organization work for more than an hour."
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
    },
    @{
        # Dependabot runs draw from a separate secret store, so
        # BUILD_LOCK_READER_APP_ID arrived empty and the availability action failed
        # closed with "Missing required input" -- permanently red on every
        # dependency PR, while matrix-config had already skipped the licensed tiers
        # those credentials exist to serve. The `secrets` context is unavailable in
        # a job-level `if:`, so the guard has to be a step.
        Name = 'runner-preflight skips the fleet probe when the reader credentials are absent'
        Ok = (
            $unityTestsRunnerPreflightJob -match '(?m)^\s+id:\s*credentials\s*$' -and
            $unityTestsRunnerPreflightJob.Contains('READER_APP_ID: ${{ secrets.BUILD_LOCK_READER_APP_ID }}') -and
            $unityTestsRunnerPreflightJob -match "(?m)^\s+if:\s*\`$\{\{\s*steps\.credentials\.outputs\.present\s*==\s*'true'\s*\}\}\s*$"
        )
        Message = 'runner-preflight must detect whether this run carries build-lock reader credentials and skip the fleet probe when it does not. Probing a runner fleet no licensed leg is going to be dispatched to is not a gate, it is a guaranteed red on every Dependabot pull request.'
    },
    @{
        Name = 'Unity CI Success reports a run without Unity credentials instead of failing it'
        Ok = (
            $workflowContent.Contains('HAS_REQUIRED_SECRETS: ${{ needs.matrix-config.outputs.has-required-secrets }}') -and
            $workflowContent -match '(?m)^          if \[ "\$\{HAS_REQUIRED_SECRETS\}" != "true" \]; then\s*$' -and
            $workflowContent.Contains('## Unity validation did not run')
        )
        Message = 'unity-ci-success must have an explicit branch for a run with no Unity credentials, and its step summary must say plainly that no Unity test executed. Without it the verdict demands success from licensed jobs that were skipped by design.'
    },
    @{
        # The branch above is a waiver only if it asserts nothing; these two errors
        # are what keep it an assertion. A licensed job that reported anything but
        # `skipped` on a credential-less run means a leg ran that could not have.
        Name = 'the no-credentials branch still asserts the hosted gates and the skips'
        Ok = (
            $workflowContent.Contains('Run without Unity credentials, but a hosted gate did not pass') -and
            $workflowContent.Contains('Run without Unity credentials, but a licensed job did not skip')
        )
        Message = 'The no-credentials branch of unity-ci-success must require both hosted gates to have passed and every licensed job to have skipped. Exiting 0 without those checks would report green for a broken gate.'
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
            Message = 'Automatic re-runs must be bounded twice: by run_attempt (a re-run that lands in the same cancelled state escalates to a human instead of looping on every delivered tick) and by a per-run daily cap in its own state file. The cap file must be distinct from the cancel state file, whose reader falls back to a .reruns key and would otherwise consume the cancel budget.'
            File = '.github/workflows/stuck-job-watchdog.yml'
        },
        @{
            # Measured over 300 runs (#448): 288 cron requests a day produce 65.5
            # delivered runs, a median gap of 21.3 min and a maximum of 58.5. So the
            # requested interval understates the real one by roughly four times, and a
            # cadence chosen against the cron alone would silently exceed the only
            # window in which an all-green cancelled run (#342) can still be recovered.
            Name = 'watchdog cadence leaves room inside the re-run recovery window'
            Ok = (Test-WatchdogCadenceFitsRecoveryWindow -Content $watchdogContent -ThrottleFactor 4)
            Message = 'The watchdog schedule must satisfy (slowest cron interval x 4) < MAX_RERUN_AGE_SECONDS. GitHub delivers roughly one run per four requested ticks, so a cron of 15 minutes is already an hour of real latency -- and at an hour a run cancelled with every step green ages out of the recovery window between two ticks, which is the one failure this workflow exists to repair automatically. Every schedule must be written as "*/N * * * *" (quoted or not) with N of at least 1, because that is the only form whose delivered gap this can reason about; a fixed-time cron fails closed here rather than being waved through.'
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

# `max-parallel: 2` used to be pinned here on the theory that it matched the two
# Unity runners. It does not gate execution -- a self-hosted runner already takes
# one job at a time -- it gates *eligibility*, so every completion became a fresh
# dispatch transition, and dispatch to a self-hosted runner is the slow part.
# Measured on run 31130259492 (main, green): 16 legs, 70.9 min of work on 2
# runners against an 88 min wall clock, with a runner idle for 13.4 min while four
# legs sat queued. Sibling repos were not on the runners in that window and the
# organization Unity lock acquired in 2-4 s on every leg, so the cap is the only
# remaining explanation. The contract is now the inverse: no cap.
$unityMatrixJobsWithParallelismCap = @()
foreach ($capJob in @('unity-tests', 'unity-tests-standalone', 'unity-tests-single-threaded')) {
    if (-not $jobTexts.ContainsKey($capJob)) {
        $unityMatrixJobsWithParallelismCap += "$capJob (missing)"
    }
    elseif ([string]$jobTexts[$capJob] -match '(?m)^\s+max-parallel:') {
        $unityMatrixJobsWithParallelismCap += $capJob
    }
}
if (-not $benchmarksJobTexts.ContainsKey('benchmarks')) {
    $unityMatrixJobsWithParallelismCap += 'benchmarks (missing)'
}
elseif ([string]$benchmarksJobTexts['benchmarks'] -match '(?m)^\s+max-parallel:') {
    $unityMatrixJobsWithParallelismCap += 'benchmarks'
}
if ($unityMatrixJobsWithParallelismCap.Count -gt 0) {
    Write-Host "::error file=.github/workflows/unity-tests.yml::Unity self-hosted matrix jobs must not cap max-parallel: it limits matrix eligibility rather than execution, so each completion becomes a slow self-hosted dispatch transition and both runners idle with legs queued. Concurrency is already bounded by the runner pool and the organization Unity lock. Offending: $($unityMatrixJobsWithParallelismCap -join ', '). Keep .github/workflows/unity-benchmarks.yml in sync."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity matrix jobs leave every leg immediately eligible for the self-hosted runners."
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
            -LicensedWorkStepNames @{ benchmarks = 'Run Unity Test Runner' }) -and
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

$centralReturnUses = "Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/return-unity-license@$centralReturnActionCommit"
$testCentralReturnCalls = [regex]::Matches($workflowContent, [regex]::Escape("uses: $centralReturnUses")).Count
$benchmarkCentralReturnCalls = [regex]::Matches(($benchmarksWorkflowLines -join "`n"), [regex]::Escape("uses: $centralReturnUses")).Count
if ($testCentralReturnCalls -ne 3 -or $benchmarkCentralReturnCalls -ne 1) {
    Write-Host "::error file=scripts/tests/test-unity-workflow-matrix-contract.ps1::All four Windows licensed callers must use the immutable central return executor (tests=$testCentralReturnCalls, benchmarks=$benchmarkCentralReturnCalls)."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info 'Checked all Windows licensed callers use the central return executor.'
}

$legacyClassifierReferences = @(
    [regex]::Matches(
        $returnUnityLicenseActionContent,
        '(?m)^\s+uses:\s+Ambiguous-Interactive/ambiguous-organization-build-lock/\.github/actions/classify-unity-cleanup-evidence@(?<ref>[0-9a-f]{40})\s*$'
    ) | ForEach-Object { $_.Groups['ref'].Value } | Sort-Object -CaseSensitive -Unique
)
$legacyClassifierUses = if ($legacyClassifierReferences.Count -eq 1) {
    "Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/classify-unity-cleanup-evidence@$($legacyClassifierReferences[0])"
} else {
    ''
}
$returnActionResourceProofContract = (
    $legacyClassifierReferences.Count -eq 1 -and
    [regex]::Matches($returnUnityLicenseActionContent, [regex]::Escape("uses: $legacyClassifierUses")).Count -eq 2 -and
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
$centralClassifierUses = "Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/classify-unity-cleanup-evidence@$centralCleanupClassifierCommit"
$legacyGateUses = $centralGateUses
$centralLifecycleFailures = @()
$licensedLifecycleCount = 0
$centralLifecycleCount = 0
$legacyLifecycleCount = 0
foreach ($workflowJobSet in $licensedWorkflowJobSets) {
    foreach ($job in $workflowJobSet.Jobs.GetEnumerator()) {
        [string]$jobText = $job.Value
        if (-not $jobText.Contains('Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/acquire-build-lock@')) {
            continue
        }
        $licensedLifecycleCount += 1
        $returnIndex = $jobText.IndexOf('- name: Return Unity license', [StringComparison]::Ordinal)
        $classifierIndex = $jobText.IndexOf('- name: Classify Unity cleanup evidence', [StringComparison]::Ordinal)
        $releaseIndex = $jobText.IndexOf('- name: Release organization Unity lock', [StringComparison]::Ordinal)
        $gateIndex = $jobText.IndexOf('- name: Require confirmed Unity cleanup', [StringComparison]::Ordinal)
        $deleteIndex = $jobText.IndexOf('- name: Delete private Unity cleanup evidence', [StringComparison]::Ordinal)
        $usesCentralCleanup = $jobText.Contains("uses: $centralReturnUses")
        if ($usesCentralCleanup) {
            $centralLifecycleCount += 1
            $lifecycleIsOrdered = (
                $returnIndex -ge 0 -and
                $classifierIndex -gt $returnIndex -and
                $releaseIndex -gt $classifierIndex -and
                $gateIndex -gt $releaseIndex -and
                $deleteIndex -lt 0
            )
            $releaseAndGateAreExact = (
                $jobText -match ('(?ms)- name: Classify Unity cleanup evidence\s*\r?\n\s+id: cleanup_classification\s*\r?\n\s+if: \$\{\{ always\(\) && steps\.unity_lock\.outputs\.acquired == ''true'' \}\}.*?uses: ' + [regex]::Escape($centralClassifierUses) + '.*?return-log-digest: \$\{\{ steps\.return_unity_license\.outputs\.return-log-digest \}\}') -and
                $jobText -match '(?ms)- name: Release organization Unity lock\s*\r?\n\s+id: release_unity_lock\s*\r?\n\s+if: always\(\).*?resource-cleanup-status: \$\{\{ steps\.cleanup_classification\.outputs\.resource-cleanup-status \}\}.*?resource-health: \$\{\{ steps\.cleanup_classification\.outputs\.resource-health \}\}.*?resource-reason: \$\{\{ steps\.cleanup_classification\.outputs\.resource-reason \}\}' -and
                $jobText.Contains("uses: $centralGateUses") -and
                $jobText.Contains("classification-complete: `${{ steps.cleanup_classification.outputs.classification-complete }}") -and
                $jobText.Contains("cleanup-status: `${{ steps.cleanup_classification.outputs.resource-cleanup-status }}") -and
                $jobText.Contains("release-outcome: `${{ steps.release_unity_lock.outcome }}") -and
                $jobText.Contains("cleanup-result: `${{ steps.release_unity_lock.outputs.cleanup-result }}") -and
                $jobText.Contains("reservation-state: `${{ steps.release_unity_lock.outputs.reservation-state }}") -and
                $jobText.Contains("incident-id: `${{ steps.release_unity_lock.outputs.incident-id }}")
            )
            $privateEvidenceContractIsExact = -not $jobText.Contains('- name: Delete private Unity cleanup evidence')
            $returnOwnershipIsExact = $jobText.Contains('UH_CENTRAL_LICENSE_RETURN: "true"')
        } else {
            $legacyLifecycleCount += 1
            $lifecycleIsOrdered = (
                $returnIndex -ge 0 -and
                $classifierIndex -lt 0 -and
                $releaseIndex -gt $returnIndex -and
                $gateIndex -gt $releaseIndex -and
                $deleteIndex -gt $gateIndex
            )
            $releaseAndGateAreExact = (
                $jobText -match '(?ms)- name: Release organization Unity lock\s*\r?\n\s+id: release_unity_lock\s*\r?\n.*?resource-cleanup-status: \$\{\{ steps\.return_unity_license\.outputs\.resource-cleanup-status \}\}.*?resource-health: \$\{\{ steps\.return_unity_license\.outputs\.resource-health \}\}.*?resource-reason: \$\{\{ steps\.return_unity_license\.outputs\.resource-reason \}\}' -and
                -not [string]::IsNullOrWhiteSpace($legacyGateUses) -and
                $jobText.Contains("uses: $legacyGateUses") -and
                $jobText.Contains("classification-complete: `${{ steps.return_unity_license.outputs.classification-complete }}") -and
                $jobText.Contains("release-outcome: `${{ steps.release_unity_lock.outcome }}") -and
                $jobText.Contains("cleanup-result: `${{ steps.release_unity_lock.outputs.cleanup-result }}")
            )
            $privateEvidenceContractIsExact = (
                $jobText -match '(?ms)- name: Delete private Unity cleanup evidence\s*\r?\n\s+if: \$\{\{ always\(\) && steps\.unity_lock\.outputs\.acquired == ''true'' \}\}.*?Remove-(?:Item|Directory) '
            )
            $returnOwnershipIsExact = -not $jobText.Contains('UH_CENTRAL_LICENSE_RETURN:')
        }
        if (-not $lifecycleIsOrdered -or -not $releaseAndGateAreExact -or -not $privateEvidenceContractIsExact -or -not $returnOwnershipIsExact) {
            $centralLifecycleFailures += "$($workflowJobSet.File):$($job.Key)"
        }
    }
}
if (
    $licensedLifecycleCount -ne 6 -or
    $centralLifecycleCount -ne 4 -or
    $legacyLifecycleCount -ne 2 -or
    $centralLifecycleFailures.Count -gt 0
) {
    Write-Host "::error file=scripts/tests/test-unity-workflow-matrix-contract.ps1::Four Windows jobs must preserve central return -> digest classifier -> release -> gate with classifier-owned evidence deletion, while two Ubuntu/Docker jobs retain the legacy fail-closed return -> release -> gate -> deletion contract. Total=$licensedLifecycleCount Central=$centralLifecycleCount Legacy=$legacyLifecycleCount Failures=$($centralLifecycleFailures -join ', ')."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info 'Checked four central Windows lifecycles and two retained fail-closed container lifecycles.'
}

$centralReturnOwnershipContract = (
    $runCiTestsContent.Contains('$centralReturnOwnsLicense = [string]::Equals(') -and
    [regex]::Matches($runCiTestsContent, 'if \(\$hasLicenseCreds -and -not \$centralReturnOwnsLicense\)').Count -eq 1 -and
    [regex]::Matches($runCiTestsContent, 'if \(\$hasLicenseCreds\)').Count -eq 1
)
if (-not $centralReturnOwnershipContract) {
    Write-Host "::error file=scripts/unity/run-ci-tests.ps1::The central lifecycle must preserve pre-activation reclaim but suppress repository-local finally-return so the immutable central executor owns the single post-activation return and does not quarantine a redundant 400006."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info 'Checked central lifecycle callers leave the single authoritative return to the central executor.'
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
#
# The workflow that owns the checkout is FOUND, not named. Naming it is the same defect this file
# exists to prevent one level up: the checkout used to live in pwsh-invocations-lint.yml, that
# workflow was consolidated away, and a hard-coded path would have failed on the filename rather
# than on the thing being asserted.
$policyCheckoutWorkflows = @(
    Get-ChildItem -LiteralPath (Join-Path $repoRoot '.github/workflows') -Filter '*.yml' -File |
        Sort-Object Name |
        Where-Object {
            (Get-Content -LiteralPath $_.FullName -Raw) -match
                '(?m)^\s+repository:\s+Ambiguous-Interactive/ambiguous-organization-build-lock\s*$'
        }
)
if ($policyCheckoutWorkflows.Count -eq 0) {
    Write-Host '::error::No workflow checks out the central cleanup policy, so the parity gate cannot run anywhere.'
    $failed = $true
}
foreach ($policyCheckoutWorkflow in $policyCheckoutWorkflows) {
    $relativePolicyPath = ".github/workflows/$($policyCheckoutWorkflow.Name)"
    $policyCheckoutWorkflowContent = Get-Content -LiteralPath $policyCheckoutWorkflow.FullName -Raw
    $policyPinIsDerived = (
        $policyCheckoutWorkflowContent -match '(?m)^\s+run:\s*\|\s*$[\s\S]*?node scripts/resolve-build-lock-pin\.js require-confirmed-unity-cleanup' -and
        $policyCheckoutWorkflowContent -match '(?m)^\s+ref:\s+\$\{\{ steps\.policy_pin\.outputs\.sha \}\}\s*$' -and
        $policyCheckoutWorkflowContent -notmatch '(?m)^\s+ref:\s+[0-9a-f]{40}\s*$'
    )
    if (-not $policyPinIsDerived) {
        Write-Host "::error file=$relativePolicyPath::The central cleanup policy checkout must take its ref from scripts/resolve-build-lock-pin.js via steps.policy_pin.outputs.sha, never a literal commit SHA."
        $failed = $true
    } elseif ($VerboseOutput) {
        Write-Info "Checked $relativePolicyPath derives its central cleanup policy commit from the workflow pins."
    }
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
            Message = 'unity-tests-single-threaded must wait for unity-tests so a fundamentally broken tree does not spend a Unity seat.'
        },
        @{
            Name = 'uses always for skipped dependencies'
            Pattern = 'always\(\)'
            Message = 'unity-tests-single-threaded must use always() so workflow_dispatch runs with a skipped tier can still evaluate its result gate.'
        },
        @{
            Name = 'requires successful main Unity matrix'
            Pattern = "needs\.unity-tests\.result\s*==\s*'success'"
            Message = 'unity-tests-single-threaded must run only after unity-tests succeeds.'
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

    # Inverted, like the max-parallel contract above it. The SINGLE_THREADED tier must NOT wait for
    # the standalone tier: that edge was justified by same-workflow org-lock contention, which
    # sessions 168/169 disproved by measurement (2-4 s acquires, both hosts running Unity at once),
    # and it cost a median 2.8 runner-minutes per run in idle. Restoring it needs a measurement,
    # not a comment.
    $forbiddenSingleThreadedPatterns = @(
        @{
            Name = 'does not wait for the standalone tier'
            Pattern = '(?m)^      - unity-tests-standalone\s*$'
            Message = 'unity-tests-single-threaded must not need unity-tests-standalone: a runner that finishes the standalone tier early cannot start this one, and the org lock does not serialize legs.'
        },
        @{
            Name = 'does not gate on the standalone tier result'
            Pattern = 'needs\.unity-tests-standalone\.result'
            Message = 'unity-tests-single-threaded must not gate on unity-tests-standalone.result; the standalone tier is no longer one of its dependencies.'
        }
    )

    foreach ($contract in $forbiddenSingleThreadedPatterns) {
        if ($singleThreadedJob -match $contract.Pattern) {
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
