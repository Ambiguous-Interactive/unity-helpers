function Get-UnityMigrationIsolationFixtures {
    $namespace = 'WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration.'
    foreach ($name in @('OdinMigrationApiTests', 'OdinMigrationBackupRecoveryTests',
            'OdinMigrationDictionaryImportVerificationTests', 'OdinMigrationDictionaryApplyIntegrationTests',
            'OdinMigrationApplyRecordTests')) {
        $namespace + $name
    }
}

function Get-UnityMigrationIsolationGroups {
    $reloadFixture = 'WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration.OdinMigrationDictionaryApplyIntegrationTests'
    [pscustomobject]@{
        Name = 'Contracts'
        Fixtures = @(Get-UnityMigrationIsolationFixtures | Where-Object { $_ -cne $reloadFixture })
    }
    [pscustomobject]@{
        Name = 'OdinMigrationDictionaryApplyIntegrationTests'
        Fixtures = @($reloadFixture)
    }
}

function Get-UnityRootNUnitResultFile {
    param([string]$ResultsDirectory)

    $file = Get-Item -LiteralPath (Join-Path $ResultsDirectory 'results.xml') -ErrorAction SilentlyContinue
    if ($null -eq $file -or $file.PSIsContainer) { return $null }
    try {
        [xml]$document = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction Stop
        if ($null -eq $document -or $null -eq $document.SelectSingleNode('/test-run')) { return $null }
        return $file
    } catch {
        return $null
    }
}

function Assert-UnityCompilationFixtureResults {
    param([string]$Path, [string[]]$FixtureName)

    [xml]$document = Get-Content -LiteralPath $Path -Raw
    $cases = @($document.SelectNodes('//test-case'))
    if ($cases.Count -lt 1) {
        throw 'The isolated compilation fixture produced no test cases.'
    }
    $required = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $observed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $FixtureName) {
        if ([string]::IsNullOrWhiteSpace($name) -or -not $required.Add($name)) {
            throw 'Required isolated fixture names must be nonempty and unique.'
        }
    }
    if ($required.Count -eq 0) { throw 'At least one isolated fixture must be required.' }
    foreach ($case in $cases) {
        $name = $case.GetAttribute('classname')
        if (-not $required.Contains($name) -or $case.GetAttribute('result') -cne 'Passed') {
            throw 'Every isolated compilation case must belong to a required fixture and pass.'
        }
        [void]$observed.Add($name)
    }
    if (-not $observed.SetEquals($required)) { throw 'A required isolated fixture did not execute.' }
    if ($null -ne $document.SelectSingleNode('/test-run[@result="Failed" or number(@failed) > 0] | //test-suite[@result="Failed"]')) {
        throw 'Isolated fixture roots and suites must pass.'
    }
}
