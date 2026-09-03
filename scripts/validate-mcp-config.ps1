Param(
  [string]$RepoRoot,
  [switch]$VerboseOutput
)

<#
.SYNOPSIS
    Validates the repository's machine-local MCP client configuration.

.DESCRIPTION
    The Unity MCP bridge endpoint and generated launcher paths are per-developer,
    so the generated MCP client config files must never be committed and must be
    structurally valid. This linter enforces the invariants below so the setup
    cannot silently rot:

    1. UNH-MCP-TRACKED  - every machine-local MCP client config path is matched by
       .gitignore (it holds a per-developer host:port and must never be committed).
    2. UNH-MCP-INVALID  - any config that IS present is structurally valid (JSON
       configs are parsed; the Codex TOML block is regex-checked). When the
       optional Unity entry is present, its URL ends with `/mcp` (case-sensitive).
    3. UNH-MCP-MISSINGREF - every `scripts/mcp/*.sh|*.ps1|*.mjs` path referenced by the
       MCP docs actually exists on disk (catches the dangling-reference class of
       bug, e.g. a documented helper script that was never copied over).

    4. UNH-MCP-PORT     - the bridge default port is this repository's own (9007) and no
       sibling studio project's port appears in it. A shared port is how a client config
       ends up aimed at another project's editor (issue #333).

    Shared-only config is valid when Unity is unavailable; configure-shared is
    deliberately independent of Unity bridge health. Keep the config list in
    sync with scripts/mcp/unity-mcp.mjs and docs/guides/mcp-local-setup.md.

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

  # Machine-local MCP client config files written by `npm run unity:mcp:configure`.
  # host:port and the bearer token are per-developer, so all of these MUST be
  # gitignored. .vscode/** and .codex/* already cover two of them; .mcp.json
  # (Claude Code AND nanocoder), .cursor/mcp.json, opencode.json and .env.local
  # need explicit entries. .env.local is the SOURCE of the others and holds the
  # token, so leaving it tracked defeats the rest.
  $localConfigs = @('.mcp.json', '.cursor/mcp.json', '.vscode/mcp.json', '.codex/config.toml', 'opencode.json', '.env.local')

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
    'opencode.json'    = 'mcp'
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
    $servers = $json.$serverKey
    $hasSharedServer = $null -ne $servers -and $null -ne $servers.PSObject.Properties['github']
    $url = $null
    try { $url = $json.$serverKey.'unity-mcp-remote'.url } catch { $url = $null }
    if ([string]::IsNullOrWhiteSpace($url)) {
      if (-not $hasSharedServer) {
        $errors.Add("::error file=$path::UNH-MCP-INVALID: missing '$serverKey.unity-mcp-remote.url'.")
      }
      else {
        Write-Info "  shared-only config OK: $path"
      }
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
      if ($toml -notmatch '(?m)^\s*\[mcp_servers\.github\]\s*$') {
        $errors.Add("::error file=.codex/config.toml::UNH-MCP-INVALID: missing [mcp_servers.unity_mcp_remote] block.")
      }
      else {
        Write-Info '  shared-only config OK: .codex/config.toml'
      }
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
  $refRegex = [regex]'(?<![\w/])scripts/mcp/[A-Za-z0-9._/-]+\.(?:sh|ps1|mjs)\b'
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

  # ---- Check 4: the bridge owns this repository's port, not a sibling's ----
  # Every studio project runs its own bridge, and a bridge is bound to one editor.
  # Sharing a port with a sibling is how a client config ends up aimed at another
  # project's Unity while every reachability check reports success (issue #333).
  Write-Info "Check 4: the bridge default port is this repository's own..."
  $bridge = 'scripts/mcp/unity-mcp.mjs'
  $siblingPorts = [ordered]@{
    '9003' = 'DxMessaging'
    '9004' = 'IshoBoy'
    '9010' = 'DoxReloaded'
    '9020' = 'qora-redux'
  }
  if (-not (Test-Path -LiteralPath $bridge)) {
    $errors.Add("::error file=$bridge::UNH-MCP-MISSINGREF: the Unity MCP bridge is missing.")
  }
  else {
    $bridgeText = Get-Content -Raw -LiteralPath $bridge
    if ($bridgeText -notmatch '(?m)^\s*port:\s*9007,') {
      $errors.Add("::error file=$bridge::UNH-MCP-PORT: the bridge default port must be 9007.")
    }
    if ($bridgeText -notmatch '(?m)FALLBACK_PORTS\s*=\s*Object\.freeze\(\[9007\]\)') {
      $errors.Add("::error file=$bridge::UNH-MCP-PORT: discovery must fall back to 9007 only; probing a sibling's port is how a config lands on another project's editor.")
    }
    foreach ($siblingPort in $siblingPorts.Keys) {
      # Named in a comment is fine and useful; used as a value is not.
      if ($bridgeText -match "(?m)^\s*port:\s*$siblingPort," -or $bridgeText -match "FALLBACK_PORTS\s*=\s*Object\.freeze\(\[[^\]]*$siblingPort") {
        $errors.Add("::error file=$bridge::UNH-MCP-PORT: port $siblingPort belongs to $($siblingPorts[$siblingPort]); this repository uses 9007.")
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
