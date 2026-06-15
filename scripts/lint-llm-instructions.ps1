<#
.SYNOPSIS
    Validates LLM instruction files (.llm/) are correct and up-to-date.

.DESCRIPTION
    Validates:
    1. Every authored skill file (.llm/skills/*.md except the generated index.md)
       has a trigger comment.
    2. Every trigger comment is ASCII-only (a stray em-dash / smart quote is the
       exact cross-OS drift that previously broke CI - see serialization-safety).
    3. The generated skills index .llm/skills/index.md is up-to-date (matches the
       generator output) AND is byte-stable: UTF-8 without BOM, LF line endings.
    4. The generator is deterministic (two runs produce identical bytes).
    5. .llm/context.md links to ./skills/index.md, carries no stale embedded
       BEGIN/END index markers, and still has exactly one H1.

.PARAMETER Fix
    Regenerate .llm/skills/index.md to fix an out-of-date index.

.PARAMETER VerboseOutput
    Emit detailed progress.

.EXAMPLE
    pwsh -NoProfile -File scripts/lint-llm-instructions.ps1
    pwsh -NoProfile -File scripts/lint-llm-instructions.ps1 -Fix
#>
# lint-pwsh-invocations: allow-subprocess-pwsh generate-skills-index.ps1 is invoked
# as an isolated child writing to a temp -OutputPath; file-vs-file comparison avoids
# stdout-encoding nondeterminism and preserves the child's `exit` semantics.
Param(
    [switch]$Fix,
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info($msg) {
    if ($VerboseOutput) { Write-Host "[llm-lint] $msg" -ForegroundColor Cyan }
}

function Write-WarningMsg($msg) {
    Write-Host "[llm-lint] WARNING: $msg" -ForegroundColor Yellow
}

function Write-ErrorMsg($msg) {
    Write-Host "[llm-lint] ERROR: $msg" -ForegroundColor Red
}

function Write-SuccessMsg($msg) {
    Write-Host "[llm-lint] $msg" -ForegroundColor Green
}

$markdownHelpersPath = Join-Path -Path $PSScriptRoot -ChildPath 'markdown-helpers.ps1'
. $markdownHelpersPath

$repoRoot = (Get-Item $PSScriptRoot).Parent.FullName
$skillsDir = Join-Path -Path $repoRoot -ChildPath '.llm/skills'
$contextFile = Join-Path -Path $repoRoot -ChildPath '.llm/context.md'
$generateScript = Join-Path -Path $repoRoot -ChildPath 'scripts/generate-skills-index.ps1'
$indexFileName = 'index.md'
$indexFile = Join-Path -Path $skillsDir -ChildPath $indexFileName

$exitCode = 0

# =============================================================================
# 1. Required paths exist
# =============================================================================
Write-Info "Checking skills directory and context.md..."
foreach ($required in @(
        @{ Path = $skillsDir; Label = 'Skills directory' }
        @{ Path = $contextFile; Label = 'context.md' }
        @{ Path = $generateScript; Label = 'generate-skills-index.ps1' }
    )) {
    if (-not (Test-Path -LiteralPath $required.Path)) {
        Write-ErrorMsg "$($required.Label) not found at: $($required.Path)"
        exit 1
    }
}

# Authored skill files: every *.md EXCEPT the generated index.
$skillFiles = Get-ChildItem -LiteralPath $skillsDir -Filter '*.md' |
    Where-Object { $_.Name -ne $indexFileName } |
    Sort-Object Name

# =============================================================================
# 2. Trigger comments present + ASCII-only
# =============================================================================
Write-Host ""
Write-Host "Validating skill trigger comments..." -ForegroundColor Blue

# Capture the whole single-line trigger comment (non-greedy up to the first -->),
# so a description containing '>' is captured rather than mis-reported as missing.
$triggerPattern = '<!--\s*trigger:\s*(.+?)\s*-->'
$validCategories = @('Core', 'Performance', 'Feature')
$missingTriggers = @()
$nonAsciiTriggers = @()
$malformedTriggers = @()

foreach ($file in $skillFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    $match = [regex]::Match($content, $triggerPattern)
    if (-not $match.Success) {
        $missingTriggers += $file.Name
        continue
    }

    # Non-ASCII (em-dash, en-dash, smart quotes, ellipsis, ...) in a trigger is the
    # cross-OS drift class; flag the exact offending characters.
    $badChars = [regex]::Matches($match.Value, '[^\x00-\x7F]')
    if ($badChars.Count -gt 0) {
        $shown = ($badChars | ForEach-Object { $_.Value } | Select-Object -Unique) -join ' '
        $nonAsciiTriggers += "$($file.Name): non-ASCII character(s) [$shown]"
    }

    # Structure: keywords | description [| category]. Enforce 2-3 '|'-separated
    # fields (a literal '|' in the description would silently corrupt the generated
    # row) and a KNOWN category (a typo like 'Core Skills' must not fall through to
    # the Feature default unnoticed).
    $fields = $match.Groups[1].Value -split '\|'
    if ($fields.Count -lt 2 -or $fields.Count -gt 3) {
        $malformedTriggers += "$($file.Name): trigger must have 2 or 3 '|'-separated fields (found $($fields.Count)); a literal '|' in the description is not allowed"
    }
    elseif ($fields.Count -eq 3 -and ($fields[2].Trim() -notin $validCategories)) {
        $malformedTriggers += "$($file.Name): unknown category '$($fields[2].Trim())' (expected: $($validCategories -join ', '))"
    }
}

if ($missingTriggers.Count -gt 0) {
    Write-ErrorMsg "The following skill files are missing trigger comments:"
    foreach ($file in $missingTriggers) { Write-Host "  - $file" -ForegroundColor Red }
    Write-Host "Required format: <!-- trigger: keyword1, keyword2 | Description | Category -->" -ForegroundColor Yellow
    Write-Host "Categories: Core, Performance, Feature (default: Feature)" -ForegroundColor Yellow
    $exitCode = 1
}

if ($nonAsciiTriggers.Count -gt 0) {
    Write-ErrorMsg "Trigger comments must be ASCII-only (use '-' not em-dash, straight quotes):"
    foreach ($t in $nonAsciiTriggers) { Write-Host "  - $t" -ForegroundColor Red }
    Write-Host "Non-ASCII descriptions cause cross-OS index drift (the failure this lint prevents)." -ForegroundColor Yellow
    $exitCode = 1
}

if ($malformedTriggers.Count -gt 0) {
    Write-ErrorMsg "Malformed trigger comments:"
    foreach ($t in $malformedTriggers) { Write-Host "  - $t" -ForegroundColor Red }
    Write-Host "Required format: <!-- trigger: keyword1, keyword2 | Description | Category -->" -ForegroundColor Yellow
    $exitCode = 1
}

if ($missingTriggers.Count -eq 0 -and $nonAsciiTriggers.Count -eq 0 -and $malformedTriggers.Count -eq 0) {
    Write-SuccessMsg "All $($skillFiles.Count) skill files have valid ASCII trigger comments"
}

# =============================================================================
# 3 & 4. Index is up-to-date + deterministic
# =============================================================================
Write-Host ""
Write-Host "Validating skills index ($indexFileName)..." -ForegroundColor Blue

# Read CRLF->LF-normalized text for cross-platform-safe content comparison.
function Get-NormalizedText {
    param([Parameter(Mandatory = $true)][string]$Path)
    return ([System.IO.File]::ReadAllText($Path) -replace "`r`n", "`n")
}

$tempA = [System.IO.Path]::GetTempFileName()
$tempB = [System.IO.Path]::GetTempFileName()
try {
    & pwsh -NoProfile -File $generateScript -OutputPath $tempA | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-ErrorMsg "Generator failed (exit $LASTEXITCODE) writing expected index."
        exit 1
    }
    & pwsh -NoProfile -File $generateScript -OutputPath $tempB | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-ErrorMsg "Generator failed (exit $LASTEXITCODE) on determinism re-run."
        exit 1
    }

    # 4. Determinism: two independent runs must be byte-identical.
    $bytesA = [System.IO.File]::ReadAllBytes($tempA)
    $bytesB = [System.IO.File]::ReadAllBytes($tempB)
    $deterministic = ($bytesA.Length -eq $bytesB.Length)
    if ($deterministic) {
        for ($i = 0; $i -lt $bytesA.Length; $i++) {
            if ($bytesA[$i] -ne $bytesB[$i]) { $deterministic = $false; break }
        }
    }
    if (-not $deterministic) {
        Write-ErrorMsg "Generator is NON-deterministic: two runs produced different bytes."
        $exitCode = 1
    }
    else {
        Write-Info "Generator is deterministic (identical bytes across two runs)."
    }

    $expected = Get-NormalizedText -Path $tempA

    if (-not (Test-Path -LiteralPath $indexFile)) {
        if ($Fix) {
            & pwsh -NoProfile -File $generateScript | Out-Null
            Write-SuccessMsg "Generated missing $indexFileName"
        }
        else {
            Write-ErrorMsg "$indexFileName does not exist. Run: pwsh -NoProfile -File scripts/generate-skills-index.ps1"
            exit 1
        }
    }

    $current = Get-NormalizedText -Path $indexFile

    if (-not [string]::Equals($expected, $current, [System.StringComparison]::Ordinal)) {
        if ($Fix) {
            & pwsh -NoProfile -File $generateScript | Out-Null
            if ($LASTEXITCODE -ne 0) {
                Write-ErrorMsg "Generator failed during -Fix (exit $LASTEXITCODE)."
                exit 1
            }
            Write-SuccessMsg "Regenerated $indexFileName"
            $current = Get-NormalizedText -Path $indexFile
        }
        else {
            Write-ErrorMsg "Skills index $indexFileName is out of date!"
            $expectedLines = $expected -split "`n"
            $currentLines = $current -split "`n"
            $maxLines = [Math]::Max($expectedLines.Count, $currentLines.Count)
            $shown = 0
            for ($i = 0; $i -lt $maxLines -and $shown -lt 5; $i++) {
                $exp = if ($i -lt $expectedLines.Count) { $expectedLines[$i] } else { '(missing)' }
                $cur = if ($i -lt $currentLines.Count) { $currentLines[$i] } else { '(missing)' }
                if ($exp -ne $cur) {
                    Write-Host "  Line $($i + 1):" -ForegroundColor Yellow
                    Write-Host "    Expected: $exp" -ForegroundColor Green
                    Write-Host "    Current:  $cur" -ForegroundColor Red
                    $shown++
                }
            }
            Write-Host "Run: pwsh -NoProfile -File scripts/generate-skills-index.ps1" -ForegroundColor Cyan
            $exitCode = 1
        }
    }
    else {
        Write-SuccessMsg "Skills index is up to date"
    }
}
finally {
    Remove-Item -LiteralPath $tempA, $tempB -Force -ErrorAction SilentlyContinue
}

# =============================================================================
# 5. Encoding contract on the committed index: UTF-8 no BOM, LF only
# =============================================================================
if (Test-Path -LiteralPath $indexFile) {
    $indexBytes = [System.IO.File]::ReadAllBytes($indexFile)
    $hasBom = $indexBytes.Length -ge 3 -and $indexBytes[0] -eq 0xEF -and $indexBytes[1] -eq 0xBB -and $indexBytes[2] -eq 0xBF
    $hasCr = $indexBytes -contains 0x0D
    if ($hasBom) {
        Write-ErrorMsg "$indexFileName has a UTF-8 BOM; it must be written without a BOM (cross-OS stability)."
        $exitCode = 1
    }
    if ($hasCr) {
        Write-ErrorMsg "$indexFileName contains CR (CRLF); it must use LF line endings."
        $exitCode = 1
    }
    if (-not $hasBom -and -not $hasCr) {
        Write-Info "$indexFileName encoding OK (UTF-8 no BOM, LF)."
    }
}

# =============================================================================
# 6. context.md links to the index, has no stale markers, single H1
# =============================================================================
Write-Host ""
Write-Host "Validating context.md..." -ForegroundColor Blue

$contextContent = Get-Content -LiteralPath $contextFile -Raw

foreach ($staleMarker in @('<!-- BEGIN GENERATED SKILLS INDEX -->', '<!-- END GENERATED SKILLS INDEX -->')) {
    if ($contextContent.Contains($staleMarker)) {
        Write-ErrorMsg "context.md still contains a stale embedded-index marker: $staleMarker"
        Write-Host "The index now lives in .llm/skills/index.md; remove the embedded block." -ForegroundColor Yellow
        $exitCode = 1
    }
}

# Match by URL (not link text) so the doc-link rule's "human-readable text"
# requirement and this check don't conflict.
if ($contextContent -notmatch '\]\(\./skills/index\.md\)') {
    Write-ErrorMsg "context.md must link to the generated index (a link to ./skills/index.md)."
    $exitCode = 1
}

$contextH1Lines = @(Get-MarkdownH1Lines -Lines @(Get-Content -LiteralPath $contextFile))
if ($contextH1Lines.Count -ne 1) {
    Write-ErrorMsg "context.md must have exactly one H1 (found $($contextH1Lines.Count))."
    $contextH1Lines | ForEach-Object { Write-Host "  L$($_.LineNumber): $($_.Text)" -ForegroundColor Red }
    $exitCode = 1
}

if ($exitCode -eq 0) {
    Write-SuccessMsg "context.md links to the index, has no stale markers, single H1"
}

# =============================================================================
# Summary
# =============================================================================
Write-Host ""
Write-Host ("=" * 60)
if ($exitCode -eq 0) {
    Write-SuccessMsg "LLM instructions validation passed!"
}
else {
    Write-ErrorMsg "LLM instructions validation failed!"
}
Write-Host ("=" * 60)

exit $exitCode
