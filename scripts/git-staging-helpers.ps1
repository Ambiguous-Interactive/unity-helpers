# Shared helpers for git-aware formatter/linter scripts to avoid duplicated logic.
#
# IMPORTANT: All git index operations (git add, git reset, git checkout-index, etc.)
# MUST use the retry helpers from this module to handle index.lock contention properly.
# Never call `git add` directly in scripts - always use Invoke-GitAddWithRetry.
#
# The index.lock file is created by git during any operation that modifies the index.
# When using tools like lazygit or running multiple hooks, concurrent operations can
# cause "fatal: Unable to create '.git/index.lock': File exists" errors.
#
# Environment variables:
#   GIT_STAGING_VERBOSE   - Set to "1" to enable verbose logging for debugging
#   GIT_LOCK_MAX_ATTEMPTS - Max retry attempts for git add (default: 30)
#   GIT_LOCK_INITIAL_DELAY_MS - Initial retry delay in milliseconds (default: 50)
#   GIT_LOCK_MAX_DELAY_MS - Max retry delay in milliseconds (default: 3000)
#   GIT_LOCK_WAIT_TIMEOUT_MS - Max wait for index lock polling (default: 30000)
#   GIT_LOCK_POLL_INTERVAL_MS - Lock polling interval in milliseconds (default: 50)
#   GIT_LOCK_INITIAL_WAIT_MS - Initial wait for lock at hook start (default: 10000)
#
# Usage:
#   . $PSScriptRoot/git-staging-helpers.ps1
#   Invoke-EnsureNoIndexLock  # Call at start of hook to wait for external tools
#   Invoke-GitAddWithRetry -Items @("file.txt")

# Script-scoped configuration
$script:GitStagingVerbose = $env:GIT_STAGING_VERBOSE -eq "1"

function Get-EnvIntOrDefault {
    param(
        [string]$Name,
        [int]$DefaultValue
    )

    $raw = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $DefaultValue
    }

    $value = 0
    if ([int]::TryParse($raw, [ref]$value) -and $value -gt 0) {
        return $value
    }

    return $DefaultValue
}

$script:GitLockMaxAttempts = Get-EnvIntOrDefault -Name 'GIT_LOCK_MAX_ATTEMPTS' -DefaultValue 30
$script:GitLockInitialDelayMs = Get-EnvIntOrDefault -Name 'GIT_LOCK_INITIAL_DELAY_MS' -DefaultValue 50
$script:GitLockMaxDelayMs = Get-EnvIntOrDefault -Name 'GIT_LOCK_MAX_DELAY_MS' -DefaultValue 3000
$script:GitLockWaitTimeoutMs = Get-EnvIntOrDefault -Name 'GIT_LOCK_WAIT_TIMEOUT_MS' -DefaultValue 30000
$script:GitLockPollIntervalMs = Get-EnvIntOrDefault -Name 'GIT_LOCK_POLL_INTERVAL_MS' -DefaultValue 50
$script:GitLockInitialWaitMs = Get-EnvIntOrDefault -Name 'GIT_LOCK_INITIAL_WAIT_MS' -DefaultValue 10000

# Script-scoped mutex for coordinating git operations within the same PowerShell session.
# This prevents race conditions when multiple hooks run in parallel within a single process.
$script:GitOperationMutex = $null

function Write-GitStagingVerbose {
    param([string]$Message)
    if ($script:GitStagingVerbose) {
        Write-Host "[git-staging] $Message" -ForegroundColor DarkGray
    }
}

function Write-GitStagingWarning {
    param([string]$Message)
    Write-Warning "[git-staging] $Message"
}

function Write-GitStagingError {
    param([string]$Message)
    Write-Host "[git-staging] ERROR: $Message" -ForegroundColor Red
}

function Get-GitOperationMutex {
    if ($null -eq $script:GitOperationMutex) {
        # Use a named mutex so all PowerShell processes coordinate
        $mutexName = "Global\UnityHelpers_GitIndexOperation"
        try {
            $script:GitOperationMutex = [System.Threading.Mutex]::new($false, $mutexName)
        } catch {
            # Fallback: create a local mutex if global fails (e.g., permissions)
            $script:GitOperationMutex = [System.Threading.Mutex]::new($false)
        }
    }
    return $script:GitOperationMutex
}

function Assert-GitAvailable {
    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) {
        throw "git not found; cannot inspect or stage files."
    }

    return $git
}

function Get-GitRepositoryInfo {
    $gitDirResult = & git rev-parse --git-dir 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gitDirResult)) {
        throw "git rev-parse --git-dir failed; ensure this script runs inside a git repository."
    }

    try {
        $resolvedGitDir = (Resolve-Path -LiteralPath $gitDirResult.Trim()).Path
    } catch {
        throw "Unable to resolve git directory path: $gitDirResult"
    }

    # Also get repository root for scripts that need it
    $repoRoot = & git rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repoRoot)) {
        $repoRoot = Split-Path -Parent $resolvedGitDir
    }

    return [pscustomobject]@{
        Directory      = $resolvedGitDir
        IndexLockPath  = (Join-Path -Path $resolvedGitDir -ChildPath 'index.lock')
        RepositoryRoot = $repoRoot.Trim()
    }
}

function Wait-ForGitIndexLock {
    param(
        [string]$IndexLockPath,
        [int]$MaxWaitMilliseconds = $script:GitLockWaitTimeoutMs,
        [int]$PollIntervalMilliseconds = $script:GitLockPollIntervalMs
    )

    if (-not $IndexLockPath) {
        return $true
    }

    $elapsed = 0
    while ((Test-Path -LiteralPath $IndexLockPath) -and $elapsed -lt $MaxWaitMilliseconds) {
        Start-Sleep -Milliseconds $PollIntervalMilliseconds
        $elapsed += $PollIntervalMilliseconds
    }

    return -not (Test-Path -LiteralPath $IndexLockPath)
}

<#
.SYNOPSIS
    Ensures no git index.lock exists before starting hook operations.

.DESCRIPTION
    This function should be called at the START of any git hook to wait for
    external tools (like lazygit, GitKraken, IDE integrations) to finish.

    When lazygit stages files, it holds index.lock during the staging operation.
    If a hook starts before lazygit releases the lock, git add operations will fail.
    This function waits for that lock to be released before proceeding.

.PARAMETER MaxWaitMilliseconds
    Maximum time to wait for lock to be released (default: GIT_LOCK_INITIAL_WAIT_MS or 10000).

.PARAMETER PollIntervalMilliseconds
    How often to check for lock (default: 50ms).

.OUTPUTS
    $true if lock was released or never existed.
    $false if lock still exists after timeout.

.EXAMPLE
    # At the start of your hook:
    if (-not (Invoke-EnsureNoIndexLock)) {
        Write-Warning "index.lock still held by another process"
    }
#>
function Invoke-EnsureNoIndexLock {
    param(
        [int]$MaxWaitMilliseconds = $script:GitLockInitialWaitMs,
        [int]$PollIntervalMilliseconds = $script:GitLockPollIntervalMs
    )

    try {
        $repositoryInfo = Get-GitRepositoryInfo
        $indexLockPath = $repositoryInfo.IndexLockPath
    } catch {
        Write-GitStagingVerbose "Could not get repository info: $_"
        return $true  # Can't check lock, assume it's fine
    }

    if (-not (Test-Path -LiteralPath $indexLockPath)) {
        Write-GitStagingVerbose "No index.lock present, proceeding immediately"
        return $true
    }

    Write-GitStagingVerbose "index.lock exists, waiting up to ${MaxWaitMilliseconds}ms for external tool to finish..."

    $elapsed = 0
    $lastLog = 0
    while ((Test-Path -LiteralPath $indexLockPath) -and $elapsed -lt $MaxWaitMilliseconds) {
        Start-Sleep -Milliseconds $PollIntervalMilliseconds
        $elapsed += $PollIntervalMilliseconds

        # Log progress every second in verbose mode
        if ($script:GitStagingVerbose -and ($elapsed - $lastLog) -ge 1000) {
            Write-GitStagingVerbose "Still waiting for index.lock... (${elapsed}ms/${MaxWaitMilliseconds}ms)"
            $lastLog = $elapsed
        }
    }

    if (Test-Path -LiteralPath $indexLockPath) {
        Write-GitStagingWarning "index.lock still exists after waiting ${MaxWaitMilliseconds}ms"
        Write-GitStagingWarning "Another process may be holding the lock. Operations may fail."
        Write-GitStagingWarning "Lock file: $indexLockPath"

        # Try to identify what's holding the lock (for debugging on Windows)
        if ($script:GitStagingVerbose) {
            try {
                # Use handle.exe or Get-Process to find lock holder if available
                $lockFileInfo = Get-Item -LiteralPath $indexLockPath -ErrorAction SilentlyContinue
                if ($lockFileInfo) {
                    Write-GitStagingVerbose "Lock file created: $($lockFileInfo.CreationTime)"
                }
            } catch {
                # Ignore errors trying to identify lock holder
            }
        }
        return $false
    }

    Write-GitStagingVerbose "index.lock released after ${elapsed}ms, proceeding"
    return $true
}

function Get-StagedPathsForGlobs {
    param(
        [string[]]$DefaultPaths,
        [string[]]$Globs
    )

    if ($DefaultPaths -and $DefaultPaths.Count -gt 0) {
        return $DefaultPaths
    }

    if (-not $Globs -or $Globs.Count -eq 0) {
        return @()
    }

    $gitDiffArgs = @('diff', '--cached', '--name-only', '--diff-filter=ACM', '--')
    $gitDiffArgs += $Globs
    $paths = & git @gitDiffArgs
    if ($LASTEXITCODE -ne 0 -or -not $paths) {
        return @()
    }

    return $paths
}

function Get-ExistingPaths {
    param([string[]]$Candidates)

    $existingPaths = @()
    foreach ($candidate in $Candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        if (Test-Path -LiteralPath $candidate) {
            $existingPaths += $candidate
        }
    }

    return $existingPaths
}

function Invoke-GitAddWithRetry {
    param(
        [string[]]$Items,
        [string]$IndexLockPath,
        [int]$MaxAttempts = $script:GitLockMaxAttempts,
        [int]$InitialDelayMilliseconds = $script:GitLockInitialDelayMs,
        [int]$MaxDelayMilliseconds = $script:GitLockMaxDelayMs,
        [switch]$UpdateOnly,
        [switch]$Quiet
    )

    if (-not $Items -or $Items.Count -eq 0) {
        Write-GitStagingVerbose "git_add_with_retry: no files to add, returning success"
        return 0
    }

    Write-GitStagingVerbose "git_add_with_retry: staging $($Items.Count) file(s)"

    $gitArgs = @('add')
    if ($UpdateOnly) {
        $gitArgs += '--update'
    }
    $gitArgs += '--'
    $gitArgs += $Items

    $mutex = Get-GitOperationMutex
    $mutexAcquired = $false

    try {
        # Try to acquire the mutex with a timeout
        try {
            $mutexAcquired = $mutex.WaitOne(10000) # 10 second timeout
            if ($mutexAcquired) {
                Write-GitStagingVerbose "Acquired process mutex for git operation"
            }
        } catch [System.Threading.AbandonedMutexException] {
            # Another process crashed while holding the mutex; we now own it
            $mutexAcquired = $true
            Write-GitStagingVerbose "Acquired abandoned mutex (previous process crashed)"
        } catch {
            # Mutex failed, continue without it
            $mutexAcquired = $false
            Write-GitStagingVerbose "Could not acquire mutex, proceeding without"
        }

        for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
            # Wait for any existing lock to be released before attempting
            if ($IndexLockPath -and (Test-Path -LiteralPath $IndexLockPath)) {
                Write-GitStagingVerbose "index.lock exists before attempt $attempt, waiting..."
                $lockCleared = Wait-ForGitIndexLock -IndexLockPath $IndexLockPath -MaxWaitMilliseconds 5000 -PollIntervalMilliseconds 50
                if (-not $lockCleared) {
                    if (-not $Quiet) {
                        Write-GitStagingWarning "index.lock still exists after waiting; attempting git add anyway (attempt $attempt/$MaxAttempts)"
                    }
                } else {
                    Write-GitStagingVerbose "index.lock released, proceeding with attempt $attempt"
                }
            }

            # Capture stderr for better error messages
            $stderrFile = [System.IO.Path]::GetTempFileName()
            try {
                & git @gitArgs 2>$stderrFile
                $exitCode = $LASTEXITCODE

                if ($exitCode -eq 0) {
                    Write-GitStagingVerbose "git add succeeded on attempt $attempt"
                    return 0
                }

                $gitStderr = Get-Content -Path $stderrFile -Raw -ErrorAction SilentlyContinue
                Write-GitStagingVerbose "git add failed with exit code $exitCode on attempt $attempt"
                if ($gitStderr) {
                    Write-GitStagingVerbose "git stderr: $gitStderr"
                }

                # Determine if we should retry (exit code 128 or index.lock error message)
                $shouldRetry = $exitCode -eq 128
                if (-not $shouldRetry -and $gitStderr) {
                    if ($gitStderr -match "index\.lock" -or $gitStderr -match "Unable to create") {
                        $shouldRetry = $true
                    }
                }

                if ($shouldRetry -and $attempt -lt $MaxAttempts) {
                    # Exponential backoff with jitter, capped at MaxDelayMilliseconds
                    $baseDelay = [Math]::Min($InitialDelayMilliseconds * [Math]::Pow(1.4, $attempt - 1), $MaxDelayMilliseconds)
                    $maxJitter = [int]([Math]::Floor($baseDelay * 0.4))
                    $jitter = if ($maxJitter -gt 0) {
                        Get-Random -Minimum 0 -Maximum ($maxJitter + 1)
                    }
                    else {
                        0
                    }
                    $delay = [int]$baseDelay + $jitter
                    if (-not $Quiet) {
                        Write-GitStagingWarning "git add failed (exit code $exitCode), retrying in ${delay}ms (attempt $attempt/$MaxAttempts)..."
                    }
                    Start-Sleep -Milliseconds $delay
                    continue
                }

                # Non-retryable failure - show the actual error
                if ($gitStderr -and -not $Quiet) {
                    Write-GitStagingError "git add failed with non-retryable error: $gitStderr"
                }

                return $exitCode
            } finally {
                Remove-Item -Path $stderrFile -Force -ErrorAction SilentlyContinue
            }
        }

        Write-GitStagingError "git add failed after $MaxAttempts attempts"
        return 128
    } finally {
        if ($mutexAcquired -and $null -ne $mutex) {
            try {
                $mutex.ReleaseMutex()
                Write-GitStagingVerbose "Released process mutex"
            } catch {
                # Ignore errors releasing mutex
            }
        }
    }
}

# Convenience function for scripts that modify files and need to re-stage them.
# Use this instead of raw `git add` calls to ensure proper lock handling.
function Invoke-GitAddSingleFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [string]$IndexLockPath
    )

    return Invoke-GitAddWithRetry -Items @($FilePath) -IndexLockPath $IndexLockPath -Quiet
}

