#requires -Version 7.0
Set-StrictMode -Version Latest
function Get-ToolArgumentTokens {
    param([string]$Text)
    foreach ($match in [regex]::Matches($Text, '(?:"[^"]*"|[^"\s])+')) {
        $match.Value.Replace('"', '')
    }
}
function Expand-ConverterResponseArguments {
    param([string[]]$Arguments, [string]$WorkingDirectory, [string]$ResponseDirectory, [int]$Depth = 0)
    if ($Depth -gt 8) { throw 'Converter response chain exceeds bound' }
    $expanded = [Collections.Generic.List[string]]::new()
    $responses = [Collections.Generic.List[object]]::new()
    foreach ($argument in $Arguments) {
        if (-not $argument.StartsWith('@', [StringComparison]::Ordinal)) { $expanded.Add($argument); continue }
        $relative = $argument.Substring(1)
        $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($directory in @($WorkingDirectory, $ResponseDirectory)) {
            $candidate = if ([IO.Path]::IsPathFullyQualified($relative)) { $relative } else { Join-Path $directory $relative }
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { $paths.Add([IO.Path]::GetFullPath($candidate)) | Out-Null }
        }
        if ($paths.Count -ne 1) { throw "Converter response path missing or ambiguous: $relative" }
        $path = @($paths)[0]
        $responses.Add(@{ path = $path; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() })
        $nested = Expand-ConverterResponseArguments -Arguments @(Get-ToolArgumentTokens -Text ([IO.File]::ReadAllText($path))) -WorkingDirectory $WorkingDirectory -ResponseDirectory ([IO.Path]::GetDirectoryName($path)) -Depth ($Depth + 1)
        foreach ($item in $nested.arguments) { $expanded.Add($item) }
        foreach ($item in $nested.responses) { $responses.Add($item) }
    }
    @{ arguments = $expanded.ToArray(); responses = $responses.ToArray() }
}
function Get-Il2CppConversionInputs {
    param([string[]]$CommandLines, [string]$Project)
    foreach ($line in $CommandLines) {
        $tokens = @(Get-ToolArgumentTokens -Text $line)
        for ($index = 0; $index -lt $tokens.Count; ++$index) {
            if ($tokens[$index] -notmatch '(?:^|[\\/])il2cpp(?:\.exe)?$') { continue }
            $precedingExecutable = $false
            for ($prior = 0; $prior -lt $index; ++$prior) {
                if ($tokens[$prior] -match '(?:^|[\\/])(?:UnityLinker(?:\.exe)?|dotnet(?:\.exe)?|[^\\/]+\.(?:exe|dll))$') { $precedingExecutable = $true; break }
            }
            if ($precedingExecutable) { continue }
            $arguments = @($tokens | Select-Object -Skip ($index + 1))
            if (@($arguments | Where-Object { $_ -eq '--convert-to-cpp' -or $_.StartsWith('@', [StringComparison]::Ordinal) }).Count -eq 0) { continue }
            $resolved = Expand-ConverterResponseArguments -Arguments $arguments -WorkingDirectory $Project -ResponseDirectory $Project
            if ($resolved.arguments -notcontains '--convert-to-cpp') { continue }
            $directories = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            for ($argumentIndex = 0; $argumentIndex -lt $resolved.arguments.Count; ++$argumentIndex) {
                $argument = $resolved.arguments[$argumentIndex]
                $directory = $null
                if ($argument -match '^--(?:managed-)?directory=(.+)$') { $directory = $Matches[1] }
                elseif ($argument -eq '--directory' -or $argument -eq '--managed-directory') {
                    if ($argumentIndex + 1 -ge $resolved.arguments.Count) { throw 'Converter directory argument has no value' }
                    $directory = $resolved.arguments[++$argumentIndex]
                }
                if ($null -eq $directory) { continue }
                if (-not [IO.Path]::IsPathFullyQualified($directory)) { $directory = Join-Path $Project $directory }
                if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Actual converter input directory missing: $directory" }
                $directories.Add([IO.Path]::GetFullPath($directory)) | Out-Null
            }
            if ($directories.Count -ne 1) { throw 'Converter invocation must identify one unambiguous managed input set' }
            $sha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($line))).ToLowerInvariant()
            @{ executable = $tokens[$index]; command = $line; commandSHA256 = $sha; inputDirectory = @($directories)[0]; responseChain = $resolved.responses }
        }
    }
}
function Get-QualifiedConversionModules {
    param([object[]]$Conversions)
    $required = @('NestedOnly.Consumer.dll', 'WallstopStudios.UnityHelpers.dll', 'protobuf-net.dll', 'protobuf-net.Core.dll')
    foreach ($conversion in $Conversions) {
        $complete = $true
        foreach ($module in $required) {
            if (-not (Test-Path -LiteralPath (Join-Path $conversion.inputDirectory $module) -PathType Leaf)) { $complete = $false; break }
        }
        if (-not $complete) { continue }
        $modules = @(foreach ($file in (Get-ChildItem -LiteralPath $conversion.inputDirectory -File -Filter '*.dll')) {
            @{ module = $file.Name; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
        })
        @{ invocation = $conversion; modules = $modules }
    }
}
