#Requires -Version 5.1
# cspell:ignore redist redists UCRT
[CmdletBinding(PositionalBinding = $false)]
param(
    [Alias('UnityVersions')]
    [string[]]$RunnerMaintenanceUnityVersions = @(),
    [ValidateSet('EditorOnly', 'StandaloneWindowsIl2Cpp', 'Android', 'Full')]
    [Alias('ProvisioningProfile')]
    [string]$RunnerMaintenanceProvisioningProfile = 'Full',
    [Alias('InstallRoot')]
    [string]$RunnerMaintenanceInstallRoot = '',
    [Alias('DetectOnly')]
    [switch]$RunnerMaintenanceDetectOnly,
    [Alias('HostOnly')]
    [switch]$RunnerMaintenanceHostOnly,
    [Alias('SkipHostBootstrap')]
    [switch]$RunnerMaintenanceSkipHostBootstrap,
    [Alias('DiagnosticsRoot')]
    [string]$RunnerMaintenanceDiagnosticsRoot = '',
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$UnboundArguments
)
# Adding a ValueFromRemainingArguments parameter is NOT enough on its own: with positional
# binding on, `pwsh -File <script> -UnityVersions a b` binds 'b' to whichever named
# parameter is positionally next, and the catch-all never sees it. Measured, not assumed.
# PositionalBinding = $false on the param block above is what makes every stray value land
# here; this then refuses it rather than guessing which array parameter it belonged to.
# Pass a comma-separated list instead. See .llm/skills/bash-pwsh-invocation.md.
if ($UnboundArguments -and 0 -lt $UnboundArguments.Count) {
    # Not Write-Error: under $ErrorActionPreference = 'Stop' that terminates with exit 1, so
    # the exit code would depend on where in the file this guard happens to sit. 64 is sysexits.h
    # EX_USAGE and, unlike 1 or 2, is not already used by these scripts for a real failure.
    [Console]::Error.WriteLine(
        "Unbound arguments: $($UnboundArguments -join ', '). Pass a comma-separated list " +
        "(-UnityVersions a,b,c) rather than space-separated values."
    )
    exit 64
}


Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-RunnerMaintenanceInfo {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "[maintain-windows-runner] $Message"
}

function Write-RunnerMaintenanceWarning {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "::warning::$Message"
}

function Get-UnityMaintenanceScriptRoot {
    if ($PSScriptRoot) {
        return $PSScriptRoot
    }

    return Split-Path -Parent $MyInvocation.MyCommand.Path
}

function Get-UnityMaintenanceRepoRoot {
    $scriptRoot = Get-UnityMaintenanceScriptRoot
    $current = Get-Item -LiteralPath $scriptRoot
    while ($null -ne $current) {
        $unityVersionsPath = Join-Path $current.FullName '.github/unity-versions.json'
        if (Test-Path -LiteralPath $unityVersionsPath -PathType Leaf) {
            return $current.FullName
        }

        $current = $current.Parent
    }

    return (Get-Item -LiteralPath (Join-Path $scriptRoot '../..')).FullName
}

function Get-RunnerMaintenanceDefaultDiagnosticsRoot {
    param([Parameter(Mandatory = $true)][string]$RepoRoot)

    return Join-Path $RepoRoot '.artifacts/runner-bootstrap'
}

function Resolve-RunnerMaintenanceInstallRoot {
    param(
        [AllowEmptyString()][string]$InstallRoot,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($InstallRoot)) {
        return $InstallRoot
    }
    if (-not [string]::IsNullOrWhiteSpace($env:UNITY_EDITOR_INSTALL_ROOT)) {
        return $env:UNITY_EDITOR_INSTALL_ROOT
    }

    # The tool cache is on the runner's storage volume and is also the root
    # consumed by every licensed Unity workflow in this repository.
    $runnerToolCache = ''
    foreach ($variableName in @('RUNNER_TOOL_CACHE', 'RUNNER_TOOLSDIRECTORY', 'AGENT_TOOLSDIRECTORY', 'UH_RUNNER_TOOL_CACHE')) {
        $candidate = [Environment]::GetEnvironmentVariable($variableName)
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            $runnerToolCache = $candidate
            break
        }
    }
    if ($runnerToolCache) {
        $separator = if ($runnerToolCache -match '^[A-Za-z]:' -or $runnerToolCache.Contains('\')) { '\' } else { [IO.Path]::DirectorySeparatorChar }
        return ($runnerToolCache.TrimEnd('\', '/') + $separator + 'u6-v3')
    }

    # Interactive shells do not inherit the service's environment. Load only
    # the runner tool-cache keys from its .env file, using the same precedence
    # as actions/runner. Without an override, actions/runner defaults to
    # <work folder>\_tool.
    $normalizedRepoRoot = $RepoRoot.TrimEnd('\', '/')
    if ($normalizedRepoRoot -match '^(?<runner>.*)(?<separator>[\\/])_work(?:[\\/]|$)') {
        $runnerRoot = $Matches['runner'].TrimEnd('\', '/')
        $separator = $Matches['separator']
        $runnerEnvironmentPath = $runnerRoot + $separator + '.env'
        if (Test-Path -LiteralPath $runnerEnvironmentPath -PathType Leaf) {
            $runnerEnvironment = @{}
            foreach ($line in @(Get-Content -LiteralPath $runnerEnvironmentPath -ErrorAction Stop)) {
                $equalsIndex = $line.IndexOf('=')
                if (0 -lt $equalsIndex) {
                    $name = $line.Substring(0, $equalsIndex)
                    if ($name -in @('RUNNER_TOOL_CACHE', 'RUNNER_TOOLSDIRECTORY', 'AGENT_TOOLSDIRECTORY')) {
                        $runnerEnvironment[$name] = $line.Substring($equalsIndex + 1)
                    }
                }
            }
            foreach ($variableName in @('RUNNER_TOOL_CACHE', 'RUNNER_TOOLSDIRECTORY', 'AGENT_TOOLSDIRECTORY')) {
                $candidate = [string]$runnerEnvironment[$variableName]
                if (-not [string]::IsNullOrWhiteSpace($candidate)) {
                    $cacheSeparator = if ($candidate -match '^[A-Za-z]:' -or $candidate.Contains('\')) { '\' } else { [IO.Path]::DirectorySeparatorChar }
                    return ($candidate.TrimEnd('\', '/') + $cacheSeparator + 'u6-v3')
                }
            }
        }

        return ($runnerRoot + $separator + '_work' + $separator + '_tool' + $separator + 'u6-v3')
    }

    # A manually cloned checkout still keeps editor payloads on its own drive.
    if ($normalizedRepoRoot -match '^(?<drive>[A-Za-z]:)\\') {
        return ($Matches['drive'] + '\Unity\Editors')
    }

    return 'C:\Unity\Editors'
}

function Resolve-RunnerMaintenanceUnityVersions {
    param(
        [AllowNull()][string[]]$UnityVersions,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $versions = @(
        $UnityVersions |
            ForEach-Object { [string]$_ } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($versions.Count -gt 0) {
        return $versions
    }

    $unityVersionsPath = Join-Path $RepoRoot '.github/unity-versions.json'
    if (-not (Test-Path -LiteralPath $unityVersionsPath -PathType Leaf)) {
        throw "Invoke-WindowsRunnerMaintenance requires Unity versions or a checkout with .github/unity-versions.json. Missing: $unityVersionsPath"
    }

    $unityVersionsConfig = Get-Content -LiteralPath $unityVersionsPath -Raw | ConvertFrom-Json
    $versions = @(
        @($unityVersionsConfig.all) |
            ForEach-Object { [string]$_ } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($versions.Count -lt 1) {
        throw '.github/unity-versions.json must define at least one Unity version in all[].'
    }

    Write-RunnerMaintenanceInfo "Unity versions from .github/unity-versions.json: $($versions -join ', ')"
    return $versions
}

function Resolve-RunnerMaintenanceDetectOnly {
    param([bool]$DetectOnly)

    if ($env:UH_RUNNER_DISABLE_AUTO_BOOTSTRAP -eq '1') {
        Write-RunnerMaintenanceInfo 'UH_RUNNER_DISABLE_AUTO_BOOTSTRAP=1 -> forcing DetectOnly.'
        return $true
    }

    return $DetectOnly
}

function Invoke-WindowsRunnerMaintenance {
    [CmdletBinding()]
    param(
        [string[]]$UnityVersions = @(),
        [ValidateSet('EditorOnly', 'StandaloneWindowsIl2Cpp', 'Android', 'Full')]
        [string]$ProvisioningProfile = 'Full',
        [string]$InstallRoot = '',
        [switch]$DetectOnly,
        [switch]$HostOnly,
        [switch]$SkipHostBootstrap,
        [string]$DiagnosticsRoot = ''
    )

    if ($HostOnly -and $SkipHostBootstrap) {
        throw '-HostOnly and -SkipHostBootstrap are mutually exclusive.'
    }

    $repoRoot = Get-UnityMaintenanceRepoRoot
    $versions = if ($HostOnly) { @() } else { @(Resolve-RunnerMaintenanceUnityVersions -UnityVersions $UnityVersions -RepoRoot $repoRoot) }
    $scriptRoot = Get-UnityMaintenanceScriptRoot
    $bootstrapScript = Join-Path $scriptRoot 'bootstrap-windows-runner.ps1'
    $ensureEditorScript = Join-Path $scriptRoot 'ensure-editor.ps1'
    if (-not $SkipHostBootstrap -and -not (Test-Path -LiteralPath $bootstrapScript -PathType Leaf)) {
        throw "Missing runner bootstrap script: $bootstrapScript"
    }
    if (-not $HostOnly -and -not (Test-Path -LiteralPath $ensureEditorScript -PathType Leaf)) {
        throw "Missing Unity editor provisioning script: $ensureEditorScript"
    }

    $maintenanceDetectOnly = Resolve-RunnerMaintenanceDetectOnly -DetectOnly ([bool]$DetectOnly)
    $maintenanceDiagnosticsRoot = if ([string]::IsNullOrWhiteSpace($DiagnosticsRoot)) {
        Get-RunnerMaintenanceDefaultDiagnosticsRoot -RepoRoot $repoRoot
    } else {
        [string]$DiagnosticsRoot
    }
    $maintenanceInstallRoot = Resolve-RunnerMaintenanceInstallRoot -InstallRoot $InstallRoot -RepoRoot $repoRoot
    $maintenanceProvisioningProfile = [string]$ProvisioningProfile

    if (-not $SkipHostBootstrap) {
        . $bootstrapScript
        $bootstrapOutput = @(Invoke-WindowsRunnerBootstrap `
            -DetectOnly:$maintenanceDetectOnly `
            -UnityInstallRoot $maintenanceInstallRoot `
            -DiagnosticsRoot $maintenanceDiagnosticsRoot)
        if ($bootstrapOutput.Count -lt 1) {
            throw 'Windows runner bootstrap did not return an exit code.'
        }
        if ($bootstrapOutput.Count -gt 1) {
            foreach ($line in @($bootstrapOutput[0..($bootstrapOutput.Count - 2)])) {
                if ($null -ne $line) {
                    Write-RunnerMaintenanceInfo "[bootstrap] $line"
                }
            }
        }

        [int]$bootstrapCode = $bootstrapOutput[-1]
        if ($bootstrapCode -ne 0) {
            return $bootstrapCode
        }
    }

    if ($HostOnly) {
        Write-RunnerMaintenanceInfo 'Windows host prerequisites are ready.'
        return 0
    }

    $failedVersions = New-Object System.Collections.Generic.List[string]
    foreach ($version in $versions) {
        Write-RunnerMaintenanceInfo "Verifying Unity $version ($maintenanceProvisioningProfile) under $maintenanceInstallRoot"
        $versionDiagnosticsRoot = if ([string]::IsNullOrWhiteSpace($maintenanceDiagnosticsRoot)) {
            ''
        } else {
            $safeVersion = $version -replace '[^A-Za-z0-9_.-]', '_'
            Join-Path $maintenanceDiagnosticsRoot "unity-$safeVersion"
        }

        $ensureEditorArgs = @{
            UnityVersion = $version
            InstallRoot = $maintenanceInstallRoot
            ProvisioningProfile = $maintenanceProvisioningProfile
            CiManagedOnly = $true
        }
        if (-not [string]::IsNullOrWhiteSpace($versionDiagnosticsRoot)) {
            $ensureEditorArgs.DiagnosticsPath = $versionDiagnosticsRoot
        }
        if ($maintenanceDetectOnly) {
            $ensureEditorArgs.RequireHealthyExisting = $true
        }

        try {
            $ensureEditorOutput = @(& $ensureEditorScript @ensureEditorArgs 2>&1)
            foreach ($line in $ensureEditorOutput) {
                if ($null -ne $line) {
                    Write-RunnerMaintenanceInfo "[ensure-editor] $line"
                }
            }
            Write-RunnerMaintenanceInfo "Unity $version is installed, has required modules, and passes native startup."
        } catch {
            $failedVersions.Add($version) | Out-Null
            Write-RunnerMaintenanceWarning "Unity $version maintenance failed: $($_.Exception.Message)"
        }
    }

    if ($failedVersions.Count -gt 0) {
        if ($maintenanceDetectOnly) {
            return 2
        }
        return 1
    }

    return 0
}

if ($MyInvocation.InvocationName -ne '.') {
    $exitCode = Invoke-WindowsRunnerMaintenance `
        -UnityVersions $RunnerMaintenanceUnityVersions `
        -ProvisioningProfile $RunnerMaintenanceProvisioningProfile `
        -InstallRoot $RunnerMaintenanceInstallRoot `
        -DetectOnly:$RunnerMaintenanceDetectOnly `
        -HostOnly:$RunnerMaintenanceHostOnly `
        -SkipHostBootstrap:$RunnerMaintenanceSkipHostBootstrap `
        -DiagnosticsRoot $RunnerMaintenanceDiagnosticsRoot
    exit $exitCode
}
