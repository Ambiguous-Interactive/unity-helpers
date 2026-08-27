#!/usr/bin/env pwsh
# <#
# .SYNOPSIS
#     Host-side pre-attach cleanup for the dev container.
# .DESCRIPTION
#     Wired as devcontainer.json's initializeCommand, this runs on the HOST
#     before Dev Containers resolves the container.
#
#     Root cause it remediates: when Docker Desktop's VM (or WSL2) restarts
#     uncleanly, the previous container can be left as a zombie. Docker's
#     metadata still lists it under this workspace's
#     devcontainer.local_folder label, so Dev Containers treats it as
#     reusable, but its init process is gone: every `docker exec` fails with
#     "error executing setns process" (nsexec: failed to open
#     /proc/<pid>/ns/ipc) and `devcontainer up` aborts with "An error
#     occurred setting up the container." The extension never recovers on
#     its own; "Reopen in Container" keeps failing until the container is
#     removed.
#
#     This script removes containers labeled for this workspace that are
#     (a) not running, or (b) running but unable to execute a trivial exec
#     probe. Dev Containers then recreates the container from the cached
#     image, which is exactly the recoverable path.
#
#     Best-effort by design: every failure path exits 0 so a missing or
#     unresponsive docker CLI can never block opening the workspace.
# #>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# $PSScriptRoot is the .devcontainer directory; the workspace folder is its parent, which is the
# exact value Dev Containers stores in each container's devcontainer.local_folder label.
$workspaceFolder = Split-Path -Parent $PSScriptRoot
$execProbeTimeoutSeconds = 20

function Write-Info {
    param([string]$Message)
    Write-Host "[initialize-host] $Message"
}

function Invoke-Docker {
    param([string[]]$DockerArgs)
    & docker @DockerArgs 2>$null
}

# A trivial exec into the container. A healthy running container answers
# instantly; a zombie fails with an OCI setns/runc error from the daemon.
# The probe is bounded so a hung docker CLI cannot stall workspace open.
function Test-ContainerUsable {
    param([string]$ContainerId)
    try {
        $probe = Start-Process -FilePath 'docker' `
            -ArgumentList @('exec', $ContainerId, '/bin/sh', '-c', 'exit 0') `
            -NoNewWindow -PassThru
        if (-not $probe.WaitForExit($execProbeTimeoutSeconds * 1000)) {
            if (-not $probe.HasExited) { $probe.Kill() }
            return $false
        }
        return $probe.ExitCode -eq 0
    }
    catch {
        return $false
    }
}

try {
    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $docker) {
        Write-Info 'docker CLI not found on host; skipping stale container cleanup.'
        exit 0
    }

    # List containers carrying the label KEY, then compare the label VALUE
    # case-insensitively: the extension stores the path exactly as VS Code
    # passed it (drive letter case varies between sessions).
    $candidateIds = @(Invoke-Docker @(
            'ps', '-aq', '--filter', 'label=devcontainer.local_folder'))
    if ($candidateIds.Count -eq 0) {
        Write-Info 'No existing devcontainer candidates; nothing to clean up.'
        exit 0
    }

    foreach ($containerId in $candidateIds) {
        if ([string]::IsNullOrWhiteSpace($containerId)) { continue }

        $inspectJson = Invoke-Docker @('inspect', '--type', 'container', $containerId)
        if (-not $inspectJson) { continue }

        $inspect = $inspectJson | ConvertFrom-Json
        if ($inspect.Count -eq 0) { continue }
        $container = $inspect[0]

        $labelFolders = @($container.Config.Labels.PSObject.Properties |
            Where-Object { $_.Name -eq 'devcontainer.local_folder' } |
            Select-Object -ExpandProperty Value)
        if ($labelFolders.Count -eq 0) { continue }

        $labelFolder = [string]$labelFolders[0]
        # -ine is case-insensitive not-equal: drive letter case varies between sessions. A plain
        # container from another project must never reach the removal branch below.
        if ($labelFolder -ine $workspaceFolder) { continue }

        $status = [string]$container.State.Status
        $remove = $false
        $reason = ''

        if ($status -ne 'running') {
            $remove = $true
            $reason = "status is '$status'"
        }
        else {
            if (Test-ContainerUsable -ContainerId $containerId) {
                Write-Info "Container $containerId is running and healthy; keeping it."
            }
            else {
                $remove = $true
                $reason = 'running but failed the exec health probe (zombie container)'
            }
        }

        if ($remove) {
            Write-Info "Removing stale container $containerId ($reason) so Dev Containers can recreate it."
            $null = Invoke-Docker @('rm', '-f', $containerId)
        }
    }
}
catch {
    # Cleanup is opportunistic. A broken docker CLI, a daemon restart, or any
    # unexpected host condition must never prevent the workspace from opening.
    Write-Info "Stale container cleanup skipped: $($_.Exception.Message)"
}

exit 0
