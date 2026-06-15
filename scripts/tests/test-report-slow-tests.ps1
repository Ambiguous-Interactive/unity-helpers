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
  param([string[]]$ExtraArgs)
  $reportArgs = @($reportScript, '-ResultsPath', $xmlPath) + $ExtraArgs
  $out = & pwsh -NoProfile -File @reportArgs 2>&1
  return @{ ExitCode = $LASTEXITCODE; Output = ($out -join "`n") }
}

Write-Host "Testing report-slow-tests.ps1..." -ForegroundColor White

# Ranking: slowest fixture first; slowest case is Test1 (60s).
$r1 = Invoke-Report -ExtraArgs @('-Top', '5')
Write-TestResult "Report.ExitsZero" ($r1.ExitCode -eq 0) "exit $($r1.ExitCode)"
$slowIdx = $r1.Output.IndexOf('A.SlowFixture')
$fastIdx = $r1.Output.IndexOf('A.FastFixture')
Write-TestResult "Report.SlowFixtureRankedFirst" ($slowIdx -ge 0 -and $fastIdx -ge 0 -and $slowIdx -lt $fastIdx) "SlowFixture should appear before FastFixture"
Write-TestResult "Report.ShowsSlowestCase" ($r1.Output -match 'A\.SlowFixture\.Test1') "Expected slowest case Test1 listed"
Write-TestResult "Report.ShowsRunTotal" ($r1.Output -match '5 tests') "Expected run total reported"

# Budget: warn-only when under -FailOverBudget; still exit 0.
$r2 = Invoke-Report -ExtraArgs @('-FixtureBudgetSeconds', '50')
Write-TestResult "Report.BudgetWarnExitZero" ($r2.ExitCode -eq 0) "warn-only should exit 0, got $($r2.ExitCode)"
Write-TestResult "Report.BudgetWarnEmitsWarning" ($r2.Output -match '::warning::Fixture over 50s budget: A.SlowFixture') "Expected ::warning:: for over-budget fixture"

# Budget: fail when -FailOverBudget and a fixture exceeds.
$r3 = Invoke-Report -ExtraArgs @('-FixtureBudgetSeconds', '50', '-FailOverBudget')
Write-TestResult "Report.BudgetFailExitOne" ($r3.ExitCode -eq 1) "fail-over-budget should exit 1, got $($r3.ExitCode)"
Write-TestResult "Report.BudgetFailEmitsError" ($r3.Output -match '::error::Fixture over 50s budget: A.SlowFixture') "Expected ::error:: for over-budget fixture"

# Budget: no fixture exceeds a high budget -> exit 0 even with -FailOverBudget.
$r4 = Invoke-Report -ExtraArgs @('-FixtureBudgetSeconds', '500', '-FailOverBudget')
Write-TestResult "Report.UnderBudgetExitZero" ($r4.ExitCode -eq 0) "no fixture over budget should exit 0, got $($r4.ExitCode)"

# StepSummary markdown.
$r5 = Invoke-Report -ExtraArgs @('-StepSummary')
Write-TestResult "Report.StepSummaryMarkdown" ($r5.Output -match '### Slowest tests' -and $r5.Output -match '\| Seconds \| Fixture \|') "Expected step-summary markdown table"

# Error handling.
$missing = & pwsh -NoProfile -File $reportScript -ResultsPath (Join-Path ([System.IO.Path]::GetTempPath()) 'does-not-exist-xyz.xml') 2>&1
Write-TestResult "Report.MissingFileExitOne" ($LASTEXITCODE -eq 1) "missing file should exit 1, got $LASTEXITCODE"

$badPath = [System.IO.Path]::ChangeExtension([System.IO.Path]::GetTempFileName(), '.xml')
[System.IO.File]::WriteAllText($badPath, "<not-xml <<<", (New-Object System.Text.UTF8Encoding($false)))
& pwsh -NoProfile -File $reportScript -ResultsPath $badPath 2>&1 | Out-Null
Write-TestResult "Report.MalformedXmlExitOne" ($LASTEXITCODE -eq 1) "malformed XML should exit 1, got $LASTEXITCODE"
Remove-Item -LiteralPath $badPath -Force -ErrorAction SilentlyContinue

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
