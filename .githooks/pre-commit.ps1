#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$HookArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptHookDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRootResult = (& git rev-parse --show-toplevel 2>$null)
$repoRoot = if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($repoRootResult)) {
    ([string]$repoRootResult).Trim()
}
else {
    (Resolve-Path -LiteralPath (Join-Path $scriptHookDir '..')).Path
}
$hookDir = Join-Path $repoRoot '.githooks'
$scriptsDir = Join-Path $repoRoot 'scripts'

. (Join-Path $scriptsDir 'git-staging-helpers.ps1')

function Write-HookInfo {
    param([string]$Message)
    Write-Host "[pre-commit] $Message"
}

function Write-HookError {
    param([string]$Message)
    Write-Host "[pre-commit] ERROR: $Message" -ForegroundColor Red
}

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FileName,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.WorkingDirectory = $repoRoot

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    if ($process.ExitCode -ne 0 -and -not $AllowFailure) {
        if (-not [string]::IsNullOrWhiteSpace($stderr)) {
            Write-Host $stderr.TrimEnd() -ForegroundColor Yellow
        }
        throw "$FileName exited with code $($process.ExitCode): $($Arguments -join ' ')"
    }

    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        Stdout = $stdout
        Stderr = $stderr
    }
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    return Invoke-Native -FileName 'git' -Arguments $Arguments -AllowFailure:$AllowFailure
}

function Split-NulList {
    param([AllowNull()][string]$Text)

    if ([string]::IsNullOrEmpty($Text)) {
        return @()
    }

    return @(
        $Text -split ([string][char]0) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { ([string]$_).Replace('\', '/') }
    )
}

function ConvertTo-LiteralPathspec {
    param([Parameter(Mandatory = $true)][string]$Path)

    return ":(literal)$Path"
}

function ConvertFrom-GitGrepRegionOutput {
    param([AllowNull()][string]$Text)

    $violations = [System.Collections.Generic.List[string]]::new()
    if ([string]::IsNullOrEmpty($Text)) {
        return $violations
    }

    $cursor = 0
    while ($cursor -lt $Text.Length) {
        $pathEnd = $Text.IndexOf([char]0, $cursor)
        if ($pathEnd -lt 0) {
            break
        }

        $lineEnd = $Text.IndexOf([char]0, $pathEnd + 1)
        if ($lineEnd -lt 0) {
            break
        }

        $textEnd = $Text.IndexOf("`n", $lineEnd + 1)
        if ($textEnd -lt 0) {
            $textEnd = $Text.Length
        }

        $path = $Text.Substring($cursor, $pathEnd - $cursor)
        $lineNumber = $Text.Substring($pathEnd + 1, $lineEnd - $pathEnd - 1)
        $lineText = $Text.Substring($lineEnd + 1, $textEnd - $lineEnd - 1).TrimEnd("`r")
        $violations.Add("${path}:${lineNumber}: $lineText") | Out-Null

        $cursor = $textEnd + 1
    }

    return $violations
}

function Get-StagedRegionViolations {
    param([string[]]$Paths)

    $violations = [System.Collections.Generic.List[string]]::new()
    $pathspecs = @(
        $Paths |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique |
            ForEach-Object { ConvertTo-LiteralPathspec -Path $_ }
    )
    if ($pathspecs.Count -eq 0) {
        return $violations
    }

    $pattern = '^[[:space:]]*#[[:space:]]*(region|endregion)'
    $chunkSize = 200
    for ($offset = 0; $offset -lt $pathspecs.Count; $offset += $chunkSize) {
        $end = [Math]::Min($offset + $chunkSize - 1, $pathspecs.Count - 1)
        $chunk = @($pathspecs[$offset..$end])
        $grep = Invoke-Git -Arguments (@('grep', '--cached', '-n', '-I', '-E', '-z', $pattern, '--') + $chunk) -AllowFailure
        if ($grep.ExitCode -eq 0) {
            foreach ($violation in @(ConvertFrom-GitGrepRegionOutput -Text $grep.Stdout)) {
                $violations.Add($violation) | Out-Null
            }
        }
        elseif ($grep.ExitCode -gt 1) {
            throw "git grep failed while checking staged C# regions."
        }
    }

    return $violations
}

function Get-StagedPaths {
    $result = Invoke-Git -Arguments @('diff', '--cached', '--name-only', '--diff-filter=ACMR', '-z')
    return Split-NulList -Text $result.Stdout
}

function Get-UnstagedPathSet {
    $result = Invoke-Git -Arguments @('diff', '--name-only', '--diff-filter=ACMRTUXB', '-z') -AllowFailure
    $set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    if ($result.ExitCode -ne 0) {
        return $set
    }

    foreach ($path in @(Split-NulList -Text $result.Stdout)) {
        [void]$set.Add($path)
    }
    return $set
}

function Invoke-HookPowerShellScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ScriptRelativePath,
        [string[]]$Arguments = @(),
        [int[]]$AllowedExitCodes = @(0)
    )

    $scriptPath = Join-Path $repoRoot $ScriptRelativePath
    $pwshPath = (Get-Process -Id $PID).Path
    if ([string]::IsNullOrWhiteSpace($pwshPath)) {
        $pwshPath = if (Get-Command pwsh -ErrorAction SilentlyContinue) { 'pwsh' } else { 'powershell' }
    }

    $invokeArgs = @('-NoProfile')
    if ($PSVersionTable.PSEdition -ne 'Core') {
        $invokeArgs += @('-ExecutionPolicy', 'Bypass')
    }
    $invokeArgs += @('-File', $scriptPath)
    $invokeArgs += $Arguments

    & $pwshPath @invokeArgs
    $exitCode = $LASTEXITCODE
    if ($AllowedExitCodes -notcontains $exitCode) {
        throw "$ScriptRelativePath exited with code $exitCode."
    }
}

function Get-HookNormalizedUniquePaths {
    param([AllowNull()][string[]]$Paths)

    if ($null -eq $Paths -or $Paths.Count -eq 0) {
        return @()
    }

    return @(
        $Paths |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { ([string]$_).Replace('\', '/') } |
            Sort-Object -Unique
    )
}

function Assert-HookWholeFileAutoFixSafe {
    param(
        [string]$Context,
        [string[]]$Paths,
        [ValidateSet('before', 'after')]
        [string]$Phase = 'before',
        [switch]$AllowInitiallyUnstaged
    )

    if ($AllowInitiallyUnstaged) {
        return
    }

    $items = @(Get-HookNormalizedUniquePaths -Paths $Paths)
    if ($items.Count -eq 0) {
        return
    }

    $partialPaths = @($items | Where-Object {
        $null -ne $script:InitiallyUnstagedPaths -and $script:InitiallyUnstagedPaths.Contains($_)
    })
    if ($partialPaths.Count -eq 0) {
        return
    }

    Write-HookError "Refusing to auto-stage whole file(s) with pre-existing unstaged changes ${Phase} ${Context}."
    foreach ($item in $partialPaths) {
        Write-Host "  $item" -ForegroundColor Yellow
    }
    Write-Host 'Run npm run agent:preflight:fix before staging, or stage the intended hunks and retry.' -ForegroundColor Cyan
    exit 1
}

function Add-HookPathsToIndex {
    param(
        [string]$Context,
        [string[]]$Paths
    )

    $items = @(Get-HookNormalizedUniquePaths -Paths $Paths)
    if ($items.Count -eq 0) {
        return
    }

    Assert-HookWholeFileAutoFixSafe `
        -Context $Context `
        -Paths $items `
        -Phase 'after' `
        -AllowInitiallyUnstaged:($Context -match '\.meta companion')

    $repositoryInfo = Get-GitRepositoryInfo
    $exitCode = Invoke-GitAddWithRetry -Items $items -IndexLockPath $repositoryInfo.IndexLockPath
    if ($exitCode -ne 0) {
        Write-HookError "Failed to stage files ($Context)."
        foreach ($item in $items) {
            Write-Host "  $item" -ForegroundColor Yellow
        }
        Write-Host 'Close other git tools, then run npm run agent:preflight:fix and retry.' -ForegroundColor Cyan
        exit $exitCode
    }
}

function Test-PathDirtyOrUntracked {
    param([string]$Path)

    $result = Invoke-Git -Arguments @('status', '--porcelain', '--', $Path) -AllowFailure
    return ($result.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($result.Stdout))
}

function Remove-GitIgnoredHookArtifacts {
    $rootArtifactExtensions = @('txt', 'out', 'err')
    $hookArtifactExtensions = @('txt', 'log', 'out', 'err', 'tmp')
    $artifactExtensions = @($rootArtifactExtensions + $hookArtifactExtensions | Sort-Object -Unique)
    $hookNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    if (-not (Test-Path -LiteralPath $hookDir -PathType Container)) {
        return
    }

    foreach ($hook in Get-ChildItem -LiteralPath $hookDir -File -ErrorAction SilentlyContinue) {
        if ($hook.Name -like '*.sample' -or $artifactExtensions -contains $hook.Extension.TrimStart('.')) {
            continue
        }

        $name = if ($hook.Extension) { [System.IO.Path]::GetFileNameWithoutExtension($hook.Name) } else { $hook.Name }
        if (-not [string]::IsNullOrWhiteSpace($name)) {
            [void]$hookNames.Add($name)
        }
    }

    $candidates = [System.Collections.Generic.List[string]]::new()
    foreach ($hookName in $hookNames) {
        foreach ($extension in $rootArtifactExtensions) {
            $relative = "$hookName.$extension"
            if (Test-Path -LiteralPath (Join-Path $repoRoot $relative) -PathType Leaf) {
                $candidates.Add($relative) | Out-Null
            }
        }
        foreach ($extension in $hookArtifactExtensions) {
            $relative = ".githooks/$hookName.$extension"
            if (Test-Path -LiteralPath (Join-Path $repoRoot $relative) -PathType Leaf) {
                $candidates.Add($relative) | Out-Null
            }
        }
    }

    $uniqueCandidates = @($candidates | Sort-Object -Unique)
    if ($uniqueCandidates.Count -eq 0) {
        return
    }

    $unsafe = [System.Collections.Generic.List[string]]::new()
    foreach ($relative in $uniqueCandidates) {
        $check = Invoke-Git -Arguments @('check-ignore', '-q', '--', $relative) -AllowFailure
        if ($check.ExitCode -eq 0) {
            Remove-Item -LiteralPath (Join-Path $repoRoot $relative) -Force
            Write-HookInfo "Removed stray hook artifact: $relative"
        }
        else {
            $unsafe.Add($relative) | Out-Null
        }
    }

    if ($unsafe.Count -gt 0) {
        Write-HookError 'Stray hook-output artifact file(s) were not confirmed gitignored:'
        foreach ($relative in $unsafe) {
            Write-Host "  $relative" -ForegroundColor Yellow
        }
        Write-Host 'Add the artifact pattern to .gitignore or remove the stale file, then retry.' -ForegroundColor Cyan
        exit 1
    }
}

function Add-FinalNewlines {
    param([string[]]$Paths)

    $patterns = @(
        '*.json', '*.jsonc', '*.asmdef', '*.asmref', '*.md', '*.markdown',
        '*.yml', '*.yaml', '*.js', '*.ts', '*.cs', '*.sh', '*.ps1', '*.txt',
        '*.html', '*.css', '*.xml'
    )
    $fixed = [System.Collections.Generic.List[string]]::new()

    foreach ($path in $Paths) {
        $matched = $false
        foreach ($pattern in $patterns) {
            if ($path -like $pattern) {
                $matched = $true
                break
            }
        }
        if (-not $matched) {
            continue
        }

        $fullPath = Join-Path $repoRoot $path
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            continue
        }

        $bytes = [System.IO.File]::ReadAllBytes($fullPath)
        if ($bytes.Length -eq 0 -or $bytes[$bytes.Length - 1] -eq 10) {
            continue
        }

        $appendBytes = if ($bytes.Length -gt 0 -and $bytes -contains 13) {
            [byte[]](13, 10)
        }
        else {
            [byte[]](10)
        }
        Assert-HookWholeFileAutoFixSafe -Context 'final newline normalization' -Paths @($path) -Phase 'before'
        [System.IO.File]::WriteAllBytes($fullPath, [byte[]]($bytes + $appendBytes))
        $fixed.Add($path) | Out-Null
    }

    if ($fixed.Count -gt 0) {
        Write-HookInfo "Added final newline to $($fixed.Count) file(s)."
        Add-HookPathsToIndex -Context 'final newline normalization' -Paths $fixed
    }
}

function Invoke-VersionSync {
    param([string[]]$StagedPaths)

    $syncTargets = [System.Collections.Generic.List[string]]::new()
    if ($StagedPaths -contains 'package.json' -or $StagedPaths -contains '.llm/context.md' -or $StagedPaths -contains 'docs/images/unity-helpers-banner.svg') {
        Assert-HookWholeFileAutoFixSafe `
            -Context 'banner version sync' `
            -Paths @('docs/images/unity-helpers-banner.svg', '.llm/context.md') `
            -Phase 'before'
        Write-HookInfo 'Synchronizing banner version metadata.'
        Invoke-HookPowerShellScript -ScriptRelativePath 'scripts/sync-banner-version.ps1'
        $syncTargets.Add('docs/images/unity-helpers-banner.svg') | Out-Null
        $syncTargets.Add('.llm/context.md') | Out-Null
    }

    $issueInputs = @(
        'package.json',
        'CHANGELOG.md',
        '.github/ISSUE_TEMPLATE/bug_report.yml',
        '.github/ISSUE_TEMPLATE/feature_request.yml'
    )
    if (@($issueInputs | Where-Object { $StagedPaths -contains $_ }).Count -gt 0) {
        Assert-HookWholeFileAutoFixSafe `
            -Context 'issue template version sync' `
            -Paths @('.github/ISSUE_TEMPLATE/bug_report.yml', '.github/ISSUE_TEMPLATE/feature_request.yml') `
            -Phase 'before'
        Write-HookInfo 'Synchronizing issue template versions.'
        Invoke-HookPowerShellScript -ScriptRelativePath 'scripts/sync-issue-template-versions.ps1'
        $syncTargets.Add('.github/ISSUE_TEMPLATE/bug_report.yml') | Out-Null
        $syncTargets.Add('.github/ISSUE_TEMPLATE/feature_request.yml') | Out-Null
    }

    $dirtyTargets = @($syncTargets | Where-Object { Test-PathDirtyOrUntracked -Path $_ })
    if ($dirtyTargets.Count -gt 0) {
        Add-HookPathsToIndex -Context 'version sync' -Paths $dirtyTargets
    }
}

function Invoke-FastCSharpChecks {
    param([string[]]$StagedPaths)

    $csharpFiles = @($StagedPaths | Where-Object { $_ -like '*.cs' })
    if ($csharpFiles.Count -eq 0) {
        return
    }

    $regionViolations = @(Get-StagedRegionViolations -Paths $csharpFiles)

    if ($regionViolations.Count -gt 0) {
        Write-HookError 'Forbidden #region/#endregion directives detected:'
        foreach ($violation in $regionViolations) {
            Write-Host "  $violation" -ForegroundColor Yellow
        }
        exit 1
    }
}

function Invoke-LlmChecks {
    param([string[]]$StagedPaths)

    $llmFiles = @($StagedPaths | Where-Object { $_ -like '.llm/*' })
    if ($llmFiles.Count -eq 0) {
        return
    }

    Write-HookInfo 'Validating LLM instruction consistency.'
    try {
        Invoke-HookPowerShellScript -ScriptRelativePath 'scripts/lint-llm-instructions.ps1'
    }
    catch {
        Write-HookInfo 'LLM instructions failed validation; attempting auto-fix.'
        Assert-HookWholeFileAutoFixSafe `
            -Context 'LLM instruction auto-fix' `
            -Paths @('.llm/context.md', '.llm/skills/index.md') `
            -Phase 'before'
        Invoke-HookPowerShellScript -ScriptRelativePath 'scripts/lint-llm-instructions.ps1' -Arguments @('-Fix')
        Add-HookPathsToIndex -Context 'LLM instruction auto-fix' -Paths @('.llm/context.md', '.llm/skills/index.md')
        Invoke-HookPowerShellScript -ScriptRelativePath 'scripts/lint-llm-instructions.ps1'
    }

    $sizeTargets = @($StagedPaths | Where-Object { $_ -eq '.llm/context.md' -or $_ -like '.llm/skills/*.md' })
    if ($sizeTargets.Count -gt 0) {
        Write-HookInfo 'Checking changed skill/context file sizes.'
        Invoke-HookPowerShellScript -ScriptRelativePath 'scripts/lint-skill-sizes.ps1' -Arguments (@('-Paths') + $sizeTargets)
    }
}

function Invoke-MetaChecks {
    param([string[]]$StagedPaths)

    $sourceRootPattern = '^(Runtime|Editor|Tests|Samples~|Shaders|Styles|URP|docs|scripts)/'
    $metaRequired = @($StagedPaths | Where-Object {
        $_ -match $sourceRootPattern -and
        $_ -notlike '*.meta' -and
        $_ -notlike '*/package-lock.json' -and
        $_ -notlike '*/Gemfile.lock' -and
        $_ -notlike '*.tmp' -and
        (Split-Path -Leaf $_) -notin @('.gitkeep', '.DS_Store', 'Thumbs.db') -and
        $_ -notlike '*.pyc' -and
        $_ -notlike '*.pyo' -and
        $_ -notlike '*.swp' -and
        $_ -notlike '*.swo'
    })

    if ($metaRequired.Count -eq 0) {
        return
    }

    $stagedSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($path in $StagedPaths) {
        [void]$stagedSet.Add($path)
    }

    $missing = [System.Collections.Generic.List[string]]::new()
    $unstaged = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $directories = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

    foreach ($path in $metaRequired) {
        $fullPath = Join-Path $repoRoot $path
        if (-not (Test-Path -LiteralPath $fullPath)) {
            continue
        }

        $metaPath = "$path.meta"
        $metaFullPath = Join-Path $repoRoot $metaPath
        if (-not (Test-Path -LiteralPath $metaFullPath -PathType Leaf)) {
            $missing.Add($path) | Out-Null
        }
        elseif (-not $stagedSet.Contains($metaPath) -and (Test-PathDirtyOrUntracked -Path $metaPath)) {
            [void]$unstaged.Add($metaPath)
        }

        $directory = if (Test-Path -LiteralPath $fullPath -PathType Container) {
            $path
        }
        else {
            (Split-Path -Path $path -Parent).Replace('\', '/')
        }

        while (-not [string]::IsNullOrWhiteSpace($directory) -and $directory -ne '.') {
            if ($directory -in @('Runtime', 'Editor', 'Tests', 'Samples~', 'Shaders', 'Styles', 'URP', 'docs', 'scripts')) {
                break
            }
            [void]$directories.Add($directory)
            $directory = Split-Path -Path $directory -Parent
            if (-not [string]::IsNullOrWhiteSpace($directory)) {
                $directory = $directory.Replace('\', '/')
            }
        }
    }

    foreach ($directory in $directories) {
        $directoryFullPath = Join-Path $repoRoot $directory
        if (-not (Test-Path -LiteralPath $directoryFullPath -PathType Container)) {
            continue
        }

        $metaPath = "$directory.meta"
        $metaFullPath = Join-Path $repoRoot $metaPath
        if (-not (Test-Path -LiteralPath $metaFullPath -PathType Leaf)) {
            $missing.Add($directory) | Out-Null
        }
        elseif (-not $stagedSet.Contains($metaPath) -and (Test-PathDirtyOrUntracked -Path $metaPath)) {
            [void]$unstaged.Add($metaPath)
        }
    }

    $unstagedPaths = @($unstaged | Sort-Object)
    if ($unstagedPaths.Count -gt 0) {
        Write-HookInfo "Auto-staging $($unstagedPaths.Count) dirty .meta companion file(s)."
        Add-HookPathsToIndex -Context '.meta companion auto-stage' -Paths $unstagedPaths
    }

    $missingPaths = @($missing | Sort-Object -Unique)
    if ($missingPaths.Count -gt 0) {
        Write-HookError 'Missing .meta files for staged paths:'
        foreach ($path in $missingPaths) {
            Write-Host "  $path" -ForegroundColor Yellow
        }
        Write-Host 'Run npm run agent:preflight:fix to generate recoverable .meta files.' -ForegroundColor Cyan
        exit 1
    }
}

try {
    Push-Location $repoRoot

    Remove-GitIgnoredHookArtifacts

    $stagedPaths = @(Get-StagedPaths)
    if ($stagedPaths.Count -eq 0) {
        Write-HookInfo 'No staged files to check.'
        exit 0
    }

    if (-not (Invoke-EnsureNoIndexLock)) {
        Write-HookError 'index.lock still held after waiting.'
        Write-Host 'Close competing git tools and retry the commit.' -ForegroundColor Cyan
        exit 1
    }

    $script:InitiallyUnstagedPaths = Get-UnstagedPathSet

    # The hook is intentionally limited to fast, local, auto-repairable checks.
    # Formatting, EOL normalization, spelling, Markdown lint, docs link lint,
    # CSharpier, test lint, duplicate-using lint, and license audits belong in
    # agent:preflight and CI so hooks remain a last-resort safety net.
    Invoke-VersionSync -StagedPaths $stagedPaths
    Add-FinalNewlines -Paths $stagedPaths

    $stagedPaths = @(Get-StagedPaths)
    Invoke-LlmChecks -StagedPaths $stagedPaths
    Invoke-FastCSharpChecks -StagedPaths $stagedPaths
    Invoke-MetaChecks -StagedPaths $stagedPaths

    Write-HookInfo 'Fast pre-commit checks passed.'
    exit 0
}
catch {
    Write-HookError $_.Exception.Message
    Write-Host 'Run npm run agent:preflight:fix before retrying the commit.' -ForegroundColor Cyan
    exit 1
}
finally {
    Pop-Location
}
