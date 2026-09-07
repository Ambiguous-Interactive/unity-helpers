#!/usr/bin/env pwsh
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$sourcePath = Join-Path $PSScriptRoot '../unity/run-ci-tests.ps1'
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($sourcePath, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Unity runner must parse.' }
foreach ($name in @('Assert-UnityRunMode', 'Invoke-UnityPackageExport')) {
    $definition = $ast.Find({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    if ($null -eq $definition) { throw "Missing production function $name." }
    Invoke-Expression $definition.Extent.Text
}

function Assert-Rejected {
    param([scriptblock]$Action, [string]$Expected)
    $message = ''
    try { & $Action } catch { $message = $_.Exception.Message }
    if ($message -notlike "*$Expected*") { throw "Expected '$Expected', got '$message'." }
}

$temporary = Join-Path ([IO.Path]::GetTempPath()) ('unity-export-' + [guid]::NewGuid().ToString('N'))
$ProjectPath = Join-Path $temporary 'staged project'
$ExportPackagePath = Join-Path $temporary 'output folder/release.unitypackage'
$UnityVersion = '6000.3.0f1'
$checks = 0
try {
    foreach ($relative in @('Assets/Editor', 'Assets/WallstopStudios/UnityHelpers', 'ProjectSettings', 'Packages')) {
        New-Item -ItemType Directory -Force -Path (Join-Path $ProjectPath $relative) | Out-Null
    }
    Set-Content -LiteralPath (Join-Path $ProjectPath 'Assets/Editor/UnityHelpersPackageExporter.cs') -Value 'exporter'
    Set-Content -LiteralPath (Join-Path $ProjectPath 'Assets/WallstopStudios/UnityHelpers/package.json') -Value '{}'
    $manifestPath = Join-Path $ProjectPath 'Packages/manifest.json'
    Set-Content -LiteralPath $manifestPath -Value '{"dependencies":{"com.unity.test-framework":"1.1.33"}}'
    Set-Content -LiteralPath (Join-Path $ProjectPath 'ProjectSettings/ProjectVersion.txt') -Value "m_EditorVersion: $UnityVersion"
    $valid = @{ Mode = 'export'; Project = $ProjectPath; ExportPath = $ExportPackagePath; Version = $UnityVersion }
    Assert-UnityRunMode @valid
    foreach ($mode in @('editmode', 'playmode', 'standalone')) {
        Assert-UnityRunMode -Mode $mode -Assemblies 'Package.Tests'
        Assert-Rejected { Assert-UnityRunMode -Mode $mode } '-AssemblyNames'
        Assert-Rejected { Assert-UnityRunMode -Mode $mode -Assemblies 'Package.Tests' -ExportPath $ExportPackagePath } '-TestMode export'
        $checks += 3
    }
    foreach ($invalid in @(
        @{ Project = ''; Expected = 'already staged' },
        @{ ExportPath = ''; Expected = 'already staged' },
        @{ ExportPath = (Join-Path $temporary 'wrong.txt'); Expected = '.unitypackage file' },
        @{ Version = '2021.3.0f1'; Expected = 'must target Unity' },
        @{ Assemblies = 'Fake.Tests'; Expected = 'test selection' },
        @{ HasTestOptions = $true; Expected = 'test selection' }
    )) {
        $arguments = $valid.Clone()
        foreach ($key in $invalid.Keys) { if ($key -ne 'Expected') { $arguments[$key] = $invalid[$key] } }
        Assert-Rejected { Assert-UnityRunMode @arguments } $invalid.Expected
        $checks++
    }
    Set-Content -LiteralPath $manifestPath -Value '{"dependencies":{"com.wallstop-studios.unity-helpers":"file:../.."}}'
    Assert-Rejected { Assert-UnityRunMode @valid } 'without a UPM package copy'
    Set-Content -LiteralPath $manifestPath -Value '{"dependencies":{"com.unity.test-framework":"1.1.33"}}'
    Remove-Item -LiteralPath (Join-Path $ProjectPath 'Assets/Editor/UnityHelpersPackageExporter.cs')
    Assert-Rejected { Assert-UnityRunMode @valid } 'not staged'
    $checks += 2

    function Invoke-UnityEditor {
        param($EditorPath, $Arguments, $Label, $LogPath, $TimeoutSeconds, $StallSeconds)
        if ($TimeoutSeconds -ne 7200 -or $StallSeconds -ne 900) { throw 'Export lost its watchdog limits.' }
        foreach ($required in @('-batchmode', '-nographics', '-quit', '-releaseCodeOptimization', '-executeMethod')) {
            if ($required -notin $Arguments) { throw "Missing export argument $required." }
        }
        if ('-runTests' -in $Arguments -or '-assemblyNames' -in $Arguments) { throw 'Export invoked tests.' }
        if ($Arguments[[Array]::IndexOf($Arguments, '-projectPath') + 1] -cne $ProjectPath -or
            $Arguments[[Array]::IndexOf($Arguments, '-exportOutput') + 1] -cne $ExportPackagePath) {
            throw 'Export split a path containing spaces.'
        }
        if (Test-Path -LiteralPath $ExportPackagePath) { throw 'Stale export survived before launch.' }
        if (Test-Path -LiteralPath "$ExportPackagePath.sha256") { throw 'Stale digest survived before launch.' }
        if ($script:Outcome -in @('success', 'failure-with-output')) {
            [IO.File]::WriteAllText($ExportPackagePath, 'fresh package payload')
        } elseif ($script:Outcome -eq 'empty') {
            [IO.File]::WriteAllText($ExportPackagePath, '')
        }
        if ($script:Outcome -like 'failure*') { return 1 }
        return 0
    }
    $logPath = Join-Path $temporary 'unity.log'
    foreach ($script:Outcome in @('success', 'missing', 'empty', 'failure-with-output', 'failure')) {
        New-Item -ItemType Directory -Force -Path (Split-Path $ExportPackagePath -Parent) | Out-Null
        Set-Content -LiteralPath $ExportPackagePath -Value 'stale package'
        Set-Content -LiteralPath "$ExportPackagePath.sha256" -Value 'stale hash'
        $action = { Invoke-UnityPackageExport -EditorPath 'fake editor' -Project $ProjectPath -OutputPath $ExportPackagePath -LogPath $logPath }
        if ($script:Outcome -eq 'success') {
            & $action
            $expected = (Get-FileHash -LiteralPath $ExportPackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
            $digest = (Get-Content -LiteralPath "$ExportPackagePath.sha256" -Raw).Trim()
            if ($digest -cne "$expected  release.unitypackage") { throw 'Digest does not describe this export.' }
        } else {
            $expected = if ($script:Outcome -like 'failure*') { 'exit code 1' } else { 'fresh non-empty' }
            Assert-Rejected $action $expected
            if (Test-Path -LiteralPath "$ExportPackagePath.sha256") { throw 'Failed export published a digest.' }
        }
        $checks++
    }

    $initialization = @($ast.EndBlock.Statements | Where-Object {
        $_ -is [System.Management.Automation.Language.IfStatementAst] -and
        $_.Extent.Text -match 'Initialize-EphemeralProject'
    })
    if ($initialization.Count -ne 1) { throw 'Expected one guarded test-project initialization.' }
    function Initialize-EphemeralProject { throw 'Export replaced its staged release payload.' }
    function Clear-StaleUnityCompilationCache { throw 'Export changed test compilation caches.' }
    $TestMode = 'export'
    & ([scriptblock]::Create($initialization[0].Extent.Text))
    $checks++

    $lifecycle = @($ast.EndBlock.Statements | Where-Object {
        $_ -is [System.Management.Automation.Language.TryStatementAst] -and
        $_.Body.Extent.Text -match 'Invoke-UnityNativeStartupProbe'
    })
    if ($lifecycle.Count -ne 1) { throw 'Expected one shared native license lifecycle.' }
    function Invoke-UnityNativeStartupProbe { param($EditorPath, $LogPath) $script:Events.Add('probe') }
    function Invoke-UnityLicenseActivate { param($EditorPath, $Serial, $Email, $Password, $LogPath) $script:Events.Add('activate') }
    function Invoke-UnityLicenseReturn { param($EditorPath, $Email, $Password, $LogPath) $script:Events.Add('return') }
    function Invoke-UnityPackageExport {
        param($EditorPath, $Project, $OutputPath, $LogPath, $ExtraArguments)
        $script:Events.Add('export')
        if ($script:ExportFails) { throw 'injected export failure' }
    }
    function Write-UnityCompilationSourceInventoryMarker { throw 'Export changed the test compilation inventory.' }
    $UnityEditorPath = 'fake editor'
    $startupProbeLogPath = $logPath
    $activateLogPath = $logPath
    $returnLogPath = $logPath
    $hasLicenseCreds = $true
    $TestMode = 'export'
    $acceleratorArgs = @()
    foreach ($centralReturnOwnsLicense in @($false, $true)) {
        foreach ($script:ExportFails in @($false, $true)) {
            $script:Events = [Collections.Generic.List[string]]::new()
            $body = [scriptblock]::Create($lifecycle[0].Extent.Text)
            if ($script:ExportFails) { Assert-Rejected { & $body } 'injected export failure' } else { & $body }
            $expected = if ($centralReturnOwnsLicense) { 'probe,activate,export' } else { 'probe,activate,export,return' }
            if (($script:Events -join ',') -cne $expected) { throw "Export bypassed shared license ownership: $($script:Events -join ',')." }
            $checks++
        }
    }
} finally {
    Remove-Item -LiteralPath $temporary -Force -Recurse
}
Write-Host "[test-unity-package-export] $checks native export controls passed."
