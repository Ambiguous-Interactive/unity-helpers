#!/usr/bin/env pwsh
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'scripts/unity/run-ci-tests.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Unity runner must parse.' }
foreach ($name in @('Get-PackageManifestSource', 'Get-UnityTestProjectModules', 'Get-UnityTestFrameworkVersion', 'ConvertTo-UnityFileUriPath', 'New-ManifestJson')) {
    $definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    if ($null -eq $definition) { throw "Missing shared framework policy function: $name" }
    Invoke-Expression $definition.Extent.Text
}
$PackageName = 'com.wallstop-studios.unity-helpers'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('unity-framework-policy-' + [Guid]::NewGuid().ToString('N'))
$previousVersion = $env:UNITY_VERSION
$previousProject = $env:UNITY_TEST_PROJECT_DIR
$checks = 0
try {
    $fixture = Join-Path $temporary 'repository with spaces'
    New-Item -ItemType Directory -Path (Join-Path $fixture 'scripts/unity'), (Join-Path $fixture '.github') -Force | Out-Null
    foreach ($name in @('create-test-project.sh', 'test-framework-evidence.cs.txt', 'test-framework-evidence.asmdef.txt')) {
        Copy-Item -LiteralPath (Join-Path $root "scripts/unity/$name") -Destination (Join-Path $fixture "scripts/unity/$name")
    }
    Copy-Item -LiteralPath (Join-Path $root '.github/unity-test-project-modules.json') -Destination (Join-Path $fixture '.github/unity-test-project-modules.json')
    $policyPath = Join-Path $fixture '.github/unity-test-framework.json'
    foreach ($version in @('1.4.6', '1.4.99')) {
        @{ requestedVersion = $version } | ConvertTo-Json | Set-Content -LiteralPath $policyPath
        $exporter = Join-Path $root 'scripts/unity/stage-unitypackage.js'
        $actual = & node -e 'console.log(require(process.argv[1]).testFrameworkVersion(process.argv[2]))' $exporter $fixture
        if ($LASTEXITCODE -ne 0 -or $actual -cne $version) { throw 'Export builder ignored the shared framework policy.' }
        $checks++
        foreach ($unity in @('2021.3.45f1', '2022.3.45f1', '6000.5.2f1', '6000.6.0f1')) {
            $manifest = New-ManifestJson -Root $fixture | ConvertFrom-Json -AsHashtable
            if ($manifest.dependencies['com.unity.test-framework'] -cne $version -or $manifest.testables[0] -cne $PackageName -or $manifest.dependencies.Count -ne 14) { throw 'CI builder did not use the complete shared manifest policy.' }
            $env:UNITY_VERSION = $unity
            $env:UNITY_TEST_PROJECT_DIR = Join-Path $temporary "local-$unity-$version"
            $output = & bash (Join-Path $fixture 'scripts/unity/create-test-project.sh') 2>&1
            if ($LASTEXITCODE -ne 0) { throw "Local builder failed: $output" }
            $local = Get-Content -LiteralPath (Join-Path $env:UNITY_TEST_PROJECT_DIR 'Packages/manifest.json') -Raw | ConvertFrom-Json -AsHashtable
            if ($local.dependencies['com.unity.test-framework'] -cne $version -or $local.dependencies.Count -ne 14) { throw 'Local builder ignored the shared framework policy.' }
            foreach ($name in @('UhCiTestFrameworkEvidence.cs', 'UhCiTestFrameworkEvidence.asmdef')) {
                if (-not (Test-Path -LiteralPath (Join-Path $env:UNITY_TEST_PROJECT_DIR "Assets/UhCiTestFrameworkEvidence/Editor/$name"))) { throw "Missing local evidence source $name" }
            }
            $checks += 2
        }
    }
    foreach ($invalid in @('{}', '{"requestedVersion":null}', '{"requestedVersion":1.4}', '{"requestedVersion":""}', '{"requestedVersion":" 1.4.6"}', '{"requestedVersion":"latest"}', '{"requestedVersion":"1.4.6-preview.1"}', '{')) {
        [IO.File]::WriteAllText($policyPath, $invalid)
        $rejected = $false
        try { New-ManifestJson -Root $fixture | Out-Null } catch { $rejected = $true }
        if (-not $rejected) { throw "CI accepted invalid framework policy $invalid" }
        $env:UNITY_TEST_PROJECT_DIR = Join-Path $temporary ('invalid-' + $checks)
        $output = & bash (Join-Path $fixture 'scripts/unity/create-test-project.sh') 2>&1
        if ($LASTEXITCODE -eq 0 -or (Test-Path -LiteralPath (Join-Path $env:UNITY_TEST_PROJECT_DIR 'Packages/manifest.json'))) { throw "Local builder published invalid framework policy $invalid : $output" }
        $output = & node -e 'require(process.argv[1]).testFrameworkVersion(process.argv[2])' $exporter $fixture 2>&1
        if ($LASTEXITCODE -eq 0) { throw 'Export builder accepted invalid framework policy.' }
        $checks += 3
    }
    Remove-Item -LiteralPath $policyPath
    $rejected = $false
    try { New-ManifestJson -Root $fixture | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'CI accepted missing framework policy.' }
    $checks++
    $current = Get-Content -LiteralPath (Join-Path $root '.github/unity-test-framework.json') -Raw | ConvertFrom-Json
    if ($current.requestedVersion -cne '1.4.6') { throw 'Reviewed registry candidate must be requested.' }
    $checks++
} finally {
    $env:UNITY_VERSION = $previousVersion
    $env:UNITY_TEST_PROJECT_DIR = $previousProject
    Remove-Item -LiteralPath $temporary -Force -Recurse -ErrorAction SilentlyContinue
}
Write-Host "Unity framework policy: $checks controls passed."
