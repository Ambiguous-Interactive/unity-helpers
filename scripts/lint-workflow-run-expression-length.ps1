#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fail when a GitHub Actions `run:` block both contains a `${{ }}` expression
    AND exceeds the per-expression template-length limit.

.DESCRIPTION
    GitHub compiles ANY `run:` block that contains even one `${{ }}` interpolation
    as a SINGLE template expression. There is a hard limit of 21,000 characters
    per template expression. A `run:` block body that interleaves a large script
    with inline `${{ inputs.* }}` / `${{ github.* }}` references is therefore
    compiled as one oversized expression, and once the body crosses 21,000
    characters the workflow fails to PARSE with:

        The template is not valid ... Exceeded max expression length 21000

    This is a cryptic, queue-time failure: on a self-hosted runner it surfaces
    only after the job has been provisioned (~30 min), and it takes down every
    leg of the matrix at once. The real-world instance that motivated this lint
    was `.github/actions/verify-unity-results/action.yml`, whose ~26 KB `run:`
    block still carried inline `${{ inputs.* }}` expressions.

    The fix is to move the interpolated values into an `env:` mapping and read
    them via `$env:NAME` (or `process.env.NAME`) so the `run:` body contains no
    `${{ }}` and is NOT compiled as a template (no length limit then applies).

    This lint scans every workflow AND every composite action, extracts each
    `run:` block body, measures its character length, and FAILS (exit 1) when a
    block BOTH contains the literal `${{` AND its length exceeds a safe
    threshold of 20,000 characters (a deliberate margin below GitHub's hard
    21,000 limit). The existing pwsh-invocations lint does NOT scan composite
    actions under .github/actions/**, so this lint covers them explicitly.

    Error code emitted:
      WFL001 - A `run:` block contains `${{` and its body length exceeds the
               20,000-character safe threshold (GitHub's hard limit is 21,000
               characters per template expression). Move the interpolated
               inputs into an `env:` mapping and read them via `$env:NAME`
               (or `process.env.NAME`) so the run body contains no `${{ }}`
               and is not compiled as a template.

    Scanned paths:
      - .github/workflows/*.yml
      - .github/workflows/*.yaml
      - .github/actions/**/action.yml

    Run-block forms handled:
      - Literal block scalar:  `run: |`   (body = lines indented deeper than the
                               `run:` key).
      - Folded block scalar:   `run: >`   (same indentation-based body capture).
      - Block-scalar step form: `- run: |` / `- run: >` (the `run:` key may be
                               prefixed by a `- ` sequence indicator).
      - Inline single-line:    `run: <command>` (body = the remainder of the
                               line; rarely large, but measured for completeness).

    A `run:` token that appears inside a quoted YAML string (e.g.
    `description: "the run: key does X"`) is NOT a block start; we only treat a
    line as a run block when `run:` is the mapping KEY (optionally preceded by a
    `- ` sequence marker) and is followed by a block-scalar indicator or an
    unquoted inline value.

.PARAMETER VerboseOutput
    Show per-file diagnostics including files scanned with no violations and the
    measured length of every `run:` block.

.EXAMPLE
    ./scripts/lint-workflow-run-expression-length.ps1
    Lint every workflow and composite action in the repo.

.EXAMPLE
    ./scripts/lint-workflow-run-expression-length.ps1 -VerboseOutput
    Lint with verbose per-file / per-block output.
#>
param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# GitHub's hard per-expression limit is 21,000 characters. We fail at a margin
# below it so a borderline block is corrected BEFORE it can intermittently
# break depending on the exact runtime-substituted length of its expressions.
$script:MaxRunExpressionLength = 20000

function Write-Info($msg) {
    if ($VerboseOutput) { Write-Host "[lint-workflow-run-expression-length] $msg" -ForegroundColor Cyan }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$selfRel = 'scripts/lint-workflow-run-expression-length.ps1'
$selfTestRel = 'scripts/tests/test-lint-workflow-run-expression-length.ps1'

function Get-RepoRelativePath {
    param([string]$FullPath)
    $normalized = $FullPath.Replace('\', '/')
    $root = $repoRoot.Replace('\', '/')
    if ($normalized.StartsWith($root + '/')) {
        return $normalized.Substring($root.Length + 1)
    }
    return $normalized
}

function Get-TargetFiles {
    $results = [System.Collections.Generic.List[string]]::new()

    # .github/workflows/*.yml and *.yaml (non-recursive — GitHub only honors
    # workflow files at the top level of .github/workflows).
    $wfDir = Join-Path (Join-Path $repoRoot '.github') 'workflows'
    if (Test-Path $wfDir) {
        Get-ChildItem -Path $wfDir -File -Filter '*.yml' -ErrorAction SilentlyContinue |
            ForEach-Object { $results.Add($_.FullName) | Out-Null }
        Get-ChildItem -Path $wfDir -File -Filter '*.yaml' -ErrorAction SilentlyContinue |
            ForEach-Object { $results.Add($_.FullName) | Out-Null }
    }

    # .github/actions/**/action.yml (recursive — composite actions live in
    # nested directories). GitHub recognizes both action.yml and action.yaml.
    $actionsDir = Join-Path (Join-Path $repoRoot '.github') 'actions'
    if (Test-Path $actionsDir) {
        Get-ChildItem -Path $actionsDir -File -Recurse -Filter 'action.yml' -ErrorAction SilentlyContinue |
            ForEach-Object { $results.Add($_.FullName) | Out-Null }
        Get-ChildItem -Path $actionsDir -File -Recurse -Filter 'action.yaml' -ErrorAction SilentlyContinue |
            ForEach-Object { $results.Add($_.FullName) | Out-Null }
    }

    return $results | Sort-Object -Unique
}

# Extract every `run:` block from a YAML file's lines and return a list of
# objects: @{ StartLine = <1-based>; Body = <string> }.
#
# Indentation rules (mirrors the YAML block-scalar contract):
#   - A run block starts on a line matching `^(\s*)(?:-\s+)?run\s*:` where the
#     `run:` token is the mapping key. The `- ` sequence indicator counts toward
#     the key's effective indentation for child-body capture: under
#     `- run: |`, the block scalar body must be indented deeper than the column
#     where `run` itself sits.
#   - Block scalar (`|` or `>`, with optional chomping/indent indicators like
#     `|-`, `>+`, `|2`): the body is every subsequent line that is either blank
#     OR indented strictly deeper than the run KEY's own indentation. Capture
#     stops at the first non-blank line indented at or shallower than the key.
#   - Inline form (`run: some command` with no block indicator): the body is the
#     remainder of that single line after `run:`.
#
# We compute the run key's indentation as the count of leading spaces up to and
# including the `- ` marker's payload — i.e. the column of the `r` in `run`.
# YAML requires block-scalar content to be more indented than that column.
function Get-RunBlocks {
    param([string[]]$Lines)

    $blocks = [System.Collections.Generic.List[object]]::new()
    $i = 0
    while ($i -lt $Lines.Length) {
        $line = $Lines[$i]

        # Match a run: mapping key, optionally preceded by a `- ` sequence
        # indicator. Capture the leading whitespace, the optional dash marker,
        # and the value that follows the colon.
        $m = [regex]::Match($line, '^(?<lead>\s*)(?<dash>-\s+)?run\s*:(?<rest>.*)$')
        if (-not $m.Success) {
            $i++
            continue
        }

        # Column of the `r` in `run` = leading spaces + width of the `- ` marker.
        $keyIndent = $m.Groups['lead'].Value.Length + $m.Groups['dash'].Value.Length
        $rest = $m.Groups['rest'].Value
        $startLine = $i + 1

        # Determine whether this is a block scalar (| or >) or an inline value.
        # Trim only leading spaces from the value to inspect the indicator; the
        # indicator may carry chomping (+/-) and an explicit indent digit.
        $restTrimmed = $rest.TrimStart()
        $scalarMatch = [regex]::Match($restTrimmed, '^[|>][+-]?[0-9]?\s*(#.*)?$')

        if ($scalarMatch.Success) {
            # Block scalar: gather the indented body.
            $bodyLines = [System.Collections.Generic.List[string]]::new()
            $j = $i + 1
            while ($j -lt $Lines.Length) {
                $next = $Lines[$j]
                if ($next -match '^\s*$') {
                    # Blank lines belong to the scalar; preserve them.
                    $bodyLines.Add('') | Out-Null
                    $j++
                    continue
                }
                $nextIndent = ($next -replace '^(\s*).*$', '$1').Length
                if ($nextIndent -le $keyIndent) {
                    break
                }
                $bodyLines.Add($next) | Out-Null
                $j++
            }

            # Trim trailing blank lines (they are not meaningful body content
            # and YAML would strip them under default chomping). Join with `\n`
            # to reconstruct the body length GitHub would compile.
            while ($bodyLines.Count -gt 0 -and [string]::IsNullOrEmpty($bodyLines[$bodyLines.Count - 1])) {
                $bodyLines.RemoveAt($bodyLines.Count - 1)
            }
            $body = ($bodyLines -join "`n")

            $blocks.Add(@{ StartLine = $startLine; Body = $body }) | Out-Null
            $i = $j
            continue
        }

        # Inline form: the body is the remainder of the line after `run:`.
        # Strip a trailing comment only when the value is unquoted; for our
        # purposes (length + presence of `${{`) the raw remainder is fine.
        $body = $rest.Trim()
        $blocks.Add(@{ StartLine = $startLine; Body = $body }) | Out-Null
        $i++
    }

    return , $blocks
}

$targets = @(Get-TargetFiles)
Write-Info "Scanning $($targets.Count) workflow/action file(s)"

$violations = [System.Collections.Generic.List[object]]::new()

foreach ($file in $targets) {
    $rel = Get-RepoRelativePath $file

    # Exclude this script and its own test (the test builds fixture STRINGS that
    # intentionally contain the offending pattern). Neither is a YAML target in
    # practice, but guard defensively to mirror the sibling pwsh lint.
    if ($rel -eq $selfRel) { continue }
    if ($rel -eq $selfTestRel) { continue }

    $lines = @()
    try {
        # Coerce to an array so an empty file (Get-Content returns $null) does
        # not crash under StrictMode on .Length, and a single-line file with no
        # trailing newline (returns a scalar string) iterates as one line rather
        # than per-character.
        $lines = @(Get-Content -LiteralPath $file -ErrorAction Stop)
    } catch {
        Write-Info "Skipping unreadable file: $rel"
        continue
    }

    $blocks = Get-RunBlocks -Lines $lines
    foreach ($block in $blocks) {
        $body = [string]$block.Body
        $length = $body.Length
        $hasExpression = $body.Contains('${{')
        Write-Info ("  {0}: run block at line {1} -> length={2}, hasExpression={3}" -f $rel, $block.StartLine, $length, $hasExpression)

        if ($hasExpression -and $length -gt $script:MaxRunExpressionLength) {
            # Build the message via single-quoted literals + concatenation so the
            # literal '${{ }}' token and the '$env:NAME' hint are emitted verbatim
            # (a double-quoted string would treat '$' and backticks specially).
            $message = 'run: block contains a ' + '${{ }}' + ' expression and is ' + $length + ' characters long, exceeding the ' + $script:MaxRunExpressionLength + '-character safe threshold (GitHub''s hard limit is 21000 characters per template expression). A run: block that contains any ' + '${{ }}' + ' is compiled as a single template expression; once it crosses the limit the workflow fails to parse with ''Exceeded max expression length 21000''. Fix: move the interpolated inputs into an env: mapping and read them via $env:NAME (or process.env.NAME) so the run body contains no ' + '${{ }}' + ' and is not compiled as a template.'
            $violations.Add(@{
                Path = $rel
                Line = $block.StartLine
                Code = 'WFL001'
                Length = $length
                Message = $message
            }) | Out-Null
        }
    }
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) {
        Write-Host ("{0}:{1}: {2} {3}" -f $v.Path, $v.Line, $v.Code, $v.Message) -ForegroundColor Red
    }
    Write-Host ""
    Write-Host ("[lint-workflow-run-expression-length] {0} violation(s) found." -f $violations.Count) -ForegroundColor Red
    exit 1
}

if ($VerboseOutput) {
    Write-Host "[lint-workflow-run-expression-length] OK: No oversized run: blocks containing `${{ }}` detected." -ForegroundColor Green
}
exit 0
