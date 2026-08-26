Param(
  [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Get-Item $PSScriptRoot).Parent.FullName

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[lint-meta-files] $msg" -ForegroundColor Cyan }
}

# Directories to scan for meta files
# Note: .github is excluded from scanning entirely because it's GitHub-specific,
# not part of the Unity package distribution
$sourceRoots = @('Runtime', 'Editor', 'Tests', 'Samples~', 'Shaders', 'Styles', 'URP', 'docs', 'scripts')

# Directories to exclude entirely
$excludeDirs = @('node_modules', '.git', 'obj', 'bin', 'Library', 'Temp', '.pytest_cache', '__pycache__', '.mypy_cache')

# File patterns to exclude from requiring meta files
$excludeFilePatterns = @(
  '*.meta',                    # Meta files themselves don't need meta files
  'package-lock.json',         # npm lock file doesn't need meta
  'Gemfile.lock',              # Ruby lock file doesn't need meta
  '*.tmp',                     # Temporary files don't need meta
  '.gitkeep',                  # Git directory placeholders are not Unity assets
  '.DS_Store',                 # macOS Finder metadata
  'Thumbs.db',                 # Windows Explorer thumbnails
  '*.pyc',                     # Python compiled bytecode
  '*.pyo',                     # Python optimized bytecode
  '*.swp',                     # Vim swap files
  '*.swo'                      # Vim swap files
)

# Directory patterns to exclude from requiring meta files.
#
# ANY directory whose name ends with `~` is excluded, not just the two this repository happens to
# have today. Unity ignores such a directory entirely, so it never generates a .meta inside one and
# a hand-written .meta there is an orphan that nothing maintains. Naming them individually meant the
# next one was a lint failure with no correct fix: a `.c` file has to live somewhere Unity cannot
# see it, because Unity compiles any native source it CAN see into the IL2CPP player.
$excludeDirPatterns = @(
  '*~'                         # Unity ignores any folder with a ~ suffix (Samples~, Generator~, ...)
)

function Get-RelativePath([string]$path) {
  $root = $repoRoot
  if ($path.StartsWith($root)) {
    $relative = $path.Substring($root.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar)
    # Normalize to forward slashes for consistency
    return $relative -replace '\\', '/'
  }
  return $path -replace '\\', '/'
}

function Test-ShouldExclude([string]$relativePath, [bool]$isDirectory) {
  # Check directory exclusions
  foreach ($dir in $excludeDirs) {
    # Match: exact name, nested dir itself, contents at root level, or nested contents
    if ($relativePath -eq $dir -or $relativePath -like "*/$dir" -or $relativePath -like "$dir/*" -or $relativePath -like "*/$dir/*") {
      return $true
    }
  }

  # Check directory pattern exclusions (applies to both files and directories inside matched dirs).
  # The segment walk is what makes a nested `foo~/` work: matching only the whole relative path
  # would exclude `Samples~/x` and miss `scripts/random-quality/testu01~/x`.
  foreach ($pattern in $excludeDirPatterns) {
    if ($relativePath -like $pattern -or $relativePath -eq $pattern -or $relativePath -like "$pattern/*") {
      return $true
    }
    foreach ($segment in ($relativePath -split '/')) {
      if ($segment -like $pattern) {
        return $true
      }
    }
  }

  if ($isDirectory) {
    # Directory-specific checks can go here
  } else {
    # Check file pattern exclusions
    foreach ($pattern in $excludeFilePatterns) {
      if ($relativePath -like $pattern) {
        return $true
      }
      # Also check just the filename
      $fileName = Split-Path -Leaf $relativePath
      if ($fileName -like $pattern) {
        return $true
      }
    }
  }

  return $false
}

function Get-AllItems {
  param([string[]]$roots)

  $items = @{
    Files = @()
    Directories = @()
  }

  # Single git ls-files call for all roots (replaces N recursive Get-ChildItem traversals)
  $existingRoots = @($roots | Where-Object { Test-Path (Join-Path $repoRoot $_) })
  if ($existingRoots.Count -eq 0) { return $items }

  $trackedFiles = (& git -C $repoRoot ls-files -z -- @existingRoots) -split "`0" | Where-Object { $_ -ne '' }

  # Collect unique directories from tracked file paths
  $dirSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

  foreach ($rel in $trackedFiles) {
    # Always include .meta files so the orphaned-meta check can inspect them;
    # the missing-meta loop already skips *.meta on its own.
    $isMeta = $rel -like '*.meta'
    if ($isMeta -or -not (Test-ShouldExclude $rel $false)) {
      $fullPath = Join-Path $repoRoot $rel
      $items.Files += @{
        FullPath = $fullPath
        RelativePath = ($rel -replace '\\', '/')
      }
    }
    # Collect parent directory chain
    $parent = Split-Path $rel -Parent
    while ($parent -and $parent -ne '.') {
      $normalized = $parent -replace '\\', '/'
      if (-not $dirSet.Add($normalized)) { break }
      $parent = Split-Path $parent -Parent
    }
  }

  foreach ($dirRel in $dirSet) {
    if (-not (Test-ShouldExclude $dirRel $true)) {
      $fullPath = Join-Path $repoRoot $dirRel
      if (Test-Path -LiteralPath $fullPath -PathType Container) {
        $items.Directories += @{
          FullPath = $fullPath
          RelativePath = $dirRel
        }
      }
    }
  }

  return $items
}

$missingMeta = @()
$orphanedMeta = @()

Write-Info "Scanning for Unity meta file issues..."

# Collect all items from all source roots using a single git ls-files call
$allFiles = @()
$allDirectories = @()

# Add root directories themselves (if they exist and aren't excluded)
foreach ($root in $sourceRoots) {
  $fullRoot = Join-Path $repoRoot $root
  if (Test-Path $fullRoot) {
    $rootRel = $root -replace '\\', '/'
    if (-not (Test-ShouldExclude $rootRel $true)) {
      $allDirectories += @{
        FullPath = (Resolve-Path $fullRoot).Path
        RelativePath = $rootRel
      }
    }
  }
}

# Single call to get all tracked files across all source roots
$items = Get-AllItems $sourceRoots
$allFiles += $items.Files
$allDirectories += $items.Directories

Write-Info "Found $($allFiles.Count) files and $($allDirectories.Count) directories to check"

# Check for missing meta files (files/directories without .meta)
foreach ($file in $allFiles) {
  # Skip .meta files when checking for missing meta
  if ($file.RelativePath -like '*.meta') {
    continue
  }

  $metaPath = "$($file.FullPath).meta"
  if (-not (Test-Path -LiteralPath $metaPath)) {
    $missingMeta += @{
      Path = $file.RelativePath
      Type = 'File'
    }
  }
}

foreach ($dir in $allDirectories) {
  $metaPath = "$($dir.FullPath).meta"
  if (-not (Test-Path -LiteralPath $metaPath)) {
    $missingMeta += @{
      Path = $dir.RelativePath
      Type = 'Directory'
    }
  }
}

# Check for orphaned meta files (meta exists but source doesn't)
foreach ($file in $allFiles) {
  if ($file.RelativePath -like '*.meta') {
    # This is a meta file - check if the source exists
    $sourcePath = $file.FullPath -replace '\.meta$', ''
    $sourceRel = $file.RelativePath -replace '\.meta$', ''

    # Check if this meta file's source should be excluded
    $sourceIsDir = Test-Path -LiteralPath $sourcePath -PathType Container
    if (Test-ShouldExclude $sourceRel $sourceIsDir) {
      continue
    }

    if (-not (Test-Path -LiteralPath $sourcePath)) {
      $orphanedMeta += @{
        Path = $file.RelativePath
        ExpectedSource = $sourceRel
      }
    }
  }
}

# Output results
$hasErrors = $false

if ($missingMeta.Count -gt 0) {
  $hasErrors = $true
  Write-Host "Missing .meta files:" -ForegroundColor Red
  Write-Host ""
  foreach ($item in $missingMeta | Sort-Object { $_.Path }) {
    $typeLabel = if ($item.Type -eq 'Directory') { '[DIR]' } else { '[FILE]' }
    # Output in format compatible with GitHub Actions annotations
    $ghAnnotation = "::error file=$($item.Path)::UNH005: Missing .meta file for $($item.Type.ToLower()): $($item.Path)"
    Write-Host $ghAnnotation
    Write-Host "  $typeLabel $($item.Path)" -ForegroundColor Yellow
  }
  Write-Host ""
}

if ($orphanedMeta.Count -gt 0) {
  $hasErrors = $true
  Write-Host "Orphaned .meta files (source file/directory missing):" -ForegroundColor Red
  Write-Host ""
  foreach ($item in $orphanedMeta | Sort-Object { $_.Path }) {
    # Output in format compatible with GitHub Actions annotations
    $ghAnnotation = "::error file=$($item.Path)::UNH006: Orphaned .meta file (missing source): $($item.ExpectedSource)"
    Write-Host $ghAnnotation
    Write-Host "  $($item.Path) -> missing: $($item.ExpectedSource)" -ForegroundColor Yellow
  }
  Write-Host ""
}

if ($hasErrors) {
  Write-Host "Unity meta file lint failed:" -ForegroundColor Red
  Write-Host "  Missing meta files: $($missingMeta.Count)" -ForegroundColor Yellow
  Write-Host "  Orphaned meta files: $($orphanedMeta.Count)" -ForegroundColor Yellow
  Write-Host ""
  Write-Host "To fix missing meta files, run: ./scripts/generate-meta.sh <path>" -ForegroundColor Cyan
  Write-Host "To fix orphaned meta files, delete the .meta file or restore the source." -ForegroundColor Cyan
  exit 1
} else {
  Write-Info "No meta file issues found."
  Write-Host "All Unity meta files are valid." -ForegroundColor Green
  exit 0
}
