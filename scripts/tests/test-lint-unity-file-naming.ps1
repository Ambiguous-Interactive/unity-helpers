Param(
  [switch]$VerboseOutput
)

<#
.SYNOPSIS
    Test runner for lint-unity-file-naming.ps1

.DESCRIPTION
    This script tests the Unity file naming linter by running it against
    fixture files that should either pass or fail the lint checks.

    Test fixtures are in:
    - scripts/tests/fixtures/should-fail/ - Files expected to trigger lint errors
    - scripts/tests/fixtures/should-pass/ - Files expected to pass lint checks

.PARAMETER VerboseOutput
    Show detailed output during test execution

.EXAMPLE
    ./scripts/tests/test-lint-unity-file-naming.ps1
    ./scripts/tests/test-lint-unity-file-naming.ps1 -VerboseOutput
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[test-lint-unity-file-naming] $msg" -ForegroundColor Cyan }
}

function Write-TestResult {
  param(
    [string]$TestName,
    [bool]$Passed,
    [string]$Message = ""
  )
  
  if ($Passed) {
    Write-Host "  [PASS] $TestName" -ForegroundColor Green
    $script:TestsPassed++
  } else {
    Write-Host "  [FAIL] $TestName" -ForegroundColor Red
    if ($Message) {
      Write-Host "         $Message" -ForegroundColor Yellow
    }
    $script:TestsFailed++
    $script:FailedTests += $TestName
  }
}

function Get-ScriptRoot {
  # Get the directory containing this test script
  return $PSScriptRoot
}

function Get-LinterScript {
  $scriptRoot = Get-ScriptRoot
  $linterPath = Join-Path (Split-Path $scriptRoot -Parent) "lint-unity-file-naming.ps1"
  if (-not (Test-Path $linterPath)) {
    throw "Linter script not found at: $linterPath"
  }
  return $linterPath
}

function Invoke-LinterInDirectory {
  param([string]$Linter, [string]$Directory)

  $runner = [System.Management.Automation.PowerShell]::Create()
  try {
    $null = $runner.Runspace.SessionStateProxy.Path.SetLocation($Directory)
    $null = $runner.AddCommand($Linter)
    $result = $runner.Invoke()
    $output = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $result) { $output.Add($item.ToString()) }
    foreach ($item in $runner.Streams.Information) { $output.Add($item.MessageData.ToString()) }
    foreach ($item in $runner.Streams.Error) { $output.Add($item.ToString()) }
    if ($runner.Streams.Error.Count -gt 0) {
      throw "Linter emitted a PowerShell error: $($output -join [Environment]::NewLine)"
    }
    $exitCode = $runner.Runspace.SessionStateProxy.GetVariable('LASTEXITCODE')
    if ($null -eq $exitCode) {
      throw "Linter returned without an exit code. $($output -join [Environment]::NewLine)"
    }
    return [pscustomobject]@{
      ExitCode = [int]$exitCode
      Output = $output
    }
  } finally {
    $runner.Dispose()
  }
}

function Test-LinterOnFile {
  param(
    [string]$FilePath,
    [bool]$ExpectFailure
  )
  
  $linter = Get-LinterScript
  $fileName = Split-Path $FilePath -Leaf
  
  # Create a temporary directory structure that mimics Runtime/ for the linter
  $tempDir = New-Item -ItemType Directory -Path (Join-Path ([System.IO.Path]::GetTempPath()) "lint-test-$(Get-Random)")
  $runtimeDir = New-Item -ItemType Directory -Path (Join-Path $tempDir "Runtime")
  
  try {
    # Copy the test file to the temp Runtime directory
    Copy-Item $FilePath -Destination $runtimeDir
    
    # Keep each fixture isolated without starting another PowerShell process.
    $result = Invoke-LinterInDirectory -Linter $linter -Directory $tempDir.FullName
    $exitCode = $result.ExitCode
    $output = $result.Output
    
    Write-Info "Linter output for $fileName (exit code: $exitCode):"
    if ($VerboseOutput -and $output) {
      $output | ForEach-Object { Write-Info "  $_" }
    }
    
    if ($exitCode -ne 0 -and $exitCode -ne 1) {
      return @{ Passed = $false; Message = "Unexpected linter exit code: $exitCode" }
    }

    $linterFailed = ($exitCode -eq 1)
    
    if ($ExpectFailure -and $linterFailed) {
      return @{ Passed = $true; Message = "" }
    } elseif ($ExpectFailure -and -not $linterFailed) {
      return @{ Passed = $false; Message = "Expected linter to fail but it passed" }
    } elseif (-not $ExpectFailure -and $linterFailed) {
      $errorMsg = ($output | Where-Object { $_ -like "*UNH00*" }) -join "; "
      return @{ Passed = $false; Message = "Expected linter to pass but it failed: $errorMsg" }
    } else {
      return @{ Passed = $true; Message = "" }
    }
  } finally {
    # Cleanup temp directory
    Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
  }
}

function Run-ShouldFailTests {
  Write-Host ""
  Write-Host "Testing files that SHOULD trigger lint errors:" -ForegroundColor Magenta
  Write-Host ""
  
  $fixturesDir = Join-Path (Get-ScriptRoot) "fixtures/should-fail"
  
  if (-not (Test-Path $fixturesDir)) {
    Write-Host "  [SKIP] No should-fail fixtures directory found" -ForegroundColor Yellow
    return
  }
  
  $testFiles = Get-ChildItem -Path $fixturesDir -Filter "*.cs"
  
  if ($testFiles.Count -eq 0) {
    Write-Host "  [SKIP] No test fixtures found in should-fail directory" -ForegroundColor Yellow
    return
  }
  
  foreach ($file in $testFiles) {
    $result = Test-LinterOnFile -FilePath $file.FullName -ExpectFailure $true
    Write-TestResult -TestName $file.Name -Passed $result.Passed -Message $result.Message
  }
}

function Run-ShouldPassTests {
  Write-Host ""
  Write-Host "Testing files that should NOT trigger lint errors:" -ForegroundColor Magenta
  Write-Host ""
  
  $fixturesDir = Join-Path (Get-ScriptRoot) "fixtures/should-pass"
  
  if (-not (Test-Path $fixturesDir)) {
    Write-Host "  [SKIP] No should-pass fixtures directory found" -ForegroundColor Yellow
    return
  }
  
  $testFiles = Get-ChildItem -Path $fixturesDir -Filter "*.cs"
  
  if ($testFiles.Count -eq 0) {
    Write-Host "  [SKIP] No test fixtures found in should-pass directory" -ForegroundColor Yellow
    return
  }
  
  foreach ($file in $testFiles) {
    $result = Test-LinterOnFile -FilePath $file.FullName -ExpectFailure $false
    Write-TestResult -TestName $file.Name -Passed $result.Passed -Message $result.Message
  }
}

function Run-HarnessTests {
  $root = Join-Path ([System.IO.Path]::GetTempPath()) "lint-harness-$([System.Guid]::NewGuid().ToString('N'))"
  $null = New-Item -ItemType Directory -Path $root
  $scriptPath = Join-Path $root 'harness.ps1'
  try {
    Set-Content -LiteralPath $scriptPath -Value "Write-Host 'harness-zero'; exit 0"
    $result = Invoke-LinterInDirectory -Linter $scriptPath -Directory $root
    Write-TestResult 'Harness_ExitZero' ($result.ExitCode -eq 0 -and ($result.Output -join ' ') -match 'harness-zero')

    Set-Content -LiteralPath $scriptPath -Value "Write-Host 'harness-one'; exit 1"
    $result = Invoke-LinterInDirectory -Linter $scriptPath -Directory $root
    Write-TestResult 'Harness_ExitOne' ($result.ExitCode -eq 1 -and ($result.Output -join ' ') -match 'harness-one')

    Set-Content -LiteralPath $scriptPath -Value "Write-Host 'harness-no-exit'"
    $missingExitFailed = $false
    try { $null = Invoke-LinterInDirectory -Linter $scriptPath -Directory $root }
    catch { $missingExitFailed = $_.Exception.Message -match 'without an exit code' }
    Write-TestResult 'Harness_MissingExitFails' $missingExitFailed

    Set-Content -LiteralPath $scriptPath -Value "Write-Error 'harness-error'; exit 0"
    $errorFailed = $false
    try { $null = Invoke-LinterInDirectory -Linter $scriptPath -Directory $root }
    catch { $errorFailed = $_.Exception.Message -match 'PowerShell error' }
    Write-TestResult 'Harness_PowerShellErrorFails' $errorFailed
  } finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
  }
}

# Main execution
Write-Host ""
Write-Host "========================================" -ForegroundColor White
Write-Host "Unity File Naming Linter Tests" -ForegroundColor White  
Write-Host "========================================" -ForegroundColor White

Run-HarnessTests
Run-ShouldFailTests
Run-ShouldPassTests

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor White
Write-Host "Test Summary" -ForegroundColor White
Write-Host "========================================" -ForegroundColor White
Write-Host ""

$totalTests = $script:TestsPassed + $script:TestsFailed

if ($script:TestsFailed -eq 0) {
  Write-Host "All $totalTests tests passed!" -ForegroundColor Green
  exit 0
} else {
  Write-Host "Passed: $($script:TestsPassed) / $totalTests" -ForegroundColor Yellow
  Write-Host "Failed: $($script:TestsFailed) / $totalTests" -ForegroundColor Red
  Write-Host ""
  Write-Host "Failed tests:" -ForegroundColor Red
  foreach ($test in $script:FailedTests) {
    Write-Host "  - $test" -ForegroundColor Red
  }
  exit 1
}
