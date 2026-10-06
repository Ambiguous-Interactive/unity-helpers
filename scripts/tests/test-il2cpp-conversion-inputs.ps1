#requires -Version 7.0
[CmdletBinding()]
param([string]$ControlsPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../unity/lib/il2cpp-conversion-inputs.ps1')
$usesTemporaryDirectory = [string]::IsNullOrWhiteSpace($ControlsPath)
$root = if ($usesTemporaryDirectory) {
    Join-Path ([IO.Path]::GetTempPath()) ('nullable-converter-controls-' + [Guid]::NewGuid().ToString('N'))
} else {
    [IO.Path]::GetFullPath($ControlsPath)
}
if (Test-Path $root) { throw 'Refusing existing parser control directory' }
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $complete = Join-Path $root 'managed stripped'
    $first = Join-Path $root 'partial-a'
    $second = Join-Path $root 'partial-b'
    New-Item -ItemType Directory -Path $complete, $first, $second | Out-Null
    $required = @('NestedOnly.Consumer.dll', 'WallstopStudios.UnityHelpers.dll', 'protobuf-net.dll', 'protobuf-net.Core.dll')
    foreach ($name in $required) { [IO.File]::WriteAllText((Join-Path $complete $name), "SYNTHETIC PARSER CONTROL $name") }
    foreach ($name in $required[0..1]) { [IO.File]::WriteAllText((Join-Path $first $name), "SYNTHETIC PARSER CONTROL $name") }
    foreach ($name in $required[2..3]) { [IO.File]::WriteAllText((Join-Path $second $name), "SYNTHETIC PARSER CONTROL $name") }
    $results = [Collections.Generic.List[object]]::new()
    function Require {
        param([bool]$Valid, [string]$Name)
        if (-not $Valid) { throw "Parser control failed: $Name" }
        $results.Add(@{ control = $Name; passed = $true })
    }
    $direct = @(Get-Il2CppConversionInputs -CommandLines @("`"/Editor/il2cpp/build/deploy/il2cpp.exe`" --convert-to-cpp --directory=`"$complete`"") -Project $root)
    Require ($direct.Count -eq 1 -and @(Get-QualifiedConversionModules -Conversions $direct).Count -eq 1) 'actual-converter-direct-one-complete-input-set'
    $linker = @(Get-Il2CppConversionInputs -CommandLines @("`"/Editor/il2cpp/build/deploy/UnityLinker.exe`" --directory=`"$complete`" --include-link-xml=descriptor.xml") -Project $root)
    Require ($linker.Count -eq 0) 'UnityLinker-in-il2cpp-directory-does-not-qualify'
    $mentioned = @(Get-Il2CppConversionInputs -CommandLines @("UnityLinker.exe --note il2cpp.exe --convert-to-cpp --directory=`"$complete`"") -Project $root)
    Require ($mentioned.Count -eq 0) 'converter-mentioned-in-another-executable-invocation-rejected'
    $bare = @(Get-Il2CppConversionInputs -CommandLines @("--convert-to-cpp --directory=`"$complete`"") -Project $root)
    Require ($bare.Count -eq 0) 'bare-unassociated-response-content-does-not-qualify'
    $notConvert = @(Get-Il2CppConversionInputs -CommandLines @("il2cpp.exe --directory=`"$complete`" --print-command-line") -Project $root)
    Require ($notConvert.Count -eq 0) 'converter-name-without-conversion-does-not-qualify'
    $inner = Join-Path $root 'inner.rsp'
    $outer = Join-Path $root 'outer.rsp'
    [IO.File]::WriteAllText($inner, "--convert-to-cpp --directory=`"$complete`"")
    [IO.File]::WriteAllText($outer, '@inner.rsp')
    $chained = @(Get-Il2CppConversionInputs -CommandLines @("il2cpp.exe @`"$outer`"") -Project $root)
    Require ($chained.Count -eq 1 -and $chained[0].responseChain.Count -eq 2 -and @(Get-QualifiedConversionModules -Conversions $chained).Count -eq 1) 'actual-converter-associated-nested-response-chain'
    $partials = @(Get-Il2CppConversionInputs -CommandLines @("il2cpp.exe --convert-to-cpp --directory=`"$first`"", "il2cpp.exe --convert-to-cpp --directory=`"$second`"") -Project $root)
    Require ($partials.Count -eq 2 -and @(Get-QualifiedConversionModules -Conversions $partials).Count -eq 0) 'four-module-union-across-separate-invocations-rejected'
    $threw = $false
    try { Get-Il2CppConversionInputs -CommandLines @("il2cpp.exe --convert-to-cpp --directory=`"$first`" --directory=`"$second`"") -Project $root | Out-Null } catch { $threw = $true }
    Require $threw 'multiple-different-input-directories-fail-closed'
    $threw = $false
    try { Get-Il2CppConversionInputs -CommandLines @('il2cpp.exe @missing.rsp') -Project $root | Out-Null } catch { $threw = $true }
    Require $threw 'missing-actual-response-chain-fails-closed'
    $threw = $false
    try { Get-Il2CppConversionInputs -CommandLines @('il2cpp.exe --convert-to-cpp --directory=missing') -Project $root | Out-Null } catch { $threw = $true }
    Require $threw 'missing-actual-input-directory-fails-closed'
    $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'parser-results.json')
    if ($results.Count -ne 10) { throw 'Parser controls silently omitted cases' }
    "Parser controls passed10; synthetic input modules are not Unity evidence."

} finally {
    if ($usesTemporaryDirectory) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
