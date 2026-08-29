# =============================================================================
# Git Push Config Validator (read-only)
# =============================================================================
# Fast check-only validator suitable for validate:prepush. Runs the two
# self-healing checks from scripts/agent-preflight.ps1 in read-only mode, plus
# one devcontainer-only postcondition:
#
#   - push.autoSetupRemote == true and push.default == simple (local config)
#   - No stray <hook-name>.{txt,log,out,err,tmp} artifact files at repo root
#     or inside .githooks/
#   - github.com still resolves through the cached-token credential helper and
#     not through the Dev Containers helper (#600). That one belongs here
#     because it is the LAST thing checked before a push and the only symptom
#     otherwise is the push itself hanging for its full timeout while a dialog
#     waits on the owner's desktop. Measured at ~0.09 s.
#
# Exits 0 on success, 1 if any check fails. Never modifies state.
# Remediation on failure: run npm run agent:preflight:fix.
# =============================================================================

Param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'git-path-helpers.ps1')

$repoRoot = (Get-Item $PSScriptRoot).Parent.FullName

function Write-Info($Message) {
    Write-Host "[validate-git-push-config] $Message" -ForegroundColor Cyan
}

function Write-ErrorMsg($Message) {
    Write-Host "[validate-git-push-config] ERROR: $Message" -ForegroundColor Red
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-ErrorMsg 'git is required.'
    exit 1
}

$failureCount = 0

$expected = @{
    'push.autoSetupRemote' = 'true'
    'push.default' = 'simple'
}

$mismatches = New-Object System.Collections.Generic.List[string]

Push-Location $repoRoot
try {
    foreach ($key in $expected.Keys) {
        $actual = & git config --local --get $key 2>$null
        if ($LASTEXITCODE -ne 0) { $actual = '' }
        # Trim defensively — some git builds emit trailing CR/whitespace
        # (especially on Windows / MSYS mounts) and we compare against a
        # bare literal like 'true' / 'simple'.
        $actual = ([string]$actual).Trim()
        if ($actual -ne $expected[$key]) {
            $display = if ([string]::IsNullOrWhiteSpace($actual)) { 'unset' } else { $actual }
            $mismatches.Add("$key is '$display' (expected '$($expected[$key])')") | Out-Null
        }
    }
}
finally {
    Pop-Location
}

if ($mismatches.Count -gt 0) {
    Write-ErrorMsg 'Git push defaults are not configured for this repository:'
    foreach ($item in $mismatches) {
        Write-Host "  $item" -ForegroundColor Yellow
    }
    Write-Host 'Run: npm run agent:preflight:fix' -ForegroundColor Cyan
    $failureCount++
}

$hooksDir = Join-Path $repoRoot '.githooks'
$strayFiles = New-Object System.Collections.Generic.List[string]

if (Test-Path -LiteralPath $hooksDir -PathType Container) {
    $hookNames = @(
        Get-ChildItem -LiteralPath $hooksDir -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notlike '*.sample' -and $_.Extension -notin @('.txt', '.log', '.out', '.err', '.tmp') } |
            ForEach-Object {
                if ($_.Name -like '*.*') {
                    [System.IO.Path]::GetFileNameWithoutExtension($_.Name)
                }
                else {
                    $_.Name
                }
            } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
    )

    $artifactExtensions = @('txt', 'log', 'out', 'err', 'tmp')
    foreach ($hook in $hookNames) {
        foreach ($ext in $artifactExtensions) {
            $candidate = Join-Path $repoRoot "$hook.$ext"
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $strayFiles.Add($candidate) | Out-Null
            }

            $hookCandidate = Join-Path $hooksDir "$hook.$ext"
            if (Test-Path -LiteralPath $hookCandidate -PathType Leaf) {
                $strayFiles.Add($hookCandidate) | Out-Null
            }
        }
    }
}

$uniqueStrayFiles = @($strayFiles | Sort-Object -Unique)
if ($uniqueStrayFiles.Count -gt 0) {
    # Differentiate gitignored strays (safe for agent-preflight -Fix to auto-
    # delete) from files that merely match the error-log pattern but are NOT
    # gitignored (must be reviewed manually — may be user-authored artifacts).
    # git check-ignore exit codes: 0 = ignored, 1 = not ignored, 128 = error.
    #
    # IMPORTANT: git check-ignore expects REPO-RELATIVE paths with POSIX
    # forward-slash separators. Absolute Windows paths (with `\`) can be
    # silently misclassified. Normalize via ConvertTo-GitRelativePosixPath
    # (scripts/git-path-helpers.ps1) before every call.
    $ignoredFiles = New-Object System.Collections.Generic.List[string]
    $unignoredFiles = New-Object System.Collections.Generic.List[string]
    $checkIgnoreErrors = New-Object System.Collections.Generic.List[string]
    foreach ($file in $uniqueStrayFiles) {
        $relative = ConvertTo-GitRelativePosixPath -Path $file -RepoRoot $repoRoot
        if ([string]::IsNullOrWhiteSpace($relative) -or $relative -eq '.') {
            $checkIgnoreErrors.Add("${file}: cannot resolve repo-relative path") | Out-Null
            $unignoredFiles.Add($file) | Out-Null
            continue
        }

        & git -C $repoRoot check-ignore -q -- "$relative" 2>$null
        $checkExit = $LASTEXITCODE
        switch ($checkExit) {
            0 { $ignoredFiles.Add($file) | Out-Null }
            1 { $unignoredFiles.Add($file) | Out-Null }
            default {
                $checkIgnoreErrors.Add("${file}: git check-ignore exit $checkExit") | Out-Null
                $unignoredFiles.Add($file) | Out-Null
            }
        }
    }

    Write-ErrorMsg 'Stray git-hook artifact file(s) detected (likely redirected hook output):'
    if ($ignoredFiles.Count -gt 0) {
        Write-Host '  gitignored (safe to auto-delete via agent:preflight:fix):' -ForegroundColor Yellow
        foreach ($file in $ignoredFiles) {
            Write-Host "    $file" -ForegroundColor Yellow
        }
    }
    if ($unignoredFiles.Count -gt 0) {
        Write-Host '  NOT gitignored (manual review required):' -ForegroundColor Yellow
        foreach ($file in $unignoredFiles) {
            Write-Host "    $file" -ForegroundColor Yellow
        }
    }
    if ($checkIgnoreErrors.Count -gt 0) {
        Write-Host '  git check-ignore errors:' -ForegroundColor Yellow
        foreach ($entry in $checkIgnoreErrors) {
            Write-Host "    $entry" -ForegroundColor Yellow
        }
    }
    if ($ignoredFiles.Count -gt 0) {
        Write-Host 'For gitignored files: run npm run agent:preflight:fix.' -ForegroundColor Cyan
    }
    if ($unignoredFiles.Count -gt 0) {
        Write-Host 'For files not gitignored: delete manually if stale, or add a .gitignore entry and re-run (auto-delete is refused for safety).' -ForegroundColor Cyan
    }
    $failureCount++
}

# Devcontainer credential postcondition (#600).
#
# Gated on the Dev Containers helper actually being registered, and the gate is
# evaluated HERE rather than inside the shell script so that a developer machine
# with its own credential manager never spawns bash at all — on Windows a `bash`
# on PATH need not share this filesystem, and would then fail for a reason that
# has nothing to do with credentials.
$credentialCheck = Join-Path $repoRoot 'scripts/check-container-git-credentials.sh'
if (Test-Path -LiteralPath $credentialCheck -PathType Leaf) {
    # Anchored with -C, not left to the caller's cwd. $credentialCheck is derived from $repoRoot but
    # this block runs after the Pop-Location above, so an unanchored `git config` would read
    # whichever repository the caller happened to be standing in and gate a claim about THIS one on
    # it. The repo rule is explicit: a script that derives its own root anchors every repo-relative
    # git call there.
    $configuredHelpers = @(& git -C $repoRoot config --get-all credential.helper 2>$null)
    if ($LASTEXITCODE -ne 0) { $configuredHelpers = @() }
    $devContainerHelper = @(
        $configuredHelpers | Where-Object {
            $_ -like '*vscode-remote-containers*' -or $_ -like '*git-credential-helper*'
        }
    )

    if ($devContainerHelper.Count -gt 0) {
        $bashCommand = Get-Command bash -ErrorAction SilentlyContinue
        if ($null -eq $bashCommand) {
            Write-ErrorMsg 'The Dev Containers credential helper is registered but bash is unavailable to verify the override.'
            Write-Host "Run manually: bash $credentialCheck" -ForegroundColor Cyan
            $failureCount++
        }
        else {
            $credentialOutput = & $bashCommand.Source $credentialCheck '--quiet' 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-ErrorMsg 'github.com does not resolve through the cached-token credential helper:'
                foreach ($line in @($credentialOutput)) {
                    Write-Host "  $line" -ForegroundColor Yellow
                }
                $failureCount++
            }
        }
    }
}

if ($failureCount -gt 0) {
    Write-Host "[validate-git-push-config] Failed with $failureCount check(s) reporting issues." -ForegroundColor Red
    exit 1
}

Write-Info 'Git push config and hook artifact checks passed.'
exit 0
