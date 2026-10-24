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
            if ($file.Path -isnot [string] -or $file.Path -cnotmatch '^[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*$' -or
                @($file.Path.Split('/') | Where-Object { $_ -in @('.', '..') }).Count -gt 0 -or -not $paths.Add($file.Path)) {
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
