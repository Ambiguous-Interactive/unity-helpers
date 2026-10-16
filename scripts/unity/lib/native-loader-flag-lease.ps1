#!/usr/bin/env pwsh

function Get-NativeLoaderDisabledValue {
    param([object]$Value, [string]$Kind)

    $original = ConvertTo-NativeGlobalFlagObservation -Value $Value -Kind $Kind
    if ($original.State -ne 'observed' -or -not $original.LoaderSnapsEnabled) { throw 'Comparison requires an observed loader-snaps flag' }
    $bits = [Convert]::ToUInt32($original.Hex, 16) -band 4294967293u
    if ($Kind -eq 'String') { return '0x' + $bits.ToString('X8') }
    return [BitConverter]::ToInt32([BitConverter]::GetBytes([uint32]$bits), 0)
}

function Get-NativeLoaderFlagDigest {
    param([byte[]]$Bytes)

    $hash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash($Bytes)).Replace('-', '') } finally { $hash.Dispose() }
}

function New-NativeLoaderFlagLease {
    param([Microsoft.Win32.RegistryKey]$Key)

    $lease = [ordered]@{
        SchemaVersion = 1
        Machine = [Environment]::MachineName
        RegistryKey = $Key.Name
        RegistryView = $Key.View.ToString()
        OriginalKind = $Key.GetValueKind('GlobalFlag').ToString()
        OriginalValue = $Key.GetValue('GlobalFlag', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        DisabledValue = $null
        State = 'prepared'
    }
    $lease.DisabledValue = Get-NativeLoaderDisabledValue -Value $lease.OriginalValue -Kind $lease.OriginalKind
    return $lease
}

function Get-NativeLoaderFlagLeaseValues {
    param([object]$Lease, [Microsoft.Win32.RegistryKey]$Key)

    if ($Lease.SchemaVersion -isnot [int] -and $Lease.SchemaVersion -isnot [long]) { throw 'Invalid flag lease version' }
    if ($Lease.SchemaVersion -ne 1 -or $Lease.Machine -cne [Environment]::MachineName -or $Lease.RegistryKey -cne $Key.Name -or $Lease.RegistryView -cne $Key.View.ToString()) { throw 'Flag lease identity mismatch' }
    $originalValue = $Lease.OriginalValue
    if ($Lease.OriginalKind -eq 'DWord') {
        if (($originalValue -isnot [int] -and $originalValue -isnot [long]) -or $originalValue -lt [int]::MinValue -or $originalValue -gt [int]::MaxValue) { throw 'Invalid original DWORD' }
        $originalValue = [int]$originalValue
    }
    $disabledValue = Get-NativeLoaderDisabledValue -Value $originalValue -Kind $Lease.OriginalKind
    if ($Lease.DisabledValue -cne $disabledValue) { throw 'Invalid disabled flag value' }
    return @{ Original = $originalValue; Disabled = $disabledValue; Kind = [Microsoft.Win32.RegistryValueKind]::$($Lease.OriginalKind) }
}

function Test-NativeLoaderFlagValue {
    param([Microsoft.Win32.RegistryKey]$Key, [Microsoft.Win32.RegistryValueKind]$Kind, [object]$Value)

    return $Key.GetValueKind('GlobalFlag') -eq $Kind -and $Key.GetValue('GlobalFlag', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) -ceq $Value
}

function Save-NativeLoaderFlagLease {
    param([object]$Lease, [string]$Path)

    $payload = [Text.Encoding]::UTF8.GetBytes(($Lease | ConvertTo-Json -Depth 5 -Compress))
    $digest = Get-NativeLoaderFlagDigest -Bytes $payload
    $envelope = [ordered]@{ Version = 1; Sha256 = $digest; Data = [Convert]::ToBase64String($payload) }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($envelope | ConvertTo-Json))
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
}

function Read-NativeLoaderFlagLease {
    param([string]$Path)

    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        if ($stream.Length -gt 65536) { throw 'Flag lease exceeds read bound' }
        $buffer = [byte[]]::new(65537)
        $length = 0
        while ($length -lt $buffer.Length) {
            $read = $stream.Read($buffer, $length, $buffer.Length - $length)
            if ($read -eq 0) { break }
            $length += $read
        }
        if ($length -gt 65536) { throw 'Flag lease exceeds read bound' }
        $envelope = [Text.Encoding]::UTF8.GetString($buffer, 0, $length) | ConvertFrom-Json -Depth 5
    } finally { $stream.Dispose() }
    if (($envelope.Version -isnot [int] -and $envelope.Version -isnot [long]) -or $envelope.Version -ne 1 -or $envelope.Data -isnot [string] -or $envelope.Data.Length -gt 16384 -or $envelope.Sha256 -isnot [string] -or $envelope.Sha256 -cnotmatch '^[0-9A-F]{64}$') { throw 'Invalid flag lease envelope' }
    $payload = [Convert]::FromBase64String($envelope.Data)
    $digest = Get-NativeLoaderFlagDigest -Bytes $payload
    if ($digest -cne $envelope.Sha256) { throw 'Flag lease checksum mismatch' }
    return [Text.Encoding]::UTF8.GetString($payload) | ConvertFrom-Json -Depth 5
}

function Disable-NativeLoaderFlag {
    param([object]$Lease, [Microsoft.Win32.RegistryKey]$Key, [Parameter(Mandatory = $true)][string]$LeasePath)

    $values = Get-NativeLoaderFlagLeaseValues -Lease $Lease -Key $Key
    if (-not (Test-NativeLoaderFlagValue -Key $Key -Kind $values.Kind -Value $values.Original)) { throw 'Original flag changed before comparison' }
    $Lease.State = 'clear-requested'
    try { Save-NativeLoaderFlagLease -Lease $Lease -Path $LeasePath } catch { $Lease.State = 'prepared'; throw }
    $Key.SetValue('GlobalFlag', $values.Disabled, $values.Kind)
    $Key.Flush()
    if (-not (Test-NativeLoaderFlagValue -Key $Key -Kind $values.Kind -Value $values.Disabled)) { throw 'Disabled flag verification failed' }
    $Lease.State = 'disabled'
}

function Restore-NativeLoaderFlag {
    param([object]$Lease, [Microsoft.Win32.RegistryKey]$Key)

    $values = Get-NativeLoaderFlagLeaseValues -Lease $Lease -Key $Key
    if (Test-NativeLoaderFlagValue -Key $Key -Kind $values.Kind -Value $values.Original) { $Lease.State = 'restored'; return }
    if (-not (Test-NativeLoaderFlagValue -Key $Key -Kind $values.Kind -Value $values.Disabled)) {
        $Lease.State = 'conflict'
        throw 'Flag changed externally; restoration refused'
    }
    $Key.SetValue('GlobalFlag', $values.Original, $values.Kind)
    $Key.Flush()
    if (-not (Test-NativeLoaderFlagValue -Key $Key -Kind $values.Kind -Value $values.Original)) { throw 'Original flag restoration verification failed' }
    $Lease.State = 'restored'
}

function Complete-NativeLoaderFlagLease {
    param([object]$Lease, [Microsoft.Win32.RegistryKey]$Key)

    $values = Get-NativeLoaderFlagLeaseValues -Lease $Lease -Key $Key
    if ($Lease.State -in @('clear-requested', 'disabled')) { Restore-NativeLoaderFlag -Lease $Lease -Key $Key; return }
    if ($Lease.State -in @('prepared', 'restored') -and (Test-NativeLoaderFlagValue -Key $Key -Kind $values.Kind -Value $values.Original)) { return }
    $Lease.State = 'conflict'
    throw 'Automatic cleanup has no active flag write to restore; external state retained'
}
