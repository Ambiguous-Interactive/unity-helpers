[CmdletBinding(PositionalBinding = $false)]
Param(
  [switch]$VerboseOutput,
  [Parameter(ValueFromRemainingArguments = $true)]
  [string[]]$UnboundArguments
)

<#
.SYNOPSIS
    Tests for scripts/ci/verify-release-tag.ps1.

.DESCRIPTION
    Exercises the tag contract extracted from the release.yml verify-tag job
    (issue #360): strict unprefixed X.Y.Z semver, single-line inputs, package.json
    reads, tag/version agreement, and the GitHub outputs written on success.
    No git, network, or GitHub runtime required.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($UnboundArguments -and 0 -lt $UnboundArguments.Count) {
  Write-Host "Unbound arguments: $($UnboundArguments -join ', ')" -ForegroundColor Red
  exit 64
}

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

function Write-TestResult {
  param([string]$TestName, [bool]$Passed, [string]$Message = "")
  if ($Passed) {
    Write-Host "  [PASS] $TestName" -ForegroundColor Green
    $script:TestsPassed++
  }
  else {
    Write-Host "  [FAIL] $TestName" -ForegroundColor Red
    if ($Message) { Write-Host "         $Message" -ForegroundColor Yellow }
    $script:TestsFailed++
    $script:FailedTests += $TestName
  }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$verifyScript = Join-Path $repoRoot 'scripts' 'ci' 'verify-release-tag.ps1'

function New-PackageJson {
  param([string]$Name = "com.wallstop-studios.unity-helpers", [string]$Version = "3.5.2")
  $directory = Join-Path ([System.IO.Path]::GetTempPath()) ("verify-release-tag-" + [System.Guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Force -Path $directory | Out-Null
  $path = Join-Path $directory 'package.json'
  $manifest = [ordered]@{ name = $Name; version = $Version }
  $manifest | ConvertTo-Json | Set-Content -LiteralPath $path -Encoding utf8NoBOM
  return $path
}

function Invoke-VerifyReleaseTag {
  param(
    [string]$Tag = "3.5.2",
    [string]$SourceRef = "main",
    [string]$PackageJsonPath,
    [switch]$WithGithubOutput
  )
  $githubOutputPath = ''
  if ($WithGithubOutput) {
    $githubOutputPath = [System.IO.Path]::GetTempFileName()
  }
  try {
    $arguments = @(
      $verifyScript,
      '-Tag', $Tag,
      '-SourceRef', $SourceRef,
      '-PackageJsonPath', $PackageJsonPath
    )
    $previousGithubOutput = $env:GITHUB_OUTPUT
    if ($githubOutputPath) {
      $env:GITHUB_OUTPUT = $githubOutputPath
    } else {
      Remove-Item Env:GITHUB_OUTPUT -ErrorAction SilentlyContinue
    }
    try {
      $out = & pwsh -NoProfile -File @arguments 2>&1
      $exitCode = $LASTEXITCODE
    } finally {
      if ($null -ne $previousGithubOutput) {
        $env:GITHUB_OUTPUT = $previousGithubOutput
      } else {
        Remove-Item Env:GITHUB_OUTPUT -ErrorAction SilentlyContinue
      }
    }
    $githubOutput = ''
    if ($githubOutputPath -and (Test-Path -LiteralPath $githubOutputPath)) {
      $githubOutput = (Get-Content -LiteralPath $githubOutputPath -Raw)
    }
    return @{
      ExitCode = $exitCode
      Output = ($out -join "`n")
      GithubOutput = $githubOutput
    }
  } finally {
    if ($githubOutputPath) {
      Remove-Item -LiteralPath $githubOutputPath -Force -ErrorAction SilentlyContinue
    }
  }
}

Write-Host "Testing verify-release-tag.ps1..." -ForegroundColor White

$packageJson = New-PackageJson

# Valid tag matching package.json passes and emits the step outputs.
$r1 = Invoke-VerifyReleaseTag -PackageJsonPath $packageJson -WithGithubOutput
Write-TestResult "Verify.MatchingTagExitsZero" ($r1.ExitCode -eq 0) "exit $($r1.ExitCode); output: $($r1.Output)"
Write-TestResult "Verify.ErrorStreamClean" ($r1.Output -notmatch '::error::') "expected no ::error:: on success: $($r1.Output)"
Write-TestResult "Verify.WritesPackageName" ($r1.GithubOutput -match '(?m)^package-name=com\.wallstop-studios\.unity-helpers$') "GITHUB_OUTPUT missing package-name: $($r1.GithubOutput)"
Write-TestResult "Verify.WritesPackageVersion" ($r1.GithubOutput -match '(?m)^package-version=3\.5\.2$') "GITHUB_OUTPUT missing package-version: $($r1.GithubOutput)"
Write-TestResult "Verify.WritesTag" ($r1.GithubOutput -match '(?m)^tag=3\.5\.2$') "GITHUB_OUTPUT missing tag: $($r1.GithubOutput)"

# Mismatched version fails with the exact message.
$mismatchJson = New-PackageJson -Version "3.5.1"
$r2 = Invoke-VerifyReleaseTag -Tag "3.5.2" -PackageJsonPath $mismatchJson
Write-TestResult "Verify.MismatchedVersionFails" ($r2.ExitCode -eq 1) "exit $($r2.ExitCode)"
Write-TestResult "Verify.MismatchedVersionMessage" ($r2.Output -match '::error::Tag 3\.5\.2 does not match package\.json version 3\.5\.1\.') "output: $($r2.Output)"

# Invalid semver shapes fail.
$invalidTags = @('v3.5.2', '3.5', '3.5.2.1', '03.5.2', '3.5.2-beta', 'release-3.5.2', '')
foreach ($invalidTag in $invalidTags) {
  $label = if ($invalidTag) { $invalidTag } else { '<empty>' }
  $r3 = Invoke-VerifyReleaseTag -Tag $invalidTag -PackageJsonPath $packageJson
  Write-TestResult "Verify.InvalidTagRejects[$label]" ($r3.ExitCode -eq 1) "exit $($r3.ExitCode); output: $($r3.Output)"
}
$r3Empty = Invoke-VerifyReleaseTag -Tag '' -PackageJsonPath $packageJson
Write-TestResult "Verify.EmptyTagMessage" ($r3Empty.Output -match '::error::Release version is required\.') "output: $($r3Empty.Output)"
$r3Prefixed = Invoke-VerifyReleaseTag -Tag 'v3.5.2' -PackageJsonPath $packageJson
Write-TestResult "Verify.PrefixedTagMessage" ($r3Prefixed.Output -match '::error::Release tags must use unprefixed X\.Y\.Z semver\.') "output: $($r3Prefixed.Output)"

# Newlines and carriage returns in the tag fail.
$r4Lf = Invoke-VerifyReleaseTag -Tag "3.5.2`nrm -rf /" -PackageJsonPath $packageJson
Write-TestResult "Verify.NewlineTagFails" ($r4Lf.ExitCode -eq 1) "exit $($r4Lf.ExitCode)"
Write-TestResult "Verify.NewlineTagMessage" ($r4Lf.Output -match '::error::Release version must be a single line\.') "output: $($r4Lf.Output)"
$r4Cr = Invoke-VerifyReleaseTag -Tag "3.5.2`rcode-injection" -PackageJsonPath $packageJson
Write-TestResult "Verify.CarriageReturnTagFails" ($r4Cr.ExitCode -eq 1 -and $r4Cr.Output -match '::error::Release version must be a single line\.') "exit $($r4Cr.ExitCode); output: $($r4Cr.Output)"

# Source ref validation.
$r5Empty = Invoke-VerifyReleaseTag -SourceRef '' -PackageJsonPath $packageJson
Write-TestResult "Verify.EmptySourceRefFails" ($r5Empty.ExitCode -eq 1 -and $r5Empty.Output -match '::error::Release source ref is required\.') "exit $($r5Empty.ExitCode); output: $($r5Empty.Output)"
$r5Newline = Invoke-VerifyReleaseTag -SourceRef "main`nsecond" -PackageJsonPath $packageJson
Write-TestResult "Verify.NewlineSourceRefFails" ($r5Newline.ExitCode -eq 1 -and $r5Newline.Output -match '::error::Release source ref must be a single line\.') "exit $($r5Newline.ExitCode); output: $($r5Newline.Output)"

# package.json with missing name or version fails.
$namelessJson = New-PackageJson -Name ''
$r6Nameless = Invoke-VerifyReleaseTag -PackageJsonPath $namelessJson
Write-TestResult "Verify.NamelessPackageFails" ($r6Nameless.ExitCode -eq 1 -and $r6Nameless.Output -match '::error::package\.json name/version is missing\.') "exit $($r6Nameless.ExitCode); output: $($r6Nameless.Output)"
$missingFile = Join-Path ([System.IO.Path]::GetTempPath()) ("no-such-package-" + [System.Guid]::NewGuid().ToString('N') + '.json')
$r6Missing = Invoke-VerifyReleaseTag -PackageJsonPath $missingFile
Write-TestResult "Verify.MissingPackageFileFails" ($r6Missing.ExitCode -eq 1 -and $r6Missing.Output -match '::error::package\.json name/version is missing\.') "exit $($r6Missing.ExitCode); output: $($r6Missing.Output)"

# Validation order: an empty tag reports the tag error first and exits, so the source-ref
# check must not have run.
$r7 = Invoke-VerifyReleaseTag -Tag '' -SourceRef '' -PackageJsonPath $packageJson
Write-TestResult "Verify.TagCheckedBeforeSourceRef" (
  $r7.ExitCode -eq 1 -and
  $r7.Output -match '::error::Release version is required\.' -and
  $r7.Output -notmatch 'Release source ref is required\.'
) "exit $($r7.ExitCode); output: $($r7.Output)"

Remove-Item -LiteralPath $packageJson -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Split-Path -Parent $packageJson) -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $mismatchJson -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Split-Path -Parent $mismatchJson) -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $namelessJson -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Split-Path -Parent $namelessJson) -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host ("=" * 60)
Write-Host ("Tests passed: {0}" -f $script:TestsPassed) -ForegroundColor Green
Write-Host ("Tests failed: {0}" -f $script:TestsFailed) -ForegroundColor $(if ($script:TestsFailed -gt 0) { "Red" } else { "Green" })
if ($script:FailedTests.Count -gt 0) {
  Write-Host "Failed tests:" -ForegroundColor Red
  foreach ($t in $script:FailedTests) { Write-Host "  - $t" -ForegroundColor Red }
}
Write-Host ("=" * 60)
exit $script:TestsFailed
