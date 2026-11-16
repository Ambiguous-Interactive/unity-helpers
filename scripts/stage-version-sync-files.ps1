# Native index adapter: retain the existing cross-process mutex and retry policy.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'git-staging-helpers.ps1')
$request = [Console]::In.ReadToEnd() | ConvertFrom-Json -AsHashtable
if (-not $request.ContainsKey('files') -or $request.files -isnot [array] -or $request.files.Count -eq 0) {
    throw 'Expected a nonempty literal file array.'
}
foreach ($item in $request.files) {
    if ($item -isnot [string] -or [string]::IsNullOrWhiteSpace($item) -or $item.Contains([char]0)) {
        throw 'Expected literal nonempty file paths.'
    }
}
Assert-GitAvailable | Out-Null
$repositoryInfo = Get-GitRepositoryInfo
$code = Invoke-GitAddWithRetry -Items ([string[]]$request.files) -IndexLockPath $repositoryInfo.IndexLockPath
exit $code
