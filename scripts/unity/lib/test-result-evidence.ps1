function Assert-TestResultEvidenceCheckout {
    param([string]$RepositoryRoot)

    $wrapperRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
    foreach ($relative in @(
            'scripts/unity/lib/test-result-evidence.ps1',
            'scripts/unity/test_result_evidence.py',
            'scripts/python~/pyproject.toml',
            'scripts/python~/uv.lock')) {
        $actual = (Get-FileHash -LiteralPath (Join-Path $wrapperRoot $relative) -Algorithm SHA256 -ErrorAction Stop).Hash
        $recorded = (Get-FileHash -LiteralPath (Join-Path $RepositoryRoot $relative) -Algorithm SHA256 -ErrorAction Stop).Hash
        if ($actual -cne $recorded) { throw "Test evidence reader differs from the repository inputs: $relative" }
    }
}

function Read-TestResultEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][ValidateSet('NUnit', 'Trx')][string]$Format,
        [string]$RepositoryRoot
    )

    $file = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($file.PSIsContainer -or $file -isnot [IO.FileInfo]) {
        throw 'Test evidence XML must be a nonempty file of at most 32 MiB.'
    }
    $arguments = @(
        'run', '--quiet', '--project', (Join-Path $PSScriptRoot '../../python~'), '--locked', '--no-dev',
        'python', (Join-Path $PSScriptRoot '../test_result_evidence.py'), '--format', $Format, '--', $file.FullName
    )
    if ($RepositoryRoot) { Assert-TestResultEvidenceCheckout $RepositoryRoot }
    $PSNativeCommandUseErrorActionPreference = $false
    $output = @(& uv @arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $diagnostic = $output -join [Environment]::NewLine
        if (-not [string]::IsNullOrWhiteSpace($diagnostic)) { throw $diagnostic }
        throw "The test evidence reader exited $LASTEXITCODE."
    }

    if ($RepositoryRoot) { Assert-TestResultEvidenceCheckout $RepositoryRoot }

    # Keep identities and timestamps as strings on every supported PowerShell version.
    $json = ($output | Where-Object { $_ -is [string] }) -join [Environment]::NewLine
    $document = [Text.Json.JsonDocument]::Parse($json)
    try {
        $root = $document.RootElement
        $cases = @(
            foreach ($case in $root.GetProperty('Cases').EnumerateArray()) {
                [pscustomobject]@{
                    Fixture = $case.GetProperty('Fixture').GetString()
                    FullName = $case.GetProperty('FullName').GetString()
                    Result = $case.GetProperty('Result').GetString()
                    AssemblyPath = $case.GetProperty('AssemblyPath').GetString()
                    TestId = $case.GetProperty('TestId').GetString()
                    ExecutionId = $case.GetProperty('ExecutionId').GetString()
                    AdapterTypeName = $case.GetProperty('AdapterTypeName').GetString()
                }
            }
        )
        return [pscustomobject]@{
            RunId = $root.GetProperty('RunId').GetString()
            StartedUtc = $root.GetProperty('StartedUtc').GetString()
            CompletedUtc = $root.GetProperty('CompletedUtc').GetString()
            Cases = $cases
            Total = $root.GetProperty('Total').GetInt32()
            Passed = $root.GetProperty('Passed').GetInt32()
            Failed = $root.GetProperty('Failed').GetInt32()
            Skipped = $root.GetProperty('Skipped').GetInt32()
            Inconclusive = $root.GetProperty('Inconclusive').GetInt32()
        }
    } finally { $document.Dispose() }
}
