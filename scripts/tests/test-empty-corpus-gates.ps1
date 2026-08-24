<#
.SYNOPSIS
    Proves the repository-corpus linters fail when their corpus is missing.

.DESCRIPTION
    Session 221 found three gates that could pass without checking anything, and #556 records the
    property that matters: can this gate go red? For a scanner over a clean corpus, a green run is
    not evidence that it can.

    Every gate here walks a fixed set of source roots. Before #556 a renamed or deleted root, or a
    walk that matched nothing, produced a success message and exit 0. This test copies scripts/ into
    a scratch root that has no Runtime/, Editor/ or Tests/ and asserts each one now fails.
#>
Param(
  [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

function Write-TestResult {
  param(
    [string]$TestName,
    [bool]$Passed,
    [string]$Message = ''
  )

  if ($Passed) {
    Write-Host "  [PASS] $TestName" -ForegroundColor Green
    $script:TestsPassed++
  }
  else {
    Write-Host "  [FAIL] $TestName" -ForegroundColor Red
    if (-not [string]::IsNullOrWhiteSpace($Message)) {
      Write-Host "         $Message" -ForegroundColor Yellow
    }
    $script:TestsFailed++
    $script:FailedTests += $TestName
  }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$tempBase = if ($env:TMPDIR) { $env:TMPDIR } elseif ($env:TEMP) { $env:TEMP } else { '/tmp' }
$scratchRoot = Join-Path $tempBase "test-empty-corpus-gates-$([System.Guid]::NewGuid().ToString('N'))"

# The gates under test resolve their corpus from $PSScriptRoot/.., so the scratch root has to carry
# a real copy of scripts/ -- several of them dot-source siblings.
$gates = @(
  @{ Name = 'lint-license-headers'; Path = 'scripts/lint-license-headers.ps1' },
  @{ Name = 'lint-no-regions'; Path = 'scripts/lint-no-regions.ps1' },
  @{ Name = 'lint-csharp-naming'; Path = 'scripts/lint-csharp-naming.ps1' },
  @{ Name = 'lint-asmdef'; Path = 'scripts/lint-asmdef.ps1' },
  @{ Name = 'lint-conditional-call-chains'; Path = 'scripts/lint-conditional-call-chains.ps1' },
  @{ Name = 'lint-duplicate-usings'; Path = 'scripts/lint-duplicate-usings.ps1' }
)

Write-Host ''
Write-Host '========================================' -ForegroundColor White
Write-Host 'Empty-Corpus Gate Tests' -ForegroundColor White
Write-Host '========================================' -ForegroundColor White
Write-Host ''

try {
  New-Item -ItemType Directory -Path $scratchRoot -Force | Out-Null
  Copy-Item -Path (Join-Path $repoRoot 'scripts') -Destination $scratchRoot -Recurse -Force

  foreach ($gate in $gates) {
    $scriptPath = Join-Path $scratchRoot $gate.Path
    if (-not (Test-Path -LiteralPath $scriptPath)) {
      Write-TestResult "$($gate.Name) fixture exists" $false "Missing: $scriptPath"
      continue
    }

    $previousLocation = Get-Location
    try {
      Set-Location -LiteralPath $scratchRoot
      $output = & pwsh -NoProfile -File $scriptPath *>&1
      $exitCode = $LASTEXITCODE
    }
    finally {
      Set-Location -LiteralPath $previousLocation
    }

    Write-TestResult "$($gate.Name) fails when its corpus is absent" ($exitCode -ne 0) "Expected a non-zero exit with no Runtime/Editor/Tests present. Exit: $exitCode. Output: $($output | Out-String)"
  }

  # audit-license-years.sh derives its corpus from `git ls-files`, so it gets its own shape:
  # zero files used to print "All files have correct copyright years!".
  $auditScript = Join-Path $scratchRoot 'scripts/audit-license-years.sh'
  $previousLocation = Get-Location
  try {
    Set-Location -LiteralPath $scratchRoot
    $output = & bash $auditScript --summary *>&1
    $exitCode = $LASTEXITCODE
  }
  finally {
    Set-Location -LiteralPath $previousLocation
  }
  Write-TestResult 'audit-license-years fails when no .cs files are audited' ($exitCode -ne 0) "Expected a non-zero exit with an empty corpus. Exit: $exitCode. Output: $($output | Out-String)"

  # dependabot's config is a single file rather than a tree, so it gets its own shape: deleting the
  # config used to skip the schema check and report success.
  $dependabotScript = Join-Path $scratchRoot 'scripts/lint-dependabot.ps1'
  $previousLocation = Get-Location
  try {
    Set-Location -LiteralPath $scratchRoot
    $output = & pwsh -NoProfile -File $dependabotScript *>&1
    $exitCode = $LASTEXITCODE
  }
  finally {
    Set-Location -LiteralPath $previousLocation
  }
  Write-TestResult 'lint-dependabot fails when the config is absent' ($exitCode -ne 0) "Expected a non-zero exit with no .github/dependabot.yml. Exit: $exitCode. Output: $($output | Out-String)"

  # There is deliberately no green half here. Every one of these gates already runs against the
  # real repository in lint:repo, on every push; repeating those six full-tree scans inside a
  # contract test would double their cost to prove what the next job proves anyway (#543).
}
finally {
  Remove-Item -Recurse -Force $scratchRoot -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host '========================================' -ForegroundColor White
Write-Host "Passed: $script:TestsPassed  Failed: $script:TestsFailed" -ForegroundColor White
Write-Host '========================================' -ForegroundColor White

if ($script:TestsFailed -gt 0) {
  foreach ($name in $script:FailedTests) {
    Write-Host "  - $name" -ForegroundColor Red
  }
  exit 1
}

exit 0
