#!/usr/bin/env pwsh

function ConvertTo-NativeGlobalFlagObservation {
    param([AllowNull()][object]$Value, [string]$Kind)

    $result = [ordered]@{ State = 'invalid'; Kind = $Kind; Hex = $null; LoaderSnapsEnabled = $null }
    $bits = 0u
    if ($Kind -eq 'DWord' -and $Value -is [int]) {
        $bits = [Convert]::ToUInt32($Value.ToString('X8'), 16)
    } elseif ($Kind -eq 'String' -and $Value -is [string] -and $Value.Length -le 10 -and $Value -cmatch '^(?:0[xX])?[0-9a-fA-F]{1,8}$') {
        $bits = [Convert]::ToUInt32(($Value -replace '^0[xX]', ''), 16)
    } else { return $result }
    $result.State = 'observed'
    $result.Hex = $bits.ToString('X8')
    $result.LoaderSnapsEnabled = ($bits -band 2u) -ne 0
    return $result
}

function Get-NativeGlobalFlagRegistryObservation {
    param([string]$Subject, [string]$KeyPath)

    $result = [ordered]@{
        Subject = $Subject
        Hive = 'LocalMachine'
        View = 'Registry64'
        KeyPath = $KeyPath
        ValueName = 'GlobalFlag'
        State = 'unavailable'
        Kind = $null
        Hex = $null
        LoaderSnapsEnabled = $null
        ErrorType = $null
    }
    $base = $null
    $key = $null
    try {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
        $key = $base.OpenSubKey($KeyPath, $false)
        if ($null -eq $key -or -not ($key.GetValueNames() -contains 'GlobalFlag')) {
            $result.State = 'absent'
        } else {
            $kind = $key.GetValueKind('GlobalFlag').ToString()
            $parsed = ConvertTo-NativeGlobalFlagObservation -Value $key.GetValue('GlobalFlag', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) -Kind $kind
            foreach ($name in $parsed.Keys) { $result[$name] = $parsed[$name] }
        }
    } catch { $result.ErrorType = $_.Exception.GetBaseException().GetType().Name } finally {
        if ($null -ne $key) { $key.Dispose() }
        if ($null -ne $base) { $base.Dispose() }
    }
    return $result
}

function Get-NativeGlobalFlagSnapshot {
    return [ordered]@{
        ObservedUtc = [DateTime]::UtcNow.ToString('o')
        Machine = [Environment]::MachineName
        Scope = 'registry-settings-only'
        Settings = @(
            (Get-NativeGlobalFlagRegistryObservation -Subject 'Unity.exe' -KeyPath 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\Unity.exe')
            (Get-NativeGlobalFlagRegistryObservation -Subject 'System' -KeyPath 'SYSTEM\CurrentControlSet\Control\Session Manager')
        )
    }
}
