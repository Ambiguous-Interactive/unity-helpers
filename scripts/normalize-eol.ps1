param(
    [string[]]$Paths,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPaths,
    [switch]$DryRun,
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

# Extensions to normalize (tracked by git)
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
    $files = (& git -C $repoRoot ls-files -z) -split "`0" | Where-Object { $_ -ne '' }
    return $files | Where-Object { Test-ShouldCheckPath $_ }
}

function Get-TargetFiles([string[]]$paths, [string[]]$trackedFiles) {
    if (-not $paths -or $paths.Count -eq 0) {
        return $trackedFiles
    }

    $targets = New-Object System.Collections.Generic.List[string]
    foreach ($path in $paths) {
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $resolved = Resolve-Path -LiteralPath $path -ErrorAction SilentlyContinue
        if (-not $resolved) { continue }

        $fullPath = $resolved.Path
        if (-not [System.IO.File]::Exists($fullPath)) { continue }

        $relative = [System.IO.Path]::GetRelativePath($repoRoot, $fullPath)
        $normalized = $relative -replace '\\', '/'
        if (Test-ShouldCheckPath $normalized) {
            $targets.Add($normalized) | Out-Null
        }
    }

    return $targets
}

function To-CrLf([string]$text) {
    $tmp = $text -replace "`r`n", "`n" -replace "`r", "`n"
    return $tmp -replace "`n", "`r`n"
}

function To-Lf([string]$text) {
    return $text -replace "`r`n", "`n" -replace "`r", "`n"
}

$changed = 0
$eolFixed = 0
$bomRemoved = 0
$modified = New-Object System.Collections.Generic.List[string]

$tracked = Get-TrackedFiles
$targets = Get-TargetFiles $effectivePaths $tracked
foreach ($path in $targets) {
    $fullPath = Join-Path $repoRoot $path
    try { $bytes = [System.IO.File]::ReadAllBytes($fullPath) } catch { continue }

    $hasBom = $false
    if ($bytes.Length -ge 3) {
        $hasBom = ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    }

    if ($hasBom) {
        $text = [System.Text.Encoding]::UTF8.GetString($bytes, 3, $bytes.Length - 3)
    } else {
        $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    }

    # Determine if this file should use LF (Unix) or CRLF (Windows) line endings
    $useLf = Test-ShouldUseLf $path
    $normalized = if ($useLf) { To-Lf $text } else { To-CrLf $text }

    $fileChanged = $false
    if ($normalized -ne $text) { $fileChanged = $true; $eolFixed++ }
    # Remove BOM if present (we enforce UTF-8 without BOM)
    if ($hasBom) { $fileChanged = $true; $bomRemoved++ }

    if ($fileChanged) {
        if (-not $DryRun) {
            # Write UTF-8 without BOM
            [System.IO.File]::WriteAllBytes($fullPath, [System.Text.Encoding]::UTF8.GetBytes($normalized))
        }
        $changed++
        $modified.Add($path) | Out-Null
        if ($VerboseOutput) { Write-Host "Fixed: $path" }
    }
}

Write-Host "Files fixed: $changed (EOL:$eolFixed, BOMRemoved:$bomRemoved)"
if ($DryRun -and $changed -gt 0) { exit 2 }
exit 0
