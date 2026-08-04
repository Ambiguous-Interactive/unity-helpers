Param(
  [switch]$VerboseOutput
)

<#
.SYNOPSIS
    Test runner for check-eol.ps1.

.DESCRIPTION
    Verifies that check-eol.ps1 detects committed blobs whose stored form
    violates git's text normalization, in addition to its worktree checks.

    An unnormalized blob (CR bytes stored under a `text` attribute) checks out
    looking correct but makes git report the file as modified in every fresh
    clone, which aborts automation that switches branches after checkout. A
    worktree-only check cannot see that, so this covers the index form:

    - A normalized repository passes (exit 0).
    - A CRLF blob under `text eol=crlf` fails (exit 3) and names the path.
    - A CRLF blob under `text eol=lf` fails (exit 3).
    - A mixed-EOL blob fails (exit 3).
    - Binary content and `-text` paths are exempt (exit 0).
    - `-Paths` scoping only reports blobs inside the requested scope.
    - Worktree EOL violations still fail alongside the blob check.

    Blobs are written with `git hash-object --no-filters` because that is how
    unnormalized content reaches the repository in practice: writers that
    bypass git's clean filter, such as commits created through the GitHub API.

.PARAMETER VerboseOutput
    Show detailed output during test execution.

.EXAMPLE
    pwsh -NoProfile -File scripts/tests/test-check-eol.ps1
    pwsh -NoProfile -File scripts/tests/test-check-eol.ps1 -VerboseOutput
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

$checkScriptPath = (Resolve-Path (Join-Path $PSScriptRoot '..' 'check-eol.ps1')).Path

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[test-check-eol] $msg" -ForegroundColor Cyan }
}

function Write-TestResult {
  param(
    [string]$TestName,
    [bool]$Passed,
    [string]$Message = ''
  )

  if ($Passed) {
    Write-Host "  [PASS] $TestName" -ForegroundColor Green
    $script:TestsPassed++
  } else {
    Write-Host "  [FAIL] $TestName" -ForegroundColor Red
    if ($Message) { Write-Host "         $Message" -ForegroundColor Yellow }
    $script:TestsFailed++
    $script:FailedTests += $TestName
  }
}

function New-TestRepo {
  param([string]$GitattributesContent = "* text=auto eol=crlf`n")

  $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "check-eol-test-$([System.Guid]::NewGuid().ToString('N').Substring(0,8))"
  New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
  New-Item -ItemType Directory -Path (Join-Path $tempDir 'scripts') -Force | Out-Null
  Copy-Item -LiteralPath $checkScriptPath -Destination (Join-Path $tempDir 'scripts/check-eol.ps1')

  Push-Location $tempDir
  try {
    & git init -q 2>&1 | Out-Null
    & git config user.email 'test@test.com' 2>&1 | Out-Null
    & git config user.name 'Test' 2>&1 | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $tempDir '.gitattributes'), $GitattributesContent)
    & git add .gitattributes 2>&1 | Out-Null
    & git commit -q -m 'Add attributes' 2>&1 | Out-Null
  } finally {
    Pop-Location
  }

  return $tempDir
}

function Add-NormalizedFile {
  param(
    [string]$RepoDir,
    [string]$RelativePath,
    [string]$Content
  )

  $fullPath = Join-Path $RepoDir $RelativePath
  $parent = Split-Path -Parent $fullPath
  if ($parent -and -not (Test-Path -LiteralPath $parent)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
  }
  [System.IO.File]::WriteAllText($fullPath, $Content)

  Push-Location $RepoDir
  try {
    & git add -- $RelativePath 2>&1 | Out-Null
  } finally {
    Pop-Location
  }
}

# Stages content verbatim, bypassing git's clean filter, then materializes the
# same bytes in the worktree so only the blob form is anomalous.
function Add-UnfilteredFile {
  param(
    [string]$RepoDir,
    [string]$RelativePath,
    [byte[]]$Bytes
  )

  $fullPath = Join-Path $RepoDir $RelativePath
  $parent = Split-Path -Parent $fullPath
  if ($parent -and -not (Test-Path -LiteralPath $parent)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
  }
  [System.IO.File]::WriteAllBytes($fullPath, $Bytes)

  Push-Location $RepoDir
  try {
    $blob = (& git hash-object -w --no-filters -- $RelativePath).Trim()
    & git update-index --add --cacheinfo "100644,$blob,$RelativePath" 2>&1 | Out-Null
  } finally {
    Pop-Location
  }
}

function Invoke-Checker {
  param(
    [string]$RepoDir,
    [string[]]$Paths = @()
  )

  $checkerArgs = @((Join-Path $RepoDir 'scripts/check-eol.ps1'), '-VerboseOutput')
  if ($Paths.Count -gt 0) {
    $checkerArgs += '-Paths'
    $checkerArgs += $Paths
  }

  Push-Location $RepoDir
  try {
    $output = & pwsh -NoProfile -File @checkerArgs 2>&1
    $exitCode = $LASTEXITCODE
  } finally {
    Pop-Location
  }

  Write-Info "exit=$exitCode output=$($output -join ' | ')"
  return @{
    ExitCode = $exitCode
    Output   = ($output -join "`n")
  }
}

function Remove-TestRepo {
  param([string]$RepoDir)
  Remove-Item -Path $RepoDir -Recurse -Force -ErrorAction SilentlyContinue
}

function Get-Bytes([string]$text) {
  return [System.Text.Encoding]::UTF8.GetBytes($text)
}

function Get-CrlfBytes([string[]]$lines) {
  return Get-Bytes ((($lines -join "`r`n")) + "`r`n")
}

Write-Host 'Testing check-eol.ps1...' -ForegroundColor White

# ==== Test group 1: normalized repository passes ====
Write-Host "`nTest group: Normalized repository" -ForegroundColor Magenta

$repo = New-TestRepo
try {
  Add-NormalizedFile -RepoDir $repo -RelativePath 'scripts/example.ps1' -Content "Write-Host 'a'`r`nWrite-Host 'b'`r`n"
  Add-NormalizedFile -RepoDir $repo -RelativePath 'docs/example.md' -Content "# Title`n`nBody`n"
  $result = Invoke-Checker -RepoDir $repo
  Write-TestResult 'NormalizedRepo_Passes' ($result.ExitCode -eq 0) "Expected exit 0, got $($result.ExitCode): $($result.Output)"
  Write-TestResult 'NormalizedRepo_ReportsZeroBlobs' ($result.Output -match 'Unnormalized committed blobs: 0') "Output: $($result.Output)"
} finally {
  Remove-TestRepo $repo
}

# ==== Test group 2: unnormalized blobs fail ====
Write-Host "`nTest group: Unnormalized committed blobs" -ForegroundColor Magenta

$blobScenarios = @(
  @{
    Name         = 'CrlfBlob_UnderEolCrlf_Fails'
    Attributes   = "* text=auto eol=crlf`n"
    RelativePath = 'scripts/polluted.ps1'
    Text         = "Write-Host 'a'`r`nWrite-Host 'b'`r`n"
    ExpectRegex  = 'scripts/polluted\.ps1 \(committed blob is crlf'
  },
  @{
    Name         = 'CrlfBlob_UnderEolLf_Fails'
    Attributes   = "* text=auto eol=lf`n"
    RelativePath = 'workflow.yml'
    Text         = "name: a`r`non: push`r`n"
    ExpectRegex  = 'workflow\.yml \(committed blob is crlf'
  },
  @{
    Name         = 'MixedBlob_Fails'
    Attributes   = "* text=auto eol=crlf`n"
    RelativePath = 'scripts/mixed.ps1'
    Text         = "Write-Host 'a'`r`nWrite-Host 'b'`nWrite-Host 'c'`n"
    ExpectRegex  = 'scripts/mixed\.ps1 \(committed blob is mixed'
  }
)

foreach ($scenario in $blobScenarios) {
  $repo = New-TestRepo -GitattributesContent $scenario.Attributes
  try {
    Add-UnfilteredFile -RepoDir $repo -RelativePath $scenario.RelativePath -Bytes (Get-Bytes $scenario.Text)
    $result = Invoke-Checker -RepoDir $repo
    Write-TestResult $scenario.Name ($result.ExitCode -eq 3) "Expected exit 3, got $($result.ExitCode): $($result.Output)"
    Write-TestResult "$($scenario.Name)_NamesPath" ($result.Output -match $scenario.ExpectRegex) "Expected output to match '$($scenario.ExpectRegex)': $($result.Output)"
    Write-TestResult "$($scenario.Name)_SuggestsRenormalize" ($result.Output -match 'git add --renormalize') "Output: $($result.Output)"
  } finally {
    Remove-TestRepo $repo
  }
}

# ==== Test group 3: exemptions ====
Write-Host "`nTest group: Exempt content" -ForegroundColor Magenta

$exemptScenarios = @(
  @{
    # `-text` is the documented escape hatch for content that must keep CR bytes.
    Name         = 'MinusTextPath_Exempt'
    Attributes   = "* text=auto eol=crlf`n*.bin -text`n"
    RelativePath = 'fixtures/crlf-sample.bin'
  },
  @{
    # `binary` is a macro that turns `text` off, so it round-trips byte for byte.
    Name         = 'BinaryAttributePath_Exempt'
    Attributes   = "* text=auto eol=crlf`n*.fixture binary`n"
    RelativePath = 'fixtures/crlf-sample.fixture'
  },
  @{
    # Without a text attribute git performs no conversion, so CRs survive.
    Name         = 'NoTextAttribute_Exempt'
    Attributes   = "*.md text eol=lf`n"
    RelativePath = 'fixtures/unattributed.txt'
  }
)

foreach ($scenario in $exemptScenarios) {
  $repo = New-TestRepo -GitattributesContent $scenario.Attributes
  try {
    Add-UnfilteredFile -RepoDir $repo -RelativePath $scenario.RelativePath -Bytes (Get-CrlfBytes @('first', 'second'))
    $result = Invoke-Checker -RepoDir $repo
    Write-TestResult $scenario.Name ($result.ExitCode -eq 0) "Expected exit 0, got $($result.ExitCode): $($result.Output)"
  } finally {
    Remove-TestRepo $repo
  }
}

$repo = New-TestRepo
try {
  # Auto-detected binary content carries CR bytes legitimately.
  Add-UnfilteredFile -RepoDir $repo -RelativePath 'assets/blob.dat' -Bytes ([byte[]](0x00, 0x0D, 0x0A, 0x01, 0x0D, 0x0A))
  $result = Invoke-Checker -RepoDir $repo
  Write-TestResult 'BinaryContent_Exempt' ($result.ExitCode -eq 0) "Expected exit 0, got $($result.ExitCode): $($result.Output)"
} finally {
  Remove-TestRepo $repo
}

# ==== Test group 4: -Paths scoping ====
Write-Host "`nTest group: Path scoping" -ForegroundColor Magenta

$repo = New-TestRepo
try {
  Add-UnfilteredFile -RepoDir $repo -RelativePath 'scripts/polluted.ps1' -Bytes (Get-Bytes "Write-Host 'a'`r`n")
  Add-NormalizedFile -RepoDir $repo -RelativePath 'scripts/clean.ps1' -Content "Write-Host 'b'`r`n"

  $inScope = Invoke-Checker -RepoDir $repo -Paths @('scripts/polluted.ps1')
  Write-TestResult 'PathScope_IncludesPollutedPath' ($inScope.ExitCode -eq 3) "Expected exit 3, got $($inScope.ExitCode): $($inScope.Output)"

  $outOfScope = Invoke-Checker -RepoDir $repo -Paths @('scripts/clean.ps1')
  Write-TestResult 'PathScope_ExcludesUnrelatedPath' ($outOfScope.ExitCode -eq 0) "Expected exit 0, got $($outOfScope.ExitCode): $($outOfScope.Output)"

  $absolute = Invoke-Checker -RepoDir $repo -Paths @((Join-Path $repo 'scripts/polluted.ps1'))
  Write-TestResult 'PathScope_AcceptsAbsolutePath' ($absolute.ExitCode -eq 3) "Expected exit 3, got $($absolute.ExitCode): $($absolute.Output)"
} finally {
  Remove-TestRepo $repo
}

# ==== Test group 5: worktree checks still enforced ====
Write-Host "`nTest group: Worktree checks" -ForegroundColor Magenta

$repo = New-TestRepo
try {
  Add-NormalizedFile -RepoDir $repo -RelativePath 'scripts/lf-in-crlf-file.ps1' -Content "Write-Host 'a'`r`n"
  # Rewrite the worktree copy with LF only; the blob stays normalized.
  [System.IO.File]::WriteAllBytes(
    (Join-Path $repo 'scripts/lf-in-crlf-file.ps1'),
    (Get-Bytes "Write-Host 'a'`n")
  )
  $result = Invoke-Checker -RepoDir $repo
  Write-TestResult 'WorktreeEolViolation_StillFails' ($result.ExitCode -eq 3) "Expected exit 3, got $($result.ExitCode): $($result.Output)"
  Write-TestResult 'WorktreeEolViolation_ReportsEolIssue' ($result.Output -match 'expected CRLF, found LF-only or mixed') "Output: $($result.Output)"
  Write-TestResult 'WorktreeEolViolation_BlobStillClean' ($result.Output -match 'Unnormalized committed blobs: 0') "Output: $($result.Output)"
} finally {
  Remove-TestRepo $repo
}

# ---- Summary ----
Write-Host ''
Write-Host "Passed: $script:TestsPassed" -ForegroundColor Green
if ($script:TestsFailed -gt 0) {
  Write-Host "Failed: $script:TestsFailed" -ForegroundColor Red
  foreach ($failed in $script:FailedTests) {
    Write-Host "  - $failed" -ForegroundColor Yellow
  }
  exit 1
}

Write-Host 'All check-eol.ps1 tests passed.' -ForegroundColor Green
exit 0
