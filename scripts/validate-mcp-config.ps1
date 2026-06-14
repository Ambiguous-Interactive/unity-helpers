Param(
  [string]$RepoRoot,
  [switch]$VerboseOutput
)

<#
.SYNOPSIS
    Validates the repository's machine-local Unity MCP client configuration.

.DESCRIPTION
    The Unity MCP bridge endpoint (host:port) is per-developer, so the generated
    MCP client config files must never be committed, must be structurally valid,
    and must target the `/mcp` streamable-HTTP path. This linter enforces three
    invariants so the MCP setup copied from DxMessaging cannot silently rot:

    1. UNH-MCP-TRACKED  - every machine-local MCP client config path is matched by
       .gitignore (it holds a per-developer host:port and must never be committed).
    2. UNH-MCP-INVALID  - any config that IS present is structurally valid (JSON
       configs are parsed; the Codex TOML block is regex-checked) and its
       `unity-mcp-remote` server URL ends with `/mcp` (case-sensitive).
    3. UNH-MCP-MISSINGREF - every `scripts/mcp/*.sh|*.ps1` path referenced by the
       MCP docs actually exists on disk (catches the dangling-reference class of
       bug, e.g. a documented helper script that was never copied over).

    Keep the config list in sync with scripts/mcp/configure-unity-mcp-endpoint.sh
    and docs/guides/mcp-local-setup.md.

.PARAMETER VerboseOutput
    Show detailed per-check output.

.EXAMPLE
    ./scripts/validate-mcp-config.ps1
    ./scripts/validate-mcp-config.ps1 -VerboseOutput
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[validate-mcp-config] $msg" -ForegroundColor Cyan }
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
  throw 'git is not available on PATH. validate-mcp-config requires git check-ignore.'
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
  $RepoRoot = Join-Path $PSScriptRoot '..'
}
$repoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
Push-Location $repoRoot
try {
  $errors = New-Object System.Collections.Generic.List[string]

  # Machine-local MCP client config files written by
  # scripts/mcp/configure-unity-mcp-endpoint.sh. host:port is per-developer, so
  # all four MUST be gitignored. .vscode/** and .codex/* already cover two of
  # them; .mcp.json and .cursor/mcp.json need explicit entries.
  $localConfigs = @('.mcp.json', '.cursor/mcp.json', '.vscode/mcp.json', '.codex/config.toml')

  # ---- Check 1: every machine-local config path is gitignored ----
  Write-Info 'Check 1: machine-local MCP configs are gitignored...'
  foreach ($cfg in $localConfigs) {
    & git check-ignore --quiet -- $cfg 2>$null
    $checkIgnoreExit = $LASTEXITCODE
    if ($checkIgnoreExit -eq 0) {
      Write-Info "  gitignored OK: $cfg"
    }
    elseif ($checkIgnoreExit -eq 1) {
      # Exit 1 = path is NOT ignored. (Exit 128 = git error, handled below.)
      $errors.Add("::error file=.gitignore::UNH-MCP-TRACKED: '$cfg' is a machine-local MCP config (per-developer host:port) and MUST be gitignored. Add it to .gitignore.")
    }
    else {
      throw "git check-ignore failed for '$cfg' (exit $checkIgnoreExit). Is '$repoRoot' a git repository?"
    }
  }

  # ---- Check 2: present configs are structurally valid and target /mcp ----
  Write-Info 'Check 2: present MCP configs are valid and target /mcp...'
  $jsonConfigs = [ordered]@{
    '.mcp.json'        = 'mcpServers'
    '.cursor/mcp.json' = 'mcpServers'
    '.vscode/mcp.json' = 'servers'
  }
  foreach ($path in $jsonConfigs.Keys) {
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $json = $null
    try {
      $json = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
      $errors.Add("::error file=$path::UNH-MCP-INVALID: not valid JSON ($($_.Exception.Message)).")
      continue
    }
    $serverKey = $jsonConfigs[$path]
    $url = $null
    try { $url = $json.$serverKey.'unity-mcp-remote'.url } catch { $url = $null }
    if ([string]::IsNullOrWhiteSpace($url)) {
      $errors.Add("::error file=$path::UNH-MCP-INVALID: missing '$serverKey.unity-mcp-remote.url'.")
    }
    elseif ($url -cnotmatch '/mcp/?$') {
      # Case-SENSITIVE: the server serves /mcp, not /MCP.
      $errors.Add("::error file=$path::UNH-MCP-INVALID: unity-mcp-remote url '$url' should end with '/mcp'.")
    }
    else {
      Write-Info "  config OK: $path -> $url"
    }
  }

  if (Test-Path -LiteralPath '.codex/config.toml') {
    $toml = Get-Content -Raw -LiteralPath '.codex/config.toml'
    # Isolate the [mcp_servers.unity_mcp_remote] table (until the next table
    # header or EOF) so a `/mcp` url in a DIFFERENT section can't satisfy the
    # check, then drop comment lines so a commented-out url cannot pass either.
    $tomlBlock = [regex]::Match($toml, '(?ms)^\s*\[mcp_servers\.unity_mcp_remote\]\s*(.*?)(?=^\s*\[|\z)')
    if (-not $tomlBlock.Success) {
      $errors.Add("::error file=.codex/config.toml::UNH-MCP-INVALID: missing [mcp_servers.unity_mcp_remote] block.")
    }
    else {
      $tomlBody = (($tomlBlock.Groups[1].Value -split "`n") | Where-Object { $_ -notmatch '^\s*#' }) -join "`n"
      if ($tomlBody -cnotmatch 'url\s*=\s*"[^"]*/mcp"') {
        $errors.Add("::error file=.codex/config.toml::UNH-MCP-INVALID: unity_mcp_remote url must be set and end with '/mcp'.")
      }
      else {
        Write-Info '  config OK: .codex/config.toml'
      }
    }
  }

  # ---- Check 3: every scripts/mcp/* path referenced by the docs exists ----
  Write-Info 'Check 3: MCP doc script references resolve...'
  $docFiles = @('scripts/mcp/README.md', 'docs/guides/mcp-local-setup.md')
  $refRegex = [regex]'(?<![\w/])scripts/mcp/[A-Za-z0-9._/-]+\.(?:sh|ps1)\b'
  foreach ($doc in $docFiles) {
    if (-not (Test-Path -LiteralPath $doc)) { continue }
    $text = Get-Content -Raw -LiteralPath $doc
    $seen = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($m in $refRegex.Matches($text)) {
      $ref = $m.Value
      if (-not $seen.Add($ref)) { continue }
      if (Test-Path -LiteralPath $ref) {
        Write-Info "  reference OK: $ref"
      }
      else {
        $errors.Add("::error file=$doc::UNH-MCP-MISSINGREF: references '$ref' which does not exist. Add the script or remove the reference.")
      }
    }
  }

  if ($errors.Count -gt 0) {
    Write-Host 'MCP config validation FAILED:' -ForegroundColor Red
    foreach ($e in $errors) { Write-Host $e }
    Write-Host ''
    Write-Host 'See docs/guides/mcp-local-setup.md and scripts/mcp/README.md.' -ForegroundColor Cyan
    exit 1
  }

  Write-Host '[validate-mcp-config] OK: MCP client configs are gitignored, valid, and doc references resolve.' -ForegroundColor Green
}
finally {
  Pop-Location
}
