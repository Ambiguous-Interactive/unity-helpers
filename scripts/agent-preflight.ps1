Param(
    [string[]]$Paths,
    [string]$PathList,
    [switch]$Fix,
    [switch]$AllowCriticalSkillSize,
    [switch]$VerboseOutput,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPaths
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'git-staging-helpers.ps1')
. (Join-Path $PSScriptRoot 'git-push-defaults-helpers.ps1')
. (Join-Path $PSScriptRoot 'git-path-helpers.ps1')

function Write-Info($Message) {
    if ($VerboseOutput) {
        Write-Host "[agent-preflight] $Message" -ForegroundColor Cyan
    }
}

function Write-ErrorMsg($Message) {
    Write-Host "[agent-preflight] ERROR: $Message" -ForegroundColor Red
}

function Write-WarningMsg($Message) {
    Write-Host "[agent-preflight] WARNING: $Message" -ForegroundColor Yellow
}

function Get-GitChangedPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $results = New-Object System.Collections.Generic.List[string]
    $commands = @(
        @('diff', '--name-only', '--diff-filter=ACMRTUXB'),
        @('diff', '--cached', '--name-only', '--diff-filter=ACMRTUXB'),
        @('ls-files', '--others', '--exclude-standard')
    )

    Push-Location $RepoRoot
    try {
        foreach ($command in $commands) {
            $output = & git @command 2>$null
            if ($LASTEXITCODE -ne 0) {
                continue
            }

            foreach ($line in $output) {
                if ([string]::IsNullOrWhiteSpace($line)) {
                    continue
                }
                $results.Add($line)
            }
        }
    }
    finally {
        Pop-Location
    }

    return @($results)
}

function Get-GitStagedPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $stagedPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

    Push-Location $RepoRoot
    try {
        $output = & git diff --cached --name-only --diff-filter=ACM 2>$null
        if ($LASTEXITCODE -ne 0) {
            return ,$stagedPaths
        }

        foreach ($line in $output) {
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }

            $normalizedPath = $line.Replace('\', '/')
            $stagedPaths.Add($normalizedPath) | Out-Null
        }
    }
    finally {
        Pop-Location
    }

    return ,$stagedPaths
}

function Get-PathListEntries {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$PathList
    )

    if ([string]::IsNullOrWhiteSpace($PathList)) {
        return @()
    }

    $pathListPath = if ([System.IO.Path]::IsPathRooted($PathList)) {
        $PathList
    }
    else {
        Join-Path -Path $RepoRoot -ChildPath $PathList
    }

    if (-not (Test-Path -LiteralPath $pathListPath -PathType Leaf)) {
        Write-ErrorMsg "Path list file not found: $pathListPath"
        return @()
    }

    $bytes = [System.IO.File]::ReadAllBytes($pathListPath)
    if ($bytes.Length -eq 0) {
        return @()
    }

    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    if ($text.Contains([string][char]0)) {
        return @($text -split ([string][char]0) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }

    return @($text -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Add-PathsToGitIndexWithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string[]]$Paths
    )

    if ($null -eq $Paths -or $Paths.Count -eq 0) {
        return $true
    }

    $uniquePaths = @($Paths | Sort-Object -Unique)
    $indexLockPath = Join-Path -Path (Join-Path -Path $RepoRoot -ChildPath '.git') -ChildPath 'index.lock'

    Push-Location $RepoRoot
    try {
        $exitCode = Invoke-GitAddWithRetry -Items $uniquePaths -IndexLockPath $indexLockPath
        return ($exitCode -eq 0)
    }
    finally {
        Pop-Location
    }
}

function Test-GitPushConfig {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [ref]$FailureCount,
        [switch]$Fix
    )

    $expected = @{
        'push.autoSetupRemote' = 'true'
        'push.default' = 'simple'
    }

    $mismatches = New-Object System.Collections.Generic.List[string]

    Push-Location $RepoRoot
    try {
        foreach ($key in $expected.Keys) {
            $actual = & git config --local --get $key 2>$null
            if ($LASTEXITCODE -ne 0) { $actual = '' }
            # Trim defensively — git may emit trailing CR/whitespace
            # (especially on Windows / MSYS mounts) and we compare against
            # bare literals.
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

    if ($mismatches.Count -eq 0) {
        Write-Info 'Git push defaults OK (push.autoSetupRemote=true, push.default=simple).'
        return
    }

    if ($Fix) {
        Write-Host '[agent-preflight] Fixing git push defaults via Set-RepoGitPushDefaults...' -ForegroundColor Blue

        # Use the dot-sourced helper directly instead of spawning a subprocess
        # (`pwsh -NoProfile -File scripts/configure-git-defaults.ps1`). The
        # subprocess form breaks on Windows PowerShell 5.1 hosts that do not
        # have pwsh on PATH; the in-process form reuses whichever shell
        # already loaded agent-preflight.ps1.
        $helperResult = Set-RepoGitPushDefaults -RepoRoot $RepoRoot
        if (-not $helperResult.Success) {
            Write-ErrorMsg 'Set-RepoGitPushDefaults failed; git push defaults were NOT applied.'
            foreach ($err in $helperResult.Errors) {
                Write-Host "  $err" -ForegroundColor Yellow
            }
            $FailureCount.Value++
            return
        }

        # Defense-in-depth: re-read both local config values one more time.
        # The helper already verified persistence internally, but this extra
        # check catches anything weird that could happen between the helper's
        # verify pass and this point (e.g., a concurrent external edit).
        $verifyMismatches = New-Object System.Collections.Generic.List[string]
        Push-Location $RepoRoot
        try {
            foreach ($key in $expected.Keys) {
                $verified = & git config --local --get $key 2>$null
                if ($LASTEXITCODE -ne 0) { $verified = '' }
                $verified = ([string]$verified).Trim()
                if ($verified -ne $expected[$key]) {
                    $display = if ([string]::IsNullOrWhiteSpace($verified)) { 'unset' } else { $verified }
                    $verifyMismatches.Add("$key is '$display' (expected '$($expected[$key])')") | Out-Null
                }
            }
        }
        finally {
            Pop-Location
        }

        if ($verifyMismatches.Count -gt 0) {
            Write-ErrorMsg 'Set-RepoGitPushDefaults reported success but git push defaults did NOT persist:'
            foreach ($item in $verifyMismatches) {
                Write-Host "  $item" -ForegroundColor Yellow
            }
            Write-Host 'Inspect .git/config for permission or wrapper issues. You can also invoke scripts/configure-git-defaults.ps1 directly to reproduce.' -ForegroundColor Cyan
            $FailureCount.Value++
            return
        }

        Write-Host '[agent-preflight] Git push defaults applied and verified.' -ForegroundColor Green
        return
    }

    Write-ErrorMsg 'Git push defaults are not configured for this repository:'
    foreach ($item in $mismatches) {
        Write-Host "  $item" -ForegroundColor Yellow
    }
    Write-Host 'Run: npm run agent:preflight:fix, or manually: git config --local push.autoSetupRemote true && git config --local push.default simple' -ForegroundColor Cyan
    $FailureCount.Value++
}

function Test-StrayArtifactFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [ref]$FailureCount,
        [switch]$Fix
    )

    $hooksDir = Join-Path $RepoRoot '.githooks'
    if (-not (Test-Path -LiteralPath $hooksDir -PathType Container)) {
        return
    }

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
    $strayFiles = New-Object System.Collections.Generic.List[string]

    foreach ($hook in $hookNames) {
        foreach ($ext in $artifactExtensions) {
            $candidate = Join-Path $RepoRoot "$hook.$ext"
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $strayFiles.Add($candidate) | Out-Null
            }

            $hookCandidate = Join-Path $hooksDir "$hook.$ext"
            if (Test-Path -LiteralPath $hookCandidate -PathType Leaf) {
                $strayFiles.Add($hookCandidate) | Out-Null
            }
        }
    }

    $uniqueStrayFiles = @($strayFiles | Sort-Object -Unique)
    if ($uniqueStrayFiles.Count -eq 0) {
        return
    }

    # Safety gate: only delete files that git confirms are gitignored. A file
    # matching the error-log pattern that is NOT gitignored may be a legitimate
    # user artifact (e.g. a committed or intentionally-tracked "pre-commit.log"
    # note) and must not be silently removed.
    #
    # IMPORTANT: git check-ignore expects REPO-RELATIVE paths with POSIX
    # forward-slash separators. On Windows, the absolute paths we build via
    # `Join-Path $RepoRoot ...` and `.FullName` contain backslashes, which
    # git check-ignore may silently MISCLASSIFY (reporting "not ignored"
    # for a file that is in fact gitignored). A misclassification here
    # would cause the safety gate below to refuse auto-delete of legitimate
    # stray files. Use ConvertTo-GitRelativePosixPath (from
    # scripts/git-path-helpers.ps1) to normalize before every call.
    #
    # git check-ignore exit codes:
    #   0   -> ignored
    #   1   -> not ignored
    #   128 -> error (e.g. not a git repo, IO failure)
    $ignoredFiles = New-Object System.Collections.Generic.List[string]
    $unignoredFiles = New-Object System.Collections.Generic.List[string]
    $checkIgnoreErrors = New-Object System.Collections.Generic.List[string]
    foreach ($file in $uniqueStrayFiles) {
        $relative = ConvertTo-GitRelativePosixPath -Path $file -RepoRoot $RepoRoot
        if ([string]::IsNullOrWhiteSpace($relative) -or $relative -eq '.') {
            # File is outside the repo root (should not happen for strays that
            # we discovered under $RepoRoot or .githooks/) or normalization
            # failed — refuse to delete without a clean ignore confirmation.
            $checkIgnoreErrors.Add("${file}: cannot resolve repo-relative path") | Out-Null
            $unignoredFiles.Add($file) | Out-Null
            continue
        }

        & git -C $RepoRoot check-ignore -q -- "$relative" 2>$null
        $checkExit = $LASTEXITCODE
        switch ($checkExit) {
            0 { $ignoredFiles.Add($file) | Out-Null }
            1 { $unignoredFiles.Add($file) | Out-Null }
            default {
                # Treat check-ignore failures as "unsafe to delete". The file is
                # still a match for an error-log pattern so we surface it, but
                # we refuse to delete it without a clean ignore confirmation.
                $checkIgnoreErrors.Add("${file}: git check-ignore exit $checkExit") | Out-Null
                $unignoredFiles.Add($file) | Out-Null
            }
        }
    }

    if ($Fix) {
        if ($ignoredFiles.Count -gt 0) {
            Write-Host '[agent-preflight] Removing stray git-hook artifact file(s) (verified gitignored):' -ForegroundColor Blue
            foreach ($file in $ignoredFiles) {
                try {
                    Remove-Item -LiteralPath $file -Force -ErrorAction Stop
                    Write-Host "  removed: $file" -ForegroundColor Green
                }
                catch {
                    Write-ErrorMsg "Failed to remove ${file}: $($_.Exception.Message)"
                    $FailureCount.Value++
                }
            }
        }

        if ($unignoredFiles.Count -gt 0) {
            Write-WarningMsg 'Skipped deletion of stray artifact file(s) not confirmed as gitignored:'
            foreach ($file in $unignoredFiles) {
                Write-Host "  $file" -ForegroundColor Yellow
            }
            if ($checkIgnoreErrors.Count -gt 0) {
                Write-Host 'git check-ignore encountered errors on:' -ForegroundColor Yellow
                foreach ($entry in $checkIgnoreErrors) {
                    Write-Host "  $entry" -ForegroundColor Yellow
                }
            }
            Write-Host 'These files match an error-log pattern but are not gitignored. Delete manually if stale, or add a .gitignore entry and re-run.' -ForegroundColor Cyan
            $FailureCount.Value++
        }
        return
    }

    Write-ErrorMsg 'Stray git-hook artifact file(s) detected (likely redirected hook output):'
    if ($ignoredFiles.Count -gt 0) {
        Write-Host '  gitignored (safe to auto-delete with -Fix):' -ForegroundColor Yellow
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
        Write-Host 'Run with -Fix to delete the gitignored files (npm run agent:preflight:fix). Never redirect git command output to files in the working tree.' -ForegroundColor Cyan
    }
    if ($unignoredFiles.Count -gt 0) {
        Write-Host 'Files matching an error-log pattern but not gitignored will NOT be auto-deleted - delete manually if stale, or add a .gitignore entry and re-run.' -ForegroundColor Cyan
    }
    $FailureCount.Value++
}

function Test-MetaRequiredPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    if ($RelativePath -notmatch '^(Runtime|Editor|Tests|Samples~|Shaders|Styles|URP|docs|scripts)/') {
        return $false
    }

    $leaf = Split-Path -Path $RelativePath -Leaf
    if ($RelativePath -like '*.meta') { return $false }
    if ($leaf -eq 'package-lock.json') { return $false }
    if ($leaf -eq 'Gemfile.lock') { return $false }
    if ($RelativePath -like '*.tmp') { return $false }
    if ($leaf -eq '.gitkeep') { return $false }
    if ($leaf -eq '.DS_Store') { return $false }
    if ($leaf -eq 'Thumbs.db') { return $false }
    if ($RelativePath -like '*.pyc') { return $false }
    if ($RelativePath -like '*.pyo') { return $false }
    if ($RelativePath -like '*.swp') { return $false }
    if ($RelativePath -like '*.swo') { return $false }

    return $true
}

function Test-NodeToolAvailable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$ToolName,
        [Parameter(Mandatory = $true)]
        [string]$Purpose,
        [Parameter(Mandatory = $true)]
        [switch]$Fix,
        [Parameter(Mandatory = $true)]
        [ref]$FailureCount
    )

    if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
        Write-ErrorMsg "Node.js is required for $Purpose. Install Node.js/npm and run npm install."
        $FailureCount.Value++
        return $false
    }

    Push-Location $RepoRoot
    try {
        $toolOutput = & node (Join-Path $RepoRoot 'scripts/run-node-bin.js') $ToolName --version 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Info "npm tool available for ${Purpose}: $ToolName"
            return $true
        }
    }
    finally {
        Pop-Location
    }

    Write-ErrorMsg "Required npm tool '$ToolName' is not installed for $Purpose."
    foreach ($line in $toolOutput) {
        Write-Host $line -ForegroundColor DarkGray
    }
    Write-Host 'Run: npm install' -ForegroundColor Cyan
    $rerunScript = if ($Fix) { 'agent:preflight:fix' } else { 'agent:preflight' }
    Write-Host "Then re-run: npm run $rerunScript" -ForegroundColor Cyan
    $FailureCount.Value++
    return $false
}

function Invoke-NodeToolOnPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$ToolName,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [Parameter(Mandatory = $true)]
        [string[]]$Paths,
        [switch]$SuppressOutput
    )

    $existingPaths = @()
    foreach ($path in $Paths) {
        $fullPath = Join-Path -Path $RepoRoot -ChildPath $path
        if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            $existingPaths += $path
        }
    }

    if ($existingPaths.Count -eq 0) {
        return 0
    }

    Push-Location $RepoRoot
    try {
        $output = & node (Join-Path $RepoRoot 'scripts/run-node-bin.js') $ToolName @Arguments -- $existingPaths 2>&1
        $exitCode = $LASTEXITCODE
        if (-not $SuppressOutput) {
            foreach ($line in $output) {
                Write-Host $line
            }
        }
        return $exitCode
    }
    finally {
        Pop-Location
    }
}

function Invoke-Prettier {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [switch]$SuppressOutput
    )

    $prettierCommand = [Environment]::GetEnvironmentVariable('AGENT_PREFLIGHT_PRETTIER_COMMAND')
    Push-Location $RepoRoot
    try {
        if (-not [string]::IsNullOrWhiteSpace($prettierCommand)) {
            $output = & $prettierCommand @Arguments 2>&1
        }
        else {
            $output = & node (Join-Path $RepoRoot 'scripts/run-prettier.js') @Arguments 2>&1
        }

        $exitCode = $LASTEXITCODE
        if (-not $SuppressOutput) {
            foreach ($line in $output) {
                Write-Host $line
            }
        }
        return $exitCode
    }
    finally {
        Pop-Location
    }
}

function Test-PrettierAvailable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [switch]$Fix,
        [Parameter(Mandatory = $true)]
        [ref]$FailureCount
    )

    $prettierCommand = [Environment]::GetEnvironmentVariable('AGENT_PREFLIGHT_PRETTIER_COMMAND')
    if ([string]::IsNullOrWhiteSpace($prettierCommand) -and -not (Get-Command node -ErrorAction SilentlyContinue)) {
        Write-ErrorMsg 'Node.js is required to run the repo-local Prettier launcher.'
        Write-Host 'Install Node.js/npm, then run: npm install' -ForegroundColor Cyan
        $FailureCount.Value++
        return $false
    }

    if ((-not [string]::IsNullOrWhiteSpace($prettierCommand)) -and -not (Get-Command $prettierCommand -ErrorAction SilentlyContinue)) {
        Write-ErrorMsg "Configured Prettier command '$prettierCommand' was not found."
        $FailureCount.Value++
        return $false
    }

    $exitCode = Invoke-Prettier -RepoRoot $RepoRoot -Arguments @('--version') -SuppressOutput
    if ($exitCode -eq 0) {
        Write-Info 'repo-local Prettier launcher is available.'
        return $true
    }

    Write-ErrorMsg 'Repo-local Prettier is unavailable.'
    Write-Host 'Run: npm install' -ForegroundColor Cyan
    $rerunScript = if ($Fix) { 'agent:preflight:fix' } else { 'agent:preflight' }
    Write-Host "Then re-run: npm run $rerunScript" -ForegroundColor Cyan
    $FailureCount.Value++
    return $false
}

function Invoke-PrettierOnPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [Parameter(Mandatory = $true)]
        [string[]]$Paths
    )

    $existingPaths = @()
    foreach ($path in $Paths) {
        $fullPath = Join-Path -Path $RepoRoot -ChildPath $path
        if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            $existingPaths += $path
        }
    }

    if ($existingPaths.Count -eq 0) {
        return 0
    }

    return Invoke-Prettier -RepoRoot $RepoRoot -Arguments (@($Arguments) + @('--') + @($existingPaths))
}

function New-LicenseYearCache {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $cachePath = Join-Path -Path (Join-Path -Path $RepoRoot -ChildPath '.git') -ChildPath 'license-year-cache'
    Push-Location $RepoRoot
    try {
        $gitCachePath = & git rev-parse --git-path license-year-cache 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($gitCachePath)) {
            $gitCachePath = ([string]$gitCachePath).Trim()
            $cachePath = if ([System.IO.Path]::IsPathRooted($gitCachePath)) {
                $gitCachePath
            }
            else {
                Join-Path -Path $RepoRoot -ChildPath $gitCachePath
            }
        }
    }
    finally {
        Pop-Location
    }

    $items = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    if (Test-Path -LiteralPath $cachePath -PathType Leaf) {
        foreach ($line in Get-Content -LiteralPath $cachePath -ErrorAction SilentlyContinue) {
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }

            $parts = ([string]$line) -split "`t", 2
            if ($parts.Count -eq 2 -and -not [string]::IsNullOrWhiteSpace($parts[0]) -and $parts[1] -match '^\d{4}$') {
                $items[$parts[0]] = $parts[1]
            }
        }
    }

    return [pscustomobject]@{
        Path = $cachePath
        Items = $items
        Dirty = $false
    }
}

function Save-LicenseYearCache {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Cache
    )

    if (-not $Cache.Dirty) {
        return
    }

    $cacheDirectory = Split-Path -Parent $Cache.Path
    if (-not (Test-Path -LiteralPath $cacheDirectory -PathType Container)) {
        return
    }

    $lines = foreach ($key in ($Cache.Items.Keys | Sort-Object)) {
        "$key`t$($Cache.Items[$key])"
    }

    $content = ($lines -join "`n") + "`n"
    [System.IO.File]::WriteAllText($Cache.Path, $content, [System.Text.UTF8Encoding]::new($false))
}

function Get-LicenseCreationYear {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$RelativePath,
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Cache
    )

    if ($Cache.Items.ContainsKey($RelativePath)) {
        return $Cache.Items[$RelativePath]
    }

    Push-Location $RepoRoot
    try {
        $historyYears = @(git log --follow --diff-filter=A --format=%ad --date=format:%Y -- $RelativePath 2>$null)
        if ($LASTEXITCODE -ne 0 -or $historyYears.Count -eq 0) {
            return [string](Get-Date).Year
        }

        $year = [string]$historyYears[$historyYears.Count - 1]
        if ([string]::IsNullOrWhiteSpace($year)) {
            return [string](Get-Date).Year
        }

        if ([int]$year -lt 2023) {
            $year = '2023'
        }

        $Cache.Items[$RelativePath] = $year
        $Cache.Dirty = $true
        return $year
    }
    finally {
        Pop-Location
    }
}

function Get-LicenseHeaderYear {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $firstLine = ''
    try {
        $firstLine = [System.IO.File]::ReadLines($Path) | Select-Object -First 1
    }
    catch {
        return ''
    }

    $match = [regex]::Match([string]$firstLine, 'Copyright \(c\) (?<year>\d{4})')
    if (-not $match.Success) {
        return ''
    }

    return $match.Groups['year'].Value
}

function Set-LicenseHeader {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Year
    )

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    $newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $normalized = $text -replace "`r`n", "`n" -replace "`r", "`n"
    $lineArray = [regex]::Split($normalized, "`n")
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $lineArray) {
        $lines.Add($line) | Out-Null
    }

    if ($lines.Count -eq 1 -and $lines[0] -eq '') {
        $lines.Clear()
    }

    $headerLine1 = "// MIT License - Copyright (c) $Year wallstop"
    $headerLine2 = '// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE'

    if ($lines.Count -gt 0 -and $lines[0].Contains('MIT License')) {
        $lines[0] = $headerLine1
        if ($lines.Count -gt 1 -and $lines[1].Contains('Full license text:')) {
            $lines[1] = $headerLine2
        }
        else {
            $lines.Insert(1, $headerLine2)
        }
    }
    else {
        $lines.Insert(0, $headerLine1)
        $lines.Insert(1, $headerLine2)
        $lines.Insert(2, '')
    }

    $updated = [string]::Join($newline, $lines)
    if ($updated -eq $text) {
        return $false
    }

    [System.IO.File]::WriteAllBytes($Path, [System.Text.UTF8Encoding]::new($false).GetBytes($updated))
    return $true
}

function Test-LicenseYearHeaders {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string[]]$Paths,
        [Parameter(Mandatory = $true)]
        [ref]$FailureCount,
        [switch]$Fix
    )

    $targets = @($Paths | Where-Object {
        $_ -like '*.cs' -and (Test-Path -LiteralPath (Join-Path -Path $RepoRoot -ChildPath $_) -PathType Leaf)
    } | Sort-Object -Unique)

    if ($targets.Count -eq 0) {
        return
    }

    Write-Host '[agent-preflight] Checking license year headers on changed C# files...' -ForegroundColor Blue
    $cache = New-LicenseYearCache -RepoRoot $RepoRoot
    $issues = New-Object System.Collections.Generic.List[string]
    $updatedPaths = New-Object System.Collections.Generic.List[string]

    foreach ($path in $targets) {
        $fullPath = Join-Path -Path $RepoRoot -ChildPath $path
        $actualYear = Get-LicenseHeaderYear -Path $fullPath
        $expectedYear = Get-LicenseCreationYear -RepoRoot $RepoRoot -RelativePath $path -Cache $cache

        if ([string]::IsNullOrWhiteSpace($actualYear)) {
            $issues.Add("${path}: missing copyright year, expected $expectedYear") | Out-Null
        }
        elseif ($actualYear -ne $expectedYear) {
            $issues.Add("${path}: has $actualYear, expected $expectedYear") | Out-Null
        }
    }

    if ($Fix -and $issues.Count -gt 0) {
        foreach ($path in $targets) {
            $fullPath = Join-Path -Path $RepoRoot -ChildPath $path
            $expectedYear = Get-LicenseCreationYear -RepoRoot $RepoRoot -RelativePath $path -Cache $cache
            if (Set-LicenseHeader -Path $fullPath -Year $expectedYear) {
                $updatedPaths.Add($path) | Out-Null
            }
        }

        if ($updatedPaths.Count -gt 0) {
            Write-Host "[agent-preflight] Updated $($updatedPaths.Count) license header(s)." -ForegroundColor Green

            $stagedPaths = Get-GitStagedPaths -RepoRoot $RepoRoot
            $stagedUpdatedPaths = @($updatedPaths | Where-Object { $stagedPaths.Contains($_) })
            if ($stagedUpdatedPaths.Count -gt 0 -and -not (Add-PathsToGitIndexWithRetry -RepoRoot $RepoRoot -Paths $stagedUpdatedPaths)) {
                Write-ErrorMsg 'Failed to stage license header fixes. Git index.lock contention or another git error is likely.'
                foreach ($path in $stagedUpdatedPaths) {
                    Write-Host "  $path" -ForegroundColor Yellow
                }
                Write-Host 'Close other git operations, then re-run npm run agent:preflight:fix.' -ForegroundColor Cyan
                $FailureCount.Value++
            }
        }

        $issues.Clear()
        foreach ($path in $targets) {
            $fullPath = Join-Path -Path $RepoRoot -ChildPath $path
            $actualYear = Get-LicenseHeaderYear -Path $fullPath
            $expectedYear = Get-LicenseCreationYear -RepoRoot $RepoRoot -RelativePath $path -Cache $cache
            if ($actualYear -ne $expectedYear) {
                $issues.Add("${path}: has $actualYear, expected $expectedYear") | Out-Null
            }
        }
    }

    Save-LicenseYearCache -Cache $cache

    if ($issues.Count -gt 0) {
        Write-ErrorMsg 'License year header issues detected in changed C# files:'
        foreach ($issue in $issues) {
            Write-Host "  $issue" -ForegroundColor Yellow
        }
        Write-Host 'Run: npm run agent:preflight:fix' -ForegroundColor Cyan
        $FailureCount.Value++
    }
}

$repoRoot = (Get-Item $PSScriptRoot).Parent.FullName
$sourceRoots = @('Runtime', 'Editor', 'Tests', 'Samples~', 'Shaders', 'Styles', 'URP', 'docs', 'scripts')

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-ErrorMsg 'git is required to compute changed files.'
    exit 1
}

$failureCount = 0
$availableNodeTools = @{}
$prettierAvailable = $false

Test-GitPushConfig -RepoRoot $repoRoot -FailureCount ([ref]$failureCount) -Fix:$Fix
Test-StrayArtifactFiles -RepoRoot $repoRoot -FailureCount ([ref]$failureCount) -Fix:$Fix

$candidatePaths = if (-not [string]::IsNullOrWhiteSpace($PathList)) {
    Get-PathListEntries -RepoRoot $repoRoot -PathList $PathList
}
elseif ($null -ne $Paths -and $Paths.Count -gt 0) {
    $resolved = @($Paths)
    if ($null -ne $AdditionalPaths -and $AdditionalPaths.Count -gt 0) {
        $resolved += $AdditionalPaths
    }
    $resolved
}
else {
    Get-GitChangedPaths -RepoRoot $repoRoot
}

$dedupedPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$relativePaths = New-Object System.Collections.Generic.List[string]

foreach ($candidate in $candidatePaths) {
    $relative = ConvertTo-GitRelativePosixPath -Path $candidate -RepoRoot $repoRoot
    if ($null -eq $relative -or [string]::IsNullOrWhiteSpace($relative) -or $relative -eq '.') {
        continue
    }

    if ($dedupedPaths.Add($relative)) {
        $relativePaths.Add($relative) | Out-Null
    }
}

if ($relativePaths.Count -eq 0) {
    if ($failureCount -gt 0) {
        Write-Host "[agent-preflight] Failed with $failureCount check group(s) reporting issues." -ForegroundColor Red
        exit 1
    }
    Write-Host '[agent-preflight] No changed files detected. Nothing to validate.' -ForegroundColor Green
    exit 0
}

Write-Info "Detected $($relativePaths.Count) changed path(s)."

$llmFiles = @($relativePaths | Where-Object { $_ -like '.llm/*' })
$llmSizeTargets = @(
    $relativePaths | Where-Object {
        $_ -eq '.llm/context.md' -or $_ -like '.llm/skills/*.md'
    }
)
$prettierTargets = @(
    $relativePaths | Where-Object {
        $_ -like '*.md' -or
        $_ -like '*.markdown' -or
        $_ -like '*.json' -or
        $_ -like '*.jsonc' -or
        $_ -like '*.asmdef' -or
        $_ -like '*.asmref' -or
        $_ -like '*.yml' -or
        $_ -like '*.yaml' -or
        $_ -like '*.js'
    }
)
$markdownTargets = @(
    $relativePaths | Where-Object {
        $_ -like '*.md' -or $_ -like '*.markdown'
    }
)
$spellingTargets = @(
    $relativePaths | Where-Object {
        $_ -like '*.md' -or
        $_ -like '*.markdown' -or
        $_ -like '*.json' -or
        $_ -like '*.jsonc' -or
        $_ -like '*.asmdef' -or
        $_ -like '*.asmref' -or
        $_ -like '*.yml' -or
        $_ -like '*.yaml' -or
        $_ -like '*.js' -or
        $_ -like '*.cs'
    }
)
$csharpTargets = @($relativePaths | Where-Object { $_ -like '*.cs' })
$testFiles = @($csharpTargets | Where-Object { $_ -like 'Tests/*.cs' })
$metaRelevantPaths = @($relativePaths | Where-Object { Test-MetaRequiredPath -RelativePath $_ })
$eolTargets = @($relativePaths)
$cspellConfigChanged = $dedupedPaths.Contains('cspell.json')
$lintErrorCodeContractTargets = @(
    $relativePaths | Where-Object {
        $_ -match '^(scripts/lint-[^/]+\.(ps1|js)|scripts/tests/test-lint-[^/]+\.(ps1|js|sh)|\.githooks/[^/]+|scripts/validate-lint-error-codes\.ps1|scripts/tests/test-validate-lint-error-codes\.ps1|cspell\.json)$'
    }
)

$requiredNodeTools = [ordered]@{}
if ($markdownTargets.Count -gt 0) {
    $requiredNodeTools['markdownlint'] = 'markdownlint validation for changed Markdown files'
}
if ($spellingTargets.Count -gt 0) {
    $requiredNodeTools['cspell'] = 'spelling validation for changed Markdown/JSON/YAML/JavaScript/C# files'
}

if ($prettierTargets.Count -gt 0) {
    Write-Host '[agent-preflight] Verifying repo-local Prettier...' -ForegroundColor Blue
    $prettierAvailable = Test-PrettierAvailable -RepoRoot $repoRoot -Fix:$Fix -FailureCount ([ref]$failureCount)
}

if ($requiredNodeTools.Count -gt 0) {
    Write-Host '[agent-preflight] Verifying local npm hook tools...' -ForegroundColor Blue
    foreach ($toolName in $requiredNodeTools.Keys) {
        $availableNodeTools[$toolName] = Test-NodeToolAvailable `
            -RepoRoot $repoRoot `
            -ToolName $toolName `
            -Purpose $requiredNodeTools[$toolName] `
            -Fix:$Fix `
            -FailureCount ([ref]$failureCount)
    }
}

if ($metaRelevantPaths.Count -gt 0 -and -not (Invoke-EnsureNoIndexLock)) {
    if ($Fix) {
        Write-WarningMsg 'index.lock still held after waiting; auto-stage operations may fail if contention persists.'
    }
    else {
        Write-WarningMsg 'index.lock is currently held by another process. Read-only checks can pass while commit-time auto-stage fails. Close competing git tools and run npm run agent:preflight:fix before committing.'
    }
}

if ($llmSizeTargets.Count -gt 0) {
    Write-Host '[agent-preflight] Checking changed skill/context file sizes...' -ForegroundColor Blue
    $failOnCritical = -not $AllowCriticalSkillSize
    & (Join-Path $repoRoot 'scripts/lint-skill-sizes.ps1') -Paths $llmSizeTargets -FailOnCritical:$failOnCritical -VerboseOutput:$VerboseOutput
    if ($LASTEXITCODE -ne 0) {
        $failureCount++
    }
}

if ($llmFiles.Count -gt 0) {
    Write-Host '[agent-preflight] Validating LLM instruction consistency...' -ForegroundColor Blue
    & (Join-Path $repoRoot 'scripts/lint-llm-instructions.ps1') -Fix:$Fix -VerboseOutput:$VerboseOutput
    if ($LASTEXITCODE -ne 0) {
        $failureCount++
    }
}

if ($csharpTargets.Count -gt 0) {
    Test-LicenseYearHeaders -RepoRoot $repoRoot -Paths $csharpTargets -FailureCount ([ref]$failureCount) -Fix:$Fix
}

if ($prettierTargets.Count -gt 0) {
    if ($prettierAvailable) {
        if ($Fix) {
            Write-Host '[agent-preflight] Formatting changed Markdown/JSON/YAML/JavaScript files with Prettier...' -ForegroundColor Blue
            $prettierExit = Invoke-PrettierOnPaths `
                -RepoRoot $repoRoot `
                -Arguments @('--write', '--log-level', 'warn') `
                -Paths $prettierTargets
            if ($prettierExit -ne 0) {
                Write-ErrorMsg "Prettier formatting failed with exit code $prettierExit."
                $failureCount++
            }
            else {
                $stagedPaths = Get-GitStagedPaths -RepoRoot $repoRoot
                $stagedPrettierTargets = @($prettierTargets | Where-Object { $stagedPaths.Contains($_) })
                if ($stagedPrettierTargets.Count -gt 0) {
                    if (-not (Add-PathsToGitIndexWithRetry -RepoRoot $repoRoot -Paths $stagedPrettierTargets)) {
                        Write-ErrorMsg 'Failed to stage Prettier-formatted files. Git index.lock contention or another git error is likely.'
                        foreach ($path in $stagedPrettierTargets) {
                            Write-Host "  $path" -ForegroundColor Yellow
                        }
                        Write-Host 'Close other git operations, then re-run npm run agent:preflight:fix.' -ForegroundColor Cyan
                        $failureCount++
                    }
                }
            }
        }
        else {
            Write-Host '[agent-preflight] Checking changed Markdown/JSON/YAML/JavaScript formatting with Prettier...' -ForegroundColor Blue
            $prettierExit = Invoke-PrettierOnPaths `
                -RepoRoot $repoRoot `
                -Arguments @('--check') `
                -Paths $prettierTargets
            if ($prettierExit -ne 0) {
                Write-ErrorMsg 'Prettier found formatting issues in changed files.'
                Write-Host 'Run: npm run agent:preflight:fix' -ForegroundColor Cyan
                $failureCount++
            }
        }
    }
}

if ($markdownTargets.Count -gt 0) {
    if ($availableNodeTools.ContainsKey('markdownlint') -and $availableNodeTools['markdownlint']) {
        if ($Fix) {
            Write-Host '[agent-preflight] Adding missing Markdown fence languages where inferable...' -ForegroundColor Blue
            $markdownFenceFixExit = 0
            & (Join-Path $repoRoot 'scripts/fix-markdown-fence-languages.ps1') -Paths $markdownTargets -VerboseOutput:$VerboseOutput
            $markdownFenceFixExit = $LASTEXITCODE
            if ($markdownFenceFixExit -ne 0) {
                Write-ErrorMsg "Markdown fence language auto-fix failed with exit code $markdownFenceFixExit."
                $failureCount++
            }
            else {
                $stagedPaths = Get-GitStagedPaths -RepoRoot $repoRoot
                $stagedMarkdownTargets = @($markdownTargets | Where-Object { $stagedPaths.Contains($_) })
                if ($stagedMarkdownTargets.Count -gt 0) {
                    if (-not (Add-PathsToGitIndexWithRetry -RepoRoot $repoRoot -Paths $stagedMarkdownTargets)) {
                        Write-ErrorMsg 'Failed to stage Markdown fence language fixes. Git index.lock contention or another git error is likely.'
                        foreach ($path in $stagedMarkdownTargets) {
                            Write-Host "  $path" -ForegroundColor Yellow
                        }
                        Write-Host 'Close other git operations, then re-run npm run agent:preflight:fix.' -ForegroundColor Cyan
                        $failureCount++
                    }
                }
            }

            Write-Host '[agent-preflight] Auto-fixing changed Markdown files with markdownlint...' -ForegroundColor Blue
            $markdownFixExit = Invoke-NodeToolOnPaths `
                -RepoRoot $repoRoot `
                -ToolName 'markdownlint' `
                -Arguments @('--fix', '--config', '.markdownlint.json', '--ignore-path', '.markdownlintignore') `
                -Paths $markdownTargets `
                -SuppressOutput
            if ($markdownFixExit -ne 0) {
                Write-Info "markdownlint --fix exited $markdownFixExit; final validation will report remaining issues."
            }

            $stagedPaths = Get-GitStagedPaths -RepoRoot $repoRoot
            $stagedMarkdownTargets = @($markdownTargets | Where-Object { $stagedPaths.Contains($_) })
            if ($stagedMarkdownTargets.Count -gt 0) {
                if (-not (Add-PathsToGitIndexWithRetry -RepoRoot $repoRoot -Paths $stagedMarkdownTargets)) {
                    Write-ErrorMsg 'Failed to stage markdownlint-fixed files. Git index.lock contention or another git error is likely.'
                    foreach ($path in $stagedMarkdownTargets) {
                        Write-Host "  $path" -ForegroundColor Yellow
                    }
                    Write-Host 'Close other git operations, then re-run npm run agent:preflight:fix.' -ForegroundColor Cyan
                    $failureCount++
                }
            }
        }

        Write-Host '[agent-preflight] Linting changed Markdown files with markdownlint...' -ForegroundColor Blue
        $markdownLintExit = Invoke-NodeToolOnPaths `
            -RepoRoot $repoRoot `
            -ToolName 'markdownlint' `
            -Arguments @('--config', '.markdownlint.json', '--ignore-path', '.markdownlintignore') `
            -Paths $markdownTargets
        if ($markdownLintExit -ne 0) {
            Write-ErrorMsg 'markdownlint found issues in changed Markdown files.'
            Write-Host 'Run: npm run agent:preflight:fix' -ForegroundColor Cyan
            $failureCount++
        }
    }
}

if ($spellingTargets.Count -gt 0) {
    Write-Host '[agent-preflight] Checking spelling on changed spell-checkable files...' -ForegroundColor Blue
    if (-not $availableNodeTools.ContainsKey('cspell') -or -not $availableNodeTools['cspell']) {
        Write-Info 'Skipping cspell execution because the required npm tool availability check already failed.'
    }
    else {
        $spellingFileList = $null
        try {
            $spellingFileList = [System.IO.Path]::GetTempFileName()
            Set-Content -LiteralPath $spellingFileList -Value $spellingTargets -Encoding UTF8
        }
        catch {
            Write-ErrorMsg "Failed to prepare temporary spelling file list: $($_.Exception.Message)"
            $failureCount++
        }

        if ($null -ne $spellingFileList) {
            try {
                Push-Location $repoRoot
                try {
                    # Capture cspell output so we can (a) surface it
                    # verbatim to the caller and (b) extract lint-error-
                    # code-shaped unknown tokens and print a copy-pasteable
                    # cspell.json patch. This makes the agent preflight
                    # the EARLIEST point at which a new lint-error-code
                    # family without a cspell entry is caught — before
                    # any hook runs.
                    $spellingOutput = & node (Join-Path $repoRoot 'scripts/run-node-bin.js') cspell lint --no-must-find-files --no-progress --show-suggestions --file-list $spellingFileList 2>&1
                    $spellingExit = $LASTEXITCODE
                    foreach ($line in $spellingOutput) { Write-Host $line }
                    if ($spellingExit -ne 0) {
                        Write-ErrorMsg 'Spelling errors detected in changed spell-checkable files.'
                        $unknownPrefixes = @()
                        foreach ($line in $spellingOutput) {
                            $text = [string]$line
                            # Width: unbounded (>=2) because cspell never
                            # emits monster tokens and a narrow upper
                            # bound (originally 5) let prefixes longer
                            # than 5 chars slip past the patch emitter —
                            # the exact fragility reviewed in P0-3.
                            $codeMatch = [regex]::Match($text, 'Unknown word \(([A-Z]{2,})\)')
                            if ($codeMatch.Success) {
                                $unknownPrefixes += $codeMatch.Groups[1].Value
                            }
                        }
                        $unknownPrefixes = @($unknownPrefixes | Sort-Object -Unique)
                        if ($unknownPrefixes.Count -gt 0) {
                            Write-Host ''
                            Write-Host '=== Detected unregistered lint-error-code prefix(es) ===' -ForegroundColor Red
                            Write-Host 'Copy-paste this patch into the root "words" array in cspell.json' -ForegroundColor Yellow
                            Write-Host '(append each quoted prefix as a new array element):' -ForegroundColor Yellow
                            Write-Host ''
                            foreach ($prefix in $unknownPrefixes) {
                                Write-Host ('    "{0}",' -f $prefix) -ForegroundColor White
                            }
                            Write-Host ''
                            Write-Host 'See scripts/validate-lint-error-codes.ps1 for the contract that' -ForegroundColor Cyan
                            Write-Host 'enforces this requirement once the prefix is registered.' -ForegroundColor Cyan
                            Write-Host ''
                        }
                        Write-Host 'Run: npm run lint:spelling' -ForegroundColor Cyan
                        $failureCount++
                    }
                }
                finally {
                    Pop-Location
                }
            }
            finally {
                Remove-Item -LiteralPath $spellingFileList -ErrorAction SilentlyContinue
            }
        }
    }
}

if ($cspellConfigChanged) {
    Write-Host '[agent-preflight] Validating cspell.json configuration...' -ForegroundColor Blue
    if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
        Write-ErrorMsg 'Node.js is required to validate cspell.json. Install Node.js/npm and run npm install.'
        $failureCount++
    }
    else {
        if ($Fix) {
            Push-Location $repoRoot
            try {
                $cspellFixOutput = & node (Join-Path $repoRoot 'scripts/lint-cspell-config.js') --fix 2>&1
                $cspellFixExit = $LASTEXITCODE
                foreach ($line in $cspellFixOutput) { Write-Host $line }
            }
            finally {
                Pop-Location
            }

            if ($cspellFixExit -ne 0) {
                Write-ErrorMsg "cspell.json configuration auto-fix failed with exit code $cspellFixExit."
                $failureCount++
            }
            else {
                if ($prettierAvailable) {
                    $cspellPrettierExit = Invoke-PrettierOnPaths `
                        -RepoRoot $repoRoot `
                        -Arguments @('--write', '--log-level', 'warn') `
                        -Paths @('cspell.json')
                    if ($cspellPrettierExit -ne 0) {
                        Write-ErrorMsg "Prettier formatting failed for cspell.json after configuration auto-fix with exit code $cspellPrettierExit."
                        $failureCount++
                    }
                }

                $stagedPaths = Get-GitStagedPaths -RepoRoot $repoRoot
                if ($stagedPaths.Contains('cspell.json')) {
                    if (-not (Add-PathsToGitIndexWithRetry -RepoRoot $repoRoot -Paths @('cspell.json'))) {
                        Write-ErrorMsg 'Failed to stage cspell.json after configuration auto-fix. Git index.lock contention or another git error is likely.'
                        Write-Host 'Close other git operations, then re-run npm run agent:preflight:fix.' -ForegroundColor Cyan
                        $failureCount++
                    }
                }
            }
        }

        Push-Location $repoRoot
        try {
            $cspellConfigOutput = & node (Join-Path $repoRoot 'scripts/lint-cspell-config.js') 2>&1
            $cspellConfigExit = $LASTEXITCODE
            foreach ($line in $cspellConfigOutput) { Write-Host $line }
        }
        finally {
            Pop-Location
        }

        if ($cspellConfigExit -ne 0) {
            Write-ErrorMsg 'cspell.json configuration issues detected.'
            Write-Host 'Run: npm run agent:preflight:fix' -ForegroundColor Cyan
            $failureCount++
        }
    }
}

if ($lintErrorCodeContractTargets.Count -gt 0) {
    Write-Host '[agent-preflight] Validating lint-error-code cspell coverage...' -ForegroundColor Blue
    & (Join-Path $repoRoot 'scripts/validate-lint-error-codes.ps1') -VerboseOutput:$VerboseOutput
    if ($LASTEXITCODE -ne 0) {
        $failureCount++
    }
}

if ($eolTargets.Count -gt 0) {
    if ($Fix) {
        Write-Host '[agent-preflight] Normalizing line endings on changed files...' -ForegroundColor Blue
        Push-Location $repoRoot
        try {
            & (Join-Path $repoRoot 'scripts/normalize-eol.ps1') -Paths $eolTargets
            $normalizeEolExit = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }

        if ($normalizeEolExit -ne 0) {
            $failureCount++
        }
        else {
            $stagedPaths = Get-GitStagedPaths -RepoRoot $repoRoot
            $stagedEolTargets = @($eolTargets | Where-Object { $stagedPaths.Contains($_) })
            if ($stagedEolTargets.Count -gt 0) {
                if (-not (Add-PathsToGitIndexWithRetry -RepoRoot $repoRoot -Paths $stagedEolTargets)) {
                    Write-ErrorMsg 'Failed to stage EOL-normalized files. Git index.lock contention or another git error is likely.'
                    foreach ($path in $stagedEolTargets) {
                        Write-Host "  $path" -ForegroundColor Yellow
                    }
                    Write-Host 'Close other git operations, then re-run npm run agent:preflight:fix.' -ForegroundColor Cyan
                    $failureCount++
                }
            }
        }
    }

    Write-Host '[agent-preflight] Checking line endings on changed files...' -ForegroundColor Blue
    Push-Location $repoRoot
    try {
        & (Join-Path $repoRoot 'scripts/check-eol.ps1') -VerboseOutput:$VerboseOutput -Paths $eolTargets
        $checkEolExit = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($checkEolExit -ne 0) {
        Write-ErrorMsg 'Line ending issues detected in changed files.'
        Write-Host 'Run: npm run agent:preflight:fix' -ForegroundColor Cyan
        $failureCount++
    }
}

if ($testFiles.Count -gt 0) {
    if ($Fix) {
        Write-Host '[agent-preflight] Auto-fixing Unity null assertions in changed tests...' -ForegroundColor Blue
        & (Join-Path $repoRoot 'scripts/lint-tests.ps1') -FixNullChecks -Paths $testFiles
        if ($LASTEXITCODE -ne 0) {
            $failureCount++
        }
    }

    Write-Host '[agent-preflight] Running test linter on changed tests...' -ForegroundColor Blue
    & (Join-Path $repoRoot 'scripts/lint-tests.ps1') -Paths $testFiles -VerboseOutput:$VerboseOutput
    if ($LASTEXITCODE -ne 0) {
        $failureCount++
    }
}

if ($csharpTargets.Count -gt 0) {
    Write-Host '[agent-preflight] Checking duplicate using directives on changed C# files...' -ForegroundColor Blue
    Push-Location $repoRoot
    try {
        & (Join-Path $repoRoot 'scripts/lint-duplicate-usings.ps1') -Paths $csharpTargets
        if ($LASTEXITCODE -ne 0) {
            $failureCount++
        }
    }
    finally {
        Pop-Location
    }

    $regionViolations = New-Object System.Collections.Generic.List[string]
    foreach ($path in $csharpTargets) {
        $fullPath = Join-Path -Path $repoRoot -ChildPath $path
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            continue
        }

        $matches = Select-String -LiteralPath $fullPath -Pattern '^\s*#\s*(region|endregion)' -CaseSensitive:$false
        foreach ($match in $matches) {
            $regionViolations.Add("${path}:$($match.LineNumber): $($match.Line.Trim())") | Out-Null
        }
    }

    if ($regionViolations.Count -gt 0) {
        Write-ErrorMsg 'Forbidden #region/#endregion directives detected in changed C# files:'
        foreach ($violation in $regionViolations) {
            Write-Host "  $violation" -ForegroundColor Yellow
        }
        $failureCount++
    }
}

if ($metaRelevantPaths.Count -gt 0) {
    Write-Host '[agent-preflight] Checking Unity .meta coverage for changed paths...' -ForegroundColor Blue

    $missingMetaTargets = New-Object System.Collections.Generic.List[string]
    $dirSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($path in $metaRelevantPaths) {
        $fullPath = Join-Path -Path $repoRoot -ChildPath $path
        if (-not (Test-Path -LiteralPath $fullPath)) {
            continue
        }

        if (-not (Test-Path -LiteralPath "$fullPath.meta")) {
            $missingMetaTargets.Add($path) | Out-Null
        }

        $directory = if (Test-Path -LiteralPath $fullPath -PathType Container) {
            $path
        }
        else {
            Split-Path -Path $path -Parent
        }

        while (-not [string]::IsNullOrWhiteSpace($directory) -and $directory -ne '.') {
            if ($sourceRoots -contains $directory) {
                break
            }

            $dirSet.Add($directory) | Out-Null
            $directory = Split-Path -Path $directory -Parent
        }
    }

    foreach ($directory in $dirSet) {
        $dirPath = Join-Path -Path $repoRoot -ChildPath $directory
        if (-not (Test-Path -LiteralPath $dirPath -PathType Container)) {
            continue
        }

        if (-not (Test-Path -LiteralPath "$dirPath.meta")) {
            $missingMetaTargets.Add($directory) | Out-Null
        }
    }

    if ($missingMetaTargets.Count -gt 0 -and $Fix) {
        if (Get-Command bash -ErrorAction SilentlyContinue) {
            foreach ($target in ($missingMetaTargets | Sort-Object -Unique)) {
                Write-Info "Generating meta for $target"
                & bash (Join-Path $repoRoot 'scripts/generate-meta.sh') $target
                if ($LASTEXITCODE -ne 0) {
                    Write-ErrorMsg "Failed to auto-generate .meta for: $target"
                    $failureCount++
                }
            }

            # Re-check after generation
            $remaining = @()
            foreach ($target in ($missingMetaTargets | Sort-Object -Unique)) {
                $targetPath = Join-Path -Path $repoRoot -ChildPath $target
                if (-not (Test-Path -LiteralPath "$targetPath.meta")) {
                    $remaining += $target
                }
            }

            $missingMetaTargets = New-Object System.Collections.Generic.List[string]
            foreach ($target in $remaining) {
                $missingMetaTargets.Add($target) | Out-Null
            }
        }
        else {
            Write-WarningMsg 'bash not found; cannot auto-generate missing .meta files.'
        }
    }

    if ($missingMetaTargets.Count -gt 0) {
        Write-ErrorMsg 'Missing .meta files detected for changed paths:'
        foreach ($target in ($missingMetaTargets | Sort-Object -Unique)) {
            Write-Host "  $target" -ForegroundColor Yellow
        }
        Write-Host 'Generate with: ./scripts/generate-meta.sh <path>' -ForegroundColor Cyan
        $failureCount++
    }

        $stagedPaths = Get-GitStagedPaths -RepoRoot $repoRoot
    if ($stagedPaths.Count -gt 0) {
        $unstagedMetaCompanionsSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        $pathScopedMetaRelevantSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($metaPath in $metaRelevantPaths) {
            $pathScopedMetaRelevantSet.Add($metaPath) | Out-Null
        }

        foreach ($stagedPath in $stagedPaths) {
            if (($null -ne $Paths -and $Paths.Count -gt 0) -and -not $pathScopedMetaRelevantSet.Contains($stagedPath)) {
                continue
            }

            if (-not (Test-MetaRequiredPath -RelativePath $stagedPath)) {
                continue
            }

            $stagedFullPath = Join-Path -Path $repoRoot -ChildPath $stagedPath
            if (-not (Test-Path -LiteralPath $stagedFullPath)) {
                continue
            }

            $fileMetaPath = "$stagedPath.meta"
            $fileMetaFullPath = Join-Path -Path $repoRoot -ChildPath $fileMetaPath
            if ((Test-Path -LiteralPath $fileMetaFullPath) -and -not $stagedPaths.Contains($fileMetaPath)) {
                $unstagedMetaCompanionsSet.Add($fileMetaPath) | Out-Null
            }

            $directory = if (Test-Path -LiteralPath $stagedFullPath -PathType Container) {
                $stagedPath
            }
            else {
                (Split-Path -Path $stagedPath -Parent).Replace('\', '/')
            }

            while (-not [string]::IsNullOrWhiteSpace($directory) -and $directory -ne '.') {
                if ($sourceRoots -contains $directory) {
                    break
                }

                $directoryMetaPath = "$directory.meta"
                $directoryMetaFullPath = Join-Path -Path $repoRoot -ChildPath $directoryMetaPath
                if ((Test-Path -LiteralPath $directoryMetaFullPath) -and -not $stagedPaths.Contains($directoryMetaPath)) {
                    $unstagedMetaCompanionsSet.Add($directoryMetaPath) | Out-Null
                }

                $directory = Split-Path -Path $directory -Parent
                if (-not [string]::IsNullOrWhiteSpace($directory)) {
                    $directory = $directory.Replace('\', '/')
                }
            }
        }

        $unstagedMetaCompanions = @($unstagedMetaCompanionsSet | Sort-Object)
        if ($unstagedMetaCompanions.Count -gt 0) {
            if ($Fix) {
                Write-Host '[agent-preflight] Auto-staging unstaged .meta companions for staged files...' -ForegroundColor Blue
                if (Add-PathsToGitIndexWithRetry -RepoRoot $repoRoot -Paths $unstagedMetaCompanions) {
                    Write-Host "[agent-preflight] Staged $($unstagedMetaCompanions.Count) .meta companion file(s)." -ForegroundColor Green
                }
                else {
                    Write-ErrorMsg 'Failed to stage one or more .meta companion files. Git index.lock contention or another git error is likely.'
                    foreach ($metaPath in $unstagedMetaCompanions) {
                        Write-Host "  $metaPath" -ForegroundColor Yellow
                    }
                    Write-Host 'Close other git operations, then re-run npm run agent:preflight:fix.' -ForegroundColor Cyan
                    $failureCount++
                }
            }
            else {
                Write-ErrorMsg 'Unstaged .meta companion files detected for staged paths:'
                foreach ($metaPath in $unstagedMetaCompanions) {
                    Write-Host "  $metaPath" -ForegroundColor Yellow
                }
                Write-Host 'Run with -Fix to auto-stage these files (npm run agent:preflight:fix).' -ForegroundColor Cyan
                $failureCount++
            }
        }
    }
}

if ($failureCount -gt 0) {
    Write-Host "[agent-preflight] Failed with $failureCount check group(s) reporting issues." -ForegroundColor Red
    exit 1
}

Write-Host '[agent-preflight] All relevant changed-file checks passed.' -ForegroundColor Green
exit 0
