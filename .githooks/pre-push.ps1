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
$script:TreeRegionCheckShas = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$script:RegionRecoveryPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$script:RegionPattern = '^[[:space:]]*#[[:space:]]*(region|endregion)'

function Write-HookInfo {
    param([string]$Message)
    Write-Host "[pre-push] $Message"
}

function Write-HookError {
    param([string]$Message)
    Write-Host "[pre-push] ERROR: $Message" -ForegroundColor Red
}

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FileName,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [AllowNull()]
        [string]$StandardInput = $null,
        [switch]$AllowFailure
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $null -ne $StandardInput
    $startInfo.UseShellExecute = $false
    $startInfo.WorkingDirectory = $repoRoot

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void]$process.Start()
    if ($null -ne $StandardInput) {
        $process.StandardInput.Write($StandardInput)
        $process.StandardInput.Close()
    }
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
        [AllowNull()]
        [string]$StandardInput = $null,
        [switch]$AllowFailure
    )

    return Invoke-Native -FileName 'git' -Arguments $Arguments -StandardInput $StandardInput -AllowFailure:$AllowFailure
}

function Test-ZeroObjectId {
    param([AllowNull()][string]$ObjectId)

    return (-not [string]::IsNullOrWhiteSpace($ObjectId) -and $ObjectId -match '^0+$')
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
    param(
        [AllowNull()]
        [string]$Text,
        [AllowNull()]
        [string]$TreeId = $null,
        [AllowNull()]
        [System.Collections.Generic.HashSet[string]]$AllowedPaths = $null,
        [switch]$AsObjects
    )

    $violations = [System.Collections.Generic.List[object]]::new()
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
        if (-not [string]::IsNullOrWhiteSpace($TreeId) -and $path.StartsWith("${TreeId}:", [System.StringComparison]::Ordinal)) {
            $path = $path.Substring($TreeId.Length + 1)
        }
        $lineNumber = $Text.Substring($pathEnd + 1, $lineEnd - $pathEnd - 1)
        $lineText = $Text.Substring($lineEnd + 1, $textEnd - $lineEnd - 1).TrimEnd("`r")
        if ($null -eq $AllowedPaths -or $AllowedPaths.Contains($path)) {
            if ($AsObjects) {
                $violations.Add([pscustomobject]@{
                        Path = $path
                        Text = "${path}:${lineNumber}:$lineText"
                    }) | Out-Null
            }
            else {
                $violations.Add("${path}:${lineNumber}:$lineText") | Out-Null
            }
        }

        $cursor = $textEnd + 1
    }

    return $violations
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

function Add-UniquePath {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Set,
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not [string]::IsNullOrWhiteSpace($Path)) {
        [void]$Set.Add($Path.Replace('\', '/'))
    }
}

function Get-NewBranchBaseSha {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LocalSha
    )

    $baseCandidates = [System.Collections.Generic.List[string]]::new()
    $upstream = Invoke-Git -Arguments @('rev-parse', '--abbrev-ref', '--symbolic-full-name', '@{upstream}') -AllowFailure
    if ($upstream.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($upstream.Stdout)) {
        $baseCandidates.Add($upstream.Stdout.Trim()) | Out-Null
    }

    $originHead = Invoke-Git -Arguments @('symbolic-ref', '--quiet', '--short', 'refs/remotes/origin/HEAD') -AllowFailure
    if ($originHead.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($originHead.Stdout)) {
        $baseCandidates.Add($originHead.Stdout.Trim()) | Out-Null
    }

    foreach ($candidate in @('origin/main', 'origin/master', 'main', 'master')) {
        $baseCandidates.Add($candidate) | Out-Null
    }

    foreach ($candidate in @($baseCandidates | Sort-Object -Unique)) {
        $candidateRev = Invoke-Git -Arguments @('rev-parse', '--verify', "$candidate^{commit}") -AllowFailure
        if ($candidateRev.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($candidateRev.Stdout)) {
            continue
        }

        if ($candidateRev.Stdout.Trim() -eq $LocalSha) {
            continue
        }

        $mergeBase = Invoke-Git -Arguments @('merge-base', $candidate, $LocalSha) -AllowFailure
        if ($mergeBase.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($mergeBase.Stdout)) {
            $baseSha = $mergeBase.Stdout.Trim()
            if ($baseSha -ne $LocalSha) {
                return $baseSha
            }
        }
    }

    return $null
}

function Get-RegionChangedPathsForRange {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseSha,
        [Parameter(Mandatory = $true)]
        [string]$LocalSha
    )

    $range = "$BaseSha..$LocalSha"
    $diff = Invoke-Git -Arguments @(
        'diff',
        '--name-only',
        '-z',
        '--diff-filter=ACMRTUXB',
        '-G',
        $script:RegionPattern,
        $range,
        '--',
        '*.cs'
    ) -AllowFailure
    if ($diff.ExitCode -gt 1) {
        throw "git diff failed while checking pushed C# region changes."
    }

    return Split-NulList -Text $diff.Stdout
}

function Get-RegionChangedPathsForRefUpdate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LocalSha,
        [Parameter(Mandatory = $true)]
        [string]$RemoteSha
    )

    if (Test-ZeroObjectId -ObjectId $RemoteSha) {
        $baseSha = Get-NewBranchBaseSha -LocalSha $LocalSha
        if ([string]::IsNullOrWhiteSpace($baseSha)) {
            [void]$script:TreeRegionCheckShas.Add($LocalSha)
            return @()
        }

        return Get-RegionChangedPathsForRange -BaseSha $baseSha -LocalSha $LocalSha
    }

    return Get-RegionChangedPathsForRange -BaseSha $RemoteSha -LocalSha $LocalSha
}

function Write-AgentPreflightFixHint {
    param([string[]]$Paths)

    $pathListResult = Invoke-Git -Arguments @('rev-parse', '--git-path', 'pre-push-agent-preflight-paths.bin') -AllowFailure
    $pathList = if ($pathListResult.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($pathListResult.Stdout)) {
        $pathListResult.Stdout.Trim()
    }
    else {
        Join-Path $repoRoot '.git/pre-push-agent-preflight-paths.bin'
    }

    $payload = if ($Paths.Count -gt 0) { ($Paths -join ([string][char]0)) + ([string][char]0) } else { '' }
    [System.IO.File]::WriteAllBytes($pathList, [System.Text.UTF8Encoding]::new($false).GetBytes($payload))

    Write-Host 'Automated recovery (path-scoped):'
    Write-Host "  npm run agent:preflight:fix -- -PathList `"$pathList`""
    Write-Host 'Then commit generated fixes and push again.'
}

function Test-RegionChangesInPushedCSharp {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$ShaToPaths
    )

    $violations = [System.Collections.Generic.List[string]]::new()
    $chunkSize = 200
    foreach ($sha in $ShaToPaths.Keys) {
        $changedPaths = @($ShaToPaths[$sha] | Sort-Object -Unique)
        $pathspecs = @(
            $changedPaths |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                ForEach-Object { ConvertTo-LiteralPathspec -Path $_ }
        )
        if ($pathspecs.Count -eq 0) {
            continue
        }

        for ($offset = 0; $offset -lt $pathspecs.Count; $offset += $chunkSize) {
            $end = [Math]::Min($offset + $chunkSize - 1, $pathspecs.Count - 1)
            $chunk = @($pathspecs[$offset..$end])
            $grep = Invoke-Git -Arguments (@('grep', '-n', '-I', '-E', '-z', $script:RegionPattern, $sha, '--') + $chunk) -AllowFailure
            if ($grep.ExitCode -eq 0) {
                foreach ($violation in @(ConvertFrom-GitGrepRegionOutput -Text $grep.Stdout -TreeId $sha -AsObjects)) {
                    $violations.Add($violation.Text) | Out-Null
                    Add-UniquePath -Set $script:RegionRecoveryPaths -Path $violation.Path
                }
            }
            elseif ($grep.ExitCode -gt 1) {
                throw "git grep failed while checking pushed C# region changes."
            }
        }
    }

    return Write-RegionViolations -Violations $violations
}

function Test-RegionsInPushedCSharpTree {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Shas
    )

    $violations = [System.Collections.Generic.List[string]]::new()
    foreach ($sha in @($Shas | Sort-Object -Unique)) {
        $grep = Invoke-Git -Arguments @('grep', '-n', '-I', '-E', '-z', $script:RegionPattern, $sha, '--', '*.cs') -AllowFailure
        if ($grep.ExitCode -eq 0) {
            foreach ($violation in @(ConvertFrom-GitGrepRegionOutput -Text $grep.Stdout -TreeId $sha -AsObjects)) {
                $violations.Add($violation.Text) | Out-Null
                Add-UniquePath -Set $script:RegionRecoveryPaths -Path $violation.Path
            }
        }
        elseif ($grep.ExitCode -gt 1) {
            throw "git grep failed while checking pushed C# tree."
        }
    }

    return Write-RegionViolations -Violations $violations
}

function Write-RegionViolations {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]]$Violations
    )

    if ($Violations.Count -eq 0) {
        return $true
    }

    Write-Host ''
    Write-HookError 'C# regions (#region/#endregion) are forbidden.'
    foreach ($violation in @($Violations | Select-Object -First 50)) {
        Write-Host "  $violation" -ForegroundColor Yellow
    }
    Write-Host ''
    Write-Host 'Remove all #region and #endregion directives before pushing.' -ForegroundColor Cyan
    Write-Host ''
    return $false
}

try {
    Push-Location $repoRoot
    Remove-GitIgnoredHookArtifacts

    $stdin = [Console]::In.ReadToEnd()
    $lines = @($stdin -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($lines.Count -eq 0) {
        Write-HookInfo 'No files requiring pre-push checks, skipping.'
        exit 0
    }

    $hasRefs = $false
    $hasRelevantChanges = $false
    $allChanged = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $shaToCSharp = @{}

    foreach ($line in $lines) {
        $parts = @($line -split '\s+')
        if ($parts.Count -lt 4) {
            continue
        }

        $localSha = $parts[1]
        $remoteSha = $parts[3]
        if (Test-ZeroObjectId -ObjectId $localSha) {
            continue
        }

        $hasRefs = $true
        $changedPaths = @(Get-RegionChangedPathsForRefUpdate -LocalSha $localSha -RemoteSha $remoteSha)
        if ($script:TreeRegionCheckShas.Contains($localSha)) {
            $hasRelevantChanges = $true
            if (-not $shaToCSharp.ContainsKey($localSha)) {
                $shaToCSharp[$localSha] = [System.Collections.Generic.List[string]]::new()
            }
        }
        foreach ($path in $changedPaths) {
            Add-UniquePath -Set $allChanged -Path $path
            if ($path -like '*.cs') {
                $hasRelevantChanges = $true
                if (-not $shaToCSharp.ContainsKey($localSha)) {
                    $shaToCSharp[$localSha] = [System.Collections.Generic.List[string]]::new()
                }
                $shaToCSharp[$localSha].Add($path) | Out-Null
            }
        }
    }

    if (-not $hasRefs -or -not $hasRelevantChanges) {
        Write-HookInfo 'No files requiring pre-push checks, skipping.'
        exit 0
    }

    $changedPathList = @($allChanged | Sort-Object)
    if ($changedPathList.Count -gt 0) {
        Write-HookInfo "Checking $($changedPathList.Count) changed file(s)."
    }
    else {
        Write-HookInfo 'Checking pushed C# tree.'
    }

    if ($shaToCSharp.Count -gt 0) {
        Write-HookInfo 'Checking pushed C# changes for forbidden #region directives.'
        $regionCheckPassed = $true
        if ($script:TreeRegionCheckShas.Count -gt 0) {
            $regionCheckPassed = (Test-RegionsInPushedCSharpTree -Shas @($script:TreeRegionCheckShas)) -and $regionCheckPassed
        }

        $changedRegionShaToPaths = @{}
        foreach ($sha in $shaToCSharp.Keys) {
            if ($script:TreeRegionCheckShas.Contains($sha)) {
                continue
            }

            if ($shaToCSharp[$sha].Count -gt 0) {
                $changedRegionShaToPaths[$sha] = $shaToCSharp[$sha]
            }
        }

        if ($changedRegionShaToPaths.Count -gt 0) {
            $regionCheckPassed = (Test-RegionChangesInPushedCSharp -ShaToPaths $changedRegionShaToPaths) -and $regionCheckPassed
        }

        if (-not $regionCheckPassed) {
            Write-Host 'Pre-push checks FAILED.'
            $recoveryPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            foreach ($path in $changedPathList) {
                Add-UniquePath -Set $recoveryPaths -Path $path
            }
            foreach ($path in $script:RegionRecoveryPaths) {
                Add-UniquePath -Set $recoveryPaths -Path $path
            }
            Write-AgentPreflightFixHint -Paths @($recoveryPaths | Sort-Object)
            Write-Host 'To skip in emergencies: git push --no-verify (CI will still validate)'
            exit 1
        }
        Write-HookInfo 'No #region directives found.'
    }

    Write-HookInfo 'Pre-push checks complete. Push proceeding.'
    exit 0
}
catch {
    Write-HookError $_.Exception.Message
    exit 1
}
finally {
    Pop-Location
}
