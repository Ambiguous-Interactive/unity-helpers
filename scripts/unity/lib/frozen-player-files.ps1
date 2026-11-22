#!/usr/bin/env pwsh

. (Join-Path $PSScriptRoot 'source-census-python.ps1')

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

    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $declaration = $null
    if ($null -ne $DeclaredFiles) {
        if ($DeclaredFiles.Count -lt 1 -or $DeclaredFiles.Count -gt $MaximumFiles) {
            throw "Frozen input lists require between 1 and $MaximumFiles files."
        }
        $declaration = [Collections.Generic.List[object]]::new($DeclaredFiles.Count)
        foreach ($entry in $DeclaredFiles) {
            if ($entry.Path -isnot [string] -or $entry.Sha256 -isnot [string]) {
                throw 'Frozen input paths and hashes must be scalar strings.'
            }
            if (-not $paths.Add($entry.Path)) {
                throw "Invalid or colliding frozen input path/hash: $($entry.Path)"
            }
            $declaration.Add(@{ Path = $entry.Path; Sha256 = $entry.Sha256 })
        }
    }
    $rootPath = [IO.Path]::GetFullPath($Root)
    $inventory = Invoke-WProtoSourceCensus -Operation inventory -Root $rootPath -DeclaredFiles $declaration -MaximumFiles $MaximumFiles
    if ($null -eq $DeclaredFiles) {
        foreach ($row in $inventory.rows) {
            if (-not $paths.Add($row.Path)) { throw "Frozen payload has colliding paths: $($row.Path)" }
        }
    }
    foreach ($row in $inventory.rows) {
        [pscustomobject][ordered]@{ Path = $row.Path; Kind = $row.Kind; Length = $row.Length; Sha256 = $row.Sha256 }
    }
    if ($null -ne $inventory.error) { throw $inventory.error }
}
