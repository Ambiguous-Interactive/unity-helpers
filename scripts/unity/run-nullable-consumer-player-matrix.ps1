#requires -Version 7.0
# MIT License - Copyright (c) 2026 wallstop
# Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$GoldenDirectory,
    [Parameter(Mandatory)][string]$ResultDirectory,
    [Parameter(Mandatory)][string]$ExpectedUnityVersion,
    [Parameter(Mandatory)][string]$CandidateCommit,
    [Parameter(Mandatory)][string]$SourceManifest,
    [Parameter(Mandatory)][string]$RunToken,
    [Parameter(Mandatory)][string]$PlayerManifestPath,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$PlayerManifestSHA256
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/il2cpp-conversion-inputs.ps1')
$exePath = [IO.Path]::GetFullPath($Executable)
$goldPath = [IO.Path]::GetFullPath($GoldenDirectory)
$resultPath = [IO.Path]::GetFullPath($ResultDirectory)
if (-not [IO.File]::Exists($exePath)) { throw "Executable missing: $exePath" }
[IO.Directory]::CreateDirectory($resultPath) | Out-Null
$buildRoot = [IO.Path]::GetDirectoryName($exePath)
$settings = [IO.File]::ReadAllText((Join-Path $buildRoot 'settings.txt'))
foreach ($line in @("unity=$ExpectedUnityVersion", 'backend=IL2CPP', 'stripping=High', 'compiler=Release', 'development=False')) {
    if ($settings -notmatch ('(?m)^' + [regex]::Escape($line) + '$')) { throw "Actual build setting missing: $line" }
}
$executableSHA256 = (Get-FileHash $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
$cases = [Collections.Generic.List[object]]::new()
foreach ($shape in @('null', 'empty', 'populated')) {
    $modes = @('typed-hash', 'object-hash', 'typed-equality', 'object-equality', 'serialize-read')
    foreach ($mode in $modes) { $cases.Add(@{ mode = $mode; shape = $shape }) }
}
$cases.Add(@{ mode = 'merge'; shape = 'populated' })
foreach ($mode in @('presence-typed-hash', 'presence-object-hash', 'presence-typed-equality', 'presence-object-equality', 'presence-serialize-read', 'presence-foreign')) { $cases.Add(@{ mode = $mode; shape = 'presence' }) }
$results = [Collections.Generic.List[object]]::new()
foreach ($case in $cases) {
    Assert-Il2CppPlayerManifest -PlayerRoot $buildRoot -ManifestPath $PlayerManifestPath -ExpectedSHA256 $PlayerManifestSHA256
    $stem = "$($case.mode)-$($case.shape)"
    $record = Join-Path $resultPath "$stem.result.txt"
    if ([IO.File]::Exists($record)) { throw "Refusing stale result: $record" }
    $start = [Diagnostics.ProcessStartInfo]::new($exePath)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('-batchmode', '-nographics', '-logFile', (Join-Path $resultPath "$stem.player.log"), '--mode', $case.mode, '--shape', $case.shape, '--golden', $goldPath, '--result', $record, '--unity', $ExpectedUnityVersion, '--commit', $CandidateCommit, '--source', $SourceManifest, '--run-token', $RunToken)) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    try {
        $process.StartInfo = $start
        if (-not $process.Start()) { throw 'Player process did not start' }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(120000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "Player watchdog timeout: $stem"
        }
        [IO.File]::WriteAllText((Join-Path $resultPath "$stem.stdout.txt"), $stdout.GetAwaiter().GetResult())
        [IO.File]::WriteAllText((Join-Path $resultPath "$stem.stderr.txt"), $stderr.GetAwaiter().GetResult())
        if ($process.ExitCode -ne 0 -or -not [IO.File]::Exists($record)) { throw "Player failed or record missing: $stem, exit $($process.ExitCode)" }
        $text = [IO.File]::ReadAllText($record)
        $prefix = "Passed|$($case.mode)|$($case.shape)|"
        if (-not $text.StartsWith($prefix, [StringComparison]::Ordinal)) { throw "Unexpected record: $text" }
        $exact = "Passed|$($case.mode)|$($case.shape)|$ExpectedUnityVersion|IL2CPP=True|commit=$CandidateCommit|source=$SourceManifest|run=$RunToken"
        if ($text -ne $exact) { throw "Actual player provenance differs: $text" }
        if ((Get-FileHash $exePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $executableSHA256) { throw 'Player executable changed during matrix' }
        $golden = Join-Path $goldPath "$($case.shape).base64"
        if (-not [IO.File]::Exists($golden)) { throw "Golden not captured: $golden" }
        $results.Add(@{ mode = $case.mode; shape = $case.shape; record = $text; goldenSHA256 = (Get-FileHash $golden -Algorithm SHA256).Hash; executableSHA256 = $executableSHA256; playerManifestSHA256 = $PlayerManifestSHA256; candidateCommit = $CandidateCommit; sourceManifest = $SourceManifest; runToken = $RunToken; unityVersion = $ExpectedUnityVersion })
    } finally {
        try { $process.Dispose() } finally {
            Assert-Il2CppPlayerManifest -PlayerRoot $buildRoot -ManifestPath $PlayerManifestPath -ExpectedSHA256 $PlayerManifestSHA256
        }
    }
}
$expected = 22
if ($results.Count -ne $expected) { throw "Expected $expected actual processes, got $($results.Count)" }
$results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $resultPath 'matrix.json')
Write-Output "Completed $expected fresh processes. IL2CPP=True"
