#!/usr/bin/env pwsh
# cspell:ignore Redist
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
$runnerBootstrapPath = Join-Path $repoRoot '.github/workflows/runner-bootstrap.yml'
$actionlintPath = Join-Path $repoRoot '.github/actionlint.yaml'
$runnerRunbookPath = Join-Path $repoRoot 'docs/runbooks/unity-runners-after-transfer.md'
$runnerDiagnosticsActionPath = Join-Path $repoRoot '.github/actions/print-self-hosted-runner-diagnostics/action.yml'
$unityVersionsPath = Join-Path $repoRoot '.github/unity-versions.json'
$windowsRunnerBootstrapPath = Join-Path $repoRoot 'scripts/unity/bootstrap-windows-runner.ps1'
$windowsRunnerMaintenancePath = Join-Path $repoRoot 'scripts/unity/maintain-windows-runner.ps1'
$ensureEditorPath = Join-Path $repoRoot 'scripts/unity/ensure-editor.ps1'

if (-not (Test-Path -LiteralPath $workflowPath)) {
    Write-Host "::error::Unity workflow not found: $workflowPath"
    exit 1
}
if (-not (Test-Path -LiteralPath $benchmarksWorkflowPath)) {
    Write-Host "::error::Unity benchmarks workflow not found: $benchmarksWorkflowPath"
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
        'Get-EnsureEditorProgressStallSeconds',
        'Get-EnsureEditorProgressNoticeIntervalSeconds',
        'Get-EnsureEditorQuarantineMoveRetryAttempts',
        'Invoke-WithRetry',
        'Test-IsPathInsideDirectory',
        'Get-CollapsedCliOutputTail',
        'Get-CliProgressTriple',
        'Get-LastCliProgressMessage',
        'Invoke-UnityCliCaptureWithTimeout',
        'Move-UnityInstallDirectoryToQuarantine'
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

[string[]]$lines = Get-Content -LiteralPath $workflowPath
[string]$workflowContent = $lines -join "`n"
[string[]]$benchmarksWorkflowLines = Get-Content -LiteralPath $benchmarksWorkflowPath
[string[]]$runnerBootstrapLines = Get-Content -LiteralPath $runnerBootstrapPath
[string]$runnerBootstrapContent = Get-Content -LiteralPath $runnerBootstrapPath -Raw
[string]$actionlintContent = Get-Content -LiteralPath $actionlintPath -Raw
[string]$runnerRunbookContent = Get-Content -LiteralPath $runnerRunbookPath -Raw
[string]$runnerDiagnosticsActionContent = Get-Content -LiteralPath $runnerDiagnosticsActionPath -Raw
[string]$windowsRunnerBootstrapContent = Get-Content -LiteralPath $windowsRunnerBootstrapPath -Raw
[string]$windowsRunnerMaintenanceContent = Get-Content -LiteralPath $windowsRunnerMaintenancePath -Raw
[string]$ensureEditorContent = Get-Content -LiteralPath $ensureEditorPath -Raw
$unityVersionsConfig = Get-Content -LiteralPath $unityVersionsPath -Raw | ConvertFrom-Json
[string[]]$unityVersions = @(
    $unityVersionsConfig.all |
        ForEach-Object { [string]$_ } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)
[bool]$failed = $false
[bool]$insideJobs = $false
$jobTexts = Get-WorkflowJobTexts -WorkflowLines $lines
$benchmarksJobTexts = Get-WorkflowJobTexts -WorkflowLines $benchmarksWorkflowLines
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

if ($unityVersions.Count -lt 1) {
    Write-Host "::error file=.github/unity-versions.json::Unity CI version config must define at least one entry in all[]."
    $failed = $true
} elseif ($unityVersions[-1] -ne '6000.5.2f1') {
    Write-Host "::error file=.github/unity-versions.json::Unity 6000.5.2f1 must be the latest tracked Unity version so Unity 6000.5 regressions are caught in CI."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity version source of truth includes Unity 6000.5.2f1 as the latest version."
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

$unityTestsRunnerMaintenanceJob = if ($jobTexts.ContainsKey('runner-maintenance')) { $jobTexts['runner-maintenance'] } else { '' }
$unityTestsMatrixJob = if ($jobTexts.ContainsKey('unity-tests')) { $jobTexts['unity-tests'] } else { '' }
$benchmarksRunnerMaintenanceJob = if ($benchmarksJobTexts.ContainsKey('runner-maintenance')) { $benchmarksJobTexts['runner-maintenance'] } else { '' }
$benchmarksMatrixJob = if ($benchmarksJobTexts.ContainsKey('benchmarks')) { $benchmarksJobTexts['benchmarks'] } else { '' }
$unityWorkflowsRunMaintenanceBeforeMatrix = (
    $jobTexts.ContainsKey('runner-maintenance') -and
    $benchmarksJobTexts.ContainsKey('runner-maintenance') -and
    $unityTestsRunnerMaintenanceJob.Contains('scripts\unity\maintain-windows-runner.ps1') -and
    $benchmarksRunnerMaintenanceJob.Contains('scripts\unity\maintain-windows-runner.ps1') -and
    $unityTestsRunnerMaintenanceJob.Contains('-ProvisioningProfile StandaloneWindowsIl2Cpp') -and
    $benchmarksRunnerMaintenanceJob.Contains('-ProvisioningProfile StandaloneWindowsIl2Cpp') -and
    $unityTestsRunnerMaintenanceJob.Contains('.artifacts\runner-bootstrap') -and
    $benchmarksRunnerMaintenanceJob.Contains('.artifacts\runner-bootstrap') -and
    $unityTestsRunnerMaintenanceJob.Contains("needs.runner-preflight.result == 'success'") -and
    $benchmarksRunnerMaintenanceJob.Contains("needs.runner-preflight.result == 'success'") -and
    $unityTestsMatrixJob.Contains('- runner-maintenance') -and
    $benchmarksMatrixJob.Contains('- runner-maintenance') -and
    $unityTestsMatrixJob.Contains("needs.runner-maintenance.result == 'success'") -and
    $benchmarksMatrixJob.Contains("needs.runner-maintenance.result == 'success'")
)
if (-not $unityWorkflowsRunMaintenanceBeforeMatrix) {
    Write-Host "::error file=.github/workflows/unity-tests.yml::Unity workflows must run scripts/unity/maintain-windows-runner.ps1 as a self-hosted maintenance gate before licensed matrix jobs so .github/unity-versions.json additions cannot outpace installed editors. Keep .github/workflows/unity-benchmarks.yml in sync."
    $failed = $true
} elseif ($VerboseOutput) {
    Write-Info "Checked Unity workflows run runner maintenance before self-hosted matrices."
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
