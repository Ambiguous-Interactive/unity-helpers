#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Tests the runner's environment-warning scan in scripts/unity/run-ci-tests.ps1
    (issue #657).

.DESCRIPTION
    A Unity leg can pass every test and still be running on a host whose licensing
    client is broken; the leg then fails tens of minutes later at
    return-unity-license, with a classification that names the return rather than
    the cause. The scan reads the editor log the runner already writes and names the
    cause as a warning.

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

Write-Host "Unity environment warnings: $passed passed, $failed failed."
if ($failed -gt 0) {
    exit 1
}
exit 0
