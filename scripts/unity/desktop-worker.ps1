[CmdletBinding()]
param([Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9+/]+=*$')][string]$EncodedWorker)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot '../python~'), '--locked', '--no-dev',
    'python', (Join-Path $PSScriptRoot 'desktop_worker.py'),
    '--shell', [Environment]::ProcessPath, '--encoded-worker', $EncodedWorker
)
& uv @arguments
exit $LASTEXITCODE
