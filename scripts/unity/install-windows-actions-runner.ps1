#Requires -Version 5.1
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('AdminPrepare', 'UserInstall', 'AdminConfigure')]
    [string]$Phase,

    [string]$InstallRoot = 'C:\actions-runner',
    [string]$RunnerUser = $env:USERNAME,
    [string]$RunnerVersion = '',
    [string]$ArchiveSha256 = '',
    [string]$RegistrationUrl = '',
    [string]$RegistrationToken = '',
    [string]$RunnerName = $env:COMPUTERNAME,
    [string]$RunnerGroup = '',
    [string]$Labels = 'RAM-64GB',
    [string]$WorkDirectory = '_work'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-RunnerInstallInfo {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "[install-windows-actions-runner] $Message"
}

function Test-RunnerInstallIsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-RunnerInstallAdministrator {
    if (-not (Test-RunnerInstallIsAdministrator)) {
        throw "Phase '$Phase' requires an elevated Windows PowerShell prompt (Run as administrator)."
    }
}

function Assert-RunnerInstallWindows {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw 'The GitHub Actions runner installer supports Windows hosts only.'
    }
}

function Assert-RunnerInstallRoot {
    $resolvedRoot = [IO.Path]::GetFullPath($InstallRoot)
    $driveRoot = [IO.Path]::GetPathRoot($resolvedRoot)
    if ([string]::Equals($resolvedRoot.TrimEnd('\'), $driveRoot.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw "InstallRoot must be a dedicated directory, not a drive root: $resolvedRoot"
    }
    return $resolvedRoot
}

function Invoke-RunnerNativeCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Description
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Invoke-RunnerAdminPrepare {
    param([Parameter(Mandatory = $true)][string]$Root)

    Assert-RunnerInstallAdministrator
    if ([string]::IsNullOrWhiteSpace($RunnerUser)) {
        throw 'RunnerUser is required for AdminPrepare.'
    }

    New-Item -ItemType Directory -Force -Path $Root | Out-Null
    Invoke-RunnerNativeCommand `
        -FilePath (Join-Path $env:SystemRoot 'System32\icacls.exe') `
        -Arguments @(
            $Root,
            '/inheritance:r',
            '/grant:r',
            "${RunnerUser}:(OI)(CI)M",
            '/grant:r',
            '*S-1-5-20:(OI)(CI)M',
            '/grant:r',
            '*S-1-5-32-544:(OI)(CI)F',
            '/grant:r',
            '*S-1-5-18:(OI)(CI)F'
        ) `
        -Description 'Runner directory ACL configuration'
    Write-RunnerInstallInfo "Prepared $Root for package installation by '$RunnerUser' and service execution by Network Service."
}

function Invoke-RunnerUserInstall {
    param([Parameter(Mandatory = $true)][string]$Root)

    if ($RunnerVersion -notmatch '^\d+\.\d+\.\d+$') {
        throw 'UserInstall requires -RunnerVersion in MAJOR.MINOR.PATCH form.'
    }
    if ($ArchiveSha256 -notmatch '^[A-Fa-f0-9]{64}$') {
        throw 'UserInstall requires the 64-character SHA-256 published with the GitHub Actions runner download.'
    }
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        throw "InstallRoot does not exist. Run AdminPrepare first: $Root"
    }
    if (Test-Path -LiteralPath (Join-Path $Root '.runner') -PathType Leaf) {
        throw "A runner is already configured at $Root. Remove it with config.cmd remove before replacing its package."
    }

    $archiveName = "actions-runner-win-x64-$RunnerVersion.zip"
    $archivePath = Join-Path ([IO.Path]::GetTempPath()) $archiveName
    $downloadUri = "https://github.com/actions/runner/releases/download/v$RunnerVersion/$archiveName"
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Write-RunnerInstallInfo "Downloading GitHub Actions runner v$RunnerVersion."
        Invoke-WebRequest -Uri $downloadUri -OutFile $archivePath -UseBasicParsing
        $actualSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
        if (-not [string]::Equals($actualSha256, $ArchiveSha256, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Runner archive SHA-256 mismatch. Expected $ArchiveSha256, got $actualSha256."
        }

        $existingFiles = @(Get-ChildItem -LiteralPath $Root -Force)
        if ($existingFiles.Count -gt 0) {
            throw "InstallRoot must be empty before UserInstall: $Root"
        }
        Expand-Archive -LiteralPath $archivePath -DestinationPath $Root
        foreach ($requiredFile in @('config.cmd', 'run.cmd')) {
            if (-not (Test-Path -LiteralPath (Join-Path $Root $requiredFile) -PathType Leaf)) {
                throw "Runner archive did not contain $requiredFile."
            }
        }
        Write-RunnerInstallInfo "Installed and checksum-verified runner files in $Root. Run AdminConfigure next."
    } finally {
        Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-RunnerAdminConfigure {
    param([Parameter(Mandatory = $true)][string]$Root)

    Assert-RunnerInstallAdministrator
    if (
        [string]::IsNullOrWhiteSpace($RegistrationUrl) -or
        $RegistrationUrl -notmatch '^https://github\.com/[^/]+(?:/[^/]+)?/?$'
    ) {
        throw 'AdminConfigure requires the GitHub organization or repository URL that issued the registration token.'
    }
    if ([string]::IsNullOrWhiteSpace($RegistrationToken)) {
        throw 'AdminConfigure requires a short-lived runner registration token.'
    }
    if ([string]::IsNullOrWhiteSpace($RunnerName)) {
        throw 'AdminConfigure requires RunnerName.'
    }
    if ([IO.Path]::IsPathRooted($WorkDirectory) -or $WorkDirectory -match '(^|[\\/])\.\.([\\/]|$)') {
        throw 'WorkDirectory must be a relative directory beneath InstallRoot.'
    }

    $configPath = Join-Path $Root 'config.cmd'
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
        throw "Runner package is missing. Run UserInstall first: $configPath"
    }
    if (Test-Path -LiteralPath (Join-Path $Root '.runner') -PathType Leaf) {
        Write-RunnerInstallInfo "Runner is already configured at $Root; leaving its registration and service unchanged."
        return
    }

    $customLabels = @($Labels -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($customLabels -notcontains $RunnerName) {
        $customLabels += $RunnerName
    }
    $arguments = @(
        '--unattended',
        '--url',
        $RegistrationUrl.TrimEnd('/'),
        '--token',
        $RegistrationToken,
        '--name',
        $RunnerName,
        '--work',
        $WorkDirectory,
        '--labels',
        ($customLabels -join ','),
        '--runasservice',
        '--replace'
    )
    if (-not [string]::IsNullOrWhiteSpace($RunnerGroup)) {
        $arguments += @('--runnergroup', $RunnerGroup)
    }

    Push-Location $Root
    try {
        Write-RunnerInstallInfo "Registering '$RunnerName' as a Windows service for $RegistrationUrl. The registration token will not be logged."
        Invoke-RunnerNativeCommand -FilePath $configPath -Arguments $arguments -Description 'GitHub Actions runner configuration'
    } finally {
        Pop-Location
    }
    Write-RunnerInstallInfo "Configured runner '$RunnerName' with labels '$($customLabels -join ',')'."
}

Assert-RunnerInstallWindows
$normalizedInstallRoot = Assert-RunnerInstallRoot
switch ($Phase) {
    'AdminPrepare' { Invoke-RunnerAdminPrepare -Root $normalizedInstallRoot }
    'UserInstall' { Invoke-RunnerUserInstall -Root $normalizedInstallRoot }
    'AdminConfigure' { Invoke-RunnerAdminConfigure -Root $normalizedInstallRoot }
}
