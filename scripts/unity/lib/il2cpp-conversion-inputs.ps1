#requires -Version 7.0
Set-StrictMode -Version Latest
function Get-ToolArgumentTokens {
    param([string]$Text)
    foreach ($match in [regex]::Matches($Text, '(?:"[^"]*"|[^"\s])+')) {
        $match.Value.Replace('"', '')
    }
}
function Expand-ConverterResponseArguments {
    param([string[]]$Arguments, [string]$WorkingDirectory, [string]$ResponseDirectory, [int]$Depth = 0, [string]$ArchivedRoot, [string]$ResponseSuffix, [object[]]$ResponseRecords)
    if ($Depth -gt 8) { throw 'Converter response chain exceeds bound' }
    $expanded = [Collections.Generic.List[string]]::new()
    $responses = [Collections.Generic.List[object]]::new()
    foreach ($argument in $Arguments) {
        if (-not $argument.StartsWith('@', [StringComparison]::Ordinal)) { $expanded.Add($argument); continue }
        $relative = $argument.Substring(1)
        $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($directory in @($WorkingDirectory, $ResponseDirectory)) {
            $candidate = if ([IO.Path]::IsPathFullyQualified($relative)) { $relative } else { Join-Path $directory $relative }
            $candidate = [IO.Path]::GetFullPath($candidate)
            if ([string]::IsNullOrWhiteSpace($ArchivedRoot)) {
                if (Test-Path -LiteralPath $candidate -PathType Leaf) { $paths.Add($candidate) | Out-Null }
            } elseif (@($ResponseRecords | Where-Object { [string]::Equals($_.path, $candidate, [StringComparison]::OrdinalIgnoreCase) }).Count -ne 0) { $paths.Add($candidate) | Out-Null }
        }
        if ($paths.Count -ne 1) { throw "Converter response path missing or ambiguous: $relative" }
        $path = @($paths)[0]
        $readPath = $path
        if (-not [string]::IsNullOrWhiteSpace($ArchivedRoot)) {
            $record = @($ResponseRecords | Where-Object { [string]::Equals($_.path, $path, [StringComparison]::OrdinalIgnoreCase) })
            if ($record.Count -ne 1 -or $record[0].sha256 -notmatch '^[0-9a-f]{64}$') { throw 'Archived response record missing or duplicated' }
            $readPath = Join-Path $ArchivedRoot "$($record[0].sha256).$ResponseSuffix.rsp"
            if ((Get-FileHash -LiteralPath $readPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $record[0].sha256) { throw 'Archived response content hash differs' }
        }
        $responses.Add(@{ path = $path; sha256 = (Get-FileHash -LiteralPath $readPath -Algorithm SHA256).Hash.ToLowerInvariant() })
        $nested = Expand-ConverterResponseArguments -Arguments @(Get-ToolArgumentTokens -Text ([IO.File]::ReadAllText($readPath))) -WorkingDirectory $WorkingDirectory -ResponseDirectory ([IO.Path]::GetDirectoryName($path)) -Depth ($Depth + 1) -ArchivedRoot $ArchivedRoot -ResponseSuffix $ResponseSuffix -ResponseRecords $ResponseRecords
        foreach ($item in $nested.arguments) { $expanded.Add($item) }
        foreach ($item in $nested.responses) { $responses.Add($item) }
    }
    @{ arguments = $expanded.ToArray(); responses = $responses.ToArray() }
}
function Get-ManagedToolInvocations {
    param([string[]]$CommandLines, [string]$Project, [ValidateSet('UnityLinker', 'il2cpp')][string]$Tool, [string]$ArchivedRoot, [object[]]$ResponseRecords)
    foreach ($line in $CommandLines) {
        $tokens = @(Get-ToolArgumentTokens -Text $line)
        if ($tokens.Count -eq 0 -or $tokens[0] -notmatch ('(?:^|[\\/])' + [regex]::Escape($Tool) + '(?:\.exe)?$')) { continue }
        $arguments = @($tokens | Select-Object -Skip 1)
        $marker = if ($Tool -eq 'il2cpp') { '--convert-to-cpp' } else { '--include-link-xml' }
        if (@($arguments | Where-Object { $_ -eq $marker -or $_.StartsWith($marker + '=', [StringComparison]::Ordinal) -or $_.StartsWith('@', [StringComparison]::Ordinal) }).Count -eq 0) { continue }
        $suffix = if ($Tool -eq 'il2cpp') { 'converter' } else { 'linker' }
        $resolved = Expand-ConverterResponseArguments -Arguments $arguments -WorkingDirectory $Project -ResponseDirectory $Project -ArchivedRoot $ArchivedRoot -ResponseSuffix $suffix -ResponseRecords $ResponseRecords
        if (-not [string]::IsNullOrWhiteSpace($ArchivedRoot)) {
            if ($resolved.responses.Count -ne $ResponseRecords.Count) { throw 'Archived response chain inventory differs' }
            for ($responseIndex = 0; $responseIndex -lt $resolved.responses.Count; ++$responseIndex) {
                if ($resolved.responses[$responseIndex].path -ne $ResponseRecords[$responseIndex].path -or $resolved.responses[$responseIndex].sha256 -ne $ResponseRecords[$responseIndex].sha256) { throw 'Archived response chain association differs' }
            }
        }
        $sha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($line))).ToLowerInvariant()
        @{ executable = $tokens[0]; command = $line; commandSHA256 = $sha; arguments = $resolved.arguments; responseChain = $resolved.responses }
    }
}
function Get-LinkerLifecycleInputs {
    param([Parameter(Mandatory)][string]$CaptureRoot)
    if (((Get-Item -LiteralPath $CaptureRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Linked linker lifecycle snapshot root' }
    $manifest = Join-Path $CaptureRoot 'source-paths.SHA256SUMS'
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $files = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $files.Add('source-paths.SHA256SUMS') | Out-Null
    $rows = [Collections.Generic.List[object]]::new()
    foreach ($line in [IO.File]::ReadAllLines($manifest)) {
        if ($line -notmatch '^([0-9a-f]{64})  ([^\r\n]+)$') { throw 'Malformed linker lifecycle snapshot' }
        $hash = $Matches[1]; $path = $Matches[2]
        if (-not [IO.Path]::IsPathFullyQualified($path) -or [IO.Path]::GetFullPath($path) -ne $path -or -not $paths.Add($path)) { throw 'Duplicated or nonabsolute linker lifecycle source path' }
        $captured = Join-Path $CaptureRoot "$hash.xml"
        if ((Get-FileHash -LiteralPath $captured -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw 'Linker lifecycle captured content hash differs' }
        $files.Add("$hash.xml") | Out-Null
        $rows.Add(@{ path = $path; sha256 = $hash; capturedPath = $captured })
    }
    if ($rows.Count -eq 0) { throw 'Empty linker lifecycle snapshot' }
    $actual = @(Get-ChildItem -LiteralPath $CaptureRoot -Recurse -Force)
    if ($actual.Count -ne $files.Count) { throw 'Linker lifecycle snapshot file inventory differs' }
    foreach ($item in $actual) {
        if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or -not $files.Contains($item.Name)) { throw 'Unexpected or linked linker lifecycle snapshot file' }
    }
    $rows.ToArray()
}
function Get-LinkerXmlContentPath {
    param([Parameter(Mandatory)][string]$Path, [object[]]$CapturedXmlInputs = @())
    $record = @($CapturedXmlInputs | Where-Object { $null -ne $_ -and [string]::Equals($_.path, $Path, [StringComparison]::OrdinalIgnoreCase) })
    if ($record.Count -gt 1) { throw 'Duplicated linker lifecycle source association' }
    if ($record.Count -eq 1) {
        if ((Get-FileHash -LiteralPath $record[0].capturedPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $record[0].sha256) { throw 'Linker lifecycle captured content hash differs' }
        if ((Test-Path -LiteralPath $Path -PathType Leaf) -and (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $record[0].sha256) { throw 'Linker XML source changed after lifecycle capture' }
        return $record[0].capturedPath
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Actual linker XML input missing: $Path" }
    $Path
}
function Get-UnityLinkerInputs {
    param([string[]]$CommandLines, [string]$Project, [string]$ArchivedRoot, [object[]]$ResponseRecords, [object[]]$CapturedXmlInputs = @())
    foreach ($invocation in (Get-ManagedToolInvocations -CommandLines $CommandLines -Project $Project -Tool UnityLinker -ArchivedRoot $ArchivedRoot -ResponseRecords $ResponseRecords)) {
        $xml = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        for ($index = 0; $index -lt $invocation.arguments.Count; ++$index) {
            $argument = $invocation.arguments[$index]
            $path = $null
            if ($argument -match '^--include-link-xml=(.+)$') { $path = $Matches[1] }
            elseif ($argument -eq '--include-link-xml') {
                if ($index + 1 -ge $invocation.arguments.Count) { throw 'Linker XML argument has no value' }
                $path = $invocation.arguments[++$index]
            }
            if ($null -eq $path) { continue }
            if (-not [IO.Path]::IsPathFullyQualified($path)) { $path = Join-Path $Project $path }
            $path = [IO.Path]::GetFullPath($path)
            if ([string]::IsNullOrWhiteSpace($ArchivedRoot)) { Get-LinkerXmlContentPath -Path $path -CapturedXmlInputs $CapturedXmlInputs | Out-Null }
            if (-not $xml.Add($path)) { throw 'Duplicated linker XML argument' }
        }
        if ($xml.Count -eq 0) { continue }
        $invocation.Remove('arguments')
        $invocation.xmlInputPaths = @($xml)
        $invocation
    }
}
function Get-Il2CppConversionInputs {
    param([string[]]$CommandLines, [string]$Project, [string]$ArchivedRoot, [object[]]$ResponseRecords)
    foreach ($invocation in (Get-ManagedToolInvocations -CommandLines $CommandLines -Project $Project -Tool il2cpp -ArchivedRoot $ArchivedRoot -ResponseRecords $ResponseRecords)) {
        $arguments = $invocation.arguments
        if ($arguments -notcontains '--convert-to-cpp') { continue }
        $directories = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $assemblies = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        for ($index = 0; $index -lt $arguments.Count; ++$index) {
            $argument = $arguments[$index]
            $value = $null
            $assembly = $false
            if ($argument -match '^--(?:managed-)?directory=(.+)$') { $value = $Matches[1] }
            elseif ($argument -match '^--assembly=(.+)$') { $value = $Matches[1]; $assembly = $true }
            elseif ($argument -in @('--directory', '--managed-directory', '--assembly')) {
                if ($index + 1 -ge $arguments.Count) { throw 'Converter directory/assembly argument has no value' }
                $assembly = $argument -eq '--assembly'
                $value = $arguments[++$index]
            }
            if ($null -eq $value) { continue }
            if (-not [IO.Path]::IsPathFullyQualified($value)) { $value = Join-Path $Project $value }
            $value = [IO.Path]::GetFullPath($value)
            if ($assembly) {
                if ([string]::IsNullOrWhiteSpace($ArchivedRoot) -and -not (Test-Path -LiteralPath $value -PathType Leaf)) { throw "Actual converter assembly input missing: $value" }
                if (-not $assemblies.Add($value)) { throw 'Duplicated converter assembly argument' }
            } else {
                if ([string]::IsNullOrWhiteSpace($ArchivedRoot) -and -not (Test-Path -LiteralPath $value -PathType Container)) { throw "Actual converter input directory missing: $value" }
                $directories.Add($value) | Out-Null
            }
        }
        if ($assemblies.Count -ne 0 -and $directories.Count -ne 0) { throw 'Mixed converter directory and assembly input styles are unsupported' }
        $mode = 'directory'
        if ($assemblies.Count -ne 0) {
            $mode = 'assemblies'
            foreach ($path in $assemblies) { $directories.Add([IO.Path]::GetDirectoryName($path)) | Out-Null }
        }
        if ($directories.Count -ne 1) { throw 'Converter invocation must identify one unambiguous managed input set' }
        $invocation.Remove('arguments')
        $invocation.inputDirectory = @($directories)[0]
        $invocation.inputMode = $mode
        $invocation.explicitInputPaths = @($assemblies)
        $invocation
    }
}
function Get-QualifiedConversionModules {
    param([object[]]$Conversions)
    $required = @('NestedOnly.Consumer.dll', 'WallstopStudios.UnityHelpers.dll', 'protobuf-net.dll', 'protobuf-net.Core.dll')
    foreach ($conversion in $Conversions) {
        $paths = if ($conversion.inputMode -eq 'assemblies') { @($conversion.explicitInputPaths) } elseif ($conversion.inputMode -eq 'directory') { @(Get-ChildItem -LiteralPath $conversion.inputDirectory -File -Filter '*.dll' | ForEach-Object FullName) } else { throw 'Unknown converter input mode' }
        $names = @($paths | ForEach-Object { [IO.Path]::GetFileName($_) })
        if (@($required | Where-Object { $_ -notin $names }).Count -ne 0) { continue }
        $modules = @(foreach ($path in $paths) { @{ module = [IO.Path]::GetFileName($path); inputPath = $path; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() } })
        @{ invocation = $conversion; modules = $modules }
    }
}
function Get-Il2CppPlayerInventory {
    param([Parameter(Mandatory)][string]$PlayerRoot)
    $root = [IO.Path]::GetFullPath($PlayerRoot)
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'Actual player directory missing' }
    $rootItem = Get-Item -LiteralPath $root -Force
    $items = @(Get-ChildItem -LiteralPath $root -Recurse -Force)
    foreach ($item in @($rootItem) + $items) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Player inventory cannot contain linked paths' }
    }
    $files = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($file in $items) {
        if ($file.PSIsContainer) { continue }
        $relative = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
        $files.Add($relative, (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant())
    }
    foreach ($required in @('IndependentConsumer.exe', 'GameAssembly.dll', 'UnityPlayer.dll', 'IndependentConsumer_Data/il2cpp_data/Metadata/global-metadata.dat')) {
        if (-not $files.ContainsKey($required)) { throw "Required actual player file missing: $required" }
    }
    [string[]]$paths = @($files.Keys)
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    foreach ($path in $paths) { @{ path = $path; sha256 = $files[$path] } }
}
function Assert-Il2CppPlayerManifest {
    param([Parameter(Mandatory)][string]$PlayerRoot, [Parameter(Mandatory)][string]$ManifestPath, [Parameter(Mandatory)][string]$ExpectedSHA256)
    if ($ExpectedSHA256 -notmatch '^[0-9a-f]{64}$' -or (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $ExpectedSHA256) { throw 'Actual player manifest hash differs' }
    $rows = @(Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json)
    $expected = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($row in $rows) {
        if ($row.path -isnot [string] -or $row.path -match '(^/|\\|:|[\r\n]|(?:^|/)\.{1,2}(?:/|$)|//|/$)' -or [string]::IsNullOrWhiteSpace($row.path) -or $row.sha256 -notmatch '^[0-9a-f]{64}$' -or -not $expected.TryAdd($row.path, $row.sha256)) { throw 'Malformed or duplicated actual player manifest entry' }
    }
    $actual = @(Get-Il2CppPlayerInventory -PlayerRoot $PlayerRoot)
    if ($expected.Count -ne $actual.Count) { throw 'Actual player file inventory differs' }
    foreach ($row in $actual) {
        if (-not $expected.ContainsKey($row.path) -or $expected[$row.path] -ne $row.sha256) { throw "Actual player file differs: $($row.path)" }
    }
}
function New-Il2CppPlayerManifest {
    param([Parameter(Mandatory)][string]$PlayerRoot, [Parameter(Mandatory)][string]$ManifestPath)
    $root = [IO.Path]::GetFullPath($PlayerRoot)
    $path = [IO.Path]::GetFullPath($ManifestPath)
    $relative = [IO.Path]::GetRelativePath($root, $path)
    if ($relative -ne '..' -and -not $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) { throw 'Player manifest must be outside the player directory' }
    if (Test-Path -LiteralPath $path) { throw 'Refusing existing actual player manifest' }
    $rows = @(Get-Il2CppPlayerInventory -PlayerRoot $root)
    $rows | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $path -Encoding utf8
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Il2CppPlayerManifest -PlayerRoot $root -ManifestPath $path -ExpectedSHA256 $hash
    $hash
}

# The producer and verifier share this recipe; Unity-added dependencies are not ignored.
function New-NullableConsumerManifest {
    param(
        [Parameter(Mandatory)][string]$TemplateJson,
        [Parameter(Mandatory)][string]$ConfigurationJson,
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][ValidateSet('2021.3.45f1', '6000.6.0f1')][string]$UnityVersion
    )
    $manifest = ConvertFrom-Json -InputObject $TemplateJson -AsHashtable
    $configuration = ConvertFrom-Json -InputObject $ConfigurationJson -AsHashtable
    if ($manifest -isnot [Collections.IDictionary] -or $manifest['dependencies'] -isnot [Collections.IDictionary] -or
        $configuration -isnot [Collections.IDictionary] -or $configuration['modules'] -isnot [Collections.IDictionary] -or
        $configuration['nullableConsumerDependencies'] -isnot [Collections.IDictionary] -or
        -not $configuration['nullableConsumerDependencies'].Contains($UnityVersion) -or
        $configuration['nullableConsumerDependencies'][$UnityVersion] -isnot [Collections.IDictionary]) {
        throw 'Invalid nullable consumer dependency recipe'
    }
    $manifest['dependencies']['com.wallstop-studios.unity-helpers'] = 'file:' + $Repository.Replace('\', '/')
    foreach ($dependencies in @($configuration['modules'], $configuration['nullableConsumerDependencies'][$UnityVersion])) {
        foreach ($entry in $dependencies.GetEnumerator()) {
            if ($entry.Value -isnot [string]) { throw 'Nullable consumer dependency versions must be strings' }
            $manifest['dependencies'][$entry.Key] = $entry.Value
        }
    }
    return ConvertTo-Json -InputObject $manifest -Depth 100
}

# JsonElement preserves ordinal names and JSON kinds; PowerShell scalar equality does not.
function Test-NullableConsumerJsonValue {
    param([System.Text.Json.JsonElement]$Expected, [System.Text.Json.JsonElement]$Actual)
    if ($Expected.ValueKind -ne $Actual.ValueKind) { return $false }
    switch ($Expected.ValueKind.ToString()) {
        'Object' {
            $expectedProperties = [Collections.Generic.Dictionary[string,System.Text.Json.JsonElement]]::new([StringComparer]::Ordinal)
            $actualProperties = [Collections.Generic.Dictionary[string,System.Text.Json.JsonElement]]::new([StringComparer]::Ordinal)
            foreach ($property in $Expected.EnumerateObject()) {
                if (-not $expectedProperties.TryAdd($property.Name, $property.Value)) { return $false }
            }
            foreach ($property in $Actual.EnumerateObject()) {
                if (-not $actualProperties.TryAdd($property.Name, $property.Value)) { return $false }
            }
            if ($expectedProperties.Count -ne $actualProperties.Count) { return $false }
            foreach ($entry in $expectedProperties.GetEnumerator()) {
                if (-not $actualProperties.ContainsKey($entry.Key) -or
                    -not (Test-NullableConsumerJsonValue -Expected $entry.Value -Actual $actualProperties[$entry.Key])) { return $false }
            }
            return $true
        }
        'Array' {
            if ($Expected.GetArrayLength() -ne $Actual.GetArrayLength()) { return $false }
            for ($index = 0; $index -lt $Expected.GetArrayLength(); ++$index) {
                if (-not (Test-NullableConsumerJsonValue -Expected $Expected[$index] -Actual $Actual[$index])) { return $false }
            }
            return $true
        }
        'String' { return [string]::Equals($Expected.GetString(), $Actual.GetString(), [StringComparison]::Ordinal) }
        'Number' { return [string]::Equals($Expected.GetRawText(), $Actual.GetRawText(), [StringComparison]::Ordinal) }
        'True' { return $true }
        'False' { return $true }
        'Null' { return $true }
        default { return $false }
    }
}

function Test-NullableConsumerManifest {
    param([Parameter(Mandatory)][string]$ExpectedJson, [Parameter(Mandatory)][string]$ActualJson)
    $expectedDocument = $null
    $actualDocument = $null
    try {
        $expectedDocument = [System.Text.Json.JsonDocument]::Parse($ExpectedJson)
        $actualDocument = [System.Text.Json.JsonDocument]::Parse($ActualJson)
        return Test-NullableConsumerJsonValue -Expected $expectedDocument.RootElement -Actual $actualDocument.RootElement
    } finally {
        if ($null -ne $actualDocument) { $actualDocument.Dispose() }
        if ($null -ne $expectedDocument) { $expectedDocument.Dispose() }
    }
}
