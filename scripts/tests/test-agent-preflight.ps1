Param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

function Write-TestResult {
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Message = ''
    )

    if ($Passed) {
        Write-Host "  [PASS] $TestName" -ForegroundColor Green
        $script:TestsPassed++
    }
    else {
        Write-Host "  [FAIL] $TestName" -ForegroundColor Red
        if ($Message) {
            Write-Host "         $Message" -ForegroundColor Yellow
        }
        $script:TestsFailed++
        $script:FailedTests += $TestName
    }
}

function Test-ByteArrayEqual {
    param(
        [byte[]]$Expected,
        [byte[]]$Actual
    )

    if ($Expected.Length -ne $Actual.Length) {
        return $false
    }

    for ($i = 0; $i -lt $Expected.Length; $i++) {
        if ($Expected[$i] -ne $Actual[$i]) {
            return $false
        }
    }

    return $true
}

function New-TestRepo {
    param(
        [switch]$ConfigurePushDefaults,
        [string[]]$GitIgnorePatterns,
        [switch]$SkipFakeCspell
    )

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "agent-preflight-test-$([System.Guid]::NewGuid().ToString('N').Substring(0,8))"
    $scriptsDir = Join-Path $tempRoot 'scripts'
    New-Item -ItemType Directory -Path $scriptsDir -Force | Out-Null

    $repoRoot = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
    Copy-Item (Join-Path $repoRoot 'scripts/agent-preflight.ps1') (Join-Path $scriptsDir 'agent-preflight.ps1') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/git-staging-helpers.ps1') (Join-Path $scriptsDir 'git-staging-helpers.ps1') -Force
    # agent-preflight.ps1 dot-sources these helpers at startup; omitting the
    # copy would surface as an obscure "path not found" during the dot-source
    # line rather than the actual test we're trying to run.
    Copy-Item (Join-Path $repoRoot 'scripts/git-push-defaults-helpers.ps1') (Join-Path $scriptsDir 'git-push-defaults-helpers.ps1') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/git-path-helpers.ps1') (Join-Path $scriptsDir 'git-path-helpers.ps1') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/generate-meta.sh') (Join-Path $scriptsDir 'generate-meta.sh') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/run-node-bin.js') (Join-Path $scriptsDir 'run-node-bin.js') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/run-prettier.js') (Join-Path $scriptsDir 'run-prettier.js') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/fix-markdown-fence-languages.ps1') (Join-Path $scriptsDir 'fix-markdown-fence-languages.ps1') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/lint-staged-markdown.ps1') (Join-Path $scriptsDir 'lint-staged-markdown.ps1') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/lint-duplicate-usings.ps1') (Join-Path $scriptsDir 'lint-duplicate-usings.ps1') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/lint-tests.ps1') (Join-Path $scriptsDir 'lint-tests.ps1') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/comment-stripping.ps1') (Join-Path $scriptsDir 'comment-stripping.ps1') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/check-eol.ps1') (Join-Path $scriptsDir 'check-eol.ps1') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/normalize-eol.ps1') (Join-Path $scriptsDir 'normalize-eol.ps1') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/lint-cspell-config.js') (Join-Path $scriptsDir 'lint-cspell-config.js') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/validate-lint-error-codes.ps1') (Join-Path $scriptsDir 'validate-lint-error-codes.ps1') -Force
    # configure-git-defaults.ps1 is preserved as a CLI entry point; it also
    # depends on git-push-defaults-helpers.ps1 (already copied above).
    Copy-Item (Join-Path $repoRoot 'scripts/configure-git-defaults.ps1') (Join-Path $scriptsDir 'configure-git-defaults.ps1') -Force

    if ($null -ne $GitIgnorePatterns -and $GitIgnorePatterns.Count -gt 0) {
        Set-Content -Path (Join-Path $tempRoot '.gitignore') -Value $GitIgnorePatterns -Encoding UTF8
    }

    if (-not $SkipFakeCspell) {
        Add-FakeCspellPackage -RepoPath $tempRoot -Mode Pass
    }

    Push-Location $tempRoot
    try {
        git init -q
        git add .
        git -c user.email=test@example.com -c user.name=test commit -q -m 'init'
        if ($ConfigurePushDefaults) {
            git config --local push.autoSetupRemote true
            git config --local push.default simple
        }
    }
    finally {
        Pop-Location
    }

    return $tempRoot
}

function Invoke-Preflight {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoPath,
        [string[]]$Arguments,
        [hashtable]$EnvOverrides
    )

    $previousValues = @{}
    if ($null -ne $EnvOverrides) {
        foreach ($key in $EnvOverrides.Keys) {
            if (Test-Path "Env:$key") {
                $previousValues[$key] = [Environment]::GetEnvironmentVariable($key)
            }
            else {
                $previousValues[$key] = $null
            }

            [Environment]::SetEnvironmentVariable($key, [string]$EnvOverrides[$key])
        }
    }

    Push-Location $RepoPath
    try {
        $output = & pwsh -NoProfile -File scripts/agent-preflight.ps1 @Arguments 2>&1
        return @{
            ExitCode = $LASTEXITCODE
            Output = ($output -join "`n")
        }
    }
    finally {
        Pop-Location

        if ($null -ne $EnvOverrides) {
            foreach ($key in $EnvOverrides.Keys) {
                [Environment]::SetEnvironmentVariable($key, $previousValues[$key])
            }
        }
    }
}

function Get-StagedPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoPath
    )

    Push-Location $RepoPath
    try {
        $output = & git diff --cached --name-only --diff-filter=ACMR 2>&1
        if ($LASTEXITCODE -ne 0) {
            return @()
        }

        return @($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }
    finally {
        Pop-Location
    }
}

function Add-FakePrettierPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoPath
    )

    $prettierBinDir = Join-Path $RepoPath 'node_modules/prettier/bin'
    New-Item -ItemType Directory -Path $prettierBinDir -Force | Out-Null
    Set-Content -Path (Join-Path $RepoPath 'node_modules/prettier/package.json') -Value '{"bin":"./bin/prettier.cjs"}' -Encoding ascii
    $prettierBin = Join-Path $prettierBinDir 'prettier.cjs'
    $script = @'
#!/usr/bin/env node
if (process.argv.includes("--version")) {
  console.log("3.8.3");
}
process.exit(0);
'@
    Set-Content -Path $prettierBin -Value $script -Encoding ascii
}

function Add-FakeMarkdownlintPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoPath
    )

    $binDir = Join-Path $RepoPath 'node_modules/markdownlint-cli'
    New-Item -ItemType Directory -Path $binDir -Force | Out-Null
    Set-Content -Path (Join-Path $binDir 'package.json') -Value '{"bin":{"markdownlint":"markdownlint.js"}}' -Encoding ascii
    $script = @'
#!/usr/bin/env node
if (process.argv.includes("--version")) {
  console.log("0.48.0");
}
process.exit(0);
'@
    Set-Content -Path (Join-Path $binDir 'markdownlint.js') -Value $script -Encoding ascii
}

function Add-FakeCspellPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoPath,
        [Parameter(Mandatory = $true)]
        [ValidateSet('Pass', 'FailLint')]
        [string]$Mode
    )

    $exitCode = if ($Mode -eq 'FailLint') { '1' } else { '0' }
    $binDir = Join-Path $RepoPath 'node_modules/cspell/bin'
    New-Item -ItemType Directory -Path $binDir -Force | Out-Null
    Set-Content -Path (Join-Path $RepoPath 'node_modules/cspell/package.json') -Value '{"bin":{"cspell":"bin/cspell.cjs"}}' -Encoding ascii
    $script = @'
#!/usr/bin/env node
if (process.argv.includes("--version")) {
  console.log("10.0.0");
  process.exit(0);
}
if (process.argv.includes("lint")) {
  process.exit(__EXIT_CODE__);
}
process.exit(0);
'@
    $script = $script.Replace('__EXIT_CODE__', $exitCode)
    Set-Content -Path (Join-Path $binDir 'cspell.cjs') -Value $script -Encoding ascii
}

function Add-FakeNpmRepairCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoPath
    )

    $scriptPath = Join-Path $RepoPath 'fake-npm-repair.ps1'
    $script = @'
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
)

if ($Arguments.Count -lt 1 -or $Arguments[0] -ne 'ci') {
    Write-Error "Expected npm ci arguments, got: $($Arguments -join ' ')"
    exit 2
}

$prettierBinDir = Join-Path (Get-Location).Path 'node_modules/prettier/bin'
New-Item -ItemType Directory -Path $prettierBinDir -Force | Out-Null
Set-Content -Path (Join-Path (Get-Location).Path 'node_modules/prettier/package.json') -Value '{"bin":"./bin/prettier.cjs"}' -Encoding ascii
$prettierScript = @(
    '#!/usr/bin/env node',
    'if (process.argv.includes("--version")) {',
    '  console.log("3.8.3");',
    '}',
    'process.exit(0);'
) -join [Environment]::NewLine
Set-Content -Path (Join-Path $prettierBinDir 'prettier.cjs') -Value $prettierScript -Encoding ascii

$markdownlintDir = Join-Path (Get-Location).Path 'node_modules/markdownlint-cli'
New-Item -ItemType Directory -Path $markdownlintDir -Force | Out-Null
Set-Content -Path (Join-Path $markdownlintDir 'package.json') -Value '{"bin":{"markdownlint":"markdownlint.js"}}' -Encoding ascii
$markdownlintScript = @(
    '#!/usr/bin/env node',
    'if (process.argv.includes("--version")) {',
    '  console.log("0.48.0");',
    '}',
    'process.exit(0);'
) -join [Environment]::NewLine
Set-Content -Path (Join-Path $markdownlintDir 'markdownlint.js') -Value $markdownlintScript -Encoding ascii

$cspellBinDir = Join-Path (Get-Location).Path 'node_modules/cspell/bin'
New-Item -ItemType Directory -Path $cspellBinDir -Force | Out-Null
Set-Content -Path (Join-Path (Get-Location).Path 'node_modules/cspell/package.json') -Value '{"bin":{"cspell":"bin/cspell.cjs"}}' -Encoding ascii
$cspellScript = @(
    '#!/usr/bin/env node',
    'if (process.argv.includes("--version")) {',
    '  console.log("10.0.0");',
    '  process.exit(0);',
    '}',
    'if (process.argv.includes("lint")) {',
    '  process.exit(0);',
    '}',
    'process.exit(0);'
) -join [Environment]::NewLine
Set-Content -Path (Join-Path $cspellBinDir 'cspell.cjs') -Value $cspellScript -Encoding ascii

exit 0
'@
    Set-Content -Path $scriptPath -Value $script -Encoding UTF8
    return $scriptPath
}

Write-Host 'Testing agent-preflight.ps1...' -ForegroundColor White

# Test 1: No changed files should exit successfully
Write-Host "`nTest group: baseline behavior" -ForegroundColor Magenta
$repo1 = New-TestRepo -ConfigurePushDefaults
try {
    $result1 = Invoke-Preflight -RepoPath $repo1 -Arguments @()
    Write-TestResult 'NoChanges_ExitCode0' ($result1.ExitCode -eq 0) "Expected exit code 0, got $($result1.ExitCode)"
    Write-TestResult 'NoChanges_Message' ($result1.Output -match 'No changed files detected') 'Expected no-changes message'
}
finally {
    Remove-Item -Path $repo1 -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 2: Missing meta file should fail
Write-Host "`nTest group: missing meta detection" -ForegroundColor Magenta
$repo2 = New-TestRepo -ConfigurePushDefaults
try {
    $runtimeDir = Join-Path $repo2 'Runtime'
    New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
    $filePath = Join-Path $runtimeDir 'MyFeature.cs'
    Set-Content -Path $filePath -Value 'public sealed class MyFeature {}' -Encoding UTF8

    $result2 = Invoke-Preflight -RepoPath $repo2 -Arguments @('-Paths', 'Runtime/MyFeature.cs')
    Write-TestResult 'MissingMeta_ExitCode1' ($result2.ExitCode -eq 1) "Expected exit code 1, got $($result2.ExitCode)"
    Write-TestResult 'MissingMeta_ErrorMessage' ($result2.Output -match 'Missing \.meta files detected') 'Expected missing meta error message'
    Write-TestResult 'MissingMeta_ListsPath' ($result2.Output -match 'Runtime/MyFeature\.cs') 'Expected missing path to be listed in output'
}
finally {
    Remove-Item -Path $repo2 -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 3: Fix mode should auto-generate missing meta files
Write-Host "`nTest group: auto-fix mode" -ForegroundColor Magenta
$repo3 = New-TestRepo -ConfigurePushDefaults
try {
    $editorNestedDir = Join-Path $repo3 'Editor/Nested'
    New-Item -ItemType Directory -Path $editorNestedDir -Force | Out-Null
    $filePath = Join-Path $editorNestedDir 'Tool.cs'
    Set-Content -Path $filePath -Value 'public sealed class Tool {}' -Encoding UTF8

    Push-Location $repo3
    try {
        git add Editor/Nested/Tool.cs
    }
    finally {
        Pop-Location
    }

    $result3 = Invoke-Preflight -RepoPath $repo3 -Arguments @('-Fix', '-Paths', 'Editor/Nested/Tool.cs')
    Write-TestResult 'FixMode_ExitCode0' ($result3.ExitCode -eq 0) "Expected exit code 0, got $($result3.ExitCode). Output: $($result3.Output)"
    Write-TestResult 'FixMode_FileMetaCreated' (Test-Path (Join-Path $repo3 'Editor/Nested/Tool.cs.meta')) 'Expected file .meta to be created'
    Write-TestResult 'FixMode_DirMetaCreated' (Test-Path (Join-Path $repo3 'Editor/Nested.meta')) 'Expected directory .meta to be created'
    $fileMetaContent3 = Get-Content -Path (Join-Path $repo3 'Editor/Nested/Tool.cs.meta') -Raw
    Write-TestResult 'FixMode_FileMetaUsesMonoImporter' ($fileMetaContent3 -match 'MonoImporter:') 'Expected C# .meta to use MonoImporter'
    $agentPreflightContent3 = Get-Content -Path (Join-Path $repo3 'scripts/agent-preflight.ps1') -Raw
    Write-TestResult 'FixMode_MetaRecoveryDoesNotRequireBash' ($agentPreflightContent3 -notmatch 'bash .*generate-meta\.sh') 'Expected native PowerShell .meta generation, not bash generate-meta.sh'

    $staged3 = Get-StagedPaths -RepoPath $repo3
    Write-TestResult 'FixMode_FileMetaStaged' ($staged3 -contains 'Editor/Nested/Tool.cs.meta') 'Expected file .meta to be staged by -Fix mode'
    Write-TestResult 'FixMode_DirMetaStaged' ($staged3 -contains 'Editor/Nested.meta') 'Expected directory .meta to be staged by -Fix mode'
}
finally {
    Remove-Item -Path $repo3 -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 3.1: Git-discovered paths with embedded newlines must not be split
$repo3Newline = New-TestRepo -ConfigurePushDefaults
try {
    $currentYear = (Get-Date).Year
    $runtimeDir = Join-Path $repo3Newline 'Runtime'
    New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
    $newlineFileName = "New`nLine.cs"
    $newlineRelativePath = "Runtime/New`nLine.cs"
    $newlinePath = Join-Path $runtimeDir $newlineFileName
    Set-Content -Path $newlinePath -Value @"
// MIT License - Copyright (c) $currentYear wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

public sealed class NewLinePath {}
"@ -Encoding UTF8

    Push-Location $repo3Newline
    try {
        git add -- $newlineRelativePath
    }
    finally {
        Pop-Location
    }

    $result3Newline = Invoke-Preflight -RepoPath $repo3Newline -Arguments @('-Fix')
    Write-TestResult 'NewlinePathFix_ExitCode0' ($result3Newline.ExitCode -eq 0) "Expected exit code 0 for newline path recovery, got $($result3Newline.ExitCode). Output: $($result3Newline.Output)"
    Write-TestResult 'NewlinePathFix_FileMetaCreated' (Test-Path -LiteralPath "$newlinePath.meta") 'Expected exact newline-path .meta companion to be created'

    Push-Location $repo3Newline
    try {
        git cat-file -e ":$newlineRelativePath.meta" 2>$null
        $newlineMetaStaged = $LASTEXITCODE -eq 0
    }
    finally {
        Pop-Location
    }
    Write-TestResult 'NewlinePathFix_FileMetaStaged' $newlineMetaStaged 'Expected exact newline-path .meta companion to be staged'
}
finally {
    Remove-Item -Path $repo3Newline -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 4: Preflight without -Fix should fail when staged source has unstaged .meta companion
Write-Host "`nTest group: staged companion drift detection" -ForegroundColor Magenta
$repo4 = New-TestRepo -ConfigurePushDefaults
try {
    $runtimeDir = Join-Path $repo4 'Runtime'
    New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
    Set-Content -Path (Join-Path $runtimeDir 'StagedOnly.cs') -Value 'public sealed class StagedOnly {}' -Encoding UTF8
    Set-Content -Path (Join-Path $runtimeDir 'StagedOnly.cs.meta') -Value @'
fileFormatVersion: 2
guid: 0123456789abcdef0123456789abcdef
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
'@ -Encoding UTF8

    Push-Location $repo4
    try {
        git add Runtime/StagedOnly.cs
    }
    finally {
        Pop-Location
    }

    $result4 = Invoke-Preflight -RepoPath $repo4 -Arguments @('-Paths', 'Runtime/StagedOnly.cs')
    Write-TestResult 'UnstagedCompanion_ExitCode1' ($result4.ExitCode -eq 1) "Expected exit code 1, got $($result4.ExitCode)"
    Write-TestResult 'UnstagedCompanion_ErrorMessage' ($result4.Output -match 'Unstaged \.meta companion files detected') 'Expected unstaged companion error message'

    $staged4 = Get-StagedPaths -RepoPath $repo4
    Write-TestResult 'UnstagedCompanion_NotAutoStagedWithoutFix' (-not ($staged4 -contains 'Runtime/StagedOnly.cs.meta')) 'Did not expect .meta to be staged without -Fix'
}
finally {
    Remove-Item -Path $repo4 -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 5: Preflight -Fix should auto-stage unstaged .meta companions
Write-Host "`nTest group: staged companion auto-stage" -ForegroundColor Magenta
$repo5 = New-TestRepo -ConfigurePushDefaults
try {
    $editorDir = Join-Path $repo5 'Editor/Tools'
    New-Item -ItemType Directory -Path $editorDir -Force | Out-Null
    Set-Content -Path (Join-Path $editorDir 'Window.cs') -Value 'public sealed class Window {}' -Encoding UTF8
    Set-Content -Path (Join-Path $editorDir 'Window.cs.meta') -Value @'
fileFormatVersion: 2
guid: fedcba9876543210fedcba9876543210
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
'@ -Encoding UTF8
    Set-Content -Path (Join-Path $repo5 'Editor.meta') -Value @'
fileFormatVersion: 2
guid: 11111111111111111111111111111111
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
'@ -Encoding UTF8
    Set-Content -Path (Join-Path $repo5 'Editor/Tools.meta') -Value @'
fileFormatVersion: 2
guid: 22222222222222222222222222222222
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
'@ -Encoding UTF8

    Push-Location $repo5
    try {
        git add Editor/Tools/Window.cs
    }
    finally {
        Pop-Location
    }

    $result5 = Invoke-Preflight -RepoPath $repo5 -Arguments @('-Fix', '-Paths', 'Editor/Tools/Window.cs')
    Write-TestResult 'UnstagedCompanionFix_ExitCode0' ($result5.ExitCode -eq 0) "Expected exit code 0, got $($result5.ExitCode). Output: $($result5.Output)"
    Write-TestResult 'UnstagedCompanionFix_StageMessage' ($result5.Output -match 'Auto-staging unstaged \.meta companions') 'Expected auto-stage message'

    $staged5 = Get-StagedPaths -RepoPath $repo5
    Write-TestResult 'UnstagedCompanionFix_FileMetaStaged' ($staged5 -contains 'Editor/Tools/Window.cs.meta') 'Expected file companion .meta to be staged'
    Write-TestResult 'UnstagedCompanionFix_DirMetaStaged' ($staged5 -contains 'Editor/Tools.meta') 'Expected directory companion .meta to be staged'
}
finally {
    Remove-Item -Path $repo5 -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 6: -Paths scoping should only touch staged files in the specified scope
Write-Host "`nTest group: path-scoped staged companion behavior" -ForegroundColor Magenta
$repo6 = New-TestRepo -ConfigurePushDefaults
try {
    $runtimeDir = Join-Path $repo6 'Runtime'
    New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null

    Set-Content -Path (Join-Path $runtimeDir 'ScopedA.cs') -Value 'public sealed class ScopedA {}' -Encoding UTF8
    Set-Content -Path (Join-Path $runtimeDir 'ScopedB.cs') -Value 'public sealed class ScopedB {}' -Encoding UTF8

    Set-Content -Path (Join-Path $runtimeDir 'ScopedA.cs.meta') -Value @'
fileFormatVersion: 2
guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
'@ -Encoding UTF8
    Set-Content -Path (Join-Path $runtimeDir 'ScopedB.cs.meta') -Value @'
fileFormatVersion: 2
guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
'@ -Encoding UTF8

    Push-Location $repo6
    try {
        git add Runtime/ScopedA.cs Runtime/ScopedB.cs
    }
    finally {
        Pop-Location
    }

    $result6 = Invoke-Preflight -RepoPath $repo6 -Arguments @('-Fix', '-Paths', 'Runtime/ScopedA.cs')
    Write-TestResult 'ScopedPaths_ExitCode0' ($result6.ExitCode -eq 0) "Expected exit code 0, got $($result6.ExitCode). Output: $($result6.Output)"

    $staged6 = Get-StagedPaths -RepoPath $repo6
    Write-TestResult 'ScopedPaths_StagesScopedCompanion' ($staged6 -contains 'Runtime/ScopedA.cs.meta') 'Expected scoped .meta companion to be staged'
    Write-TestResult 'ScopedPaths_DoesNotStageUnscopedCompanion' (-not ($staged6 -contains 'Runtime/ScopedB.cs.meta')) 'Did not expect unscoped .meta companion to be staged'
}
finally {
    Remove-Item -Path $repo6 -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 6b: modified tracked sources do not need unchanged .meta companions restaged
Write-Host "`nTest group: unchanged tracked companion behavior" -ForegroundColor Magenta
$repo6b = New-TestRepo -ConfigurePushDefaults
try {
    $editorDir = Join-Path $repo6b 'Editor/Tracked'
    New-Item -ItemType Directory -Path $editorDir -Force | Out-Null
    Set-Content -Path (Join-Path $repo6b 'Editor.meta') -Value @'
fileFormatVersion: 2
guid: 33333333333333333333333333333333
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
'@ -Encoding UTF8
    Set-Content -Path (Join-Path $repo6b 'Editor/Tracked.meta') -Value @'
fileFormatVersion: 2
guid: 44444444444444444444444444444444
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
'@ -Encoding UTF8
    Set-Content -Path (Join-Path $editorDir 'Existing.asset') -Value @'
%YAML 1.1
--- !u!114 &11400000
MonoBehaviour:
  m_Name: Existing
'@ -Encoding UTF8
    Set-Content -Path (Join-Path $editorDir 'Existing.asset.meta') -Value @'
fileFormatVersion: 2
guid: 55555555555555555555555555555555
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
'@ -Encoding UTF8

    Push-Location $repo6b
    try {
        git add Editor.meta Editor/Tracked.meta Editor/Tracked/Existing.asset Editor/Tracked/Existing.asset.meta
        git -c user.email=test@example.com -c user.name=test commit -q -m 'add tracked editor asset'
        Set-Content -Path (Join-Path $editorDir 'Existing.asset') -Value @'
%YAML 1.1
--- !u!114 &11400000
MonoBehaviour:
  m_Name: Existing
  m_EditorClassIdentifier: Changed
'@ -Encoding UTF8
        git add Editor/Tracked/Existing.asset
    }
    finally {
        Pop-Location
    }

    $result6b = Invoke-Preflight -RepoPath $repo6b -Arguments @('-Paths', 'Editor/Tracked/Existing.asset')
    Write-TestResult 'TrackedCompanions_ExitCode0' ($result6b.ExitCode -eq 0) "Expected exit code 0, got $($result6b.ExitCode). Output: $($result6b.Output)"
    Write-TestResult 'TrackedCompanions_NoUnstagedMetaError' (-not ($result6b.Output -match 'Unstaged \.meta companion files detected')) 'Did not expect unchanged tracked .meta companions to be reported'

    $staged6b = Get-StagedPaths -RepoPath $repo6b
    Write-TestResult 'TrackedCompanions_DoesNotRestageFileMeta' (-not ($staged6b -contains 'Editor/Tracked/Existing.asset.meta')) 'Did not expect unchanged file .meta companion to be staged'
    Write-TestResult 'TrackedCompanions_DoesNotRestageDirMeta' (-not ($staged6b -contains 'Editor/Tracked.meta')) 'Did not expect unchanged directory .meta companion to be staged'
}
finally {
    Remove-Item -Path $repo6b -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 7: -Fix should fail with clear diagnostics if index.lock contention blocks staging
Write-Host "`nTest group: lock contention diagnostics" -ForegroundColor Magenta
$repo7 = New-TestRepo -ConfigurePushDefaults
try {
    $runtimeDir = Join-Path $repo7 'Runtime'
    New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null

    Set-Content -Path (Join-Path $runtimeDir 'LockCase.cs') -Value 'public sealed class LockCase {}' -Encoding UTF8
    Set-Content -Path (Join-Path $runtimeDir 'LockCase.cs.meta') -Value @'
fileFormatVersion: 2
guid: cccccccccccccccccccccccccccccccc
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
'@ -Encoding UTF8

    Push-Location $repo7
    try {
        git add Runtime/LockCase.cs
        Set-Content -Path (Join-Path $repo7 '.git/index.lock') -Value 'lock' -Encoding UTF8
    }
    finally {
        Pop-Location
    }

    $result7 = Invoke-Preflight -RepoPath $repo7 -Arguments @('-Fix', '-Paths', 'Runtime/LockCase.cs') -EnvOverrides @{
        GIT_LOCK_MAX_ATTEMPTS = '2'
        GIT_LOCK_INITIAL_DELAY_MS = '1'
        GIT_LOCK_MAX_DELAY_MS = '2'
        GIT_LOCK_WAIT_TIMEOUT_MS = '1'
        GIT_LOCK_POLL_INTERVAL_MS = '1'
        GIT_LOCK_INITIAL_WAIT_MS = '1'
    }

    Write-TestResult 'LockContention_ExitCode1' ($result7.ExitCode -eq 1) "Expected exit code 1, got $($result7.ExitCode)"
    Write-TestResult 'LockContention_ErrorMessage' ($result7.Output -match 'Failed to stage one or more \.meta companion files') 'Expected lock contention staging failure message'
    Write-TestResult 'LockContention_RecoveryHint' ($result7.Output -match 'Close other git operations') 'Expected actionable recovery hint in output'
}
finally {
    Remove-Item -Path $repo7 -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 8: Changed markdown files should pass when cspell lint succeeds
Write-Host "`nTest group: spelling checks on changed files" -ForegroundColor Magenta
$repo8 = New-TestRepo -ConfigurePushDefaults
try {
    Set-Content -Path (Join-Path $repo8 'README.md') -Value 'Spelling check baseline.' -Encoding UTF8
    Add-FakePrettierPackage -RepoPath $repo8
    Add-FakeMarkdownlintPackage -RepoPath $repo8
    Add-FakeCspellPackage -RepoPath $repo8 -Mode Pass
    $result8 = Invoke-Preflight -RepoPath $repo8 -Arguments @('-Paths', 'README.md')

    Write-TestResult 'SpellingChecks_ExitCode0' ($result8.ExitCode -eq 0) "Expected exit code 0, got $($result8.ExitCode). Output: $($result8.Output)"
    Write-TestResult 'SpellingChecks_Message' ($result8.Output -match 'Checking spelling on changed spell-checkable files') 'Expected spelling check status message'
}
finally {
    Remove-Item -Path $repo8 -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 8b: -Fix should add missing Markdown fence languages before markdownlint is the last resort
Write-Host "`nTest group: markdown fence language auto-fix" -ForegroundColor Magenta
$repo8b = New-TestRepo -ConfigurePushDefaults
try {
    $readmePath = Join-Path $repo8b 'README.md'
    Set-Content -Path $readmePath -Value @'
# Fixture

```
Unity (Windows, stdio) -> bridge -> agent
```

```
npm run agent:preflight:fix
```
'@ -Encoding UTF8
    Add-FakePrettierPackage -RepoPath $repo8b
    Add-FakeMarkdownlintPackage -RepoPath $repo8b

    Push-Location $repo8b
    try {
        git add README.md
    }
    finally {
        Pop-Location
    }

    $result8b = Invoke-Preflight -RepoPath $repo8b -Arguments @('-Fix', '-Paths', 'README.md')
    Write-TestResult 'MarkdownFenceFix_ExitCode0' ($result8b.ExitCode -eq 0) "Expected exit code 0 after fence fix, got $($result8b.ExitCode). Output: $($result8b.Output)"

    $fixedMarkdown = Get-Content -Path $readmePath -Raw
    Write-TestResult 'MarkdownFenceFix_TextFallback' ($fixedMarkdown -match '```text\s+Unity \(Windows, stdio\) -> bridge -> agent') 'Expected plain-text diagram fence to get text language'
    Write-TestResult 'MarkdownFenceFix_BashInference' ($fixedMarkdown -match '```bash\s+npm run agent:preflight:fix') 'Expected shell command fence to get bash language'

    Push-Location $repo8b
    try {
        $stagedMarkdown = git show ':README.md' | Out-String
    }
    finally {
        Pop-Location
    }
    Write-TestResult 'MarkdownFenceFix_StagedBlobUpdated' ($stagedMarkdown -match '```text' -and $stagedMarkdown -match '```bash') 'Expected staged README.md blob to include inferred fence languages'
}
finally {
    Remove-Item -Path $repo8b -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 8c: lint-staged-markdown.ps1 should share the same fence-language recovery
Write-Host "`nTest group: staged markdown helper fence recovery" -ForegroundColor Magenta
$repo8c = New-TestRepo -ConfigurePushDefaults
try {
    $readmePath = Join-Path $repo8c 'README.md'
    Set-Content -Path $readmePath -Value @'
# Fixture

```
git status --short
```
'@ -Encoding UTF8
    Add-FakeMarkdownlintPackage -RepoPath $repo8c

    Push-Location $repo8c
    try {
        git add README.md
        $helperOutput = & pwsh -NoProfile -File scripts/lint-staged-markdown.ps1 README.md 2>&1
        $helperExit = $LASTEXITCODE
        $stagedMarkdown = git show ':README.md' | Out-String
    }
    finally {
        Pop-Location
    }

    $helperJoined = ($helperOutput -join "`n")
    Write-TestResult 'LintStagedMarkdownFenceFix_ExitCode0' ($helperExit -eq 0) "Expected exit code 0 from lint-staged-markdown.ps1, got $helperExit. Output: $helperJoined"
    Write-TestResult 'LintStagedMarkdownFenceFix_WorktreeUpdated' ((Get-Content -Path $readmePath -Raw) -match '```bash\s+git status --short') 'Expected worktree README.md to include bash fence language'
    Write-TestResult 'LintStagedMarkdownFenceFix_StagedUpdated' ($stagedMarkdown -match '```bash\s+git status --short') 'Expected staged README.md blob to include bash fence language'
}
finally {
    Remove-Item -Path $repo8c -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 9: Changed markdown typos should fail preflight with actionable output
Write-Host "`nTest group: spelling failure diagnostics" -ForegroundColor Magenta
$repo9 = New-TestRepo -ConfigurePushDefaults
try {
    Set-Content -Path (Join-Path $repo9 'README.md') -Value 'Synthetic spelling failure fixture.' -Encoding UTF8
    Add-FakePrettierPackage -RepoPath $repo9
    Add-FakeMarkdownlintPackage -RepoPath $repo9
    Add-FakeCspellPackage -RepoPath $repo9 -Mode FailLint
    $result9 = Invoke-Preflight -RepoPath $repo9 -Arguments @('-Paths', 'README.md')

    Write-TestResult 'SpellingFailure_ExitCode1' ($result9.ExitCode -eq 1) "Expected exit code 1, got $($result9.ExitCode). Output: $($result9.Output)"
    Write-TestResult 'SpellingFailure_ErrorMessage' ($result9.Output -match 'Spelling errors detected in changed spell-checkable files') 'Expected spelling failure message'
    Write-TestResult 'SpellingFailure_RecoveryHint' ($result9.Output -match 'npm run lint:spelling') 'Expected recovery command hint'
}
finally {
    Remove-Item -Path $repo9 -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 10: Missing cspell should fail with an actionable dependency message
Write-Host "`nTest group: spelling missing dependency diagnostics" -ForegroundColor Magenta
$repo10 = New-TestRepo -ConfigurePushDefaults -SkipFakeCspell
try {
    Set-Content -Path (Join-Path $repo10 'README.md') -Value 'Spelling check baseline.' -Encoding UTF8
    Add-FakePrettierPackage -RepoPath $repo10
    Add-FakeMarkdownlintPackage -RepoPath $repo10
    $result10 = Invoke-Preflight -RepoPath $repo10 -Arguments @('-Paths', 'README.md')

    Write-TestResult 'SpellingMissingDependency_ExitCode1' ($result10.ExitCode -eq 1) "Expected exit code 1 when cspell is unavailable, got $($result10.ExitCode). Output: $($result10.Output)"
    Write-TestResult 'SpellingMissingDependency_Message' ($result10.Output -match "Required npm tool 'cspell' is not installed") 'Expected missing-cspell diagnostic message'
}
finally {
    Remove-Item -Path $repo10 -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 10a: -Fix restores missing repo-local npm tools from package-lock.json
Write-Host "`nTest group: npm dependency auto-repair" -ForegroundColor Magenta
$repo10a = New-TestRepo -ConfigurePushDefaults -SkipFakeCspell
try {
    Set-Content -Path (Join-Path $repo10a 'README.md') -Value 'Spelling check baseline.' -Encoding UTF8
    Set-Content -Path (Join-Path $repo10a 'package.json') -Value '{"name":"fixture","devDependencies":{"cspell":"10.0.0","markdownlint-cli":"0.48.0","prettier":"3.8.3"}}' -Encoding UTF8
    Set-Content -Path (Join-Path $repo10a 'package-lock.json') -Value '{"name":"fixture","lockfileVersion":3,"packages":{}}' -Encoding UTF8
    $fakeNpm = Add-FakeNpmRepairCommand -RepoPath $repo10a

    $result10a = Invoke-Preflight -RepoPath $repo10a -Arguments @('-Fix', '-Paths', 'README.md') -EnvOverrides @{
        AGENT_PREFLIGHT_NPM_COMMAND = $fakeNpm
    }

    Write-TestResult 'NpmRepair_ExitCode0' ($result10a.ExitCode -eq 0) "Expected exit code 0 after npm repair, got $($result10a.ExitCode). Output: $($result10a.Output)"
    Write-TestResult 'NpmRepair_RunsNpmCi' ($result10a.Output -match 'Restoring repo-local npm tools with npm ci') 'Expected npm ci repair message'
    Write-TestResult 'NpmRepair_CspellCreated' (Test-Path (Join-Path $repo10a 'node_modules/cspell/bin/cspell.cjs')) 'Expected fake cspell binary to be restored'
}
finally {
    Remove-Item -Path $repo10a -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 10b: Missing Prettier should fail before hook-time formatting
Write-Host "`nTest group: prettier missing dependency diagnostics" -ForegroundColor Magenta
$repo10b = New-TestRepo -ConfigurePushDefaults
try {
    Set-Content -Path (Join-Path $repo10b 'package.json') -Value '{"name":"fixture"}' -Encoding UTF8
    $result10b = Invoke-Preflight -RepoPath $repo10b -Arguments @('-Paths', 'package.json')

    Write-TestResult 'PrettierMissingDependency_ExitCode1' ($result10b.ExitCode -eq 1) "Expected exit code 1 when repo-local Prettier is unavailable, got $($result10b.ExitCode). Output: $($result10b.Output)"
    Write-TestResult 'PrettierMissingDependency_Message' ($result10b.Output -match 'Repo-local Prettier is unavailable|Prettier is not installed') 'Expected missing-Prettier diagnostic message'
    Write-TestResult 'PrettierMissingDependency_InstallHint' ($result10b.Output -match 'npm install') 'Expected npm install remediation hint'
}
finally {
    Remove-Item -Path $repo10b -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 10c: lint-error-code contract should run before pre-push when lint scripts change
Write-Host "`nTest group: lint-error-code preflight contract" -ForegroundColor Magenta
$repo10c = New-TestRepo -ConfigurePushDefaults
try {
    $hooksDir = Join-Path $repo10c '.githooks'
    New-Item -ItemType Directory -Path $hooksDir -Force | Out-Null
    Set-Content -Path (Join-Path $hooksDir 'pre-push') -Value '#!/usr/bin/env bash
echo "UNH001"
' -Encoding UTF8

    $result10c = Invoke-Preflight -RepoPath $repo10c -Arguments @('-Paths', '.githooks/pre-push')
    Write-TestResult 'LintErrorCodeContract_ExitCode0' ($result10c.ExitCode -eq 0) "Expected exit code 0, got $($result10c.ExitCode). Output: $($result10c.Output)"
    Write-TestResult 'LintErrorCodeContract_RunsValidator' ($result10c.Output -match 'Validating lint-error-code cspell coverage') 'Expected agent-preflight to run lint-error-code coverage before pre-push'
}
finally {
    Remove-Item -Path $repo10c -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 10d: Changed C# license year drift should fail, and -Fix should repair/stage
Write-Host "`nTest group: license header auto-fix" -ForegroundColor Magenta
$repo10cLicense = New-TestRepo -ConfigurePushDefaults
try {
    $currentYear = (Get-Date).Year
    $filePath = Join-Path $repo10cLicense 'Loose.cs'
    Set-Content -Path $filePath -Value @'
// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

public sealed class Loose {}
'@ -Encoding UTF8

    $result10cLicense = Invoke-Preflight -RepoPath $repo10cLicense -Arguments @('-Paths', 'Loose.cs')
    Write-TestResult 'LicenseHeaderDrift_ExitCode1' ($result10cLicense.ExitCode -eq 1) "Expected exit code 1 for mismatched license year, got $($result10cLicense.ExitCode). Output: $($result10cLicense.Output)"
    Write-TestResult 'LicenseHeaderDrift_Message' ($result10cLicense.Output -match 'License year header issues detected') 'Expected license header drift diagnostic'

    Push-Location $repo10cLicense
    try {
        git add Loose.cs
    }
    finally {
        Pop-Location
    }

    $result10cFix = Invoke-Preflight -RepoPath $repo10cLicense -Arguments @('-Fix', '-Paths', 'Loose.cs')
    Write-TestResult 'LicenseHeaderFix_ExitCode0' ($result10cFix.ExitCode -eq 0) "Expected exit code 0 after license fix, got $($result10cFix.ExitCode). Output: $($result10cFix.Output)"
    $fixedContent = Get-Content -Path $filePath -Raw
    Write-TestResult 'LicenseHeaderFix_WorktreeUpdated' ($fixedContent -match "Copyright \(c\) $currentYear wallstop") "Expected worktree header year $currentYear"

    Push-Location $repo10cLicense
    try {
        $stagedContent = git show ':Loose.cs'
    }
    finally {
        Pop-Location
    }
    Write-TestResult 'LicenseHeaderFix_StagedUpdatedBlob' (($stagedContent -join "`n") -match "Copyright \(c\) $currentYear wallstop") "Expected staged header year $currentYear"
}
finally {
    Remove-Item -Path $repo10cLicense -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 10e: staged test null assertion fixes must update the index, not only the worktree
Write-Host "`nTest group: test null assertion auto-fix staging" -ForegroundColor Magenta
$repo10cNullFix = New-TestRepo -ConfigurePushDefaults
try {
    $currentYear = (Get-Date).Year
    $testDir = Join-Path $repo10cNullFix 'Tests/Runtime'
    New-Item -ItemType Directory -Path $testDir -Force | Out-Null
    $testPath = Join-Path $testDir 'NullAssertionTests.cs'
    Set-Content -Path $testPath -Value @"
// MIT License - Copyright (c) $currentYear wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

using NUnit.Framework;

namespace Fixture.Tests
{
    public sealed class NullAssertionTests
    {
        [Test]
        public void NullObjectReportsTrue()
        {
            object value = null;
            object other = new object();

            Assert.IsNull(value);
            Assert.IsNotNull(other);
        }
    }
}
"@ -Encoding UTF8

    Push-Location $repo10cNullFix
    try {
        git add Tests/Runtime/NullAssertionTests.cs
    }
    finally {
        Pop-Location
    }

    $result10cNullFix = Invoke-Preflight -RepoPath $repo10cNullFix -Arguments @('-Fix', '-Paths', 'Tests\Runtime\NullAssertionTests.cs')
    Write-TestResult 'NullAssertionFix_ExitCode0' ($result10cNullFix.ExitCode -eq 0) "Expected exit code 0 after null assertion fix, got $($result10cNullFix.ExitCode). Output: $($result10cNullFix.Output)"

    $fixedTestContent = Get-Content -Path $testPath -Raw
    Write-TestResult 'NullAssertionFix_WorktreeUpdated' ($fixedTestContent -match 'Assert\.IsTrue\(value == null\)' -and $fixedTestContent -match 'Assert\.IsTrue\(other != null\)') 'Expected worktree assertions to use Assert.IsTrue null comparisons'

    Push-Location $repo10cNullFix
    try {
        $stagedTestContent = git show ':Tests/Runtime/NullAssertionTests.cs' | Out-String
    }
    finally {
        Pop-Location
    }
    Write-TestResult 'NullAssertionFix_StagedUpdatedBlob' ($stagedTestContent -match 'Assert\.IsTrue\(value == null\)' -and $stagedTestContent -match 'Assert\.IsTrue\(other != null\)') 'Expected staged test blob to include null assertion fixes'
}
finally {
    Remove-Item -Path $repo10cNullFix -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 10e.1: staged renamed files must be re-staged after null assertion fixes
$repo10cRenamedNullFix = New-TestRepo -ConfigurePushDefaults
try {
    $currentYear = (Get-Date).Year
    $testDir = Join-Path $repo10cRenamedNullFix 'Tests/Runtime'
    New-Item -ItemType Directory -Path $testDir -Force | Out-Null
    $oldTestPath = Join-Path $testDir 'OldNullAssertionTests.cs'
    Set-Content -Path $oldTestPath -Value @"
// MIT License - Copyright (c) $currentYear wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

using NUnit.Framework;

namespace Fixture.Tests
{
    public sealed class OldNullAssertionTests
    {
        [Test]
        public void NullObjectReportsTrue()
        {
            object value = null;

            Assert.IsNull(value);
        }
    }
}
"@ -Encoding UTF8

    Push-Location $repo10cRenamedNullFix
    try {
        git add Tests/Runtime/OldNullAssertionTests.cs
        git -c user.email=test@example.com -c user.name=test commit -q -m 'add old null assertion test'
        git mv Tests/Runtime/OldNullAssertionTests.cs Tests/Runtime/RenamedNullAssertionTests.cs
    }
    finally {
        Pop-Location
    }

    $result10cRenamedNullFix = Invoke-Preflight -RepoPath $repo10cRenamedNullFix -Arguments @('-Fix', '-Paths', 'Tests/Runtime/RenamedNullAssertionTests.cs')
    Write-TestResult 'RenamedNullAssertionFix_ExitCode0' ($result10cRenamedNullFix.ExitCode -eq 0) "Expected exit code 0 after renamed null assertion fix, got $($result10cRenamedNullFix.ExitCode). Output: $($result10cRenamedNullFix.Output)"

    Push-Location $repo10cRenamedNullFix
    try {
        $stagedRenamedTestContent = git show ':Tests/Runtime/RenamedNullAssertionTests.cs' | Out-String
    }
    finally {
        Pop-Location
    }
    Write-TestResult 'RenamedNullAssertionFix_StagedUpdatedBlob' ($stagedRenamedTestContent -match 'Assert\.IsTrue\(value == null\)') 'Expected staged renamed test blob to include null assertion fix'
}
finally {
    Remove-Item -Path $repo10cRenamedNullFix -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 10e.2: region guard must inspect staged C# blobs, not only worktree files
$repo10cStagedRegion = New-TestRepo -ConfigurePushDefaults
try {
    $currentYear = (Get-Date).Year
    $stagedRegionPath = Join-Path $repo10cStagedRegion 'StagedRegion.cs'
    Set-Content -Path $stagedRegionPath -Value @"
// MIT License - Copyright (c) $currentYear wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

public sealed class StagedRegion
{
#region Bad
#endregion
}
"@ -Encoding UTF8

    Push-Location $repo10cStagedRegion
    try {
        git add StagedRegion.cs
    }
    finally {
        Pop-Location
    }

    Set-Content -Path $stagedRegionPath -Value @"
// MIT License - Copyright (c) $currentYear wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

public sealed class StagedRegion
{
}
"@ -Encoding UTF8

    $result10cStagedRegion = Invoke-Preflight -RepoPath $repo10cStagedRegion -Arguments @('-Paths', 'StagedRegion.cs')
    Write-TestResult 'StagedRegionGuard_ExitCode1' ($result10cStagedRegion.ExitCode -eq 1) "Expected exit code 1 for staged #region, got $($result10cStagedRegion.ExitCode). Output: $($result10cStagedRegion.Output)"
    Write-TestResult 'StagedRegionGuard_ReportsStagedBlob' ($result10cStagedRegion.Output -match 'StagedRegion\.cs' -and $result10cStagedRegion.Output -match '#region') 'Expected staged #region diagnostic even though worktree removed it'
}
finally {
    Remove-Item -Path $repo10cStagedRegion -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 10f: -Fix must not sweep pre-existing unstaged hunks into staged whole-file fixes
Write-Host "`nTest group: partial staging auto-fix guard" -ForegroundColor Magenta
$repo10cPartial = New-TestRepo -ConfigurePushDefaults
try {
    $currentYear = (Get-Date).Year
    $previousYear = $currentYear - 1
    $partialPath = Join-Path $repo10cPartial 'Partial.cs'
    Set-Content -Path $partialPath -Value @"
// MIT License - Copyright (c) $previousYear wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

public sealed class Partial {}
"@ -Encoding UTF8

    Push-Location $repo10cPartial
    try {
        git add Partial.cs
    }
    finally {
        Pop-Location
    }

    Add-Content -Path $partialPath -Value 'public sealed class UnstagedOnly {}' -Encoding UTF8
    $partialContentBefore = Get-Content -Path $partialPath -Raw

    $result10cPartial = Invoke-Preflight -RepoPath $repo10cPartial -Arguments @('-Fix', '-Paths', 'Partial.cs')
    Write-TestResult 'PartialStageGuard_ExitCode1' ($result10cPartial.ExitCode -eq 1) "Expected exit code 1 for partial-staging refusal, got $($result10cPartial.ExitCode). Output: $($result10cPartial.Output)"
    Write-TestResult 'PartialStageGuard_RefusalMessage' ($result10cPartial.Output -match 'Refusing to auto-stage whole file\(s\) with pre-existing unstaged changes') 'Expected explicit partial-staging refusal message'
    $partialContentAfter = Get-Content -Path $partialPath -Raw
    Write-TestResult 'PartialStageGuard_WorktreeUnchangedBeforeRefusal' ($partialContentAfter -ceq $partialContentBefore) 'Expected license fixer to refuse before mutating the partially staged worktree file'

    Push-Location $repo10cPartial
    try {
        $stagedPartialContent = git show ':Partial.cs' | Out-String
    }
    finally {
        Pop-Location
    }
    Write-TestResult 'PartialStageGuard_DoesNotStageUnstagedHunk' ($stagedPartialContent -notmatch 'UnstagedOnly') 'Expected staged blob to exclude pre-existing unstaged hunk'
}
finally {
    Remove-Item -Path $repo10cPartial -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 10f.1: null-assertion fixer must also refuse before mutating a partially staged test
$repo10cPartialNull = New-TestRepo -ConfigurePushDefaults
try {
    $currentYear = (Get-Date).Year
    $testDir = Join-Path $repo10cPartialNull 'Tests/Runtime'
    New-Item -ItemType Directory -Path $testDir -Force | Out-Null
    $testPath = Join-Path $testDir 'PartialNullAssertionTests.cs'
    Set-Content -Path $testPath -Value @"
// MIT License - Copyright (c) $currentYear wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

using NUnit.Framework;

namespace Fixture.Tests
{
    public sealed class PartialNullAssertionTests
    {
        [Test]
        public void NullObjectReportsTrue()
        {
            object value = null;

            Assert.IsNull(value);
        }
    }
}
"@ -Encoding UTF8

    Push-Location $repo10cPartialNull
    try {
        git add Tests/Runtime/PartialNullAssertionTests.cs
    }
    finally {
        Pop-Location
    }

    Add-Content -Path $testPath -Value '// UnstagedOnly' -Encoding UTF8
    $partialNullBefore = Get-Content -Path $testPath -Raw

    $result10cPartialNull = Invoke-Preflight -RepoPath $repo10cPartialNull -Arguments @('-Fix', '-Paths', 'Tests/Runtime/PartialNullAssertionTests.cs')
    Write-TestResult 'PartialStageGuard_NullFixExitCode1' ($result10cPartialNull.ExitCode -eq 1) "Expected exit code 1 for partial-staging refusal, got $($result10cPartialNull.ExitCode). Output: $($result10cPartialNull.Output)"
    $partialNullAfter = Get-Content -Path $testPath -Raw
    Write-TestResult 'PartialStageGuard_NullFixWorktreeUnchanged' ($partialNullAfter -ceq $partialNullBefore) 'Expected null assertion fixer to refuse before mutating the partially staged worktree file'
}
finally {
    Remove-Item -Path $repo10cPartialNull -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 10f.2: EOL fixer plans modified paths before writing partially staged files
$repo10cPartialEol = New-TestRepo -ConfigurePushDefaults
try {
    $currentYear = (Get-Date).Year
    $eolPartialPath = Join-Path $repo10cPartialEol 'EolPartial.cs'
    [System.IO.File]::WriteAllBytes(
        $eolPartialPath,
        [System.Text.UTF8Encoding]::new($false).GetBytes("// MIT License - Copyright (c) $currentYear wallstop`n// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE`n`npublic sealed class EolPartial {}`n")
    )

    Push-Location $repo10cPartialEol
    try {
        git add EolPartial.cs
    }
    finally {
        Pop-Location
    }

    Add-Content -Path $eolPartialPath -Value '// UnstagedOnly' -Encoding UTF8
    $partialEolBefore = [System.IO.File]::ReadAllBytes($eolPartialPath)

    $result10cPartialEol = Invoke-Preflight -RepoPath $repo10cPartialEol -Arguments @('-Fix', '-Paths', 'EolPartial.cs')
    Write-TestResult 'PartialStageGuard_EolFixExitCode1' ($result10cPartialEol.ExitCode -eq 1) "Expected exit code 1 for partial-staging refusal, got $($result10cPartialEol.ExitCode). Output: $($result10cPartialEol.Output)"
    $partialEolAfter = [System.IO.File]::ReadAllBytes($eolPartialPath)
    Write-TestResult 'PartialStageGuard_EolFixWorktreeUnchanged' (Test-ByteArrayEqual -Expected $partialEolBefore -Actual $partialEolAfter) 'Expected EOL fixer to refuse before mutating the partially staged worktree file'
}
finally {
    Remove-Item -Path $repo10cPartialEol -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 10f.3: LLM instruction fixer refuses before overwriting generated index with unstaged edits
$repo10cPartialLlm = New-TestRepo -ConfigurePushDefaults
try {
    Add-FakePrettierPackage -RepoPath $repo10cPartialLlm
    Add-FakeMarkdownlintPackage -RepoPath $repo10cPartialLlm

    $llmDir = Join-Path $repo10cPartialLlm '.llm'
    $skillsDir = Join-Path $llmDir 'skills'
    New-Item -ItemType Directory -Path $skillsDir -Force | Out-Null
    Set-Content -Path (Join-Path $llmDir 'context.md') -Value '# Context' -Encoding UTF8
    Set-Content -Path (Join-Path $skillsDir 'index.md') -Value 'manual index edits' -Encoding UTF8
    Set-Content -Path (Join-Path $repo10cPartialLlm 'scripts/lint-skill-sizes.ps1') -Value @'
Param(
    [string[]]$Paths,
    [switch]$FailOnCritical,
    [switch]$VerboseOutput
)
exit 0
'@ -Encoding UTF8
    Set-Content -Path (Join-Path $repo10cPartialLlm 'scripts/lint-llm-instructions.ps1') -Value @'
Param(
    [switch]$Fix,
    [switch]$VerboseOutput
)
Set-Content -Path "llm-fix-ran.txt" -Value "ran" -Encoding UTF8
Set-Content -Path ".llm/skills/index.md" -Value "generated index" -Encoding UTF8
exit 0
'@ -Encoding UTF8

    Push-Location $repo10cPartialLlm
    try {
        git add .llm scripts/lint-skill-sizes.ps1 scripts/lint-llm-instructions.ps1
        git -c user.email=test@example.com -c user.name=test commit -q -m 'add llm fixtures'
        Set-Content -Path (Join-Path $llmDir 'context.md') -Value '# Context changed' -Encoding UTF8
        git add .llm/context.md
        Set-Content -Path (Join-Path $skillsDir 'index.md') -Value 'manual index edits plus unstaged work' -Encoding UTF8
    }
    finally {
        Pop-Location
    }

    $partialLlmBefore = Get-Content -Path (Join-Path $skillsDir 'index.md') -Raw
    $result10cPartialLlm = Invoke-Preflight -RepoPath $repo10cPartialLlm -Arguments @('-Fix', '-Paths', '.llm/context.md')
    $partialLlmAfter = Get-Content -Path (Join-Path $skillsDir 'index.md') -Raw
    Write-TestResult 'PartialStageGuard_LlmFixExitCode1' ($result10cPartialLlm.ExitCode -eq 1) "Expected exit code 1 for LLM partial-staging refusal, got $($result10cPartialLlm.ExitCode). Output: $($result10cPartialLlm.Output)"
    Write-TestResult 'PartialStageGuard_LlmFixWorktreeUnchanged' ($partialLlmAfter -ceq $partialLlmBefore) 'Expected LLM fixer to refuse before mutating .llm/skills/index.md'
    Write-TestResult 'PartialStageGuard_LlmFixNotInvoked' (-not (Test-Path (Join-Path $repo10cPartialLlm 'llm-fix-ran.txt'))) 'Expected lint-llm-instructions.ps1 -Fix not to run after pre-mutation refusal'
}
finally {
    Remove-Item -Path $repo10cPartialLlm -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 10g: PathList recovery should work even when the worktree starts clean
Write-Host "`nTest group: path-list recovery from clean worktree" -ForegroundColor Magenta
$repo10d = New-TestRepo -ConfigurePushDefaults
try {
    $currentYear = (Get-Date).Year
    $previousYear = $currentYear - 1
    $filePath = Join-Path $repo10d 'CommittedBad.cs'
    Set-Content -Path $filePath -Value @"
// MIT License - Copyright (c) $previousYear wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

public sealed class CommittedBad {}
"@ -Encoding UTF8

    Push-Location $repo10d
    try {
        git add CommittedBad.cs
        git -c user.email=test@example.com -c user.name=test commit -q -m 'add bad license year'
    }
    finally {
        Pop-Location
    }

    $pathListPath = Join-Path $repo10d '.git/pre-push-agent-preflight-paths.bin'
    [System.IO.File]::WriteAllBytes($pathListPath, [System.Text.Encoding]::UTF8.GetBytes("CommittedBad.cs`0"))

    $result10d = Invoke-Preflight -RepoPath $repo10d -Arguments @('-Fix', '-PathList', $pathListPath)
    Write-TestResult 'PathListRecovery_ExitCode0' ($result10d.ExitCode -eq 0) "Expected exit code 0 after path-list recovery, got $($result10d.ExitCode). Output: $($result10d.Output)"

    $fixedContent = Get-Content -Path $filePath -Raw
    Write-TestResult 'PathListRecovery_WorktreeUpdated' ($fixedContent -match "Copyright \(c\) $currentYear wallstop") "Expected worktree header year $currentYear"

    Push-Location $repo10d
    try {
        $dirty = git status --short -- CommittedBad.cs
    }
    finally {
        Pop-Location
    }
    Write-TestResult 'PathListRecovery_DirtyForRecommit' (($dirty -join "`n") -match 'CommittedBad\.cs') 'Expected recovered file to be dirty so the bad pushed commit can be amended/recommitted'
}
finally {
    Remove-Item -Path $repo10d -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 10f: EOL drift should be auto-fixed and re-staged
Write-Host "`nTest group: EOL auto-fix" -ForegroundColor Magenta
$repo10e = New-TestRepo -ConfigurePushDefaults
try {
    Add-FakePrettierPackage -RepoPath $repo10e
    $packagePath = Join-Path $repo10e 'package.json'
    [System.IO.File]::WriteAllBytes(
        $packagePath,
        [System.Text.UTF8Encoding]::new($false).GetBytes("{`r`n  `"name`": `"fixture`"`r`n}`r`n")
    )

    Push-Location $repo10e
    try {
        git add package.json
    }
    finally {
        Pop-Location
    }

    $result10e = Invoke-Preflight -RepoPath $repo10e -Arguments @('-Fix', '-Paths', 'package.json')
    Write-TestResult 'EolFix_ExitCode0' ($result10e.ExitCode -eq 0) "Expected exit code 0 after EOL fix, got $($result10e.ExitCode). Output: $($result10e.Output)"

    $fixedText = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($packagePath))
    Write-TestResult 'EolFix_WorktreeUsesLf' (-not $fixedText.Contains("`r`n")) 'Expected package.json to be normalized to LF in worktree'

    Push-Location $repo10e
    try {
        $stagedText = git show ':package.json' | Out-String
    }
    finally {
        Pop-Location
    }
    Write-TestResult 'EolFix_StagedBlobUsesLf' (-not $stagedText.Contains("`r`n")) 'Expected staged package.json blob to be normalized to LF'
}
finally {
    Remove-Item -Path $repo10e -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 10f: cspell.json config drift should be auto-fixed and re-staged
Write-Host "`nTest group: cspell config auto-fix" -ForegroundColor Magenta
$repo10f = New-TestRepo -ConfigurePushDefaults
try {
    Add-FakePrettierPackage -RepoPath $repo10f
    $cspellPath = Join-Path $repo10f 'cspell.json'
    Set-Content -Path $cspellPath -Value @'
{
  "caseSensitive": false,
  "words": [
    "Wallstop",
    "wallstop"
  ],
  "dictionaryDefinitions": []
}
'@ -Encoding UTF8

    Push-Location $repo10f
    try {
        git add cspell.json
    }
    finally {
        Pop-Location
    }

    $result10f = Invoke-Preflight -RepoPath $repo10f -Arguments @('-Fix', '-Paths', 'cspell.json')
    Write-TestResult 'CspellConfigFix_ExitCode0' ($result10f.ExitCode -eq 0) "Expected exit code 0 after cspell config fix, got $($result10f.ExitCode). Output: $($result10f.Output)"

    $fixedConfig = Get-Content -Path $cspellPath -Raw | ConvertFrom-Json
    Write-TestResult 'CspellConfigFix_WorktreeDeduped' (@($fixedConfig.words).Count -eq 1) 'Expected cspell.json duplicate word to be removed in worktree'

    Push-Location $repo10f
    try {
        $stagedConfig = (git show ':cspell.json' | Out-String) | ConvertFrom-Json
    }
    finally {
        Pop-Location
    }
    Write-TestResult 'CspellConfigFix_StagedDeduped' (@($stagedConfig.words).Count -eq 1) 'Expected cspell.json duplicate word to be removed in staged blob'
}
finally {
    Remove-Item -Path $repo10f -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 11: Missing push.autoSetupRemote should fail preflight
Write-Host "`nTest group: git push config detection" -ForegroundColor Magenta
$repo11 = New-TestRepo
try {
    $result11 = Invoke-Preflight -RepoPath $repo11 -Arguments @('-Paths', 'nonexistent/should-not-match')
    Write-TestResult 'PushConfigMissing_ExitCode1' ($result11.ExitCode -eq 1) "Expected exit code 1 when push.autoSetupRemote unset, got $($result11.ExitCode). Output: $($result11.Output)"
    Write-TestResult 'PushConfigMissing_ErrorMessage' ($result11.Output -match 'Git push defaults are not configured') 'Expected push config error message'
    Write-TestResult 'PushConfigMissing_RemediationHint' ($result11.Output -match 'npm run agent:preflight:fix') 'Expected remediation hint referencing agent:preflight:fix'
}
finally {
    Remove-Item -Path $repo11 -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 12: -Fix mode should restore push config and re-run green
Write-Host "`nTest group: git push config auto-fix" -ForegroundColor Magenta
$repo12 = New-TestRepo
try {
    $result12 = Invoke-Preflight -RepoPath $repo12 -Arguments @('-Fix', '-Paths', 'nonexistent/should-not-match')
    Write-TestResult 'PushConfigFix_ExitCode0' ($result12.ExitCode -eq 0) "Expected exit code 0 after -Fix, got $($result12.ExitCode). Output: $($result12.Output)"

    Push-Location $repo12
    try {
        $autoSetup = ([string](git config --local --get push.autoSetupRemote 2>$null)).Trim()
        $pushDefault = ([string](git config --local --get push.default 2>$null)).Trim()
    }
    finally {
        Pop-Location
    }
    Write-TestResult 'PushConfigFix_AutoSetupRemote' ($autoSetup -eq 'true') "Expected push.autoSetupRemote=true after -Fix, got '$autoSetup'"
    Write-TestResult 'PushConfigFix_PushDefault' ($pushDefault -eq 'simple') "Expected push.default=simple after -Fix, got '$pushDefault'"

    $result12b = Invoke-Preflight -RepoPath $repo12 -Arguments @('-Paths', 'nonexistent/should-not-match')
    Write-TestResult 'PushConfigFix_RerunGreen' ($result12b.ExitCode -eq 0) "Expected rerun to be green, got $($result12b.ExitCode). Output: $($result12b.Output)"
}
finally {
    Remove-Item -Path $repo12 -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 13: pre-push.txt at repo root should fail and -Fix removes it (gitignored)
Write-Host "`nTest group: stray pre-push.txt detection" -ForegroundColor Magenta
$repo13 = New-TestRepo -ConfigurePushDefaults -GitIgnorePatterns @('pre-push.txt*')
try {
    $hooksDir = Join-Path $repo13 '.githooks'
    New-Item -ItemType Directory -Path $hooksDir -Force | Out-Null
    Set-Content -Path (Join-Path $hooksDir 'pre-push') -Value '#!/usr/bin/env bash' -Encoding UTF8
    Set-Content -Path (Join-Path $repo13 'pre-push.txt') -Value 'fatal: ... no upstream branch' -Encoding UTF8

    $result13 = Invoke-Preflight -RepoPath $repo13 -Arguments @('-Paths', 'nonexistent/should-not-match')
    Write-TestResult 'StrayPrePushTxt_ExitCode1' ($result13.ExitCode -eq 1) "Expected exit code 1 when pre-push.txt exists, got $($result13.ExitCode). Output: $($result13.Output)"
    Write-TestResult 'StrayPrePushTxt_ErrorMessage' ($result13.Output -match 'Stray git-hook artifact file') 'Expected stray artifact error message'
    Write-TestResult 'StrayPrePushTxt_ListsPath' ($result13.Output -match 'pre-push\.txt') 'Expected pre-push.txt path in output'

    $result13fix = Invoke-Preflight -RepoPath $repo13 -Arguments @('-Fix', '-Paths', 'nonexistent/should-not-match')
    Write-TestResult 'StrayPrePushTxtFix_ExitCode0' ($result13fix.ExitCode -eq 0) "Expected exit code 0 after -Fix, got $($result13fix.ExitCode). Output: $($result13fix.Output)"
    Write-TestResult 'StrayPrePushTxtFix_FileDeleted' (-not (Test-Path (Join-Path $repo13 'pre-push.txt'))) 'Expected pre-push.txt to be deleted by -Fix'
}
finally {
    Remove-Item -Path $repo13 -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 14: .githooks/pre-merge-commit.tmp should fail and -Fix removes it (gitignored)
Write-Host "`nTest group: stray hook tmp artifact detection" -ForegroundColor Magenta
$repo14 = New-TestRepo -ConfigurePushDefaults -GitIgnorePatterns @('*.tmp')
try {
    $hooksDir = Join-Path $repo14 '.githooks'
    New-Item -ItemType Directory -Path $hooksDir -Force | Out-Null
    Set-Content -Path (Join-Path $hooksDir 'pre-merge-commit') -Value '#!/usr/bin/env bash' -Encoding UTF8
    Set-Content -Path (Join-Path $hooksDir 'pre-merge-commit.tmp') -Value 'temp output' -Encoding UTF8

    $result14 = Invoke-Preflight -RepoPath $repo14 -Arguments @('-Paths', 'nonexistent/should-not-match')
    Write-TestResult 'StrayHookTmp_ExitCode1' ($result14.ExitCode -eq 1) "Expected exit code 1 when hook .tmp exists, got $($result14.ExitCode). Output: $($result14.Output)"
    Write-TestResult 'StrayHookTmp_ListsPath' ($result14.Output -match 'pre-merge-commit\.tmp') 'Expected .githooks/pre-merge-commit.tmp in output'

    $result14fix = Invoke-Preflight -RepoPath $repo14 -Arguments @('-Fix', '-Paths', 'nonexistent/should-not-match')
    Write-TestResult 'StrayHookTmpFix_ExitCode0' ($result14fix.ExitCode -eq 0) "Expected exit code 0 after -Fix, got $($result14fix.ExitCode). Output: $($result14fix.Output)"
    Write-TestResult 'StrayHookTmpFix_FileDeleted' (-not (Test-Path (Join-Path $hooksDir 'pre-merge-commit.tmp'))) 'Expected pre-merge-commit.tmp to be deleted by -Fix'
}
finally {
    Remove-Item -Path $repo14 -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 14b: .githooks/pre-push.txt should fail and -Fix removes it (gitignored)
Write-Host "`nTest group: stray hook txt artifact detection" -ForegroundColor Magenta
$repo14b = New-TestRepo -ConfigurePushDefaults -GitIgnorePatterns @('.githooks/*.txt')
try {
    $hooksDir = Join-Path $repo14b '.githooks'
    New-Item -ItemType Directory -Path $hooksDir -Force | Out-Null
    Set-Content -Path (Join-Path $hooksDir 'pre-push') -Value '#!/usr/bin/env bash' -Encoding UTF8
    Set-Content -Path (Join-Path $hooksDir 'pre-push.txt') -Value 'redirected output' -Encoding UTF8

    $result14b = Invoke-Preflight -RepoPath $repo14b -Arguments @('-Paths', 'nonexistent/should-not-match')
    Write-TestResult 'StrayHookTxt_ExitCode1' ($result14b.ExitCode -eq 1) "Expected exit code 1 when hook .txt exists, got $($result14b.ExitCode). Output: $($result14b.Output)"
    Write-TestResult 'StrayHookTxt_ListsPath' ($result14b.Output -match 'pre-push\.txt') 'Expected .githooks/pre-push.txt in output'

    $result14bFix = Invoke-Preflight -RepoPath $repo14b -Arguments @('-Fix', '-Paths', 'nonexistent/should-not-match')
    Write-TestResult 'StrayHookTxtFix_ExitCode0' ($result14bFix.ExitCode -eq 0) "Expected exit code 0 after -Fix, got $($result14bFix.ExitCode). Output: $($result14bFix.Output)"
    Write-TestResult 'StrayHookTxtFix_FileDeleted' (-not (Test-Path (Join-Path $hooksDir 'pre-push.txt'))) 'Expected pre-push.txt to be deleted by -Fix'
}
finally {
    Remove-Item -Path $repo14b -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 14c: .githooks/notes.txt should not be treated as an artifact when notes is not a hook name
Write-Host "`nTest group: non-hook .githooks artifact pattern safety" -ForegroundColor Magenta
$repo14c = New-TestRepo -ConfigurePushDefaults -GitIgnorePatterns @('.githooks/*.txt')
try {
    $hooksDir = Join-Path $repo14c '.githooks'
    New-Item -ItemType Directory -Path $hooksDir -Force | Out-Null
    Set-Content -Path (Join-Path $hooksDir 'pre-push') -Value '#!/usr/bin/env bash' -Encoding UTF8
    Set-Content -Path (Join-Path $hooksDir 'notes.txt') -Value 'local note' -Encoding UTF8

    $result14c = Invoke-Preflight -RepoPath $repo14c -Arguments @('-Fix', '-Paths', 'nonexistent/should-not-match')
    Write-TestResult 'NonHookGithooksTxt_ExitCode0' ($result14c.ExitCode -eq 0) "Expected exit code 0 when ignored .githooks/notes.txt is not hook-named, got $($result14c.ExitCode). Output: $($result14c.Output)"
    Write-TestResult 'NonHookGithooksTxt_Preserved' (Test-Path (Join-Path $hooksDir 'notes.txt')) 'Expected .githooks/notes.txt to be preserved'
}
finally {
    Remove-Item -Path $repo14c -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 15: Generalized discovery - a custom hook file drives detection of <name>.txt
Write-Host "`nTest group: generalized stray artifact discovery" -ForegroundColor Magenta
$repo15 = New-TestRepo -ConfigurePushDefaults
try {
    $hooksDir = Join-Path $repo15 '.githooks'
    New-Item -ItemType Directory -Path $hooksDir -Force | Out-Null
    Set-Content -Path (Join-Path $hooksDir 'post-checkout') -Value '#!/usr/bin/env bash' -Encoding UTF8
    Set-Content -Path (Join-Path $repo15 'post-checkout.txt') -Value 'redirected output' -Encoding UTF8

    $result15 = Invoke-Preflight -RepoPath $repo15 -Arguments @('-Paths', 'nonexistent/should-not-match')
    Write-TestResult 'GeneralizedDiscovery_ExitCode1' ($result15.ExitCode -eq 1) "Expected exit code 1 when post-checkout.txt exists, got $($result15.ExitCode). Output: $($result15.Output)"
    Write-TestResult 'GeneralizedDiscovery_CatchesNewHook' ($result15.Output -match 'post-checkout\.txt') 'Expected discovery to catch artifact derived from custom hook name'
}
finally {
    Remove-Item -Path $repo15 -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 16: -Fix must NOT delete stray-pattern files that are not gitignored
Write-Host "`nTest group: gitignore-safety gate on auto-deletion" -ForegroundColor Magenta
# Deliberately construct a repo WITHOUT a .gitignore entry for pre-push.txt.
# The file still matches the error-log pattern, so it must be reported as a
# failure — but -Fix must refuse to delete it (safety).
$repo16 = New-TestRepo -ConfigurePushDefaults
try {
    $hooksDir = Join-Path $repo16 '.githooks'
    New-Item -ItemType Directory -Path $hooksDir -Force | Out-Null
    Set-Content -Path (Join-Path $hooksDir 'pre-push') -Value '#!/usr/bin/env bash' -Encoding UTF8
    $strayPath = Join-Path $repo16 'pre-push.txt'
    Set-Content -Path $strayPath -Value 'intentional user note; not gitignored' -Encoding UTF8

    # Sanity: confirm the file is NOT gitignored in this test repo.
    Push-Location $repo16
    try {
        & git check-ignore -q -- 'pre-push.txt' 2>$null | Out-Null
        $preCheckExit = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    Write-TestResult 'GitignoreSafety_PreconditionNotIgnored' ($preCheckExit -eq 1) "Expected pre-push.txt to be NOT gitignored in test repo (git check-ignore exit 1), got $preCheckExit"

    # Check-only mode: must fail with differentiated messaging.
    $result16 = Invoke-Preflight -RepoPath $repo16 -Arguments @('-Paths', 'nonexistent/should-not-match')
    Write-TestResult 'GitignoreSafety_CheckExitCode1' ($result16.ExitCode -eq 1) "Expected exit code 1 when stray pre-push.txt is not gitignored, got $($result16.ExitCode). Output: $($result16.Output)"
    Write-TestResult 'GitignoreSafety_CheckListsPath' ($result16.Output -match 'pre-push\.txt') 'Expected pre-push.txt in check-only output'
    Write-TestResult 'GitignoreSafety_CheckDifferentiates' ($result16.Output -match 'NOT gitignored') 'Expected check-only output to surface the "NOT gitignored" category'
    Write-TestResult 'GitignoreSafety_CheckFileStillExists' (Test-Path -LiteralPath $strayPath) 'Expected pre-push.txt to still exist after check-only run'

    # -Fix mode: must NOT delete and MUST still fail.
    $result16fix = Invoke-Preflight -RepoPath $repo16 -Arguments @('-Fix', '-Paths', 'nonexistent/should-not-match')
    Write-TestResult 'GitignoreSafety_FixExitCode1' ($result16fix.ExitCode -eq 1) "Expected -Fix exit code 1 (refused delete counts as failure), got $($result16fix.ExitCode). Output: $($result16fix.Output)"
    Write-TestResult 'GitignoreSafety_FixDidNotDelete' (Test-Path -LiteralPath $strayPath) 'Expected pre-push.txt to NOT be deleted under -Fix when not gitignored'
    Write-TestResult 'GitignoreSafety_FixMentionsGitignore' ($result16fix.Output -match 'gitignore') 'Expected -Fix output to reference gitignore safety/remediation'
}
finally {
    Remove-Item -Path $repo16 -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 16b: -Fix must not delete root hook-shaped logs just because a broad
# global ignore pattern such as *.log matches them. Root auto-recovery is scoped
# to explicit hook redirection extensions (.txt/.out/.err); .log/.tmp recovery
# is only automatic under .githooks/<hook>.*.
Write-Host "`nTest group: root hook-shaped log safety" -ForegroundColor Magenta
$repo16b = New-TestRepo -ConfigurePushDefaults -GitIgnorePatterns @('*.log')
try {
    $hooksDir = Join-Path $repo16b '.githooks'
    New-Item -ItemType Directory -Path $hooksDir -Force | Out-Null
    Set-Content -Path (Join-Path $hooksDir 'pre-commit') -Value '#!/usr/bin/env bash' -Encoding UTF8
    Set-Content -Path (Join-Path $hooksDir 'pre-push') -Value '#!/usr/bin/env bash' -Encoding UTF8
    $rootCommitLog = Join-Path $repo16b 'pre-commit.log'
    $rootPushLog = Join-Path $repo16b 'pre-push.log'
    Set-Content -Path $rootCommitLog -Value 'local diagnostic log' -Encoding UTF8
    Set-Content -Path $rootPushLog -Value 'local diagnostic log' -Encoding UTF8

    $result16b = Invoke-Preflight -RepoPath $repo16b -Arguments @('-Fix', '-Paths', 'nonexistent/should-not-match')
    Write-TestResult 'RootHookLogSafety_FixExitCode0' ($result16b.ExitCode -eq 0) "Expected -Fix exit 0 when only root hook-shaped logs exist, got $($result16b.ExitCode). Output: $($result16b.Output)"
    Write-TestResult 'RootHookLogSafety_PreservedCommitLog' (Test-Path -LiteralPath $rootCommitLog) 'Expected root pre-commit.log to be preserved under broad *.log ignore'
    Write-TestResult 'RootHookLogSafety_PreservedPushLog' (Test-Path -LiteralPath $rootPushLog) 'Expected root pre-push.log to be preserved under broad *.log ignore'
}
finally {
    Remove-Item -Path $repo16b -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 17: git check-ignore path-normalization (gitignored strays are correctly
# classified even when the stray artifact file lives in a directory whose name
# case mismatches the gitignore pattern on case-insensitive filesystems, AND
# when agent-preflight has to pass ABSOLUTE paths through to git check-ignore).
# Before the fix, absolute Windows-style paths could be silently misclassified
# as "not gitignored" — causing auto-delete to refuse a file the user wanted
# cleaned up. The helper `ConvertTo-GitRelativePosixPath` normalizes once per
# input path before hand-off.
Write-Host "`nTest group: git check-ignore path normalization" -ForegroundColor Magenta
$repo17 = New-TestRepo -ConfigurePushDefaults -GitIgnorePatterns @('.githooks/pre-commit.log', '.githooks/pre-push.log')
try {
    $hooksDir = Join-Path $repo17 '.githooks'
    New-Item -ItemType Directory -Path $hooksDir -Force | Out-Null
    Set-Content -Path (Join-Path $hooksDir 'pre-commit') -Value '#!/usr/bin/env bash' -Encoding UTF8
    Set-Content -Path (Join-Path $hooksDir 'pre-push') -Value '#!/usr/bin/env bash' -Encoding UTF8
    # Create stray artifacts matching the gitignore pattern. agent-preflight
    # must detect them, classify them as gitignored, and (with -Fix) delete
    # them. Without the path-normalization fix, git check-ignore could miss
    # them when called with absolute paths on Windows.
    $strayCommit = Join-Path $hooksDir 'pre-commit.log'
    $strayPush = Join-Path $hooksDir 'pre-push.log'
    Set-Content -Path $strayCommit -Value 'stale log' -Encoding UTF8
    Set-Content -Path $strayPush -Value 'stale log' -Encoding UTF8

    # Check-only: both files listed, both marked as gitignored.
    $result17 = Invoke-Preflight -RepoPath $repo17 -Arguments @('-Paths', 'nonexistent/should-not-match')
    Write-TestResult 'PathNormalize_CheckExitCode1' ($result17.ExitCode -eq 1) "Expected exit 1 when gitignored strays exist, got $($result17.ExitCode). Output: $($result17.Output)"
    Write-TestResult 'PathNormalize_CheckListsCommit' ($result17.Output -match 'pre-commit\.log') 'Expected pre-commit.log in check-only output'
    Write-TestResult 'PathNormalize_CheckListsPush' ($result17.Output -match 'pre-push\.log') 'Expected pre-push.log in check-only output'
    # Crucial regression: the output must classify the strays as "gitignored"
    # (safe to auto-delete) rather than "NOT gitignored" (refuse to delete).
    # This is the exact bit that the path-normalization fix ensures.
    Write-TestResult 'PathNormalize_CheckClassifiedAsIgnored' ($result17.Output -match 'safe to auto-delete') 'Expected check-only output to classify strays as "gitignored (safe to auto-delete)"'
    Write-TestResult 'PathNormalize_CheckNotMisclassified' (-not ($result17.Output -match 'NOT gitignored')) 'Expected check-only output to NOT misclassify gitignored strays as "NOT gitignored"'

    # -Fix: both files deleted, run succeeds.
    $result17fix = Invoke-Preflight -RepoPath $repo17 -Arguments @('-Fix', '-Paths', 'nonexistent/should-not-match')
    Write-TestResult 'PathNormalize_FixExitCode0' ($result17fix.ExitCode -eq 0) "Expected -Fix exit 0 after cleaning gitignored strays, got $($result17fix.ExitCode). Output: $($result17fix.Output)"
    Write-TestResult 'PathNormalize_FixDeletedCommitLog' (-not (Test-Path -LiteralPath $strayCommit)) 'Expected pre-commit.log to be deleted by -Fix when gitignored'
    Write-TestResult 'PathNormalize_FixDeletedPushLog' (-not (Test-Path -LiteralPath $strayPush)) 'Expected pre-push.log to be deleted by -Fix when gitignored'
}
finally {
    Remove-Item -Path $repo17 -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 18: Set-RepoGitPushDefaults dot-source contract. agent-preflight and
# install-hooks rely on dot-sourcing git-push-defaults-helpers.ps1 and calling
# Set-RepoGitPushDefaults directly — rather than spawning a pwsh subprocess.
# This test verifies the helper's contract in isolation:
#   - Succeeds on a fresh repo.
#   - Persists push.autoSetupRemote=true and push.default=simple.
#   - Is idempotent (second call is a no-op that still reports Success=$true).
#   - Returns a result hashtable with Success / Errors / Values fields.
Write-Host "`nTest group: Set-RepoGitPushDefaults dot-source" -ForegroundColor Magenta
$repo18 = New-TestRepo  # Intentionally NOT -ConfigurePushDefaults — we want
                        # the helper to actually apply the config.
try {
    $helperScript = Join-Path (Join-Path $repo18 'scripts') 'git-push-defaults-helpers.ps1'
    Write-TestResult 'DotSource_HelperExists' (Test-Path -LiteralPath $helperScript) "Expected helper at $helperScript"

    # Dot-source into a child pwsh process so we don't pollute the test's
    # current function table. Using `& pwsh` here is the correct production
    # invocation path for this isolated test — we're explicitly verifying
    # the dot-source contract from a fresh host.
    $probe = @"
Set-StrictMode -Version Latest
`$ErrorActionPreference = 'Stop'
. '$($helperScript -replace "'", "''")'
`$result = Set-RepoGitPushDefaults -RepoRoot '$($repo18 -replace "'", "''")'
if (-not `$result.Success) {
    Write-Host "FAIL: Success=`$(`$result.Success); Errors=`$(`$result.Errors -join '; ')"
    exit 1
}
# Verify persisted config values.
Push-Location '$($repo18 -replace "'", "''")'
try {
    `$actualAuto = (& git config --local --get push.autoSetupRemote 2>`$null).Trim()
    `$actualDefault = (& git config --local --get push.default 2>`$null).Trim()
    if (`$actualAuto -ne 'true') { Write-Host "FAIL: push.autoSetupRemote=`$actualAuto"; exit 2 }
    if (`$actualDefault -ne 'simple') { Write-Host "FAIL: push.default=`$actualDefault"; exit 3 }
} finally { Pop-Location }
# Idempotent second call.
`$result2 = Set-RepoGitPushDefaults -RepoRoot '$($repo18 -replace "'", "''")'
if (-not `$result2.Success) { Write-Host "FAIL-IDEMPOTENT: Errors=`$(`$result2.Errors -join '; ')"; exit 4 }
Write-Host "OK"
exit 0
"@

    $probeFile = Join-Path $repo18 'probe-set-git-push-defaults.ps1'
    Set-Content -Path $probeFile -Value $probe -Encoding UTF8
    $probeOutput = & pwsh -NoProfile -File $probeFile 2>&1
    $probeExit = $LASTEXITCODE
    $probeJoined = ($probeOutput -join "`n")
    Write-TestResult 'DotSource_Succeeded' ($probeExit -eq 0) "Expected exit 0 from probe; got $probeExit. Output: $probeJoined"
    Write-TestResult 'DotSource_ReportsOK' ($probeJoined -match '^OK$|\nOK$|\nOK\r?$|^OK\r?$') "Expected probe to print 'OK' on success. Output: $probeJoined"
}
finally {
    Remove-Item -Path $repo18 -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 19: configure-git-defaults.ps1 CLI exit-code contract. The helper-based
# tests above cover the in-process dot-source path; this test exercises the
# script as an actual subprocess the way an end user (or install-hooks wrapper)
# invokes it. Guards:
#   - Exit 0 on a fresh git repo.
#   - Stdout shows push.autoSetupRemote=true and push.default=simple.
#   - Exit non-zero when the RepoRoot is not a git work tree (so the helper's
#     defensive branches surface a real failure signal, not a silent success).
Write-Host "`nTest group: configure-git-defaults.ps1 CLI contract" -ForegroundColor Magenta
$repo19 = New-TestRepo  # Intentionally NOT -ConfigurePushDefaults — the CLI
                        # must apply the config itself.
$nonRepo19 = Join-Path ([System.IO.Path]::GetTempPath()) "configure-git-defaults-notgit-$([System.Guid]::NewGuid().ToString('N').Substring(0,8))"
try {
    # --- Cli_SuccessExitCodeZero / OutputContains* ---
    $configureScript = Join-Path (Join-Path $repo19 'scripts') 'configure-git-defaults.ps1'
    Write-TestResult 'Cli_ConfigureScriptExists' (Test-Path -LiteralPath $configureScript) "Expected configure-git-defaults.ps1 at $configureScript"

    $cliOutput = & pwsh -NoProfile -File $configureScript -RepoRoot $repo19 2>&1
    $cliExit = $LASTEXITCODE
    $cliJoined = ($cliOutput -join "`n")
    Write-TestResult 'Cli_SuccessExitCodeZero' ($cliExit -eq 0) "Expected exit 0 from configure-git-defaults.ps1 against a fresh repo; got $cliExit. Output: $cliJoined"
    Write-TestResult 'Cli_OutputContainsAutoSetupRemote' ($cliJoined -match 'push\.autoSetupRemote\s*=\s*true') "Expected stdout to report push.autoSetupRemote=true. Output: $cliJoined"
    Write-TestResult 'Cli_OutputContainsPushDefault' ($cliJoined -match 'push\.default\s*=\s*simple') "Expected stdout to report push.default=simple. Output: $cliJoined"

    # --- Cli_PersistsConfigValues ---
    # Defense-in-depth: after the subprocess returned, the local git config
    # must reflect the persisted values (not just that the subprocess printed
    # them).
    Push-Location $repo19
    try {
        $actualAuto = ([string](& git config --local --get push.autoSetupRemote 2>$null)).Trim()
        $actualDefault = ([string](& git config --local --get push.default 2>$null)).Trim()
    }
    finally {
        Pop-Location
    }
    Write-TestResult 'Cli_PersistsAutoSetupRemote' ($actualAuto -eq 'true') "Expected push.autoSetupRemote='true' after subprocess; got '$actualAuto'."
    Write-TestResult 'Cli_PersistsPushDefault' ($actualDefault -eq 'simple') "Expected push.default='simple' after subprocess; got '$actualDefault'."

    # --- Cli_NonGitDirFailsNonZero ---
    # When the directory isn't a git work tree, the helper's defensive
    # branch returns Success=$false and the CLI wrapper must surface that
    # as a non-zero exit code.
    New-Item -ItemType Directory -Path $nonRepo19 -Force | Out-Null
    $cliOutput2 = & pwsh -NoProfile -File $configureScript -RepoRoot $nonRepo19 2>&1
    $cliExit2 = $LASTEXITCODE
    $cliJoined2 = ($cliOutput2 -join "`n")
    Write-TestResult 'Cli_NonGitDirFailsNonZero' ($cliExit2 -ne 0) "Expected non-zero exit when RepoRoot is not a git work tree; got $cliExit2. Output: $cliJoined2"
    Write-TestResult 'Cli_NonGitDirErrorsMentionNotGit' ($cliJoined2 -match 'Not a git repository') "Expected stderr/stdout to include 'Not a git repository' diagnostic. Output: $cliJoined2"
}
finally {
    Remove-Item -Path $repo19 -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $nonRepo19 -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host ('=' * 60)
Write-Host ("Tests passed: {0}" -f $script:TestsPassed) -ForegroundColor Green
Write-Host ("Tests failed: {0}" -f $script:TestsFailed) -ForegroundColor $(if ($script:TestsFailed -gt 0) { 'Red' } else { 'Green' })

if ($script:FailedTests.Count -gt 0) {
    Write-Host 'Failed tests:' -ForegroundColor Red
    foreach ($failed in $script:FailedTests) {
        Write-Host "  - $failed" -ForegroundColor Red
    }
}

exit $script:TestsFailed
