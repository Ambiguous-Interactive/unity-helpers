param(
    [string[]]$Paths,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPaths,
    [switch]$VerboseOutput
)

$ErrorActionPreference = 'Stop'
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

function Get-TrackedFiles {
    if ($effectivePaths.Count -gt 0) {
        # Use provided file list instead of scanning all tracked files
        return $effectivePaths | Where-Object {
            $ext = [System.IO.Path]::GetExtension($_).TrimStart('.').ToLowerInvariant()
            $extensions -contains $ext
        }
    }
    $files = (& git -C $repoRoot ls-files -z) -split "`0" | Where-Object { $_ -ne '' }
    return $files | Where-Object {
        $ext = [System.IO.Path]::GetExtension($_).TrimStart('.').ToLowerInvariant()
        $extensions -contains $ext
    }
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

if ($VerboseOutput) {
    if ($eolIssues.Count -gt 0) {
        Write-Host "EOL issues (wrong line endings):"; $eolIssues | Sort-Object -Unique | ForEach-Object { Write-Host " - $_" }
    }
    if ($bomIssues.Count -gt 0) {
        Write-Host "Contains UTF-8 BOM (disallowed):"; $bomIssues | Sort-Object -Unique | ForEach-Object { Write-Host " - $_" }
    }
}

Write-Host "EOL issues: $($eolIssues | Sort-Object -Unique | Measure-Object | ForEach-Object { $_.Count })"
Write-Host "Files with BOM: $($bomIssues | Sort-Object -Unique | Measure-Object | ForEach-Object { $_.Count })"

if ($eolIssues.Count -gt 0 -or $bomIssues.Count -gt 0) { exit 3 }
exit 0
