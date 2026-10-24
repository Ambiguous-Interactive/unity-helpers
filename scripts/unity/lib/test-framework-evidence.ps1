# MIT License - Copyright (c) wallstop studios

function Assert-UnityTestFrameworkEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [Parameter(Mandatory = $true)][string]$Project
    )
    $pathComparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $snapshots = @(Get-ChildItem -LiteralPath $Directory -Directory -ErrorAction Stop)
    if ($snapshots.Count -lt 1) { throw 'No loaded Test Framework snapshot was captured.' }
    foreach ($snapshot in $snapshots) {
        if (Test-Path -LiteralPath (Join-Path $snapshot.FullName 'capture-error.txt')) { throw 'Test Framework source capture was incomplete.' }
        $receipt = Get-Content -LiteralPath (Join-Path $snapshot.FullName 'receipt.json') -Raw | ConvertFrom-Json -AsHashtable
        if ($receipt.SchemaVersion -ne 1 -or $receipt.ProcessId -ne $ProcessId -or $ProcessId -le 0 -or
            -not [string]::Equals([IO.Path]::GetFullPath($receipt.ProjectPath), [IO.Path]::GetFullPath($Project), $pathComparison)) {
            throw 'Test Framework snapshot does not match this Unity process/project.'
        }
        $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($file in $receipt.Files) {
            if ($file.Path -isnot [string] -or [string]::IsNullOrWhiteSpace($file.Path) -or
                [IO.Path]::IsPathRooted($file.Path) -or $file.Path.Contains('\') -or $file.Path -match '[\x00-\x1f\x7f:]' -or
                @($file.Path.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0 -or -not $paths.Add($file.Path)) {
                throw 'Malformed or duplicate Test Framework evidence path.'
            }
            $path = Join-Path $snapshot.FullName $file.Path
            $item = Get-Item -LiteralPath $path -ErrorAction Stop
            if ($item.Length -ne $file.Length -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.Sha256) {
                throw "Test Framework evidence bytes differ: $($file.Path)"
            }
        }
        foreach ($required in @('manifest.json', 'packages-lock.json', 'package/package.json')) {
            if (-not $paths.Contains($required)) { throw "Missing Test Framework metadata: $required" }
        }
        $sourceCount = @($paths | Where-Object { $_.StartsWith('package/', [StringComparison]::Ordinal) -and $_.EndsWith('.cs', [StringComparison]::Ordinal) }).Count
        if ($sourceCount -lt 1 -or $sourceCount -ne $receipt.SourceCount) { throw 'Test Framework source inventory is empty or incomplete.' }
        $package = Get-Content -LiteralPath (Join-Path $snapshot.FullName 'package/package.json') -Raw | ConvertFrom-Json -AsHashtable
        $lock = Get-Content -LiteralPath (Join-Path $snapshot.FullName 'packages-lock.json') -Raw | ConvertFrom-Json -AsHashtable
        if ($package.name -cne 'com.unity.test-framework' -or $package.version -cne $receipt.PackageVersion -or
            $lock.dependencies.'com.unity.test-framework'.version -cne $receipt.PackageVersion) {
            throw 'Resolved Test Framework package identity differs from captured metadata.'
        }
        $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($name in @('UnityEditor.TestRunner', 'UnityEngine.TestRunner', 'nunit.framework')) { [void]$expected.Add($name) }
        foreach ($assembly in $receipt.Assemblies) {
            if (-not $expected.Remove($assembly.Name) -or $assembly.SavedPath -cne "assemblies/$($assembly.Name).dll" -or -not $paths.Contains($assembly.SavedPath)) {
                throw 'Missing or duplicate loaded Test Framework assembly identity.'
            }
            $assemblyPath = Join-Path $snapshot.FullName $assembly.SavedPath
            $identity = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath)
            $stream = [IO.File]::OpenRead($assemblyPath)
            $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
            try {
                $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
                $mvid = $reader.GetGuid($reader.GetModuleDefinition().Mvid).ToString()
                if ($identity.FullName -cne $assembly.FullName -or $mvid -cne $assembly.ModuleVersionId) {
                    throw 'Loaded Test Framework module differs from captured DLL bytes.'
                }
            } finally { $pe.Dispose(); $stream.Dispose() }
        }
        if ($expected.Count -ne 0) { throw 'Missing loaded Test Framework assembly identity.' }
        Write-Host "[test-framework] Unity $($receipt.UnityVersion), resolved $($receipt.PackageVersion) ($($receipt.PackageSource)); $sourceCount source files, three bound modules, PID $ProcessId."
    }
}

function Get-UnityFrameworkTextHash {
    param([Parameter(Mandatory = $true)][string]$Path)
    $text = [IO.File]::ReadAllText($Path).Replace("`r`n", "`n").TrimEnd("`r", "`n")
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text))).ToLowerInvariant()
}

function Get-UnityTestFrameworkProjectFingerprint {
    param([Parameter(Mandatory = $true)][string]$Root)
    $hashes = foreach ($relative in @('.github/unity-test-framework.json', 'scripts/unity/test-framework-evidence.cs.txt', 'scripts/unity/test-framework-evidence.asmdef.txt')) {
        Get-UnityFrameworkTextHash -Path (Join-Path $Root $relative)
    }
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($hashes -join "`n")))).ToLowerInvariant().Substring(0, 16)
}

function Remove-LegacyUnityTestFrameworkCollector {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Project,
        [Parameter(Mandatory = $true)][string]$ArtifactsPath
    )
    $projectRoot = [IO.Path]::GetFullPath($Project)
    $parent = Join-Path $projectRoot 'Assets/UhCiTestFrameworkEvidence'
    $editor = Join-Path $parent 'Editor'
    $expected = [ordered]@{
        'UhCiTestFrameworkEvidence.cs' = 'db0be48f94d24c3b45a671d76bcd9a391afbf93ab34f4fc37022a53938c8fb58'
        'UhCiTestFrameworkEvidence.asmdef' = 'e6f18bc445ed60f7d6c65c0202b556c8d0710545bc0ff030a0342d32bd6adad1'
    }
    $present = @($expected.Keys | Where-Object { Test-Path -LiteralPath (Join-Path $editor $_) })
    if ($present.Count -eq 0) { return }
    if ($present.Count -ne 2) { throw 'Incomplete legacy framework collector; preserving its files.' }
    $paths = [Collections.Generic.List[string]]::new()
    foreach ($name in $expected.Keys) {
        $path = Join-Path $editor $name
        $paths.Add($path)
        if (Test-Path -LiteralPath "$path.meta") { $paths.Add("$path.meta") }
    }
    foreach ($path in @($projectRoot, (Join-Path $projectRoot 'Assets'), $parent, $editor, "$editor.meta", "$parent.meta") + $paths.ToArray()) {
        if (Test-Path -LiteralPath $path) {
            if ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked legacy framework collector; preserving its files.' }
        }
    }
    foreach ($name in $expected.Keys) {
        if ((Get-UnityFrameworkTextHash -Path (Join-Path $editor $name)) -cne $expected[$name]) { throw 'Unrecognized legacy framework collector; preserving its files.' }
    }
    . (Join-Path $PSScriptRoot '../assert-no-active-unity-editor.ps1')
    $saved = Join-Path ([IO.Path]::GetFullPath($ArtifactsPath)) ('retired-framework-collector/' + [Guid]::NewGuid().ToString('N'))
    if ($saved.StartsWith($projectRoot.TrimEnd('/', '\') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Retained collector evidence must be outside the legacy project.' }
    $null = New-Item -ItemType Directory -Path $saved -Force
    $inventory = foreach ($path in $paths) {
        $destination = Join-Path $saved ([IO.Path]::GetFileName($path))
        Copy-Item -LiteralPath $path -Destination $destination -ErrorAction Stop
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -cne $hash) { throw 'Legacy collector evidence copy differs; preserving originals.' }
        @{ OriginalPath = $path; SavedPath = $destination; Sha256 = $hash; Length = (Get-Item -LiteralPath $path).Length }
    }
    $report = @{ Project = $projectRoot; Files = @($inventory); Removed = $false }
    $reportPath = Join-Path $saved 'receipt.json'
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    foreach ($file in $inventory) {
        if ((Get-FileHash -LiteralPath $file.OriginalPath -Algorithm SHA256).Hash -cne $file.Sha256) { throw 'Legacy collector changed after capture; preserving originals.' }
    }
    foreach ($path in $paths) { Remove-Item -LiteralPath $path -Force -ErrorAction Stop }
    $report.Removed = $true
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Write-Host "[test-framework] Retired the exact obsolete collector from $projectRoot; originals retained at $saved."
}
