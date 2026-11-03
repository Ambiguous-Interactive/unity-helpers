Param(
    [Parameter(Mandatory = $true)][string]$ResultsPath,
    [int]$Top = 20,
    [double]$FixtureBudgetSeconds = 0,
    [switch]$FailOverBudget,
    [switch]$StepSummary
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot '../python~'), '--locked', '--no-dev',
    'python', (Join-Path $PSScriptRoot 'report_slow_tests.py'), '--results-path', $ResultsPath,
    '--top', $Top, '--fixture-budget-seconds',
    $FixtureBudgetSeconds.ToString([System.Globalization.CultureInfo]::InvariantCulture)
)
if ($FailOverBudget) { $arguments += '--fail-over-budget' }
if ($StepSummary) { $arguments += '--step-summary' }
& uv @arguments
exit $LASTEXITCODE
