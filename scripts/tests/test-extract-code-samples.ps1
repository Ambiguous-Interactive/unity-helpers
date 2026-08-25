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

# The extractor walks a tree of Markdown and reports what it found, so a fixture is a tree of
# Markdown. CODE_SAMPLES_ROOT points the walk at one; nothing in CI sets it.
function Invoke-ExtractorForTree {
  param(
    [Parameter(Mandatory = $true)]
    [hashtable]$Files,

    [switch]$SkipCreate
  )

  $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("extract-code-samples-" + [System.Guid]::NewGuid().ToString('N'))
  if (-not $SkipCreate) {
    New-Item -ItemType Directory -Path $tempDir | Out-Null
    foreach ($relative in $Files.Keys) {
      $target = Join-Path $tempDir $relative
      $parent = Split-Path -Parent $target
      if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
      }
      Set-Content -Path $target -Value $Files[$relative] -NoNewline
    }
  }

  $scriptPath = Join-Path $PSScriptRoot '..' 'extract-code-samples.js'

  try {
    $previous = $env:CODE_SAMPLES_ROOT
    $env:CODE_SAMPLES_ROOT = $tempDir
    # --output-dir inside the fixture: DEFAULT_OUTPUT_DIR is pinned to the repository, so without
    # this the self-test overwrites the real artifacts/code-samples report with fixture data.
    $output = & node $scriptPath --extract-only --output-dir (Join-Path $tempDir 'artifacts') 2>&1
    $exitCode = $LASTEXITCODE

    return @{
      ExitCode = $exitCode
      Output   = $output | Out-String
    }
  }
  finally {
    $env:CODE_SAMPLES_ROOT = $previous
    Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
  }
}

Write-Host ''
Write-Host '========================================' -ForegroundColor White
Write-Host 'Documentation Code Sample Extractor Tests' -ForegroundColor White
Write-Host '========================================' -ForegroundColor White
Write-Host ''

# The red half first. Before #556 this gate exited 0 on every one of these, so it could not fail
# under any input and a green run said nothing about whether it was still reading the docs.
$result = Invoke-ExtractorForTree -Files @{ 'docs/empty.md' = "# Docs`n`nProse only.`n" }
Write-TestResult -TestName 'A docs tree with no C# fences fails' -Passed ($result.ExitCode -eq 1) -Message $result.Output

$result = Invoke-ExtractorForTree -Files @{ '.keep' = 'x' }
Write-TestResult -TestName 'A docs tree with no Markdown at all fails' -Passed ($result.ExitCode -eq 1) -Message $result.Output

$otherLanguages = @"
# Docs

``````json
{ "a": 1 }
``````

``````bash
echo hi
``````
"@
$result = Invoke-ExtractorForTree -Files @{ 'docs/other.md' = $otherLanguages }
Write-TestResult -TestName 'Fences in other languages do not count as C# samples' -Passed ($result.ExitCode -eq 1) -Message $result.Output

$result = Invoke-ExtractorForTree -Files @{} -SkipCreate
Write-TestResult -TestName 'A scan root that does not exist fails' -Passed ($result.ExitCode -eq 1) -Message $result.Output

# The green half: one real C# fence is enough, and it must be found through a subdirectory walk.
$withSample = @"
# Docs

``````csharp
public sealed class Example
{
    public int Value => 1;
}
``````
"@
$result = Invoke-ExtractorForTree -Files @{ 'docs/features/example.md' = $withSample }
Write-TestResult -TestName 'A nested C# fence is found and passes' -Passed ($result.ExitCode -eq 0) -Message $result.Output

$csAlias = @"
# Docs

``````cs
public sealed class Example { }
``````
"@
$result = Invoke-ExtractorForTree -Files @{ 'docs/alias.md' = $csAlias }
Write-TestResult -TestName 'The cs fence alias counts as a C# sample' -Passed ($result.ExitCode -eq 0) -Message $result.Output

# The gate ships with the scan pointed at the repository, and that run must stay green.
$scriptPath = Join-Path $PSScriptRoot '..' 'extract-code-samples.js'
$previous = $env:CODE_SAMPLES_ROOT
try {
  Remove-Item Env:\CODE_SAMPLES_ROOT -ErrorAction SilentlyContinue
  $output = & node $scriptPath --extract-only 2>&1
  $exitCode = $LASTEXITCODE
}
finally {
  if ($null -ne $previous) {
    $env:CODE_SAMPLES_ROOT = $previous
  }
}
Write-TestResult -TestName 'The default repository scan still passes' -Passed ($exitCode -eq 0) -Message ($output | Out-String)

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
