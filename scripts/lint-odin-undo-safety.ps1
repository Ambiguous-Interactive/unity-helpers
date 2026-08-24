Param(
  [switch]$VerboseOutput,
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
# Shared comment-masking helper: avoids flagging WeakTargets/Undo references
# that appear inside `///` xml-doc or `/* ... */` block comments.
. (Join-Path $PSScriptRoot 'comment-stripping.ps1')

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[lint-odin-undo-safety] $msg" -ForegroundColor Cyan }
}

# --- Patterns ---

# Detects WeakTargets cast directly to UnityEngine.Object[] without null filtering
# Unsafe: (UnityEngine.Object[])Property.Tree.WeakTargets
# Unsafe: Property.Tree.WeakTargets.Cast<UnityEngine.Object>().ToArray()
# Unsafe: weakTargets.Cast<UnityEngine.Object>()
$unsafeCastPattern = [regex]'(?i)(?:\(\s*UnityEngine\.Object\s*\[\s*\]\s*\)\s*[\w.]*WeakTargets|WeakTargets\s*\.Cast\s*<\s*UnityEngine\.Object\s*>)'

# Detects "as UnityEngine.Object" casts without pattern-match null filtering
# Unsafe: weakTargets[i] as UnityEngine.Object
# Safe:   weakTargets[i] is UnityEngine.Object unityObject && unityObject != null
$unsafeAsCastPattern = [regex]'(?i)WeakTargets\s*\[.*\]\s+as\s+UnityEngine\.Object'

# Detects Undo.RecordObjects where the array argument contains a direct unsafe cast
# Unsafe: Undo.RecordObjects((UnityEngine.Object[])Property.Tree.WeakTargets, ...)
# Unsafe: Undo.RecordObjects(weakTargets.Cast<UnityEngine.Object>().ToArray(), ...)
$undoWithUnsafeCastPattern = [regex]'(?i)Undo\.RecordObjects\s*\(\s*(?:\(\s*UnityEngine\.Object\s*\[\s*\]\s*\)\s*[\w.]*WeakTargets|.*WeakTargets.*Cast\s*<\s*UnityEngine\.Object\s*>)'

# Safe filtering patterns that indicate null-aware handling near WeakTargets usage
$safeFilterPatterns = @(
  [regex]'(?i)is\s+UnityEngine\.Object\s+\w+\s*&&\s*\w+\s*!=\s*null',     # Pattern-match with null check
  [regex]'(?i)is\s+UnityEngine\.Object\s+\w+\s+and\s+not\s+null',          # C# 9 pattern-match
  [regex]'(?i)Where\s*\(\s*\w+\s*=>\s*\w+\s*!=\s*null\s*\)',               # LINQ Where null filter
  [regex]'(?i)OfType\s*<\s*UnityEngine\.Object\s*>',                        # OfType<T> filters nulls
  [regex]'(?i)\.FindAll\s*\(\s*\w+\s*=>\s*\w+\s*!=\s*null',                # FindAll null filter
  [regex]'(?i)serializedObject\.targetObjects'                               # Already safe property
)

# Suppression comment pattern
$suppressPattern = [regex]'UNH-SUPPRESS'

$violations = @()

function Get-RelativePath([string]$path) {
  $root = (Get-Location).Path
  if ($path.StartsWith($root)) {
    return ($path.Substring($root.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar))
  }
  return $path
}

function Test-SafeFilter([string[]]$content, [int]$lineIndex, [int]$searchRange) {
  # Search nearby lines for safe filtering patterns
  $zeroBasedIndex = $lineIndex - 1
  $startLine = [Math]::Max(0, $zeroBasedIndex - $searchRange)
  $endLine = [Math]::Min($content.Count - 1, $zeroBasedIndex + $searchRange)

  for ($i = $startLine; $i -le $endLine; $i++) {
    foreach ($pattern in $safeFilterPatterns) {
      if ($pattern.IsMatch($content[$i])) {
        return $true
      }
    }
  }
  return $false
}

Write-Host "Checking Odin drawer Undo safety patterns..." -ForegroundColor White

# Resolve files to scan
$filesToScan = @()
if ($Paths -and $Paths.Count -gt 0) {
  foreach ($p in $Paths) {
    try {
      $resolved = Resolve-Path $p -ErrorAction Stop
      if ($resolved -and ($resolved.Path -like '*.cs')) {
        $filesToScan += $resolved.Path
      }
    } catch {
      Write-Info "Skipping path '$p' because it was not found."
    }
  }
} else {
  $odinPath = 'Editor/CustomDrawers/Odin'
  if (Test-Path $odinPath) {
    # [IO.Directory]::EnumerateFiles rather than Get-ChildItem -Recurse -Include; the
    # Sort-Object -Unique below already fixes the order.
    $filesToScan += [System.IO.Directory]::EnumerateFiles(
      (Resolve-Path -LiteralPath $odinPath).Path,
      '*.cs',
      [System.IO.SearchOption]::AllDirectories
    )
  }
}

$filesToScan = $filesToScan | Sort-Object -Unique

foreach ($file in $filesToScan) {
  if ($file -like '*.meta') { continue }
  $rel = Get-RelativePath $file

  $rawContent = [System.IO.File]::ReadAllLines($file)
  if ($rawContent.Count -eq 0) { continue }

  # Replace comments with spaces so unsafe-cast patterns inside XML-doc/block
  # comments don't trigger false positives. Pattern-match against masked lines,
  # but check UNH-SUPPRESS against the ORIGINAL (since the suppress marker
  # itself lives in a comment).
  $masked = Get-CommentMaskedLines -Lines $rawContent -Language 'csharp'
  if ($null -eq $masked) { $masked = $rawContent }

  Write-Info "Scanning $rel"

  $lineIndex = 0
  foreach ($line in $masked) {
    $lineIndex++
    $rawLine = $rawContent[$lineIndex - 1]

    # Skip lines with UNH-SUPPRESS comment (check the raw line: suppress marker
    # lives inside `//` and would be masked away on $line)
    if ($suppressPattern.IsMatch($rawLine)) { continue }

    # Pattern 1: Direct cast of WeakTargets to UnityEngine.Object[]
    if ($unsafeCastPattern.IsMatch($line)) {
      $hasSafe = Test-SafeFilter $masked $lineIndex 10

      if (-not $hasSafe) {
        $violations += @{
          Path = $rel
          Line = $lineIndex
          Message = "UNH006: Unsafe cast of WeakTargets to UnityEngine.Object[]. WeakTargets may contain non-UnityEngine.Object entries or destroyed objects. Filter with null-check pattern: 'if (weakTargets[i] is UnityEngine.Object obj && obj != null)'."
        }
      }
    }

    # Pattern 2: "as UnityEngine.Object" cast without pattern-match filter
    if ($unsafeAsCastPattern.IsMatch($line)) {
      $hasSafe = Test-SafeFilter $masked $lineIndex 10

      if (-not $hasSafe) {
        $violations += @{
          Path = $rel
          Line = $lineIndex
          Message = "UNH006: Unsafe 'as UnityEngine.Object' cast on WeakTargets element. Use pattern matching with null check: 'if (weakTargets[i] is UnityEngine.Object obj && obj != null)'."
        }
      }
    }

    # Pattern 3: Undo.RecordObjects with unsafely-cast WeakTargets array
    if ($undoWithUnsafeCastPattern.IsMatch($line)) {
      $violations += @{
        Path = $rel
        Line = $lineIndex
        Message = "UNH006: Undo.RecordObjects called with unsafely-cast WeakTargets. Build a filtered array excluding null entries before passing to Undo.RecordObjects."
      }
    }
  }
}

if ($violations.Count -gt 0) {
  Write-Host "Odin drawer Undo safety lint failed:" -ForegroundColor Red
  Write-Host ""
  foreach ($v in $violations) {
    # Output in format compatible with GitHub Actions annotations
    $ghAnnotation = "::error file=$($v.Path),line=$($v.Line)::$($v.Message)"
    Write-Host $ghAnnotation
    Write-Host ("{0}:{1}: {2}" -f $v.Path, $v.Line, $v.Message) -ForegroundColor Yellow
  }
  Write-Host ""
  Write-Host "Found $($violations.Count) unsafe Undo pattern(s) in Odin drawers." -ForegroundColor Red
  Write-Host ""
  Write-Host "Safe pattern:" -ForegroundColor Yellow
  Write-Host "  IList weakTargets = Property.Tree.WeakTargets;" -ForegroundColor Yellow
  Write-Host "  List<UnityEngine.Object> validTargets = new(weakTargets.Count);" -ForegroundColor Yellow
  Write-Host "  for (int i = 0; i < weakTargets.Count; i++)" -ForegroundColor Yellow
  Write-Host "  {" -ForegroundColor Yellow
  Write-Host "      if (weakTargets[i] is UnityEngine.Object obj && obj != null)" -ForegroundColor Yellow
  Write-Host "          validTargets.Add(obj);" -ForegroundColor Yellow
  Write-Host "  }" -ForegroundColor Yellow
  Write-Host "  Undo.RecordObjects(validTargets.ToArray(), `"..`");" -ForegroundColor Yellow
  Write-Host ""
  Write-Host "Add '// UNH-SUPPRESS' on the line to suppress intentional uses." -ForegroundColor Yellow
  exit 1
} else {
  Write-Info "No unsafe Undo patterns found."
  Write-Host "All Odin drawers use safe Undo patterns." -ForegroundColor Green
  exit 0
}
