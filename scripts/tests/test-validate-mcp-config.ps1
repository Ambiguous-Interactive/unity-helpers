Param(
  [switch]$VerboseOutput
)

<#
.SYNOPSIS
    Test runner for validate-mcp-config.ps1

.DESCRIPTION
    Verifies that validate-mcp-config.ps1 correctly:
    - Passes a clean fixture (all configs gitignored, valid, doc refs resolve)
    - Detects UNH-MCP-TRACKED (a machine-local config not matched by .gitignore)
    - Detects UNH-MCP-INVALID (a config URL not ending in /mcp)
    - Detects UNH-MCP-MISSINGREF (a doc referencing a nonexistent helper script)
    - Passes against the real repository (regression smoke test)

.PARAMETER VerboseOutput
    Show detailed output during test execution.

.EXAMPLE
    ./scripts/tests/test-validate-mcp-config.ps1
    ./scripts/tests/test-validate-mcp-config.ps1 -VerboseOutput
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$validator = Join-Path $repoRoot 'scripts/validate-mcp-config.ps1'

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[test-validate-mcp-config] $msg" -ForegroundColor Cyan }
}

function Write-TestResult {
  param([string]$TestName, [bool]$Passed, [string]$Message = '')
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

# Creates a temporary git repo fixture and returns its path. $Files is a hashtable
# of repo-relative path -> file content. A .gitignore is always written from
# $GitIgnore.
function New-McpFixture {
  param(
    [hashtable]$Files = @{},
    [string]$GitIgnore = ''
  )
  $dir = Join-Path ([System.IO.Path]::GetTempPath()) ("mcp-fixture-" + [guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Force -Path $dir | Out-Null
  Push-Location $dir
  try {
    git init -q 2>$null | Out-Null
    git config user.email 'test@example.com' 2>$null | Out-Null
    git config user.name 'test' 2>$null | Out-Null
  }
  finally {
    Pop-Location
  }
  Set-Content -LiteralPath (Join-Path $dir '.gitignore') -Value $GitIgnore -NoNewline
  foreach ($rel in $Files.Keys) {
    $full = Join-Path $dir $rel
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $full) | Out-Null
    Set-Content -LiteralPath $full -Value $Files[$rel] -NoNewline
  }
  return $dir
}

function Invoke-Validator {
  param([string]$FixtureRoot)
  $out = & pwsh -NoProfile -File $validator -RepoRoot $FixtureRoot 2>&1
  return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($out -join "`n") }
}

# Gitignore that covers all four machine-local config paths (mirrors the real repo).
$cleanGitIgnore = @"
.mcp.json
.cursor/mcp.json
.vscode/**
.codex/*
"@

$validMcpJson = '{ "mcpServers": { "unity-mcp-remote": { "type": "http", "url": "http://192.168.1.33:9003/mcp" } } }'
# Reference-free README so fixtures exercise Check 1/Check 2 without tripping the
# Check 3 doc-reference rule (the real-repo smoke test covers Check 3's happy path).
$readmeOk = "See the local MCP setup guide for configuration steps."
$readmeMissing = "Run ``scripts/mcp/install-claude-desktop-config.sh`` to set up."
$configureScript = "#!/usr/bin/env bash`necho configure"

Write-Host 'Testing validate-mcp-config.ps1...' -ForegroundColor White

if (-not (Test-Path -LiteralPath $validator)) {
  Write-Host "Validator not found at $validator" -ForegroundColor Red
  exit 1
}

# --- Test 1: clean fixture passes ---
$f1 = New-McpFixture -GitIgnore $cleanGitIgnore -Files @{
  '.mcp.json'                                = $validMcpJson
  'scripts/mcp/README.md'                    = $readmeOk
  'scripts/mcp/configure-unity-mcp-endpoint.sh' = $configureScript
}
try {
  $r1 = Invoke-Validator -FixtureRoot $f1
  Write-Info $r1.Output
  Write-TestResult 'Clean fixture passes (exit 0)' ($r1.ExitCode -eq 0) $r1.Output
}
finally { Remove-Item -Recurse -Force -LiteralPath $f1 -ErrorAction SilentlyContinue }

# --- Test 2: untracked config -> UNH-MCP-TRACKED ---
$f2 = New-McpFixture -GitIgnore ".cursor/mcp.json`n.vscode/**`n.codex/*" -Files @{
  '.mcp.json'             = $validMcpJson
  'scripts/mcp/README.md' = $readmeOk
}
try {
  $r2 = Invoke-Validator -FixtureRoot $f2
  Write-TestResult 'Untracked .mcp.json -> UNH-MCP-TRACKED' (($r2.ExitCode -ne 0) -and ($r2.Output -match 'UNH-MCP-TRACKED')) $r2.Output
}
finally { Remove-Item -Recurse -Force -LiteralPath $f2 -ErrorAction SilentlyContinue }

# --- Test 3: invalid URL -> UNH-MCP-INVALID ---
$f3 = New-McpFixture -GitIgnore $cleanGitIgnore -Files @{
  '.mcp.json'             = '{ "mcpServers": { "unity-mcp-remote": { "type": "http", "url": "http://192.168.1.33:9003/wrong" } } }'
  'scripts/mcp/README.md' = $readmeOk
}
try {
  $r3 = Invoke-Validator -FixtureRoot $f3
  Write-TestResult 'Bad URL -> UNH-MCP-INVALID' (($r3.ExitCode -ne 0) -and ($r3.Output -match 'UNH-MCP-INVALID')) $r3.Output
}
finally { Remove-Item -Recurse -Force -LiteralPath $f3 -ErrorAction SilentlyContinue }

# --- Test 4: dangling doc reference -> UNH-MCP-MISSINGREF ---
$f4 = New-McpFixture -GitIgnore $cleanGitIgnore -Files @{
  '.mcp.json'             = $validMcpJson
  'scripts/mcp/README.md' = $readmeMissing
}
try {
  $r4 = Invoke-Validator -FixtureRoot $f4
  Write-TestResult 'Missing helper script -> UNH-MCP-MISSINGREF' (($r4.ExitCode -ne 0) -and ($r4.Output -match 'UNH-MCP-MISSINGREF')) $r4.Output
}
finally { Remove-Item -Recurse -Force -LiteralPath $f4 -ErrorAction SilentlyContinue }

# --- Test 5: valid Codex TOML passes ---
$validToml = "[mcp_servers.unity_mcp_remote]`nurl = `"http://192.168.1.33:9003/mcp`"`nstartup_timeout_sec = 20`n"
$f5 = New-McpFixture -GitIgnore $cleanGitIgnore -Files @{
  '.mcp.json'             = $validMcpJson
  '.codex/config.toml'    = $validToml
  'scripts/mcp/README.md' = $readmeOk
}
try {
  $r5 = Invoke-Validator -FixtureRoot $f5
  Write-TestResult 'Valid Codex TOML passes (exit 0)' ($r5.ExitCode -eq 0) $r5.Output
}
finally { Remove-Item -Recurse -Force -LiteralPath $f5 -ErrorAction SilentlyContinue }

# --- Test 6: bad Codex TOML url -> UNH-MCP-INVALID ---
$badToml = "[mcp_servers.unity_mcp_remote]`nurl = `"http://192.168.1.33:9003/wrong`"`n`n[other]`nurl = `"http://x/mcp`"`n"
$f6 = New-McpFixture -GitIgnore $cleanGitIgnore -Files @{
  '.mcp.json'             = $validMcpJson
  '.codex/config.toml'    = $badToml
  'scripts/mcp/README.md' = $readmeOk
}
try {
  $r6 = Invoke-Validator -FixtureRoot $f6
  Write-TestResult 'Bad Codex TOML url (with /mcp in another section) -> UNH-MCP-INVALID' (($r6.ExitCode -ne 0) -and ($r6.Output -match 'UNH-MCP-INVALID')) $r6.Output
}
finally { Remove-Item -Recurse -Force -LiteralPath $f6 -ErrorAction SilentlyContinue }

# --- Test 7: regression smoke test against the real repo ---
$r7 = Invoke-Validator -FixtureRoot $repoRoot
Write-TestResult 'Real repository passes (exit 0)' ($r7.ExitCode -eq 0) $r7.Output

Write-Host ''
Write-Host "Passed: $script:TestsPassed  Failed: $script:TestsFailed" -ForegroundColor White
if ($script:TestsFailed -gt 0) {
  Write-Host 'Failed tests:' -ForegroundColor Red
  foreach ($t in $script:FailedTests) { Write-Host "  - $t" -ForegroundColor Red }
  exit 1
}
Write-Host 'All validate-mcp-config tests passed.' -ForegroundColor Green
