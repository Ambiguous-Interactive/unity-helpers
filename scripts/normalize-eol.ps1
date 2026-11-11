[CmdletBinding(PositionalBinding = $false)]
param(
    [string[]]$Paths,
    [string]$ModifiedPathList,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPaths,
    [switch]$DryRun,
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev',
    'python', (Join-Path $PSScriptRoot 'normalize_eol.py')
)
if ($DryRun) { $arguments += '--dry-run' }
if ($VerboseOutput) { $arguments += '--verbose' }
if (-not [string]::IsNullOrWhiteSpace($ModifiedPathList)) {
    $arguments += '--modified-path-list=' + $ModifiedPathList
}
$selectedPaths = @($Paths) + @($AdditionalPaths)
$selectedPaths = @($selectedPaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($selectedPaths.Count -gt 0) {
    $arguments += '--paths'
    $arguments += @($selectedPaths | ForEach-Object {
        [System.IO.Path]::GetFullPath($_, $PWD.ProviderPath)
    })
}
if (-not (Get-Command uv -ErrorAction SilentlyContinue)) {
    [Console]::Error.WriteLine('[normalize-eol] uv is required. Install uv, then rerun npm run eol:fix.')
    exit 1
}
& uv @arguments
exit $LASTEXITCODE
