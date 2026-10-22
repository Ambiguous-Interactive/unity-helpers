#!/usr/bin/env pwsh

function Assert-FrozenPlayerRegularPath {
    param([Parameter(Mandatory = $true)][string]$Path, [switch]$Directory)
    $fullPath = [IO.Path]::GetFullPath($Path)
    $item = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
    if ($item.PSIsContainer -ne $Directory.IsPresent) { throw "Frozen path has the wrong file/directory kind: $fullPath" }
    $ancestorPath = $fullPath
    while ($ancestorPath) {
        $ancestor = Get-Item -LiteralPath $ancestorPath -Force -ErrorAction Stop
        if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Frozen path has a reparse ancestor: $ancestorPath" }
        $parent = [IO.Directory]::GetParent($ancestorPath)
        $ancestorPath = if ($null -eq $parent) { $null } else { $parent.FullName }
    }
}

function Get-FrozenPlayerFileInventory {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [AllowNull()][object[]]$DeclaredFiles = $null,
        [int]$MaximumFiles = 4096
    )
    $rootPath = [IO.Path]::GetFullPath($Root)
    Assert-FrozenPlayerRegularPath -Path $rootPath -Directory
    $paths = New-Object 'System.Collections.Generic.List[string]'
    $expected = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase)
    if ($null -ne $DeclaredFiles) {
        if ($DeclaredFiles.Count -lt 1 -or $DeclaredFiles.Count -gt $MaximumFiles) {
            throw "Frozen input lists require between 1 and $MaximumFiles files."
        }
        foreach ($entry in $DeclaredFiles) {
            if ($entry.Path -isnot [string] -or $entry.Sha256 -isnot [string]) { throw 'Frozen input paths and hashes must be scalar strings.' }
            $relative = [string]$entry.Path
            $sha = [string]$entry.Sha256
            if ([string]::IsNullOrWhiteSpace($relative) -or $relative.Length -gt 1024 -or
                $relative -match '[\\:<>"|?*\x00-\x1f]' -or [IO.Path]::IsPathRooted($relative) -or
                @($relative.Split('/') | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' -or $_ -match '[. ]$' }).Count -ne 0 -or
                $sha -cnotmatch '^[a-f0-9]{64}$' -or $expected.ContainsKey($relative)) {
                throw "Invalid or colliding frozen input path/hash: $relative"
            }
            $expected.Add($relative, $sha)
            $paths.Add($relative)
        }
    } else {
        $directories = New-Object 'System.Collections.Generic.Stack[string]'
        $directories.Push($rootPath)
        $fileCount = 0
        while ($directories.Count -gt 0) {
            foreach ($item in @(Get-ChildItem -LiteralPath $directories.Pop() -Force -ErrorAction Stop)) {
                if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                    throw "Frozen payload contains a reparse point: $($item.FullName)"
                }
                $relative = $item.FullName.Substring($rootPath.TrimEnd([IO.Path]::DirectorySeparatorChar).Length + 1).Replace('\', '/')
                if ($expected.ContainsKey($relative)) { throw "Frozen payload has colliding paths: $relative" }
                $expected.Add($relative, '')
                $paths.Add($relative)
                if ($item.PSIsContainer) { $directories.Push($item.FullName) } else { $fileCount++ }
                if ($paths.Count -gt 100000) { throw 'Frozen payload exceeds 100000 entries.' }
            }
        }
        if ($fileCount -eq 0) { throw 'Frozen payload contains no files.' }
    }
    [string[]]$orderedPaths = $paths.ToArray()
    [Array]::Sort($orderedPaths, [StringComparer]::Ordinal)
    foreach ($relative in $orderedPaths) {
        $fullPath = $rootPath
        foreach ($component in $relative.Split('/')) {
            $fullPath = [IO.Path]::GetFullPath((Join-Path $fullPath $component))
            $rootPrefix = $rootPath.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
            if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Frozen path leaves inventory root: $relative" }
            $item = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Frozen inventory contains a reparse point: $relative"
            }
        }
        if ($item.PSIsContainer) {
            if ($null -ne $DeclaredFiles) { throw "Frozen input is not a file: $relative" }
            [pscustomobject][ordered]@{ Path = $relative; Kind = 'directory'; Length = $null; Sha256 = $null }
            continue
        }
        $lengthBefore = $item.Length
        $hash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
        $lengthAfter = (Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop).Length
        if ($lengthBefore -ne $lengthAfter) { throw "Frozen file changed while hashing: $relative" }
        [pscustomobject][ordered]@{ Path = $relative; Kind = 'file'; Length = $lengthAfter; Sha256 = $hash }
        if ($expected[$relative] -and $expected[$relative] -cne $hash) {
            throw "Frozen declared file hash does not match: $relative"
        }
    }
}
