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

function Invoke-Gate {
  param([string]$ScriptPath, [string]$Root, [switch]$Cli)

  if ($Cli) {
    Push-Location $Root
    try {
      $output = & pwsh -NoProfile -File $ScriptPath *>&1
      return @{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
    }
    finally { Pop-Location }
  }

  $runner = [System.Management.Automation.PowerShell]::Create()
  try {
    $null = $runner.AddCommand('Set-Location').AddParameter('LiteralPath', $Root)
    $null = $runner.AddStatement().AddCommand($ScriptPath)
    $result = $runner.Invoke()
    $output = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $result) { $output.Add($item.ToString()) }
    foreach ($item in $runner.Streams.Information) { $output.Add($item.MessageData.ToString()) }
    foreach ($item in $runner.Streams.Warning) { $output.Add($item.ToString()) }
    foreach ($item in $runner.Streams.Error) { $output.Add($item.ToString()) }
    if ($runner.Streams.Error.Count -gt 0) {
      throw "Gate emitted a PowerShell error: $($output -join [Environment]::NewLine)"
    }
    $exitCode = $runner.Runspace.SessionStateProxy.GetVariable('LASTEXITCODE')
    if ($null -eq $exitCode) {
      throw "Gate returned without an exit status. $($output -join [Environment]::NewLine)"
    }
    return @{ ExitCode = [int]$exitCode; Output = ($output -join [Environment]::NewLine) }
  }
  finally { $runner.Dispose() }
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

    $result = Invoke-Gate -ScriptPath $scriptPath -Root $scratchRoot
    $message = if ($gate.Name -eq 'lint-duplicate-usings') { 'scan found no C# files' } else { 'root not found' }
    Write-TestResult "$($gate.Name) fails when its corpus is absent" `
      ($result.ExitCode -eq 1 -and $result.Output.Contains($message)) `
      "Expected exit 1 and '$message' with no Runtime/Editor/Tests present. Exit: $($result.ExitCode). Output: $($result.Output)"
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
  Write-TestResult 'audit-license-years fails when no .cs files are audited' ($exitCode -eq 1 -and ($output | Out-String).Contains('no .cs files were audited')) "Expected exit 1 with an empty corpus. Exit: $exitCode. Output: $($output | Out-String)"

  # dependabot's config is a single file rather than a tree, so it gets its own shape: deleting the
  # config used to skip the schema check and report success.
  $dependabotScript = Join-Path $scratchRoot 'scripts/lint-dependabot.ps1'
  $result = Invoke-Gate -ScriptPath $dependabotScript -Root $scratchRoot
  Write-TestResult 'lint-dependabot fails when the config is absent' `
    ($result.ExitCode -eq 1 -and $result.Output.Contains('dependabot.yml not found')) `
    "Expected exit 1 for the absent config. Exit: $($result.ExitCode). Output: $($result.Output)"

  $cliResult = Invoke-Gate -ScriptPath $dependabotScript -Root $scratchRoot -Cli
  Write-TestResult 'real CLI preserves the missing-config diagnostic and exit' `
    ($cliResult.ExitCode -eq 1 -and $cliResult.Output.Contains('dependabot.yml not found'))

  $control = Join-Path $scratchRoot 'control.ps1'
  foreach ($status in @(0, 1, 7)) {
    Set-Content -LiteralPath $control -Value "Write-Host 'control-$status'; exit $status"
    $result = Invoke-Gate -ScriptPath $control -Root $scratchRoot
    Write-TestResult "runspace preserves exit $status and output" `
      ($result.ExitCode -eq $status -and $result.Output.Contains("control-$status"))
  }
  $failures = @(
    @{ Name = 'missing exit status'; Content = "Write-Host 'no-status'"; Message = 'without an exit status' },
    @{ Name = 'PowerShell error'; Content = "Write-Error 'control-error'; exit 1"; Message = 'PowerShell error' },
    @{ Name = 'terminating error'; Content = "throw 'control-throw'"; Message = 'control-throw' }
  )
  foreach ($failure in $failures) {
    Set-Content -LiteralPath $control -Value $failure.Content
    $rejected = $false
    try { $null = Invoke-Gate -ScriptPath $control -Root $scratchRoot }
    catch { $rejected = $_.Exception.Message.Contains($failure.Message) }
    Write-TestResult "runspace rejects $($failure.Name)" $rejected
  }
  Set-Content -LiteralPath $control -Value "`$global:GateControl = 1; exit 0"
  $null = Invoke-Gate -ScriptPath $control -Root $scratchRoot
  Set-Content -LiteralPath $control -Value "if (Get-Variable GateControl -Scope Global -ErrorAction SilentlyContinue) { exit 1 }; exit 0"
  $result = Invoke-Gate -ScriptPath $control -Root $scratchRoot
  Write-TestResult 'runspace isolates global state' ($result.ExitCode -eq 0)
  Set-Content -LiteralPath $control -Value "& git --version | Out-Null"
  $result = Invoke-Gate -ScriptPath $control -Root $scratchRoot
  $cliResult = Invoke-Gate -ScriptPath $control -Root $scratchRoot -Cli
  Write-TestResult 'native implicit success matches the CLI exit status' `
    ($result.ExitCode -eq 0 -and $cliResult.ExitCode -eq 0)


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
