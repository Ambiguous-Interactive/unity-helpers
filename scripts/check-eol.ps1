param(
    [string[]]$Paths,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPaths,
    [switch]$VerboseOutput
)

$ErrorActionPreference = 'Stop'
# agent-preflight.ps1 dot-invokes this script and its StrictMode leaks in, so
# standalone runs opt into the same rules rather than passing under looser ones.
Set-StrictMode -Version Latest
$repoRoot = (Get-Item $PSScriptRoot).Parent.FullName
$effectivePaths = @()
if ($Paths -and $Paths.Count -gt 0) {
    $effectivePaths += $Paths
}
if ($AdditionalPaths -and $AdditionalPaths.Count -gt 0) {
    $effectivePaths += $AdditionalPaths
}

# =============================================================================
# LINE ENDING POLICY (must match .gitattributes, .prettierrc.json, .yamllint.yaml)
# =============================================================================
# DEFAULT: CRLF (Windows) for most text files
# EXCEPTIONS (LF required):
#   - YAML files (.yml, .yaml) - yamllint requires unix line endings
#   - Shell scripts (.sh) - Unix requirement
#   - .github/** ALL files - GitHub Actions run on Linux, Dependabot commits LF
#   - .githooks/* - Unix requirement (matched via path pattern)
#   - package.json, package-lock.json - Dependabot commits LF
#   - _includes/*.html - Jekyll includes (GitHub Pages runs on Linux)
# =======================================================================================

$extensions = @(
    'cs','csproj','sln',
    'json','yaml','yml','md','xml','uxml','uss',
    'shader','hlsl','compute','cginc',
    'asmdef','asmref','meta','ps1','sh','html'
)

# Extensions that ALWAYS require LF (Unix) line endings
$lfExtensions = @('sh', 'yaml', 'yml', 'md')

# Path patterns that require LF line endings (regardless of extension)
# These match .gitattributes rules
$lfPathPatterns = @(
    '^\.github/',           # All files in .github/** directory
    '^\.githooks/',         # All files in .githooks/** directory
    '^package\.json$',      # package.json at repo root
    '^package-lock\.json$', # package-lock.json at repo root
    '^_includes/.*\.html$'  # Jekyll includes (_includes/*.html)
)

$trackedTextPathPatterns = @(
    '^\.gitignore$'
)

function Test-ShouldUseLf([string]$path) {
    # Normalize path separators to forward slashes for consistent matching
    $normalizedPath = $path -replace '\\', '/'
    
    # Check extension-based rules first
    $ext = [System.IO.Path]::GetExtension($path).TrimStart('.').ToLowerInvariant()
    if ($lfExtensions -contains $ext) {
        return $true
    }
    
    # Check path-based rules
    foreach ($pattern in $lfPathPatterns) {
        if ($normalizedPath -match $pattern) {
            return $true
        }
    }
    
    return $false
}

function Test-ShouldCheckPath([string]$path) {
    $normalizedPath = $path -replace '\\', '/'
    $ext = [System.IO.Path]::GetExtension($path).TrimStart('.').ToLowerInvariant()
    if ($extensions -contains $ext) {
        return $true
    }

    foreach ($pattern in $trackedTextPathPatterns) {
        if ($normalizedPath -match $pattern) {
            return $true
        }
    }

    return $false
}

function Get-TrackedFiles {
    if ($effectivePaths.Count -gt 0) {
        # Use provided file list instead of scanning all tracked files
        return $effectivePaths | Where-Object { Test-ShouldCheckPath $_ }
    }
    $files = (& git -C $repoRoot ls-files -z) -split "`0" | Where-Object { $_ -ne '' }
    return $files | Where-Object { Test-ShouldCheckPath $_ }
}

function ConvertTo-RepoRelativePath([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return $null }
    $candidate = $path
    if ([System.IO.Path]::IsPathRooted($candidate)) {
        $candidate = [System.IO.Path]::GetRelativePath($repoRoot, $candidate)
    }
    $candidate = $candidate -replace '\\', '/'
    if ($candidate.StartsWith('./')) { $candidate = $candidate.Substring(2) }
    if ($candidate.StartsWith('../')) { return $null }
    return $candidate
}

# The worktree scan below reads bytes from disk, which is what a developer and
# every formatter sees. It cannot see what git stored. A blob committed with
# CRLF under `text eol=crlf` checks out looking correct while git reports the
# file as modified in every fresh clone, because the comparison renormalizes
# the worktree copy back to LF. That silently breaks any automation that
# switches branches after checkout (create-pull-request, the stuck-job
# watchdog's state branch), which is invisible to a worktree-only check.
# `git ls-files --eol` reports the index form, so it detects the class directly.
function Get-IndexEolIssues {
    $issues = New-Object System.Collections.Generic.List[string]

    $scope = $null
    if ($effectivePaths.Count -gt 0) {
        $scope = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::Ordinal)
        foreach ($path in $effectivePaths) {
            $relative = ConvertTo-RepoRelativePath $path
            if ($relative) { $scope.Add($relative) | Out-Null }
        }
        if ($scope.Count -eq 0) { return $issues }
    }

    $records = ((& git -C $repoRoot ls-files --eol -z) -join '') -split "`0"
    foreach ($record in $records) {
        if ([string]::IsNullOrEmpty($record)) { continue }
        $tabIndex = $record.IndexOf("`t")
        if ($tabIndex -lt 0) { continue }

        $path = $record.Substring($tabIndex + 1)
        if ($scope -and -not $scope.Contains($path)) { continue }

        $fields = $record.Substring(0, $tabIndex)
        if ($fields -notmatch '(?:^|\s)i/(?<eol>\S+)') { continue }
        # Index classifications: lf, crlf, mixed, none, -text. Only CR bytes in
        # a blob git converts on checkout renormalize back on comparison.
        $indexEol = $Matches['eol']
        if ($indexEol -ne 'crlf' -and $indexEol -ne 'mixed') { continue }

        # `-text` (and `binary`, the macro that turns `text` off) tells git to
        # copy bytes through untouched, so CR bytes there round-trip cleanly.
        # That is the supported escape hatch for content whose CRs are content.
        if ($fields -notmatch 'attr/(?<attributes>.*)$') { continue }
        $attributes = $Matches['attributes']
        if ($attributes -match '(?:^|\s)-text(?:\s|$)') { continue }
        if ($attributes -notmatch '(?:^|\s)text(?:=\S+)?(?:\s|$)') { continue }

        $issues.Add("$path (committed blob is $indexEol; git stores text blobs as LF)") | Out-Null
    }

    return $issues
}

function Test-HasCrlf([byte[]]$bytes) {
    for ($i = 1; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -eq 0x0A -and $bytes[$i-1] -eq 0x0D) {
            return $true
        }
    }
    return $false
}

function Test-HasLfOnly([byte[]]$bytes) {
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -eq 0x0A -and ($i -eq 0 -or $bytes[$i-1] -ne 0x0D)) {
            return $true
        }
    }
    return $false
}

$eolIssues = New-Object System.Collections.Generic.List[string]
$bomIssues = New-Object System.Collections.Generic.List[string]

foreach ($path in Get-TrackedFiles) {
    $fullPath = Join-Path $repoRoot $path
    try { $bytes = [System.IO.File]::ReadAllBytes($fullPath) } catch { continue }
    # BOM (flag any file that contains a UTF-8 BOM — we require NO BOM)
    $hasBom = $false
    if ($bytes.Length -ge 3) { $hasBom = ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) }
    if ($hasBom) { $bomIssues.Add($path) | Out-Null }
    
    # Determine expected line ending for this file
    $shouldUseLf = Test-ShouldUseLf $path
    $hasLfOnly = Test-HasLfOnly $bytes
    $hasCrlf = Test-HasCrlf $bytes
    
    # Check for line ending violations
    if ($shouldUseLf) {
        # LF-required file: flag if it contains any CRLF
        if ($hasCrlf) {
            $eolIssues.Add("$path (expected LF, found CRLF)") | Out-Null
        }
    } else {
        # CRLF-required file: flag if it contains LF-only (mixed or pure LF)
        if ($hasLfOnly) {
            $eolIssues.Add("$path (expected CRLF, found LF-only or mixed)") | Out-Null
        }
    }
}

# @() keeps Count available: PowerShell unrolls a returned list, so an empty
# result would otherwise arrive as $null and a single result as a bare string.
# agent-preflight.ps1 dot-invokes this script under StrictMode, where reading
# Count off either of those throws.
$indexIssues = @(Get-IndexEolIssues)

if ($VerboseOutput) {
    if ($eolIssues.Count -gt 0) {
        Write-Host "EOL issues (wrong line endings):"; $eolIssues | Sort-Object -Unique | ForEach-Object { Write-Host " - $_" }
    }
    if ($bomIssues.Count -gt 0) {
        Write-Host "Contains UTF-8 BOM (disallowed):"; $bomIssues | Sort-Object -Unique | ForEach-Object { Write-Host " - $_" }
    }
}

if ($indexIssues.Count -gt 0) {
    Write-Host "Unnormalized committed blobs (every fresh clone reports these as modified):"
    $indexIssues | Sort-Object -Unique | ForEach-Object { Write-Host " - $_" }
    Write-Host "Fix by re-staging the blobs through git's clean filter, then committing:"
    Write-Host "  git add --renormalize <path>"
    Write-Host "Content that must keep CR bytes in the blob has to be marked '-text' in .gitattributes."
}

Write-Host "EOL issues: $($eolIssues | Sort-Object -Unique | Measure-Object | ForEach-Object { $_.Count })"
Write-Host "Files with BOM: $($bomIssues | Sort-Object -Unique | Measure-Object | ForEach-Object { $_.Count })"
Write-Host "Unnormalized committed blobs: $($indexIssues | Sort-Object -Unique | Measure-Object | ForEach-Object { $_.Count })"

if ($eolIssues.Count -gt 0 -or $bomIssues.Count -gt 0 -or $indexIssues.Count -gt 0) { exit 3 }
exit 0
