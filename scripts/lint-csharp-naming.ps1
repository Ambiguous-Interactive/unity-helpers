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
# Load shared comment-masking helper so XML doc/block-comment text doesn't
# trigger the method-declaration regex (e.g., method names referenced in
# `<see cref="Foo_Bar"/>` should not be flagged as underscore violations).
. (Join-Path $PSScriptRoot 'comment-stripping.ps1')

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
  if ($VerboseOutput) { Write-Host "[lint-csharp-naming] $msg" -ForegroundColor Cyan }
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

function Convert-ToPascalCase([string]$name) {
  # Split by underscores and capitalize each part
  $parts = $name -split '_'
  $result = ""
  foreach ($part in $parts) {
    if ($part.Length -gt 0) {
      # Capitalize first letter, keep rest as-is
      $result += $part.Substring(0, 1).ToUpper() + $part.Substring(1)
    }
  }
  return $result
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

# Pattern to match C# method declarations with underscores in name
# This pattern requires:
# - Line start (after optional whitespace)
# - Optional access modifier (public/private/protected/internal)
# - Optional modifiers (static/virtual/override/abstract/sealed/async/new/extern/partial/unsafe)
# - Return type (must be a valid identifier, not starting with underscore)
# - Method name (captured)
# - Opening parenthesis for parameters
# The key improvement: return type must start with uppercase letter (valid C# type)
# or be a keyword like void, int, bool, etc.
# IMPORTANT: leading indent uses `[ \t]*` (NOT `\s*`) so the regex anchors at
# the actual method-declaration line. With `\s*`, masked `///` lines collapse to
# whitespace; `\s` matches `\n`, so the engine would consume newlines and report
# the line number of the preceding doc-comment block instead of the method
# itself. `[ \t]*` keeps each match within a single line.
$methodPattern = [regex]'(?m)^[ \t]*(?:(?:\[[\w\s,\(\)\"=\.]+\][ \t]*)*)(?:(?<access>public|private|protected|internal)\s+)?(?:(?<modifiers>(?:(?:static|virtual|override|abstract|sealed|async|new|extern|partial|unsafe|readonly)\s+)*))(?<return>(?:void|bool|byte|sbyte|char|decimal|double|float|int|uint|long|ulong|short|ushort|string|object|dynamic|var|(?:[A-Z]\w*(?:\s*<[^>]+>)?(?:\s*\[\s*,?\s*\])*(?:\s*\?)?)))(?:\s+)(?<name>[A-Z]\w*)\s*(?:<[^>]+>)?\s*\('

# Pattern specifically for underscore in method name
$underscoreInNamePattern = [regex]'_'

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
  foreach ($rootName in $sourceRoots) {
    # Anchored on the script's own location, not the caller's working directory: these roots are
    # repository-relative, and resolving them against the cwd turned "run from anywhere else" into
    # a hard failure once the missing-root guard below landed.
    $root = Join-Path $PSScriptRoot '..' $rootName
    # Renaming a source root used to remove that whole tree from the scan silently (#556).
    if (-not (Test-Path $root)) {
      Write-Host "[lint-csharp-naming] ERROR: source root not found: $rootName. If it moved, update `$sourceRoots in the same commit." -ForegroundColor Red
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

$violations = @()

Write-Info "Scanning for C# method names with underscores..."

$files = Get-FilesToCheck -StagedOnly:$StagedOnly

# -StagedOnly matching nothing is ordinary; a repository-wide walk finding no C# files is
# the walk breaking, and reporting success for it is the failure mode #556 is about.
if (-not $StagedOnly -and @($files).Count -eq 0) {
  Write-Host '[lint-csharp-naming] ERROR: the repository-wide scan found no C# files, so a pass here would mean nothing.' -ForegroundColor Red
  exit 1
}

foreach ($file in $files) {
  $filePath = $file.FullName
  $rel = Get-RelativePath $filePath

  # Skip .meta files
  if ($filePath -like '*.meta') { continue }

  $content = [System.IO.File]::ReadAllText($filePath)
  if ([string]::IsNullOrWhiteSpace($content)) { continue }

  # Mask comments so XML-doc references / block comments don't get scanned for
  # method declarations. Use Get-CommentRanges so we can mask in-place against
  # $content directly — preserves CRLF, offsets, and length exactly.
  $maskedContent = $content
  $commentRanges = Get-CommentRanges -Text $content -Language 'csharp'
  $chars = $maskedContent.ToCharArray()
  $hasRanges = $false
  foreach ($range in $commentRanges) {
    $hasRanges = $true
    for ($k = $range.Start; $k -lt $range.End -and $k -lt $chars.Length; $k++) {
      if ($chars[$k] -ne "`n" -and $chars[$k] -ne "`r") { $chars[$k] = ' ' }
    }
  }
  if ($hasRanges) { $maskedContent = -join $chars }

  # Find all method declarations against the masked content
  $matches = $methodPattern.Matches($maskedContent)

  foreach ($m in $matches) {
    $methodName = $m.Groups['name'].Value

    # Skip if method name doesn't contain underscore
    if (-not $underscoreInNamePattern.IsMatch($methodName)) { continue }

    # Skip operator overloads (op_Addition, op_Equality, etc.)
    if ($methodName -match '^op_') { continue }

    # Calculate line number
    $prefix = $content.Substring(0, $m.Index)
    $lineNo = ($prefix -split "`n").Length

    $violations += @{
      Path     = $rel
      FullPath = $filePath
      Line     = $lineNo
      Method   = $methodName
      Message  = "UNH004: Method name '$methodName' contains underscore(s). Use PascalCase without underscores."
    }
  }
}

if ($violations.Count -gt 0) {
  $isCI = Test-IsCI
  $canFix = $Fix -and (-not $isCI)

  if ($canFix) {
    # Auto-fix: rename methods in affected files
    Write-Host "Auto-fixing: renaming methods with underscores..." -ForegroundColor Cyan

    # Group violations by file path
    $fileGroups = $violations | Group-Object -Property Path

    $totalRenamed = 0
    $fixedFiles = @()

    foreach ($group in $fileGroups) {
      $filePath = $group.Group[0].FullPath
      $rel = $group.Name

      # Read file content
      $content = [System.IO.File]::ReadAllText($filePath)
      $originalContent = $content

      # Get unique method names to rename in this file
      $methodsToRename = $group.Group | Select-Object -ExpandProperty Method -Unique

      foreach ($oldName in $methodsToRename) {
        $newName = Convert-ToPascalCase $oldName

        # Replace all occurrences with word boundaries
        # Use regex to match the exact method name (not as part of a larger identifier)
        $pattern = "(?<![a-zA-Z0-9_])$([regex]::Escape($oldName))(?![a-zA-Z0-9_])"
        $content = [regex]::Replace($content, $pattern, $newName)

        Write-Host "  $rel : $oldName -> $newName" -ForegroundColor Green
        $totalRenamed++
      }

      # Write back if changed
      if ($content -ne $originalContent) {
        [System.IO.File]::WriteAllText($filePath, $content)
        $fixedFiles += $rel

        # Re-stage the file if we're in staged-only mode
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
    Write-Host "Fixed $($fixedFiles.Count) file(s), renamed $totalRenamed method(s)." -ForegroundColor Green
    Write-Host "Note: References in other files may need manual updating." -ForegroundColor Yellow
    # Exit successfully since we fixed the issues
    exit 0
  } elseif ($Fix -and $isCI) {
    # In CI, -Fix is ignored; just report errors
    Write-Host "C# naming convention lint failed (auto-fix disabled in CI):" -ForegroundColor Red
    Write-Host ""
    foreach ($v in $violations) {
      # Output in format compatible with GitHub Actions annotations
      $ghAnnotation = "::error file=$($v.Path),line=$($v.Line)::$($v.Message)"
      Write-Host $ghAnnotation
      Write-Host ("{0}:{1}: {2}" -f $v.Path, $v.Line, $v.Message) -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Found $($violations.Count) method(s) with underscores in name." -ForegroundColor Red
    Write-Host "Run locally with -Fix to auto-rename methods." -ForegroundColor Yellow
    exit 1
  } else {
    Write-Host "C# naming convention lint failed:" -ForegroundColor Red
    Write-Host ""
    foreach ($v in $violations) {
      # Output in format compatible with GitHub Actions annotations
      $ghAnnotation = "::error file=$($v.Path),line=$($v.Line)::$($v.Message)"
      Write-Host $ghAnnotation
      Write-Host ("{0}:{1}: {2}" -f $v.Path, $v.Line, $v.Message) -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Found $($violations.Count) method(s) with underscores in name." -ForegroundColor Red
    Write-Host "Method names should use PascalCase without underscores (e.g., 'DoSomething' not 'Do_Something')." -ForegroundColor Yellow
    exit 1
  }
} else {
  Write-Info "No naming convention violations found."
  if (-not $StagedOnly) {
    Write-Host "All C# method names follow naming conventions." -ForegroundColor Green
  }
  exit 0
}
