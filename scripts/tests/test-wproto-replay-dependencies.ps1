#!/usr/bin/env pwsh
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (Get-Command Assert-FrozenPlayerRegularPath -ErrorAction SilentlyContinue) {
    throw 'Replay dependency controls require a fresh process without runner functions.'
}
. (Join-Path $PSScriptRoot '../unity/lib/wproto-player-provenance.ps1')
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('wproto replay dependencies ' + [Guid]::NewGuid().ToString('N'))
$checks = 0
function Assert-Control {
    param([bool]$Passed, [string]$Name)
    if (-not $Passed) { throw "Control failed: $Name" }
    $script:checks++
}
function Assert-Rejected {
    param([scriptblock]$Action, [string]$Expected)
    $failure = $null
    try { & $Action | Out-Null } catch { $failure = $_.ToString() }
    Assert-Control ($null -ne $failure -and $failure.Contains($Expected)) "$Expected (observed: $failure)"
}
try {
    foreach ($name in @('Assets', 'Packages', 'ProjectSettings')) {
        $directory = Join-Path $temporary $name
        $null = New-Item -ItemType Directory -Force -Path $directory
        [IO.File]::WriteAllText((Join-Path $directory 'input.txt'), $name)
    }
    $inventory = @(Get-WProtoProjectInputs -ProjectPath $temporary)
    Assert-Control ($inventory.Count -eq 3) 'nonempty complete project inventory'
    foreach ($entry in $inventory) {
        $path = Join-Path $temporary $entry.Path
        Assert-Control ($entry.Kind -ceq 'file' -and $entry.Length -eq [IO.File]::ReadAllBytes($path).Length -and
            $entry.Sha256 -ceq (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()) 'exact project bytes'
    }
    $root = Join-Path $temporary 'Assets'
    $hash = (Get-FileHash -LiteralPath (Join-Path $root 'input.txt') -Algorithm SHA256).Hash.ToLowerInvariant()
    $declared = @([pscustomobject]@{ Path = 'input.txt'; Sha256 = $hash })
    $files = @(Get-FrozenPlayerFileInventory -Root $root -DeclaredFiles $declared)
    Assert-Control ($files.Count -eq 1 -and $files[0].Sha256 -ceq $hash) 'declared input inventory'
    [IO.File]::AppendAllText((Join-Path $root 'input.txt'), 'changed')
    Assert-Rejected { Get-FrozenPlayerFileInventory -Root $root -DeclaredFiles $declared } 'does not match'
    Assert-Rejected { Assert-FrozenPlayerRegularPath -Path $root } 'wrong file/directory kind'
    Assert-Rejected { Assert-FrozenPlayerRegularPath -Path (Join-Path $root 'missing.txt') } 'does not exist'

    foreach ($backend in @('Mono2x', 'IL2CPP')) {
        foreach ($version in @('2021.3.45f1', '6000.6.0f1')) {
            $restored = "PlayerSettings:`n  runInBackground: 0`n  fullscreenMode: 1`n  resizableWindow: 0`n  m_ShowUnitySplashScreen: 1`n  m_ShowUnitySplashLogo: 1`n"
            $prepared = $restored.Replace('runInBackground: 0', 'runInBackground: 1').Replace('fullscreenMode: 1', 'fullscreenMode: 3').Replace(
                'resizableWindow: 0', 'resizableWindow: 1').Replace('m_ShowUnitySplashScreen: 1', 'm_ShowUnitySplashScreen: 0').Replace(
                'm_ShowUnitySplashLogo: 1', 'm_ShowUnitySplashLogo: 0')
            if ($version -like '2021.*') {
                $restored += "  aotOptions: none`n"
                $prepared += "  aotOptions: nimt-trampolines=1024`n"
            } else {
                $restored += "  managedCodeVariant:`n    Standalone: 3`n"
                $variant = if ($backend -ceq 'IL2CPP') { '1' } else { '3' }
                $prepared += "  managedCodeVariant:`n    Standalone: $variant`n"
            }
            $restoredPath = Join-Path $temporary 'restored.asset'
            $preparedPath = Join-Path $temporary 'prepared.asset'
            [IO.File]::WriteAllText($restoredPath, $restored)
            [IO.File]::WriteAllText($preparedPath, $prepared)
            $configuredEntry = [pscustomobject]@{ Path = 'ProjectSettings/ProjectSettings.asset'; Kind = 'file'
                Length = (Get-Item -LiteralPath $restoredPath).Length
                Sha256 = (Get-FileHash -LiteralPath $restoredPath -Algorithm SHA256).Hash.ToLowerInvariant() }
            $preparedEntry = [pscustomobject]@{ Path = 'ProjectSettings/ProjectSettings.asset'; Kind = 'file'
                Length = (Get-Item -LiteralPath $preparedPath).Length
                Sha256 = (Get-FileHash -LiteralPath $preparedPath -Algorithm SHA256).Hash.ToLowerInvariant() }
            $parameters = @{ Configured = $configuredEntry; Prepared = $preparedEntry; Built = $configuredEntry
                RestoredPath = $restoredPath; PreparedPath = $preparedPath; UnityVersion = $version; Backend = $backend }
            Assert-WProtoPreparedPlayerSettings @parameters
            Assert-Control $true "fresh-process prepared settings $version/$backend"
            [IO.File]::AppendAllText($preparedPath, 'changed')
            Assert-Rejected { Assert-WProtoPreparedPlayerSettings @parameters } 'bytes differ'
            if ($version -ceq '6000.6.0f1') {
                foreach ($duplicate in @("`n  managedCodeVariant: {}`n", "`n    Standalone: Release`n")) {
                    [IO.File]::WriteAllText($restoredPath, $restored + $duplicate)
                    [IO.File]::WriteAllText($preparedPath, $prepared + $duplicate)
                    foreach ($entry in @(@{ Entry = $configuredEntry; Path = $restoredPath }, @{ Entry = $preparedEntry; Path = $preparedPath })) {
                        $entry.Entry.Length = (Get-Item -LiteralPath $entry.Path).Length
                        $entry.Entry.Sha256 = (Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash.ToLowerInvariant()
                    }
                    Assert-Rejected { Assert-WProtoPreparedPlayerSettings @parameters } 'variant'
                }
            }
        }
    }
    $link = Join-Path $temporary 'linked Assets'
    $linkKind = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { 'Junction' } else { 'SymbolicLink' }
    $null = New-Item -ItemType $linkKind -Path $link -Target $root
    Assert-Rejected { Assert-FrozenPlayerRegularPath -Path $link -Directory } 'reparse ancestor'
    Assert-Rejected { Get-FrozenPlayerFileInventory -Root $link } 'reparse ancestor'
    Assert-Control ($checks -eq 22) 'all dependency subjects executed'
} finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "[test-wproto-replay-dependencies] $checks fresh-process controls passed."
