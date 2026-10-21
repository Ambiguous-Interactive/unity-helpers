#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Tests the runner's environment-warning scan in scripts/unity/run-ci-tests.ps1
    (issue #657).

.DESCRIPTION
    The scan reports licensing observations without predicting the final cleanup
    result. Signature and connection warnings can precede confirmed cleanup.
    Return-helper controls execute the actual source function with a fixture
    command; no Unity process, account or licensing service is contacted.

    Subjects are the verbatim log lines from run 33365150391, so a rewording of the
    pattern list that stops matching the real log fails here. The function is pulled
    out of run-ci-tests.ps1 through the PowerShell AST, the same idiom
    test-unity-license-activation-retry.ps1 uses, because the script's top-level
    param()/execution make it non-dot-sourceable.
#>
[CmdletBinding()]
param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../unity/lib/credential-redaction.ps1')

$scriptRoot = Split-Path -Parent $PSScriptRoot
$target = Join-Path $scriptRoot 'unity/run-ci-tests.ps1'
$patternLib = Join-Path $scriptRoot 'unity/lib/catastrophic-patterns.ps1'
foreach ($required in @($target, $patternLib)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        Write-Host "FATAL: cannot find $required"
        exit 1
    }
}

. $patternLib

$tokens = $null
$errs = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($target, [ref]$tokens, [ref]$errs)
if ($errs -and $errs.Count -gt 0) {
    Write-Host "FATAL: run-ci-tests.ps1 has parse errors:"
    $errs | ForEach-Object { Write-Host "  $($_.Extent.StartLineNumber): $($_.Message)" }
    exit 1
}

$fn = $ast.FindAll(
    {
        param($n)
        $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Write-UnityEnvironmentWarningAnnotations'
    },
    $true
) | Select-Object -First 1
if (-not $fn) {
    Write-Host "FATAL: function 'Write-UnityEnvironmentWarningAnnotations' not found in run-ci-tests.ps1"
    exit 1
}
Invoke-Expression $fn.Extent.Text

$passed = 0
$failed = 0

function Assert-That {
    param([string]$Description, [bool]$Condition)
    if ($Condition) {
        if ($VerboseOutput) { Write-Host "  PASS: $Description" }
        $script:passed++
    } else {
        Write-Host "  FAIL: $Description"
        $script:failed++
    }
}

function Invoke-Scan {
    param([string[]]$Lines)

    $logPath = Join-Path ([System.IO.Path]::GetTempPath()) ("uh-env-warning-" + [System.Guid]::NewGuid().ToString('N') + '.log')
    try {
        if ($null -ne $Lines) {
            Set-Content -LiteralPath $logPath -Value $Lines -Encoding utf8
        }

        return @(Write-UnityEnvironmentWarningAnnotations -LogPath $logPath 6>&1 | ForEach-Object { "$_" })
    } finally {
        if (Test-Path -LiteralPath $logPath -PathType Leaf) {
            Remove-Item -LiteralPath $logPath -Force
        }
    }
}

# The three lines are quoted from run 33365150391's editor log, reproduced in the
# issue. A pattern list that stops matching these has stopped doing its job.
$sickLog = @(
    '[Licensing::IpcConnector] Connection attempt to the License Client on channel: "LicenseClient-ELI-MACHINE$" failed because channel doesn''t exist; code: "0x80000002"',
    '[Licensing::Client] Error: Code 10 while verifying Licensing Client signature (process Id: 33836, path: ".../2022.3.45f1/Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client.exe")',
    '[Licensing::Module] Error: LicensingClient has failed validation; ignoring',
    '[Licensing::Module] Error: Access token is unavailable; failed to update',
    'Tests finished. Passed: 1201 Failed: 0'
)

$sickOutput = Invoke-Scan -Lines $sickLog
$sickWarnings = @($sickOutput | Where-Object { $_ -like '::warning::*' })
Assert-That 'a broken licensing client is reported' ($sickWarnings.Count -ge 3)
Assert-That 'the report names issue #657' (@($sickWarnings | Where-Object { $_ -like '*#657*' }).Count -ge 1)
Assert-That 'nothing is escalated to an error' (@($sickOutput | Where-Object { $_ -like '::error::*' }).Count -eq 0)
Assert-That 'the scan reports how much it read' (@($sickOutput | Where-Object { $_ -like '*Scanned 5 log line(s)*' }).Count -eq 1)

# A healthy log must stay silent, or the warning is noise on every leg.
$healthyOutput = Invoke-Scan -Lines @(
    '[Licensing::Module] Entitlement is valid',
    'Tests finished. Passed: 1201 Failed: 0'
)
Assert-That 'a healthy log raises nothing' (@($healthyOutput | Where-Object { $_ -like '::warning::*' }).Count -eq 0)
Assert-That 'a healthy scan still says it looked' (@($healthyOutput | Where-Object { $_ -like '*0 match(es)*' }).Count -eq 1)

# "Found nothing" and "never ran" must not read the same.
$missingPath = Join-Path ([System.IO.Path]::GetTempPath()) ("uh-env-warning-absent-" + [System.Guid]::NewGuid().ToString('N') + '.log')
$absentOutput = @(Write-UnityEnvironmentWarningAnnotations -LogPath $missingPath 6>&1 | ForEach-Object { "$_" })
Assert-That 'an absent log says nothing was scanned' (@($absentOutput | Where-Object { $_ -like '*nothing was scanned*' }).Count -eq 1)
Assert-That 'an absent log raises no warning' (@($absentOutput | Where-Object { $_ -like '::warning::*' }).Count -eq 0)

Assert-That 'the pattern list is not empty' (@(Get-EnvironmentWarningPatterns).Count -ge 1)

# Keep the observed pattern and warning severity, but do not infer client lifetime,
# service-side seat state or the outcome of a later independent cleanup command.
Assert-That 'signature warning names the observed validation failure' (@($sickWarnings | Where-Object { $_ -match 'signature validation failed' }).Count -eq 1)
Assert-That 'IPC warning names the connection observation' (@($sickWarnings | Where-Object { $_ -match 'absent at connection attempt' }).Count -eq 1)
Assert-That 'warnings do not predict final cleanup or client lifetime' (($sickWarnings -join "`n") -notmatch 'cannot be returned|will fail|never started|leaked seat')
Assert-That 'the three original observations remain warnings' ($sickWarnings.Count -eq 3)
Assert-That 'all matching log lines remain counted' (@($sickOutput | Where-Object { $_ -like '*3 match(es)*' }).Count -eq 1)

# This final receipt follows signature warnings in actual package-export job
# 119432668050 (run 39660650635). Scanning both observations must retain the early
# warnings without turning the later confirmation into another warning.
$cleanupConfirmation = 'Licensed Unity cleanup is confirmed. acquired=true classification=true cleanup=confirmed/healthy cleanup-reason=cleanup-confirmed release=success/cooldown-started released=true release-health=healthy release-reason=cleanup-confirmed reservation=cooldown/present incident=none'
$confirmedOutput = Invoke-Scan -Lines @($sickLog + $cleanupConfirmation)
$confirmedWarnings = @($confirmedOutput | Where-Object { $_ -like '::warning::*' })
Assert-That 'later cleanup confirmation does not suppress licensing observations' ($confirmedWarnings.Count -eq 3)
Assert-That 'later cleanup confirmation does not contradict early warning labels' (($confirmedWarnings -join "`n") -notmatch 'cannot be returned|will fail|never started|leaked seat')
Assert-That 'the combined scan retains all six input lines' (@($confirmedOutput | Where-Object { $_ -like '*Scanned 6 log line(s)*3 match(es)*' }).Count -eq 1)

foreach ($name in @('Invoke-UnityLicenseReturn', 'Write-CiNotice')) {
    $definition = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true))
    if ($definition.Count -ne 1) { throw "Expected exactly one production function: $name" }
    Invoke-Expression $definition[0].Extent.Text
}

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('uh-license-observations-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
$priorExit = Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
try {
    function Invoke-FixtureLicenseEditor {
        param([Parameter(ValueFromRemainingArguments = $true)][object[]]$Arguments)
        if ($script:ReturnCase.Throws) { throw 'Fixture launch was unavailable.' }
        Write-Output $script:ReturnCase.Line
        $global:LASTEXITCODE = $script:ReturnCase.Exit
    }
    foreach ($case in @(
        @{ Name = 'SkippedZero'; Line = 'Serial number unavailable for ULF return'; Exit = 0; Throws = $false; Warning = $true; Observation = 'skipped ULF return.*exit code 0' },
        @{ Name = 'SkippedNonzero'; Line = '[Licensing::Module] Error: Serial number unavailable for ULF return; skipping operation'; Exit = 9; Throws = $false; Warning = $true; Observation = 'skipped ULF return.*exit code 9' },
        @{ Name = 'Nonzero'; Line = 'Return command did not complete.'; Exit = 2; Throws = $false; Warning = $true; Observation = 'return exited with code 2' },
        @{ Name = 'Zero'; Line = 'Return command completed.'; Exit = 0; Throws = $false; Warning = $false; Observation = 'return process exited with code 0' },
        @{ Name = 'LaunchFailure'; Line = ''; Exit = 0; Throws = $true; Warning = $true; Observation = 'return failed: Fixture launch was unavailable' }
    )) {
        $script:ReturnCase = $case
        $privateLog = Join-Path $fixtureRoot ($case.Name + '/return.log')
        $output = @(Invoke-UnityLicenseReturn -EditorPath 'Invoke-FixtureLicenseEditor' -Email 'fixture-private-email' -Password 'fixture-private-password' -LogPath $privateLog 6>&1 | ForEach-Object { "$_" })
        $text = $output -join "`n"
        Assert-That "$($case.Name): actual return observation is retained" ($text -match $case.Observation)
        Assert-That "$($case.Name): warning severity is retained" ((@($output | Where-Object { $_ -like '::warning::*' }).Count -eq 1) -eq $case.Warning)
        Assert-That "$($case.Name): no asserted leak or completed seat return" ($text -notmatch 'leaked seat|Returned the Unity license|cannot be returned|will fail')
        Assert-That "$($case.Name): account values stay out of output" ($text -notmatch 'fixture-private-email|fixture-private-password')
        if (-not $case.Throws) {
            Assert-That "$($case.Name): private log records the actual process status" ((Get-Content -LiteralPath $privateLog -Raw) -match "exit_return_rc=$($case.Exit)")
            Assert-That "$($case.Name): final cleanup stays independent" ($text -match 'final cleanup')
        }
    }
} finally {
    if ($null -eq $priorExit) { Remove-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue }
    else { $global:LASTEXITCODE = $priorExit.Value }
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    Remove-Item Function:Invoke-FixtureLicenseEditor
}

Write-Host "Unity environment warnings: $passed passed, $failed failed."
if ($failed -gt 0) {
    exit 1
}
exit 0
