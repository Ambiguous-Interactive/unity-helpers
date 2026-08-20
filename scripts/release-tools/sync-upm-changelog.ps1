Param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,
    [switch]$Check
)

<#
.SYNOPSIS
    Mirrors the current version's CHANGELOG.md section into package.json `_upm.changelog`.

.DESCRIPTION
    Unity's Package Manager reads a package's release notes from `PackageInfo.upmReserved`,
    which the editor populates from the resolved package's own package.json `_upm` object.
    `npm publish` strips every underscore-prefixed key from the registry metadata document,
    but `npm pack` copies package.json into the tarball verbatim, so the field reaches npm,
    OpenUPM, Git-URL installs and the .unitypackage alike.

    The packaged CHANGELOG.md is a separate surface: it is what UPM opens when `changelogUrl`
    is unreachable. Both are required, which is why this script does not touch either file's
    role -- it only keeps the mirrored copy in step with the changelog.

    Run with -Check to verify without writing.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'release-helpers.ps1')

# Unity's own packages ship 98-5,017 characters of notes. This budget sits above that while
# keeping a rotated [Unreleased] block from turning package.json into an unreviewable diff.
$script:ChangelogBudget = 8000

function Get-UpmChangelogValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Section,
        [Parameter(Mandatory = $true)]
        [string]$ChangelogUrl
    )

    $normalized = (Normalize-ReleaseText -Content $Section).TrimEnd()
    if ($normalized.Length -le $script:ChangelogBudget) {
        return $normalized
    }

    $pointer = "`n`n(Truncated. Full changelog: $ChangelogUrl)"
    $room = $script:ChangelogBudget - $pointer.Length
    $lines = $normalized.Split("`n")
    $kept = [System.Collections.Generic.List[string]]::new()
    $used = 0
    foreach ($line in $lines) {
        $cost = $line.Length + 1
        if ($used + $cost -gt $room) {
            break
        }
        $kept.Add($line)
        $used += $cost
    }

    return (($kept -join "`n").TrimEnd() + $pointer)
}

function Set-UpmChangelogContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    # A targeted rewrite rather than a re-serialization: ConvertTo-Json reorders keys and
    # re-escapes strings, which would churn every line of a file this change does not own.
    $encoded = ConvertTo-Json -InputObject $Value -Compress
    $block = "  `"_upm`": {`n    `"changelog`": $encoded`n  },`n"

    $existing = [regex]::Matches($Content, '(?ms)^[ ]{2}"_upm":[ ]*\{.*?^[ ]{2}\},\r?\n')
    if ($existing.Count -gt 1) {
        throw "Expected at most one package.json _upm block; found $($existing.Count)."
    }

    if ($existing.Count -eq 1) {
        $updated = $Content.Remove($existing[0].Index, $existing[0].Length).Insert($existing[0].Index, $block)
    } else {
        $anchor = [regex]::Matches($Content, '(?m)^[ ]{2}"changelogUrl":[^\n]*\n')
        if ($anchor.Count -ne 1) {
            throw "Expected exactly one package.json changelogUrl property to anchor _upm after; found $($anchor.Count)."
        }
        $insertAt = $anchor[0].Index + $anchor[0].Length
        $updated = $Content.Insert($insertAt, $block)
    }

    $parsed = $updated | ConvertFrom-Json
    if ([string]$parsed._upm.changelog -ne $Value) {
        throw 'package.json rewrite verification failed; the parsed _upm.changelog did not match.'
    }

    return (Normalize-ReleaseText -Content $updated).TrimEnd() + "`n"
}

try {
    $packageJsonPath = Join-Path $RepoRoot 'package.json'
    $changelogPath = Join-Path $RepoRoot 'CHANGELOG.md'

    $packageJsonContent = Get-Content -Path $packageJsonPath -Raw
    $package = $packageJsonContent | ConvertFrom-Json

    $changelogUrl = ''
    if ($null -ne $package.PSObject.Properties['changelogUrl']) {
        $changelogUrl = [string]$package.changelogUrl
    }
    if ([string]::IsNullOrWhiteSpace($changelogUrl)) {
        throw 'package.json has no changelogUrl; UPM needs one to offer a Changelog link.'
    }

    $version = [string]$package.version
    $section = Get-ChangelogSection -Content (Get-Content -Path $changelogPath -Raw) -Version $version
    $expected = Get-UpmChangelogValue -Section $section -ChangelogUrl $changelogUrl

    $actual = ''
    if ($null -ne $package.PSObject.Properties['_upm'] -and $null -ne $package._upm.PSObject.Properties['changelog']) {
        $actual = [string]$package._upm.changelog
    }

    if ($actual -eq $expected) {
        Write-Host "sync-upm-changelog: package.json _upm.changelog is in step with CHANGELOG.md [$version]."
        exit 0
    }

    if ($Check) {
        Write-Error "package.json _upm.changelog is stale for version $version. Run 'npm run sync:upm-changelog'."
        exit 1
    }

    $updated = Set-UpmChangelogContent -Content $packageJsonContent -Value $expected
    Set-ReleaseFileContent -Path $packageJsonPath -Content $updated
    Write-Host "sync-upm-changelog: wrote $($expected.Length) characters of [$version] notes to package.json."
    exit 0
} catch {
    Write-Error "sync-upm-changelog failed: $($_.Exception.Message)"
    exit 1
}
