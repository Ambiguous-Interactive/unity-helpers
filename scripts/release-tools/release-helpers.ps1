Set-StrictMode -Version Latest

$script:ReleaseSemverPattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$'
$script:ReleaseBumpKinds = @('major', 'minor', 'patch')

function ConvertTo-ReleaseSemverParts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $match = [regex]::Match($Version, $script:ReleaseSemverPattern)
    if (-not $match.Success) {
        throw "Invalid semver '$Version'. Expected X.Y.Z with numeric components and no leading zeroes."
    }

    return [pscustomobject]@{
        Major = [int]$match.Groups[1].Value
        Minor = [int]$match.Groups[2].Value
        Patch = [int]$match.Groups[3].Value
    }
}

function Compare-ReleaseSemver {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Left,
        [Parameter(Mandatory = $true)]
        [string]$Right
    )

    $leftParts = ConvertTo-ReleaseSemverParts -Version $Left
    $rightParts = ConvertTo-ReleaseSemverParts -Version $Right

    foreach ($partName in @('Major', 'Minor', 'Patch')) {
        if ($leftParts.$partName -gt $rightParts.$partName) {
            return 1
        }
        if ($leftParts.$partName -lt $rightParts.$partName) {
            return -1
        }
    }

    return 0
}

function Get-NextReleaseVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CurrentVersion,
        [ValidateSet('major', 'minor', 'patch')]
        [string]$Bump = 'patch',
        [string]$Version = ''
    )

    $current = ConvertTo-ReleaseSemverParts -Version $CurrentVersion
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        [void](ConvertTo-ReleaseSemverParts -Version $Version)
        if ((Compare-ReleaseSemver -Left $Version -Right $CurrentVersion) -le 0) {
            throw "Explicit version $Version must be strictly greater than the current version $CurrentVersion."
        }
        return $Version
    }

    switch ($Bump) {
        'major' { return "$($current.Major + 1).0.0" }
        'minor' { return "$($current.Major).$($current.Minor + 1).0" }
        'patch' { return "$($current.Major).$($current.Minor).$($current.Patch + 1)" }
        default { throw "Unknown bump kind '$Bump'. Expected: $($script:ReleaseBumpKinds -join ', ')." }
    }
}

function Normalize-ReleaseText {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    return ($Content -replace "`r`n", "`n") -replace "`r", "`n"
}

function Get-ChangelogFenceMask {
    param(
        [string[]]$Lines
    )

    $mask = New-Object bool[] $Lines.Count
    $inFence = $false
    $fenceMarker = ''

    for ($index = 0; $index -lt $Lines.Count; $index++) {
        $line = $Lines[$index]
        $trimmed = $line.TrimStart()
        $isFenceLine = $false

        if ($trimmed -match '^(?<marker>`{3,}|~{3,})') {
            $marker = $Matches['marker']
            $markerPrefix = $marker.Substring(0, 1)
            if (-not $inFence) {
                $inFence = $true
                $fenceMarker = $markerPrefix
                $isFenceLine = $true
            } elseif ($fenceMarker -eq $markerPrefix) {
                $isFenceLine = $true
                $mask[$index] = $true
                $inFence = $false
                $fenceMarker = ''
                continue
            }
        }

        if ($inFence -or $isFenceLine) {
            $mask[$index] = $true
        }
    }

    return $mask
}

function Test-ChangelogVersionHeading {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $lines = (Normalize-ReleaseText -Content $Content).Split("`n")
    $fenced = Get-ChangelogFenceMask -Lines $lines
    $escaped = [regex]::Escape($Version)

    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($fenced[$index]) {
            continue
        }
        if ($lines[$index] -match "^## \[$escaped\](?: - \d{4}-\d{2}-\d{2})?$") {
            return $true
        }
    }

    return $false
}

function Get-ChangelogSection {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $lines = (Normalize-ReleaseText -Content $Content).Split("`n")
    $fenced = Get-ChangelogFenceMask -Lines $lines
    $escaped = [regex]::Escape($Version)
    $headingIndex = -1

    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($fenced[$index]) {
            continue
        }
        if ($lines[$index] -match "^## \[$escaped\](?: - \d{4}-\d{2}-\d{2})?$") {
            $headingIndex = $index
            break
        }
    }

    if ($headingIndex -lt 0) {
        throw "CHANGELOG.md has no '## [$Version]' section."
    }

    $endIndex = $lines.Count
    for ($index = $headingIndex + 1; $index -lt $lines.Count; $index++) {
        if (-not $fenced[$index] -and $lines[$index].StartsWith('## ')) {
            $endIndex = $index
            break
        }
    }

    $body = @()
    if ($headingIndex + 1 -lt $endIndex) {
        $body = @($lines[($headingIndex + 1)..($endIndex - 1)])
    }

    while ($body.Count -gt 0 -and [string]::IsNullOrWhiteSpace($body[0])) {
        $body = @($body | Select-Object -Skip 1)
    }
    while ($body.Count -gt 0 -and [string]::IsNullOrWhiteSpace($body[$body.Count - 1])) {
        $body = @($body | Select-Object -First ($body.Count - 1))
    }

    $hasContent = $false
    foreach ($line in $body) {
        if (-not [string]::IsNullOrWhiteSpace($line) -and -not $line.StartsWith('### ')) {
            $hasContent = $true
            break
        }
    }

    if (-not $hasContent) {
        throw "CHANGELOG.md section '## [$Version]' has no release-note content."
    }

    return ($body -join "`n")
}

function Update-ReleaseChangelogContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,
        [Parameter(Mandatory = $true)]
        [string]$Version,
        [Parameter(Mandatory = $true)]
        [string]$Date
    )

    $normalized = Normalize-ReleaseText -Content $Content
    $lines = $normalized.Split("`n")
    $fenced = Get-ChangelogFenceMask -Lines $lines
    $unreleasedIndexes = @()
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if (-not $fenced[$index] -and $lines[$index] -eq '## [Unreleased]') {
            $unreleasedIndexes += $index
        }
    }

    if ($unreleasedIndexes.Count -ne 1) {
        throw "CHANGELOG.md must contain exactly one '## [Unreleased]' heading; found $($unreleasedIndexes.Count)."
    }

    $unreleasedIndex = $unreleasedIndexes[0]
    $nextHeadingIndex = $lines.Count
    for ($index = $unreleasedIndex + 1; $index -lt $lines.Count; $index++) {
        if (-not $fenced[$index] -and $lines[$index].StartsWith('## ')) {
            $nextHeadingIndex = $index
            break
        }
    }

    $block = @()
    if ($unreleasedIndex + 1 -lt $nextHeadingIndex) {
        $block = @($lines[($unreleasedIndex + 1)..($nextHeadingIndex - 1)])
    }

    while ($block.Count -gt 0 -and [string]::IsNullOrWhiteSpace($block[0])) {
        $block = @($block | Select-Object -Skip 1)
    }
    while ($block.Count -gt 0 -and [string]::IsNullOrWhiteSpace($block[$block.Count - 1])) {
        $block = @($block | Select-Object -First ($block.Count - 1))
    }

    $hasContent = $false
    foreach ($line in $block) {
        if (-not [string]::IsNullOrWhiteSpace($line) -and -not $line.StartsWith('### ')) {
            $hasContent = $true
            break
        }
    }

    if (Test-ChangelogVersionHeading -Content $normalized -Version $Version) {
        if ($hasContent) {
            throw "CHANGELOG.md already contains '## [$Version]' but '## [Unreleased]' still has release-note content."
        }

        return [pscustomobject]@{
            Content = $normalized.TrimEnd() + "`n"
            Rotated = $false
        }
    }

    if (-not $hasContent) {
        throw "The '## [Unreleased]' section has no release-note content."
    }

    $rotatedLines = [System.Collections.Generic.List[string]]::new()
    for ($index = 0; $index -le $unreleasedIndex; $index++) {
        [void]$rotatedLines.Add($lines[$index])
    }
    [void]$rotatedLines.Add('')
    [void]$rotatedLines.Add("## [$Version] - $Date")
    [void]$rotatedLines.Add('')
    foreach ($line in $block) {
        [void]$rotatedLines.Add($line)
    }

    if ($nextHeadingIndex -lt $lines.Count) {
        [void]$rotatedLines.Add('')
        for ($index = $nextHeadingIndex; $index -lt $lines.Count; $index++) {
            [void]$rotatedLines.Add($lines[$index])
        }
    }

    return [pscustomobject]@{
        Content = (($rotatedLines -join "`n").TrimEnd() + "`n")
        Rotated = $true
    }
}

function Set-ReleaseFileContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    $encoding = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Update-PackageJsonVersionContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,
        [Parameter(Mandatory = $true)]
        [string]$CurrentVersion,
        [Parameter(Mandatory = $true)]
        [string]$NextVersion
    )

    $parsed = $Content | ConvertFrom-Json
    if ([string]$parsed.version -ne $CurrentVersion) {
        throw "package.json parsed version '$($parsed.version)' did not match expected current version '$CurrentVersion'."
    }

    $pattern = '("version"\s*:\s*")' + [regex]::Escape($CurrentVersion) + '(")'
    $matches = [regex]::Matches($Content, $pattern)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one package.json version property for '$CurrentVersion'; found $($matches.Count)."
    }

    $updated = [regex]::Replace($Content, $pattern, "`${1}$NextVersion`${2}", 1)
    $updatedParsed = $updated | ConvertFrom-Json
    if ([string]$updatedParsed.version -ne $NextVersion) {
        throw "package.json rewrite verification failed; parsed version is '$($updatedParsed.version)', expected '$NextVersion'."
    }

    return (Normalize-ReleaseText -Content $updated).TrimEnd() + "`n"
}

function Update-PackageLockVersionContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,
        [Parameter(Mandatory = $true)]
        [string]$NextVersion
    )

    $lockJson = $Content | ConvertFrom-Json -AsHashtable
    if ($lockJson.ContainsKey('version')) {
        $lockJson['version'] = $NextVersion
    }

    if ($lockJson.ContainsKey('packages') -and $lockJson['packages'].ContainsKey('')) {
        $rootPackage = $lockJson['packages']['']
        if ($rootPackage.ContainsKey('version')) {
            $rootPackage['version'] = $NextVersion
        }
    }

    return (($lockJson | ConvertTo-Json -Depth 100) -replace "`r`n", "`n").TrimEnd() + "`n"
}

function Invoke-ReleasePreparation {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [ValidateSet('major', 'minor', 'patch')]
        [string]$Bump = 'patch',
        [string]$Version = '',
        [string]$Date = (Get-Date -Format 'yyyy-MM-dd'),
        [switch]$DryRun
    )

    if ($Date -notmatch '^\d{4}-\d{2}-\d{2}$') {
        throw "Release date '$Date' must use YYYY-MM-DD format."
    }

    $resolvedRepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
    $packageJsonPath = Join-Path $resolvedRepoRoot 'package.json'
    $packageLockPath = Join-Path $resolvedRepoRoot 'package-lock.json'
    $changelogPath = Join-Path $resolvedRepoRoot 'CHANGELOG.md'

    if (-not (Test-Path -LiteralPath $packageJsonPath -PathType Leaf)) {
        throw "package.json not found at $packageJsonPath."
    }
    if (-not (Test-Path -LiteralPath $changelogPath -PathType Leaf)) {
        throw "CHANGELOG.md not found at $changelogPath."
    }

    $packageJsonContent = [System.IO.File]::ReadAllText($packageJsonPath)
    $packageJson = $packageJsonContent | ConvertFrom-Json
    $currentVersion = [string]$packageJson.version
    [void](ConvertTo-ReleaseSemverParts -Version $currentVersion)
    $nextVersion = Get-NextReleaseVersion -CurrentVersion $currentVersion -Bump $Bump -Version $Version

    $updatedPackageJson = Update-PackageJsonVersionContent `
        -Content $packageJsonContent `
        -CurrentVersion $currentVersion `
        -NextVersion $nextVersion

    $changelogContent = [System.IO.File]::ReadAllText($changelogPath)
    $updatedChangelog = Update-ReleaseChangelogContent `
        -Content $changelogContent `
        -Version $nextVersion `
        -Date $Date

    $updatedPackageLock = $null
    $packageLockUpdated = $false
    if (Test-Path -LiteralPath $packageLockPath -PathType Leaf) {
        $packageLockContent = [System.IO.File]::ReadAllText($packageLockPath)
        $updatedPackageLock = Update-PackageLockVersionContent -Content $packageLockContent -NextVersion $nextVersion
        $packageLockUpdated = $true
    }

    if (-not $DryRun) {
        if ($updatedChangelog.Rotated) {
            Set-ReleaseFileContent -Path $changelogPath -Content $updatedChangelog.Content
        }
        Set-ReleaseFileContent -Path $packageJsonPath -Content $updatedPackageJson
        if ($packageLockUpdated) {
            Set-ReleaseFileContent -Path $packageLockPath -Content $updatedPackageLock
        }
    }

    return [pscustomobject]@{
        CurrentVersion = $currentVersion
        NextVersion = $nextVersion
        Date = $Date
        PackageJsonPath = $packageJsonPath
        PackageLockPath = if ($packageLockUpdated) { $packageLockPath } else { $null }
        ChangelogPath = $changelogPath
        ChangelogRotated = [bool]$updatedChangelog.Rotated
        DryRun = [bool]$DryRun
    }
}

function New-ReleaseNotes {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$Version,
        [switch]$Footer
    )

    $resolvedRepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
    $packageJsonPath = Join-Path $resolvedRepoRoot 'package.json'
    $changelogPath = Join-Path $resolvedRepoRoot 'CHANGELOG.md'

    $packageJson = Get-Content -LiteralPath $packageJsonPath -Raw | ConvertFrom-Json
    $packageName = [string]$packageJson.name
    $section = Get-ChangelogSection `
        -Content ([System.IO.File]::ReadAllText($changelogPath)) `
        -Version $Version

    if (-not $Footer) {
        return $section.TrimEnd() + "`n"
    }

    $footerContent = @(
        '## Install',
        '',
        "- Import the attached ``.unitypackage`` into a Unity project, or",
        "- install ``$packageName@$Version`` from npm / OpenUPM through Unity Package Manager.",
        '',
        'The release includes the npm tarball and the `.unitypackage`, each with a `.sha256` checksum.'
    ) -join "`n"

    return $section.TrimEnd() + "`n`n---`n`n" + $footerContent + "`n"
}
