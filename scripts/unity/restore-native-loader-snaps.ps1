#!/usr/bin/env pwsh
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$LeasePath, [switch]$Apply)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/native-global-flags.ps1')
. (Join-Path $PSScriptRoot 'lib/native-loader-flag-lease.ps1')
if (-not $IsWindows) { throw 'Registry restoration requires Windows' }
$lease = Read-NativeLoaderFlagRecoveryLease -Path $LeasePath
$base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
$key = $null
try {
    $key = $base.OpenSubKey('SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\Unity.exe', $Apply.IsPresent)
    if ($null -eq $key) { throw 'Unity.exe image flags are missing' }
    $values = Get-NativeLoaderFlagLeaseValues -Lease $lease -Key $key
    if (-not $Apply) {
        if (-not (Test-NativeLoaderFlagValue -Key $key -Kind $values.Kind -Value $values.Original) -and ($lease.State -cne 'clear-requested' -or -not (Test-NativeLoaderFlagValue -Key $key -Kind $values.Kind -Value $values.Disabled))) { throw 'Current flag differs from the restoration plan' }
        Write-Host 'Validated restoration plan only; pass -Apply to restore the recorded value.'
        return
    }
    Complete-NativeLoaderFlagLease -Lease $lease -Key $key
    Write-Host 'Original Unity.exe GlobalFlag value and type are confirmed restored.'
} finally {
    if ($null -ne $key) { $key.Dispose() }
    $base.Dispose()
}
