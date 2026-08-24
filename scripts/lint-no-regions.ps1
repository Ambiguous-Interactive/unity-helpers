Param(
  [switch]$VerboseOutput,
  [switch]$StagedOnly,
  [switch]$Fix
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Load shared git helpers for safe index operations
$helpersPath = Join-Path -Path $PSScriptRoot -ChildPath 'git-staging-helpers.ps1'
. $helpersPath

# Get repository info for lock handling
$script:RepositoryInfo = $null
try {
  Assert-GitAvailable | Out-Null
  $script:RepositoryInfo = Get-GitRepositoryInfo
} catch {
  # Not fatal for this script - we may just be linting without staging
}

# If we're going to fix files (and thus do git add), wait for any external tool
# to release the index.lock before starting operations.
if ($Fix -and $script:RepositoryInfo) {
  if (-not (Invoke-EnsureNoIndexLock)) {
    Write-Warning "index.lock still held after waiting. Proceeding anyway, but staging may fail."
  }
}
function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[lint-no-regions] $msg" -ForegroundColor Cyan }
}

function Test-IsCI {
  # Check for common CI environment variables
  $ciVars = @('CI', 'GITHUB_ACTIONS', 'GITLAB_CI', 'JENKINS_URL', 'TRAVIS', 'CIRCLECI', 'AZURE_PIPELINES', 'TF_BUILD', 'BUILDKITE', 'CODEBUILD_BUILD_ID')
  foreach ($var in $ciVars) {
    if ([Environment]::GetEnvironmentVariable($var)) {
      return $true
    }
  }
  return $false
}

function Invoke-CSharpier([string[]]$filePaths) {
  if ($filePaths.Count -eq 0) { return }

  $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
  if (-not $dotnet) {
    Write-Info "dotnet not found; skipping CSharpier formatting."
    return
  }

  & dotnet tool restore > $null 2>&1
  & dotnet tool run csharpier format $filePaths > $null 2>&1
}

# Directories to scan
$sourceRoots = @('Runtime', 'Editor', 'Tests')

# Directories to exclude
$excludeDirs = @('node_modules', '.git', 'obj', 'bin', 'Library', 'Temp')

# Pattern to match #region or #endregion directives
$regionPattern = [regex]'(?m)^\s*#\s*(region|endregion)\b'

# Get files to check
function Get-FilesToCheck {
  param([switch]$StagedOnly)

  if ($StagedOnly) {
    # Get staged C# files
    $stagedFiles = & git diff --cached --name-only --diff-filter=ACM -- '*.cs' 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $stagedFiles) {
      return @()
    }
    $files = @()
    foreach ($f in $stagedFiles) {
      if (Test-Path -LiteralPath $f) {
        $files += (Get-Item -LiteralPath $f)
      }
    }
    return $files
  }

  $files = @()
  foreach ($root in $sourceRoots) {
    # Renaming a source root used to remove that whole tree from the scan silently (#556).
    if (-not (Test-Path $root)) {
      Write-Host "[lint-no-regions] ERROR: source root not found: $root. If it moved, update `$sourceRoots in the same commit." -ForegroundColor Red
      exit 1
    }
    # [IO.Directory]::EnumerateFiles rather than Get-ChildItem -Recurse -Include, which enumerates
    # everything and post-filters. Measured on this repository's 1643 C# files over the
    # devcontainer's 9p mount: 0.8 s against 28.5 s. Sorted because the walk order is the
    # filesystem's, and a linter that reports findings in a different order on every machine is a
    # diff nobody can review.
    $matched = [System.IO.Directory]::EnumerateFiles(
      (Resolve-Path -LiteralPath $root).Path,
      '*.cs',
      [System.IO.SearchOption]::AllDirectories
    )
    foreach ($path in ($matched | Sort-Object)) {
      $excluded = $false
      foreach ($dir in $excludeDirs) {
        if ($path -like "*\$dir\*" -or $path -like "*/$dir/*") {
          $excluded = $true
          break
        }
      }
      if (-not $excluded) {
        $files += [System.IO.FileInfo]::new($path)
      }
    }
  }
  return $files
}

function Get-RelativePath([string]$path) {
  $root = (Get-Location).Path
  if ($path.StartsWith($root)) {
    return ($path.Substring($root.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar))
  }
  return $path
}

function Remove-RegionsFromFile([string]$filePath) {
  # Read file line by line, preserving original line endings
  $lines = [System.IO.File]::ReadAllLines($filePath)
  $newLines = @()
  $removedCount = 0

  foreach ($line in $lines) {
    # Check if line is a #region or #endregion directive
    if ($line -match '^\s*#\s*(region|endregion)\b') {
      $removedCount++
      # Skip this line (don't add to output)
    } else {
      $newLines += $line
    }
  }

  if ($removedCount -gt 0) {
    # Write back without region lines
    [System.IO.File]::WriteAllLines($filePath, $newLines)
  }

  return $removedCount
}

$violations = @()

Write-Info "Scanning for #region directives in C# files..."

$files = Get-FilesToCheck -StagedOnly:$StagedOnly

foreach ($file in $files) {
  $filePath = $file.FullName
  $rel = Get-RelativePath $filePath

  # Skip .meta files
  if ($filePath -like '*.meta') { continue }

  $content = [System.IO.File]::ReadAllText($filePath)
  if ([string]::IsNullOrWhiteSpace($content)) { continue }

  # Find all #region/#endregion directives
  $matches = $regionPattern.Matches($content)

  foreach ($m in $matches) {
    $directive = $m.Groups[1].Value
    $fullMatch = $m.Value.Trim()

    # Calculate line number
    $prefix = $content.Substring(0, $m.Index)
    $lineNo = ($prefix -split "`n").Length

    $violations += @{
      Path      = $rel
      FullPath  = $filePath
      Line      = $lineNo
      Directive = $fullMatch
      Message   = "UNH005: #$directive directive found. Remove #region/#endregion directives from code."
    }
  }
}

if ($violations.Count -gt 0) {
  $isCI = Test-IsCI
  $canFix = $Fix -and (-not $isCI)

  if ($canFix) {
    # Auto-fix: remove regions from affected files
    Write-Host "Auto-fixing: removing #region directives..." -ForegroundColor Cyan

    # Group violations by file path
    $fileGroups = $violations | Group-Object -Property Path

    $totalRemoved = 0
    $fixedFiles = @()

    foreach ($group in $fileGroups) {
      $filePath = $group.Group[0].FullPath
      $rel = $group.Name

      $removed = Remove-RegionsFromFile $filePath
      if ($removed -gt 0) {
        $totalRemoved += $removed
        $fixedFiles += $rel
        Write-Host "  Removed $removed directive(s) from $rel" -ForegroundColor Green

        # Re-stage the file if we're in staged-only mode using safe retry helper
        if ($StagedOnly -and $null -ne $script:RepositoryInfo) {
          Invoke-GitAddWithRetry -Items @($filePath) -IndexLockPath $script:RepositoryInfo.IndexLockPath -Quiet | Out-Null
        }
      }
    }

    # Run CSharpier on all fixed files
    if ($fixedFiles.Count -gt 0) {
      $fullPaths = $fileGroups | ForEach-Object { $_.Group[0].FullPath }
      Write-Host "Running CSharpier on modified files..." -ForegroundColor Cyan
      Invoke-CSharpier $fullPaths

      # Re-stage after CSharpier formatting using safe retry helper
      if ($StagedOnly -and $null -ne $script:RepositoryInfo) {
        Invoke-GitAddWithRetry -Items $fullPaths -IndexLockPath $script:RepositoryInfo.IndexLockPath -Quiet | Out-Null
      }
    }

    Write-Host ""
    Write-Host "Fixed $($fixedFiles.Count) file(s), removed $totalRemoved #region directive(s)." -ForegroundColor Green
    # Exit successfully since we fixed the issues
    exit 0
  } elseif ($Fix -and $isCI) {
    # In CI, -Fix is ignored; just report errors
    Write-Host "#region lint failed (auto-fix disabled in CI):" -ForegroundColor Red
    Write-Host ""
    foreach ($v in $violations) {
      # Output in format compatible with GitHub Actions annotations
      $ghAnnotation = "::error file=$($v.Path),line=$($v.Line)::$($v.Message)"
      Write-Host $ghAnnotation
      Write-Host ("{0}:{1}: {2}" -f $v.Path, $v.Line, $v.Message) -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Found $($violations.Count) #region directive(s)." -ForegroundColor Red
    Write-Host "#region directives are not allowed. Run locally with -Fix to auto-remove." -ForegroundColor Yellow
    exit 1
  } else {
    Write-Host "#region lint failed:" -ForegroundColor Red
    Write-Host ""
    foreach ($v in $violations) {
      # Output in format compatible with GitHub Actions annotations
      $ghAnnotation = "::error file=$($v.Path),line=$($v.Line)::$($v.Message)"
      Write-Host $ghAnnotation
      Write-Host ("{0}:{1}: {2}" -f $v.Path, $v.Line, $v.Message) -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Found $($violations.Count) #region directive(s)." -ForegroundColor Red
    Write-Host "#region directives are not allowed. Use proper code organization instead." -ForegroundColor Yellow
    exit 1
  }
} else {
  Write-Info "No #region directives found."
  if (-not $StagedOnly) {
    Write-Host "No #region directives found in C# files." -ForegroundColor Green
  }
  exit 0
}
