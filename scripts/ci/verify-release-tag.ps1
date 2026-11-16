[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Tag,
    [Parameter(Mandatory = $true)][AllowEmptyString()][string]$SourceRef,
    [Parameter(Mandatory = $true)][string]$PackageJsonPath,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$UnboundArguments
)
$ErrorActionPreference = 'Stop'
if ($UnboundArguments -and 0 -lt $UnboundArguments.Count) {
    [Console]::Error.WriteLine("Unbound arguments: $($UnboundArguments -join ', '). Pass -Tag, -SourceRef, and -PackageJsonPath explicitly.")
    exit 64
}
$arguments = @(
    'run', '--quiet', '--project', (Join-Path $PSScriptRoot '../python~'), '--locked', '--no-dev',
    'python', (Join-Path $PSScriptRoot 'verify_release_tag.py'),
    "--tag=$Tag", "--source-ref=$SourceRef", "--package-json=$PackageJsonPath"
)
& uv @arguments
exit $LASTEXITCODE
