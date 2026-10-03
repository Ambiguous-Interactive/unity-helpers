#!/usr/bin/env pwsh
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot '../unity/run-ci-tests.ps1'
$source = Get-Content -LiteralPath $runner -Raw
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'Unity runner must parse.' }
foreach ($name in @('Assert-FrozenPlayerRegularPath', 'Get-FrozenPlayerFileInventory', 'Read-FrozenPlayerDeclaration', 'Start-FrozenPlayerProvenance',
        'New-FrozenPlayerAnchor', 'Get-FrozenPlayerRuntimeJoin', 'Add-FrozenPlayerProvenanceSnapshot', 'Get-FrozenPlayerJointCorrectness', 'Complete-FrozenPlayerProvenance')) {
    $function = $ast.Find({ param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
        }, $true)
    if ($null -eq $function) { throw "Missing production function $name." }
    Invoke-Expression $function.Extent.Text
}
$checks = 0
function Assert-Control {
    param([bool]$Passed, [string]$Name)
    if (-not $Passed) { throw "Control failed: $Name" }
    $script:checks++
}
function Assert-Rejected {
    param([scriptblock]$Action, [string]$Name, [string]$Message = '')
    $observed = $null
    try { & $Action | Out-Null } catch { $observed = $_.ToString() }
    Assert-Control ($null -ne $observed -and ($Message -eq '' -or $observed.Contains($Message))) $Name
}
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('frozen player controls ' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
function New-DeclarationFixture {
    param([string]$RunId)
    $root = Join-Path $temporary $RunId
    New-Item -ItemType Directory -Path $root | Out-Null
    $payload = Join-Path $root 'payload'
    New-Item -ItemType Directory -Path (Join-Path $payload 'Player_Data') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $payload 'player.exe'), 'executable')
    [IO.File]::WriteAllText((Join-Path $payload 'Player_Data/native.dll'), 'native')
    [IO.File]::WriteAllText((Join-Path $root 'source with spaces.cs'), 'frozen source')
    [IO.File]::WriteAllText((Join-Path $root 'corpus.json'), 'frozen corpus declaration')
    $manifest = [ordered]@{
        SchemaVersion = 1; RunId = $RunId; UnityVersion = '6000.6.0f1'; Backend = 'Mono2x'
        SourceFiles = @(@{ Path = 'source with spaces.cs'; Sha256 = (Get-FileHash -LiteralPath (Join-Path $root 'source with spaces.cs')).Hash.ToLowerInvariant() })
        CorpusFiles = @(@{ Path = 'corpus.json'; Sha256 = (Get-FileHash -LiteralPath (Join-Path $root 'corpus.json')).Hash.ToLowerInvariant() })
    }
    $path = Join-Path $root 'declaration.json'
    [IO.File]::WriteAllText($path, (ConvertTo-Json -InputObject $manifest -Depth 5))
    return [pscustomobject]@{ Root = $root; Payload = $payload; Manifest = $manifest; Path = $path }
}
function Read-ControlDeclaration {
    param($Fixture)
    Read-FrozenPlayerDeclaration -Path $Fixture.Path -Mode standalone -UnityVersion '6000.6.0f1' -Backend Mono2x -ArtifactsPath $Fixture.Root -RepoRoot $Fixture.Root
}
function Start-ControlContext {
    param($Fixture)
    $declaration = Read-ControlDeclaration $Fixture
    $context = Start-FrozenPlayerProvenance -Declaration $declaration -RepoRoot $Fixture.Root -PayloadRoot $Fixture.Payload
    if ($null -ne $declaration.CanonicalCorpus) { New-FrozenPlayerAnchor -Context $context -ProjectPath (Join-Path $Fixture.Root 'generated project') }
    Add-FrozenPlayerProvenanceSnapshot -Context $context -Stage 'before-configure'
    Add-FrozenPlayerProvenanceSnapshot -Context $context -Stage 'after-configure'
    Add-FrozenPlayerProvenanceSnapshot -Context $context -Stage 'after-build' -IncludePayload
    return $context
}
$requiredNames = @(
    'WallstopStudios.UnityHelpers.Tests.Runtime.Performance.BorrowedBufferPreflightTests.ReleasePlayerChannelsAreReported',
    'WallstopStudios.UnityHelpers.Tests.Extensions.BorrowedBase64Tests.BorrowedDecoderMatchesShippedDecoderAcrossPoolBoundariesAndUtf8Failures',
    'WallstopStudios.UnityHelpers.Tests.Extensions.BorrowedBase64Tests.BorrowedWholeCallAvoidsLeaseWorkWithShippedPositiveControl'
)
function Write-ControlResults {
    param([string]$Path, [string[]]$Names = $requiredNames, [string]$LeafResult = 'Passed', [string]$RootResult = 'Passed', [int]$RootPassed = 3, [string]$Extra = '')
    $leaves = ($Names | ForEach-Object { "<test-case fullname=`"$_`" result=`"$LeafResult`"/>" }) -join ''
    [IO.File]::WriteAllText($Path, "<test-run result=`"$RootResult`" testcasecount=`"3`" total=`"3`" passed=`"$RootPassed`" failed=`"0`" skipped=`"0`" inconclusive=`"0`">$leaves$Extra</test-run>")
}
function New-CanonicalFixture {
    param([string]$RunId)
    $fixture = New-DeclarationFixture $RunId
    $fixture.Manifest.CanonicalCorpusPath = 'corpus.json'
    [IO.File]::WriteAllText($fixture.Path, (ConvertTo-Json -InputObject $fixture.Manifest -Depth 5))
    $metaDirectory = Join-Path $fixture.Root 'Tests/Runtime/Performance'
    New-Item -ItemType Directory -Path $metaDirectory -Force | Out-Null
    [IO.File]::Copy((Join-Path $PSScriptRoot '../../Tests/Runtime/Performance/WallstopStudios.UnityHelpers.Tests.Runtime.Performance.asmdef.meta'),
        (Join-Path $metaDirectory 'WallstopStudios.UnityHelpers.Tests.Runtime.Performance.asmdef.meta'))
    New-Item -ItemType Directory -Path (Join-Path $fixture.Root 'generated project/Assets') -Force | Out-Null
    return $fixture
}
function New-RuntimeRecords {
    param($Context)
    $preflight = [ordered]@{
        SchemaVersion = 1; FrozenRunId = $Context.Declaration.Value.RunId
        FrozenDeclarationSha256 = $Context.Declaration.Sha256
        FrozenCorpusSha256 = $Context.Declaration.CanonicalCorpus.Sha256
        ActualCorpusSha256 = $Context.Declaration.CanonicalCorpus.Sha256
        RuntimeIdentity = [ordered]@{ UnityVersion = '6000.6.0f1'; Backend = 'Mono'; Platform = 'WindowsPlayer'; ProcessBits = 64; IsEditor = $false; DevelopmentBuild = $false }
    }
    $correctness = [ordered]@{ SchemaVersion = 1; CorpusSha256 = $Context.Declaration.CanonicalCorpus.Sha256; VerifiedRows = 35; DecoderAssertions = 70; DeclaredCampaignRows = 12 }
    return [pscustomobject]@{ Preflight = $preflight; Correctness = $correctness }
}
function Write-RuntimeRecords {
    param([string]$Path, $Records, [switch]$CorrectnessFirst)
    $preflight = 'BORROWED_PREFLIGHT ' + (ConvertTo-Json -InputObject $Records.Preflight -Depth 5 -Compress)
    $correctness = 'BORROWED_BASE64_CORPUS ' + (ConvertTo-Json -InputObject $Records.Correctness -Compress)
    $lines = if ($CorrectnessFirst) { @($correctness, $preflight) } else { @($preflight, $correctness) }
    [IO.File]::WriteAllText($Path, ($lines -join "`n") + "`n")
}
try {
    $fixture = New-DeclarationFixture 'positive'
    $context = Start-ControlContext $fixture
    Add-FrozenPlayerProvenanceSnapshot -Context $context -Stage 'before-launch' -IncludePayload
    $context.LaunchAttempted = $true
    $context.InvocationObserved = $true
    $context.ResultsValidated = $true
    $context.PlayerOutcome = @{ ExitCode = 0; TimedOut = $false }
    $results = Join-Path $fixture.Root 'results.xml'
    Write-ControlResults $results
    Complete-FrozenPlayerProvenance -Context $context -ResultsPath $results -EvidencePaths @($results)
    $report = Get-Content -LiteralPath $context.ReportPath -Raw | ConvertFrom-Json
    Assert-Control ($report.ProvenanceStableState -eq 'passed' -and $report.JointCorrectness.State -eq 'passed') 'stable payload and exactly three correct cases'
    Assert-Control (-not $report.CampaignEligible -and -not $report.AdoptionEligible -and
        $report.SourceCoverageState -eq 'unverified' -and $report.CorpusConsumptionState -eq 'unverified' -and
        $report.RuntimeRecordJoinState -eq 'unverified' -and $report.RetainedMemoryState -eq 'unverified') 'provenance cannot qualify the campaign'
    Assert-Control ($report.Snapshots.Count -eq 5 -and $report.Snapshots[2].PayloadFiles.Count -eq 3) 'all snapshots include complete files and directories'
    Assert-Control ([Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $context.Declaration.ReportDirectory 'declaration.json'))) -ceq
        [Convert]::ToBase64String($context.Declaration.Bytes)) 'exact declaration bytes are frozen'
    $originalReport = [IO.File]::ReadAllText($context.ReportPath)
    Assert-Rejected { Read-ControlDeclaration $fixture } 'duplicate RunId rejected before build' 'already has an output directory'
    Assert-Control ([IO.File]::ReadAllText($context.ReportPath) -ceq $originalReport) 'duplicate RunId cannot overwrite evidence'

    foreach ($mode in @('editmode', 'playmode', 'export')) {
        $fixture = New-DeclarationFixture "mode-$mode"
        Assert-Rejected { Read-FrozenPlayerDeclaration -Path $fixture.Path -Mode $mode -UnityVersion '6000.6.0f1' -Backend Mono2x -ArtifactsPath $fixture.Root -RepoRoot $fixture.Root } "reject $mode opt-in" 'executed standalone'
    }
    $fixture = New-DeclarationFixture 'generate-only'
    Assert-Rejected { Read-FrozenPlayerDeclaration -Path $fixture.Path -Mode standalone -GenerateOnly -UnityVersion '6000.6.0f1' -Backend Mono2x -ArtifactsPath $fixture.Root -RepoRoot $fixture.Root } 'reject generate-only opt-in'
    foreach ($invalid in @('malformed', 'run-id', 'schema', 'version', 'backend', 'empty-source', 'empty-corpus')) {
        $fixture = New-DeclarationFixture "declaration-$invalid"
        switch ($invalid) {
            'run-id' { $fixture.Manifest.RunId = '../escape' }
            'schema' { $fixture.Manifest.SchemaVersion = 2 }
            'version' { $fixture.Manifest.UnityVersion = '6000.4.6f1' }
            'backend' { $fixture.Manifest.Backend = 'IL2CPP' }
            'empty-source' { $fixture.Manifest.SourceFiles = @() }
            'empty-corpus' { $fixture.Manifest.CorpusFiles = @() }
        }
        [IO.File]::WriteAllText($fixture.Path, $(if ($invalid -eq 'malformed') { '{invalid' } else { ConvertTo-Json -InputObject $fixture.Manifest -Depth 5 }))
        Assert-Rejected { Read-ControlDeclaration $fixture } "reject $invalid declaration"
    }
    foreach ($invalid in @('traversal', 'rooted', 'duplicate', 'case-collision', 'wrong-sha', 'missing-file', 'directory', 'trailing-dot', 'trailing-space')) {
        $fixture = New-DeclarationFixture "inputs-$invalid"
        $entry = $fixture.Manifest.SourceFiles[0]
        switch ($invalid) {
            'traversal' { $entry.Path = '../outside.cs' }
            'rooted' { $entry.Path = '/outside.cs' }
            'duplicate' { $fixture.Manifest.SourceFiles += $entry }
            'case-collision' { $fixture.Manifest.SourceFiles += @{ Path = 'SOURCE WITH SPACES.CS'; Sha256 = $entry.Sha256 } }
            'wrong-sha' { $entry.Sha256 = '0' * 64 }
            'missing-file' { $entry.Path = 'missing.cs' }
            'directory' { $entry.Path = 'payload' }
            'trailing-dot' { $entry.Path = 'source with spaces.cs.' }
            'trailing-space' { $entry.Path = 'source with spaces.cs ' }
        }
        Assert-Rejected { Get-FrozenPlayerFileInventory -Root $fixture.Root -DeclaredFiles $fixture.Manifest.SourceFiles } "reject $invalid input inventory"
    }
    foreach ($change in @('source', 'corpus', 'declaration-whitespace', 'declaration-missing', 'payload-added', 'payload-deleted', 'payload-same-size', 'payload-empty-directory')) {
        $fixture = New-DeclarationFixture "change-$change"
        $context = Start-ControlContext $fixture
        switch ($change) {
            'source' { [IO.File]::WriteAllText((Join-Path $fixture.Root 'source with spaces.cs'), 'changed source') }
            'corpus' { [IO.File]::WriteAllText((Join-Path $fixture.Root 'corpus.json'), 'changed corpus') }
            'declaration-whitespace' { [IO.File]::AppendAllText($fixture.Path, ' ') }
            'declaration-missing' { Remove-Item -LiteralPath $fixture.Path }
            'payload-added' { [IO.File]::WriteAllText((Join-Path $fixture.Payload 'new.dll'), 'new') }
            'payload-deleted' { Remove-Item -LiteralPath (Join-Path $fixture.Payload 'Player_Data/native.dll') }
            'payload-same-size' { [IO.File]::WriteAllText((Join-Path $fixture.Payload 'Player_Data/native.dll'), 'NATIVE') }
            'payload-empty-directory' { New-Item -ItemType Directory -Path (Join-Path $fixture.Payload 'new empty folder') | Out-Null }
        }
        Assert-Rejected { Add-FrozenPlayerProvenanceSnapshot -Context $context -Stage 'before-launch' -IncludePayload } "detect $change before launch"
        $context.ExecutionError = "retained primary error for $change"
        Complete-FrozenPlayerProvenance -Context $context -ResultsPath (Join-Path $fixture.Root 'missing.xml') -EvidencePaths @($fixture.Path)
        $report = Get-Content -LiteralPath $context.ReportPath -Raw | ConvertFrom-Json
        Assert-Control ($report.ProvenanceStableState -eq 'rejected' -and $report.ExecutionError -eq $context.ExecutionError -and
            -not $report.InvocationObserved -and $report.Snapshots[-1].PayloadFiles.Count -gt 0) "preserve $change rejection and final payload on original failure"
    }
    $fixture = New-DeclarationFixture 'post-launch'
    $context = Start-ControlContext $fixture
    $context.LaunchAttempted = $true
    $context.PlayerOutcome = @{ ExitCode = 124; TimedOut = $true }
    $context.InvocationObserved = $true
    $context.ResultsValidated = $true
    [IO.File]::WriteAllText((Join-Path $fixture.Payload 'player.exe'), 'mutatedexe')
    $results = Join-Path $fixture.Root 'results.xml'
    Write-ControlResults $results
    Assert-Rejected { Complete-FrozenPlayerProvenance -Context $context -ResultsPath $results -EvidencePaths @($results) } 'detect payload change after watchdog termination'
    $report = Get-Content -LiteralPath $context.ReportPath -Raw | ConvertFrom-Json
    Assert-Control ($report.PlayerOutcome.TimedOut -and $report.PlayerOutcome.ExitCode -eq 124 -and $report.EvidenceFiles[0].Sha256.Length -eq 64) 'preserve actual timeout and result digest'

    $fixture = New-DeclarationFixture 'changed-reservation'
    $context = Start-ControlContext $fixture
    $context.ExecutionError = 'existing primary failure'
    [IO.File]::WriteAllText($context.ReportPath, 'external contents')
    Assert-Rejected { Complete-FrozenPlayerProvenance -Context $context -ResultsPath (Join-Path $fixture.Root 'missing.xml') -EvidencePaths @($fixture.Path) } 'reject changed reservation contents' 'refusing to overwrite'
    Assert-Control ([IO.File]::ReadAllText($context.ReportPath) -ceq 'external contents') 'never overwrite unexpected existing report'
    $emptyPayload = Join-Path $temporary 'directory-only payload'
    New-Item -ItemType Directory -Path (Join-Path $emptyPayload 'empty') -Force | Out-Null
    Assert-Rejected { Get-FrozenPlayerFileInventory -Root $emptyPayload } 'reject directories-only payload' 'no files'
    $fixture = New-DeclarationFixture 'reparse-controls'
    $context = Start-ControlContext $fixture
    $target = Join-Path $fixture.Root 'linked target'
    New-Item -ItemType Directory -Path $target | Out-Null
    [IO.File]::WriteAllText((Join-Path $target 'file.txt'), 'source')
    $link = Join-Path $fixture.Root 'linked folder'
    $linkKind = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { 'Junction' } else { 'SymbolicLink' }
    New-Item -ItemType $linkKind -Path $link -Target $target -ErrorAction Stop | Out-Null
    Assert-Rejected { Get-FrozenPlayerFileInventory -Root $fixture.Root -DeclaredFiles @(@{ Path = 'linked folder/file.txt'; Sha256 = (Get-FileHash -LiteralPath (Join-Path $target 'file.txt')).Hash.ToLowerInvariant() }) } 'reject reparse source ancestor' 'reparse'
    New-Item -ItemType $linkKind -Path (Join-Path $fixture.Payload 'linked folder') -Target $target -ErrorAction Stop | Out-Null
    Assert-Rejected { Add-FrozenPlayerProvenanceSnapshot -Context $context -Stage 'before-launch' -IncludePayload } 'reject payload reparse point' 'reparse'
    # Same declaration bytes behind a newly substituted linked ancestor must reject.
    $fixture = New-DeclarationFixture 'declaration-reparse'
    $container = Join-Path $fixture.Root 'declaration folder'
    New-Item -ItemType Directory -Path $container | Out-Null
    $newPath = Join-Path $container 'declaration.json'
    Move-Item -LiteralPath $fixture.Path -Destination $newPath
    $fixture.Path = $newPath
    $context = Start-ControlContext $fixture
    $moved = Join-Path $fixture.Root 'original declaration folder'
    Move-Item -LiteralPath $container -Destination $moved
    New-Item -ItemType $linkKind -Path $container -Target $moved -ErrorAction Stop | Out-Null
    Assert-Rejected { Add-FrozenPlayerProvenanceSnapshot -Context $context -Stage 'after-configure' } 'reject same-byte declaration reparse substitution' 'reparse'

    $fixture = New-DeclarationFixture 'joint-correctness'
    $results = Join-Path $fixture.Root 'results.xml'
    foreach ($kind in @('missing', 'malformed', 'skipped', 'failed', 'missing-leaf', 'extra-leaf', 'duplicate', 'wrong-identity', 'failed-root', 'bad-counts', 'failed-suite', 'missing-suite-status', 'bad-case-count', 'dtd')) {
        Write-ControlResults $results
        switch ($kind) {
            'missing' { Remove-Item -LiteralPath $results }
            'malformed' { [IO.File]::WriteAllText($results, '<test-run') }
            'skipped' { Write-ControlResults $results -LeafResult Skipped }
            'failed' { Write-ControlResults $results -LeafResult Failed }
            'missing-leaf' { Write-ControlResults $results -Names $requiredNames[0..1] }
            'extra-leaf' { Write-ControlResults $results -Names ($requiredNames + 'unexpected.method') }
            'duplicate' { Write-ControlResults $results -Names @($requiredNames[0], $requiredNames[0], $requiredNames[2]) }
            'wrong-identity' { Write-ControlResults $results -Names @('wrong.method', $requiredNames[1], $requiredNames[2]) }
            'failed-root' { Write-ControlResults $results -RootResult Failed }
            'bad-counts' { Write-ControlResults $results -RootPassed 4 }
            'failed-suite' { Write-ControlResults $results -Extra '<test-suite result="Failed"><failure><message>teardown failed</message></failure></test-suite>' }
            'missing-suite-status' { Write-ControlResults $results -Extra '<test-suite/>' }
            'bad-case-count' { [IO.File]::WriteAllText($results, [IO.File]::ReadAllText($results).Replace('testcasecount="3"', 'testcasecount="4"')) }
            'dtd' { [IO.File]::WriteAllText($results, '<!DOCTYPE test-run [<!ENTITY x "x">]><test-run/>') }
        }
        $joint = Get-FrozenPlayerJointCorrectness -ResultsPath $results
        Assert-Control ($joint.State -eq 'rejected' -and $joint.Reason) "reject $kind joint correctness"
    }
    $oversized = [IO.File]::Create($results)
    try { $oversized.SetLength(33554433) } finally { $oversized.Dispose() }
    Assert-Control ((Get-FrozenPlayerJointCorrectness -ResultsPath $results).State -eq 'rejected') 'reject oversized XML before parsing'
    foreach ($path in @('', 'CORPUS.JSON', 'source with spaces.cs', '../corpus.json')) {
        $fixture = New-CanonicalFixture ('canonical-invalid-' + [Guid]::NewGuid().ToString('N'))
        $fixture.Manifest.CanonicalCorpusPath = $path
        [IO.File]::WriteAllText($fixture.Path, (ConvertTo-Json -InputObject $fixture.Manifest -Depth 5))
        Assert-Rejected { Read-ControlDeclaration $fixture } "reject unlisted or nonexact canonical path '$path'" 'exactly identify'
    }
    foreach ($order in @('preflight-first', 'correctness-first')) {
        $fixture = New-CanonicalFixture "canonical-$order"
        $context = Start-ControlContext $fixture
        $anchorSource = Join-Path $context.GeneratedRoot 'Assets/FrozenPlayerAnchor/BorrowedBufferPreflightTests.Anchor.cs'
        $anchorReference = Join-Path $context.GeneratedRoot 'Assets/FrozenPlayerAnchor/FrozenPlayerAnchor.asmref'
        Assert-Control (([IO.File]::ReadAllText($anchorSource)).Contains('static partial void ReadFrozenAnchor(') -and
            ([IO.File]::ReadAllText($anchorSource)).Contains($context.Declaration.Sha256) -and
            ([IO.File]::ReadAllText($anchorSource)).Contains($context.Declaration.CanonicalCorpus.Sha256) -and
            ((Get-Content -LiteralPath $anchorReference -Raw | ConvertFrom-Json).reference -ceq 'GUID:e8f3b866a545477f99bc5729d9d6a7f7')) 'compiled literals and exact assembly reference are generated'
        Assert-Control ($context.Snapshots[0].GeneratedAnchorFiles.Count -eq 5) 'anchor source and metadata hashes are frozen before configure'
        Assert-Rejected { New-FrozenPlayerAnchor -Context $context -ProjectPath $context.GeneratedRoot } 'existing implementation cannot be overwritten' 'fresh project scope'
        $context.LaunchAttempted = $true
        $context.InvocationObserved = $true
        $context.ResultsValidated = $true
        $context.PlayerOutcome = @{ ExitCode = 0; TimedOut = $false }
        $results = Join-Path $fixture.Root 'results.xml'
        $log = Join-Path $fixture.Root 'player.log'
        Write-ControlResults $results
        $records = New-RuntimeRecords $context
        Write-RuntimeRecords $log $records -CorrectnessFirst:($order -eq 'correctness-first')
        Complete-FrozenPlayerProvenance -Context $context -ResultsPath $results -EvidencePaths @($results, $log) -PlayerLogPath $log
        $report = Get-Content -LiteralPath $context.ReportPath -Raw | ConvertFrom-Json
        Assert-Control ($report.RuntimeRecordJoinState -eq 'passed' -and $report.CorrectnessCorpusConsumptionState -eq 'passed' -and
            $report.RuntimeJoin.VerifiedCorrectnessRows -eq 35 -and $report.RuntimeJoin.DeclaredCampaignRows -eq 12 -and
            $report.CampaignCorpusConsumptionState -eq 'unverified' -and -not $report.CampaignEligible -and
            $report.RuntimeJoin.RawRecords.Count -eq 2) "host-derived runtime/correctness join is order independent: $order"
    }
    $fixture = New-CanonicalFixture 'runtime-rejections'
    $context = Start-ControlContext $fixture
    $records = New-RuntimeRecords $context
    $log = Join-Path $fixture.Root 'player.log'
    foreach ($kind in @('missing-log', 'missing-preflight', 'missing-correctness', 'duplicate-preflight', 'duplicate-correctness',
            'conflicting-preflight', 'malformed', 'truncated', 'duplicate-key', 'wrong-run', 'wrong-declaration', 'wrong-anchor-corpus',
            'wrong-actual-corpus', 'wrong-version', 'wrong-backend', 'wrong-platform', 'editor', 'development', 'process32',
            'string-editor', 'string-schema', 'float-schema', 'bool-schema', 'string-count', 'float-count', 'bool-count',
            'string-process-bits', 'float-process-bits', 'bool-process-bits', 'missing-identity', 'array-run-id', 'array-runtime-version',
            'array-anchor-digest', 'array-correctness-digest', 'escaped-duplicate-key', 'embedded-prefix', 'deep-json', 'wrong-correctness-digest', 'wrong-row-count', 'wrong-assertion-count', 'wrong-campaign-count')) {
        $changed = New-RuntimeRecords $context
        switch ($kind) {
            'wrong-run' { $changed.Preflight.FrozenRunId = 'another-run' }
            'wrong-declaration' { $changed.Preflight.FrozenDeclarationSha256 = '0' * 64 }
            'wrong-anchor-corpus' { $changed.Preflight.FrozenCorpusSha256 = '0' * 64 }
            'wrong-actual-corpus' { $changed.Preflight.ActualCorpusSha256 = '0' * 64 }
            'wrong-version' { $changed.Preflight.RuntimeIdentity.UnityVersion = '6000.4.6f1' }
            'wrong-backend' { $changed.Preflight.RuntimeIdentity.Backend = 'Mono2x' }
            'wrong-platform' { $changed.Preflight.RuntimeIdentity.Platform = 'WindowsEditor' }
            'editor' { $changed.Preflight.RuntimeIdentity.IsEditor = $true }
            'development' { $changed.Preflight.RuntimeIdentity.DevelopmentBuild = $true }
            'process32' { $changed.Preflight.RuntimeIdentity.ProcessBits = 32 }
            'string-editor' { $changed.Preflight.RuntimeIdentity.IsEditor = 'false' }
            'string-schema' { $changed.Preflight.SchemaVersion = '1' }
            'float-schema' { $changed.Preflight.SchemaVersion = [double]1.0 }
            'bool-schema' { $changed.Preflight.SchemaVersion = $true }
            'string-count' { $changed.Correctness.VerifiedRows = '35' }
            'float-count' { $changed.Correctness.VerifiedRows = [double]35.0 }
            'bool-count' { $changed.Correctness.VerifiedRows = $true }
            'string-process-bits' { $changed.Preflight.RuntimeIdentity.ProcessBits = '64' }
            'float-process-bits' { $changed.Preflight.RuntimeIdentity.ProcessBits = [double]64.0 }
            'bool-process-bits' { $changed.Preflight.RuntimeIdentity.ProcessBits = $true }
            'missing-identity' { $changed.Preflight.Remove('RuntimeIdentity') }
            'array-run-id' { $changed.Preflight.FrozenRunId = @($context.Declaration.Value.RunId) }
            'array-runtime-version' { $changed.Preflight.RuntimeIdentity.UnityVersion = @('6000.6.0f1') }
            'array-anchor-digest' { $changed.Preflight.FrozenCorpusSha256 = @($context.Declaration.CanonicalCorpus.Sha256) }
            'array-correctness-digest' { $changed.Correctness.CorpusSha256 = @($context.Declaration.CanonicalCorpus.Sha256) }
            'wrong-correctness-digest' { $changed.Correctness.CorpusSha256 = '0' * 64 }
            'wrong-row-count' { $changed.Correctness.VerifiedRows = 34 }
            'wrong-assertion-count' { $changed.Correctness.DecoderAssertions = 69 }
            'wrong-campaign-count' { $changed.Correctness.DeclaredCampaignRows = 11 }
        }
        Write-RuntimeRecords $log $changed
        $lines = [IO.File]::ReadAllLines($log)
        switch ($kind) {
            'missing-log' { Remove-Item -LiteralPath $log }
            'missing-preflight' { [IO.File]::WriteAllText($log, $lines[1]) }
            'missing-correctness' { [IO.File]::WriteAllText($log, $lines[0]) }
            'duplicate-preflight' { [IO.File]::AppendAllText($log, $lines[0] + "`n") }
            'duplicate-correctness' { [IO.File]::AppendAllText($log, $lines[1] + "`n") }
            'conflicting-preflight' { [IO.File]::AppendAllText($log, $lines[0].Replace($context.Declaration.Value.RunId, 'conflicting-run') + "`n") }
            'malformed' { [IO.File]::WriteAllText($log, "BORROWED_PREFLIGHT {invalid`n" + $lines[1]) }
            'truncated' { [IO.File]::WriteAllText($log, $lines[0].Substring(0, $lines[0].Length - 1) + "`n" + $lines[1]) }
            'escaped-duplicate-key' { [IO.File]::WriteAllText($log, ([regex]::new('\{')).Replace($lines[0], '{"\u0053chemaVersion":1,', 1) + "`n" + $lines[1]) }
            'embedded-prefix' { [IO.File]::WriteAllText($log, 'quoted message "' + $lines[0] + '"' + "`n" + $lines[1]) }
            'deep-json' { [IO.File]::WriteAllText($log, 'BORROWED_PREFLIGHT {"nested":' + ('[' * 65) + '0' + (']' * 65) + '}' + "`n" + $lines[1]) }
            'duplicate-key' { [IO.File]::WriteAllText($log, ([regex]::new('\{')).Replace($lines[0], '{"FrozenRunId":"injected",', 1) + "`n" + $lines[1]) }
        }
        $join = Get-FrozenPlayerRuntimeJoin -Context $context -PlayerLogPath $log -PrerequisitesPassed $true
        Assert-Control ($join.State -eq 'rejected' -and $join.Errors.Count -ne 0) "reject $kind runtime join"
    }
    Write-RuntimeRecords $log $records
    Assert-Control ((Get-FrozenPlayerRuntimeJoin -Context $context -PlayerLogPath $log -PrerequisitesPassed $false).State -eq 'rejected') 'valid records cannot bypass current invocation/exact correctness/payload prerequisites'
    foreach ($relative in @('Assets/FrozenPlayerAnchor/BorrowedBufferPreflightTests.Anchor.cs', 'Assets/FrozenPlayerAnchor/FrozenPlayerAnchor.asmref')) {
        $fixture = New-CanonicalFixture ('anchor-mutation-' + [Guid]::NewGuid().ToString('N'))
        $context = Start-ControlContext $fixture
        [IO.File]::AppendAllText((Join-Path $context.GeneratedRoot $relative), 'mutated')
        Assert-Rejected { Add-FrozenPlayerProvenanceSnapshot -Context $context -Stage 'after-configure' } "detect changed generated $relative" 'hash does not match'
    }
    # Exercise the real runner catch/finally path without launching any native process.
    $fixture = New-DeclarationFixture 'invocation-throws'
    $frozenPlayerDeclaration = Read-ControlDeclaration $fixture
    $frozenPlayerProvenance = $null
    $RepoRoot = $fixture.Root
    $ProjectPath = $fixture.Root
    $standaloneExe = Join-Path $fixture.Payload 'player.exe'
    $resultsPath = Join-Path $fixture.Root 'stale.xml'
    Write-ControlResults $resultsPath
    $playerLogPath = Join-Path $fixture.Root 'missing-player.log'
    $logPath = Join-Path $fixture.Root 'missing-build.log'
    $configureLogPath = Join-Path $fixture.Root 'missing-configure.log'
    $hasLicenseCreds = $false
    $UnityEditorPath = 'never launched'
    $startupProbeLogPath = 'never written'
    function Invoke-UnityNativeStartupProbe { throw 'original synthetic startup failure' }
    $mainTry = $ast.Find({ param($node)
            $node -is [System.Management.Automation.Language.TryStatementAst] -and
            $node.Extent.Text.Contains('Invoke-UnityNativeStartupProbe -EditorPath') -and
            $node.Extent.Text.Contains('Write-UnityCompilationSourceInventoryMarker -Project')
        }, $true)
    if ($null -eq $mainTry) { throw 'Missing runner lifecycle try/finally.' }
    $observed = $null
    try { Invoke-Expression $mainTry.Extent.Text } catch { $observed = $_.ToString() }
    Assert-Control ($null -ne $observed -and $observed.Contains('original synthetic startup failure')) 'original invocation exception survives finalization'
    $report = Get-Content -LiteralPath $frozenPlayerProvenance.ReportPath -Raw | ConvertFrom-Json
    Assert-Control ($report.ExecutionError.Contains('original synthetic startup failure') -and -not $report.InvocationObserved -and
        $report.JointCorrectness.State -eq 'rejected' -and $report.EvidenceFiles.Count -eq 4) 'real lifecycle retains failure evidence without claiming native invocation'
} finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
Write-Host "[test-unity-frozen-player-provenance] $checks controls passed. No native player was launched."
