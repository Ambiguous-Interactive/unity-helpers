Param(
  [switch]$VerboseOutput,
  [switch]$StagedOnly,
  [string[]]$Paths,
  # `pwsh -File <script> -Paths a b c` binds ONLY `a` to -Paths and silently drops the rest, so a
  # multi-file invocation scans one file and reports success. This catches the remainder; see
  # .llm/skills/bash-pwsh-invocation.md.
  [Parameter(ValueFromRemainingArguments = $true)]
  [string[]]$AdditionalPaths
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The remaining-args catch above is only half of it: the two have to be merged before anything
# reads $Paths, or the extra files are collected and ignored.
if ($AdditionalPaths) {
  $Paths = @($Paths) + @($AdditionalPaths)
}

function Write-Info($msg) {
  if ($VerboseOutput) {
    Write-Host "[lint-duplicate-usings] $msg" -ForegroundColor Cyan
  }
}

function Get-RelativePath([string]$path) {
  $root = (Get-Location).Path
  if ($path.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
    $trimChars = @([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $relative = $path.Substring($root.Length).TrimStart($trimChars)
    return ($relative -replace '\\', '/')
  }

  return ($path -replace '\\', '/')
}

function New-UsingScope {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Name,
    [Parameter(Mandatory = $true)]
    [int]$ExitDepth
  )

  return @{
    Name = $Name
    ExitDepth = $ExitDepth
    Collecting = $true
    Seen = [System.Collections.Generic.Dictionary[string, int]]::new([System.StringComparer]::Ordinal)
  }
}

function Try-GetUsingDirectiveNormalization {
  param(
    [Parameter(Mandatory = $true)]
    [string]$TrimmedLine,
    [Parameter(Mandatory = $true)]
    [ref]$NormalizedDirective
  )

  $NormalizedDirective.Value = $null

  if (-not $TrimmedLine.StartsWith('using ')) {
    return $false
  }

  if (-not $TrimmedLine.EndsWith(';')) {
    return $false
  }

  # Skip `using (...)` statements.
  if ($TrimmedLine.Contains('(')) {
    return $false
  }

  $body = $TrimmedLine.Substring(6).Trim()
  $body = $body.TrimEnd(';').Trim()
  if ([string]::IsNullOrWhiteSpace($body)) {
    return $false
  }

  $isStatic = $false
  if ($body.StartsWith('static ')) {
    $isStatic = $true
    $body = $body.Substring(7).Trim()
    if ([string]::IsNullOrWhiteSpace($body)) {
      return $false
    }
  }

  if ($body.Contains('=')) {
    # Alias directives are valid (`using Alias = Namespace.Type;`).
    # Local using declarations (`using Foo bar = ...;`) have whitespace on the left side.
    if ($isStatic) {
      return $false
    }

    $parts = $body -split '=', 2
    if ($parts.Count -ne 2) {
      return $false
    }

    $left = $parts[0].Trim()
    $right = $parts[1].Trim()
    if ([string]::IsNullOrWhiteSpace($left) -or [string]::IsNullOrWhiteSpace($right)) {
      return $false
    }

    if ($left -match '\s') {
      return $false
    }

    $NormalizedDirective.Value = "using $left = $right;"
    return $true
  }

  # Import and static-import directives should not contain additional whitespace once normalized.
  if ($body -match '\s') {
    return $false
  }

  if ($isStatic) {
    $NormalizedDirective.Value = "using static $body;"
  }
  else {
    $NormalizedDirective.Value = "using $body;"
  }

  return $true
}

function Get-FilesToScan {
  param(
    [switch]$StagedOnly,
    [string[]]$Paths
  )

  $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
  $results = New-Object System.Collections.Generic.List[string]

  if ($Paths -and $Paths.Count -gt 0) {
    foreach ($path in $Paths) {
      try {
        $resolved = Resolve-Path -LiteralPath $path -ErrorAction Stop
      }
      catch {
        Write-Info "Skipping missing path: $path"
        continue
      }

      if ($resolved.Path -notlike '*.cs') {
        continue
      }

      if ($seen.Add($resolved.Path)) {
        $results.Add($resolved.Path) | Out-Null
      }
    }

    return @($results)
  }

  if ($StagedOnly) {
    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $git) {
      Write-Info 'git not found; no staged files to scan.'
      return @()
    }

    $staged = & git diff --cached --name-only --diff-filter=ACM -- '*.cs' 2>$null
    if ($LASTEXITCODE -ne 0 -or $null -eq $staged) {
      return @()
    }

    foreach ($entry in $staged) {
      if ([string]::IsNullOrWhiteSpace($entry)) {
        continue
      }

      $candidate = $entry.Trim()
      if (-not (Test-Path -LiteralPath $candidate)) {
        continue
      }

      $resolved = (Resolve-Path -LiteralPath $candidate).Path
      if ($seen.Add($resolved)) {
        $results.Add($resolved) | Out-Null
      }
    }

    return @($results)
  }

  $sourceRoots = @('Runtime', 'Editor', 'Tests', 'Samples~')
  foreach ($root in $sourceRoots) {
    if (-not (Test-Path -LiteralPath $root)) {
      continue
    }

    # [IO.Directory]::EnumerateFiles rather than Get-ChildItem -Recurse, which builds a FileInfo
    # per entry. Measured on this repository over the devcontainer's 9p mount: 0.8 s against 4.6 s
    # for the same file set. Sorted because the walk order is the filesystem's, and a linter that
    # reports findings in a different order on every machine is a diff nobody can review.
    $matched = [System.IO.Directory]::EnumerateFiles(
      (Resolve-Path -LiteralPath $root).Path,
      '*.cs',
      [System.IO.SearchOption]::AllDirectories
    )
    foreach ($file in ($matched | Sort-Object)) {
      if ($seen.Add($file)) {
        $results.Add($file) | Out-Null
      }
    }
  }

  return @($results)
}

$filesToScan = @(Get-FilesToScan -StagedOnly:$StagedOnly -Paths $Paths)
if ($filesToScan.Count -eq 0) {
  # An explicit filter that matches nothing is ordinary -- agent:preflight passes the changed files
  # and there may be none. A whole-tree scan that finds nothing is not: it means the walk broke
  # (#556).
  if ($StagedOnly -or ($Paths -and 0 -lt $Paths.Count)) {
    Write-Info 'No C# files to scan (the supplied filter matched nothing).'
    exit 0
  }

  Write-Host '[lint-duplicate-usings] ERROR: the repository-wide scan found no C# files, so a pass here would mean nothing.' -ForegroundColor Red
  exit 1
}

Write-Info "Scanning $($filesToScan.Count) C# file(s) for duplicate using directives..."

$violations = @()

foreach ($filePath in $filesToScan) {
  $relativePath = Get-RelativePath $filePath
  $lines = [System.IO.File]::ReadAllLines($filePath)

  if ($lines.Count -eq 0) {
    continue
  }

  $braceDepth = 0
  $pendingNamespaceName = $null
  $scopes = [System.Collections.Generic.List[hashtable]]::new()
  $scopes.Add((New-UsingScope -Name '<global>' -ExitDepth -1)) | Out-Null

  for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = [string]$lines[$i]
    $trimmed = $line.Trim()

    $fileScopedNamespaceMatch = [regex]::Match(
      $trimmed,
      '^namespace\s+(?<name>[A-Za-z_][A-Za-z0-9_\.]*)\s*;'
    )

    if ($fileScopedNamespaceMatch.Success) {
      $scopes.Add(
        (New-UsingScope -Name $fileScopedNamespaceMatch.Groups['name'].Value -ExitDepth -2)
      ) | Out-Null
    }
    else {
      $blockNamespaceMatch = [regex]::Match(
        $trimmed,
        '^namespace\s+(?<name>[A-Za-z_][A-Za-z0-9_\.]*)\b'
      )

      if ($blockNamespaceMatch.Success) {
        if ($trimmed -match '\{\s*(//.*)?$') {
          $scopes.Add(
            (New-UsingScope -Name $blockNamespaceMatch.Groups['name'].Value -ExitDepth ($braceDepth + 1))
          ) | Out-Null
        }
        else {
          $pendingNamespaceName = $blockNamespaceMatch.Groups['name'].Value
        }
      }
      elseif ($null -ne $pendingNamespaceName -and $trimmed -match '^\{\s*(//.*)?$') {
        $scopes.Add((New-UsingScope -Name $pendingNamespaceName -ExitDepth ($braceDepth + 1))) | Out-Null
        $pendingNamespaceName = $null
      }
    }

    $activeScope = $scopes[$scopes.Count - 1]
    if ($activeScope.Collecting) {
      $ignoreLine = [string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith('//') -or $trimmed.StartsWith('#') -or $trimmed.StartsWith('/*') -or $trimmed.StartsWith('*') -or $trimmed.StartsWith('*/')

      if (-not $ignoreLine) {
        $normalizedDirective = $null
        if (Try-GetUsingDirectiveNormalization -TrimmedLine $trimmed -NormalizedDirective ([ref]$normalizedDirective)) {
          if ($activeScope.Seen.ContainsKey($normalizedDirective)) {
            $firstLine = $activeScope.Seen[$normalizedDirective]
            $violations += @{
              Path = $relativePath
              Line = $i + 1
              Scope = $activeScope.Name
              FirstLine = $firstLine
              Directive = $normalizedDirective
              Message = "UNH007: Duplicate using directive '$normalizedDirective' in scope '$($activeScope.Name)' (first declaration at line $firstLine)."
            }
          }
          else {
            $activeScope.Seen[$normalizedDirective] = $i + 1
          }
        }
        elseif ($trimmed -eq '{' -or $trimmed -eq '}' -or $trimmed -like 'namespace *') {
          # Structural lines do not end the using-directive region.
        }
        else {
          $activeScope.Collecting = $false
        }
      }
    }

    # Count only structural brace lines so braces in strings/comments do not corrupt scope depth.
    if ($trimmed -match '^\{\s*(//.*)?$') {
      $braceDepth++
    }
    elseif ($trimmed -match '^\}\s*(//.*)?$') {
      $braceDepth--
    }

    while ($scopes.Count -gt 1) {
      $topScope = $scopes[$scopes.Count - 1]
      if ($topScope.ExitDepth -ge 0 -and $braceDepth -lt $topScope.ExitDepth) {
        $scopes.RemoveAt($scopes.Count - 1)
        continue
      }

      break
    }
  }
}

if ($violations.Count -gt 0) {
  Write-Host 'Duplicate using directive lint failed:' -ForegroundColor Red
  Write-Host ''
  foreach ($violation in $violations) {
    $ghAnnotation = "::error file=$($violation.Path),line=$($violation.Line)::$($violation.Message)"
    Write-Host $ghAnnotation
    Write-Host ("{0}:{1}: {2}" -f $violation.Path, $violation.Line, $violation.Message) -ForegroundColor Yellow
  }

  Write-Host ''
  Write-Host "Found $($violations.Count) duplicate using directive violation(s)." -ForegroundColor Red
  Write-Host 'Remove duplicate using directives within each namespace/file scope.' -ForegroundColor Yellow
  exit 1
}

Write-Info 'No duplicate using directives found.'
if (-not $StagedOnly -and (-not $Paths -or $Paths.Count -eq 0)) {
  Write-Host 'No duplicate using directives found.' -ForegroundColor Green
}

exit 0
