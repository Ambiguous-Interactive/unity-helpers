#!/usr/bin/env pwsh
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot '../unity/run-ci-tests.ps1'
$source = Get-Content -LiteralPath $runner -Raw
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'The Unity runner must parse.' }
foreach ($name in @('Test-NUnitResults', 'Get-UnityFailedNodeCount')) {
    $function = $ast.Find({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
        }, $true)
    if ($null -eq $function) { throw "Missing production function $name." }
    Invoke-Expression $function.Extent.Text
}

# Diagnostics are not the subject; the actual XML gate and invocation sites are.
function Write-CiError { param([string]$Message) }
function Write-CiNotice { param([string]$Message) }
function Write-UnityFailedTestAnnotations { param([xml]$Xml, [string]$Label) }
function Write-UnityResultFailureDiagnostics { param([string]$LogPath, [string]$Project, [string]$Label) }

$selectionStart = $source.IndexOf('$filterArgs = @()')
$selectionEnd = $source.IndexOf('$resultsPath =', $selectionStart)
if ($selectionStart -lt 0 -or $selectionEnd -le $selectionStart) { throw 'Missing test selection construction.' }
$selection = $source.Substring($selectionStart, $selectionEnd - $selectionStart)
$expectedFilter = 'Package.Fixture.Method("literal value");Package.Other'
foreach ($TestFilter in @('', $expectedFilter)) {
    $TestCategory = 'FocusedAcceptance'
    Invoke-Expression $selection
    $ProjectPath = 'C:/owned project'
    $resultsPath = 'C:/result.xml'
    $AssemblyNames = 'Package.Tests'
    $acceleratorArgs = @()
    $graphicsArgs = @('-nographics')
    foreach ($TestMode in @('editmode', 'playmode', 'standalone')) {
        $testPlatform = $TestMode
        $variable = if ($TestMode -eq 'standalone') { 'buildArgs' } else { 'testArgs' }
        $assignments = @($ast.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                    $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
                    $node.Left.VariablePath.UserPath -eq $variable
                }, $true))
        if ($assignments.Count -lt 1) { throw "Missing $variable construction." }
        foreach ($assignment in $assignments) { Invoke-Expression $assignment.Extent.Text }
        $arguments = Get-Variable -Name $variable -ValueOnly
        $position = [Array]::IndexOf($arguments, '-testFilter')
        if ($TestFilter -eq '') {
            if ($position -ne -1 -or $requireFilteredPass) { throw "$TestMode default selection changed." }
        } else {
            if ($position -lt 0 -or $arguments[$position + 1] -cne $expectedFilter -or -not $requireFilteredPass) {
                throw "$TestMode lost or split the requested test filter."
            }
        }
        if ([Array]::IndexOf($arguments, '-testCategory') -lt 0) { throw "$TestMode lost its category filter." }
    }
}

$invocations = @($ast.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.CommandAst] -and
            $node.GetCommandName() -eq 'Test-NUnitResults'
        }, $true))
if ($invocations.Count -ne 2) { throw 'Both editor and standalone result invocation sites must be tested.' }
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('unity-filter-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$checks = 0
try {
    foreach ($fixture in @(
            @{ Name = 'passed'; Total = 1; Passed = 1; Failed = 0; Skipped = 0; Body = ''; Error = '' },
            @{ Name = 'empty'; Total = 0; Passed = 0; Failed = 0; Skipped = 0; Body = ''; Error = '0 tests ran' },
            @{ Name = 'explicit skipped'; Total = 1; Passed = 0; Failed = 0; Skipped = 1; Body = ''; Error = 'No selected tests passed' },
            @{ Name = 'inconclusive'; Total = 1; Passed = 0; Failed = 0; Skipped = 0; Body = ''; Error = 'No selected tests passed' },
            @{ Name = 'failed leaf'; Total = 1; Passed = 1; Failed = 0; Skipped = 0; Body = '<test-case fullname="Broken" result="Failed" />'; Error = 'tests failed' }
        )) {
        $resultsPath = Join-Path $temporary 'results.xml'
        Set-Content -LiteralPath $resultsPath -Value ("<test-run total='{0}' passed='{1}' failed='{2}' skipped='{3}'>{4}</test-run>" -f
            $fixture.Total, $fixture.Passed, $fixture.Failed, $fixture.Skipped, $fixture.Body)
        $UnityVersion = '2021.3.45f1'
        $TestMode = 'editmode'
        $logPath = ''
        $playerLogPath = ''
        $ProjectPath = $temporary
        $playerExitForValidation = 0
        $runExit = 0
        $requireFilteredPass = $true
        foreach ($invocation in $invocations) {
            $message = ''
            try { Invoke-Expression $invocation.Extent.Text } catch { $message = $_.Exception.Message }
            if ($fixture.Error -eq '') {
                if ($message -ne '') { throw "Passing fixture rejected: $message" }
            } elseif ($message -notlike ('*' + $fixture.Error + '*')) {
                throw "Expected rejection for '$($fixture.Name)', got '$message'."
            }
            $checks++
        }
    }
} finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
Write-Host "[test-unity-test-filter] All six argument propagation controls and $checks result controls passed."
