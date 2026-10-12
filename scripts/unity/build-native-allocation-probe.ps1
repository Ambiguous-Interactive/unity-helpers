#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,
    [Parameter(Mandatory = $true)]
    [string]$ArtifactsPath,
    [string]$Repository = (Join-Path $PSScriptRoot '../..'),
    [string]$CompilerEnvironment = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'The native allocation probe requires Windows and the x64 Visual C++ compiler.'
}
if (-not $CompilerEnvironment) {
    $installer = Join-Path ([Environment]::GetEnvironmentVariable('ProgramFiles(x86)')) 'Microsoft Visual Studio/Installer/vswhere.exe'
    $installations = @(& $installer -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)
    if ($LASTEXITCODE -ne 0 -or $installations.Count -ne 1) { throw 'Cannot resolve one Visual C++ installation.' }
    $CompilerEnvironment = Join-Path $installations[0] 'VC/Auxiliary/Build/vcvars64.bat'
}
$compiler = (Resolve-Path -LiteralPath $CompilerEnvironment).Path
if ($compiler -match '["%\r\n]') { throw 'Compiler environment path cannot be represented safely in a command file.' }
$source = Join-Path $Repository 'Tests/Acceptance~/NativeAllocation/native-allocation-probe.cpp'
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
$directory = [IO.Path]::GetFullPath((Join-Path $ArtifactsPath 'native-allocation-probe'))
if (Test-Path -LiteralPath $directory) { throw 'Native probe artifacts already exist; do not overwrite an earlier qualification.' }
$null = New-Item -ItemType Directory -Path $directory
Copy-Item -LiteralPath $source -Destination (Join-Path $directory 'native-allocation-probe.cpp')
$header = '#define UH_NATIVE_PROBE_SOURCE_SHA256 "' + $sourceHash + '"' + [Environment]::NewLine
[IO.File]::WriteAllText((Join-Path $directory 'NativeSourceIdentity.h'), $header)
$arguments = '/nologo /LD /O2 /MT /W4 /WX /EHsc /FI NativeSourceIdentity.h native-allocation-probe.cpp /link /OUT:unity-helpers-allocation-probe.dll'
$command = "@echo off`r`nsetlocal DisableDelayedExpansion`r`ncall `"$compiler`"`r`nif errorlevel 1 exit /b 1`r`ncl $arguments`r`nexit /b %errorlevel%`r`n"
[IO.File]::WriteAllText((Join-Path $directory 'build.cmd'), $command)
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $env:COMSPEC
$start.Arguments = '/d /c build.cmd'
$start.WorkingDirectory = $directory
$start.UseShellExecute = $false
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$process = [Diagnostics.Process]::Start($start)
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
$process.WaitForExit()
[IO.File]::WriteAllText((Join-Path $directory 'build.log'), $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult())
if ($process.ExitCode -ne 0) { throw "Native allocation probe compilation failed with exit $($process.ExitCode). See $directory/build.log." }
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -cne $sourceHash -or
    (Get-FileHash -LiteralPath (Join-Path $directory 'native-allocation-probe.cpp') -Algorithm SHA256).Hash.ToLowerInvariant() -cne $sourceHash) {
    throw 'Native probe source changed during compilation.'
}
$binary = Join-Path $directory 'unity-helpers-allocation-probe.dll'
$binaryHash = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant()
$pluginDirectory = Join-Path $ProjectPath 'Assets/Plugins/x86_64'
$null = New-Item -ItemType Directory -Force -Path $pluginDirectory
$destination = Join-Path $pluginDirectory 'unity-helpers-allocation-probe.dll'
if (Test-Path -LiteralPath $destination) { throw 'The qualification project already has a native allocation probe.' }
Copy-Item -LiteralPath $binary -Destination $destination
if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() -cne $binaryHash) {
    throw 'Staged native allocation probe differs from the compiled binary.'
}
[ordered]@{
    SourcePath = 'Tests/Acceptance~/NativeAllocation/native-allocation-probe.cpp'; SourceSha256 = $sourceHash
    BinarySha256 = $binaryHash; CompilerEnvironment = $compiler; CompilerArguments = $arguments
    IdentityHeaderSha256 = (Get-FileHash -LiteralPath (Join-Path $directory 'NativeSourceIdentity.h') -Algorithm SHA256).Hash.ToLowerInvariant()
    Scope = 'Isolated Windows Release acceptance player; one pinned native profiler descriptor per process.'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'build.json')
return $sourceHash
