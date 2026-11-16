# Native formatting and dot-source result adapter; Git policy runs through locked Python.
Set-StrictMode -Version Latest

function Write-PushDefaultsSuccess {
    param([string]$Message)
    Write-Host "[git-push-defaults] $Message" -ForegroundColor Green
}

function Write-PushDefaultsWarning {
    param([string]$Message)
    Write-Host "[git-push-defaults] WARNING: $Message" -ForegroundColor Yellow
}

function Write-PushDefaultsError {
    param([string]$Message)
    Write-Host "[git-push-defaults] ERROR: $Message" -ForegroundColor Red
}

function Write-PushDefaultsInfo {
    param([string]$Message)
    Write-Host "[git-push-defaults] $Message" -ForegroundColor Cyan
}

function Set-RepoGitPushDefaults {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$RepoRoot)

    $result = @{
        Success = $false
        Errors = @()
        Values = @{ 'push.autoSetupRemote' = ''; 'push.default' = '' }
    }
    $priorExit = Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
    $priorExitValue = if ($null -ne $priorExit) { $priorExit.Value } else { $null }
    try {
        if (-not (Get-Command uv -ErrorAction SilentlyContinue)) {
            throw 'uv is required for locked Git push-default configuration.'
        }
        $arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev',
            'python', (Join-Path $PSScriptRoot 'configure_git_defaults.py'),
            "--repo-root=$RepoRoot", '--profile=powershell', '--result-json')
        $output = @(& uv @arguments)
        $toolExit = $LASTEXITCODE
        $payload = ($output -join "`n") | ConvertFrom-Json
        if (-not ($payload.result.Success -is [bool]) -or
            (($toolExit -eq 0) -ne $payload.result.Success)) {
            throw 'The Git push-default tool returned an inconsistent result.'
        }
        $result.Success = $payload.result.Success
        $result.Errors = @($payload.result.Errors | ForEach-Object { [string]$_ })
        $result.Values['push.autoSetupRemote'] = [string]$payload.result.Values.'push.autoSetupRemote'
        $result.Values['push.default'] = [string]$payload.result.Values.'push.default'
        foreach ($message in @($payload.messages)) {
            if (@($message).Count -ne 2) {
                throw 'The Git push-default tool returned an invalid message.'
            }
            switch ([string]$message[0]) {
                'success' { Write-PushDefaultsSuccess ([string]$message[1]) }
                'warning' { Write-PushDefaultsWarning ([string]$message[1]) }
                'error' { Write-PushDefaultsError ([string]$message[1]) }
                'info' { Write-PushDefaultsInfo ([string]$message[1]) }
                'value' { Write-Host ([string]$message[1]) }
                default { throw 'The Git push-default tool returned an unknown message type.' }
            }
        }
        if ($null -ne $payload.last_git_exit_code) {
            $global:LASTEXITCODE = [int]$payload.last_git_exit_code
        }
        elseif ($null -ne $priorExit) {
            $global:LASTEXITCODE = $priorExitValue
        }
        else {
            Remove-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
        }
    }
    catch {
        $result.Success = $false
        $message = "Could not run locked Git push-default configuration: $($_.Exception.Message)"
        $result.Errors = @($message)
        Write-PushDefaultsError $message
        if ($null -ne $priorExit) { $global:LASTEXITCODE = $priorExitValue }
    }
    return $result
}
