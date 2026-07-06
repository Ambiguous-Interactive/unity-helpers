#Requires -Version 5.1
# cspell:ignore redist redists UCRT
[CmdletBinding()]
param(
    [Alias('UnityVersions')]
    [string[]]$RunnerMaintenanceUnityVersions = @(),
    [ValidateSet('EditorOnly', 'StandaloneWindowsIl2Cpp', 'Android', 'Full')]
    [Alias('ProvisioningProfile')]
    [string]$RunnerMaintenanceProvisioningProfile = 'StandaloneWindowsIl2Cpp',
    [Alias('InstallRoot')]
    [string]$RunnerMaintenanceInstallRoot = $(if ($env:UNITY_EDITOR_INSTALL_ROOT) { $env:UNITY_EDITOR_INSTALL_ROOT } else { 'C:\Unity\Editors' }),
    [Alias('DetectOnly')]
    [switch]$RunnerMaintenanceDetectOnly,
    [Alias('DiagnosticsRoot')]
    [string]$RunnerMaintenanceDiagnosticsRoot = ''
)

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
        [string]$ProvisioningProfile = 'StandaloneWindowsIl2Cpp',
        [string]$InstallRoot = $(if ($env:UNITY_EDITOR_INSTALL_ROOT) { $env:UNITY_EDITOR_INSTALL_ROOT } else { 'C:\Unity\Editors' }),
        [switch]$DetectOnly,
        [string]$DiagnosticsRoot = ''
    )

    $repoRoot = Get-UnityMaintenanceRepoRoot
    $versions = @(Resolve-RunnerMaintenanceUnityVersions -UnityVersions $UnityVersions -RepoRoot $repoRoot)
    $scriptRoot = Get-UnityMaintenanceScriptRoot
    $bootstrapScript = Join-Path $scriptRoot 'bootstrap-windows-runner.ps1'
    $ensureEditorScript = Join-Path $scriptRoot 'ensure-editor.ps1'
    if (-not (Test-Path -LiteralPath $bootstrapScript -PathType Leaf)) {
        throw "Missing runner bootstrap script: $bootstrapScript"
    }
    if (-not (Test-Path -LiteralPath $ensureEditorScript -PathType Leaf)) {
        throw "Missing Unity editor provisioning script: $ensureEditorScript"
    }

    $maintenanceDetectOnly = Resolve-RunnerMaintenanceDetectOnly -DetectOnly ([bool]$DetectOnly)
    $maintenanceDiagnosticsRoot = if ([string]::IsNullOrWhiteSpace($DiagnosticsRoot)) {
        Get-RunnerMaintenanceDefaultDiagnosticsRoot -RepoRoot $repoRoot
    } else {
        [string]$DiagnosticsRoot
    }
    $maintenanceInstallRoot = [string]$InstallRoot
    $maintenanceProvisioningProfile = [string]$ProvisioningProfile

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
        -DiagnosticsRoot $RunnerMaintenanceDiagnosticsRoot
    exit $exitCode
}
