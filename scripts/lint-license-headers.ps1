Param(
  [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[lint-license-headers] $msg" -ForegroundColor Cyan }
}

function Write-ErrorMsg($msg) {
  Write-Host "[lint-license-headers] $msg" -ForegroundColor Red
}

function Write-SuccessMsg($msg) {
  Write-Host "[lint-license-headers] $msg" -ForegroundColor Green
}

$repoRoot = (Get-Item $PSScriptRoot).Parent.FullName

function ConvertTo-RepoRelativePath {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path
  )

  $rootPath = [System.IO.Path]::GetFullPath($repoRoot)
  $childPath = [System.IO.Path]::GetFullPath($Path)

  return [System.IO.Path]::GetRelativePath($rootPath, $childPath).Replace('\', '/')
}

# Directories to scan
$sourceRoots = @('Runtime', 'Editor', 'Tests')

# Directories to exclude
$excludeDirs = @('node_modules', '.git', 'obj', 'bin', 'Library', 'Temp')

# Opt-out marker
$optOutMarker = "No license header required"

# Number of lines to check for MIT license
$linesToCheck = 20

Write-Info "Starting license header check..."

$violations = @()
$checkedCount = 0
$skippedCount = 0

foreach ($root in $sourceRoots) {
  $rootPath = Join-Path -Path $PSScriptRoot -ChildPath "..\$root"
  if (-not (Test-Path $rootPath)) {
    # Renaming or moving a source root used to silence this check for that whole tree, and the
    # skip was only visible under -VerboseOutput (#556).
    Write-ErrorMsg "Source root not found: $root. If it moved, update `$sourceRoots in the same commit."
    exit 1
  }

  # [IO.Directory]::EnumerateFiles rather than Get-ChildItem -Recurse, which builds a FileInfo per
  # entry and post-filters. Measured on this repository over the devcontainer's 9p mount: 0.8 s
  # against 4.6 s for the same 1643 files. Sorted because the walk order is the filesystem's, and a
  # linter that reports findings in a different order on every machine is a diff nobody can review.
  $matched = [System.IO.Directory]::EnumerateFiles(
    (Resolve-Path -LiteralPath $rootPath).Path,
    '*.cs',
    [System.IO.SearchOption]::AllDirectories
  )
  $csFiles = @()
  foreach ($path in ($matched | Sort-Object)) {
    $excluded = $false
    foreach ($dir in $excludeDirs) {
      if ($path -like "*\$dir\*" -or $path -like "*/$dir/*") {
        $excluded = $true
        break
      }
    }
    if (-not $excluded) {
      $csFiles += $path
    }
  }

  foreach ($file in $csFiles) {
    $checkedCount++
    $relativePath = ConvertTo-RepoRelativePath -Path $file

    Write-Info "Checking: $relativePath"

    # Read first N lines of the file. ReadLines is lazy, so this still stops after
    # $linesToCheck lines, and it does not pay Get-Content's per-file pipeline cost -- which
    # dominates on a devcontainer's 9p mount, where the reads are the whole runtime.
    $content = @([System.IO.File]::ReadLines($file) | Select-Object -First $linesToCheck)
    if (-not $content) {
      Write-Info "  Empty or unreadable file, skipping"
      $skippedCount++
      continue
    }

    $headerText = $content -join "`n"

    # Check for opt-out marker
    if ($headerText -match [regex]::Escape($optOutMarker)) {
      Write-Info "  Opt-out marker found, skipping"
      $skippedCount++
      continue
    }

    # Check for MIT license mention (case-insensitive)
    if ($headerText -match "(?i)\bMIT\b" -or $headerText -match "(?i)MIT\s+License") {
      Write-Info "  MIT license found"
      continue
    }

    # No MIT license found - this is a violation
    $violations += $relativePath
  }
}

Write-Info ""
Write-Info "Summary:"
Write-Info "  Files checked: $checkedCount"
Write-Info "  Files skipped: $skippedCount"
Write-Info "  Violations: $($violations.Count)"

if ($checkedCount -eq 0) {
  Write-ErrorMsg "No files were checked. The scan found nothing under $($sourceRoots -join ', '), so a pass here would mean nothing."
  exit 1
}

if ($violations.Count -gt 0) {
  Write-ErrorMsg ""
  Write-ErrorMsg "The following files are missing MIT license headers:"
  Write-ErrorMsg ""
  foreach ($file in $violations) {
    Write-ErrorMsg "  - $file"
  }
  Write-ErrorMsg ""
  Write-ErrorMsg "Please add an MIT license header to these files, or add '$optOutMarker' comment to opt-out."
  exit 1
}

Write-SuccessMsg "All files have proper MIT license headers!"
exit 0
