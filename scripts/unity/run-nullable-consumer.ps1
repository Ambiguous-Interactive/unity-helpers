#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('2021.3.45f1', '6000.6.0f1')][string]$UnityVersion,
    [Parameter(Mandatory)][string]$UnityEditorPath,
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$TemporaryRoot,
    [Parameter(Mandatory)][string]$ArtifactsPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoPath = [IO.Path]::GetFullPath($Repository)
$artifactRoot = [IO.Path]::GetFullPath($ArtifactsPath)
if (Test-Path $artifactRoot) { throw 'Refusing pre-existing acceptance artifacts' }
if (-not (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf)) { throw 'Explicit Unity editor executable missing' }
& (Join-Path $repoPath 'scripts/unity/assert-no-active-unity-editor.ps1')
$canonical = (Get-Content -LiteralPath (Join-Path $repoPath '.github/unity-versions.json') -Raw | ConvertFrom-Json).all
if ($canonical[0] -ne '2021.3.45f1' -or $canonical[-1] -ne '6000.6.0f1') { throw 'Independent consumer version pins drifted from canonical floor/latest' }
$commit = (& git -C $repoPath rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Cannot bind candidate commit' }
$token = [Guid]::NewGuid().ToString('N')
$project = Join-Path ([IO.Path]::GetFullPath($TemporaryRoot)) "nullable-independent-$UnityVersion-$token"
if (Test-Path $project) { throw 'Independent consumer project must be fresh' }
New-Item -ItemType Directory -Path $project, $artifactRoot | Out-Null
$template = Join-Path $repoPath 'Tests/Acceptance~/NullableMapConsumer'
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    Copy-Item -LiteralPath (Join-Path $template $folder) -Destination $project -Recurse
}
$manifestPath = Join-Path $project 'Packages/manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest.dependencies.'com.wallstop-studios.unity-helpers' = 'file:' + $repoPath.Replace('\', '/')
$moduleConfiguration = Get-Content -LiteralPath (Join-Path $repoPath '.github/unity-test-project-modules.json') -Raw | ConvertFrom-Json
foreach ($module in $moduleConfiguration.modules.PSObject.Properties) { $manifest.dependencies | Add-Member -NotePropertyName $module.Name -NotePropertyValue $module.Value -Force }
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath
"m_EditorVersion: $UnityVersion" | Set-Content -LiteralPath (Join-Path $project 'ProjectSettings/ProjectVersion.txt')
$prepareLog = Join-Path $artifactRoot 'editor-prepare.log'
$prepareStart = [Diagnostics.ProcessStartInfo]::new([IO.Path]::GetFullPath($UnityEditorPath))
$prepareStart.UseShellExecute = $false
foreach ($argument in @('-batchmode', '-nographics', '-quit', '-buildTarget', 'StandaloneWindows64', '-projectPath', $project, '-executeMethod', 'NestedOnlyConsumer.Editor.ConsumerBuild.Prepare', '-logFile', $prepareLog)) { $prepareStart.ArgumentList.Add($argument) }
$prepareProcess = [Diagnostics.Process]::new()
try {
    $prepareProcess.StartInfo = $prepareStart
    if (-not $prepareProcess.Start()) { throw 'Independent consumer settings preparation did not start' }
    if (-not $prepareProcess.WaitForExit(7200000)) { $prepareProcess.Kill($true); $prepareProcess.WaitForExit(); throw 'Independent settings preparation watchdog expired' }
    if ($prepareProcess.ExitCode -ne 0) { throw 'Independent consumer settings preparation failed' }
} finally { $prepareProcess.Dispose() }
function Get-ConsumerSourceInputs {
    param([string]$ConsumerProject)
    foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
        foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $ConsumerProject $folder) -File -Recurse | Sort-Object FullName)) {
            if ($file.Name -eq 'ConsumerBinding.cs') { continue }
            $file
        }
    }
}
$sourceRows = [Collections.Generic.List[string]]::new()
$packageHashes = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
$packageInputs = [Collections.Generic.List[IO.FileInfo]]::new()
foreach ($folder in @('Runtime', 'Editor', 'Generator~')) {
    foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $repoPath $folder) -File -Recurse)) {
        if ($file.FullName -match '[\\/](?:bin|obj|\.git)[\\/]') { continue }
        $packageInputs.Add($file)
    }
}
$packageInputs.Add((Get-Item -LiteralPath (Join-Path $repoPath 'package.json')))
if ($packageInputs.Count -eq 0) { throw 'Candidate manifest examined no package inputs' }
foreach ($file in ($packageInputs | Sort-Object FullName)) {
    $relative = [IO.Path]::GetRelativePath($repoPath, $file.FullName).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $packageHashes.Add($file.FullName, $hash)
    $sourceRows.Add("$hash  package/$relative")
}
$consumerHashes = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
$consumerInputs = @(Get-ConsumerSourceInputs -ConsumerProject $project)
foreach ($file in $consumerInputs) {
    $relative = [IO.Path]::GetRelativePath($project, $file.FullName).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $consumerHashes.Add($file.FullName, $hash)
    $sourceRows.Add("$hash  $relative")
}
$acceptanceTools = @('run-nullable-consumer.ps1','run-nullable-consumer-player-matrix.ps1','verify-nullable-consumer.ps1','lib/il2cpp-conversion-inputs.ps1','run-acceptance.ps1','verify-acceptance.ps1','resolve-test-matrix.js')
foreach ($relative in $acceptanceTools) {
    $path = Join-Path $PSScriptRoot $relative
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $packageHashes.Add($path,$hash)
    $sourceRows.Add("$hash  acceptance-tools/$relative")
}
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $template 'Goldens~') -File | Sort-Object FullName)) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $packageHashes.Add($file.FullName,$hash)
    $sourceRows.Add("$hash  acceptance-goldens/$($file.Name)")
}
$consumerArchive = Join-Path $artifactRoot 'consumer-inputs'
foreach ($file in $consumerInputs) {
    $relative = [IO.Path]::GetRelativePath($project,$file.FullName)
    $destination = Join-Path $consumerArchive $relative
    New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
}
foreach ($relative in @('.github/unity-versions.json','.github/unity-test-project-modules.json')) {
    $path = Join-Path $repoPath $relative
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $packageHashes.Add($path,$hash)
    $sourceRows.Add("$hash  acceptance-config/$relative")
}
$sourceManifestPath = Join-Path $artifactRoot 'candidate-source.SHA256SUMS'
$sourceRows | Set-Content -LiteralPath $sourceManifestPath
$sourceSHA = (Get-FileHash $sourceManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
$bindingPath = Join-Path $project 'Assets/Consumer/ConsumerBinding.cs'
$binding = [IO.File]::ReadAllText($bindingPath).Replace('__UNITY__', $UnityVersion).Replace('__COMMIT__', $commit).Replace('__SOURCE__', $sourceSHA).Replace('__TOKEN__', $token)
[IO.File]::WriteAllText($bindingPath, $binding)
$bindingSHA = (Get-FileHash $bindingPath -Algorithm SHA256).Hash.ToLowerInvariant()
Copy-Item -LiteralPath $bindingPath -Destination (Join-Path $artifactRoot 'generated-ConsumerBinding.cs')
"$bindingSHA  generated-ConsumerBinding.cs" | Set-Content -LiteralPath (Join-Path $artifactRoot 'generated-binding.SHA256SUMS')
$buildRoot = Join-Path $artifactRoot 'player'
New-Item -ItemType Directory -Path $buildRoot | Out-Null
$executable = Join-Path $buildRoot 'IndependentConsumer.exe'
$editorLog = Join-Path $artifactRoot 'editor-build.log'
$oldOutput = $env:CONSUMER_OUTPUT
try {
    $env:CONSUMER_OUTPUT = $executable
    $start = [Diagnostics.ProcessStartInfo]::new([IO.Path]::GetFullPath($UnityEditorPath))
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('-batchmode', '-nographics', '-quit', '-buildTarget', 'StandaloneWindows64', '-projectPath', $project, '-executeMethod', 'NestedOnlyConsumer.Editor.ConsumerBuild.Build', '-logFile', '-')) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $buildStdout = $null
    $buildStderr = $null
    $stdoutFile = $null
    $stderrFile = $null
    try {
        $process.StartInfo = $start
        $stdoutFile = [IO.File]::Create($editorLog)
        $stderrFile = [IO.File]::Create((Join-Path $artifactRoot 'editor-build.stderr.log'))
        if (-not $process.Start()) { throw 'Independent Unity build did not start' }
        $buildStdout = $process.StandardOutput.BaseStream.CopyToAsync($stdoutFile)
        $buildStderr = $process.StandardError.BaseStream.CopyToAsync($stderrFile)
        if (-not $process.WaitForExit(7200000)) { $process.Kill($true); $process.WaitForExit(); throw 'Independent build watchdog expired' }
        if ($process.ExitCode -ne 0 -or -not (Test-Path $executable)) { throw 'Independent build failed or player missing' }
    } finally {
        try {
            $drains = [Threading.Tasks.Task[]]@(@($buildStdout, $buildStderr) | Where-Object { $null -ne $_ })
            if ($drains.Count -ne 0 -and -not [Threading.Tasks.Task]::WhenAll($drains).Wait(30000)) { throw 'Independent build stdout/stderr drain timed out; partial logs retained' }
        } finally {
            try { $process.Dispose() } finally {
                try { if ($null -ne $stdoutFile) { $stdoutFile.Dispose() } } finally { if ($null -ne $stderrFile) { $stderrFile.Dispose() } }
            }
        }
    }
} finally { $env:CONSUMER_OUTPUT = $oldOutput }
foreach ($entry in $packageHashes.GetEnumerator()) {
    if ((Get-FileHash -LiteralPath $entry.Key -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Value) { throw "Candidate input changed during build: $($entry.Key)" }
}
$afterCount = 1
foreach ($folder in @('Runtime', 'Editor', 'Generator~')) {
    $afterCount += @(Get-ChildItem -LiteralPath (Join-Path $repoPath $folder) -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj|\.git)[\\/]' }).Count
}
if ($afterCount -ne $packageInputs.Count) { throw 'Candidate input file inventory changed during build' }
foreach ($entry in $consumerHashes.GetEnumerator()) {
    if ((Get-FileHash -LiteralPath $entry.Key -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Value) { throw "Consumer source/settings input changed during build: $($entry.Key)" }
}
$consumerAfter = @(Get-ConsumerSourceInputs -ConsumerProject $project)
if ($consumerAfter.Count -ne $consumerInputs.Count) { throw 'Consumer source/settings inventory changed during build' }
foreach ($file in $consumerAfter) {
    if (-not $consumerHashes.ContainsKey($file.FullName)) { throw "Consumer input was added during build: $($file.FullName)" }
}
if ((Get-FileHash $bindingPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $bindingSHA) { throw 'Generated binding changed during build' }
$provenanceRoot = Join-Path $artifactRoot 'actual-tool-inputs'
New-Item -ItemType Directory -Path $provenanceRoot | Out-Null
. (Join-Path $PSScriptRoot 'lib/il2cpp-conversion-inputs.ps1')
$lifecycleRoot = Join-Path $artifactRoot 'linker-lifecycle'
$lifecycleInputs = @()
$lifecycleManifestSHA = ''
if ($UnityVersion -eq '2021.3.45f1') {
    $lifecycleInputs = @(Get-LinkerLifecycleInputs -CaptureRoot $lifecycleRoot)
    $lifecycleManifestSHA = (Get-FileHash -LiteralPath (Join-Path $lifecycleRoot 'source-paths.SHA256SUMS') -Algorithm SHA256).Hash.ToLowerInvariant()
}
$linkerInputs = @(Get-UnityLinkerInputs -CommandLines ([IO.File]::ReadAllLines($editorLog)) -Project $project -CapturedXmlInputs $lifecycleInputs)
if ($linkerInputs.Count -eq 0) { throw 'Actual UnityLinker invocation inputs were not captured; acceptance remains unproven' }
$linkerInputs | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $provenanceRoot 'unity-linker-invocations.json')
$actualResponses = [Collections.Generic.List[object]]::new()
$actualXml = [Collections.Generic.List[object]]::new()
foreach ($invocation in $linkerInputs) {
    foreach ($response in $invocation.responseChain) {
        Copy-Item -LiteralPath $response.path -Destination (Join-Path $provenanceRoot "$($response.sha256).linker.rsp")
        $actualResponses.Add(@{ commandSHA256 = $invocation.commandSHA256; inputPath = $response.path; sha256 = $response.sha256 })
    }
    foreach ($path in $invocation.xmlInputPaths) {
        $contentPath = Get-LinkerXmlContentPath -Path $path -CapturedXmlInputs $lifecycleInputs
        $sha = (Get-FileHash -LiteralPath $contentPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $content = [IO.File]::ReadAllText($contentPath)
        Copy-Item -LiteralPath $contentPath -Destination (Join-Path $provenanceRoot "$sha.xml")
        $actualXml.Add(@{ commandSHA256 = $invocation.commandSHA256; actualInputPath = $path; sha256 = $sha; includesConsumerPresence = $content.Contains('NestedOnlyConsumer.PresenceDoc') })
    }
}
ConvertTo-Json -InputObject @($actualResponses.ToArray()) -Depth 5 | Set-Content (Join-Path $provenanceRoot 'unity-linker-response-inputs.json')
if (@($actualXml | Where-Object { $_.includesConsumerPresence }).Count -eq 0) { throw 'Actual UnityLinker input did not include preserved consumer PresenceDoc' }
$actualXml | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $provenanceRoot 'actual-linker-xml-inputs.json')

$conversions = @(Get-Il2CppConversionInputs -CommandLines ([IO.File]::ReadAllLines($editorLog)) -Project $project)
$qualifiedConversions = @(Get-QualifiedConversionModules -Conversions $conversions)
if ($qualifiedConversions.Count -eq 0) { throw 'No single actual converter input set contains all four required managed modules' }
foreach ($conversion in $qualifiedConversions) {
    $moduleRoot = Join-Path $provenanceRoot ("converter-inputs/" + $conversion.invocation.commandSHA256)
    New-Item -ItemType Directory -Force -Path $moduleRoot | Out-Null
    foreach ($module in $conversion.modules) {
        Copy-Item -LiteralPath $module.inputPath -Destination (Join-Path $moduleRoot $module.module)
    }
    foreach ($response in $conversion.invocation.responseChain) {
        Copy-Item -LiteralPath $response.path -Destination (Join-Path $provenanceRoot "$($response.sha256).converter.rsp")
    }
}
$qualifiedConversions | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $provenanceRoot 'il2cpp-conversion-modules.json')
$playerManifestPath = Join-Path $artifactRoot 'player-tree.json'
$playerManifestSHA256 = New-Il2CppPlayerManifest -PlayerRoot $buildRoot -ManifestPath $playerManifestPath
@{ linkerLifecycleManifestSHA256 = $lifecycleManifestSHA; playerManifestSHA256 = $playerManifestSHA256; commit = $commit; sourceManifest = $sourceSHA; generatedBindingSHA256 = $bindingSHA; unityVersion = $UnityVersion; runToken = $token; project = $project; executableSHA256 = (Get-FileHash $executable -Algorithm SHA256).Hash.ToLowerInvariant(); actualLinkerInvocationCount = $linkerInputs.Count; actualLinkerInputCount = $actualResponses.Count; actualLinkerXmlInputCount = $actualXml.Count; packageWorktreeStatus = @(& git -C $repoPath status --porcelain -- Runtime "Generator~" package.json) } | ConvertTo-Json | Set-Content (Join-Path $artifactRoot 'declaration.json')
$goldenArchive = Join-Path $artifactRoot 'goldens'
Copy-Item -LiteralPath (Join-Path $template 'Goldens~') -Destination $goldenArchive -Recurse
& (Join-Path $PSScriptRoot 'run-nullable-consumer-player-matrix.ps1') -Executable $executable -GoldenDirectory $goldenArchive -ResultDirectory (Join-Path $artifactRoot 'fresh-processes') -ExpectedUnityVersion $UnityVersion -CandidateCommit $commit -SourceManifest $sourceSHA -RunToken $token -PlayerManifestPath $playerManifestPath -PlayerManifestSHA256 $playerManifestSHA256
