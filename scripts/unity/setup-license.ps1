Param(
    [switch]$VerboseOutput,
    [switch]$Check,
    [switch]$Reset,
    [switch]$Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/credential-redaction.ps1')

$ScriptName = 'setup-license'

# ── Default Unity image settings (match run-unity-docker.sh defaults) ────────
$DefaultUnityVersion = '2021.3.45f1'
$DefaultImageVersion = '3'

$UnityVersion = $(if ($env:UNITY_VERSION) { $env:UNITY_VERSION } else { $DefaultUnityVersion })
$ImageVersion = $(if ($env:UNITY_IMAGE_VERSION) { $env:UNITY_IMAGE_VERSION } else { $DefaultImageVersion })
$UnityImage = "unityci/editor:ubuntu-${UnityVersion}-base-${ImageVersion}"

# ── Color-coded output helpers ───────────────────────────────────────────────

function Write-Info($msg) {
    Write-Host "  [info] $msg" -ForegroundColor Cyan
}

function Write-VerboseInfo($msg) {
    if ($VerboseOutput) { Write-Host "  [verbose] $msg" -ForegroundColor DarkCyan }
}

function Write-Success($msg) {
    Write-Host "  [ok] $msg" -ForegroundColor Green
}

function Write-WarningMsg($msg) {
    Write-Host "  [warn] $msg" -ForegroundColor Yellow
}

function Write-ErrorMsg($msg) {
    Write-Host "  [error] $msg" -ForegroundColor Red
}

function Write-SectionHeader {
    param([string]$Title)
    $bar = [string]::new([char]0x2500, [Math]::Max($Title.Length + 4, 40))
    Write-Host ''
    Write-Host $bar -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host $bar -ForegroundColor Cyan
}

function Write-SubHeader {
    param([string]$Title)
    Write-Host ''
    Write-Host "  $Title" -ForegroundColor White
    Write-Host "  $([string]::new([char]0x2500, $Title.Length))" -ForegroundColor DarkGray
}

# ── Show-Help ────────────────────────────────────────────────────────────────

function Show-Help {
    Write-Host @"

Unity License Setup
====================

Interactive script to configure Unity license credentials for headless
compilation and testing in the devcontainer. Automatically detects existing
licenses, Docker availability, and environment variables.

Usage:
    pwsh -NoProfile -File scripts/unity/setup-license.ps1 [options]

Options:
    -VerboseOutput    Enable verbose logging
    -Check            Check if license is configured (exit 0/1)
    -Reset            Remove existing config and start fresh
    -Help             Show this help message

License Types:
    Personal (Free)   Uses email + password for online activation inside Docker.
                      Auto-detected .ulf files are loaded as an optional fallback.
    Pro / Plus        Uses serial key activation directly via Docker.

Secrets Directory:
    .unity-secrets/
        license.ulf        Personal license fallback file (optional)
        credentials.env    KEY=VALUE credentials file (email, password, serial)

Environment Variable Precedence:
    Environment variables take priority over .unity-secrets/ files.
    Set UNITY_EMAIL + UNITY_PASSWORD for Personal, or UNITY_SERIAL +
    UNITY_EMAIL + UNITY_PASSWORD for Pro, to skip file-based config.

Examples:
    # Interactive setup (auto-detects everything it can)
    pwsh -NoProfile -File scripts/unity/setup-license.ps1

    # Check if configured
    pwsh -NoProfile -File scripts/unity/setup-license.ps1 -Check

    # Reconfigure from scratch
    pwsh -NoProfile -File scripts/unity/setup-license.ps1 -Reset

    # Verbose output for troubleshooting
    pwsh -NoProfile -File scripts/unity/setup-license.ps1 -VerboseOutput

"@
}

# ── Repository and safety helpers ────────────────────────────────────────────

function Get-RepoRoot {
    $current = $PSScriptRoot
    while ($current) {
        $packageJson = Join-Path $current 'package.json'
        if (Test-Path $packageJson) {
            $content = Get-Content -Path $packageJson -Raw
            if ($content -match 'com\.wallstop-studios\.unity-helpers') {
                return $current
            }
        }
        $parent = Split-Path $current -Parent
        if ($parent -eq $current) { break }
        $current = $parent
    }
    return $null
}

function Test-GitignoreContainsSecrets {
    param([string]$RepoRoot)

    $gitignorePath = Join-Path $RepoRoot '.gitignore'
    if (-not (Test-Path $gitignorePath)) {
        return $false
    }

    $content = Get-Content -Path $gitignorePath -Raw
    return ($content -match '\.unity-secrets/')
}

function Test-GitIgnoresSecrets {
    param([string]$RepoRoot)

    try {
        # git check-ignore works against .gitignore patterns — the path need not exist
        $null = & git -C $RepoRoot check-ignore -q '.unity-secrets/' 2>&1
        return ($LASTEXITCODE -eq 0)
    }
    catch {
        return $false
    }
}

# ── Credential helpers ───────────────────────────────────────────────────────

function Get-MaskedSerial {
    param([string]$Serial)

    if (-not $Serial -or $Serial.Length -lt 10) {
        return '****'
    }

    $parts = $Serial -split '-'
    if ($parts.Count -eq 6) {
        return "$($parts[0])-****-****-****-****-$($parts[5])"
    }
    return "$($Serial.Substring(0, 2))****$($Serial.Substring($Serial.Length - 4))"
}

function Get-MaskedPassword {
    param([string]$Password)
    if (-not $Password) { return '(empty)' }
    return "$([string]::new('*', [Math]::Min($Password.Length, 12)))"
}

function Get-MaskedEmail {
    param([string]$Email)
    if (-not $Email) { return '(none)' }
    $atIdx = $Email.IndexOf('@')
    if ($atIdx -le 0) { return $Email }
    $localPart = $Email.Substring(0, $atIdx)
    $domain = $Email.Substring($atIdx)
    if ($localPart.Length -le 2) {
        return "${localPart}${domain}"
    }
    return "$($localPart.Substring(0, 2))****${domain}"
}

function Test-UlfContent {
    param([string]$Content)
    return ($Content -match '<root>' -and $Content -match '<License')
}

function Get-SerialFromUlf {
    param([string]$UlfContent)

    try {
        if (-not $UlfContent) { return '' }

        $devDataMatch = [regex]::Match($UlfContent, 'DeveloperData\s+Value="([^"]+)"')
        if (-not $devDataMatch.Success) {
            Write-VerboseInfo 'No DeveloperData Value attribute found in ULF content.'
            return ''
        }

        $base64Value = $devDataMatch.Groups[1].Value
        $decodedBytes = [System.Convert]::FromBase64String($base64Value)
        $decodedString = [System.Text.Encoding]::UTF8.GetString($decodedBytes)

        $serialMatch = [regex]::Match($decodedString, '[A-Z0-9]{2}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}')
        if ($serialMatch.Success) {
            Write-VerboseInfo 'Successfully extracted serial from ULF DeveloperData.'
            return $serialMatch.Value
        }

        Write-VerboseInfo 'No serial pattern found in decoded DeveloperData.'
        return ''
    }
    catch {
        Write-VerboseInfo "Failed to extract serial from ULF: $_"
        return ''
    }
}

# ── Input helpers ────────────────────────────────────────────────────────────

function Read-HostChoice {
    param(
        [string]$Prompt,
        [string[]]$ValidChoices,
        [string]$Default = ''
    )

    while ($true) {
        if ($Default) {
            $userInput = Read-Host "  $Prompt [$Default]"
            if ([string]::IsNullOrWhiteSpace($userInput)) {
                $userInput = $Default
            }
        }
        else {
            $userInput = Read-Host "  $Prompt"
        }

        if ($ValidChoices -contains $userInput) {
            return $userInput
        }

        Write-WarningMsg "Invalid choice '$userInput'. Please enter one of: $($ValidChoices -join ', ')"
    }
}

function Read-YesNo {
    param(
        [string]$Prompt,
        [bool]$DefaultYes = $false
    )

    $suffix = $(if ($DefaultYes) { '[Y/n]' } else { '[y/N]' })

    while ($true) {
        $userInput = Read-Host "  $Prompt $suffix"

        if ([string]::IsNullOrWhiteSpace($userInput)) {
            return $DefaultYes
        }

        switch ($userInput.ToLower()) {
            'y' { return $true }
            'yes' { return $true }
            'n' { return $false }
            'no' { return $false }
            default {
                Write-WarningMsg "Please enter 'y' or 'n'."
            }
        }
    }
}

function Read-MaskedInput {
    param([string]$Prompt)

    try {
        return (Read-Host "  $Prompt" -MaskInput)
    }
    catch {
        Write-VerboseInfo 'MaskInput not available, falling back to SecureString.'
        $secureStr = Read-Host "  $Prompt" -AsSecureString
        $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureStr)
        try {
            return [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        }
        finally {
            [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
}

function Wait-EnterKey {
    param([string]$Message = 'Press Enter to continue...')
    Read-Host "  $Message"
}

# ── Existing config reader ───────────────────────────────────────────────────

function Get-ExistingConfig {
    param([string]$SecretsDir)

    $config = @{
        HasCredentials  = $false
        HasLicenseFile  = $false
        LicenseType     = ''
        Email           = ''
        Serial          = ''
        Password        = ''
        LicenseFileSize = 0
    }

    $credentialsPath = Join-Path $SecretsDir 'credentials.env'
    if (Test-Path $credentialsPath) {
        $config.HasCredentials = $true
        $lines = Get-Content -Path $credentialsPath
        foreach ($line in $lines) {
            if ($line -match '^UNITY_LICENSE_TYPE=(.+)$') {
                $config.LicenseType = $Matches[1]
            }
            elseif ($line -match '^UNITY_EMAIL=(.+)$') {
                $config.Email = $Matches[1]
            }
            elseif ($line -match '^UNITY_SERIAL=(.+)$') {
                $config.Serial = $Matches[1]
            }
            elseif ($line -match '^UNITY_PASSWORD=(.+)$') {
                $config.Password = $Matches[1]
            }
        }
    }

    $licensePath = Join-Path $SecretsDir 'license.ulf'
    if (Test-Path $licensePath) {
        $config.HasLicenseFile = $true
        $config.LicenseFileSize = (Get-Item $licensePath).Length
    }

    return $config
}

function Test-ConfigIsComplete {
    param($Config)

    if (-not $Config.HasCredentials) { return $false }

    if ($Config.LicenseType -eq 'personal' -and $Config.Email -and $Config.Password) {
        return $true
    }

    # Backward compatibility: accept Personal configs with ULF but no credentials
    if ($Config.LicenseType -eq 'personal' -and $Config.HasLicenseFile) {
        return $true
    }

    if ($Config.LicenseType -eq 'pro' -and $Config.Serial -and $Config.Email -and $Config.Password) {
        return $true
    }

    return $false
}

function Write-CurrentConfig {
    param($Config)

    Write-Host ''
    Write-Host '  Current Unity License Configuration' -ForegroundColor Cyan
    Write-Host '  -----------------------------------'
    Write-Host "    License type:  $($Config.LicenseType)"

    if ($Config.Email) {
        Write-Host "    Email:         $(Get-MaskedEmail $Config.Email)"
    }

    if ($Config.Serial) {
        Write-Host "    Serial:        $(Get-MaskedSerial $Config.Serial)"
    }

    if ($Config.HasLicenseFile) {
        Write-Host "    License file:  .unity-secrets/license.ulf ($($Config.LicenseFileSize) bytes)"
    }

    Write-Host ''
}

# ── Docker helpers ───────────────────────────────────────────────────────────

function New-DockerEnvFile {
    # Writes KEY=VALUE pairs to a temp file for use with docker run --env-file.
    # NOTE: Docker --env-file format does not support multi-line values.
    # This function is only safe for single-line values (serial, email, password).
    param([hashtable]$EnvVars)

    $tempFile = [System.IO.Path]::GetTempFileName()
    $lines = @()
    foreach ($key in $EnvVars.Keys) {
        $lines += "$key=$($EnvVars[$key])"
    }
    [System.IO.File]::WriteAllText($tempFile, ($lines -join "`n") + "`n")
    return $tempFile
}

function Remove-DockerEnvFile {
    param([string]$Path)
    if ($Path -and (Test-Path $Path)) {
        Remove-Item -Path $Path -Force -ErrorAction SilentlyContinue
    }
}

function Test-DockerAvailable {
    try {
        $null = & docker info 2>&1
        return ($LASTEXITCODE -eq 0)
    }
    catch {
        return $false
    }
}

function Test-DockerImagePulled {
    try {
        $null = & docker image inspect $UnityImage 2>&1
        return ($LASTEXITCODE -eq 0)
    }
    catch {
        return $false
    }
}

function Invoke-DockerPull {
    Write-Info "Pulling Unity Docker image: $UnityImage"
    Write-Info 'This may take 5-15 minutes on first run...'
    Write-Host ''

    $pullResult = & docker pull $UnityImage 2>&1
    $pullExitCode = $LASTEXITCODE

    if ($pullExitCode -ne 0) {
        Write-ErrorMsg "Failed to pull Docker image: $UnityImage"
        foreach ($line in $pullResult) {
            Write-ErrorMsg "  $line"
        }
        return $false
    }

    Write-Success "Docker image pulled successfully."
    return $true
}

# ── ULF search helpers ──────────────────────────────────────────────────────

function Find-UlfFiles {
    $candidates = @()

    # Well-known locations
    $knownPaths = @(
        '/root/.local/share/unity3d/Unity/Unity_lic.ulf'
    )

    if ($HOME) {
        $knownPaths += (Join-Path $HOME '.local/share/unity3d/Unity/Unity_lic.ulf')
    }

    if ($IsWindows) {
        $knownPaths += 'C:\ProgramData\Unity\Unity_lic.ulf'
    }

    if ($IsLinux) {
        # Config directory (some Unity versions)
        $knownPaths += '/root/.config/unity3d/Unity/Unity_lic.ulf'
        if ($HOME) {
            $knownPaths += (Join-Path $HOME '.config/unity3d/Unity/Unity_lic.ulf')
        }

        # XDG override paths
        if ($env:XDG_DATA_HOME) {
            $knownPaths += (Join-Path $env:XDG_DATA_HOME 'unity3d/Unity/Unity_lic.ulf')
        }
        if ($env:XDG_CONFIG_HOME) {
            $knownPaths += (Join-Path $env:XDG_CONFIG_HOME 'unity3d/Unity/Unity_lic.ulf')
        }

        # Flatpak paths
        if ($HOME) {
            $knownPaths += (Join-Path $HOME '.var/app/com.unity.UnityHub/config/unity3d/Unity/Unity_lic.ulf')
            $knownPaths += (Join-Path $HOME '.var/app/com.unity.UnityHub/data/unity3d/Unity/Unity_lic.ulf')

            # Snap paths
            $knownPaths += (Join-Path $HOME 'snap/unity-hub/current/.local/share/unity3d/Unity/Unity_lic.ulf')
            $knownPaths += (Join-Path $HOME 'snap/unity-hub/current/.config/unity3d/Unity/Unity_lic.ulf')
            $knownPaths += (Join-Path $HOME 'snap/unity-hub/common/.local/share/unity3d/Unity/Unity_lic.ulf')
            $knownPaths += (Join-Path $HOME 'snap/unity-hub/common/.config/unity3d/Unity/Unity_lic.ulf')
        }
    }

    if ($IsMacOS) {
        $knownPaths += '/Library/Application Support/Unity/Unity_lic.ulf'
        if ($HOME) {
            $knownPaths += (Join-Path $HOME 'Library/Application Support/Unity/Unity_lic.ulf')
        }
    }

    # Temp paths
    if ($IsLinux -or $IsMacOS) {
        $knownPaths += '/tmp/unity.ulf'
    }

    foreach ($p in $knownPaths) {
        try {
            if ($p -and (Test-Path $p)) {
                $candidates += $p
            }
        }
        catch {
            Write-VerboseInfo "Cannot access path: $p"
        }
    }

    # Fast find in common directories (limit depth to avoid long scans)
    $searchDirs = @()
    if ($HOME -and (Test-Path $HOME)) { $searchDirs += $HOME }
    if (($IsLinux -or $IsMacOS) -and $HOME -ne '/root') {
        try { if (Test-Path '/root') { $searchDirs += '/root' } } catch { }
    }

    foreach ($searchDir in $searchDirs) {
        try {
            if (Get-Command fd -ErrorAction SilentlyContinue) {
                $fdResults = & fd --type f --extension ulf --max-depth 8 --hidden '.' $searchDir 2>$null
                if ($fdResults) {
                    foreach ($f in $fdResults) {
                        $f = $f.Trim()
                        if ($f -and ($candidates -notcontains $f)) {
                            $candidates += $f
                        }
                    }
                }
            }
            else {
                $findResults = & find $searchDir -maxdepth 8 -name '*.ulf' -type f 2>$null
                if ($findResults) {
                    foreach ($f in $findResults) {
                        $f = $f.Trim()
                        if ($f -and ($candidates -notcontains $f)) {
                            $candidates += $f
                        }
                    }
                }
            }
        }
        catch {
            Write-VerboseInfo "ULF search in $searchDir encountered an error: $_"
        }
    }

    return $candidates
}

function Find-DockerUlfFiles {
    $results = @()

    try {
        $dockerAvailable = Test-DockerAvailable

        if (-not $dockerAvailable) {
            Write-VerboseInfo 'Docker not available, skipping Docker ULF search.'
            return $results
        }

        Write-VerboseInfo 'Searching Docker containers for Unity license files...'

        $containerLines = @()
        $maxContainersToSearch = 50
        try {
            $rawOutput = & docker ps -a --format '{{.ID}} {{.Image}} {{.Names}} {{.State}}' --no-trunc 2>&1
            if ($LASTEXITCODE -eq 0 -and $rawOutput) {
                $containerLines = @($rawOutput)
            }
        }
        catch {
            Write-VerboseInfo "Failed to list Docker containers: $_"
            return $results
        }

        if ($containerLines.Count -gt $maxContainersToSearch) {
            Write-VerboseInfo "Found $($containerLines.Count) containers, limiting search to first $maxContainersToSearch."
            $containerLines = $containerLines[0..($maxContainersToSearch - 1)]
        }

        foreach ($line in $containerLines) {
            $lineTrimmed = "$line".Trim()
            if (-not $lineTrimmed) { continue }

            $parts = $lineTrimmed -split '\s+', 4
            if ($parts.Count -lt 3) { continue }

            $containerId = $parts[0]
            $containerName = $parts[2]
            $containerState = $(if ($parts.Count -ge 4) { $parts[3] } else { 'unknown' })

            Write-VerboseInfo "Checking container '$containerName' ($containerId) [state: $containerState]..."

            $ulfPaths = @(
                '/root/.local/share/unity3d/Unity/Unity_lic.ulf'
                '/root/.config/unity3d/Unity/Unity_lic.ulf'
            )
            $foundInContainer = $false

            foreach ($ulfPath in $ulfPaths) {
                if ($foundInContainer) { break }
                $ulfContent = ''

                try {
                    if ($containerState -match '(?i)running') {
                        $ulfContent = & docker exec $containerId cat $ulfPath 2>$null
                        if ($LASTEXITCODE -ne 0) { $ulfContent = '' }
                    }

                    if (-not $ulfContent) {
                        $tempFile = [System.IO.Path]::GetTempFileName()
                        try {
                            $null = & docker cp "${containerId}:${ulfPath}" $tempFile 2>&1
                            if ($LASTEXITCODE -eq 0 -and (Test-Path $tempFile)) {
                                $ulfContent = [System.IO.File]::ReadAllText($tempFile)
                            }
                        }
                        catch {
                            Write-VerboseInfo "docker cp failed for container '$containerName' at path '$ulfPath': $_"
                        }
                        finally {
                            if (Test-Path $tempFile) { Remove-Item -Path $tempFile -Force -ErrorAction SilentlyContinue }
                        }
                    }
                }
                catch {
                    Write-VerboseInfo "Error extracting ULF from container '$containerName' at path '$ulfPath': $_"
                    continue
                }

                if ($ulfContent -and (Test-UlfContent $ulfContent)) {
                    Write-VerboseInfo "Found valid ULF in container '$containerName' at '$ulfPath'."
                    $results += @{
                        ContainerId   = $containerId
                        ContainerName = $containerName
                        UlfContent    = $ulfContent
                    }
                    $foundInContainer = $true
                }
            }
        }

        # Also check Docker volumes for Unity data (only if image is already pulled)
        if (Test-DockerImagePulled) {
            try {
                Write-VerboseInfo 'Searching Docker volumes for Unity license files...'
                $volumeOutput = & docker volume ls --format '{{.Name}}' 2>$null
                if ($LASTEXITCODE -eq 0 -and $volumeOutput) {
                    $volumes = @($volumeOutput)
                    foreach ($volumeName in $volumes) {
                        $volumeNameStr = "$volumeName".Trim()
                        if (-not $volumeNameStr) { continue }
                        if ($volumeNameStr -notmatch '(?i)unity') { continue }

                        Write-VerboseInfo "Checking volume '$volumeNameStr' for license files..."
                        $volumeUlfPaths = @(
                            '/vol/Unity_lic.ulf'
                            '/vol/.local/share/unity3d/Unity/Unity_lic.ulf'
                            '/vol/.config/unity3d/Unity/Unity_lic.ulf'
                        )

                        $foundInVolume = $false
                        foreach ($volumeUlfPath in $volumeUlfPaths) {
                            if ($foundInVolume) { break }
                            try {
                                $ulfContent = & docker run --rm -v "${volumeNameStr}:/vol:ro" "$UnityImage" cat $volumeUlfPath 2>$null
                                if ($LASTEXITCODE -eq 0 -and $ulfContent) {
                                    $joinedContent = $ulfContent -join "`n"
                                    if (Test-UlfContent $joinedContent) {
                                        Write-VerboseInfo "Found valid ULF in Docker volume '$volumeNameStr' at '$volumeUlfPath'."
                                        $results += @{
                                            ContainerId   = ''
                                            ContainerName = "volume:$volumeNameStr"
                                            UlfContent    = $joinedContent
                                        }
                                        $foundInVolume = $true
                                    }
                                }
                            }
                            catch {
                                Write-VerboseInfo "Failed to check volume '$volumeNameStr' at path '$volumeUlfPath': $_"
                            }
                        }
                    }
                }
            }
            catch {
                Write-VerboseInfo "Docker volume search encountered an error: $_"
            }
        }
        else {
            Write-VerboseInfo 'Docker image not pulled, skipping Docker volume search.'
        }

        if ($results.Count -eq 0) {
            Write-VerboseInfo 'No Unity license files found in Docker containers or volumes.'
        }
        else {
            Write-VerboseInfo "Found $($results.Count) ULF file(s) in Docker containers/volumes."
        }
    }
    catch {
        Write-VerboseInfo "Docker ULF search encountered an error: $_"
    }

    return $results
}

# ── Environment variable detection ──────────────────────────────────────────

function Get-EnvironmentStatus {
    $status = @{
        HasUnityLicense  = $false
        HasUnitySerial   = $false
        HasUnityEmail    = $false
        HasUnityPassword = $false
        LicenseContent   = ''
        Serial           = ''
        Email            = ''
        Password         = ''
    }

    if ($env:UNITY_LICENSE) {
        $status.HasUnityLicense = $true
        $status.LicenseContent = $env:UNITY_LICENSE
    }

    if ($env:UNITY_SERIAL) {
        $status.HasUnitySerial = $true
        $status.Serial = $env:UNITY_SERIAL
    }

    if ($env:UNITY_EMAIL) {
        $status.HasUnityEmail = $true
        $status.Email = $env:UNITY_EMAIL
    }

    if ($env:UNITY_PASSWORD) {
        $status.HasUnityPassword = $true
        $status.Password = $env:UNITY_PASSWORD
    }

    return $status
}

# ── Pro license Docker test ──────────────────────────────────────────────────

function Test-ProActivation {
    param(
        [string]$Serial,
        [string]$Email,
        [string]$Password
    )

    Write-Info 'Testing Pro license activation via Docker...'
    Write-Info 'This may take 1-3 minutes...'

    # Use printf '%s' to safely pass credentials without shell interpretation
    $innerScript = @'
#!/usr/bin/env bash
set -euo pipefail
echo "==> Attempting serial activation..."
EXIT_CODE=0
unity-editor -batchmode -nographics -quit \
    -serial "$(printf '%s' "$UNITY_SERIAL")" \
    -username "$(printf '%s' "$UNITY_EMAIL")" \
    -password "$(printf '%s' "$UNITY_PASSWORD")" \
    -logFile - || EXIT_CODE=$?

if [ $EXIT_CODE -eq 0 ]; then
    echo "==> ACTIVATION_SUCCESS"
else
    echo "==> ACTIVATION_FAILED (exit code: $EXIT_CODE)"
fi

# Return the license regardless
echo "==> Returning license..."
unity-editor -batchmode -nographics -quit -returnlicense -logFile - || true
exit $EXIT_CODE
'@

    # Use --env-file to avoid exposing credentials on the command line
    $envFile = New-DockerEnvFile @{
        UNITY_SERIAL   = $Serial
        UNITY_EMAIL    = $Email
        UNITY_PASSWORD = $Password
    }

    try {
        $dockerOutput = & docker run --rm `
            --env-file $envFile `
            $UnityImage `
            bash -c $innerScript 2>&1

        $exitCode = $LASTEXITCODE
    }
    finally {
        Remove-DockerEnvFile $envFile
    }

    $outputText = $(if ($dockerOutput) { $dockerOutput -join "`n" } else { '' })
    $activationSuccess = $outputText -match 'ACTIVATION_SUCCESS'

    if ($activationSuccess -or $exitCode -eq 0) {
        Write-Success 'Pro license activation test PASSED.'
        return $true
    }

    Write-ErrorMsg 'Pro license activation test FAILED.'
    if ($VerboseOutput -and $dockerOutput) {
        Write-VerboseInfo 'Docker output (last 20 lines):'
        $lastLines = ($dockerOutput | Select-Object -Last 20)
        foreach ($line in $lastLines) {
            Write-VerboseInfo (ConvertTo-UnitySafeLogText -Text "  $line")
        }
    }
    return $false
}

# ── Write configuration files ───────────────────────────────────────────────

function Write-SecretsFiles {
    param(
        [string]$SecretsDir,
        [string]$LicenseType,
        [string]$Email,
        [string]$Serial,
        [string]$Password,
        [string]$UlfContent
    )

    # Create .unity-secrets/ directory
    if (-not (Test-Path $SecretsDir)) {
        New-Item -ItemType Directory -Path $SecretsDir -Force | Out-Null
        Write-VerboseInfo "Created directory: $SecretsDir"
    }

    # Write credentials.env
    $credentialsPath = Join-Path $SecretsDir 'credentials.env'
    $credentialsLines = @(
        '# Unity License Credentials'
        '# Generated by scripts/unity/setup-license.ps1'
        '# WARNING: This file contains sensitive credentials. NEVER commit to git.'
        "UNITY_LICENSE_TYPE=$LicenseType"
    )

    if ($Email) {
        $credentialsLines += "UNITY_EMAIL=$Email"
    }

    if ($Password) {
        $credentialsLines += "UNITY_PASSWORD=$Password"
    }

    if ($LicenseType -eq 'pro' -and $Serial) {
        $credentialsLines += "UNITY_SERIAL=$Serial"
    }

    $credentialsContent = ($credentialsLines -join "`n") + "`n"
    [System.IO.File]::WriteAllText($credentialsPath, $credentialsContent)
    Write-VerboseInfo "Written: $credentialsPath"

    # Write license.ulf (Personal only)
    if ($LicenseType -eq 'personal' -and $UlfContent) {
        $licensePath = Join-Path $SecretsDir 'license.ulf'
        [System.IO.File]::WriteAllText($licensePath, $UlfContent)
        Write-VerboseInfo "Written: $licensePath"
    }

    # Set restrictive file permissions
    Set-SecretPermissions -SecretsDir $SecretsDir -LicenseType $LicenseType
}

function Set-SecretPermissions {
    param(
        [string]$SecretsDir,
        [string]$LicenseType
    )

    if ($IsLinux -or $IsMacOS) {
        try {
            $credentialsPath = Join-Path $SecretsDir 'credentials.env'
            & chmod 600 $credentialsPath 2>$null
            if ($LicenseType -eq 'personal') {
                $licensePath = Join-Path $SecretsDir 'license.ulf'
                if (Test-Path $licensePath) {
                    & chmod 600 $licensePath 2>$null
                }
            }
            & chmod 700 $SecretsDir 2>$null
            Write-VerboseInfo 'Set restrictive file permissions (600/700).'
        }
        catch {
            Write-VerboseInfo 'Could not set file permissions (non-critical).'
        }
    }
    elseif ($IsWindows) {
        try {
            & icacls $SecretsDir /inheritance:r /grant:r "${env:USERNAME}:(OI)(CI)F" 2>$null | Out-Null
            Write-VerboseInfo 'Set restrictive ACL permissions (Windows).'
        }
        catch {
            Write-VerboseInfo 'Could not set ACL permissions (non-critical).'
        }
    }
}

# ── Validation ───────────────────────────────────────────────────────────────

function Test-WrittenConfig {
    param(
        [string]$SecretsDir,
        [string]$RepoRoot,
        [string]$LicenseType
    )

    $errors = @()

    $credentialsPath = Join-Path $SecretsDir 'credentials.env'
    if (-not (Test-Path $credentialsPath)) {
        $errors += 'credentials.env was not written successfully.'
    }

    if ($LicenseType -eq 'personal') {
        $licensePath = Join-Path $SecretsDir 'license.ulf'
        if (-not (Test-Path $licensePath)) {
            $errors += 'license.ulf was not written successfully.'
        }
    }

    if (-not (Test-GitignoreContainsSecrets $RepoRoot)) {
        $errors += '.unity-secrets/ is not in .gitignore.'
    }

    if (-not (Test-GitIgnoresSecrets $RepoRoot)) {
        $errors += 'git check-ignore did not confirm .unity-secrets/ is ignored.'
    }

    return $errors
}

function Invoke-OptionalVerification {
    param(
        [string]$LicenseType,
        [string]$SecretsDir,
        [bool]$DockerAvailable
    )

    if (-not $DockerAvailable) {
        Write-WarningMsg 'Docker not available; skipping activation verification.'
        return
    }

    $doVerify = Read-YesNo 'Test that credentials work by running a quick Docker activation?' -DefaultYes $false

    if (-not $doVerify) {
        Write-Info 'Skipping verification.'
        return
    }

    Write-Info 'Running verification test (this may take 1-3 minutes)...'

    if ($LicenseType -eq 'personal') {
        $licensePath = Join-Path $SecretsDir 'license.ulf'

        $innerScript = @'
#!/usr/bin/env bash
set -euo pipefail
echo "==> Activating with .ulf file..."
EXIT_CODE=0
unity-editor -batchmode -nographics -quit -manualLicenseFile /tmp/unity.ulf -logFile - || EXIT_CODE=$?
if [ $EXIT_CODE -eq 0 ]; then
    echo "==> VERIFICATION_SUCCESS"
else
    echo "==> VERIFICATION_FAILED (exit code: $EXIT_CODE)"
fi
exit $EXIT_CODE
'@

        # Mount the .ulf file directly into the container (avoids multi-line env var issues)
        $dockerOutput = & docker run --rm `
            -v "${licensePath}:/tmp/unity.ulf:ro" `
            $UnityImage `
            bash -c $innerScript 2>&1

        $exitCode = $LASTEXITCODE
        $outputText = $(if ($dockerOutput) { $dockerOutput -join "`n" } else { '' })
        if ($outputText -match 'VERIFICATION_SUCCESS' -or $exitCode -eq 0) {
            Write-Success 'Personal license verification PASSED.'
        }
        else {
            Write-WarningMsg 'Personal license verification returned non-zero exit code.'
            Write-WarningMsg 'This is common for Personal licenses and may not indicate a problem.'
            Write-WarningMsg 'The license may still work for compilation and testing.'
        }
    }
    elseif ($LicenseType -eq 'pro') {
        $config = Get-ExistingConfig $SecretsDir
        $result = Test-ProActivation -Serial $config.Serial -Email $config.Email -Password $config.Password
        if (-not $result) {
            Write-WarningMsg 'Pro license activation verification failed.'
            Write-WarningMsg 'Please verify your serial, email, and password are correct.'
        }
    }
}

# ── Summary display ──────────────────────────────────────────────────────────

function Write-FinalSummary {
    param(
        [string]$LicenseType,
        [string]$Email,
        [string]$Serial,
        [string]$SecretsDir,
        [bool]$GitIgnored
    )

    $licenseFileInfo = ''
    if ($LicenseType -eq 'personal') {
        $licensePath = Join-Path $SecretsDir 'license.ulf'
        if (Test-Path $licensePath) {
            $fileSize = (Get-Item $licensePath).Length
            $licenseFileInfo = ".unity-secrets/license.ulf ($fileSize bytes)"
        }
    }

    Write-SectionHeader 'License Configured Successfully'

    Write-Host ''
    Write-Host "    License type:  $LicenseType" -ForegroundColor White

    if ($Email) {
        Write-Host "    Email:         $(Get-MaskedEmail $Email)" -ForegroundColor White
    }

    if ($LicenseType -eq 'pro' -and $Serial) {
        Write-Host "    Serial:        $(Get-MaskedSerial $Serial)" -ForegroundColor White
    }

    if ($licenseFileInfo) {
        Write-Host "    License file:  $licenseFileInfo" -ForegroundColor White
    }

    Write-Host "    Credentials:   .unity-secrets/credentials.env" -ForegroundColor White

    if ($GitIgnored) {
        Write-Host '    Git status:    .unity-secrets/ is properly gitignored' -ForegroundColor Green
    }
    else {
        Write-Host '    Git status:    WARNING - could not confirm gitignore' -ForegroundColor Yellow
    }

    Write-Host ''
    Write-Host '  Next steps:' -ForegroundColor Cyan
    Write-Host '    1. Run Unity setup:       npm run unity:setup'
    Write-Host '    2. Compile the package:   npm run unity:compile'
    Write-Host '    3. Run tests:             npm run unity:test'
    Write-Host ''
    Write-Host '  For AI agents: Unity scripts automatically load credentials' -ForegroundColor DarkGray
    Write-Host '  from .unity-secrets/ at runtime. No manual env var setup needed.' -ForegroundColor DarkGray
    Write-Host ''
}

# ── Auto-detection phase ────────────────────────────────────────────────────

function Invoke-AutoDetection {
    param(
        [string]$SecretsDir
    )

    $detection = @{
        ExistingConfig   = $null
        ConfigComplete   = $false
        UlfFilesFound    = @()
        DockerAvailable  = $false
        ImagePulled      = $false
        EnvStatus        = $null
        EnvConfigured    = $false
    }

    Write-SectionHeader 'Auto-Detection'

    # 1. Check existing .unity-secrets/ config
    Write-Info 'Checking existing configuration...'
    $detection.ExistingConfig = Get-ExistingConfig $SecretsDir
    $detection.ConfigComplete = Test-ConfigIsComplete $detection.ExistingConfig

    if ($detection.ConfigComplete) {
        Write-Success "Existing $($detection.ExistingConfig.LicenseType) license configuration found."
    }
    elseif ($detection.ExistingConfig.HasCredentials) {
        Write-WarningMsg 'Partial configuration found (incomplete).'
    }
    else {
        Write-Info 'No existing configuration found.'
    }

    # 2. Check environment variables
    Write-Info 'Checking environment variables...'
    $detection.EnvStatus = Get-EnvironmentStatus

    if ($detection.EnvStatus.HasUnityLicense) {
        Write-Success 'UNITY_LICENSE environment variable is set.'
        $detection.EnvConfigured = $true
    }
    elseif ($detection.EnvStatus.HasUnitySerial -and $detection.EnvStatus.HasUnityEmail -and $detection.EnvStatus.HasUnityPassword) {
        Write-Success 'UNITY_SERIAL + UNITY_EMAIL + UNITY_PASSWORD environment variables are set.'
        $detection.EnvConfigured = $true
    }
    elseif ($detection.EnvStatus.HasUnitySerial -or $detection.EnvStatus.HasUnityEmail -or $detection.EnvStatus.HasUnityPassword) {
        Write-WarningMsg 'Some Unity env vars are set but incomplete (need all of UNITY_SERIAL, UNITY_EMAIL, UNITY_PASSWORD).'
    }
    else {
        Write-Info 'No Unity environment variables detected.'
    }

    # 3. Search for .ulf files
    Write-Info 'Searching for .ulf license files...'
    $detection.UlfFilesFound = @(Find-UlfFiles)

    if ($detection.UlfFilesFound.Count -gt 0) {
        Write-Success "Found $($detection.UlfFilesFound.Count) .ulf file(s):"
        foreach ($ulf in $detection.UlfFilesFound) {
            Write-Host "      $ulf" -ForegroundColor Green
        }
    }
    else {
        Write-Info 'No .ulf files found in common locations.'
    }

    # 4. Check Docker availability
    Write-Info 'Checking Docker availability...'
    $detection.DockerAvailable = Test-DockerAvailable

    if ($detection.DockerAvailable) {
        Write-Success 'Docker is available.'

        # 5. Check if GameCI image is pulled
        Write-Info "Checking for GameCI image: $UnityImage"
        $detection.ImagePulled = Test-DockerImagePulled

        if ($detection.ImagePulled) {
            Write-Success 'GameCI image is already pulled.'
        }
        else {
            Write-Info 'GameCI image is not yet pulled (will be pulled if needed).'
        }
    }
    else {
        Write-WarningMsg 'Docker is NOT available. Some features (activation testing) will be skipped.'
    }

    return $detection
}

function Invoke-AutoAcquisition {
    param(
        [string]$SecretsDir,
        $Detection
    )

    Write-SectionHeader 'Auto-Acquisition'
    Write-Info 'Attempting to automatically acquire license credentials...'

    # ── Method 1: Environment variables ──────────────────────────────────────

    # Method 1a: UNITY_LICENSE env var (full .ulf content)
    try {
        if ($Detection.EnvStatus.HasUnityLicense) {
            Write-Info 'Trying Method 1a: UNITY_LICENSE environment variable...'
            $envUlfContent = $Detection.EnvStatus.LicenseContent

            if (Test-UlfContent $envUlfContent) {
                Write-VerboseInfo 'UNITY_LICENSE content validated as ULF.'
                $serial = Get-SerialFromUlf -UlfContent $envUlfContent
                $email = $(if ($Detection.EnvStatus.HasUnityEmail) { $Detection.EnvStatus.Email } else { '' })

                Write-Success 'Auto-acquired license from UNITY_LICENSE environment variable.'
                return @{
                    LicenseType      = 'personal'
                    UlfContent       = $envUlfContent
                    Email            = $email
                    Serial           = $serial
                    Password         = ''
                    ActivationTested = $false
                    ActivationPassed = $false
                    Source           = 'UNITY_LICENSE environment variable'
                }
            }
            else {
                Write-VerboseInfo 'UNITY_LICENSE content did not pass ULF validation.'
            }
        }
    }
    catch {
        Write-VerboseInfo "Method 1a failed: $_"
    }

    # Method 1b: Pro credentials from env vars (serial + email + password)
    try {
        if ($Detection.EnvStatus.HasUnitySerial -and $Detection.EnvStatus.HasUnityEmail -and $Detection.EnvStatus.HasUnityPassword) {
            Write-Info 'Trying Method 1b: Pro credentials from environment variables...'

            $serial = $Detection.EnvStatus.Serial
            $email = $Detection.EnvStatus.Email
            $password = $Detection.EnvStatus.Password

            Write-VerboseInfo "Serial format: $(Get-MaskedSerial $serial)"
            Write-VerboseInfo "Email: $(Get-MaskedEmail $email)"

            Write-Success 'Auto-acquired pro credentials from environment variables.'
            return @{
                LicenseType      = 'pro'
                UlfContent       = ''
                Email            = $email
                Serial           = $serial
                Password         = $password
                ActivationTested = $false
                ActivationPassed = $false
                Source           = 'Environment variables (UNITY_SERIAL, UNITY_EMAIL, UNITY_PASSWORD)'
            }
        }
    }
    catch {
        Write-VerboseInfo "Method 1b failed: $_"
    }

    # ── Method 2: Auto-detected .ulf files ───────────────────────────────────

    try {
        if ($Detection.UlfFilesFound.Count -gt 0) {
            Write-Info "Trying Method 2: Auto-detected .ulf files ($($Detection.UlfFilesFound.Count) found)..."

            foreach ($ulfPath in $Detection.UlfFilesFound) {
                Write-VerboseInfo "Reading ULF file: $ulfPath"
                try {
                    $content = [System.IO.File]::ReadAllText($ulfPath)
                    if (Test-UlfContent $content) {
                        Write-VerboseInfo 'ULF content validated.'
                        $serial = Get-SerialFromUlf -UlfContent $content

                        Write-Success "Auto-acquired license from .ulf file: $ulfPath"
                        return @{
                            LicenseType      = 'personal'
                            UlfContent       = $content
                            Email            = ''
                            Serial           = $serial
                            Password         = ''
                            ActivationTested = $false
                            ActivationPassed = $false
                            Source           = "Auto-detected .ulf file: $ulfPath"
                        }
                    }
                    else {
                        Write-VerboseInfo "ULF file did not pass validation: $ulfPath"
                    }
                }
                catch {
                    Write-VerboseInfo "Failed to read ULF file '$ulfPath': $_"
                }
            }
        }
    }
    catch {
        Write-VerboseInfo "Method 2 failed: $_"
    }

    # ── Method 3: Docker containers ──────────────────────────────────────────

    try {
        Write-Info 'Trying Method 3: Searching Docker containers for license files...'
        $dockerUlfs = @(Find-DockerUlfFiles)

        if ($dockerUlfs.Count -gt 0) {
            $firstResult = $dockerUlfs[0]
            $ulfContent = $firstResult.UlfContent
            $containerName = $firstResult.ContainerName
            $serial = Get-SerialFromUlf -UlfContent $ulfContent

            Write-Success "Auto-acquired license from Docker container: $containerName"
            return @{
                LicenseType      = 'personal'
                UlfContent       = $ulfContent
                Email            = ''
                Serial           = $serial
                Password         = ''
                ActivationTested = $false
                ActivationPassed = $false
                Source           = "Docker container: $containerName"
            }
        }
    }
    catch {
        Write-VerboseInfo "Method 3 failed: $_"
    }

    # ── Method 4: Search Docker image layers ────────────────────────────────

    try {
        if ($Detection.DockerAvailable -and $Detection.ImagePulled) {
            Write-Info 'Trying Method 4: Searching Docker image for license files...'

            $imageUlfPaths = @(
                '/root/.local/share/unity3d/Unity/Unity_lic.ulf'
                '/root/.config/unity3d/Unity/Unity_lic.ulf'
            )

            foreach ($imageUlfPath in $imageUlfPaths) {
                try {
                    $imageUlfContent = & docker run --rm "$UnityImage" cat $imageUlfPath 2>$null
                    if ($LASTEXITCODE -eq 0 -and $imageUlfContent) {
                        $joinedContent = $imageUlfContent -join "`n"
                        if (Test-UlfContent $joinedContent) {
                            $serial = Get-SerialFromUlf -UlfContent $joinedContent

                            Write-Success "Auto-acquired license from Docker image: $UnityImage"
                            return @{
                                LicenseType      = 'personal'
                                UlfContent       = $joinedContent
                                Email            = ''
                                Serial           = $serial
                                Password         = ''
                                ActivationTested = $false
                                ActivationPassed = $false
                                Source           = "Docker image: $UnityImage"
                            }
                        }
                    }
                }
                catch {
                    Write-VerboseInfo "Failed to check image path '$imageUlfPath': $_"
                }
            }

            Write-VerboseInfo 'No license files found in Docker image layers.'
        }
    }
    catch {
        Write-VerboseInfo "Method 4 failed: $_"
    }

    Write-Info 'No automatic acquisition method succeeded. Falling back to interactive flow.'
    return $null
}

# ── Personal license flow ───────────────────────────────────────────────────

function Invoke-PersonalLicenseFlow {
    param(
        [array]$UlfFilesFound,
        [bool]$DockerAvailable,
        [bool]$ImagePulled,
        [string]$SecretsDir
    )

    Write-SectionHeader 'Personal License Setup'

    Write-Host ''
    Write-Host '  Unity Personal licenses are activated inside Docker using your Unity' -ForegroundColor White
    Write-Host '  account credentials. Your email and password are used for online' -ForegroundColor White
    Write-Host '  activation each time a Docker container starts.' -ForegroundColor White
    Write-Host ''
    Write-Host '  Credentials are stored locally in .unity-secrets/credentials.env' -ForegroundColor DarkGray
    Write-Host '  (gitignored, chmod 600).' -ForegroundColor DarkGray
    Write-Host ''

    Write-SubHeader 'Unity Account Credentials'
    Write-Host ''

    $email = ''
    while (-not $email) {
        $email = (Read-Host '  Enter your Unity account email').Trim()
        if (-not $email) {
            Write-WarningMsg 'Email is required for Docker license activation.'
        }
    }

    $password = ''
    while (-not $password) {
        $password = Read-MaskedInput -Prompt 'Enter your Unity account password'
        if (-not $password) {
            Write-WarningMsg 'Password is required for Docker license activation.'
        }
    }

    # Optionally load a .ulf file as a fallback for offline environments
    $ulfContent = $null
    if ($UlfFilesFound.Count -gt 0) {
        Write-Host ''
        Write-Info "Found $($UlfFilesFound.Count) existing .ulf file(s). Loading as offline fallback."
        $selectedPath = $UlfFilesFound[0]
        $ulfContent = [System.IO.File]::ReadAllText($selectedPath)
        if (Test-UlfContent $ulfContent) {
            Write-Success "Loaded fallback .ulf from: $selectedPath"
        }
        else {
            Write-WarningMsg 'File does not appear to be a valid .ulf. Skipping fallback.'
            $ulfContent = $null
        }
    }

    return @{
        LicenseType      = 'personal'
        UlfContent       = $ulfContent
        Email            = $email
        Serial           = ''
        Password         = $password
        ActivationTested = $false
        ActivationPassed = $false
        Source           = 'Interactive setup'
    }
}

function Read-UlfFromFilePath {
    while ($true) {
        Write-Host ''
        $filePath = (Read-Host '  Enter the path to your Unity_lic.ulf file').Trim().Trim('"').Trim("'")

        if (-not $filePath) {
            Write-WarningMsg 'No path entered. Please try again.'
            continue
        }

        # Expand leading ~
        if ($filePath.StartsWith('~')) {
            $filePath = $HOME + $filePath.Substring(1)
        }

        if (-not (Test-Path $filePath)) {
            Write-WarningMsg "File not found: $filePath"
            Write-WarningMsg 'Please check the path and try again.'
            continue
        }

        $content = [System.IO.File]::ReadAllText($filePath)
        if (-not (Test-UlfContent $content)) {
            Write-WarningMsg 'The file does not appear to be a valid Unity license file.'
            Write-WarningMsg 'Expected XML content with <root> and <License tags.'
            $proceed = Read-YesNo 'Use this file anyway?'
            if (-not $proceed) { continue }
        }
        else {
            Write-Success "Valid license file loaded: $filePath"
        }

        return $content
    }
}

function Read-UlfFromPaste {
    Write-Host ''
    Write-Host '  Paste your license file contents below.' -ForegroundColor White
    Write-Host '  When done pasting, press Enter on an empty line to finish.' -ForegroundColor DarkGray
    Write-Host ''

    $lines = @()
    while ($true) {
        $line = Read-Host
        if ([string]::IsNullOrEmpty($line)) {
            if ($lines.Count -gt 0) { break }
            continue
        }
        $lines += $line
    }

    $content = $lines -join "`n"

    if (-not (Test-UlfContent $content)) {
        Write-WarningMsg 'The pasted content does not appear to be a valid Unity license file.'
        Write-WarningMsg 'Expected XML content with <root> and <License tags.'
        $proceed = Read-YesNo 'Use this content anyway?'
        if (-not $proceed) {
            return $null
        }
    }
    else {
        Write-Success 'License content received and validated.'
    }

    return $content
}

# ── Pro license flow ────────────────────────────────────────────────────────

function Invoke-ProLicenseFlow {
    param(
        [bool]$DockerAvailable,
        [bool]$ImagePulled,
        [string]$SecretsDir
    )

    Write-SectionHeader 'Pro / Plus / Enterprise License Setup'

    Write-Host ''
    Write-Host '  You will need your serial key from https://id.unity.com (Subscriptions page).' -ForegroundColor White
    Write-Host '  Serial format: XX-XXXX-XXXX-XXXX-XXXX-XXXX' -ForegroundColor DarkGray
    Write-Host ''

    $serialPattern = '^[A-Z0-9]{2}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}$'

    # Collect serial
    $serial = ''
    while ($true) {
        $serial = (Read-Host '  Enter your Unity serial key').Trim().ToUpper()

        if ($serial -match $serialPattern) {
            Write-Success "Serial format validated: $(Get-MaskedSerial $serial)"
            break
        }

        Write-WarningMsg 'Invalid serial format.'
        Write-WarningMsg 'Expected format: XX-XXXX-XXXX-XXXX-XXXX-XXXX (letters and numbers only)'
    }

    # Collect email
    Write-Host ''
    $email = ''
    while ($true) {
        $email = (Read-Host '  Enter your Unity account email').Trim()

        if ($email -and $email -match '@') {
            break
        }

        Write-WarningMsg 'Please enter a valid email address.'
    }

    # Collect password (masked)
    Write-Host ''
    $password = Read-MaskedInput -Prompt 'Enter your Unity account password'

    if (-not $password) {
        Write-ErrorMsg 'Password is required for Pro license activation.'
        exit 1
    }

    # Preview what will be saved
    Write-SubHeader 'Credentials Summary (before saving)'
    Write-Host ''
    Write-Host "    Serial:   $(Get-MaskedSerial $serial)" -ForegroundColor White
    Write-Host "    Email:    $(Get-MaskedEmail $email)" -ForegroundColor White
    Write-Host "    Password: $(Get-MaskedPassword $password)" -ForegroundColor White
    Write-Host ''

    # Test activation if Docker is available
    $activationTested = $false
    $activationPassed = $false

    if ($DockerAvailable) {
        $doTest = Read-YesNo 'Test license activation before saving? (recommended)' -DefaultYes $true

        if ($doTest) {
            # Ensure image is pulled
            if (-not $ImagePulled) {
                Write-Info 'Docker image needs to be pulled first.'
                $pulled = Invoke-DockerPull
                if (-not $pulled) {
                    Write-WarningMsg 'Could not pull Docker image. Skipping activation test.'
                }
                else {
                    $ImagePulled = $true
                }
            }

            if ($ImagePulled) {
                $activationTested = $true
                $activationPassed = Test-ProActivation -Serial $serial -Email $email -Password $password

                if (-not $activationPassed) {
                    Write-Host ''
                    Write-WarningMsg 'Activation test failed. Possible causes:'
                    Write-Host '      - Invalid serial key' -ForegroundColor Yellow
                    Write-Host '      - Wrong email or password' -ForegroundColor Yellow
                    Write-Host '      - Serial already activated on too many machines' -ForegroundColor Yellow
                    Write-Host '      - Network connectivity issues' -ForegroundColor Yellow
                    Write-Host ''

                    $saveAnyway = Read-YesNo 'Save credentials anyway?'
                    if (-not $saveAnyway) {
                        Write-ErrorMsg 'Setup cancelled. Please verify your credentials and try again.'
                        exit 1
                    }
                }
            }
        }
    }
    else {
        Write-WarningMsg 'Docker not available. Cannot test activation before saving.'
        Write-Info 'Credentials will be saved without testing.'
    }

    return @{
        LicenseType      = 'pro'
        UlfContent       = ''
        Email            = $email
        Serial           = $serial
        Password         = $password
        ActivationTested = $activationTested
        ActivationPassed = $activationPassed
        Source           = 'Interactive setup'
    }
}

# ═════════════════════════════════════════════════════════════════════════════
# Main Script
# ═════════════════════════════════════════════════════════════════════════════

if ($Help) {
    Show-Help
    exit 0
}

# ── Pre-flight checks ───────────────────────────────────────────────────────

Write-VerboseInfo 'Running pre-flight checks...'

$repoRoot = Get-RepoRoot
if (-not $repoRoot) {
    Write-ErrorMsg 'Could not find repository root.'
    Write-ErrorMsg 'Please run this script from within the com.wallstop-studios.unity-helpers repository.'
    Write-ErrorMsg 'The script looks for a package.json containing "com.wallstop-studios.unity-helpers".'
    exit 1
}

Write-VerboseInfo "Repository root: $repoRoot"

$secretsDir = Join-Path $repoRoot '.unity-secrets'

# CRITICAL: Verify .gitignore contains .unity-secrets/ BEFORE doing anything
if (-not (Test-GitignoreContainsSecrets $repoRoot)) {
    Write-ErrorMsg 'SAFETY CHECK FAILED: .unity-secrets/ is NOT in .gitignore!'
    Write-ErrorMsg ''
    Write-ErrorMsg 'This script refuses to write secrets without gitignore protection.'
    Write-ErrorMsg 'Please add the following line to your .gitignore:'
    Write-ErrorMsg ''
    Write-ErrorMsg '  .unity-secrets/'
    Write-ErrorMsg ''
    exit 1
}

Write-VerboseInfo '.gitignore contains .unity-secrets/ entry.'

# ── Handle -Check flag ──────────────────────────────────────────────────────

if ($Check) {
    $config = Get-ExistingConfig $secretsDir
    $envStatus = Get-EnvironmentStatus

    # Environment variables take priority
    if ($envStatus.HasUnityLicense) {
        Write-Success 'Unity license is configured via UNITY_LICENSE environment variable.'
        exit 0
    }

    if ($envStatus.HasUnitySerial -and $envStatus.HasUnityEmail -and $envStatus.HasUnityPassword) {
        Write-Success 'Unity license is configured via UNITY_SERIAL environment variables.'
        exit 0
    }

    if (Test-ConfigIsComplete $config) {
        Write-CurrentConfig $config
        Write-Success 'Unity license is configured.'
        exit 0
    }

    Write-WarningMsg 'Unity license is NOT configured.'
    Write-Host ''
    Write-Host '  Run the following to set up your license:'
    Write-Host '    pwsh -NoProfile -File scripts/unity/setup-license.ps1'
    Write-Host ''
    exit 1
}

# ── Handle -Reset flag ──────────────────────────────────────────────────────

if ($Reset) {
    if (Test-Path $secretsDir) {
        Write-WarningMsg "Removing existing configuration at $secretsDir"
        Remove-Item -Path $secretsDir -Recurse -Force
        Write-Success 'Existing configuration removed.'
    }
    else {
        Write-Info 'No existing configuration to remove.'
    }
}

# ── Auto-detection phase ────────────────────────────────────────────────────

$detection = Invoke-AutoDetection -SecretsDir $secretsDir

# If environment variables are fully configured, tell the user
if ($detection.EnvConfigured) {
    Write-Host ''
    Write-Info 'Unity credentials are already configured via environment variables.'
    Write-Info 'Environment variables take priority over .unity-secrets/ files at runtime.'

    $continueSetup = Read-YesNo 'Set up .unity-secrets/ anyway (for persistence across sessions)?'
    if (-not $continueSetup) {
        Write-Success 'Using environment variable configuration. No changes made.'
        exit 0
    }
}

# If existing file config is complete, ask to reconfigure
if ($detection.ConfigComplete) {
    Write-CurrentConfig $detection.ExistingConfig

    $reconfigure = Read-YesNo 'License is already configured. Reconfigure?'
    if (-not $reconfigure) {
        Write-Success 'Keeping existing configuration.'
        exit 0
    }

    Write-Info 'Reconfiguring license...'
}

# ── Auto-acquisition attempt ────────────────────────────────────────────────

$result = $null

$autoResult = Invoke-AutoAcquisition -SecretsDir $secretsDir -Detection $detection

if ($autoResult) {
    Write-SectionHeader 'Auto-Acquisition Successful'
    Write-Host ''
    Write-Host "  License type:  $($autoResult.LicenseType)" -ForegroundColor White
    Write-Host "  Source:        $($autoResult.Source)" -ForegroundColor White
    if ($autoResult.Email) {
        Write-Host "  Email:         $(Get-MaskedEmail $autoResult.Email)" -ForegroundColor White
    }
    if ($autoResult.Serial) {
        Write-Host "  Serial:        $(Get-MaskedSerial $autoResult.Serial)" -ForegroundColor White
    }
    Write-Host ''

    $useAutoResult = Read-YesNo 'Use these automatically discovered credentials?' -DefaultYes $true
    if ($useAutoResult) {
        $result = $autoResult
    }
}

if (-not $result) {
    # ── License type selection ──────────────────────────────────────────────

    Write-SectionHeader 'License Type'

    Write-Host ''
    Write-Host '  Which license type do you have?' -ForegroundColor White
    Write-Host ''
    Write-Host '    [1] Personal (Free)' -ForegroundColor Green
    Write-Host '        Requires your Unity account email and password.' -ForegroundColor DarkGray
    Write-Host '        This is the most common option.' -ForegroundColor DarkGray
    Write-Host ''
    Write-Host '    [2] Pro / Plus / Enterprise (Serial Key)' -ForegroundColor Cyan
    Write-Host '        Requires serial key, email, and password.' -ForegroundColor DarkGray
    Write-Host '        Activation is tested directly via Docker.' -ForegroundColor DarkGray
    Write-Host ''

    $licenseChoice = Read-HostChoice -Prompt 'Enter choice' -ValidChoices @('1', '2') -Default '1'

    if ($licenseChoice -eq '1') {
        $result = Invoke-PersonalLicenseFlow `
            -UlfFilesFound $detection.UlfFilesFound `
            -DockerAvailable $detection.DockerAvailable `
            -ImagePulled $detection.ImagePulled `
            -SecretsDir $secretsDir
    }
    else {
        $result = Invoke-ProLicenseFlow `
            -DockerAvailable $detection.DockerAvailable `
            -ImagePulled $detection.ImagePulled `
            -SecretsDir $secretsDir
    }
}

if (-not $result) {
    Write-ErrorMsg 'No license configuration was created. Setup cancelled.'
    exit 1
}

# ── Preview what will be written ─────────────────────────────────────────────

Write-SectionHeader 'Configuration Preview'

Write-Host ''
Write-Host '  The following files will be written:' -ForegroundColor White
Write-Host ''
Write-Host "    .unity-secrets/credentials.env" -ForegroundColor Cyan
Write-Host "      UNITY_LICENSE_TYPE=$($result.LicenseType)"

if ($result.Email) {
    Write-Host "      UNITY_EMAIL=$(Get-MaskedEmail $result.Email)"
}

if ($result.Password) {
    Write-Host "      UNITY_PASSWORD=$(Get-MaskedPassword $result.Password)"
}

if ($result.LicenseType -eq 'pro' -and $result.Serial) {
    Write-Host "      UNITY_SERIAL=$(Get-MaskedSerial $result.Serial)"
}

if ($result.LicenseType -eq 'personal' -and $result.UlfContent) {
    $contentLength = $result.UlfContent.Length
    Write-Host ''
    Write-Host "    .unity-secrets/license.ulf ($contentLength characters)" -ForegroundColor Cyan
}

Write-Host ''

$doWrite = Read-YesNo 'Write these files?' -DefaultYes $true
if (-not $doWrite) {
    Write-ErrorMsg 'Setup cancelled. No files were written.'
    exit 1
}

# ── Write configuration ─────────────────────────────────────────────────────

Write-Info 'Writing configuration files...'

Write-SecretsFiles `
    -SecretsDir $secretsDir `
    -LicenseType $result.LicenseType `
    -Email $result.Email `
    -Serial $result.Serial `
    -Password $result.Password `
    -UlfContent $result.UlfContent

Write-Success 'Configuration files written.'

# ── Validation ───────────────────────────────────────────────────────────────

Write-Info 'Validating written configuration...'

$validationErrors = @(Test-WrittenConfig -SecretsDir $secretsDir -RepoRoot $repoRoot -LicenseType $result.LicenseType)

if ($validationErrors.Count -gt 0) {
    Write-ErrorMsg 'Validation failed:'
    foreach ($err in $validationErrors) {
        Write-ErrorMsg "  - $err"
    }
    exit 1
}

Write-Success 'All validation checks passed.'

# Confirm git ignore with double-check
$gitIgnored = Test-GitIgnoresSecrets $repoRoot

# ── Optional verification ────────────────────────────────────────────────────

# Skip verification prompt if Pro activation was already tested
$skipVerification = ($result.LicenseType -eq 'pro' -and $result.ActivationTested)

if (-not $skipVerification) {
    Invoke-OptionalVerification `
        -LicenseType $result.LicenseType `
        -SecretsDir $secretsDir `
        -DockerAvailable $detection.DockerAvailable
}

# ── Final summary ────────────────────────────────────────────────────────────

Write-FinalSummary `
    -LicenseType $result.LicenseType `
    -Email $result.Email `
    -Serial $result.Serial `
    -SecretsDir $secretsDir `
    -GitIgnored $gitIgnored

exit 0
