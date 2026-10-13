function Get-UnityRootNUnitResultFile {
    param([string]$ResultsDirectory)

    $file = Get-Item -LiteralPath (Join-Path $ResultsDirectory 'results.xml') -ErrorAction SilentlyContinue
    if ($null -eq $file -or $file.PSIsContainer) { return $null }
    [xml]$document = Get-Content -LiteralPath $file.FullName -Raw
    if ($null -eq $document.SelectSingleNode('/test-run')) { return $null }
    return $file
}

function Assert-UnityCompilationFixtureResults {
    param([string]$Path, [string]$FixtureName)

    [xml]$document = Get-Content -LiteralPath $Path -Raw
    $cases = @($document.SelectNodes('//test-case'))
    if ($cases.Count -lt 1) {
        throw 'The isolated compilation fixture produced no test cases.'
    }
    foreach ($case in $cases) {
        if ($case.GetAttribute('classname') -cne $FixtureName -or
            $case.GetAttribute('result') -cne 'Passed') {
            throw 'Every isolated compilation case must belong to the required fixture and pass.'
        }
    }
}
