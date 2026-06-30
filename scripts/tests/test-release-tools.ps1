Param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0

function Write-Info {
    param([string]$Message)
    if ($VerboseOutput) {
        Write-Host "[test-release-tools] $Message" -ForegroundColor Cyan
    }
}

function Write-TestResult {
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Message = ''
    )

    if ($Passed) {
        Write-Host "  [PASS] $TestName" -ForegroundColor Green
        $script:TestsPassed++
    } else {
        Write-Host "  [FAIL] $TestName" -ForegroundColor Red
        if ($Message) {
            Write-Host "         $Message" -ForegroundColor Yellow
        }
        $script:TestsFailed++
    }
}

function New-ReleaseFixture {
    param(
        [string]$Version = '1.2.3',
        [string]$Changelog
    )

    $root = Join-Path ([System.IO.Path]::GetTempPath()) "release-tools-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $root -Force | Out-Null

    if ([string]::IsNullOrWhiteSpace($Changelog)) {
        $Changelog = @(
            '# Changelog',
            '',
            '## [Unreleased]',
            '',
            '### Added',
            '',
            '- A release-worthy change.',
            '',
            '## [1.2.3]',
            '',
            '### Fixed',
            '',
            '- Old fix.',
            ''
        ) -join "`n"
    }

    $packageJson = @(
        '{',
        '  "name": "com.example.fixture",',
        "  `"version`": `"$Version`",",
        '  "displayName": "Fixture"',
        '}',
        ''
    ) -join "`n"

    $packageLock = @(
        '{',
        '  "name": "com.example.fixture",',
        '  "version": "0.0.1",',
        '  "lockfileVersion": 3,',
        '  "packages": {',
        '    "": {',
        '      "name": "com.example.fixture",',
        '      "version": "0.0.1"',
        '    }',
        '  }',
        '}',
        ''
    ) -join "`n"

    Set-Content -Path (Join-Path $root 'package.json') -Value $packageJson -Encoding UTF8 -NoNewline
    Set-Content -Path (Join-Path $root 'package-lock.json') -Value $packageLock -Encoding UTF8 -NoNewline
    Set-Content -Path (Join-Path $root 'CHANGELOG.md') -Value $Changelog -Encoding UTF8 -NoNewline

    return $root
}

function Remove-ReleaseFixture {
    param([string]$Path)
    if ($Path -and (Test-Path -LiteralPath $Path)) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repoRoot 'scripts/release-tools/release-helpers.ps1')

Write-Host 'Testing release tooling...' -ForegroundColor White

try {
    Write-TestResult `
        -TestName 'Get-NextReleaseVersion bumps semver components' `
        -Passed (
            (Get-NextReleaseVersion -CurrentVersion '1.2.3' -Bump patch) -eq '1.2.4' -and
            (Get-NextReleaseVersion -CurrentVersion '1.2.3' -Bump minor) -eq '1.3.0' -and
            (Get-NextReleaseVersion -CurrentVersion '1.2.3' -Bump major) -eq '2.0.0'
        )

    $invalidVersionRejected = $false
    try {
        [void](Get-NextReleaseVersion -CurrentVersion '1.2.3' -Version '1.02.4')
    } catch {
        $invalidVersionRejected = $_.Exception.Message -match 'Invalid semver'
    }
    Write-TestResult -TestName 'Explicit versions reject leading zeroes' -Passed $invalidVersionRejected

    $nonIncreasingRejected = $false
    try {
        [void](Get-NextReleaseVersion -CurrentVersion '1.2.3' -Version '1.2.3')
    } catch {
        $nonIncreasingRejected = $_.Exception.Message -match 'strictly greater'
    }
    Write-TestResult -TestName 'Explicit versions must increase' -Passed $nonIncreasingRejected

    $fixture = New-ReleaseFixture
    try {
        $result = Invoke-ReleasePreparation -RepoRoot $fixture -Bump minor -Date '2026-06-30'
        $packageJson = Get-Content -Path (Join-Path $fixture 'package.json') -Raw | ConvertFrom-Json
        $packageLock = Get-Content -Path (Join-Path $fixture 'package-lock.json') -Raw | ConvertFrom-Json -AsHashtable
        $changelog = Get-Content -Path (Join-Path $fixture 'CHANGELOG.md') -Raw

        Write-TestResult `
            -TestName 'Release preparation updates package files' `
            -Passed (
                $result.NextVersion -eq '1.3.0' -and
                $packageJson.version -eq '1.3.0' -and
                $packageLock['version'] -eq '1.3.0' -and
                $packageLock['packages']['']['version'] -eq '1.3.0'
            )

        Write-TestResult `
            -TestName 'Release preparation rotates changelog with a dated heading' `
            -Passed (
                $changelog -match '(?m)^## \[Unreleased\]\s*\n\s*## \[1\.3\.0\] - 2026-06-30\s*$' -and
                $changelog.Contains('- A release-worthy change.') -and
                $changelog.Contains('## [1.2.3]')
            ) `
            -Message $changelog

        $notes = New-ReleaseNotes -RepoRoot $fixture -Version '1.3.0' -Footer
        Write-TestResult `
            -TestName 'Release notes extract the rotated changelog section with footer' `
            -Passed (
                $notes.Contains('- A release-worthy change.') -and
                $notes.Contains('com.example.fixture@1.3.0') -and
                -not $notes.Contains('Old fix')
            ) `
            -Message $notes
    } finally {
        Remove-ReleaseFixture -Path $fixture
    }

    $emptyChangelog = @(
        '# Changelog',
        '',
        '## [Unreleased]',
        '',
        '### Added',
        '',
        '## [1.2.3]',
        '',
        '- Old.',
        ''
    ) -join "`n"
    $fixture = New-ReleaseFixture -Changelog $emptyChangelog
    try {
        $emptyRejected = $false
        try {
            [void](Invoke-ReleasePreparation -RepoRoot $fixture -Bump patch -Date '2026-06-30')
        } catch {
            $emptyRejected = $_.Exception.Message -match 'no release-note content'
        }
        Write-TestResult -TestName 'Empty Unreleased sections are rejected' -Passed $emptyRejected
    } finally {
        Remove-ReleaseFixture -Path $fixture
    }

    $fencedChangelog = @(
        '# Changelog',
        '',
        '## [Unreleased]',
        '',
        '### Added',
        '',
        '- Example:',
        '',
        '```markdown',
        '## [1.2.4]',
        '```',
        '',
        '- Still part of Unreleased.',
        '',
        '## [1.2.3]',
        '',
        '- Old.',
        ''
    ) -join "`n"
    $fixture = New-ReleaseFixture -Changelog $fencedChangelog
    try {
        $result = Invoke-ReleasePreparation -RepoRoot $fixture -Bump patch -Date '2026-06-30'
        $changelog = Get-Content -Path (Join-Path $fixture 'CHANGELOG.md') -Raw
        Write-TestResult `
            -TestName 'Fenced changelog headings do not block release rotation' `
            -Passed (
                $result.NextVersion -eq '1.2.4' -and
                $changelog.Contains('```markdown') -and
                $changelog.Contains('## [1.2.4] - 2026-06-30') -and
                $changelog.Contains('## [1.2.4]' + "`n" + '```')
            ) `
            -Message $changelog
    } finally {
        Remove-ReleaseFixture -Path $fixture
    }
} catch {
    Write-TestResult -TestName 'Unexpected release-tool exception' -Passed $false -Message $_.Exception.ToString()
}

Write-Host ''
Write-Host 'Results:' -ForegroundColor Magenta
Write-Host "  Passed: $script:TestsPassed"
Write-Host "  Failed: $script:TestsFailed"

if ($script:TestsFailed -gt 0) {
    exit 1
}

exit 0
