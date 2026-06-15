<#
.SYNOPSIS
    Validates LLM skill file and context file sizes against documented limits.

.DESCRIPTION
    Checks all .llm/skills/**/*.md files (including subdirectories) and .llm/context.md
    against size thresholds:
    - >500 lines: ERROR (MUST split per manage-skills.md)
    - 480-500 lines: CRITICAL WARNING (always shown, near limit)
    - >300 lines: WARNING (consider splitting, shown with -VerboseOutput)

    Skill file messages use the [skill-sizes] prefix.
    Context file messages use the [context-size] prefix.

.PARAMETER VerboseOutput
    If specified, outputs detailed information during validation including
    OK status for files within limits and WARNING status for files over 300 lines.

.EXAMPLE
    pwsh -NoProfile -File scripts/lint-skill-sizes.ps1
    pwsh -NoProfile -File scripts/lint-skill-sizes.ps1 -VerboseOutput
#>

Param(
    [switch]$VerboseOutput,
    [string[]]$Paths,
    [switch]$FailOnCritical,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPaths
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'git-path-helpers.ps1')

function Write-ErrorMsg($msg, $prefix = "[skill-sizes]") {
    Write-Host "$prefix ERROR: $msg" -ForegroundColor Red
}

function Write-CriticalWarningMsg($msg, $prefix = "[skill-sizes]") {
    Write-Host "$prefix CRITICAL: $msg" -ForegroundColor Magenta
}

function Write-WarningMsg($msg, $prefix = "[skill-sizes]") {
    Write-Host "$prefix WARNING: $msg" -ForegroundColor Yellow
}

function Write-SuccessMsg($msg, $prefix = "[skill-sizes]") {
    Write-Host "$prefix OK: $msg" -ForegroundColor Green
}

$repoRoot = (Get-Item $PSScriptRoot).Parent.FullName
$skillsDir = Join-Path -Path $repoRoot -ChildPath '.llm/skills'
$contextFile = Join-Path -Path $repoRoot -ChildPath '.llm/context.md'
$maxLines = 500
$criticalLines = 480
$warningLines = 300
$exitCode = 0

function Get-LineCount {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -Path $Path)) {
        return 0
    }

    return @(Get-Content -Path $Path).Count
}

# Validate skills directory exists
if (-not (Test-Path $skillsDir)) {
    Write-ErrorMsg "Skills directory not found at: $skillsDir"
    exit 1
}

# Exclude the generated index.md: it is machine-written (scripts/generate-skills-index.ps1),
# not an authored skill, so the authored-skill line limits do not apply to it.
$skillFiles = @(
    Get-ChildItem -Path $skillsDir -Filter '*.md' -Recurse |
        Where-Object { $_.Name -ne 'index.md' } |
        Sort-Object FullName
)
$checkContext = $true

if ($null -ne $Paths -and $Paths.Count -gt 0) {
    $allScopedPaths = @($Paths)
    if ($null -ne $AdditionalPaths -and $AdditionalPaths.Count -gt 0) {
        $allScopedPaths += $AdditionalPaths
    }

    $selectedSkillFiles = New-Object System.Collections.Generic.List[object]
    $checkContext = $false
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($rawPath in $allScopedPaths) {
        $relativePath = ConvertTo-GitRelativePosixPath -Path $rawPath -RepoRoot $repoRoot
        if ($null -eq $relativePath) {
            continue
        }

        if ($relativePath -eq '.llm/context.md') {
            $checkContext = $true
            continue
        }

        if ($relativePath -like '.llm/skills/*.md' -and $relativePath -ne '.llm/skills/index.md') {
            $fullPath = Join-Path -Path $repoRoot -ChildPath $relativePath
            if ((Test-Path -Path $fullPath) -and $seen.Add($fullPath)) {
                $selectedSkillFiles.Add((Get-Item -LiteralPath $fullPath)) | Out-Null
            }
        }
    }

    $skillFiles = @($selectedSkillFiles | Sort-Object FullName)
}

$errorCount = 0
$criticalCount = 0
$warningCount = 0
$okCount = 0

foreach ($file in $skillFiles) {
    $lineCount = @(Get-Content $file.FullName).Count
    $relativePath = $file.FullName.Substring($skillsDir.Length + 1).Replace('\', '/')

    if ($lineCount -gt $maxLines) {
        Write-ErrorMsg "${relativePath}: $lineCount lines (max: $maxLines) - MUST split"
        $exitCode = 1
        $errorCount++
    }
    elseif ($lineCount -ge $criticalLines) {
        $remaining = $maxLines - $lineCount
        if ($remaining -eq 0) {
            Write-CriticalWarningMsg "${relativePath}: $lineCount lines - AT the $maxLines line limit! Must split"
        } else {
            Write-CriticalWarningMsg "${relativePath}: $lineCount lines - only $remaining lines from $maxLines limit! Consider splitting now"
        }
        $criticalCount++
        if ($FailOnCritical) {
            $exitCode = 1
        }
    }
    elseif ($lineCount -gt $warningLines) {
        if ($VerboseOutput) {
            Write-WarningMsg "${relativePath}: $lineCount lines (consider splitting)"
        }
        $warningCount++
    }
    else {
        if ($VerboseOutput) {
            Write-SuccessMsg "${relativePath}: $lineCount lines"
        }
        $okCount++
    }
}

# Check .llm/context.md
$contextErrorCount = 0
$contextCriticalCount = 0
$contextWarningCount = 0
$contextOkCount = 0

$contextFound = $false
if ($checkContext -and (Test-Path $contextFile)) {
    $contextFound = $true
    $contextLineCount = Get-LineCount -Path $contextFile

    if ($contextLineCount -gt $maxLines) {
        Write-ErrorMsg "context.md: $contextLineCount lines (max: $maxLines) - MUST reduce" "[context-size]"
        $exitCode = 1
        $contextErrorCount++
    }
    elseif ($contextLineCount -ge $criticalLines) {
        $remaining = $maxLines - $contextLineCount
        if ($remaining -eq 0) {
            Write-CriticalWarningMsg "context.md: $contextLineCount lines - AT the $maxLines line limit! Must reduce" "[context-size]"
        } else {
            Write-CriticalWarningMsg "context.md: $contextLineCount lines - only $remaining lines from $maxLines limit! Consider reducing now" "[context-size]"
        }
        $contextCriticalCount++
        if ($FailOnCritical) {
            $exitCode = 1
        }
    }
    elseif ($contextLineCount -gt $warningLines) {
        if ($VerboseOutput) {
            Write-WarningMsg "context.md: $contextLineCount lines (consider reducing)" "[context-size]"
        }
        $contextWarningCount++
    }
    else {
        if ($VerboseOutput) {
            Write-SuccessMsg "context.md: $contextLineCount lines" "[context-size]"
        }
        $contextOkCount++
    }
}
elseif ($checkContext) {
    Write-WarningMsg "context.md not found at: $contextFile" "[context-size]"
    $contextWarningCount++
}

# Summary
Write-Host ""
Write-Host ("=" * 60)
Write-Host "[skill-sizes] Summary: $($skillFiles.Count) files checked"
if ($VerboseOutput -or $criticalCount -gt 0 -or $errorCount -gt 0) {
    Write-Host "[skill-sizes]   OK: $okCount, Warnings: $warningCount, Critical: $criticalCount, Errors: $errorCount"
}

if ($checkContext -and $contextFound) {
    Write-Host "[context-size] Summary: context.md checked ($contextLineCount lines)"
}
elseif ($checkContext) {
    Write-Host "[context-size] Summary: context.md not found"
}
if ($checkContext -and ($VerboseOutput -or $contextCriticalCount -gt 0 -or $contextErrorCount -gt 0)) {
    Write-Host "[context-size]   OK: $contextOkCount, Warnings: $contextWarningCount, Critical: $contextCriticalCount, Errors: $contextErrorCount"
}

if ($exitCode -eq 0) {
    Write-Host "All files within size limits" -ForegroundColor Green
}
else {
    Write-Host "Some files exceed size limits - see errors above" -ForegroundColor Red
}
Write-Host ("=" * 60)

exit $exitCode
