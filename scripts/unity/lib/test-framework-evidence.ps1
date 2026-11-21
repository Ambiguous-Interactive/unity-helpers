# MIT License - Copyright (c) wallstop studios

function Get-UnityFrameworkVerifierIdentity {
    param([string]$Repository)

    $inputs = @(
        'scripts/unity/lib/test-framework-evidence.ps1',
        'scripts/unity/framework_evidence.py',
        'scripts/python~/pyproject.toml',
        'scripts/python~/uv.lock'
    )
    $revision = git -C $Repository rev-parse --verify HEAD
    if ($LASTEXITCODE -ne 0 -or ($env:GITHUB_SHA -and $revision -cne $env:GITHUB_SHA)) {
        throw 'The Test Framework verifier checkout does not match the recorded revision.'
    }
    $objects = @(git -C $Repository rev-parse @($inputs | ForEach-Object { "${revision}:$_" }))
    if ($LASTEXITCODE -ne 0 -or $objects.Count -ne $inputs.Count) {
        throw 'The Test Framework verifier could not identify its tracked inputs.'
    }
    $observed = @(git -C $Repository hash-object -- @inputs)
    if ($LASTEXITCODE -ne 0 -or ($observed -join ',') -cne ($objects -join ',')) {
        throw 'The Test Framework verifier inputs do not match the recorded checkout.'
    }
    $files = foreach ($inputPath in $inputs) {
        $path = Join-Path $Repository $inputPath
        $entry = $path
        while ($entry -cne $Repository) {
            if ((Get-Item -LiteralPath $entry -Force -ErrorAction Stop).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Test Framework verifier inputs must be ordinary checkout paths: $entry"
            }
            $entry = Split-Path -LiteralPath $entry
        }
        $path
    }
    $hashes = (Get-FileHash -LiteralPath $files -Algorithm SHA256 -ErrorAction Stop).Hash
    return (@($revision) + $hashes) -join ','
}

function Assert-UnityTestFrameworkEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [Parameter(Mandatory = $true)][string]$Project
    )
    $snapshots = @(Get-ChildItem -LiteralPath $Directory -Directory -ErrorAction Stop)
    if ($snapshots.Count -lt 1) { throw 'No loaded Test Framework snapshot was captured.' }
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
    $toolIdentity = Get-UnityFrameworkVerifierIdentity -Repository $repository
    $uv = (Get-Command uv -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $arguments = @(
        'run', '--quiet', '--project', (Join-Path $repository 'scripts/python~'), '--locked', '--no-dev',
        'python', '-I', '-B', (Join-Path $repository 'scripts/unity/framework_evidence.py')
    )
    $pathComparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $streams = [Collections.Generic.Dictionary[string, IO.FileStream]]::new([StringComparer]::Ordinal)
    $retainedStates = [ordered]@{}
    $notes = [Collections.Generic.List[string]]::new()
    $PSNativeCommandUseErrorActionPreference = $false
    try {
        foreach ($snapshot in $snapshots) {
            if ($snapshot.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw 'Linked Test Framework snapshot directory.'
            }
            $output = @(& $uv @arguments --process-id $ProcessId -- $snapshot.FullName 2>&1)
            if ($LASTEXITCODE -ne 0) { throw ($output -join [Environment]::NewLine) }
            $verified = ($output -join [Environment]::NewLine) | ConvertFrom-Json -AsHashtable
            if (-not [string]::Equals([IO.Path]::GetFullPath($verified.project), [IO.Path]::GetFullPath($Project), $pathComparison)) {
                throw 'Test Framework snapshot does not match this Unity process/project.'
            }
            # Keep the exact verified bytes open through the CLR metadata read and success notice.
            foreach ($file in $verified.files.GetEnumerator()) {
                $path = Join-Path $snapshot.FullName $file.Key
                $streams[$path] = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
                if ((Get-FileHash -InputStream $streams[$path] -Algorithm SHA256).Hash -ine $file.Value) {
                    throw "Test Framework evidence changed during verification: $path"
                }
            }
            foreach ($module in $verified.modules) {
                $assemblyPath = Join-Path $snapshot.FullName $module.path
                $identity = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath)
                $stream = $streams[$assemblyPath]
                $stream.Position = 0
                $pe = [Reflection.PortableExecutable.PEReader]::new($stream, [Reflection.PortableExecutable.PEStreamOptions]::LeaveOpen)
                try {
                    $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
                    $mvid = $reader.GetGuid($reader.GetModuleDefinition().Mvid).ToString()
                    if ($identity.FullName -cne $module.name -or $mvid -cne $module.mvid) {
                        throw 'Loaded Test Framework module differs from captured DLL bytes.'
                    }
                } finally { $pe.Dispose() }
            }
            $retainedStates[$snapshot.FullName] = $verified.states
            $notes.Add("[test-framework] Unity $($verified.unity), resolved $($verified.version) ($($verified.source)); $($verified.source_count) source files, three bound modules, PID $ProcessId.")
        }
        $current = @(Get-ChildItem -LiteralPath $Directory -Directory -Name -ErrorAction Stop)
        if (($current -join [char]0) -cne ($snapshots.Name -join [char]0)) {
            throw 'The Test Framework snapshot inventory changed during verification.'
        }
        foreach ($snapshot in $snapshots) {
            if (Test-Path -LiteralPath (Join-Path $snapshot.FullName 'capture-error.txt')) {
                throw 'Test Framework source capture was incomplete.'
            }
        }
        # Bind all checked bytes to the same snapshot before reporting success.
        $receipt = ConvertTo-Json -InputObject $retainedStates -Depth 5 -Compress -EscapeHandling EscapeNonAscii
        $output = @($receipt | & $uv @arguments --check-retained-state 2>&1)
        if ($LASTEXITCODE -ne 0) { throw ($output -join [Environment]::NewLine) }
        if ((Get-UnityFrameworkVerifierIdentity -Repository $repository) -cne $toolIdentity) {
            throw 'The Test Framework verifier checkout changed during verification.'
        }
        foreach ($note in $notes) { Write-Host $note }
    } finally {
        foreach ($stream in $streams.Values) { $stream.Dispose() }
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
    $hashes += 'player-input-manager-v1'
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
