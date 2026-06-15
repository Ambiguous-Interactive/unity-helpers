<#
.SYNOPSIS
    Reports the slowest test fixtures and test cases from an NUnit3 results.xml,
    and (optionally) enforces a per-fixture wall-clock budget.

.DESCRIPTION
    This is the measurement backbone for keeping the Unity test suite fast. Unity
    EditMode tests run serially on a single self-hosted seat, so the only way to
    cut wall-clock is to make individual fixtures cheaper - and you cannot fix
    what you cannot see. After every CI test run this script ranks the slowest
    fixtures and cases (by NUnit `duration`, in seconds) so regressions are
    obvious, and writes a GitHub step-summary table when run in Actions.

    With -FailOverBudget, the script also acts as a "forever" guardrail: it fails
    (exit 1) if any single fixture exceeds -FixtureBudgetSeconds. The budget can
    start lenient and be tightened over time as fixtures are optimized.

.PARAMETER ResultsPath
    Path to an NUnit3 results XML (e.g. editmode-results.xml).

.PARAMETER Top
    How many slowest fixtures/cases to list. Default 20.

.PARAMETER FixtureBudgetSeconds
    If > 0, fixtures slower than this are flagged (and fail the run with -FailOverBudget).

.PARAMETER FailOverBudget
    Exit 1 when any fixture exceeds FixtureBudgetSeconds. Default: report only.

.PARAMETER StepSummary
    Emit a GitHub-flavored markdown summary (also appended to $env:GITHUB_STEP_SUMMARY when set).

.EXAMPLE
    pwsh -NoProfile -File scripts/unity/report-slow-tests.ps1 -ResultsPath editmode-results.xml
    pwsh -NoProfile -File scripts/unity/report-slow-tests.ps1 -ResultsPath r.xml -FixtureBudgetSeconds 120 -FailOverBudget
#>
Param(
    [Parameter(Mandatory = $true)][string]$ResultsPath,
    [int]$Top = 20,
    [double]$FixtureBudgetSeconds = 0,
    [switch]$FailOverBudget,
    [switch]$StepSummary
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ResultsPath -PathType Leaf)) {
    Write-Host "[slow-tests] ERROR: results file not found: $ResultsPath" -ForegroundColor Red
    exit 1
}

# Invariant-culture parse so a comma-decimal locale cannot mis-read durations.
function ConvertTo-Seconds {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return 0.0 }
    [double]$result = 0.0
    if ([double]::TryParse(
            $Value,
            [System.Globalization.NumberStyles]::Float,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [ref]$result)) {
        return $result
    }
    # Unity's NUnit3 writer always emits invariant (dot) decimals. A non-empty value
    # that does NOT parse here is genuinely malformed; warn LOUDLY rather than let a
    # slow fixture read as 0s and silently slip a -FailOverBudget gate.
    Write-Host "::warning::Unparseable duration '$Value' in '$ResultsPath'; treating as 0s."
    return 0.0
}

try {
    [xml]$xml = Get-Content -LiteralPath $ResultsPath -Raw
}
catch {
    Write-Host "[slow-tests] ERROR: could not parse XML '$ResultsPath': $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Use GetAttribute (returns '' when absent) so a node missing duration/fullname
# never throws under StrictMode.
function Get-Attr {
    param($Node, [string]$Name)
    if ($null -eq $Node) { return '' }
    return $Node.GetAttribute($Name)
}

$fixtures = New-Object System.Collections.Generic.List[object]
foreach ($node in $xml.SelectNodes("//test-suite[@type='TestFixture']")) {
    $name = Get-Attr $node 'fullname'
    if (-not $name) { $name = Get-Attr $node 'name' }
    $fixtures.Add([PSCustomObject]@{
            Name     = $name
            Seconds  = ConvertTo-Seconds (Get-Attr $node 'duration')
            Total    = Get-Attr $node 'total'
        })
}

$cases = New-Object System.Collections.Generic.List[object]
foreach ($node in $xml.SelectNodes("//test-case")) {
    $name = Get-Attr $node 'fullname'
    if (-not $name) { $name = Get-Attr $node 'name' }
    $cases.Add([PSCustomObject]@{
            Name    = $name
            Seconds = ConvertTo-Seconds (Get-Attr $node 'duration')
        })
}

$runNode = $xml.SelectSingleNode('//test-run')
$runSeconds = ConvertTo-Seconds (Get-Attr $runNode 'duration')
$runTotal = Get-Attr $runNode 'total'

# Descending by duration; ordinal name tiebreak keeps output deterministic.
$slowFixtures = @($fixtures | Sort-Object -Property @{Expression = 'Seconds'; Descending = $true }, @{Expression = 'Name'; Descending = $false } | Select-Object -First $Top)
$slowCases = @($cases | Sort-Object -Property @{Expression = 'Seconds'; Descending = $true }, @{Expression = 'Name'; Descending = $false } | Select-Object -First $Top)

$overBudget = @()
if ($FixtureBudgetSeconds -gt 0) {
    $overBudget = @($fixtures | Where-Object { $_.Seconds -gt $FixtureBudgetSeconds } | Sort-Object -Property @{Expression = 'Seconds'; Descending = $true })
}

Write-Host ("=" * 70)
Write-Host "[slow-tests] $ResultsPath" -ForegroundColor Cyan
Write-Host ("[slow-tests] Run total: {0} tests in {1:N1}s ({2:N1} min)" -f $runTotal, $runSeconds, ($runSeconds / 60.0))
Write-Host ("[slow-tests] Top $Top slowest fixtures:") -ForegroundColor Cyan
foreach ($f in $slowFixtures) {
    Write-Host ("  {0,8:N2}s  {1}" -f $f.Seconds, $f.Name)
}
Write-Host ("[slow-tests] Top $Top slowest test cases:") -ForegroundColor Cyan
foreach ($c in $slowCases) {
    Write-Host ("  {0,8:N3}s  {1}" -f $c.Seconds, $c.Name)
}

if ($StepSummary) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("### Slowest tests - $([System.IO.Path]::GetFileName($ResultsPath))")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine(("Run total: **{0}** tests in **{1:N1}s** ({2:N1} min)" -f $runTotal, $runSeconds, ($runSeconds / 60.0)))
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Seconds | Fixture |")
    [void]$sb.AppendLine("| --- | --- |")
    foreach ($f in $slowFixtures) {
        # Backtick-wrap so a generic fixture name like Foo<System.Int32> renders
        # literally (GitHub's summary renderer would otherwise strip the <...> tag);
        # escape any pipe so it cannot break the table column.
        $safeName = '`' + ($f.Name -replace '\|', '\|') + '`'
        [void]$sb.AppendLine(("| {0:N2} | {1} |" -f $f.Seconds, $safeName))
    }
    $summary = $sb.ToString()
    Write-Host $summary
    if ($env:GITHUB_STEP_SUMMARY) {
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $summary
    }
}

if ($overBudget.Count -gt 0) {
    Write-Host ("[slow-tests] $($overBudget.Count) fixture(s) exceed the ${FixtureBudgetSeconds}s budget:") -ForegroundColor Yellow
    foreach ($f in $overBudget) {
        $msg = "Fixture over ${FixtureBudgetSeconds}s budget: $($f.Name) took $([math]::Round($f.Seconds,1))s"
        if ($FailOverBudget) { Write-Host "::error::$msg" } else { Write-Host "::warning::$msg" }
    }
    if ($FailOverBudget) {
        Write-Host ("=" * 70)
        exit 1
    }
}

Write-Host ("=" * 70)
exit 0
