Param(
  [switch]$VerboseOutput
)

<#
.SYNOPSIS
    Test runner for lint-bundled-assemblies.ps1

.DESCRIPTION
    The linter exists because its subject is invisible: a NuGet refresh that drops a new DLL into
    Runtime/Binaries, or regenerates a .meta without its define constraint, reintroduces the
    CS0012/CS0103 assembly-resolution break of #331 with nothing to notice it. Run against the
    shipped binaries it prints "4 bundled assemblies classified and constrained", which is evidence
    about those four DLLs and none at all about whether the linter still reports (#556, #562). It
    now takes -BinariesRoot, and this drives fixture directories through every rule.

    Green half:
    - the shipped Runtime/Binaries passes
    - a fixture carrying all four classified assemblies with correct importers passes
    - flow-style `defineConstraints: ['!UNITY_6000_5_OR_NEWER']` is accepted, since Unity writes
      block style and the flow branch is the one nothing exercises

    Red halves, one per rule that fails the run, each asserted on its own message:
    - a binaries directory that does not exist
    - a directory with no DLLs at all
    - an assembly on the deliberately-not-shipped list
    - an unclassified assembly
    - a DLL with no .meta
    - block-style constraints that do not match the classification
    - a constrained assembly whose importer dropped the constraint entirely
    - flow-style constraints that do not match, which a parser reading a populated flow sequence
      as "unconstrained" would pass in the permissive direction
    - a classified assembly that is no longer present

.PARAMETER VerboseOutput
    Show detailed output during test execution

.EXAMPLE
    ./scripts/tests/test-lint-bundled-assemblies.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

$repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
$linter = Join-Path $repoRoot 'scripts/lint-bundled-assemblies.ps1'
$workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("lint-bundled-assemblies-tests-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workspace -Force | Out-Null

$constrained = @('Microsoft.Bcl.AsyncInterfaces', 'System.Text.Encodings.Web', 'System.Text.Json')
$unconstrained = 'System.IO.Pipelines'
$constraint = '!UNITY_6000_5_OR_NEWER'

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[test-lint-bundled-assemblies] $msg" -ForegroundColor Cyan }
}

function Write-TestResult {
  param([string]$TestName, [bool]$Passed, [string]$Message = '')

  if ($Passed) {
    Write-Host "  [PASS] $TestName" -ForegroundColor Green
    $script:TestsPassed++
  }
  else {
    Write-Host "  [FAIL] $TestName" -ForegroundColor Red
    if ($Message) { Write-Host "         $Message" -ForegroundColor DarkGray }
    $script:TestsFailed++
    $script:FailedTests += $TestName
  }
}

function Invoke-Linter {
  param([string]$Root)

  $output = & pwsh -NoProfile -File $linter -BinariesRoot $Root 2>&1
  return [pscustomobject]@{
    ExitCode = $LASTEXITCODE
    Output   = (($output | Out-String) -replace '\s+', ' ')
  }
}

# The DLL body is never read -- only its file name and its importer -- so an empty file is a
# faithful stand-in and keeps the fixtures free of binaries.
function New-Assembly {
  param(
    [string]$Root,
    [string]$Name,
    [string[]]$Constraints,
    [switch]$NoMeta,
    [switch]$FlowStyle
  )

  $dll = Join-Path $Root "$Name.dll"
  Set-Content -LiteralPath $dll -Value '' -NoNewline
  if ($NoMeta) {
    return
  }

  $constraintBlock = if ($FlowStyle) {
    if ($Constraints.Count -eq 0) { '  defineConstraints: []' }
    else { "  defineConstraints: [" + (($Constraints | ForEach-Object { "'$_'" }) -join ', ') + "]" }
  }
  elseif ($Constraints.Count -eq 0) {
    '  defineConstraints: []'
  }
  else {
    (@('  defineConstraints:') + ($Constraints | ForEach-Object { "  - '$_'" })) -join "`n"
  }

  Set-Content -LiteralPath "$dll.meta" -Value @"
fileFormatVersion: 2
guid: $([System.Guid]::NewGuid().ToString('N'))
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
$constraintBlock
  isPreloaded: 0
  isOverridable: 1
"@
}

# Every fixture starts as a correct set, so a red half differs from a green one by exactly the
# mutation it is named for.
function New-BinariesFixture {
  param([string]$Name, [switch]$FlowStyle)

  $root = Join-Path $workspace $Name
  New-Item -ItemType Directory -Path $root -Force | Out-Null
  foreach ($name in $constrained) {
    New-Assembly -Root $root -Name $name -Constraints @($constraint) -FlowStyle:$FlowStyle
  }
  New-Assembly -Root $root -Name $unconstrained -Constraints @() -FlowStyle:$FlowStyle
  return $root
}

function Test-Accepts {
  param([string]$TestName, [string]$Root)

  $result = Invoke-Linter -Root $Root
  if ($result.ExitCode -eq 0) {
    Write-TestResult -TestName $TestName -Passed $true
  }
  else {
    Write-TestResult -TestName $TestName -Passed $false -Message "exit $($result.ExitCode): $($result.Output)"
  }
}

function Test-Rejects {
  param([string]$TestName, [string]$Root, [string]$ExpectedMessage)

  $result = Invoke-Linter -Root $Root
  if ($result.ExitCode -eq 0) {
    Write-TestResult -TestName $TestName -Passed $false -Message 'linter accepted a binaries directory it must reject'
    return
  }
  # .Contains, not -like: -like reads [(none)] as a wildcard character class, so the assertion
  # silently stops matching the message it names. It is also case-sensitive, which is what
  # asserting on a specific message is supposed to mean.
  if (-not $result.Output.Contains($ExpectedMessage)) {
    Write-TestResult -TestName $TestName -Passed $false -Message "rejected, but not for the reason under test. Expected to contain '$ExpectedMessage'. Got: $($result.Output)"
    return
  }
  Write-TestResult -TestName $TestName -Passed $true
}

Write-Host ''
Write-Host 'Running lint-bundled-assemblies.ps1 self-tests' -ForegroundColor Cyan
Write-Host ''

try {
  Write-Info "Workspace: $workspace"

  # ── Green half ────────────────────────────────────────────────────────────
  Test-Accepts -TestName 'the shipped Runtime/Binaries passes' -Root (Join-Path $repoRoot 'Runtime/Binaries')
  Test-Accepts -TestName 'a correct fixture passes' -Root (New-BinariesFixture -Name 'good')
  Test-Accepts -TestName 'flow-style defineConstraints are accepted' -Root (New-BinariesFixture -Name 'good-flow' -FlowStyle)

  # ── Red halves ────────────────────────────────────────────────────────────
  Test-Rejects -TestName 'a missing binaries directory is rejected' `
    -Root (Join-Path $workspace 'does-not-exist') `
    -ExpectedMessage 'Bundled assembly directory not found'

  $empty = Join-Path $workspace 'empty'
  New-Item -ItemType Directory -Path $empty -Force | Out-Null
  Test-Rejects -TestName 'a directory with no DLLs is rejected' -Root $empty -ExpectedMessage 'No DLLs found'

  $notShipped = New-BinariesFixture -Name 'not-shipped'
  New-Assembly -Root $notShipped -Name 'System.Runtime.CompilerServices.Unsafe' -Constraints @()
  Test-Rejects -TestName 'a deliberately-not-shipped assembly is rejected' -Root $notShipped `
    -ExpectedMessage 'is recorded as deliberately not shipped'

  $unclassified = New-BinariesFixture -Name 'unclassified'
  New-Assembly -Root $unclassified -Name 'Newtonsoft.Json' -Constraints @()
  Test-Rejects -TestName 'an unclassified assembly is rejected' -Root $unclassified `
    -ExpectedMessage 'is not classified by scripts/lint-bundled-assemblies.ps1'

  $noMeta = New-BinariesFixture -Name 'no-meta'
  Remove-Item -LiteralPath (Join-Path $noMeta 'System.Text.Json.dll.meta')
  Test-Rejects -TestName 'a DLL with no importer metadata is rejected' -Root $noMeta `
    -ExpectedMessage 'Missing importer metadata'

  $wrongConstraint = New-BinariesFixture -Name 'wrong-constraint'
  New-Assembly -Root $wrongConstraint -Name 'System.Text.Json' -Constraints @('UNITY_2021_3_OR_NEWER')
  Test-Rejects -TestName 'a mismatched constraint is rejected' -Root $wrongConstraint `
    -ExpectedMessage 'must carry defineConstraints'

  # The failure a regenerated importer actually produces: the constraint is not wrong, it is gone.
  $droppedConstraint = New-BinariesFixture -Name 'dropped-constraint'
  New-Assembly -Root $droppedConstraint -Name 'System.Text.Json' -Constraints @()
  Test-Rejects -TestName 'a dropped constraint is rejected' -Root $droppedConstraint `
    -ExpectedMessage 'but carries [(none)]'

  # A parser that returned early on a populated flow sequence would read this as unconstrained and
  # pass it, which is the permissive direction the linter exists to close.
  $wrongFlow = New-BinariesFixture -Name 'wrong-flow'
  New-Assembly -Root $wrongFlow -Name 'System.IO.Pipelines' -Constraints @($constraint) -FlowStyle
  Test-Rejects -TestName 'a mismatched flow-style constraint is rejected' -Root $wrongFlow `
    -ExpectedMessage 'must carry defineConstraints [(none)]'

  $missingAssembly = New-BinariesFixture -Name 'missing-assembly'
  Remove-Item -LiteralPath (Join-Path $missingAssembly 'System.Text.Json.dll')
  Remove-Item -LiteralPath (Join-Path $missingAssembly 'System.Text.Json.dll.meta')
  Test-Rejects -TestName 'a classified assembly that vanished is rejected' -Root $missingAssembly `
    -ExpectedMessage 'is classified as shipped but no longer exists'
}
finally {
  Remove-Item -LiteralPath $workspace -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "Passed: $script:TestsPassed" -ForegroundColor Green
Write-Host "Failed: $script:TestsFailed" -ForegroundColor $(if ($script:TestsFailed -gt 0) { 'Red' } else { 'Green' })

if ($script:TestsFailed -gt 0) {
  Write-Host ''
  Write-Host 'Failed tests:' -ForegroundColor Red
  foreach ($name in $script:FailedTests) {
    Write-Host "  - $name" -ForegroundColor Red
  }
  exit 1
}

exit 0
