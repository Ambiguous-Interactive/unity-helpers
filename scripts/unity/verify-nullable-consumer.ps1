#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArtifactsPath,
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit,
    [Parameter(Mandatory)][ValidateSet('2021.3.45f1','6000.6.0f1')][string]$UnityVersion
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Require-Hash {
    param([string]$Path,[string]$Expected)
    if ($Expected -notmatch '^[0-9a-f]{64}$' -or (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Expected) { throw "Acceptance artifact hash differs: $Path" }
}
$root = [IO.Path]::GetFullPath($ArtifactsPath)
$declaration = Get-Content -LiteralPath (Join-Path $root 'declaration.json') -Raw | ConvertFrom-Json
if ($declaration.commit -ne $Commit -or $declaration.unityVersion -ne $UnityVersion -or $declaration.runToken -notmatch '^[0-9a-f]{32}$') { throw 'Independent consumer candidate/version/run binding differs' }
Require-Hash (Join-Path $root 'candidate-source.SHA256SUMS') $declaration.sourceManifest
Require-Hash (Join-Path $root 'generated-ConsumerBinding.cs') $declaration.generatedBindingSHA256
$binding = [IO.File]::ReadAllText((Join-Path $root 'generated-ConsumerBinding.cs'))
foreach ($value in @($Commit,$UnityVersion,$declaration.sourceManifest,$declaration.runToken)) {
    if (-not $binding.Contains('"'+$value+'"')) { throw 'Generated binding omits declared candidate value' }
}
$repoPath = [IO.Path]::GetFullPath($Repository)
$currentCommit = (& git -C $repoPath rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $currentCommit -ne $Commit) { throw 'Verifier checkout commit differs' }
$rows = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
foreach ($line in [IO.File]::ReadAllLines((Join-Path $root 'candidate-source.SHA256SUMS'))) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$' -or $Matches[2].Contains('..') -or -not $rows.TryAdd($Matches[2],$Matches[1])) { throw 'Malformed or duplicated manifest input' }
}
$expectedPackage = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($folder in @('Runtime','Editor','Generator~')) {
    foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $repoPath $folder) -File -Recurse)) {
        if ($file.FullName -match '[\\/](?:bin|obj|\.git)[\\/]') { continue }
        $label = 'package/' + [IO.Path]::GetRelativePath($repoPath,$file.FullName).Replace('\','/')
        $expectedPackage.Add($label) | Out-Null
        if (-not $rows.ContainsKey($label)) { throw 'Current checkout package input missing from bound manifest' }
        Require-Hash $file.FullName $rows[$label]
    }
}
$expectedPackage.Add('package/package.json') | Out-Null
if (-not $rows.ContainsKey('package/package.json')) { throw 'Current package.json absent from bound manifest' }
Require-Hash (Join-Path $repoPath 'package.json') $rows['package/package.json']
if (@($rows.Keys | Where-Object { $_.StartsWith('package/',[StringComparison]::Ordinal) }).Count -ne $expectedPackage.Count) { throw 'Current checkout package inventory differs from manifest' }
$tools = @('run-nullable-consumer.ps1','run-nullable-consumer-player-matrix.ps1','verify-nullable-consumer.ps1','lib/il2cpp-conversion-inputs.ps1','run-acceptance.ps1','verify-acceptance.ps1','resolve-test-matrix.js')
foreach ($relative in $tools) {
    $label = 'acceptance-tools/' + $relative
    if (-not $rows.ContainsKey($label)) { throw 'Current acceptance tool missing from bound manifest' }
    Require-Hash (Join-Path $repoPath ('scripts/unity/'+$relative)) $rows[$label]
}
if (@($rows.Keys | Where-Object { $_.StartsWith('acceptance-tools/',[StringComparison]::Ordinal) }).Count -ne $tools.Count) { throw 'Current acceptance tool inventory differs' }
$configs = @('.github/unity-versions.json','.github/unity-test-project-modules.json')
foreach ($relative in $configs) {
    $label = 'acceptance-config/'+$relative
    if (-not $rows.ContainsKey($label)) { throw 'Current acceptance configuration missing from bound manifest' }
    Require-Hash (Join-Path $repoPath $relative) $rows[$label]
}
if (@($rows.Keys | Where-Object { $_.StartsWith('acceptance-config/',[StringComparison]::Ordinal) }).Count -ne $configs.Count) { throw 'Current acceptance configuration inventory differs' }
$template = Join-Path $repoPath 'Acceptance~/NullableMapConsumer'
$goldens = @(Get-ChildItem -LiteralPath (Join-Path $template 'Goldens~') -File)
foreach ($file in $goldens) {
    $label = 'acceptance-goldens/'+$file.Name
    if (-not $rows.ContainsKey($label)) { throw 'Current golden absent from bound manifest' }
    Require-Hash $file.FullName $rows[$label]
    Require-Hash (Join-Path $root ('goldens/'+$file.Name)) $rows[$label]
}
if (@($rows.Keys | Where-Object { $_.StartsWith('acceptance-goldens/',[StringComparison]::Ordinal) }).Count -ne $goldens.Count) { throw 'Current golden inventory differs' }
$consumerArchive = Join-Path $root 'consumer-inputs'
foreach ($label in $rows.Keys) {
    if ($label.StartsWith('package/',[StringComparison]::Ordinal) -or $label.StartsWith('acceptance-tools/',[StringComparison]::Ordinal) -or $label.StartsWith('acceptance-goldens/',[StringComparison]::Ordinal) -or $label.StartsWith('acceptance-config/',[StringComparison]::Ordinal)) { continue }
    if ($label -notmatch '^(Assets|Packages|ProjectSettings)/') { throw 'Unknown bound input category' }
    Require-Hash (Join-Path $consumerArchive $label) $rows[$label]
}
$currentAssetLabels = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $template 'Assets') -File -Recurse)) {
    if ($file.Name -eq 'ConsumerBinding.cs') { continue }
    $label = [IO.Path]::GetRelativePath($template,$file.FullName).Replace('\','/')
    $currentAssetLabels.Add($label) | Out-Null
    if (-not $rows.ContainsKey($label)) { throw 'Current consumer source/link input absent from bound manifest' }
    Require-Hash $file.FullName $rows[$label]
}
foreach ($label in $rows.Keys) {
    if (-not $label.StartsWith('Assets/',[StringComparison]::Ordinal)) { continue }
    if ($label -in @('Assets/ConsumerScene.unity','Assets/ConsumerScene.unity.meta')) { continue }
    if (-not $currentAssetLabels.Contains($label)) { throw 'Current consumer source/link inventory lost a bound input' }
}
$currentBinding = [IO.File]::ReadAllText((Join-Path $template 'Assets/Consumer/ConsumerBinding.cs')).Replace('__UNITY__',$UnityVersion).Replace('__COMMIT__',$Commit).Replace('__SOURCE__',$declaration.sourceManifest).Replace('__TOKEN__',$declaration.runToken)
$currentBindingSHA = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($currentBinding))).ToLowerInvariant()
if ($currentBindingSHA -ne $declaration.generatedBindingSHA256) { throw 'Current generated-binding recipe differs' }
$currentDependencies = (Get-Content -LiteralPath (Join-Path $template 'Packages/manifest.json') -Raw | ConvertFrom-Json)
$currentDependencies.dependencies.'com.wallstop-studios.unity-helpers' = 'file:' + $repoPath.Replace('\','/')
$moduleConfiguration = Get-Content -LiteralPath (Join-Path $repoPath '.github/unity-test-project-modules.json') -Raw | ConvertFrom-Json
foreach ($module in $moduleConfiguration.modules.PSObject.Properties) { $currentDependencies.dependencies | Add-Member -NotePropertyName $module.Name -NotePropertyValue $module.Value -Force }
$archivedDependencies = Get-Content -LiteralPath (Join-Path $consumerArchive 'Packages/manifest.json') -Raw | ConvertFrom-Json
if (($currentDependencies | ConvertTo-Json -Depth 10 -Compress) -ne ($archivedDependencies | ConvertTo-Json -Depth 10 -Compress)) { throw 'Current consumer dependency manifest differs' }
$playerRoot = Join-Path $root 'player'
Require-Hash (Join-Path $playerRoot 'IndependentConsumer.exe') $declaration.executableSHA256
$settings = [IO.File]::ReadAllText((Join-Path $playerRoot 'settings.txt'))
foreach ($line in @("unity=$UnityVersion",'backend=IL2CPP','stripping=High','compiler=Release','development=False')) {
    if ($settings -notmatch ('(?m)^'+[regex]::Escape($line)+'$')) { throw "Independent player setting missing: $line" }
}
$toolRoot = Join-Path $root 'actual-tool-inputs'
$responses = @(Get-Content -LiteralPath (Join-Path $toolRoot 'unity-linker-response-inputs.json') -Raw | ConvertFrom-Json)
if ($responses.Count -eq 0 -or $responses.Count -ne $declaration.actualLinkerInputCount) { throw 'Actual linker response count differs' }
foreach ($response in $responses) { Require-Hash (Join-Path $toolRoot "$($response.sha256).rsp") $response.sha256 }
$xmlInputs = @(Get-Content -LiteralPath (Join-Path $toolRoot 'actual-linker-xml-inputs.json') -Raw | ConvertFrom-Json)
if ($xmlInputs.Count -eq 0 -or $xmlInputs.Count -ne $declaration.actualLinkerXmlInputCount) { throw 'Actual linker XML count differs' }
$presenceFound = $false
foreach ($xmlInput in $xmlInputs) {
    if (@($responses | Where-Object { $_.sha256 -eq $xmlInput.responseSHA256 }).Count -eq 0) { throw 'Linker descriptor has no corresponding actual response' }
    Require-Hash (Join-Path $toolRoot "$($xmlInput.sha256).xml") $xmlInput.sha256
    $content = [IO.File]::ReadAllText((Join-Path $toolRoot "$($xmlInput.sha256).xml"))
    if ($content.Contains('NestedOnlyConsumer.PresenceDoc')) { $presenceFound = $true }
}
if (-not $presenceFound) { throw 'Actual linker descriptor did not preserve consumer contract' }
$conversions = @(Get-Content -LiteralPath (Join-Path $toolRoot 'il2cpp-conversion-modules.json') -Raw | ConvertFrom-Json)
if ($conversions.Count -eq 0) { throw 'Actual converter input set missing' }
$required = @('NestedOnly.Consumer.dll','WallstopStudios.UnityHelpers.dll','protobuf-net.dll','protobuf-net.Core.dll')
foreach ($conversion in $conversions) {
    $invocation = $conversion.invocation
    if ($invocation.executable -notmatch '(?:^|[\\/])il2cpp(?:\.exe)?$') { throw 'Recorded invocation is not the converter executable' }
    $commandSHA = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($invocation.command))).ToLowerInvariant()
    if ($commandSHA -ne $invocation.commandSHA256) { throw 'Actual converter command hash differs' }
    foreach ($response in $invocation.responseChain) { Require-Hash (Join-Path $toolRoot "$($response.sha256).converter.rsp") $response.sha256 }
    $moduleRoot = Join-Path $toolRoot ("converter-inputs/"+$commandSHA)
    foreach ($module in $required) {
        $entries = @($conversion.modules | Where-Object { $_.module -eq $module })
        if ($entries.Count -ne 1) { throw 'Required module absent/duplicated in single actual conversion input set' }
        Require-Hash (Join-Path $moduleRoot $module) $entries[0].sha256
    }
}
$expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($shape in @('null','empty','populated')) {
    foreach ($mode in @('typed-hash','object-hash','typed-equality','object-equality','serialize-read')) { $expected.Add("$mode/$shape") | Out-Null }
}
$expected.Add('merge/populated') | Out-Null
foreach ($mode in @('presence-typed-hash','presence-object-hash','presence-typed-equality','presence-object-equality','presence-serialize-read','presence-foreign')) { $expected.Add("$mode/presence") | Out-Null }
$matrix = @(Get-Content -LiteralPath (Join-Path $root 'fresh-processes/matrix.json') -Raw | ConvertFrom-Json)
if ($matrix.Count -ne 22) { throw 'Expected exactly22 fresh processes' }
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($case in $matrix) {
    $key = "$($case.mode)/$($case.shape)"
    if (-not $expected.Contains($key) -or -not $seen.Add($key)) { throw 'Unexpected or duplicated cold consumer operation' }
    $exact = "Passed|$($case.mode)|$($case.shape)|$UnityVersion|IL2CPP=True|commit=$Commit|source=$($declaration.sourceManifest)|run=$($declaration.runToken)"
    $stem = "$($case.mode)-$($case.shape)"
    $actual = [IO.File]::ReadAllText((Join-Path $root "fresh-processes/$stem.result.txt"))
    if ($case.record -ne $exact -or $actual -ne $exact -or $case.executableSHA256 -ne $declaration.executableSHA256 -or $case.sourceManifest -ne $declaration.sourceManifest -or $case.candidateCommit -ne $Commit -or $case.runToken -ne $declaration.runToken -or $case.unityVersion -ne $UnityVersion) { throw 'Fresh-process record does not bind exact requested player' }
    foreach ($suffix in @('stdout.txt','stderr.txt','player.log')) {
        if (-not (Test-Path -LiteralPath (Join-Path $root "fresh-processes/$stem.$suffix") -PathType Leaf)) { throw 'Fresh-process raw log missing' }
    }
    Require-Hash (Join-Path $root "goldens/$($case.shape).base64") $case.goldenSHA256.ToLowerInvariant()
}
@{ nullableconsumer = 'passed'; processes = 22; commit = $Commit; unityVersion = $UnityVersion; sourceManifest = $declaration.sourceManifest; verifiedCheckoutCommit = $currentCommit; verifiedPackageInputs = $expectedPackage.Count; verifiedAcceptanceTools = $tools.Count; verifiedGoldens = $goldens.Count; executableSHA256 = $declaration.executableSHA256; converterInputSets = $conversions.Count } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'acceptance-summary.json')
