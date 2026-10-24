#!/usr/bin/env pwsh
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $root 'scripts/unity/lib/test-framework-evidence.ps1')
. (Join-Path $root 'scripts/unity/lib/project-workspace.ps1')
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('framework-workspace-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $temporary
$checks = 0
function Require {
    param([bool]$Valid, [string]$Name)
    if (-not $Valid) { throw "Framework workspace control failed: $Name" }
    $script:checks++
}
function Reject {
    param([scriptblock]$Action, [string]$Expected)
    $message = ''
    try { & $Action | Out-Null } catch { $message = $_.Exception.Message }
    Require ($message.Contains($Expected, [StringComparison]::Ordinal)) "Expected $Expected, got $message"
}
function New-Legacy {
    param([string]$Name)
    $project = Join-Path $temporary $Name
    $editor = Join-Path $project 'Assets/UhCiTestFrameworkEvidence/Editor'
    $null = New-Item -ItemType Directory -Path $editor -Force
    foreach ($extension in @('cs', 'asmdef')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "fixtures/unity-framework-collector-v0.$extension.txt") -Destination (Join-Path $editor "UhCiTestFrameworkEvidence.$extension")
        [IO.File]::WriteAllText((Join-Path $editor "UhCiTestFrameworkEvidence.$extension.meta"), "owned $extension GUID")
    }
    [IO.File]::WriteAllText("$editor.meta", 'owned Editor folder GUID')
    [IO.File]::WriteAllText((Join-Path $project 'Assets/UhCiTestFrameworkEvidence.meta'), 'owned parent folder GUID')
    return $project
}
try {
    $fingerprint = Get-UnityTestFrameworkProjectFingerprint -Root $root
    Require ($fingerprint -cmatch '^[0-9a-f]{16}$') 'bounded generation fingerprint'
    $inputRoot = Join-Path $temporary 'inputs'
    $null = New-Item -ItemType Directory -Path (Join-Path $inputRoot '.github'), (Join-Path $inputRoot 'scripts/unity') -Force
    $inputs = @('.github/unity-test-framework.json', 'scripts/unity/test-framework-evidence.cs.txt', 'scripts/unity/test-framework-evidence.asmdef.txt')
    foreach ($relative in $inputs) { Copy-Item -LiteralPath (Join-Path $root $relative) -Destination (Join-Path $inputRoot $relative) }
    Require ((Get-UnityTestFrameworkProjectFingerprint -Root $inputRoot) -ceq $fingerprint) 'same input bytes share a cache'
    foreach ($relative in $inputs) {
        $path = Join-Path $inputRoot $relative
        $before = [IO.File]::ReadAllBytes($path)
        [IO.File]::AppendAllText($path, 'changed contract')
        Require ((Get-UnityTestFrameworkProjectFingerprint -Root $inputRoot) -cne $fingerprint) "changed input isolates $relative"
        [IO.File]::WriteAllBytes($path, $before)
    }
    foreach ($mode in @('editmode', 'playmode', 'standalone')) {
        $arguments = @{ RepoRoot = $root; Version = '6000.6.0f1'; Mode = $mode; Scope = 'single-threaded'; ShareEditorProject = $true; PersistentRoot = $temporary }
        $old = Resolve-UnityProjectWorkspace @arguments
        $current = Resolve-UnityProjectWorkspace @arguments -GenerationFingerprint $fingerprint
        Require ($current.ProjectPath -cne $old.ProjectPath -and $current.LegacyProjectPath -ceq $old.ProjectPath) "legacy isolation $mode"
        Require ($current.CacheRoot -ceq $old.CacheRoot -and $current.Persistent) "shared download cache $mode"
        Require ((Resolve-UnityProjectWorkspace @arguments -GenerationFingerprint $fingerprint).ProjectPath -ceq $current.ProjectPath) "stable project $mode"
    }
    $explicit = Resolve-UnityProjectWorkspace -RepoRoot $root -Version '2021.3.45f1' -Mode editmode -ExplicitProjectPath $temporary -GenerationFingerprint $fingerprint
    Require ($explicit.ProjectPath -ceq $temporary -and -not $explicit.Persistent) 'explicit project override remains authoritative'
    Reject { Resolve-UnityProjectWorkspace -RepoRoot $root -Version '2021.3.45f1' -Mode editmode -GenerationFingerprint '../escape' } 'fingerprint must contain'
    Remove-LegacyUnityTestFrameworkCollector -Project (Join-Path $temporary 'absent') -ArtifactsPath (Join-Path $temporary 'no-evidence')
    Require (-not (Test-Path -LiteralPath (Join-Path $temporary 'no-evidence'))) 'absent legacy collector changes nothing'
    $project = New-Legacy -Name 'owned'
    $editor = Join-Path $project 'Assets/UhCiTestFrameworkEvidence/Editor'
    [IO.File]::WriteAllText((Join-Path $editor 'unrelated.asset'), 'preserved sibling')
    $original = @{}
    foreach ($path in @(Get-ChildItem -LiteralPath $editor -File | Where-Object { $_.Name -ne 'unrelated.asset' })) { $original[$path.Name] = [IO.File]::ReadAllText($path.FullName) }
    $artifacts = Join-Path $temporary 'retained'
    Remove-LegacyUnityTestFrameworkCollector -Project $project -ArtifactsPath $artifacts
    Require ([IO.File]::ReadAllText((Join-Path $editor 'unrelated.asset')) -ceq 'preserved sibling') 'unrelated assets survive'
    Require ((Test-Path -LiteralPath "$editor.meta") -and (Test-Path -LiteralPath (Join-Path $project 'Assets/UhCiTestFrameworkEvidence.meta'))) 'nonempty parent metadata survives'
    $receipt = Get-Content -LiteralPath (Get-ChildItem -LiteralPath $artifacts -Recurse -Filter receipt.json).FullName -Raw | ConvertFrom-Json
    Require ($receipt.Removed -and $receipt.Files.Count -eq 4) 'all four owned files have a retirement receipt'
    foreach ($name in $original.Keys) {
        Require (-not (Test-Path -LiteralPath (Join-Path $editor $name))) "owned file removed $name"
        $saved = @($receipt.Files | Where-Object { [IO.Path]::GetFileName($_.SavedPath) -ceq $name })
        Require ($saved.Count -eq 1 -and [IO.File]::ReadAllText($saved[0].SavedPath) -ceq $original[$name]) "original bytes retained $name"
    }
    $empty = New-Legacy -Name 'empty-owned'
    Remove-LegacyUnityTestFrameworkCollector -Project $empty -ArtifactsPath $artifacts
    Require ((Test-Path -LiteralPath (Join-Path $empty 'Assets/UhCiTestFrameworkEvidence/Editor')) -and (Test-Path -LiteralPath (Join-Path $empty 'Assets/UhCiTestFrameworkEvidence/Editor.meta'))) 'parent folders and metadata remain intact'
    $changed = New-Legacy -Name 'changed'
    $changedFile = Join-Path $changed 'Assets/UhCiTestFrameworkEvidence/Editor/UhCiTestFrameworkEvidence.cs'
    [IO.File]::AppendAllText($changedFile, 'user change')
    Reject { Remove-LegacyUnityTestFrameworkCollector -Project $changed -ArtifactsPath $artifacts } 'Unrecognized legacy'
    Require ([IO.File]::ReadAllText($changedFile).EndsWith('user change')) 'unknown source bytes preserved'
    $incomplete = New-Legacy -Name 'incomplete'
    Remove-Item -LiteralPath (Join-Path $incomplete 'Assets/UhCiTestFrameworkEvidence/Editor/UhCiTestFrameworkEvidence.asmdef')
    Reject { Remove-LegacyUnityTestFrameworkCollector -Project $incomplete -ArtifactsPath $artifacts } 'Incomplete legacy'
    Require (Test-Path -LiteralPath (Join-Path $incomplete 'Assets/UhCiTestFrameworkEvidence/Editor/UhCiTestFrameworkEvidence.cs')) 'partial collector preserved'
    $active = New-Legacy -Name 'active'
    function Get-Process { return [pscustomobject]@{ ProcessName = 'Unity'; Id = 123 } }
    try { Reject { Remove-LegacyUnityTestFrameworkCollector -Project $active -ArtifactsPath $artifacts } 'Unity editor is still running' }
    finally { Remove-Item Function:Get-Process }
    Require (Test-Path -LiteralPath (Join-Path $active 'Assets/UhCiTestFrameworkEvidence/Editor/UhCiTestFrameworkEvidence.cs')) 'active editor prevents retirement'
    $inside = New-Legacy -Name 'inside'
    Reject { Remove-LegacyUnityTestFrameworkCollector -Project $inside -ArtifactsPath (Join-Path $inside 'artifacts') } 'outside the legacy project'
    Require (Test-Path -LiteralPath (Join-Path $inside 'Assets/UhCiTestFrameworkEvidence/Editor/UhCiTestFrameworkEvidence.cs')) 'unsafe evidence destination preserves files'
    if (-not $IsWindows) {
        $linked = New-Legacy -Name 'linked'
        $target = Join-Path $linked 'Assets/UhCiTestFrameworkEvidence/Editor/UhCiTestFrameworkEvidence.cs'
        $outside = Join-Path $temporary 'outside.cs.txt'
        Move-Item -LiteralPath $target -Destination $outside
        $null = New-Item -ItemType SymbolicLink -Path $target -Target $outside
        Reject { Remove-LegacyUnityTestFrameworkCollector -Project $linked -ArtifactsPath $artifacts } 'Linked legacy'
        Require (Test-Path -LiteralPath $outside) 'linked source target survives'
    }
} finally { Remove-Item -LiteralPath $temporary -Force -Recurse }
Write-Host "Unity framework workspace: $checks controls passed."
