function Get-UnityIdleEditorIsolationFixtures {
    param([string]$UnityVersion)

    if ($UnityVersion -match '^([0-9]+)\.' -and [long]$Matches[1] -ge 6000) {
        'WallstopStudios.UnityHelpers.Tests.Editor.Tools.BuildProfileSceneSyncTests'
    }
}

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

    $arguments = @(
        'run', '--quiet', '--project', (Join-Path $PSScriptRoot '../../python~'), '--locked', '--no-dev',
        'python', (Join-Path $PSScriptRoot '../nunit_results.py'), '--isolated-fixtures', $Path
    ) + @($FixtureName)
    $PSNativeCommandUseErrorActionPreference = $false
    $diagnostic = (& uv @arguments) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0) {
        if ([string]::IsNullOrWhiteSpace($diagnostic)) {
            throw "The isolated fixture verdict exited $LASTEXITCODE."
        }
        throw $diagnostic
    }
}
