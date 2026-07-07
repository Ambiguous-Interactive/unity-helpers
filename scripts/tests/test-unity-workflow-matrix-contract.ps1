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

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$workflowPath = Join-Path $repoRoot '.github/workflows/unity-tests.yml'
$benchmarksWorkflowPath = Join-Path $repoRoot '.github/workflows/unity-benchmarks.yml'
$releaseWorkflowPath = Join-Path $repoRoot '.github/workflows/release.yml'
$runnerBootstrapPath = Join-Path $repoRoot '.github/workflows/runner-bootstrap.yml'
$actionlintPath = Join-Path $repoRoot '.github/actionlint.yaml'
$runnerRunbookPath = Join-Path $repoRoot 'docs/runbooks/unity-runners-after-transfer.md'
$runnerDiagnosticsActionPath = Join-Path $repoRoot '.github/actions/print-self-hosted-runner-diagnostics/action.yml'
$unityVersionsPath = Join-Path $repoRoot '.github/unity-versions.json'
$integrationPackagesPath = Join-Path $repoRoot '.github/integration-packages.json'
$windowsRunnerBootstrapPath = Join-Path $repoRoot 'scripts/unity/bootstrap-windows-runner.ps1'
$windowsRunnerMaintenancePath = Join-Path $repoRoot 'scripts/unity/maintain-windows-runner.ps1'
$ensureEditorPath = Join-Path $repoRoot 'scripts/unity/ensure-editor.ps1'
$runCiTestsPath = Join-Path $repoRoot 'scripts/unity/run-ci-tests.ps1'

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
        'Get-EnsureEditorProgressStallSeconds',
        'Get-EnsureEditorProgressNoticeIntervalSeconds',
        'Get-EnsureEditorQuarantineMoveRetryAttempts',
        'Invoke-WithRetry',
        'Test-IsPathInsideDirectory',
        'Get-UnityCiAlternateInstallRoot',
        'Get-UnityEditorCandidates',
        'Find-UnityEditor',
        'Test-UnityAtomicInstallFailureMayBePinnedToExistingEditor',
        'Install-UnityEditorModulesViaAtomicReinstall',
        'Get-CollapsedCliOutputTail',
        'Get-CliProgressTriple',
        'Get-LastCliProgressMessage',
        'Invoke-UnityCliCaptureWithTimeout',
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
        [Parameter(Mandatory = $true)][string]$WorkflowFile
    )

    $acquireUses = 'Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/acquire-build-lock@v1.2.0'
    $releaseUses = 'Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/release-build-lock@v1.2.0'
    $returnUses = './.github/actions/return-unity-license'
    $requiredGate = 'if: ${{ always() && steps.unity_lock.outcome == ''success'' }}'

    $acquirePattern = '(?ms)- name: Acquire organization Unity lock\s*\r?\n\s+id:\s+unity_lock\s*\r?\n(?:.*?\r?\n)*?\s+uses:\s+' + [regex]::Escape($acquireUses)
    $returnPattern = '(?ms)- name: Return Unity license\s*\r?\n\s+' + [regex]::Escape($requiredGate) + '\s*\r?\n\s+uses:\s+' + [regex]::Escape($returnUses)
    $releasePattern = '(?ms)- name: Release organization Unity lock\s*\r?\n\s+' + [regex]::Escape($requiredGate) + '\s*\r?\n\s+uses:\s+' + [regex]::Escape($releaseUses)
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

        if ($jobText -notmatch $acquirePattern) {
            $failures += "$($job.Key): acquire step must have id unity_lock before uses"
        }
        if ($jobText -notmatch $returnPattern) {
            $failures += "$($job.Key): return-unity-license must be gated on successful unity_lock acquisition"
        }
        if ($jobText -notmatch $releasePattern) {
            $failures += "$($job.Key): release-build-lock must be gated on successful unity_lock acquisition"
        }
        if (-not (0 -le $acquireIndex -and $acquireIndex -lt $returnIndex -and $returnIndex -lt $releaseIndex)) {
            $failures += "$($job.Key): lock cleanup order must be acquire, return Unity license, then release lock"
        }
    }

    if ($failures.Count -gt 0) {
        Write-Host "::error file=$WorkflowFile::Unity lock cleanup contract failed: $($failures -join '; ')"
        return $false
    }

    return $true
}

[string[]]$lines = Get-Content -LiteralPath $workflowPath
[string]$workflowContent = $lines -join "`n"
[string[]]$benchmarksWorkflowLines = Get-Content -LiteralPath $benchmarksWorkflowPath
[string[]]$releaseWorkflowLines = Get-Content -LiteralPath $releaseWorkflowPath
[string[]]$runnerBootstrapLines = Get-Content -LiteralPath $runnerBootstrapPath
[string]$runnerBootstrapContent = Get-Content -LiteralPath $runnerBootstrapPath -Raw
[string]$actionlintContent = Get-Content -LiteralPath $actionlintPath -Raw
[string]$runnerRunbookContent = Get-Content -LiteralPath $runnerRunbookPath -Raw
[string]$runnerDiagnosticsActionContent = Get-Content -LiteralPath $runnerDiagnosticsActionPath -Raw
[string]$windowsRunnerBootstrapContent = Get-Content -LiteralPath $windowsRunnerBootstrapPath -Raw
[string]$windowsRunnerMaintenanceContent = Get-Content -LiteralPath $windowsRunnerMaintenancePath -Raw
[string]$ensureEditorContent = Get-Content -LiteralPath $ensureEditorPath -Raw
[string]$runCiTestsContent = Get-Content -LiteralPath $runCiTestsPath -Raw
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
$requiredLabelsPattern = '(?m)^\s+REQUIRED_LABELS:\s*"self-hosted,Windows,RAM-64GB,\$\{\{\s*inputs\.runner-label\s*\}\}"\s*$'
$bootstrapRunsOnPattern = '(?m)^\s+runs-on:\s*\[self-hosted,\s*Windows,\s*RAM-64GB,\s*"\$\{\{\s*inputs\.runner-label\s*\}\}"\]\s*$'
$stableRunnerLabelMatcher = 'select((($labels - ((.labels // []) | map(.name))) | length) == 0)'
$brokenRunnerLabelMatcher = '($labels | all(. as $l | (.labels // [])'
$runnerBootstrapPinsRequestedMachine = (
    $runnerBootstrapJobTexts.ContainsKey('runner-preflight') -and
    $runnerBootstrapJobTexts.ContainsKey('bootstrap') -and
    $runnerPreflightJob -match $requiredLabelsPattern -and
    $runnerPreflightJob.Contains($stableRunnerLabelMatcher) -and
    -not $runnerPreflightJob.Contains($brokenRunnerLabelMatcher) -and
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
$unityWorkflowRunnerPreflightsUseStableMatcher = (
    $jobTexts.ContainsKey('runner-preflight') -and
    $benchmarksJobTexts.ContainsKey('runner-preflight') -and
    $unityTestsRunnerPreflightJob.Contains($stableRunnerLabelMatcher) -and
    $benchmarksRunnerPreflightJob.Contains($stableRunnerLabelMatcher) -and
    -not $unityTestsRunnerPreflightJob.Contains($brokenRunnerLabelMatcher) -and
    -not $benchmarksRunnerPreflightJob.Contains($brokenRunnerLabelMatcher)
)
if (-not $unityWorkflowRunnerPreflightsUseStableMatcher) {
    Write-Host "::error file=.github/workflows/unity-tests.yml::Unity workflow runner-preflight label matching must use the set-difference matcher from runner-bootstrap.yml so visible runner inventories do not crash jq by treating label strings as runner objects. Keep .github/workflows/unity-benchmarks.yml in sync."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity workflow runner-preflight label matchers use the stable set-difference form."
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
    $cacheIndex = $JobText.IndexOf('- name: Cache Unity Library and package caches')
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
        (Test-UnityWorkflowStepHasEmptyAssemblyGate -JobText $JobText -StepName 'Cache Unity Library and package caches') -and
        (Test-UnityWorkflowStepHasEmptyAssemblyGate -JobText $JobText -StepName 'Validate Unity license secrets') -and
        (Test-UnityWorkflowStepHasEmptyAssemblyGate -JobText $JobText -StepName 'Provision Unity Editor') -and
        (Test-UnityWorkflowStepHasEmptyAssemblyGate -JobText $JobText -StepName 'Acquire organization Unity lock') -and
        (Test-UnityWorkflowStepHasEmptyAssemblyGate -JobText $JobText -StepName 'Run Unity Test Runner')
    )
    $jobTimeoutCoversMaintenanceBudget = $JobText -match '(?m)^\s+timeout-minutes:\s*1200\s*$'
    $maintenanceStepEndCandidates = @(
        $runnerDiagnosticsIndex,
        $cacheIndex,
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
        ($cacheIndex -lt 0 -or $maintenanceIndex -lt $cacheIndex) -and
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
    Write-Host "::error file=.github/workflows/unity-tests.yml::Unity workflows must compute the test assembly list before runner maintenance; skip maintenance, diagnostics, cache, license validation, provisioning, lock acquisition, and Unity test execution when the selected leg is empty; and still run scripts/unity/maintain-windows-runner.ps1 inside each non-empty self-hosted Unity job before Provision Unity Editor. Maintenance must use Windows PowerShell, publish the discovered PowerShell 7 directory through GITHUB_PATH, and remain the repair path. Job timeouts must also cover the in-job maintenance/provisioning/lock/test budget. Keep .github/workflows/unity-benchmarks.yml in sync."
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
if ($bootstrapEnvExitCode -ne 2) {
    Write-Host "::error file=scripts/unity/bootstrap-windows-runner.ps1::UH_RUNNER_DISABLE_AUTO_BOOTSTRAP=1 must force direct bootstrap script execution into detect-only mode. Exit $bootstrapEnvExitCode. Output: $($bootstrapEnvOutput -join ' ')"
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

$hasPrCancelConcurrency = (
    $workflowContent.Contains('group: unity-tests-${{ github.event.pull_request.number || github.ref }}') -and
    $workflowContent.Contains('cancel-in-progress: ${{ github.event_name == ''pull_request'' }}')
)
if (-not $hasPrCancelConcurrency) {
    Write-Host "::error file=.github/workflows/unity-tests.yml::Unity Tests must cancel superseded pull_request runs so old iterations do not keep the organization Unity runner occupied."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity Tests pull_request concurrency cancellation contract."
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
    (Test-UnityLockCleanupIsGated -Jobs $jobTexts -WorkflowFile '.github/workflows/unity-tests.yml') -and
    (Test-UnityLockCleanupIsGated -Jobs $benchmarksJobTexts -WorkflowFile '.github/workflows/unity-benchmarks.yml') -and
    (Test-UnityLockCleanupIsGated -Jobs $releaseJobTexts -WorkflowFile '.github/workflows/release.yml')
)
if (-not $unityLockCleanupIsGated) {
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity lock cleanup runs only after acquisition and before release."
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
