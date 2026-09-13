#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Lint YAML files using yamllint.

.DESCRIPTION
    Runs yamllint on YAML files to check for syntax and style issues.
    Can lint all YAML files or only staged files for pre-commit hooks.

.PARAMETER StagedOnly
    If specified, only lint files that are staged in git.

.PARAMETER Paths
    Specific paths to lint. If not provided, lints all YAML files.

.PARAMETER VerboseOutput
    If specified, show verbose output including which files are being checked.

.EXAMPLE
    ./lint-yaml.ps1
    Lint all YAML files in the repository.

.EXAMPLE
    ./lint-yaml.ps1 -StagedOnly
    Lint only staged YAML files (for pre-commit hooks).

.EXAMPLE
    ./lint-yaml.ps1 -Paths .github/workflows/ci.yml
    Lint a specific file.
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [switch]$StagedOnly,
    [switch]$VerboseOutput,
    [string[]]$Paths,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPaths
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path -Path $PSScriptRoot -ChildPath '..')).Path
$effectivePaths = @()
if ($Paths -and $Paths.Count -gt 0) {
    $effectivePaths += $Paths
}
if ($AdditionalPaths -and $AdditionalPaths.Count -gt 0) {
    $effectivePaths += $AdditionalPaths
}

# Check for yamllint availability
$yamllint = Get-Command yamllint -ErrorAction SilentlyContinue
if (-not $yamllint) {
    # Try pip-installed yamllint on Windows
    $pipYamllint = Get-Command "$env:LOCALAPPDATA\Programs\Python\Python*\Scripts\yamllint.exe" -ErrorAction SilentlyContinue
    if ($pipYamllint) {
        $yamllint = $pipYamllint
    }
}

if (-not $yamllint) {
    Write-Warning "yamllint not found. Install with: pip install yamllint"
    Write-Warning "Skipping YAML lint check."
    exit 0
}

$configFile = Join-Path -Path $PSScriptRoot -ChildPath '..' -AdditionalChildPath '.yamllint.yaml'
$configFile = (Resolve-Path $configFile -ErrorAction SilentlyContinue).Path
if (-not $configFile -or -not (Test-Path $configFile)) {
    $configFile = Join-Path -Path (Get-Location) -ChildPath '.yamllint.yaml'
}

if (-not (Test-Path $configFile)) {
    Write-Error "yamllint config file not found at: $configFile"
    exit 1
}

# Get files to lint
$filesToLint = @()

if ($StagedOnly) {
    # Load git helpers if available
    $helpersPath = Join-Path -Path $PSScriptRoot -ChildPath 'git-staging-helpers.ps1'
    if (Test-Path $helpersPath) {
        . $helpersPath
        $yamlGlobs = @('*.yml', '*.yaml')
        $filesToLint = Get-StagedPathsForGlobs -DefaultPaths $effectivePaths -Globs $yamlGlobs
        $filesToLint = Get-ExistingPaths -Candidates $filesToLint
    } else {
        # Fallback: get staged files manually
        $stagedFiles = git diff --cached --name-only --diff-filter=ACMR 2>$null
        if ($stagedFiles) {
            $filesToLint = $stagedFiles | Where-Object { $_ -match '\.(ya?ml)$' } | Where-Object { Test-Path $_ }
        }
    }
} elseif ($effectivePaths.Count -gt 0) {
    $filesToLint = $effectivePaths | Where-Object { Test-Path $_ }
} else {
    # Pass the repository's actual YAML corpus. Giving yamllint '.' makes its Python walker enter
    # large ignored build and tool caches before its ignore rules can prune files; this checkout
    # measured about two minutes to discover 41 YAML files that git lists in milliseconds.
    $gitOutput = & git -C $repoRoot ls-files -z --cached --others --exclude-standard -- '*.yml' '*.yaml'
    $gitExitCode = $LASTEXITCODE
    if ($gitExitCode -ne 0) {
        Write-Error "Unable to enumerate repository YAML files. git exited with $gitExitCode."
        exit $gitExitCode
    }
    $relativeFiles = @($gitOutput -split "`0" | Where-Object { $_ -ne '' })
    $filesToLint = @($relativeFiles | ForEach-Object { Join-Path -Path $repoRoot -ChildPath $_ })
}

if ($filesToLint.Count -eq 0) {
    if ($VerboseOutput) {
        Write-Host "No YAML files to lint."
    }
    exit 0
}

if ($VerboseOutput) {
    Write-Host "Linting $($filesToLint.Count) YAML file(s) with yamllint..."
    if ($effectivePaths.Count -gt 0 -or $StagedOnly) {
        foreach ($file in $filesToLint) {
            Write-Host "  - $file"
        }
    }
}

# Run yamllint
# Note: Capture exit code immediately after command execution for reliability.
# Using $LASTEXITCODE directly after & operator with array splatting can be
# unreliable in some PowerShell versions. This pattern ensures we get the
# correct exit code by checking $LASTEXITCODE immediately after the call.
$yamllintArgs = @('-c', $configFile, '--')
$yamllintArgs += $filesToLint

$yamllintResult = $null
try {
    & $yamllint.Source @yamllintArgs
    $yamllintResult = $LASTEXITCODE
} catch {
    $yamllintResult = 1
    Write-Error "yamllint execution failed: $_"
}

# Ensure we have a valid exit code
if ($null -eq $yamllintResult) {
    $yamllintResult = 0
}

if ($yamllintResult -ne 0) {
    Write-Error "yamllint found issues. Exit code: $yamllintResult"
    exit $yamllintResult
}

if ($VerboseOutput) {
    Write-Host "yamllint passed." -ForegroundColor Green
}

exit 0
