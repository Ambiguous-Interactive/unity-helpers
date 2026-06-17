Param(
  [switch]$VerboseOutput,
  [string[]]$Paths,
  [switch]$FixNullChecks
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'comment-stripping.ps1')

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[lint-tests] $msg" -ForegroundColor Cyan }
}

# Heuristics and allowlists
$testRoots = @('Tests')
$allowedHelperFiles = @(
  'Tests/Runtime/Visuals/VisualsTestHelpers.cs',
  'Tests/Core/TextureTestHelper.cs',
  'Tests/Editor/Sprites/SpriteSheetExtractor/SharedSpriteTestFixtures.cs',
  'Tests/Editor/TestAssets/SharedAnimationTestFixtures.cs',
  'Tests/Editor/TestAssets/SharedEditorTestFixtures.cs',
  'Tests/Editor/TestAssets/SharedTextureTestFixtures.cs'
)

# Validate allowlisted paths exist (catches stale paths after file moves)
# Only validate when running from the repo root (package.json present)
if (Test-Path (Join-Path (Get-Location).Path 'package.json')) {
  foreach ($helperPath in $allowedHelperFiles) {
    $fullPath = Join-Path (Get-Location).Path $helperPath
    if (-not (Test-Path $fullPath)) {
      Write-Host "ERROR: Allowlisted helper file not found: $helperPath" -ForegroundColor Red
      Write-Host "  The file may have been moved or renamed. Update `$allowedHelperFiles in lint-tests.ps1." -ForegroundColor Yellow
      exit 1
    }
  }
}

# Removes C# string literals, character literals, and comments from a source
# string so downstream regex/bracket analysis ignores their contents. The
# content of each literal/comment is replaced with same-length whitespace so
# column positions (and therefore line counts) are preserved.
#
# Two-pass implementation:
#   1. Delegate comment masking (//, /* */ single- AND multi-line, /// XML
#      doc) to the shared helper in comment-stripping.ps1. The helper
#      additionally handles C# 11 raw string literals (""".."""), verbatim
#      strings (@"..."") with "" escapes, and interpolated strings ($"..."
#      and $@"...""), so no comment delimiter inside any string form can be
#      mistaken for a real comment opener.
#   2. Walk the comment-masked text to blank string contents: normal "..."
#      strings honor \" escapes; verbatim @"..." strings honor "" escapes;
#      character literals '...' have their content blanked.
#
# See scripts/comment-stripping.ps1 for the comment-masking language rules.
function Remove-CsStringLiteralsAndLineComments([string]$text) {
  if ([string]::IsNullOrEmpty($text)) { return $text }

  # Pass 1: mask comments via the shared helper. Splitting on "`n" then
  # rejoining on "`n" preserves total length because Get-CommentMaskedLines
  # masks comment chars with spaces while preserving newlines. When the
  # source uses CRLF, the "`r" on each line survives the split/join and
  # column counts are unchanged.
  $lines = $text -split "`n", -1
  $maskedLines = Get-CommentMaskedLines -Lines $lines -Language 'csharp'
  $commentMasked = [string]::Join("`n", $maskedLines)

  # Defensive: if the helper ever changed length (e.g. trailing newline
  # handling quirk), fall back to the original text for pass 2 rather than
  # corrupt column offsets. In practice this branch is unreachable.
  if ($commentMasked.Length -ne $text.Length) {
    $commentMasked = $text
  }

  # Pass 2: blank string/char literal contents.
  $sb = New-Object System.Text.StringBuilder ($commentMasked.Length)
  $i = 0
  $n = $commentMasked.Length
  while ($i -lt $n) {
    $c = $commentMasked[$i]
    $next = if ($i + 1 -lt $n) { $commentMasked[$i + 1] } else { [char]0 }

    # Raw string literal: """...""" (C# 11). Opens with N >= 3 consecutive
    # `"` and closes with the same count. Content is blanked so embedded
    # code-like text (e.g. `[Test]`, `Object.Destroy(x)`) cannot match
    # downstream regexes. Newlines are preserved to keep line numbers.
    if ($c -eq '"' -and $next -eq '"' -and ($i + 2) -lt $n -and $commentMasked[$i + 2] -eq '"') {
      $quoteCount = 0
      $j = $i
      while ($j -lt $n -and $commentMasked[$j] -eq '"') { $quoteCount++; $j++ }
      # Emit the opening quote run verbatim.
      for ($q = 0; $q -lt $quoteCount; $q++) { [void]$sb.Append('"') }
      $i = $j
      while ($i -lt $n) {
        if ($commentMasked[$i] -eq '"') {
          $endCount = 0
          $k = $i
          while ($k -lt $n -and $commentMasked[$k] -eq '"') { $endCount++; $k++ }
          if ($endCount -ge $quoteCount) {
            # Emit the closing quote run verbatim; any trailing quotes
            # beyond $quoteCount belong to surrounding code and are
            # appended as-is so column offsets stay intact.
            for ($q = 0; $q -lt $endCount; $q++) { [void]$sb.Append('"') }
            $i = $k
            break
          }
          # Fewer than $quoteCount quotes — part of the body; blank them.
          for ($q = 0; $q -lt $endCount; $q++) { [void]$sb.Append(' ') }
          $i = $k
          continue
        }
        if ($commentMasked[$i] -eq "`n" -or $commentMasked[$i] -eq "`r") {
          [void]$sb.Append($commentMasked[$i])
        } else {
          [void]$sb.Append(' ')
        }
        $i++
      }
      continue
    }

    # Verbatim string: @"..." (possibly $@"..." interpolated verbatim).
    # The leading "$" on $@"..." is unremarkable to the blanker — we match
    # on @" and handle the string body the same way either form enters.
    if ($c -eq '@' -and $next -eq '"') {
      [void]$sb.Append('@')
      [void]$sb.Append('"')
      $i += 2
      while ($i -lt $n) {
        $ch = $commentMasked[$i]
        if ($ch -eq '"') {
          if ($i + 1 -lt $n -and $commentMasked[$i + 1] -eq '"') {
            # Escaped doubled quote — blank both and continue
            [void]$sb.Append(' ')
            [void]$sb.Append(' ')
            $i += 2
            continue
          }
          [void]$sb.Append('"')
          $i++
          break
        }
        if ($ch -eq "`n" -or $ch -eq "`r") {
          [void]$sb.Append($ch)
        } else {
          [void]$sb.Append(' ')
        }
        $i++
      }
      continue
    }

    # Normal string: "..." (also reached for $"..." interpolated strings;
    # the leading "$" is passed through unchanged and the string body is
    # blanked the same way).
    if ($c -eq '"') {
      [void]$sb.Append('"')
      $i++
      while ($i -lt $n) {
        $ch = $commentMasked[$i]
        if ($ch -eq '\') {
          [void]$sb.Append(' ')
          if ($i + 1 -lt $n) { [void]$sb.Append(' '); $i += 2 } else { $i++ }
          continue
        }
        if ($ch -eq '"') {
          [void]$sb.Append('"')
          $i++
          break
        }
        if ($ch -eq "`n" -or $ch -eq "`r") {
          [void]$sb.Append($ch)
        } else {
          [void]$sb.Append(' ')
        }
        $i++
      }
      continue
    }

    # Character literal: '...'
    if ($c -eq "'") {
      [void]$sb.Append("'")
      $i++
      while ($i -lt $n) {
        $ch = $commentMasked[$i]
        if ($ch -eq '\') {
          [void]$sb.Append(' ')
          if ($i + 1 -lt $n) { [void]$sb.Append(' '); $i += 2 } else { $i++ }
          continue
        }
        if ($ch -eq "'") {
          [void]$sb.Append("'")
          $i++
          break
        }
        if ($ch -eq "`n" -or $ch -eq "`r") {
          [void]$sb.Append($ch)
        } else {
          [void]$sb.Append(' ')
        }
        $i++
      }
      continue
    }

    [void]$sb.Append($c)
    $i++
  }
  return $sb.ToString()
}

$destroyPattern = [regex]'\b(?:UnityEngine\.)?Object\.(?:DestroyImmediate|Destroy)\s*\((?<arg>[^)]*)\)'
$createAssignObjectPattern = [regex]'(?<var>\b\w+)\s*=\s*new\s+(?<type>GameObject|Texture2D|Material|Mesh|Camera)\s*\('
$createInlineTrackPattern = [regex]'\bTrack\s*\(\s*new\s+(?:GameObject|Texture2D|Material|Mesh|Camera)\s*\('
$createSoAssignPattern = [regex]'(?<var>\b\w+)\s*=\s*ScriptableObject\.CreateInstance\s*<'

# Naming convention patterns (UNH004: No underscores in test names)
# Matches: TestName = "Some_Name" or TestName = @"Some_Name"
$testNameUnderscorePattern = [regex]'TestName\s*=\s*@?"[^"]*_[^"]*"'
# Matches: .SetName("Some_Name") or .SetName(@"Some_Name")
$setNameUnderscorePattern = [regex]'\.SetName\s*\(\s*@?"[^"]*_[^"]*"\s*\)'
# Matches: TestCaseSource method names with underscores (nameof(Some_Method) or "Some_Method")
$testCaseSourcePattern = [regex]'\[TestCaseSource\s*\(\s*(?:nameof\s*\(\s*(?<methodName>\w+)\s*\)|"(?<stringName>\w+)")\s*\)\]'
# Matches a C# method signature line and captures its name. Tolerates modifiers
# (public/private/internal/protected/static/async/override/virtual/sealed/new/extern/unsafe/partial),
# generic type parameters on the return type, ref/namespace-qualified return types, and
# optional leading attribute(s) on the same line (e.g. "[Test] public void Inline_Test()").
# Deliberately anchored to lines that end with "(" so we don't match variable
# declarations or calls. We re-check the body context before reporting.
$methodDeclPattern = [regex]'^\s*(?:\[[^\]]+\]\s*)*(?:(?:public|private|protected|internal|static|async|override|virtual|sealed|new|extern|unsafe|partial)\s+)+(?<retType>[\w\.\<\>\,\s\?\[\]]+?)\s+(?<name>[A-Za-z_]\w*)\s*\('
# Recognizes an attribute block (possibly multi-line, reconstructed with
# bracket-balance joining) whose FIRST attribute is a test-eligible attribute.
# Used against already-reconstructed attribute-block text, not raw lines.
# Accepts an optional "global::" root-namespace prefix, an optional
# namespace-qualified prefix (e.g. "NUnit.Framework."), and an optional
# "Attribute" suffix (NUnit allows both short and long forms).
$testAttributeLinePattern = [regex]'^\s*\[\s*(?:global\s*::\s*)?(?:[A-Za-z_][\w\.]*\.)?(?:Test|TestCase|TestCaseSource|UnityTest)(?:Attribute)?(?:\s*\(|\s*\]|\s*,)'
# Recognizes a reconstructed attribute-block (one or more [Attr(...)] on the
# same logical line, possibly followed by whitespace and an optional
# trailing // comment). Used on the reconstructed joined line, so multi-line
# attribute arguments are already collapsed by the bracket-balance walker.
$anyAttributeLinePattern = [regex]'^\s*\[[^\]]+\](?:\s*\[[^\]]+\])*\s*(?://.*)?$'
# Recognizes a same-line leading test attribute prefix on the signature line
# itself (e.g. "[Test] public void Inline_Test() { }"). Matches qualified and
# long-form attribute names consistent with $testAttributeLinePattern. Also
# recognizes the comma form inside a single bracket (e.g.
# "[Test, Category(\"Fast\")] public void Foo()").
$inlineTestAttributePattern = [regex]'^\s*\[\s*(?:global\s*::\s*)?(?:[A-Za-z_][\w\.]*\.)?(?:Test|TestCase|TestCaseSource|UnityTest)(?:Attribute)?(?:\s*\(|\s*\]|\s*,)'
# Detects a test-eligible attribute appearing anywhere on the signature line,
# not just as the first attribute. Catches stacked-inline forms like
# "[Category(\"Fast\")][Test] public void Foo()" that the anchored variant
# misses. Operates on a SCRUBBED line so "[Test]" inside a string literal
# cannot match.
$inlineTestAttributeAnywherePattern = [regex]'\[\s*(?:global\s*::\s*)?(?:[A-Za-z_][\w\.]*\.)?(?:Test|TestCase|TestCaseSource|UnityTest)(?:Attribute)?(?:\s*\(|\s*\]|\s*,)'

# UNH005: Assert.IsNull/IsNotNull patterns (should use Assert.IsTrue for Unity null checks)
$assertIsNullPattern = [regex]'Assert\.IsNull\s*\('
$assertIsNotNullPattern = [regex]'Assert\.IsNotNull\s*\('
$testCaseDataReturnsNullPattern = [regex]'(?ms)\.Returns\s*\(\s*null\s*\)'
$unityTestAttributePattern = [regex]'\[\s*(?:global\s*::\s*)?(?:[A-Za-z_][\w\.]*\.)?UnityTest(?:Attribute)?(?:\s*\(|\s*\])'
$coroutineMethodPattern = [regex]'\bIEnumerator\s+(?<name>\w+)\s*\('

# Returns true if line contains an allowlisted helper file path
function Is-AllowlistedFile([string]$relPath) {
  $normalized = ($relPath -replace '\\','/') -replace '^\.\/', ''
  foreach ($a in $allowedHelperFiles) {
    if ($normalized -ieq $a) { return $true }
  }
  return $false
}

function Get-RelativePath([string]$path) {
  $root = (Get-Location).Path
  return ($path.Substring($root.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar))
}

function Fix-UnityNullAssertions {
  Param(
    [string]$Text
  )

  $originalText = $Text

  $patternNotNullWithMessage = [regex]'(?ms)^(?<indent>\s*)Assert\.IsNotNull\s*\(\s*(?<expr>[^,]+?)\s*,\s*(?<message>[^)]*?)\s*\);'
  $patternNotNullNoMessage = [regex]'(?ms)^(?<indent>\s*)Assert\.IsNotNull\s*\(\s*(?<expr>[^\)]+?)\s*\);'
  $patternNullWithMessage = [regex]'(?ms)^(?<indent>\s*)Assert\.IsNull\s*\(\s*(?<expr>[^,]+?)\s*,\s*(?<message>[^)]*?)\s*\);'
  $patternNullNoMessage = [regex]'(?ms)^(?<indent>\s*)Assert\.IsNull\s*\(\s*(?<expr>[^\)]+?)\s*\);'

  $Text = $patternNotNullWithMessage.Replace($Text, {
      param($m)
      $indent = $m.Groups['indent'].Value
      $expr = $m.Groups['expr'].Value.Trim()
      $message = $m.Groups['message'].Value.Trim()
      return "$indent" + "Assert.IsTrue($expr != null, $message);"
    })

  $Text = $patternNotNullNoMessage.Replace($Text, {
      param($m)
      $indent = $m.Groups['indent'].Value
      $expr = $m.Groups['expr'].Value.Trim()
      return "$indent" + "Assert.IsTrue($expr != null);"
    })

  $Text = $patternNullWithMessage.Replace($Text, {
      param($m)
      $indent = $m.Groups['indent'].Value
      $expr = $m.Groups['expr'].Value.Trim()
      $message = $m.Groups['message'].Value.Trim()
      return "$indent" + "Assert.IsTrue($expr == null, $message);"
    })

  $Text = $patternNullNoMessage.Replace($Text, {
      param($m)
      $indent = $m.Groups['indent'].Value
      $expr = $m.Groups['expr'].Value.Trim()
      return "$indent" + "Assert.IsTrue($expr == null);"
    })

  $modified = ($originalText -ne $Text)
  return @{
    Text = $Text
    Modified = $modified
  }
}

function Get-TestCaseSourceBody {
  Param(
    [string[]]$Lines,
    [string[]]$ScrubbedLines,
    [string]$SourceName
  )

  $escapedSourceName = [regex]::Escape($SourceName)
  $sourceDeclarationPattern = [regex](
    "(?:IEnumerable\s*<\s*TestCaseData\s*>|IEnumerable|TestCaseData\s*\[\]|List\s*<\s*TestCaseData\s*>)\s+$escapedSourceName\b"
  )

  for ($lineIndex = 0; $lineIndex -lt $ScrubbedLines.Count; $lineIndex++) {
    if ($ScrubbedLines[$lineIndex] -match 'TestCaseSource') { continue }
    if (-not $sourceDeclarationPattern.IsMatch($ScrubbedLines[$lineIndex])) { continue }

    $braceDepth = 0
    $bodyStart = -1
    for ($bodyIndex = $lineIndex; $bodyIndex -lt $ScrubbedLines.Count; $bodyIndex++) {
      $scrubbedLine = $ScrubbedLines[$bodyIndex]
      for ($charIndex = 0; $charIndex -lt $scrubbedLine.Length; $charIndex++) {
        if ($scrubbedLine[$charIndex] -eq '{') {
          if ($bodyStart -lt 0) { $bodyStart = $bodyIndex }
          $braceDepth++
        } elseif ($scrubbedLine[$charIndex] -eq '}') {
          if ($bodyStart -ge 0) {
            $braceDepth--
            if ($braceDepth -eq 0) {
              return [pscustomobject]@{
                Text = [string]::Join("`n", $Lines[$bodyStart..$bodyIndex])
                ScrubbedText = [string]::Join("`n", $ScrubbedLines[$bodyStart..$bodyIndex])
                StartLine = $bodyStart + 1
              }
            }
          }
        }
      }

      if ($bodyStart -lt 0 -and $scrubbedLine -match ';') {
        return [pscustomobject]@{
          Text = [string]::Join("`n", $Lines[$lineIndex..$bodyIndex])
          ScrubbedText = [string]::Join("`n", $ScrubbedLines[$lineIndex..$bodyIndex])
          StartLine = $lineIndex + 1
        }
      }
    }
  }

  return $null
}

function Get-TestCaseDataChains {
  Param(
    [string]$Text,
    [int]$StartLine
  )

  $chains = @()
  $matches = [regex]::Matches($Text, 'new\s+TestCaseData\s*\(')
  foreach ($match in $matches) {
    $depth = 0
    $endIndex = $Text.Length - 1
    for ($charIndex = $match.Index; $charIndex -lt $Text.Length; $charIndex++) {
      $char = $Text[$charIndex]
      if ($char -eq '(' -or $char -eq '[' -or $char -eq '{') {
        $depth++
        continue
      }
      if ($char -eq ')' -or $char -eq ']' -or $char -eq '}') {
        if ($depth -gt 0) {
          $depth--
          continue
        }
        $endIndex = $charIndex
        break
      }
      if (($char -eq ';' -or $char -eq ',') -and $depth -eq 0) {
        $endIndex = $charIndex
        break
      }
    }

    $prefix = $Text.Substring(0, $match.Index)
    $sourceLine = $StartLine + (($prefix -split "`n").Length) - 1
    $chains += [pscustomobject]@{
      Text = $Text.Substring($match.Index, $endIndex - $match.Index)
      Line = $sourceLine
    }
  }

  return $chains
}

function Get-TestAttributeBlockForMethod {
  Param(
    [string[]]$Lines,
    [string[]]$ScrubbedLines,
    [int]$MethodLineIndex
  )

  $originalParts = New-Object System.Collections.Generic.List[string]
  $scrubbedParts = New-Object System.Collections.Generic.List[string]

  $methodLine = $ScrubbedLines[$MethodLineIndex]
  $methodMatch = $coroutineMethodPattern.Match($methodLine)
  if ($methodMatch.Success -and $methodMatch.Index -gt 0) {
    $inlineOriginal = $Lines[$MethodLineIndex].Substring(0, $methodMatch.Index)
    $inlineScrubbed = $methodLine.Substring(0, $methodMatch.Index)
    if (-not [string]::IsNullOrWhiteSpace($inlineScrubbed)) {
      $originalParts.Insert(0, $inlineOriginal)
      $scrubbedParts.Insert(0, $inlineScrubbed)
    }
  }

  $lineIndex = $MethodLineIndex - 1
  while ($lineIndex -ge 0) {
    $above = $Lines[$lineIndex]
    $aboveScrubbed = $ScrubbedLines[$lineIndex]
    if ([string]::IsNullOrWhiteSpace($aboveScrubbed)) {
      $lineIndex--
      continue
    }
    if ($above -match '^\s*//') {
      $lineIndex--
      continue
    }

    $joinedOriginal = $above
    $joinedScrubbed = $aboveScrubbed
    $top = $lineIndex
    $openBr = ([regex]::Matches($joinedScrubbed, '\[')).Count - ([regex]::Matches($joinedScrubbed, '\]')).Count
    $openPr = ([regex]::Matches($joinedScrubbed, '\(')).Count - ([regex]::Matches($joinedScrubbed, '\)')).Count
    while (($openBr -ne 0 -or $openPr -ne 0) -and $top -gt 0) {
      $top--
      $joinedOriginal = "$($Lines[$top])`n$joinedOriginal"
      $joinedScrubbed = "$($ScrubbedLines[$top])`n$joinedScrubbed"
      $openBr = ([regex]::Matches($joinedScrubbed, '\[')).Count - ([regex]::Matches($joinedScrubbed, '\]')).Count
      $openPr = ([regex]::Matches($joinedScrubbed, '\(')).Count - ([regex]::Matches($joinedScrubbed, '\)')).Count
    }

    $flat = ($joinedScrubbed -replace "\r?\n",' ').Trim()
    if (-not $anyAttributeLinePattern.IsMatch($flat)) { break }

    $originalParts.Insert(0, $joinedOriginal)
    $scrubbedParts.Insert(0, $joinedScrubbed)
    $lineIndex = $top - 1
  }

  return [pscustomobject]@{
    OriginalText = [string]::Join("`n", $originalParts)
    ScrubbedText = [string]::Join("`n", $scrubbedParts)
  }
}

function Get-TestCaseSourceNamesFromAttributeBlock {
  Param(
    [string]$AttributeText
  )

  $sourceNames = @()
  $matches = [regex]::Matches(
    $AttributeText,
    '(?ms)\bTestCaseSource(?:Attribute)?\s*\(\s*(?:nameof\s*\(\s*(?<methodName>\w+)\s*\)|"(?<stringName>[^"]+)")'
  )
  foreach ($match in $matches) {
    $sourceName = $match.Groups['methodName'].Value
    if ([string]::IsNullOrWhiteSpace($sourceName)) {
      $sourceName = $match.Groups['stringName'].Value
    }
    if (-not [string]::IsNullOrWhiteSpace($sourceName)) {
      $sourceNames += $sourceName
    }
  }
  return $sourceNames
}

$violations = @()
# Non-blocking guidance (currently UNH009 asset-churn): reported every run but
# does NOT fail the build, so pre-existing churn is surfaced for cleanup without
# forcing a risky mass-conversion of fixtures to a batched base.
$advisories = @()

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
  foreach ($root in $testRoots) {
    if (-not (Test-Path $root)) { continue }
    $filesToScan += Get-ChildItem -Recurse -Include *.cs -Path $root | Select-Object -ExpandProperty FullName
  }
}

$filesToScan = $filesToScan | Sort-Object -Unique

foreach ($file in $filesToScan) {
  if ($file -like '*.meta') { continue }
  $rel = Get-RelativePath $file
  if (Is-AllowlistedFile $rel) { continue }

  # Force array semantics: Get-Content returns $null for empty files and a bare
  # [string] for single-line files; under Set-StrictMode either of those will
  # throw when we access .Count on non-array values. Wrapping with @(...) is the
  # idiomatic fix and is a no-op for arrays.
  $content = @(Get-Content $file)
  $text = $content -join "`n"

  if ($FixNullChecks) {
    $fixResult = Fix-UnityNullAssertions -Text $text
    if ($fixResult.Modified) {
      Write-Info "Auto-fixed Unity null assertions in $rel"
      [System.IO.File]::WriteAllText($file, $fixResult.Text)
      $text = $fixResult.Text
      $content = $fixResult.Text -split "`n"
    }
  }

  # Scrubbed view: string literals and comments (including multi-line block
  # comments via comment-stripping.ps1) replaced with whitespace, preserving
  # line count and column offsets. Used by per-line pattern checks so that
  # e.g. `Object.Destroy(x)` inside a `/* ... */` comment or a string does
  # not trip UNH001. Falls back to raw content on unexpected length drift.
  # NOTE: an if/else expression that produces an @(...)-wrapped array
  # unwraps it to a bare string when the array has length 1 under certain
  # pipeline conditions, which then fails the `.Count` access under
  # StrictMode. Build the array imperatively and re-wrap to guarantee
  # array semantics for downstream `.Count` / indexing access.
  $scrubbedText = Remove-CsStringLiteralsAndLineComments $text
  if ($null -eq $scrubbedText) {
    $scrubbedContent = $content
  } else {
    $scrubbedContent = @($scrubbedText -split "`n")
  }
  $scrubbedContent = @($scrubbedContent)
  if ($scrubbedContent.Count -ne $content.Count) { $scrubbedContent = $content }

  # Check destroy calls; allow if argument var was tracked earlier in file
  $lineIndex = 0
  foreach ($line in $content) {
    $lineIndex++
    # Skip lines with UNH-SUPPRESS comment
    if ($line -match 'UNH-SUPPRESS') { continue }
    $scrubbedLine = $scrubbedContent[$lineIndex - 1]
    if ($destroyPattern.IsMatch($scrubbedLine)) {
      $m = $destroyPattern.Match($scrubbedLine)
      $arg = ($m.Groups['arg'].Value).Trim()
      # Extract variable token before any commas or closing paren
      $varName = $arg -replace ',.*','' -replace '\)',''
      $allowed = $false
      if (-not [string]::IsNullOrWhiteSpace($varName)) {
        # Search up to 100 lines above for Track(varName)
        $searchStart = [Math]::Max(0, $lineIndex - 100)
        for ($i = $searchStart; $i -lt $lineIndex; $i++) {
          if ($content[$i] -match "Track\s*\(\s*$varName\b") { $allowed = $true; break }
        }
      }
      if (-not $allowed) {
        $violations += (@{
          Path=$rel; Line=$lineIndex; Message="UNH001: Avoid direct destroy in tests; track object and let teardown clean up (or add // UNH-SUPPRESS)"
        })
      }
    }
  }

  # Check untracked new allocations via assignment (var = new Type(...))
  $assignMatches = $createAssignObjectPattern.Matches($text)
  foreach ($am in $assignMatches) {
    $var = $am.Groups['var'].Value
    if ([string]::IsNullOrWhiteSpace($var)) { continue }
    # Find the index of this match in terms of line
    $prefix = $text.Substring(0, $am.Index)
    $lineNo = ($prefix -split "`n").Length
    # Skip if line has UNH-SUPPRESS
    if ($content[$lineNo-1] -match 'UNH-SUPPRESS') { continue }
    # Look ahead 10 lines for Track(var)
    $endLine = [Math]::Min($content.Count, $lineNo + 10)
    $found = $false
    for ($j = $lineNo; $j -le $endLine; $j++) {
      if ($content[$j-1] -match "Track\s*\(\s*$var\b") { $found = $true; break }
    }
    if (-not $found) {
      $violations += (@{
        Path=$rel; Line=$lineNo; Message="UNH002: Unity object allocation should be tracked: add Track($var)"
      })
    }
  }

  # Check inline Track(new ...) OK; but find bare inline new ... in args without Track
  if ($text -match '\bnew\s+(GameObject|Texture2D|Material|Mesh|Camera)\s*\(') {
    # If Track(new ...) not present at all, flag a generic warning at file level
    if (-not $createInlineTrackPattern.IsMatch($text)) {
      # locate first occurrence for line number
      $m = [regex]::Match($text, '\bnew\s+(GameObject|Texture2D|Material|Mesh|Camera)\s*\(')
      $lineNo = (($text.Substring(0, $m.Index)) -split "`n").Length
      # Skip if line has UNH-SUPPRESS
      if ($content[$lineNo-1] -match 'UNH-SUPPRESS') { continue }
      $violations += (@{
        Path=$rel; Line=$lineNo; Message="UNH002: Inline Unity object creation should be passed to Track(new …)"
      })
    }
  }

  # Check ScriptableObject.CreateInstance<T>() assigned, ensure tracked
  $soMatches = $createSoAssignPattern.Matches($text)
  foreach ($sm in $soMatches) {
    $var = $sm.Groups['var'].Value
    if ([string]::IsNullOrWhiteSpace($var)) { continue }
    $prefix = $text.Substring(0, $sm.Index)
    $lineNo = ($prefix -split "`n").Length
    # Skip if line or next few lines have UNH-SUPPRESS (multi-line statements)
    $checkEnd = [Math]::Min($content.Count, $lineNo + 2)
    $suppressed = $false
    for ($s = $lineNo - 1; $s -lt $checkEnd; $s++) {
      if ($content[$s] -match 'UNH-SUPPRESS') { $suppressed = $true; break }
    }
    if ($suppressed) { continue }
    $found = $false
    $endLine = [Math]::Min($content.Count, $lineNo + 10)
    for ($j = $lineNo; $j -le $endLine; $j++) {
      if ($content[$j-1] -match "Track\s*\(\s*$var\b") { $found = $true; break }
    }
    if (-not $found) {
      $violations += (@{
        Path=$rel; Line=$lineNo; Message="UNH002: ScriptableObject instance should be tracked: add Track($var)"
      })
    }
  }

  # UNH004: Check for underscores in TestName values
  $lineIndex = 0
  foreach ($line in $content) {
    $lineIndex++
    if ($line -match 'UNH-SUPPRESS') { continue }
    if ($testNameUnderscorePattern.IsMatch($line)) {
      $violations += (@{
        Path=$rel; Line=$lineIndex; Message="UNH004: TestName contains underscore. Use PascalCase or dot notation (e.g., 'Input.Null.ReturnsFalse')"
      })
    }
  }

  # UNH004: Check for underscores in SetName() calls
  $lineIndex = 0
  foreach ($line in $content) {
    $lineIndex++
    if ($line -match 'UNH-SUPPRESS') { continue }
    if ($setNameUnderscorePattern.IsMatch($line)) {
      $violations += (@{
        Path=$rel; Line=$lineIndex; Message="UNH004: SetName() contains underscore. Use PascalCase or dot notation (e.g., 'Input.Null.ReturnsFalse')"
      })
    }
  }

  # UNH004: Check for underscores in TestCaseSource method names
  $lineIndex = 0
  foreach ($line in $content) {
    $lineIndex++
    if ($line -match 'UNH-SUPPRESS') { continue }
    $sourceMatch = $testCaseSourcePattern.Match($line)
    if ($sourceMatch.Success) {
      $methodName = $sourceMatch.Groups['methodName'].Value
      if ([string]::IsNullOrWhiteSpace($methodName)) {
        $methodName = $sourceMatch.Groups['stringName'].Value
      }
      if (-not [string]::IsNullOrWhiteSpace($methodName) -and $methodName -match '_') {
        $violations += (@{
          Path=$rel; Line=$lineIndex; Message="UNH004: TestCaseSource method '$methodName' contains underscore. Use PascalCase (e.g., 'EdgeCaseTestData')"
        })
      }
    }
  }

  # UNH004: Check for underscores in method names decorated with
  # [Test], [TestCase(...)], [TestCaseSource(...)], or [UnityTest]. The Unity
  # runtime test TestNamingConventionTests.TestMethodNamesDoNotContainUnderscores
  # catches this at test time, but we also enforce it here so the pre-commit
  # hook blocks the violation before it ever reaches CI.
  #
  # Walker strategy: walk upward from the signature line over attribute blocks.
  # Multi-line attribute arguments (e.g. "[TestCase(\n    1,\n    2)]") are
  # handled by bracket-balance joining: when we see a line that doesn't close
  # its own brackets, we keep accumulating lines upward until the '[' and '(' on
  # that block are all matched. That reconstructed logical line is then tested
  # against the test-attribute and any-attribute patterns. Trailing "// reason"
  # comments on attribute lines and standalone "// explanation" comment lines
  # between attributes and the signature are tolerated.
  for ($i = 0; $i -lt $content.Count; $i++) {
    $line = $content[$i]
    $declMatch = $methodDeclPattern.Match($line)
    if (-not $declMatch.Success) { continue }
    $methodName = $declMatch.Groups['name'].Value
    if ($methodName -notmatch '_') { continue }
    # Avoid false positives on keywords that the regex's modifier-greedy match
    # could theoretically align to (belt-and-braces; the modifier list is fixed).
    if ($methodName -in @('if','for','foreach','while','switch','using','return','new','base','this')) { continue }

    $isTest = $false
    $attrLine = -1
    # For inline/prefix matching, scrub the signature line so that "[" or "("
    # inside string/char literals can't fool the test-attribute regex.
    $lineScrubbed = Remove-CsStringLiteralsAndLineComments $line
    # Bound the scan to the portion of the signature line BEFORE the method
    # declaration's opening '('. $methodDeclPattern matches up to and
    # including that opening paren, so taking Substring(0, Index + Length)
    # yields the attribute/modifier/return-type/name prefix and EXCLUDES any
    # parameter-level attributes (e.g. "void Foo(int x, [Test] int y)" has a
    # C# parameter attribute that is NOT a test-declaration attribute).
    # Without this bound, $inlineTestAttributeAnywherePattern would raise a
    # false UNH004 on such methods.
    $lineScrubbedPrefix = $lineScrubbed.Substring(0, $declMatch.Index + $declMatch.Length)
    # Same-line prefix: "[Test] public void Foo_Bar() { }" (anchored) OR a
    # stacked-inline form where the test attribute is not the first bracket,
    # e.g. "[Category(\"Fast\")][Test] public void Foo()". The scrubber has
    # already blanked string/char literals, so a non-anchored search cannot
    # be tricked by "[Test]" appearing inside a string. The anchored form is
    # retained alongside the anywhere form for clarity/robustness: it asserts
    # '^\s*\[' at the prefix start, which matters if the bounded prefix is
    # ever extended to start mid-line.
    if ($inlineTestAttributePattern.IsMatch($lineScrubbedPrefix) -or $inlineTestAttributeAnywherePattern.IsMatch($lineScrubbedPrefix)) {
      $isTest = $true
      $attrLine = $i
    } else {
      # Walk upward over attribute-only blocks (stacked attributes are allowed
      # in any order) until we find a test attribute, a non-attribute line,
      # or the top of the file. Attribute blocks may span multiple physical
      # lines when their arguments wrap; we accumulate lines upward until the
      # bracket balance of the joined block is zero.
      #
      # Bracket/comment analysis is done on a SCRUBBED copy of each line
      # (strings/chars blanked, comments stripped) so that "[", "]", "(", ")",
      # and "//" inside literals can't bypass the walker. Reported line
      # numbers still reference the ORIGINAL source lines.
      $j = $i - 1
      while ($j -ge 0) {
        $above = $content[$j]
        $aboveScrubbed = Remove-CsStringLiteralsAndLineComments $above
        if ([string]::IsNullOrWhiteSpace($aboveScrubbed)) { $j--; continue }
        # Single-line comments (both "//" and "///") between attributes and
        # the signature are allowed. After scrubbing, pure-comment lines
        # become blank, so this branch mainly catches any residual whitespace
        # check above; keep it for robustness against lines beginning with "//".
        if ($above -match '^\s*//') { $j--; continue }

        # If this line already balances its own brackets, treat it as a single
        # logical line. Otherwise walk upward accumulating until balance hits 0.
        $joinedScrubbed = $aboveScrubbed
        $top = $j
        $openBr = ([regex]::Matches($joinedScrubbed, '\[')).Count - ([regex]::Matches($joinedScrubbed, '\]')).Count
        $openPr = ([regex]::Matches($joinedScrubbed, '\(')).Count - ([regex]::Matches($joinedScrubbed, '\)')).Count
        while (($openBr -ne 0 -or $openPr -ne 0) -and $top -gt 0) {
          $top--
          $prevScrubbed = Remove-CsStringLiteralsAndLineComments $content[$top]
          $joinedScrubbed = "$prevScrubbed`n$joinedScrubbed"
          $openBr = ([regex]::Matches($joinedScrubbed, '\[')).Count - ([regex]::Matches($joinedScrubbed, '\]')).Count
          $openPr = ([regex]::Matches($joinedScrubbed, '\(')).Count - ([regex]::Matches($joinedScrubbed, '\)')).Count
        }
        # Collapse to a single-line string for the regex match. The scrubbed
        # text has already had //-comments stripped, so no extra strip pass.
        $flat = ($joinedScrubbed -replace "\r?\n",' ').Trim()

        # Match the test-attribute anchored pattern OR the non-anchored
        # "anywhere" variant so that stacked attribute blocks like
        # "[Category(\"Fast\")][Test]" on a line ABOVE the signature are
        # detected when [Test] is not first. Using the anywhere pattern is
        # safe here because $flat is attribute-only reconstructed text — no
        # method parameter list is included — so the parameter-attribute
        # false-positive concern that bounds the same-line check does NOT
        # apply to the walker.
        if ($testAttributeLinePattern.IsMatch($flat) -or $inlineTestAttributeAnywherePattern.IsMatch($flat)) {
          $isTest = $true
          $attrLine = $top
          break
        }
        if ($anyAttributeLinePattern.IsMatch($flat)) {
          $j = $top - 1
          continue
        }
        # Anything else terminates the attribute block.
        break
      }
    }
    if (-not $isTest) { continue }
    # UNH-SUPPRESS honored on either the method signature line or any line of
    # the attribute block (including multi-line continuations and comments).
    $suppressed = ($line -match 'UNH-SUPPRESS')
    if (-not $suppressed -and $attrLine -ge 0) {
      for ($k = $attrLine; $k -le $i; $k++) {
        if ($content[$k] -match 'UNH-SUPPRESS') { $suppressed = $true; break }
      }
    }
    if ($suppressed) { continue }
    $violations += (@{
      Path=$rel; Line=($i + 1); Message="UNH004: Test method name '$methodName' contains underscore. Use PascalCase."
    })
  }

  # UNH006: Unity coroutine tests with TestCaseSource must use
  # TestCaseData.Returns(null), otherwise NUnit reports:
  # "Method has non-void return value, but no result is expected."
  for ($lineIndex = 0; $lineIndex -lt $scrubbedContent.Count; $lineIndex++) {
    if ($content[$lineIndex] -match 'UNH-SUPPRESS') { continue }
    if (-not $coroutineMethodPattern.IsMatch($scrubbedContent[$lineIndex])) { continue }

    $attributeBlock = Get-TestAttributeBlockForMethod -Lines $content -ScrubbedLines $scrubbedContent -MethodLineIndex $lineIndex
    if (-not $unityTestAttributePattern.IsMatch($attributeBlock.ScrubbedText)) { continue }

    $sourceNames = Get-TestCaseSourceNamesFromAttributeBlock -AttributeText $attributeBlock.OriginalText
    foreach ($sourceName in $sourceNames) {
      $sourceBody = Get-TestCaseSourceBody -Lines $content -ScrubbedLines $scrubbedContent -SourceName $sourceName
      if ($null -eq $sourceBody) { continue }

      $chains = Get-TestCaseDataChains -Text $sourceBody.ScrubbedText -StartLine $sourceBody.StartLine
      foreach ($chain in $chains) {
        if ($testCaseDataReturnsNullPattern.IsMatch($chain.Text)) { continue }

        $violations += (@{
          Path=$rel; Line=$chain.Line; Message="UNH006: TestCaseData used by [UnityTest] coroutine source '$sourceName' must call .Returns(null)."
        })
      }
    }
  }

  # UNH005: Check for Assert.IsNull (should use Assert.IsTrue(x == null) for Unity null checks)
  $lineIndex = 0
  foreach ($line in $content) {
    $lineIndex++
    if ($line -match 'UNH-SUPPRESS') { continue }
    if ($assertIsNullPattern.IsMatch($line)) {
      $violations += (@{
        Path=$rel; Line=$lineIndex; Message="UNH005: Use Assert.IsTrue(x == null) instead of Assert.IsNull(x) for Unity object null checks"
      })
    }
  }

  # UNH005: Check for Assert.IsNotNull (should use Assert.IsTrue(x != null) for Unity null checks)
  $lineIndex = 0
  foreach ($line in $content) {
    $lineIndex++
    if ($line -match 'UNH-SUPPRESS') { continue }
    if ($assertIsNotNullPattern.IsMatch($line)) {
      $violations += (@{
        Path=$rel; Line=$lineIndex; Message="UNH005: Use Assert.IsTrue(x != null) instead of Assert.IsNotNull(x) for Unity object null checks"
      })
    }
  }

  # Enforce CommonTestBase inheritance only if file creates Unity objects and is under Runtime/ or Editor/
  $createsUnity = ($assignMatches.Count -gt 0) -or ($text -match '\bnew\s+(GameObject|Texture2D|Material|Mesh|Camera)\s*\(') -or ($soMatches.Count -gt 0)
  if ($createsUnity) {
    # Check for direct or indirect inheritance (CommonTestBase or any base that inherits it)
    $usesBase = ($text -match ':\s*(CommonTestBase|AttributeTagsTestBase|TagsTestBase|EditorCommonTestBase|SpriteSheetExtractorTestBase|BatchedEditorTestBase|DetectAssetChangeTestBase)')
    # Check for file-level UNH-SUPPRESS UNH003 comment
    $hasSuppress = ($text -match 'UNH-SUPPRESS.*UNH003|UNH-SUPPRESS:\s*Complex|UNH-SUPPRESS:\s*This IS the CommonTestBase')
    if (-not $usesBase -and -not $hasSuppress) {
      # Only enforce for test classes; skip helper-only files
      if ($text -match '\bnamespace\s+WallstopStudios') {
        $violations += (@{
          Path=$rel; Line=1; Message="UNH003: Test classes creating Unity objects should inherit CommonTestBase (Editor or Runtime variant)"
        })
      }
    }
  }

  # ---- Test performance budgets (UNH007 / UNH008 / UNH009) ----
  # A fixture is "perf-tagged" when it declares the Performance or Stress
  # category; those fixtures are EXCLUDED from the fast CI matrix
  # (UH_UNITY_TEST_CATEGORY="!Performance;!Stress") and run only in the
  # dedicated benchmark job, so heavy work is allowed there.
  # Match both the short `[Category("Performance")]` and fully-qualified
  # `[NUnit.Framework.Category("Performance")]` / `...CategoryAttribute(...)` forms.
  # Use a COMMENT-MASKED (but NOT string-blanked) view: masking comments stops a
  # commented-out `// [Category("Performance")]` from satisfying the rule, while
  # preserving string contents keeps the "Performance"/"Stress" category name
  # visible (the full $scrubbedText would blank it).
  $categoryRegex = '\[\s*(?:NUnit\.Framework\.)?Category(?:Attribute)?\(\s*"(Performance|Stress)"\s*\)\s*\]'
  $commentMaskedText = [string]::Join("`n", (Get-CommentMaskedLines -Lines ($text -split "`n", -1) -Language 'csharp'))
  $perfCategory = [regex]::IsMatch($commentMaskedText, $categoryRegex)

  # UNH008: a fixture that LOOKS like a benchmark (lives under a Performance/
  # folder or is named *PerformanceTests / *BenchmarkTests) MUST carry the
  # Performance or Stress category, otherwise the fast matrix would run it.
  $looksPerf = ($rel -match '(^|/)Performance/') -or [regex]::IsMatch($scrubbedText, '\bclass\s+\w*(Performance|Benchmark)\w*Tests\b')
  $isTestFile = [regex]::IsMatch($scrubbedText, '\[\s*Test\b|\[\s*TestFixture\b|\[\s*UnityTest\b')
  if ($looksPerf -and $isTestFile -and -not $perfCategory -and ($text -notmatch 'UNH-SUPPRESS.*UNH008')) {
    $violations += (@{
      Path=$rel; Line=1; Message='UNH008: Performance/benchmark fixture must declare [Category("Performance")] or [Category("Stress")] so the main CI matrix (which runs !Performance;!Stress) excludes it'
    })
  }

  # UNH009 (ADVISORY, non-blocking): per-test AssetDatabase.Refresh()/
  # SaveAndReimport() churns the asset importer on every test. Prefer
  # BatchedEditorTestBase (batches and defers a single refresh to
  # OneTimeTearDown). Reported as guidance only — converting an existing
  # fixture to a batched base can change timing-dependent behaviour and must be
  # validated in the editor, so this never fails the build. Infra/base files
  # (Tests/Core/**, *TestBase.cs) legitimately manage refreshes and are skipped.
  $isInfra = ($rel -match '(^|/)Tests/Core/') -or ($rel -match 'TestBase\.cs$')
  $batchedBase = ($text -match ':\s*(BatchedEditorTestBase|SpriteSheetExtractorTestBase|DetectAssetChangeTestBase)\b')
  if (-not $batchedBase -and -not $isInfra) {
    $lineIndex = 0
    foreach ($line in $content) {
      $lineIndex++
      if ($line -match 'UNH-SUPPRESS') { continue }
      $scrubbedLine = $scrubbedContent[$lineIndex - 1]
      if ([regex]::IsMatch($scrubbedLine, 'AssetDatabase\.Refresh\s*\(|\.SaveAndReimport\s*\(')) {
        $advisories += (@{
          Path=$rel; Line=$lineIndex; Message='UNH009: per-test AssetDatabase.Refresh()/SaveAndReimport() churns imports; prefer BatchedEditorTestBase (advisory)'
        })
      }
    }
  }

  # UNH010 (ADVISORY, non-blocking): real-time waits block the serial test clock.
  # A literal `new WaitForSeconds(x)` / `Task.Delay(n)` / `Thread.Sleep(n)` burns
  # wall-clock on EVERY run; prefer frame-stepping (`yield return null`), a
  # deterministic completion signal (TaskCompletionSource / ManualResetEventSlim),
  # or an injectable clock. Reported as guidance (legitimate frame-timing exists and
  # conversion must be validated in the editor); the per-fixture wall-clock budget
  # (scripts/unity/report-slow-tests.ps1 -FailOverBudget) is the HARD gate.
  # `Task.Delay(n, ct)` is intentionally NOT matched (cancellation-test fodder, not a
  # blocking wait). Infra/base files (Tests/Core/**, *TestBase.cs) are skipped.
  if ($isTestFile -and -not $perfCategory -and -not $isInfra) {
    # Only LITERAL durations are matched. Leading-dot literals (`.5f`), const/variable
    # args (`WaitForSeconds(delay)`), and expression args (`Task.Delay(1000 * 5)`) are
    # intentionally out of scope - they are rarer and need human judgement, and this
    # rule is advisory (the wall-clock budget gate is the hard backstop).
    $waitRegex = 'new\s+WaitForSeconds(?:Realtime)?\s*\(\s*[0-9][0-9.]*f?\s*\)|\bThread\.Sleep\s*\(\s*[0-9]+\s*\)|\bTask\.Delay\s*\(\s*[0-9]+\s*\)'
    $lineIndex = 0
    foreach ($line in $content) {
      $lineIndex++
      if ($line -match 'UNH-SUPPRESS') { continue }
      $scrubbedLine = $scrubbedContent[$lineIndex - 1]
      $waitMatch = [regex]::Match($scrubbedLine, $waitRegex)
      if ($waitMatch.Success) {
        $advisories += (@{
          Path=$rel; Line=$lineIndex; Message="UNH010: real-time wait '$($waitMatch.Value.Trim())' blocks the serial test clock; prefer frame-stepping/deterministic completion/injectable clock (advisory)"
        })
      }
    }
  }

  # UNH011: editor-only references in PLAYER-compiled test code must be guarded by
  # `#if UNITY_EDITOR`. The Runtime test assemblies (Tests/Core, Tests/Runtime — every
  # tree EXCEPT Tests/Editor) compile into the standalone player; the editor-only
  # assemblies (UnityEditor.*, WallstopStudios.UnityHelpers.Editor) are stripped there,
  # so any unguarded reference is a CS0234 that aborts the WHOLE standalone leg before a
  # single test runs (no results.xml). This catches that class in <1s without a player
  # build. The standalone leg remains the ultimate backstop.
  #   A line is "editor-guarded" when enclosed by an `#if`/`#elif` whose condition
  # mentions UNITY_EDITOR (positively), or by the `#else` of an `#if !UNITY_EDITOR`.
  # Directive + token detection both run on the comment/string-SCRUBBED view, so a
  # `// uses UnityEditor` comment or an `InternalsVisibleTo("…Editor")` string literal
  # (both legal in player code) cannot trip the rule.
  if (($rel -notmatch '(^|/)Tests/Editor/') -and ($text -notmatch 'UNH-SUPPRESS.*UNH011')) {
    $editorStack = New-Object System.Collections.Generic.List[object]
    $lineIndex = 0
    foreach ($line in $content) {
      $lineIndex++
      $sl = $scrubbedContent[$lineIndex - 1]
      $t = $sl.Trim()
      if ($t -match '^#\s*if\s+(.+)$' -or $t -match '^#\s*elif\s+(.+)$') {
        $cond = $Matches[1]
        $negated = ($cond -cmatch '!\s*UNITY_EDITOR')
        $frame = @{ Active = (($cond -cmatch 'UNITY_EDITOR') -and -not $negated); Negated = $negated }
        if ($t -match '^#\s*elif' -and $editorStack.Count -gt 0) {
          $editorStack[$editorStack.Count - 1] = $frame
        } else {
          $editorStack.Add($frame) | Out-Null
        }
        continue
      }
      if ($t -match '^#\s*else\b') {
        if ($editorStack.Count -gt 0) {
          $editorStack[$editorStack.Count - 1] = @{ Active = $editorStack[$editorStack.Count - 1].Negated; Negated = $false }
        }
        continue
      }
      if ($t -match '^#\s*endif\b') {
        if ($editorStack.Count -gt 0) { $editorStack.RemoveAt($editorStack.Count - 1) }
        continue
      }
      $guarded = $false
      foreach ($frame in $editorStack) { if ($frame.Active) { $guarded = $true; break } }
      if ($guarded) { continue }
      if ($line -match 'UNH-SUPPRESS') { continue }
      if ($sl -cmatch '\bUnityEditor' -or $sl -cmatch 'WallstopStudios\.UnityHelpers\.Editor\b') {
        $violations += (@{
          Path=$rel; Line=$lineIndex; Message='UNH011: editor-only reference in player-compiled test code must be inside #if UNITY_EDITOR (else CS0234 aborts the standalone leg)'
        })
      }
    }
  }

  # UNH007: an enormous literal loop bound in a non-perf test belongs in a
  # Performance/Stress fixture (excluded from the fast suite) or should be
  # reduced. Const/field bounds (e.g. `< SampleCount`) are intentionally NOT
  # matched — only raw literals.
  if (-not $perfCategory) {
    $lineIndex = 0
    foreach ($line in $content) {
      $lineIndex++
      if ($line -match 'UNH-SUPPRESS') { continue }
      $scrubbedLine = $scrubbedContent[$lineIndex - 1]
      $loopMatch = [regex]::Match($scrubbedLine, '\bfor\s*\([^;]*;[^;]*<\s*=?\s*([0-9][0-9_]{2,})')
      if ($loopMatch.Success) {
        $boundText = $loopMatch.Groups[1].Value -replace '_', ''
        [long]$bound = 0
        if ([long]::TryParse($boundText, [ref]$bound) -and $bound -ge 50000) {
          $violations += (@{
            Path=$rel; Line=$lineIndex; Message="UNH007: loop of $bound iterations in a non-perf test; tag the fixture [Category(`"Stress`")]/[Category(`"Performance`")], reduce the count, or add // UNH-SUPPRESS"
          })
        }
      }
    }
  }
}

if ($advisories.Count -gt 0) {
  Write-Host "Test performance advisories (non-blocking): $($advisories.Count)" -ForegroundColor DarkYellow
  foreach ($a in $advisories) {
    Write-Host ("  {0}:{1}: {2}" -f $a.Path, $a.Line, $a.Message) -ForegroundColor DarkYellow
  }
}

if ($violations.Count -gt 0) {
  Write-Host "Test lifecycle lint failed:" -ForegroundColor Red
  foreach ($v in $violations) {
    Write-Host ("{0}:{1}: {2}" -f $v.Path, $v.Line, $v.Message) -ForegroundColor Yellow
  }
  exit 1
} else {
  Write-Info "No issues found in test code."
  exit 0
}

