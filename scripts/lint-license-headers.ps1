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
    Write-Info "Skipping $root (directory not found)"
    continue
  }

  $csFiles = Get-ChildItem -Path $rootPath -Filter "*.cs" -Recurse -File | Where-Object {
    $path = $_.FullName
    $excluded = $false
    foreach ($dir in $excludeDirs) {
      if ($path -match [regex]::Escape("\$dir\") -or $path -match [regex]::Escape("/$dir/")) {
        $excluded = $true
        break
      }
    }
    -not $excluded
  }

  foreach ($file in $csFiles) {
    $checkedCount++
    $relativePath = ConvertTo-RepoRelativePath -Path $file.FullName

    Write-Info "Checking: $relativePath"

    # Read first N lines of the file
    $content = Get-Content -Path $file.FullName -TotalCount $linesToCheck -ErrorAction SilentlyContinue
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
