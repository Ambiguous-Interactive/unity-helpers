Param(
  [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[lint-asmdef] $msg" -ForegroundColor Cyan }
}

function Write-WarningMsg($msg) {
  Write-Host "[lint-asmdef] WARNING: $msg" -ForegroundColor Yellow
}

function Write-ErrorMsg($msg) {
  Write-Host "[lint-asmdef] ERROR: $msg" -ForegroundColor Red
}

function Write-SuccessMsg($msg) {
  Write-Host "[lint-asmdef] $msg" -ForegroundColor Green
}

# Safe property read for ConvertFrom-Json objects: under `Set-StrictMode -Version Latest`,
# accessing a property the JSON does not contain THROWS and aborts the whole run. Returning a
# default instead keeps the linter robust against minimal-but-valid asmdefs (e.g. one with no
# 'rootNamespace' or 'references') and lets the intended "missing field" diagnostics actually fire.
function Get-JsonProp($obj, [string]$name, $default = $null) {
  if ($null -ne $obj -and $obj.PSObject.Properties[$name]) { return $obj.$name }
  return $default
}

function ConvertTo-RepoRelativePath {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path
  )

  $rootPath = [System.IO.Path]::GetFullPath($repoRoot)
  $childPath = [System.IO.Path]::GetFullPath($Path)

  return [System.IO.Path]::GetRelativePath($rootPath, $childPath).Replace('\', '/')
}

# Faithfully replicates UnityEditor.Scripting.ScriptCompilation.VersionRanges<T>.ParseExpression
# + ExpressionTypeFactory<T>.Create() (UnityCsReference). A versionDefines "expression" that this
# grammar rejects is SILENTLY ignored by Unity: the define is never applied, with no compile error.
# That is exactly how '[14.0.0,)' disabled REFLEX_14_0_OR_NEWER and broke every Unity CI leg.
# Unity has no NuGet/Maven-style '[X,)' open-ended range -- the canonical ">= X" form is the bare
# version 'X' (MinimumVersionInclusive), the same form already used for the *_PRESENT defines.
# An EMPTY/whitespace expression is valid: Unity defines the symbol unconditionally whenever the
# package is present (EditorCompilation.GetTargetAssemblyDefines short-circuits empty expressions).
# Char rules mirror Unity's SemVersion traits (the grammar used unless name == "Unity"): ASCII only,
# first char a digit, body chars [digit | ASCII letter | '.' | '-'] -- note '+' is NOT allowed.
# Scope: this validates expression STRUCTURE + character classes, not full numeric parseability
# (e.g. a malformed but well-shaped token like '1..2' is not caught; that degrades to a missing
# define, which the "Verify tests actually ran" CI gate then surfaces).
# Returns @{ Valid = [bool]; Reason = [string]; Suggestion = [string] }.
function Test-UnityVersionDefineExpression {
  param([Parameter(Mandatory)][AllowEmptyString()][AllowNull()][string]$Expression)

  # Empty/whitespace/missing => Unity always defines the symbol when the package is present.
  if ([string]::IsNullOrWhiteSpace($Expression)) {
    return [PSCustomObject]@{ Valid = $true; Reason = $null; Suggestion = $null }
  }

  $isVersionStart = { param([char]$c) $c -ge '0' -and $c -le '9' }
  $isVersionChar = {
    param([char]$c)
    ($c -ge '0' -and $c -le '9') -or ($c -ge 'a' -and $c -le 'z') -or ($c -ge 'A' -and $c -le 'Z') -or $c -eq '.' -or $c -eq '-'
  }
  $isVersionEnd = {
    param([char]$c)
    ($c -ge '0' -and $c -le '9') -or ($c -ge 'a' -and $c -le 'z') -or ($c -ge 'A' -and $c -le 'Z')
  }

  $nul = [char]0
  $leftSymbol = $nul
  $rightSymbol = $nul
  $hasSeparator = $Expression.Contains(',')
  $begin = 0
  $end = $Expression.Length - 1

  if (-not (& $isVersionStart $Expression[0])) {
    $leftSymbol = $Expression[0]
    if ($leftSymbol -ne '[' -and $leftSymbol -ne '(') {
      return [PSCustomObject]@{ Valid = $false; Reason = "invalid leading character '$leftSymbol'"; Suggestion = $null }
    }
    $begin++
  }

  $lastChar = $Expression[$Expression.Length - 1]
  if (-not (& $isVersionChar $lastChar)) {
    $rightSymbol = $lastChar
    if ($rightSymbol -ne ']' -and $rightSymbol -ne ')') {
      return [PSCustomObject]@{ Valid = $false; Reason = "invalid trailing character '$rightSymbol'"; Suggestion = $null }
    }
    $end--
  }

  $leftSet = ($leftSymbol -ne $nul)
  $rightSet = ($rightSymbol -ne $nul)
  if ($leftSet -ne $rightSet) {
    return [PSCustomObject]@{ Valid = $false; Reason = "unbalanced brackets (missing one of '[', '(', ']', ')')"; Suggestion = $null }
  }

  # Mirror PopVersionString: a token runs from the cursor up to the first ',' (or the end).
  function Read-VersionToken([string]$expr, [int]$b, [int]$e) {
    if ($b -gt $e) { return [PSCustomObject]@{ Text = ''; Next = $b } }
    $count = 0
    $i = $b
    while ($i -le $e) {
      if ($expr[$i] -eq ',') { $i++; break }
      $count++; $i++
    }
    return [PSCustomObject]@{ Text = $expr.Substring($b, $count); Next = $i }
  }

  $left = Read-VersionToken $Expression $begin $end
  $hasLeftVersion = -not [string]::IsNullOrEmpty($left.Text)
  $right = Read-VersionToken $Expression $left.Next $end
  $hasRightVersion = -not [string]::IsNullOrEmpty($right.Text)

  # Each present version token must look like a Unity SemVersion: digit first, alnum last,
  # only allowed chars between. This rejects e.g. '1.0.0+build' (Unity drops the define).
  foreach ($tok in @($left.Text, $right.Text)) {
    if ([string]::IsNullOrEmpty($tok)) { continue }
    if (-not (& $isVersionStart $tok[0])) {
      return [PSCustomObject]@{ Valid = $false; Reason = "version token '$tok' must start with a digit"; Suggestion = $null }
    }
    if (-not (& $isVersionEnd $tok[$tok.Length - 1])) {
      return [PSCustomObject]@{ Valid = $false; Reason = "version token '$tok' must end with a letter or digit"; Suggestion = $null }
    }
    foreach ($ch in $tok.ToCharArray()) {
      if (-not (& $isVersionChar $ch)) {
        return [PSCustomObject]@{ Valid = $false; Reason = "version token '$tok' contains invalid character '$ch'"; Suggestion = $null }
      }
    }
  }

  $L = if ($leftSet) { $leftSymbol } else { '_' }
  $R = if ($rightSet) { $rightSymbol } else { '_' }
  $key = '{0}|{1}|{2}|{3}|{4}' -f $L, $R, $hasSeparator, $hasLeftVersion, $hasRightVersion

  # The nine evaluator keys registered by ExpressionTypeFactory. Everything else (including the
  # '(L)' key Unity registers but maps to an always-false evaluator) leaves the define undefined.
  $validKeys = @(
    '_|_|False|True|False', # bare       x >= L   <-- canonical ">= X" form
    '(|)|True|True|False',  # (L,)       x > L
    '[|]|False|True|False', # [L]        x == L
    '(|]|True|False|True',  # (,R]       x <= R
    '(|]|True|True|True',   # (L,R]      L < x <= R
    '(|)|True|False|True',  # (,R)       x < R
    '[|]|True|True|True',   # [L,R]      L <= x <= R
    '(|)|True|True|True',   # (L,R)      L < x < R
    '[|)|True|True|True'    # [L,R)      L <= x < R
  )

  if ($validKeys -contains $key) {
    return [PSCustomObject]@{ Valid = $true; Reason = $null; Suggestion = $null }
  }

  $suggestion = $null
  if ($Expression -match '^\[\s*(\d[^,\]\)]*?)\s*,\s*[\)\]]\s*$') {
    $bare = $Matches[1].Trim()
    $suggestion = "use the bare version '$bare' (Unity reads a bare version as '>= $bare'); Unity has no '[X,)' open-ended range"
  }
  return [PSCustomObject]@{
    Valid      = $false
    Reason     = "not a valid Unity version-define expression (the define is silently never applied)"
    Suggestion = $suggestion
  }
}

# Directories to scan for asmdef files
$sourceRoots = @('Runtime', 'Editor', 'Tests', 'Samples~', 'Styles', 'URP', 'Shaders')

# Directories to exclude
$excludeDirs = @('node_modules', '.git', 'obj', 'bin', 'Library', 'Temp')

# Well-known Unity assembly references that don't have .asmdef files in the repo
$unityBuiltInAssemblies = @(
  'UnityEngine',
  'UnityEngine.UI',
  'UnityEngine.TestRunner',
  'UnityEditor',
  'UnityEditor.UI',
  'UnityEditor.TestRunner',
  'Unity.TextMeshPro',
  'Unity.TextMeshPro.Editor',
  'Unity.InputSystem',
  'Unity.InputSystem.Editor',
  'Unity.Addressables',
  'Unity.Addressables.Editor',
  'Unity.ResourceManager',
  'Unity.Burst',
  'Unity.Burst.Editor',
  'Unity.Collections',
  'Unity.Jobs',
  'Unity.Mathematics',
  'Unity.Mathematics.Editor',
  'Unity.RenderPipelines.Core.Runtime',
  'Unity.RenderPipelines.Core.Editor',
  'Unity.RenderPipelines.Universal.Runtime',
  'Unity.RenderPipelines.Universal.Editor',
  'Unity.RenderPipelines.HighDefinition.Runtime',
  'Unity.RenderPipelines.HighDefinition.Editor',
  'Unity.Netcode.Runtime',
  'Unity.Netcode.Editor',
  'Unity.Netcode.Components',
  'Unity.Services.Core',
  'Unity.Services.Authentication',
  'Unity.VisualScripting.Core',
  'Unity.VisualScripting.Flow',
  'Unity.Localization',
  'Unity.Localization.Editor',
  'Unity.2D.Animation.Runtime',
  'Unity.2D.Animation.Editor',
  'Unity.2D.SpriteShape.Runtime',
  'Unity.2D.SpriteShape.Editor',
  'Unity.2D.Tilemap.Extras',
  'Unity.Timeline',
  'Unity.Timeline.Editor',
  'Unity.Cinemachine',
  'Unity.Cinemachine.Editor',
  'Unity.Entities',
  'Unity.Entities.Editor',
  'Unity.Transforms',
  'Unity.Physics',
  'Unity.Rendering.Hybrid',
  'Unity.ProBuilder',
  'Unity.ProBuilder.Editor',
  'Unity.Polybrush',
  'Unity.Polybrush.Editor',
  'Unity.Recorder',
  'Unity.Recorder.Editor'
)

# Well-known third-party packages (installed via Package Manager or Asset Store)
$thirdPartyAssemblies = @(
  # Dependency Injection frameworks
  'Zenject',
  'Zenject-usage',
  'Zenject.Editor',
  'VContainer',
  'VContainer.Unity',
  'VContainer.Editor',
  'Reflex',
  'Reflex.Editor',
  # Odin Inspector
  'Sirenix.OdinInspector.Attributes',
  'Sirenix.OdinInspector.Editor',
  'Sirenix.Serialization',
  'Sirenix.Serialization.Config',
  'Sirenix.Utilities',
  'Sirenix.Utilities.Editor',
  # UniTask
  'UniTask',
  'UniTask.Linq',
  'UniTask.Addressables',
  'UniTask.DOTween',
  'UniTask.TextMeshPro',
  # DOTween
  'DOTween.Modules',
  'DG.Tweening',
  # R3 (Reactive Extensions)
  'R3.Unity',
  'R3',
  # UniRx
  'UniRx',
  'UniRx.Async',
  # Newtonsoft Json
  'Newtonsoft.Json',
  'Unity.Nuget.Newtonsoft-Json',
  # MessagePack
  'MessagePack',
  'MessagePack.Unity',
  'MessagePack.Annotations',
  # Mirror Networking
  'Mirror',
  'Mirror.Components',
  # Photon
  'PhotonUnityNetworking',
  'PhotonRealtime',
  # NaughtyAttributes
  'NaughtyAttributes.Core',
  'NaughtyAttributes.Editor',
  # Other common packages
  'Spine.Unity',
  'Spine.Unity.Editor'
)

Write-Info "Starting Assembly Definition file validation..."

$repoRoot = (Get-Item $PSScriptRoot).Parent.FullName

# Collect all asmdef files and their names for reference validation
$allAsmdefFiles = @{}
$asmdefFilesToValidate = @()

foreach ($root in $sourceRoots) {
  $rootPath = Join-Path -Path $repoRoot -ChildPath $root
  if (-not (Test-Path $rootPath)) {
    Write-Info "Skipping $root (directory not found)"
    continue
  }

  $asmdefFiles = Get-ChildItem -Path $rootPath -Filter "*.asmdef" -Recurse -File | Where-Object {
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

  foreach ($file in $asmdefFiles) {
    $asmdefFilesToValidate += $file
    # Parse the file to get its name for reference checking
    try {
      $content = Get-Content -Path $file.FullName -Raw
      $json = $content | ConvertFrom-Json
      if ($json.name) {
        $allAsmdefFiles[$json.name] = $file.FullName
      }
    }
    catch {
      # Will be caught during validation
    }
  }
}

Write-Info "Found $($asmdefFilesToValidate.Count) .asmdef files to validate"
Write-Info "Building reference map with $($allAsmdefFiles.Count) assembly names"

$errorList = @()
$warningList = @()
$checkedCount = 0

foreach ($file in $asmdefFilesToValidate) {
  $checkedCount++
  $relativePath = ConvertTo-RepoRelativePath -Path $file.FullName
  $expectedName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)

  Write-Info "Validating: $relativePath"

  # 1. Validate JSON syntax
  $content = $null
  $json = $null
  try {
    $content = Get-Content -Path $file.FullName -Raw
    $json = $content | ConvertFrom-Json
  }
  catch {
    $errorList += "[$relativePath] Invalid JSON syntax: $($_.Exception.Message)"
    continue
  }

  # 2. Check that 'name' field matches filename
  $jsonName = Get-JsonProp $json 'name'
  if (-not $jsonName) {
    $errorList += "[$relativePath] Missing required 'name' field"
  }
  elseif ($jsonName -ne $expectedName) {
    $errorList += "[$relativePath] Name mismatch: 'name' field is '$jsonName' but filename suggests '$expectedName'"
  }
  else {
    Write-Info "  Name field matches filename: $jsonName"
  }

  # 3. Check that 'rootNamespace' is set (warning only)
  $jsonRootNamespace = Get-JsonProp $json 'rootNamespace'
  if (-not $jsonRootNamespace -or $jsonRootNamespace -eq '') {
    $warningList += "[$relativePath] Missing 'rootNamespace' field (recommended for code organization)"
  }
  else {
    Write-Info "  Root namespace: $jsonRootNamespace"
  }

  # 4. Verify referenced assemblies exist. Build a real array (empty if the field is absent/null)
  #    so .Count / enumeration are StrictMode-safe regardless of element count.
  $jsonReferences = @()
  if ($json.PSObject.Properties['references'] -and $null -ne $json.references) {
    $jsonReferences = @($json.references)
  }
  if ($jsonReferences.Count -gt 0) {
    Write-Info "  Checking $($jsonReferences.Count) assembly references..."

    foreach ($ref in $jsonReferences) {
      # Handle both string references and GUID references
      $refName = $ref
      if ($ref -match '^GUID:') {
        Write-Info "    GUID reference: $ref (skipping validation)"
        continue
      }

      # Check if it's a Unity built-in assembly
      if ($unityBuiltInAssemblies -contains $refName) {
        Write-Info "    Unity built-in: $refName"
        continue
      }

      # Check if it's a known third-party package
      if ($thirdPartyAssemblies -contains $refName) {
        Write-Info "    Third-party package: $refName"
        continue
      }

      # Check if it matches a known pattern for Unity packages
      if ($refName -match '^Unity\.' -or $refName -match '^UnityEngine\.' -or $refName -match '^UnityEditor\.') {
        Write-Info "    Unity package: $refName (assumed valid)"
        continue
      }

      # Check if assembly exists in the repo
      if ($allAsmdefFiles.ContainsKey($refName)) {
        Write-Info "    Found in repo: $refName"
      }
      else {
        $errorList += "[$relativePath] Referenced assembly '$refName' not found in repository"
      }
    }
  }
  else {
    Write-Info "  No assembly references"
  }

  # 5. Validate versionDefines expressions against Unity's actual grammar.
  #    Unity silently ignores an invalid expression (no compile error -- the define just never
  #    fires), so a typo here surfaces only as a downstream compile failure across the whole
  #    Unity matrix. Catching it in this fast Unity-free lint short-circuits that entire class.
  if ($json.PSObject.Properties['versionDefines'] -and $json.versionDefines) {
    Write-Info "  Checking $($json.versionDefines.Count) versionDefines..."
    foreach ($vd in $json.versionDefines) {
      $vdName = Get-JsonProp $vd 'name'
      $vdExpr = Get-JsonProp $vd 'expression'
      $vdDefine = Get-JsonProp $vd 'define'

      if ([string]::IsNullOrWhiteSpace($vdName)) {
        $errorList += "[$relativePath] versionDefines entry missing 'name' (package/module id)"
      }
      if ([string]::IsNullOrWhiteSpace($vdDefine)) {
        $errorList += "[$relativePath] versionDefines entry for '$vdName' missing 'define' symbol"
      }

      $check = Test-UnityVersionDefineExpression -Expression $vdExpr
      if (-not $check.Valid) {
        $msg = "[$relativePath] versionDefines '$vdDefine' (package '$vdName') has invalid expression '$vdExpr': $($check.Reason)"
        if ($check.Suggestion) { $msg += " -- $($check.Suggestion)" }
        $errorList += $msg
      }
      else {
        Write-Info "    OK: define '$vdDefine' expression '$vdExpr'"
      }
    }
  }
}

# 6. Optional Odin integration is validated by scripts/tests/test-sync-script-contracts.ps1.
#    Runtime Odin bases are allowed only through the package-owned odininspector version define;
#    unguarded Sirenix references still fail the dedicated contract test.

# Also validate any .asmref files
$asmrefFiles = @()
foreach ($root in $sourceRoots) {
  $rootPath = Join-Path -Path $repoRoot -ChildPath $root
  if (-not (Test-Path $rootPath)) {
    continue
  }

  $files = Get-ChildItem -Path $rootPath -Filter "*.asmref" -Recurse -File -ErrorAction SilentlyContinue | Where-Object {
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

  if ($files) {
    $asmrefFiles += $files
  }
}

if ($asmrefFiles.Count -gt 0) {
  Write-Info ""
  Write-Info "Validating $($asmrefFiles.Count) .asmref files..."

  foreach ($file in $asmrefFiles) {
    $checkedCount++
    $relativePath = ConvertTo-RepoRelativePath -Path $file.FullName

    Write-Info "Validating: $relativePath"

    # Validate JSON syntax
    try {
      $content = Get-Content -Path $file.FullName -Raw
      $json = $content | ConvertFrom-Json
    }
    catch {
      $errorList += "[$relativePath] Invalid JSON syntax: $($_.Exception.Message)"
      continue
    }

    # Check that 'reference' field points to a valid assembly
    $refName = Get-JsonProp $json 'reference'
    if (-not $refName) {
      $errorList += "[$relativePath] Missing required 'reference' field"
    }
    else {
      if ($refName -match '^GUID:') {
        Write-Info "  GUID reference: $refName (skipping validation)"
      }
      elseif ($unityBuiltInAssemblies -contains $refName -or $thirdPartyAssemblies -contains $refName -or $refName -match '^Unity\.' -or $refName -match '^UnityEngine\.' -or $refName -match '^UnityEditor\.') {
        Write-Info "  Unity/third-party assembly: $refName"
      }
      elseif ($allAsmdefFiles.ContainsKey($refName)) {
        Write-Info "  Found in repo: $refName"
      }
      else {
        $errorList += "[$relativePath] Referenced assembly '$refName' not found in repository"
      }
    }
  }
}

# 7. Cross-assembly versionDefine usage. Unity versionDefines are PER-ASSEMBLY: a define set in
#    asmdef A is invisible to assembly B. So code that does `#if SYMBOL` where SYMBOL is a
#    versionDefine must live in an assembly whose OWN asmdef declares SYMBOL -- otherwise SYMBOL
#    never fires there and the `#else`/non-SYMBOL branch silently compiles. When that branch
#    targets a different package API (e.g. Reflex <14 AddSingleton while CI has 14.x) the result
#    is the same catastrophic CS-error/no-tests failure as a malformed expression, just relocated
#    to the consuming assembly. We scope the check to assemblies that OPT IN to the symbol's
#    package (declare >= 1 versionDefine for it): that flags the real "declared REFLEX_PRESENT,
#    forgot REFLEX_14_0_OR_NEWER" mistake while never flagging intentional optional-presence
#    gating (an assembly that declares no versionDefine for the package and uses `#if SYMBOL`
#    purely to compile away when the package is absent, e.g. Tests.Core + REFLEX_PRESENT).
$symbolToPackages = @{}
$asmdefDefineInfo = @{}
foreach ($file in $asmdefFilesToValidate) {
  try { $vdJson = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json }
  catch { continue }
  $declaredSymbols = New-Object System.Collections.Generic.HashSet[string]
  $declaredPackages = New-Object System.Collections.Generic.HashSet[string]
  foreach ($vd in @(Get-JsonProp $vdJson 'versionDefines')) {
    if ($null -eq $vd) { continue }
    $d = Get-JsonProp $vd 'define'
    $n = Get-JsonProp $vd 'name'
    if ([string]::IsNullOrWhiteSpace($d)) { continue }
    [void]$declaredSymbols.Add($d)
    if (-not [string]::IsNullOrWhiteSpace($n)) {
      [void]$declaredPackages.Add($n)
      if (-not $symbolToPackages.ContainsKey($d)) {
        $symbolToPackages[$d] = New-Object System.Collections.Generic.HashSet[string]
      }
      [void]$symbolToPackages[$d].Add($n)
    }
  }
  $asmdefDefineInfo[$file.DirectoryName] = [PSCustomObject]@{
    Path = $file.FullName; Symbols = $declaredSymbols; Packages = $declaredPackages
  }
}

if ($symbolToPackages.Count -gt 0) {
  $sep = [System.IO.Path]::DirectorySeparatorChar
  $usedByAsmdef = @{}
  foreach ($root in $sourceRoots) {
    $rootPath = Join-Path -Path $repoRoot -ChildPath $root
    if (-not (Test-Path $rootPath)) { continue }
    $csFiles = Get-ChildItem -Path $rootPath -Filter '*.cs' -Recurse -File -ErrorAction SilentlyContinue | Where-Object {
      $p = $_.FullName; $excluded = $false
      foreach ($dir in $excludeDirs) {
        if ($p -match [regex]::Escape("\$dir\") -or $p -match [regex]::Escape("/$dir/")) { $excluded = $true; break }
      }
      -not $excluded
    }
    foreach ($cs in $csFiles) {
      # Attribute the file to its NEAREST-ancestor asmdef (longest matching directory).
      $owner = $null; $ownerLen = -1
      $csDir = $cs.DirectoryName
      foreach ($ad in $asmdefDefineInfo.Keys) {
        if (($csDir -eq $ad -or $csDir.StartsWith($ad + $sep)) -and $ad.Length -gt $ownerLen) {
          $owner = $ad; $ownerLen = $ad.Length
        }
      }
      if (-not $owner) { continue }
      # [IO.File]::ReadAllText is ~5x faster than Get-Content -Raw across the whole repo,
      # which keeps this lint well under the CI sub-minute budget even at 1000s of files.
      try { $text = [System.IO.File]::ReadAllText($cs.FullName) } catch { continue }
      if ([string]::IsNullOrEmpty($text) -or -not $text.Contains('#')) { continue }
      # Strip comments first so a commented-out or example `#if SYMBOL` (in a /* ... */ block
      # or after //) is not mistaken for a live directive and falsely flagged.
      $text = [regex]::Replace($text, '(?s)/\*.*?\*/', '')
      $text = [regex]::Replace($text, '(?m)//.*$', '')
      foreach ($m in [regex]::Matches($text, '(?m)^\s*#\s*(?:el)?if\b(.*)$')) {
        foreach ($tok in [regex]::Matches($m.Groups[1].Value, '[A-Za-z_][A-Za-z0-9_]*')) {
          if ($symbolToPackages.ContainsKey($tok.Value)) {
            if (-not $usedByAsmdef.ContainsKey($owner)) {
              $usedByAsmdef[$owner] = New-Object System.Collections.Generic.HashSet[string]
            }
            [void]$usedByAsmdef[$owner].Add($tok.Value)
          }
        }
      }
    }
  }

  foreach ($dir in $usedByAsmdef.Keys) {
    $info = $asmdefDefineInfo[$dir]
    $rel = $info.Path.Replace($repoRoot, '').TrimStart('\', '/')
    foreach ($sym in $usedByAsmdef[$dir]) {
      if ($info.Symbols.Contains($sym)) { continue }
      $opensPackage = $false
      foreach ($pkg in $symbolToPackages[$sym]) {
        if ($info.Packages.Contains($pkg)) { $opensPackage = $true; break }
      }
      if ($opensPackage) {
        # Suggest the package id THIS asmdef already declares (a symbol may map to several ids).
        $pkgName = @($info.Packages | Where-Object { $symbolToPackages[$sym].Contains($_) })[0]
        $errorList += "[$rel] code uses '#if $sym' but this asmdef declares other versionDefines for package '$pkgName' WITHOUT declaring '$sym'. Unity versionDefines are per-assembly, so '$sym' never fires here and the wrong #if branch compiles. Add: { ""name"": ""$pkgName"", ""expression"": ""<min-version>"", ""define"": ""$sym"" }"
      }
    }
  }
}

Write-Info ""
Write-Info "Summary:"
Write-Info "  Files checked: $checkedCount"
Write-Info "  Errors: $($errorList.Count)"
Write-Info "  Warnings: $($warningList.Count)"

# Print warnings
if ($warningList.Count -gt 0) {
  Write-Host ""
  Write-WarningMsg "Warnings found:"
  foreach ($warn in $warningList) {
    Write-WarningMsg "  $warn"
  }
}

# Print errors and exit with failure if any
if ($errorList.Count -gt 0) {
  Write-Host ""
  Write-ErrorMsg "Errors found:"
  foreach ($err in $errorList) {
    Write-ErrorMsg "  $err"
  }
  Write-Host ""
  Write-ErrorMsg "Assembly Definition validation failed with $($errorList.Count) error(s)."
  exit 1
}

Write-Host ""
Write-SuccessMsg "All Assembly Definition files are valid!"
exit 0
