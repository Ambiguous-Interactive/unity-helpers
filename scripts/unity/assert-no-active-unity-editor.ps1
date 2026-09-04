#!/usr/bin/env pwsh
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$activeEditors = @(Get-Process -ErrorAction Stop | Where-Object { $_.ProcessName -ieq 'Unity' })
if ($activeEditors.Count -ne 0) {
    $processIds = ($activeEditors | ForEach-Object { $_.Id }) -join ', '
    throw "A Unity editor is still running (process IDs: $processIds). Refusing to overlap test modes; the workflow cleanup must resolve the previous editor first."
}
