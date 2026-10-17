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
try {
    Invoke-NativeLoaderFlagRecovery -BaseKey $base -KeyPath 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\Unity.exe' -Lease $lease -Apply:$Apply
    if ($Apply) { Write-Host 'Original Unity.exe GlobalFlag value and type are confirmed restored.' }
    else { Write-Host 'Validated restoration plan only; pass -Apply to restore the recorded value.' }
} finally { $base.Dispose() }
