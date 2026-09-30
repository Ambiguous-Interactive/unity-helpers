Param(
  [switch]$VerboseOutput
)

<#
.SYNOPSIS
    Tests for scripts/unity/report-slow-tests.ps1.

.DESCRIPTION
    Uses synthetic NUnit3 results XML to verify ranking (slowest first, ordinal
    tiebreak), budget flagging (warn vs fail), and error handling (missing /
    malformed XML). No Unity required.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

function Write-TestResult {
  param([string]$TestName, [bool]$Passed, [string]$Message = "")
  if ($Passed) {
    Write-Host "  [PASS] $TestName" -ForegroundColor Green
    $script:TestsPassed++
  }
  else {
    Write-Host "  [FAIL] $TestName" -ForegroundColor Red
    if ($Message) { Write-Host "         $Message" -ForegroundColor Yellow }
    $script:TestsFailed++
    $script:FailedTests += $TestName
  }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$reportScript = Join-Path $repoRoot 'scripts' 'unity' 'report-slow-tests.ps1'

$xml = @'
<?xml version="1.0" encoding="utf-8"?>
<test-run total="5" duration="123.5">
  <test-suite type="Assembly" name="Asm">
    <test-suite type="TestFixture" fullname="A.SlowFixture" duration="100.0" total="2">
      <test-case fullname="A.SlowFixture.Test1" duration="60.0" />
      <test-case fullname="A.SlowFixture.Test2" duration="40.0" />
    </test-suite>
    <test-suite type="TestFixture" fullname="A.FastFixture" duration="3.0" total="3">
      <test-case fullname="A.FastFixture.T1" duration="1.0" />
      <test-case fullname="A.FastFixture.T2" duration="1.5" />
      <test-case fullname="A.FastFixture.T3" duration="0.5" />
    </test-suite>
  </test-suite>
</test-run>
'@

$tmp = [System.IO.Path]::GetTempFileName()
$xmlPath = [System.IO.Path]::ChangeExtension($tmp, '.xml')
[System.IO.File]::WriteAllText($xmlPath, $xml, (New-Object System.Text.UTF8Encoding($false)))
Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue

function Invoke-Report {
  param(
    [hashtable]$Parameters = @{},
    [string]$ScriptPath = $reportScript,
    [switch]$Cli
  )

  if ($Cli) {
    $reportArgs = @($ScriptPath)
    foreach ($name in $Parameters.Keys) {
      $reportArgs += "-$name"
      if ($Parameters[$name] -isnot [bool]) { $reportArgs += $Parameters[$name] }
    }
    $out = & pwsh -NoProfile -File @reportArgs 2>&1
    return @{ ExitCode = $LASTEXITCODE; Output = ($out -join "`n") }
  }

  $runner = [System.Management.Automation.PowerShell]::Create()
  try {
    $null = $runner.AddCommand($ScriptPath)
    foreach ($name in $Parameters.Keys) { $null = $runner.AddParameter($name, $Parameters[$name]) }
    $result = $runner.Invoke()
    $output = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $result) { $output.Add($item.ToString()) }
    foreach ($item in $runner.Streams.Information) { $output.Add($item.MessageData.ToString()) }
    foreach ($item in $runner.Streams.Warning) { $output.Add($item.Message) }
    foreach ($item in $runner.Streams.Error) { $output.Add($item.ToString()) }
    if ($runner.Streams.Error.Count -gt 0) {
      throw "Report emitted a PowerShell error: $($output -join [Environment]::NewLine)"
    }
    $exitCode = $runner.Runspace.SessionStateProxy.GetVariable('LASTEXITCODE')
    if ($null -eq $exitCode) {
      throw "Report returned without an exit status. $($output -join [Environment]::NewLine)"
    }
    return @{ ExitCode = [int]$exitCode; Output = ($output -join [Environment]::NewLine) }
  }
  finally {
    $runner.Dispose()
  }
}

Write-Host "Testing report-slow-tests.ps1..." -ForegroundColor White

# Ranking: slowest fixture first; slowest case is Test1 (60s).
$r1 = Invoke-Report -Parameters @{ ResultsPath = $xmlPath; Top = 5 } -Cli
Write-TestResult "Report.ExitsZero" ($r1.ExitCode -eq 0) "exit $($r1.ExitCode)"
$slowIdx = $r1.Output.IndexOf('A.SlowFixture')
$fastIdx = $r1.Output.IndexOf('A.FastFixture')
Write-TestResult "Report.SlowFixtureRankedFirst" ($slowIdx -ge 0 -and $fastIdx -ge 0 -and $slowIdx -lt $fastIdx) "SlowFixture should appear before FastFixture"
Write-TestResult "Report.ShowsSlowestCase" ($r1.Output -match 'A\.SlowFixture\.Test1') "Expected slowest case Test1 listed"
Write-TestResult "Report.ShowsRunTotal" ($r1.Output -match '5 tests') "Expected run total reported"

# Budget: warn-only when under -FailOverBudget; still exit 0.
$r2 = Invoke-Report -Parameters @{ ResultsPath = $xmlPath; FixtureBudgetSeconds = 50 }
Write-TestResult "Report.BudgetWarnExitZero" ($r2.ExitCode -eq 0) "warn-only should exit 0, got $($r2.ExitCode)"
Write-TestResult "Report.BudgetWarnEmitsWarning" ($r2.Output -match '::warning::Fixture over 50s budget: A.SlowFixture') "Expected ::warning:: for over-budget fixture"

# Budget: fail when -FailOverBudget and a fixture exceeds.
$r3 = Invoke-Report -Parameters @{ ResultsPath = $xmlPath; FixtureBudgetSeconds = 50; FailOverBudget = $true }
Write-TestResult "Report.BudgetFailExitOne" ($r3.ExitCode -eq 1) "fail-over-budget should exit 1, got $($r3.ExitCode)"
Write-TestResult "Report.BudgetFailEmitsError" ($r3.Output -match '::error::Fixture over 50s budget: A.SlowFixture') "Expected ::error:: for over-budget fixture"

# Budget: no fixture exceeds a high budget -> exit 0 even with -FailOverBudget.
$r4 = Invoke-Report -Parameters @{ ResultsPath = $xmlPath; FixtureBudgetSeconds = 500; FailOverBudget = $true }
Write-TestResult "Report.UnderBudgetExitZero" ($r4.ExitCode -eq 0) "no fixture over budget should exit 0, got $($r4.ExitCode)"

# StepSummary markdown.
$r5 = Invoke-Report -Parameters @{ ResultsPath = $xmlPath; StepSummary = $true }
Write-TestResult "Report.StepSummaryMarkdown" ($r5.Output -match '### Slowest tests' -and $r5.Output -match '\| Seconds \| Fixture \|') "Expected step-summary markdown table"

# Error handling.
$missing = Invoke-Report -Parameters @{ ResultsPath = (Join-Path ([System.IO.Path]::GetTempPath()) 'does-not-exist-xyz.xml') }
Write-TestResult "Report.MissingFileExitOne" ($missing.ExitCode -eq 1 -and $missing.Output.Contains('results file not found')) "Exit: $($missing.ExitCode). Output: $($missing.Output)"

$badPath = [System.IO.Path]::ChangeExtension([System.IO.Path]::GetTempFileName(), '.xml')
[System.IO.File]::WriteAllText($badPath, "<not-xml <<<", (New-Object System.Text.UTF8Encoding($false)))
$malformed = Invoke-Report -Parameters @{ ResultsPath = $badPath }
Write-TestResult "Report.MalformedXmlExitOne" ($malformed.ExitCode -eq 1 -and $malformed.Output.Contains('could not parse XML')) "Exit: $($malformed.ExitCode). Output: $($malformed.Output)"
Remove-Item -LiteralPath $badPath -Force -ErrorAction SilentlyContinue

$harnessScript = Join-Path ([System.IO.Path]::GetTempPath()) ('report-harness-' + [System.Guid]::NewGuid().ToString('N') + '.ps1')
try {
  foreach ($expectedExit in @(0, 1, 7)) {
    Set-Content -LiteralPath $harnessScript -Value "Write-Host 'harness-output'; exit $expectedExit"
    $result = Invoke-Report -ScriptPath $harnessScript
    Write-TestResult "Runspace.Exit$expectedExit" `
      ($result.ExitCode -eq $expectedExit -and $result.Output.Contains('harness-output'))
  }
  foreach ($control in @(
    @{ Name = 'MissingStatus'; Content = "Write-Host 'missing-status'"; Diagnostic = 'without an exit status' },
    @{ Name = 'ErrorWithExitZero'; Content = "Write-Error 'harness-error'; exit 0"; Diagnostic = 'PowerShell error' },
    @{ Name = 'ErrorWithExitOne'; Content = "Write-Error 'harness-error'; exit 1"; Diagnostic = 'PowerShell error' },
    @{ Name = 'Throw'; Content = "throw 'harness-throw'"; Diagnostic = 'harness-throw' }
  )) {
    Set-Content -LiteralPath $harnessScript -Value $control.Content
    $rejected = $false
    try { $null = Invoke-Report -ScriptPath $harnessScript }
    catch { $rejected = $_.Exception.Message.Contains($control.Diagnostic) }
    Write-TestResult "Runspace.Rejects$($control.Name)" $rejected
  }
  Set-Content -LiteralPath $harnessScript -Value "`$global:ReportFixtureState = 'dirty'; exit 0"
  $null = Invoke-Report -ScriptPath $harnessScript
  Set-Content -LiteralPath $harnessScript -Value "if (Get-Variable -Name ReportFixtureState -Scope Global -ErrorAction SilentlyContinue) { exit 1 }; exit 0"
  $result = Invoke-Report -ScriptPath $harnessScript
  Write-TestResult 'Runspace.IsolatedGlobalState' ($result.ExitCode -eq 0)
  Set-Content -LiteralPath $harnessScript -Value "Write-Warning 'harness-warning'; exit 0"
  $result = Invoke-Report -ScriptPath $harnessScript
  Write-TestResult 'Runspace.CapturesWarnings' ($result.ExitCode -eq 0 -and $result.Output.Contains('harness-warning'))
  Set-Content -LiteralPath $harnessScript -Value 'git --version'
  $native = Invoke-Report -ScriptPath $harnessScript
  $nativeCli = Invoke-Report -ScriptPath $harnessScript -Cli
  Write-TestResult 'Runspace.NativeImplicitStatusMatchesCli' `
    ($native.ExitCode -eq 0 -and $native.ExitCode -eq $nativeCli.ExitCode -and $native.Output.Contains('git version'))
}
finally {
  Remove-Item -LiteralPath $harnessScript -Force -ErrorAction SilentlyContinue
}

Remove-Item -LiteralPath $xmlPath -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host ("=" * 60)
Write-Host ("Tests passed: {0}" -f $script:TestsPassed) -ForegroundColor Green
Write-Host ("Tests failed: {0}" -f $script:TestsFailed) -ForegroundColor $(if ($script:TestsFailed -gt 0) { "Red" } else { "Green" })
if ($script:FailedTests.Count -gt 0) {
  Write-Host "Failed tests:" -ForegroundColor Red
  foreach ($t in $script:FailedTests) { Write-Host "  - $t" -ForegroundColor Red }
}
Write-Host ("=" * 60)
exit $script:TestsFailed
