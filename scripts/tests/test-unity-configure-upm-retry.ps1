#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Behavioral tests for the Unity configure-pass Package Manager retry.

.DESCRIPTION
    Extracts the shipping functions from run-ci-tests.ps1 and drives the configure pass through its
    test invoker. No Unity process is launched. The cases prove that a missing fresh marker plus an
    exact UPM cancellation signature retries once, while marker success and ordinary failures do not.
#>
[CmdletBinding()]
param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $PSScriptRoot
$target = Join-Path $scriptRoot 'unity/run-ci-tests.ps1'
. (Join-Path $scriptRoot 'unity/lib/catastrophic-patterns.ps1')
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $target,
    [ref]$tokens,
    [ref]$parseErrors
)
if ($parseErrors -and $parseErrors.Count -gt 0) {
    Write-Host "FATAL: run-ci-tests.ps1 has parse errors:"
    $parseErrors | ForEach-Object { Write-Host "  $($_.Extent.StartLineNumber): $($_.Message)" }
    exit 1
}

foreach ($name in @(
        'ConvertTo-SingleLineDiagnostic',
        'Test-UnityPackageManagerTransientFailure',
        'Test-UnityConfigurePackageManagerRetryableFailure',
        'Write-UnityPackageManagerTransientFailureWarnings',
        'Clear-UnityPackageManagerRetryState',
        'Test-UnityConfigureMarker',
        'Invoke-UnityConfigurePass'
    )) {
    $functionAst = $ast.FindAll(
        {
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq $name
        },
        $true
    ) | Select-Object -First 1
    if (-not $functionAst) {
        Write-Host "FATAL: function '$name' not found in run-ci-tests.ps1"
        exit 1
    }
    Invoke-Expression $functionAst.Extent.Text
}

$script:passed = 0
$script:failed = 0
$script:notices = New-Object System.Collections.Generic.List[string]
$script:CatastrophicPatterns = @(Get-CatastrophicPatterns)

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

function Write-CiWarning { param([string]$Message) $script:notices.Add("warning:$Message") }
function Write-CiNotice { param([string]$Message) $script:notices.Add("notice:$Message") }
function Write-UnityRunFailureDiagnostics { }
function Write-AnalyzerSetupDiagnostics { }
function Write-UnityBenignExitWarning { }
function Get-NativeExitCodeDescription { param([int]$ExitCode) return "exit-$ExitCode" }

$temporaryRoot = Join-Path (
    [System.IO.Path]::GetTempPath()
) ("unity-configure-upm-retry-{0}" -f [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null

function Invoke-ConfigureCase {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string[]]$Attempts
    )

    $caseRoot = Join-Path $temporaryRoot $Name
    $project = Join-Path $caseRoot 'project'
    $artifacts = Join-Path $caseRoot 'artifacts'
    New-Item -ItemType Directory -Force -Path $project, $artifacts | Out-Null
    foreach ($relativePath in @('Library/PackageCache', 'Library/PackageManager', 'Temp')) {
        New-Item -ItemType Directory -Force -Path (Join-Path $project $relativePath) | Out-Null
        Set-Content -LiteralPath (Join-Path $project "$relativePath/stale.txt") -Value 'stale'
    }

    $marker = Join-Path $artifacts 'configure.marker'
    $log = Join-Path $artifacts 'configure.log'
    $state = [pscustomobject]@{ Count = 0 }
    $invoker = {
        param($arguments, $logPath, $label)
        $state.Count++
        $token = $Attempts[[Math]::Min($state.Count - 1, $Attempts.Count - 1)]
        switch ($token) {
            'success' {
                Set-Content -LiteralPath $logPath -Value 'configure completed'
                Set-Content -LiteralPath $env:UH_CONFIGURE_MARKER_PATH -Value 'configured'
                return 0
            }
            'success-with-old-upm-log' {
                Set-Content -LiteralPath $logPath -Value 'Failed to resolve packages: operation cancelled'
                Set-Content -LiteralPath $env:UH_CONFIGURE_MARKER_PATH -Value 'configured'
                return 1
            }
            'upm-cancel' {
                Set-Content -LiteralPath $logPath -Value 'IPCStream (Upm-123): IPC stream failed to read'
                return 1
            }
            'upm-and-compile-error' {
                Set-Content -LiteralPath $logPath -Value @(
                    'IPCStream (Upm-123): IPC stream failed to read',
                    'error CS1002: ; expected'
                )
                return 1
            }
            'upm-and-mixed-case-fatal' {
                Set-Content -LiteralPath $logPath -Value @(
                    'IPCStream (Upm-123): IPC stream failed to read',
                    'FATAL ERROR IN THE MONO RUNTIME'
                )
                return 1
            }
            default {
                Set-Content -LiteralPath $logPath -Value 'error CS1002: ; expected'
                return 1
            }
        }
    }.GetNewClosure()

    $threw = $false
    $message = ''
    try {
        Invoke-UnityConfigurePass `
            -EditorPath 'fake-editor' `
            -ProjectPath $project `
            -MarkerPath $marker `
            -LogPath $log `
            -Label $Name `
            -ConfigureInvoker $invoker
    } catch {
        $threw = $true
        $message = $_.Exception.Message
    }

    return [pscustomobject]@{
        Attempts = $state.Count
        Threw = $threw
        Message = $message
        Marker = $marker
        Log = $log
        Project = $project
        FirstAttemptLog = Join-Path $artifacts 'configure.first-attempt.log'
        EnvironmentCleared = -not (Test-Path -LiteralPath Env:\UH_CONFIGURE_MARKER_PATH)
    }
}

try {
    $ordinarySuccess = Invoke-ConfigureCase -Name 'ordinary-success' -Attempts @('success')
    Assert-That 'success runs once' ($ordinarySuccess.Attempts -eq 1)
    Assert-That 'success does not throw' (-not $ordinarySuccess.Threw)
    Assert-That 'success clears the marker-path environment variable' (
        $ordinarySuccess.EnvironmentCleared
    )

    $markerWins = Invoke-ConfigureCase `
        -Name 'marker-wins' `
        -Attempts @('success-with-old-upm-log', 'upm-cancel')
    Assert-That 'a fresh marker prevents retry despite an old UPM signature' ($markerWins.Attempts -eq 1)
    Assert-That 'fresh-marker success does not throw on a non-zero exit' (-not $markerWins.Threw)
    Assert-That 'fresh-marker success clears the marker-path environment variable' (
        $markerWins.EnvironmentCleared
    )

    $retried = Invoke-ConfigureCase -Name 'retry' -Attempts @('upm-cancel', 'success')
    Assert-That 'an exact marker-less UPM signature retries once' ($retried.Attempts -eq 2)
    Assert-That 'successful retry does not throw' (-not $retried.Threw)
    Assert-That 'successful retry writes the configure marker' (
        Test-Path -LiteralPath $retried.Marker -PathType Leaf
    )
    Assert-That 'first failed configure log is preserved' (
        (Test-Path -LiteralPath $retried.FirstAttemptLog -PathType Leaf) -and
        (Get-Content -LiteralPath $retried.FirstAttemptLog -Raw) -match 'IPC stream failed to read'
    )
    Assert-That 'retry clears and recreates PackageCache' (
        (Test-Path -LiteralPath (Join-Path $retried.Project 'Library/PackageCache') -PathType Container) -and
        -not (Test-Path -LiteralPath (Join-Path $retried.Project 'Library/PackageCache/stale.txt'))
    )
    Assert-That 'successful retry clears the marker-path environment variable' (
        $retried.EnvironmentCleared
    )

    $bounded = Invoke-ConfigureCase -Name 'bounded' -Attempts @('upm-cancel', 'upm-cancel')
    Assert-That 'two UPM cancellations stop after exactly two attempts' ($bounded.Attempts -eq 2)
    Assert-That 'a failed retry remains fatal' $bounded.Threw
    Assert-That 'a failed retry clears the marker-path environment variable' (
        $bounded.EnvironmentCleared
    )

    $compileFailure = Invoke-ConfigureCase -Name 'compile-failure' -Attempts @('compile-error')
    Assert-That 'an ordinary compile failure is not retried' ($compileFailure.Attempts -eq 1)
    Assert-That 'an ordinary compile failure remains fatal' $compileFailure.Threw
    Assert-That 'an ordinary compile failure clears the marker-path environment variable' (
        $compileFailure.EnvironmentCleared
    )
    Assert-That 'an ordinary compile failure does not create a retry log' (
        -not (Test-Path -LiteralPath $compileFailure.FirstAttemptLog)
    )

    $mixedFailure = Invoke-ConfigureCase `
        -Name 'mixed-upm-compile-failure' `
        -Attempts @('upm-and-compile-error', 'success')
    Assert-That 'a mixed UPM and compile failure is not retried' ($mixedFailure.Attempts -eq 1)
    Assert-That 'a mixed UPM and compile failure remains fatal' $mixedFailure.Threw
    Assert-That 'a mixed failure clears the marker-path environment variable' (
        $mixedFailure.EnvironmentCleared
    )

    $mixedCaseFatal = Invoke-ConfigureCase `
        -Name 'mixed-upm-case-insensitive-fatal' `
        -Attempts @('upm-and-mixed-case-fatal', 'success')
    Assert-That 'a case-varied simple catastrophic pattern is not retried' (
        $mixedCaseFatal.Attempts -eq 1
    )
    Assert-That 'a mixed UPM and case-varied fatal signal remains fatal' $mixedCaseFatal.Threw
    Assert-That 'a mixed case-varied failure clears the marker-path environment variable' (
        $mixedCaseFatal.EnvironmentCleared
    )
} finally {
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Unity configure UPM retry tests: $($script:passed) passed, $($script:failed) failed"
if ($script:failed -gt 0) {
    exit 1
}
