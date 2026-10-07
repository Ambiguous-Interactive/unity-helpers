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

# Exact historical Unity field identifiers also form raw JsonUtility keys.
# Do not broaden this list to all serialized fields: each declaration is frozen.
$historicalSerializedFields = @(
  [pscustomobject]@{ Path = 'Editor/CustomDrawers/PendingValueWrapper.cs'; DeclaringType = 'WallstopStudios.UnityHelpers.Editor.CustomDrawers.PendingValueWrapper'; Name = 'boxedValue'; Attribute = 'SerializeReference'; RequiredSymbol = 'UNITY_EDITOR' }
  [pscustomobject]@{ Path = 'Runtime/Core/Helper/UnityMainThreadDispatcher.cs'; DeclaringType = 'WallstopStudios.UnityHelpers.Core.Helper.UnityMainThreadDispatcher'; Name = 'maxPendingActions'; Attribute = 'SerializeField'; RequiredSymbol = '' }
  [pscustomobject]@{ Path = 'Samples~/Logging - Tag Formatter/Scripts/LoggingDemoController.cs'; DeclaringType = 'Samples.UnityHelpers.Logging.LoggingDemoController'; Name = 'logOnStart'; Attribute = 'SerializeField'; RequiredSymbol = '' }
  [pscustomobject]@{ Path = 'Samples~/Logging - Tag Formatter/Scripts/LoggingDemoController.cs'; DeclaringType = 'Samples.UnityHelpers.Logging.LoggingDemoController'; Name = 'startMuted'; Attribute = 'SerializeField'; RequiredSymbol = '' }
  [pscustomobject]@{ Path = 'Samples~/Logging - Tag Formatter/Scripts/LoggingDemoController.cs'; DeclaringType = 'Samples.UnityHelpers.Logging.LoggingDemoController'; Name = 'pretty'; Attribute = 'SerializeField'; RequiredSymbol = '' }
  [pscustomobject]@{ Path = 'Samples~/Logging - Tag Formatter/Scripts/LoggingDemoController.cs'; DeclaringType = 'Samples.UnityHelpers.Logging.LoggingDemoController'; Name = 'npcCallsign'; Attribute = 'SerializeField'; RequiredSymbol = '' }
  [pscustomobject]@{ Path = 'Samples~/Logging - Tag Formatter/Scripts/LoggingDemoController.cs'; DeclaringType = 'Samples.UnityHelpers.Logging.LoggingDemoController'; Name = 'statusLabel'; Attribute = 'SerializeField'; RequiredSymbol = '' }
  [pscustomobject]@{ Path = 'Samples~/Logging - Tag Formatter/Scripts/LoggingDemoController.cs'; DeclaringType = 'Samples.UnityHelpers.Logging.LoggingDemoController'; Name = 'reportMessage'; Attribute = 'SerializeField'; RequiredSymbol = '' }
  [pscustomobject]@{ Path = 'Samples~/Logging - Tag Formatter/Scripts/LoggingDemoController.cs'; DeclaringType = 'Samples.UnityHelpers.Logging.LoggingDemoController'; Name = 'sectorRange'; Attribute = 'SerializeField'; RequiredSymbol = '' }
  [pscustomobject]@{ Path = 'Samples~/Random - PRNG/Scripts/RandomPrngDemo.cs'; DeclaringType = 'Samples.UnityHelpers.Random.Prng.RandomPrngDemo'; Name = 'seed'; Attribute = 'SerializeField'; RequiredSymbol = '' }
  [pscustomobject]@{ Path = 'Samples~/Spatial Structures - 2D and 3D/Scripts/HullUsageDemo.cs'; DeclaringType = 'Samples.UnityHelpers.SpatialStructures.HullUsageDemo'; Name = 'gridlessBounds'; Attribute = 'SerializeField'; RequiredSymbol = '' }
  [pscustomobject]@{ Path = 'Samples~/Spatial Structures - 2D and 3D/Scripts/HullUsageDemo.cs'; DeclaringType = 'Samples.UnityHelpers.SpatialStructures.HullUsageDemo'; Name = 'gridlessEdgeSamplesPerSide'; Attribute = 'SerializeField'; RequiredSymbol = '' }
  [pscustomobject]@{ Path = 'Samples~/Spatial Structures - 2D and 3D/Scripts/HullUsageDemo.cs'; DeclaringType = 'Samples.UnityHelpers.SpatialStructures.HullUsageDemo'; Name = 'grid'; Attribute = 'SerializeField'; RequiredSymbol = '' }
  [pscustomobject]@{ Path = 'Samples~/Spatial Structures - 2D and 3D/Scripts/HullUsageDemo.cs'; DeclaringType = 'Samples.UnityHelpers.SpatialStructures.HullUsageDemo'; Name = 'gridFootprint'; Attribute = 'SerializeField'; RequiredSymbol = '' }
  [pscustomobject]@{ Path = 'Samples~/Spatial Structures - 2D and 3D/Scripts/HullUsageDemo.cs'; DeclaringType = 'Samples.UnityHelpers.SpatialStructures.HullUsageDemo'; Name = 'gridHullNeighbors'; Attribute = 'SerializeField'; RequiredSymbol = '' }
  [pscustomobject]@{ Path = 'Samples~/Spatial Structures - 2D and 3D/Scripts/SpatialStructuresDemo.cs'; DeclaringType = 'Samples.UnityHelpers.SpatialStructures.SpatialStructuresDemo'; Name = 'pointCount'; Attribute = 'SerializeField'; RequiredSymbol = '' }
  [pscustomobject]@{ Path = 'Samples~/Spatial Structures - 2D and 3D/Scripts/SpatialStructuresDemo.cs'; DeclaringType = 'Samples.UnityHelpers.SpatialStructures.SpatialStructuresDemo'; Name = 'areaSize'; Attribute = 'SerializeField'; RequiredSymbol = '' }
  [pscustomobject]@{ Path = 'Samples~/Spatial Structures - 2D and 3D/Scripts/SpatialStructuresDemo.cs'; DeclaringType = 'Samples.UnityHelpers.SpatialStructures.SpatialStructuresDemo'; Name = 'queryRadius'; Attribute = 'SerializeField'; RequiredSymbol = '' }
  [pscustomobject]@{ Path = 'Samples~/UGUI - EnhancedImage/Scripts/EnhancedImageDemo.cs'; DeclaringType = 'Samples.UnityHelpers.UGUI.EnhancedImage.EnhancedImageDemo'; Name = 'materialTemplate'; Attribute = 'SerializeField'; RequiredSymbol = '' }
  [pscustomobject]@{ Path = 'Tests/Editor/TestTypes/PrivateCtorSetHost.cs'; DeclaringType = 'WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.PrivateCtorElement'; Name = 'magnitude'; Attribute = 'SerializeField'; RequiredSymbol = '' }
)

function Get-DeclaredTypeName($container) {
  $typeParts = [System.Collections.Generic.List[string]]::new()
  $ancestor = $container
  while ($null -ne $ancestor) {
    if ($ancestor -is [Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax]) {
      $typeName = $ancestor.Identifier.ValueText
      if ($null -ne $ancestor.TypeParameterList) { $typeName += '`' + $ancestor.TypeParameterList.Parameters.Count }
      $typeParts.Add($typeName)
    } elseif ($ancestor -is [Microsoft.CodeAnalysis.CSharp.Syntax.BaseNamespaceDeclarationSyntax]) {
      $typeParts.Add($ancestor.Name.ToString())
    }
    $ancestor = $ancestor.Parent
  }
  $orderedTypeParts = $typeParts.ToArray()
  [Array]::Reverse($orderedTypeParts)
  return $orderedTypeParts -join '.'
}

function Get-UnitySerializationAttributes($member) {
  foreach ($attributeList in $member.AttributeLists) {
    foreach ($attribute in $attributeList.Attributes) {
      $attributeName = $attribute.Name.ToString().Replace('global::', '')
      foreach ($known in @('SerializeField', 'SerializeReference')) {
        if ($attributeName -ceq $known -or $attributeName -ceq "UnityEngine.$known") { $known }
      }
    }
  }
}

# PowerShell ships Roslyn for Add-Type. Parse syntax only: no compilation,
# semantic reflection on package types, or additional SDK/project is needed.
function Get-PrivateInstanceFields {
  param([string]$Content, [string]$Path)
  $frozenInFile = @($historicalSerializedFields | Where-Object { $_.Path -ceq $Path })
  $invalidHistorical = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  $initial = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($Content)
  $symbols = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  foreach ($trivia in $initial.GetRoot().DescendantTrivia([Func[Microsoft.CodeAnalysis.SyntaxNode,bool]]$null, $true)) {
    if (-not $trivia.HasStructure) { continue }
    $directive = $trivia.GetStructure()
    if ($directive -isnot [Microsoft.CodeAnalysis.CSharp.Syntax.IfDirectiveTriviaSyntax] -and $directive -isnot [Microsoft.CodeAnalysis.CSharp.Syntax.ElifDirectiveTriviaSyntax]) { continue }
    foreach ($node in $directive.Condition.DescendantNodesAndSelf()) {
      if ($node -is [Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax]) { $null = $symbols.Add($node.Identifier.ValueText) }
    }
  }
  foreach ($frozen in $frozenInFile) {
    if ($frozen.RequiredSymbol) { $null = $symbols.Add($frozen.RequiredSymbol) }
  }
  $names = @($symbols | Sort-Object)
  # Bound parsing work, but fail closed rather than silently skipping any file.
  if ($names.Count -gt 12) { throw "Too many conditional symbols to exhaustively enforce naming in $Path ($($names.Count), maximum 12)." }
  $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  $options = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::Preview)
  for ($combination = 0; $combination -lt (1 -shl $names.Count); $combination++) {
    $defined = [System.Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $names.Count; $index++) {
      if (($combination -band (1 -shl $index)) -ne 0) { $defined.Add($names[$index]) }
    }
    # Repeated guards must see the same symbol assignment, including guards
    # around an opening/closing brace. Flattening #if alternatives is unsafe.
    $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($Content, $options.WithPreprocessorSymbols($defined))
    foreach ($diagnostic in $tree.GetDiagnostics()) {
      if ($diagnostic.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Error) {
        throw "Cannot enforce field naming in ${Path}: $diagnostic"
      }
    }
    # Visit declared member lists rather than every method/expression node.
    $declaredTypes = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $pending = [System.Collections.Generic.Stack[object]]::new()
    $pending.Push($tree.GetRoot())
    while ($pending.Count -gt 0) {
      $container = $pending.Pop()
      if ($frozenInFile.Count -ne 0 -and $container -is [Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax]) {
        $containerType = Get-DeclaredTypeName $container
        $null = $declaredTypes.Add($containerType)
        foreach ($frozen in @($frozenInFile | Where-Object { $_.DeclaringType -ceq $containerType })) {
          $valid = $false
          foreach ($declaration in $container.Members) {
            if ($declaration -isnot [Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax]) { continue }
            $tokens = @($declaration.Modifiers | ForEach-Object { $_.ValueText })
            if (@($tokens | Where-Object { $_ -in @('public', 'internal', 'protected', 'static', 'const', 'readonly') }).Count -ne 0) { continue }
            if (@($declaration.Declaration.Variables | Where-Object { $_.Identifier.ValueText -ceq $frozen.Name }).Count -eq 0) { continue }
            $attributeNames = @($declaration.AttributeLists | ForEach-Object { $_.Attributes } | ForEach-Object { $_.Name.ToString().Replace('global::', '') })
            if (@($attributeNames | Where-Object { $_ -cin @('NonSerialized', 'NonSerializedAttribute', 'System.NonSerialized', 'System.NonSerializedAttribute') }).Count -ne 0) { continue }
            if (@(Get-UnitySerializationAttributes $declaration) -ccontains $frozen.Attribute) { $valid = $true }
          }
          if (-not $valid -and $invalidHistorical.Add("$containerType.$($frozen.Name)")) {
            # Emit a contract failure once, even if another conditional assignment is valid.
            [pscustomobject]@{ Name = $frozen.Name; Line = 1; Declaration = ''; DeclaringType = $containerType; SerializationAttributes = @(); HistoricalViolation = $true }
          }
        }
      }
      foreach ($member in $container.Members) {
        if ($member -is [Microsoft.CodeAnalysis.CSharp.Syntax.BaseNamespaceDeclarationSyntax] -or $member -is [Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax]) {
          $pending.Push($member)
          continue
        }
        if ($member -isnot [Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax]) { continue }
        $modifiers = @($member.Modifiers | ForEach-Object { $_.ValueText })
        if ($modifiers -contains 'static' -or $modifiers -contains 'const') { continue }
        if ($modifiers -contains 'public' -or $modifiers -contains 'internal' -or $modifiers -contains 'protected' -or $container -is [Microsoft.CodeAnalysis.CSharp.Syntax.InterfaceDeclarationSyntax]) { continue }
        $declaringType = Get-DeclaredTypeName $container
        $serializationAttributes = @(Get-UnitySerializationAttributes $member)
        foreach ($variable in $member.Declaration.Variables) {
          $line = $tree.GetLineSpan($variable.Identifier.Span).StartLinePosition.Line + 1
          $name = $variable.Identifier.ValueText
          if ($seen.Add("${line}:${declaringType}:${name}:$($serializationAttributes -join ',')")) {
            [pscustomobject]@{ Name = $name; Line = $line; Declaration = $member.ToString(); DeclaringType = $declaringType; SerializationAttributes = $serializationAttributes; HistoricalViolation = $false }
          }
        }
      }
    }
    foreach ($frozen in $frozenInFile) {
      # Only the editor wrapper has a historical conditional availability boundary.
      $applicable = -not $frozen.RequiredSymbol -or $defined -ccontains $frozen.RequiredSymbol
      if ($applicable -and -not $declaredTypes.Contains($frozen.DeclaringType) -and $invalidHistorical.Add("$($frozen.DeclaringType).$($frozen.Name)")) {
        [pscustomobject]@{ Name = $frozen.Name; Line = 1; Declaration = ''; DeclaringType = $frozen.DeclaringType; SerializationAttributes = @(); HistoricalViolation = $true }
      }
    }
  }
}

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
$sourceRoots = @('Runtime', 'Editor', 'Tests', 'Generator~', 'Samples~', 'Styles', 'URP', 'Shaders')

# Directories to exclude
$excludeDirs = @('node_modules', '.git', 'obj', 'bin', 'Library', 'Temp')

function Test-ExcludedSourcePath([string]$Path) {
  $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
  $relative = [IO.Path]::GetRelativePath($repositoryRoot, $Path).Replace('\', '/')
  # Only this exact upstream tree is vendored; owned SevenZip test folders are scanned.
  if ($relative.StartsWith('Runtime/Utils/SevenZip/', [StringComparison]::Ordinal)) { return $true }
  foreach ($directory in $excludeDirs) {
    if ($relative.Split('/') -contains $directory) { return $true }
  }
  return $false
}

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

# Read git output exactly, including NUL-delimited paths and blob newlines.
# Native PowerShell line splitting would corrupt newline-bearing filenames.
function Invoke-GitRead([string[]]$Arguments) {
  $start = [Diagnostics.ProcessStartInfo]::new()
  $start.FileName = (Get-Command git -ErrorAction Stop).Source
  $start.WorkingDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
  $start.UseShellExecute = $false
  $start.RedirectStandardOutput = $true
  $start.RedirectStandardError = $true
  foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $start
  try {
    $null = $process.Start()
    $output = $process.StandardOutput.ReadToEndAsync()
    $errorOutput = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $text = $output.GetAwaiter().GetResult()
    $errorText = $errorOutput.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { throw "Cannot read staged C# input from git (exit $($process.ExitCode)): $errorText" }
    return $text
  } finally { $process.Dispose() }
}

# Get files to check
function Get-FilesToCheck {
  param([switch]$StagedOnly)

  if ($StagedOnly) {
    $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd([char[]]'\/')
    $gitRoot = [IO.Path]::GetFullPath((Invoke-GitRead @('rev-parse', '--show-toplevel')).TrimEnd([char[]]"`r`n")).TrimEnd([char[]]'\/')
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not [string]::Equals($repositoryRoot, $gitRoot, $comparison)) { throw 'Cannot read staged C# input: git repository root does not match the script root.' }
    $paths = Invoke-GitRead @('-c', 'core.quotepath=false', 'diff', '--cached', '--name-only', '-z', '--diff-filter=ACMR', '--', '*.cs')
    # Include removed/renamed historical paths too: their old indexed blob must exist.
    $frozenPaths = @($historicalSerializedFields.Path | Sort-Object -Unique)
    $affectedFrozen = Invoke-GitRead (@('diff', '--cached', '--name-only', '-z', '--no-renames', '--') + $frozenPaths)
    $paths += $affectedFrozen
    $files = @()
    $seenPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in $paths.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries)) {
      if (-not $seenPaths.Add($path)) { continue }
      $absolute = [IO.Path]::GetFullPath((Join-Path (Join-Path $PSScriptRoot '..') $path))
      if (-not (Test-ExcludedSourcePath $absolute)) {
        # A staged blob is a subject even when the worktree file was deleted.
        $files += [pscustomobject]@{ FullName = $absolute; StagedPath = $path }
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
      if (-not (Test-ExcludedSourcePath $path)) {
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
$privateFieldCount = 0

Write-Info "Scanning method names and all owned private instance fields..."

$files = Get-FilesToCheck -StagedOnly:$StagedOnly

# -StagedOnly matching nothing is ordinary; a repository-wide walk finding no C# files is
# the walk breaking, and reporting success for it is the failure mode #556 is about.
if (-not $StagedOnly -and @($files).Count -eq 0) {
  Write-Host '[lint-csharp-naming] ERROR: the repository-wide scan found no C# files, so a pass here would mean nothing.' -ForegroundColor Red
  exit 1
}

if (-not $StagedOnly) {
  foreach ($frozenPath in @($historicalSerializedFields.Path | Sort-Object -Unique)) {
    if (-not [IO.File]::Exists((Join-Path (Join-Path $PSScriptRoot '..') $frozenPath))) {
      throw "Historical serialized file is missing: $frozenPath"
    }
  }
}

foreach ($file in $files) {
  $filePath = $file.FullName
  $rel = Get-RelativePath $filePath

  # Skip .meta files
  if ($filePath -like '*.meta') { continue }

  $content = if ($StagedOnly) { Invoke-GitRead @('show', ":$($file.StagedPath)") } else { [System.IO.File]::ReadAllText($filePath) }
  if ([string]::IsNullOrWhiteSpace($content) -and $historicalSerializedFields.Path -cnotcontains [IO.Path]::GetRelativePath([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')), $filePath).Replace('\', '/')) { continue }

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

  $ownedRelative = [IO.Path]::GetRelativePath([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')), $filePath).Replace('\', '/')
  $fields = @(Get-PrivateInstanceFields -Content $content -Path $ownedRelative)
  $frozenFields = @($historicalSerializedFields | Where-Object { $_.Path -ceq $ownedRelative })
  foreach ($frozen in $frozenFields) {
    $matchesFrozen = @($fields | Where-Object { $_.Name -ceq $frozen.Name -and $_.DeclaringType -ceq $frozen.DeclaringType })
    if ($matchesFrozen.Count -eq 0 -or @($matchesFrozen | Where-Object { $_.SerializationAttributes -cnotcontains $frozen.Attribute }).Count -ne 0) {
      $violations += @{
        Path = $rel; FullPath = $filePath; Line = 1; Method = $null
        Message = "UNH004: Historical serialized field '$($frozen.DeclaringType).$($frozen.Name)' must remain a private instance field with [$($frozen.Attribute)] in $ownedRelative."
      }
    }
  }
  foreach ($field in $fields) {
    if ($field.HistoricalViolation) { continue }
    $privateFieldCount++
    $frozenMatch = @($frozenFields | Where-Object { $_.Name -ceq $field.Name -and $_.DeclaringType -ceq $field.DeclaringType -and $field.SerializationAttributes -ccontains $_.Attribute })
    if ($frozenMatch.Count -ne 0 -or $field.Name -cmatch '^_[a-z][A-Za-z0-9]*$') { continue }
    $violations += @{
      Path = $rel
      FullPath = $filePath
      Line = $field.Line
      Method = $null
      Message = "UNH004: Private instance field '$($field.Name)' must use _camelCase (including readonly and implicit-private fields)."
    }
  }

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
  $canFix = $Fix -and (-not $isCI) -and (-not $StagedOnly) -and @($violations | Where-Object { $null -eq $_.Method }).Count -eq 0

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
    Write-Host "Found $($violations.Count) naming violation(s)." -ForegroundColor Red
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
    Write-Host "Found $($violations.Count) naming violation(s)." -ForegroundColor Red
    Write-Host "Methods use PascalCase without underscores; private instance fields use _camelCase. Rename fields manually to preserve serialized names and API parameter names." -ForegroundColor Yellow
    exit 1
  }
} else {
  Write-Info "No naming convention violations found."
  if (-not $StagedOnly) {
    Write-Host "All C# method names follow naming conventions; $privateFieldCount private instance fields checked across $(@($files).Count) files." -ForegroundColor Green
  }
  exit 0
}
