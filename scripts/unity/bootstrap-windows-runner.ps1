#Requires -Version 5.1
# cspell:ignore HKLM msvcp redist redists vcredist vcruntime winget UCRT ucrtbase WindowsApps
[CmdletBinding()]
param(
    [Alias('DetectOnly')]
    [switch]$RunnerBootstrapDetectOnly,
    [Alias('UnityInstallRoot')]
    [string]$RunnerBootstrapUnityInstallRoot = $(if ($env:UNITY_EDITOR_INSTALL_ROOT) { $env:UNITY_EDITOR_INSTALL_ROOT } else { 'C:\Unity\Editors' }),
    [Alias('DiagnosticsRoot')]
    [string]$RunnerBootstrapDiagnosticsRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:WindowsRunnerBootstrapDefaultInstallRoot = $RunnerBootstrapUnityInstallRoot
$script:WindowsRunnerBootstrapDefaultDiagnosticsRoot = $RunnerBootstrapDiagnosticsRoot
$script:VcRedist2010X64Sha256 = 'f3b7a76d84d23f91957aa18456a14b4e90609e4ce8194c5653384ed38dada6f3'

function Write-RunnerBootstrapInfo {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "[bootstrap-windows-runner] $Message"
}

function Write-RunnerBootstrapWarning {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "::warning::$Message"
}

function Get-WindowsRunnerBootstrapScriptRoot {
    if ($PSScriptRoot) {
        return $PSScriptRoot
    }

    return Split-Path -Parent $MyInvocation.MyCommand.Path
}

function Get-WindowsRunnerBootstrapRepoRoot {
    $scriptRoot = Get-WindowsRunnerBootstrapScriptRoot
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

function Get-WindowsRunnerBootstrapDefaultDiagnosticsRoot {
    return Join-Path (Get-WindowsRunnerBootstrapRepoRoot) '.artifacts/runner-bootstrap'
}

function Resolve-WindowsRunnerBootstrapDetectOnly {
    param([bool]$DetectOnly)

    if ($env:UH_RUNNER_DISABLE_AUTO_BOOTSTRAP -eq '1') {
        Write-RunnerBootstrapInfo 'UH_RUNNER_DISABLE_AUTO_BOOTSTRAP=1 -> forcing DetectOnly.'
        return $true
    }

    return $DetectOnly
}

function Test-RunnerIsWindows {
    return [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
}

function Test-RunnerCommandExists {
    param([Parameter(Mandatory = $true)][string]$Name)
    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Test-RunnerWindowsAppsPowerShellAliasPath {
    param([AllowNull()][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    $normalizedPath = $Path.Trim().Replace('/', '\')
    return $normalizedPath.EndsWith('\Microsoft\WindowsApps\pwsh.exe', [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-RunnerPowerShell7ExecutablePath {
    param([AllowNull()][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    if (Test-RunnerWindowsAppsPowerShellAliasPath -Path $Path) {
        return $false
    }

    return Test-Path -LiteralPath $Path -PathType Leaf
}

function Get-RunnerRegistryDword {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name
    )

    try {
        $item = Get-ItemProperty -LiteralPath $Path -Name $Name -ErrorAction Stop
        return [int]$item.$Name
    } catch {
        return $null
    }
}

function Get-RunnerObjectPropertyValue {
    param(
        [AllowNull()][object]$InputObject,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $InputObject) {
        return $null
    }

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Test-RunnerUninstallDisplayName {
    param([Parameter(Mandatory = $true)][string]$Pattern)

    $roots = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
    )

    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) {
            continue
        }

        $matches = Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue |
            ForEach-Object {
                try { Get-ItemProperty -LiteralPath $_.PSPath -ErrorAction Stop } catch { $null }
            } |
            Where-Object {
                $displayName = Get-RunnerObjectPropertyValue -InputObject $_ -Name 'DisplayName'
                if ($null -eq $displayName) {
                    return $false
                }

                return [string]$displayName -match $Pattern
            }
        if ($matches) {
            return $true
        }
    }

    return $false
}

function Test-RunnerVcRedist2010X64 {
    $installed = Get-RunnerRegistryDword -Path 'HKLM:\SOFTWARE\Microsoft\VisualStudio\10.0\VC\VCRedist\x64' -Name 'Installed'
    if ($installed -eq 1) {
        return $true
    }

    return Test-RunnerUninstallDisplayName -Pattern 'Microsoft Visual C\+\+ 2010\s+x64\s+Redistributable'
}

function Test-RunnerVcRedist2015To2022X64 {
    $installed = Get-RunnerRegistryDword -Path 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64' -Name 'Installed'
    if ($installed -eq 1 -and (Test-RunnerVcRuntime140DllsPresent)) {
        return $true
    }

    return (Test-RunnerUninstallDisplayName -Pattern 'Microsoft Visual C\+\+ (2015|2017|2019|2022).*\(x64\)') -and (Test-RunnerVcRuntime140DllsPresent)
}

function Test-RunnerLongPathsEnabled {
    $enabled = Get-RunnerRegistryDword -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' -Name 'LongPathsEnabled'
    return $enabled -eq 1
}

function Test-RunnerUcrtPresent {
    if (-not (Test-RunnerIsWindows)) {
        return $false
    }

    $systemRoot = if ($env:SystemRoot) { $env:SystemRoot } else { 'C:\Windows' }
    $ucrtPath = Join-Path $systemRoot 'System32\ucrtbase.dll'
    return Test-Path -LiteralPath $ucrtPath -PathType Leaf
}

function Test-RunnerVcRuntime140DllsPresent {
    if (-not (Test-RunnerIsWindows)) {
        return $false
    }

    $systemRoot = if ($env:SystemRoot) { $env:SystemRoot } else { 'C:\Windows' }
    $system32 = Join-Path $systemRoot 'System32'
    $requiredDlls = @(
        'vcruntime140.dll',
        'vcruntime140_1.dll',
        'msvcp140.dll'
    )
    foreach ($dll in $requiredDlls) {
        if (-not (Test-Path -LiteralPath (Join-Path $system32 $dll) -PathType Leaf)) {
            return $false
        }
    }

    return $true
}

function Test-RunnerPowerShell7Present {
    $pathCommand = Get-Command pwsh -ErrorAction SilentlyContinue | Select-Object -First 1
    $pathPwsh = Get-RunnerObjectPropertyValue -InputObject $pathCommand -Name 'Source'
    if ([string]::IsNullOrWhiteSpace([string]$pathPwsh)) {
        $pathPwsh = Get-RunnerObjectPropertyValue -InputObject $pathCommand -Name 'Path'
    }

    if (Test-RunnerPowerShell7ExecutablePath -Path ([string]$pathPwsh)) {
        return $true
    }

    $roots = @(
        $env:ProgramFiles,
        ${env:ProgramFiles(x86)}
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    foreach ($root in $roots) {
        $candidate = Join-Path $root 'PowerShell\7\pwsh.exe'
        if (Test-RunnerPowerShell7ExecutablePath -Path $candidate) {
            return $true
        }
    }

    return $false
}

function Set-RunnerTlsDefaults {
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    } catch {
        Write-RunnerBootstrapWarning "Could not force TLS 1.2 for downloads: $($_.Exception.Message)"
    }
}

function Invoke-RunnerInstaller {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$DiagnosticsRoot,
        [string]$ExpectedSha256 = ''
    )

    Set-RunnerTlsDefaults
    $root = if ($env:RUNNER_TEMP) {
        Join-Path $env:RUNNER_TEMP 'unity-runner-bootstrap-installers'
    } else {
        Join-Path ([System.IO.Path]::GetTempPath()) 'unity-runner-bootstrap-installers'
    }
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    $installerPath = Join-Path $root $FileName

    try {
        Write-RunnerBootstrapInfo "Downloading $Uri"
        Invoke-WebRequest -Uri $Uri -OutFile $installerPath -UseBasicParsing

        if (-not [string]::IsNullOrWhiteSpace($ExpectedSha256)) {
            $actualSha256 = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actualSha256 -ne $ExpectedSha256.ToLowerInvariant()) {
                throw "$FileName SHA256 mismatch. Expected $ExpectedSha256, got $actualSha256."
            }
        }

        Assert-RunnerMicrosoftAuthenticodeSignature -Path $installerPath

        Write-RunnerBootstrapInfo "Running $FileName $($Arguments -join ' ')"
        $process = Start-Process -FilePath $installerPath -ArgumentList $Arguments -Wait -PassThru
        if ($process.ExitCode -ne 0 -and $process.ExitCode -ne 3010) {
            throw "$FileName exited with code $($process.ExitCode)."
        }
    } finally {
        if (Test-Path -LiteralPath $installerPath -PathType Leaf) {
            Remove-Item -LiteralPath $installerPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Assert-RunnerMicrosoftAuthenticodeSignature {
    param([Parameter(Mandatory = $true)][string]$Path)

    $signatureCommand = Get-Command Get-AuthenticodeSignature -ErrorAction SilentlyContinue
    if ($null -eq $signatureCommand) {
        throw "Cannot verify Authenticode signature for $Path because Get-AuthenticodeSignature is unavailable."
    }

    $signature = Get-AuthenticodeSignature -FilePath $Path
    if ($signature.Status -ne 'Valid') {
        throw "Installer signature for $Path is not valid: $($signature.Status) $($signature.StatusMessage)"
    }

    $subject = [string]$signature.SignerCertificate.Subject
    if ($subject -notmatch 'Microsoft') {
        throw "Installer signature for $Path is not issued to Microsoft. Signer subject: $subject"
    }
}

function Install-RunnerVcRedist2010X64 {
    param([Parameter(Mandatory = $true)][string]$DiagnosticsRoot)

    Invoke-RunnerInstaller `
        -Uri 'https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_x64.exe' `
        -FileName 'vcredist-2010-sp1-x64.exe' `
        -Arguments @('/q', '/norestart') `
        -DiagnosticsRoot $DiagnosticsRoot `
        -ExpectedSha256 $script:VcRedist2010X64Sha256
}

function Install-RunnerVcRedist2015To2022X64 {
    param([Parameter(Mandatory = $true)][string]$DiagnosticsRoot)

    Invoke-RunnerInstaller `
        -Uri 'https://aka.ms/vs/17/release/vc_redist.x64.exe' `
        -FileName 'vc-redist-2015-2022-x64.exe' `
        -Arguments @('/install', '/quiet', '/norestart') `
        -DiagnosticsRoot $DiagnosticsRoot
}

function Enable-RunnerLongPaths {
    New-Item -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' -Force | Out-Null
    New-ItemProperty `
        -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' `
        -Name 'LongPathsEnabled' `
        -PropertyType DWord `
        -Value 1 `
        -Force | Out-Null
}

function Install-RunnerPowerShell7 {
    if (-not (Test-RunnerCommandExists -Name 'winget')) {
        throw "PowerShell 7 is missing and winget is not available to install Microsoft.PowerShell."
    }

    $arguments = @(
        'install',
        '--id',
        'Microsoft.PowerShell',
        '--exact',
        '--source',
        'winget',
        '--scope',
        'machine',
        '--accept-package-agreements',
        '--accept-source-agreements',
        '--disable-interactivity',
        '--silent'
    )
    Write-RunnerBootstrapInfo "Running winget $($arguments -join ' ')"
    $wingetOutput = @(& winget @arguments 2>&1)
    $wingetExitCode = $LASTEXITCODE
    foreach ($line in $wingetOutput) {
        if ($null -ne $line) {
            Write-RunnerBootstrapInfo "[winget] $line"
        }
    }
    if ($wingetExitCode -ne 0) {
        throw "winget failed to install Microsoft.PowerShell (exit $wingetExitCode)."
    }

    Update-RunnerProcessPathFromEnvironment
}

function Update-RunnerProcessPathFromEnvironment {
    $segments = New-Object System.Collections.Generic.List[string]
    foreach ($target in @(
            [System.EnvironmentVariableTarget]::Machine,
            [System.EnvironmentVariableTarget]::User,
            [System.EnvironmentVariableTarget]::Process
        )) {
        $path = [System.Environment]::GetEnvironmentVariable('Path', $target)
        if ([string]::IsNullOrWhiteSpace($path)) {
            continue
        }

        foreach ($segment in $path -split ';') {
            if (-not [string]::IsNullOrWhiteSpace($segment) -and -not $segments.Contains($segment)) {
                $segments.Add($segment) | Out-Null
            }
        }
    }

    $env:Path = $segments -join ';'
}

function Add-RunnerDefenderExclusions {
    param([Parameter(Mandatory = $true)][string]$UnityInstallRoot)

    if (-not (Test-RunnerCommandExists -Name 'Add-MpPreference')) {
        Write-RunnerBootstrapInfo "Windows Defender cmdlets are unavailable; skipping Defender exclusions."
        return
    }

    $paths = @(
        $UnityInstallRoot,
        (Join-Path $UnityInstallRoot '_locks'),
        (Join-Path $UnityInstallRoot '_probes')
    )
    foreach ($path in $paths) {
        try {
            New-Item -ItemType Directory -Force -Path $path | Out-Null
            Add-MpPreference -ExclusionPath $path -ErrorAction Stop
            Write-RunnerBootstrapInfo "Ensured Defender exclusion: $path"
        } catch {
            Write-RunnerBootstrapWarning "Could not add Defender exclusion '$path': $($_.Exception.Message)"
        }
    }
}

function Get-WindowsRunnerPrerequisiteStatus {
    if (-not (Test-RunnerIsWindows)) {
        return @(
            [pscustomobject]@{
                Name        = 'Windows host'
                Present     = $false
                Remediation = 'Run this script on the self-hosted Windows Unity runner.'
            }
        )
    }

    return @(
        [pscustomobject]@{
            Name        = 'Windows host'
            Present     = Test-RunnerIsWindows
            Remediation = 'Run this script on the self-hosted Windows Unity runner.'
        },
        [pscustomobject]@{
            Name        = 'VC++ 2010 SP1 x64 redistributable'
            Present     = Test-RunnerVcRedist2010X64
            Remediation = 'Install Microsoft Visual C++ 2010 SP1 x64 Redistributable.'
        },
        [pscustomobject]@{
            Name        = 'VC++ 2015-2022 x64 redistributable'
            Present     = Test-RunnerVcRedist2015To2022X64
            Remediation = 'Install Microsoft Visual C++ 2015-2022 x64 Redistributable.'
        },
        [pscustomobject]@{
            Name        = 'Universal CRT'
            Present     = Test-RunnerUcrtPresent
            Remediation = 'Install current Windows updates or the Universal CRT update for this OS image.'
        },
        [pscustomobject]@{
            Name        = 'Windows long paths'
            Present     = Test-RunnerLongPathsEnabled
            Remediation = 'Set HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem LongPathsEnabled=1.'
        },
        [pscustomobject]@{
            Name        = 'PowerShell 7'
            Present     = Test-RunnerPowerShell7Present
            Remediation = 'Install Microsoft.PowerShell.'
        }
    )
}

function Write-WindowsRunnerPrerequisiteSummary {
    param(
        [Parameter(Mandatory = $true)][object[]]$Statuses,
        [string]$DiagnosticsRoot = ''
    )

    foreach ($status in $Statuses) {
        $marker = if ($status.Present) { 'OK' } else { 'MISSING' }
        Write-RunnerBootstrapInfo "$marker - $($status.Name)"
        if (-not $status.Present) {
            Write-RunnerBootstrapWarning "$($status.Name) missing. Remediation: $($status.Remediation)"
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($DiagnosticsRoot)) {
        New-Item -ItemType Directory -Force -Path $DiagnosticsRoot | Out-Null
        $summaryPath = Join-Path $DiagnosticsRoot 'windows-runner-prerequisites.json'
        $Statuses | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
        Write-RunnerBootstrapInfo "Wrote prerequisite summary: $summaryPath"
    }
}

function Invoke-WindowsRunnerBootstrap {
    [CmdletBinding()]
    param(
        [switch]$DetectOnly,
        [string]$UnityInstallRoot = $script:WindowsRunnerBootstrapDefaultInstallRoot,
        [string]$DiagnosticsRoot = $script:WindowsRunnerBootstrapDefaultDiagnosticsRoot
    )

    $bootstrapDetectOnly = Resolve-WindowsRunnerBootstrapDetectOnly -DetectOnly ([bool]$DetectOnly)
    $bootstrapDiagnosticsRoot = if ([string]::IsNullOrWhiteSpace($DiagnosticsRoot)) {
        Get-WindowsRunnerBootstrapDefaultDiagnosticsRoot
    } else {
        [string]$DiagnosticsRoot
    }

    $statuses = @(Get-WindowsRunnerPrerequisiteStatus)
    Write-WindowsRunnerPrerequisiteSummary -Statuses $statuses -DiagnosticsRoot $bootstrapDiagnosticsRoot
    $missing = @($statuses | Where-Object { -not $_.Present })
    if ($missing.Count -eq 0) {
        if (-not $bootstrapDetectOnly) {
            Add-RunnerDefenderExclusions -UnityInstallRoot $UnityInstallRoot
        }
        return 0
    }

    if ($bootstrapDetectOnly) {
        return 2
    }

    foreach ($status in $missing) {
        switch ($status.Name) {
            'Windows host' {
                throw $status.Remediation
            }
            'VC++ 2010 SP1 x64 redistributable' {
                Install-RunnerVcRedist2010X64 -DiagnosticsRoot $bootstrapDiagnosticsRoot
            }
            'VC++ 2015-2022 x64 redistributable' {
                Install-RunnerVcRedist2015To2022X64 -DiagnosticsRoot $bootstrapDiagnosticsRoot
            }
            'Universal CRT' {
                Write-RunnerBootstrapWarning $status.Remediation
            }
            'Windows long paths' {
                Enable-RunnerLongPaths
            }
            'PowerShell 7' {
                Install-RunnerPowerShell7
            }
        }
    }

    $after = @(Get-WindowsRunnerPrerequisiteStatus)
    Write-WindowsRunnerPrerequisiteSummary -Statuses $after -DiagnosticsRoot $bootstrapDiagnosticsRoot
    $stillMissing = @($after | Where-Object { -not $_.Present })
    if ($stillMissing.Count -gt 0) {
        foreach ($status in $stillMissing) {
            Write-RunnerBootstrapWarning "Still missing after bootstrap: $($status.Name). $($status.Remediation)"
        }
        return 1
    }

    Add-RunnerDefenderExclusions -UnityInstallRoot $UnityInstallRoot
    return 0
}

if ($MyInvocation.InvocationName -ne '.') {
    $exitCode = Invoke-WindowsRunnerBootstrap `
        -DetectOnly:$RunnerBootstrapDetectOnly `
        -UnityInstallRoot $RunnerBootstrapUnityInstallRoot `
        -DiagnosticsRoot $RunnerBootstrapDiagnosticsRoot
    exit $exitCode
}
