Param(
    [switch]$VerboseOutput,
    [switch]$LicensePipeOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'isolated-fixture-runspace.ps1')

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
    # agent-preflight resolves license years by forking this, its sibling, rather than running a
    # second copy of the walk (#681). A fixture without it makes the license check skip, which the
    # LicenseHeaderDrift_Message assertion below turns red -- so that test is this copy's control.
    Copy-Item (Join-Path $repoRoot 'scripts/license-year-lib.sh') (Join-Path $scriptsDir 'license-year-lib.sh') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/run-node-bin.js') (Join-Path $scriptsDir 'run-node-bin.js') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/run-prettier.js') (Join-Path $scriptsDir 'run-prettier.js') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/fix-markdown-fence-languages.ps1') (Join-Path $scriptsDir 'fix-markdown-fence-languages.ps1') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/lint-duplicate-usings.ps1') (Join-Path $scriptsDir 'lint-duplicate-usings.ps1') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/lint-tests.ps1') (Join-Path $scriptsDir 'lint-tests.ps1') -Force
    # No .config/dotnet-tools.json is copied on purpose: these fixtures are synthetic repositories
    # whose C# is written to exercise .meta and staging behavior, not formatting. agent-preflight
    # passes -SkipWhenUnavailable, so the CSharpier step reports a skip here instead of failing.
    Copy-Item (Join-Path $repoRoot 'scripts/lint-csharp-format.ps1') (Join-Path $scriptsDir 'lint-csharp-format.ps1') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/comment-stripping.ps1') (Join-Path $scriptsDir 'comment-stripping.ps1') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/check-eol.ps1') (Join-Path $scriptsDir 'check-eol.ps1') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/normalize-eol.ps1') (Join-Path $scriptsDir 'normalize-eol.ps1') -Force
    Copy-Item (Join-Path $repoRoot 'scripts/lint-changelog.ps1') (Join-Path $scriptsDir 'lint-changelog.ps1') -Force
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
        [hashtable]$EnvOverrides,
        [switch]$UseCli
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
        if ($UseCli) {
            $output = & pwsh -NoProfile -File scripts/agent-preflight.ps1 @Arguments 2>&1
            return @{
                ExitCode = $LASTEXITCODE
                Output = ($output -join "`n")
            }
        }

        return Invoke-IsolatedPreflight -ScriptPath (Join-Path $RepoPath 'scripts/agent-preflight.ps1') -Arguments $Arguments
    }
    finally {
        Pop-Location

        if ($null -ne $EnvOverrides) {
            foreach ($key in $EnvOverrides.Keys) {
                if ($null -eq $previousValues[$key]) {
                    Remove-Item -Path "Env:$key" -ErrorAction SilentlyContinue
                }
                else {
                    [Environment]::SetEnvironmentVariable($key, $previousValues[$key])
                }
            }
        }
    }
}

function Invoke-IsolatedPreflight {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ScriptPath,
        [string[]]$Arguments
    )

    if ($null -eq $Arguments) {
        $Arguments = @()
    }

    $runner = [System.Management.Automation.PowerShell]::Create()
    try {
        $null = $runner.AddScript('param($WorkingDirectory) Set-Location -LiteralPath $WorkingDirectory').AddArgument((Split-Path -Parent (Split-Path -Parent $ScriptPath)))
        $null = $runner.Invoke()
        if ($runner.HadErrors) {
            throw "Runspace failed to enter fixture repository: $($runner.Streams.Error -join [Environment]::NewLine)"
        }

        $runner.Commands.Clear()
        $null = $runner.AddCommand($ScriptPath)
        for ($index = 0; $index -lt $Arguments.Count; $index++) {
            $name = $Arguments[$index].TrimStart('-')
            if ($name -eq 'Fix') {
                $null = $runner.AddParameter('Fix')
                continue
            }

            if ($name -ne 'Paths' -and $name -ne 'PathList') {
                throw "Unsupported preflight fixture argument: $($Arguments[$index])"
            }

            if ($index + 1 -ge $Arguments.Count) {
                throw "Missing value for preflight fixture argument: $($Arguments[$index])"
            }

            $index++
            $null = $runner.AddParameter($name, $Arguments[$index])
        }

        $result = $runner.Invoke()
        $output = [System.Collections.Generic.List[string]]::new()
        foreach ($item in $result) {
            $output.Add($item.ToString())
        }
        foreach ($item in $runner.Streams.Information) {
            $output.Add($item.MessageData.ToString())
        }
        foreach ($item in $runner.Streams.Warning) {
            $output.Add($item.Message)
        }
        foreach ($item in $runner.Streams.Error) {
            $output.Add($item.ToString())
        }

        if ($runner.Streams.Error.Count -gt 0) {
            throw "Preflight emitted a PowerShell error: $($output -join [Environment]::NewLine)"
        }

        $exitCode = $runner.Runspace.SessionStateProxy.GetVariable('LASTEXITCODE')
        if ($null -eq $exitCode) {
            throw 'Preflight did not set an exit code.'
        }

        return @{
            ExitCode = [int]$exitCode
            Output = ($output -join "`n")
        }
    }
    finally {
        $runner.Dispose()
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
        [ValidateSet('Pass', 'FailLint', 'VerifyFileList')]
        [string]$Mode
    )

    $exitCode = if ($Mode -eq 'FailLint') { '1' } else { '0' }
    $binDir = Join-Path $RepoPath 'node_modules/cspell/bin'
    New-Item -ItemType Directory -Path $binDir -Force | Out-Null
    Set-Content -Path (Join-Path $RepoPath 'node_modules/cspell/package.json') -Value '{"bin":{"cspell":"bin/cspell.cjs"}}' -Encoding ascii
    $verifyBody = @'
  // The real cspell resolves --file-list entries relative to the LIST FILE, not the working
  // directory. The preflight writes that list to the system temp directory, so repo-relative
  // entries silently became /tmp/<path> and every file was skipped while the summary still read
  // clean. Refusing an unresolvable entry here is what makes that regression fail the suite.
  const fs = require("fs");
  const path = require("path");
  const index = process.argv.indexOf("--file-list");
  if (index < 0 || !process.argv[index + 1]) {
    console.error("cspell stub: expected --file-list");
    process.exit(2);
  }
  const listPath = process.argv[index + 1];
  const listDir = path.dirname(path.resolve(listPath));
  const entries = fs
    .readFileSync(listPath, "utf8")
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
  if (entries.length === 0) {
    console.error("cspell stub: the file list was empty");
    process.exit(2);
  }
  for (const entry of entries) {
    const resolved = path.isAbsolute(entry) ? entry : path.join(listDir, entry);
    if (!fs.existsSync(resolved)) {
      console.error("cspell stub: file list entry does not resolve: " + resolved);
      process.exit(2);
    }
  }
  process.exit(0);
'@

    $lintBody = if ($Mode -eq 'VerifyFileList') { $verifyBody } else { "  process.exit(__EXIT_CODE__);" }

    $script = @'
#!/usr/bin/env node
if (process.argv.includes("--version")) {
  console.log("10.0.0");
  process.exit(0);
}
if (process.argv.includes("lint")) {
__LINT_BODY__
}
process.exit(0);
'@
    $script = $script.Replace('__LINT_BODY__', $lintBody)
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

function Test-LicensePipeDrain {
    $repoPath = New-TestRepo -ConfigurePushDefaults
    $process = $null
    try {
        $fileCount = 512
        $directoryName = 'PipeDrain-' + ('p' * 90)
        $directory = Join-Path $repoPath $directoryName
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $paths = [System.Collections.Generic.List[string]]::new()
        $currentYear = (Get-Date).Year
        for ($index = 0; $index -lt $fileCount; ++$index) {
            $className = 'PipeDrain' + $index
            $fileName = ('x' * 60) + $index.ToString('D4') + '.cs'
            $relativePath = $directoryName + '/' + $fileName
            $paths.Add($relativePath)
            $year = if ($index -eq $fileCount - 1) { $currentYear - 1 } else { $currentYear }
            [System.IO.File]::WriteAllText(
                (Join-Path $repoPath $relativePath),
                "// MIT License - Copyright (c) $year wallstop`n// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE`n`npublic sealed class $className {}`n"
            )
            [System.IO.File]::WriteAllText(
                (Join-Path $repoPath ($relativePath + '.meta')),
                "fileFormatVersion: 2`nguid: $([Guid]::NewGuid().ToString('N'))`n"
            )
        }
        $pathList = Join-Path $repoPath 'license-pipe-paths.txt'
        [System.IO.File]::WriteAllLines($pathList, $paths)
        $inputBytes = [System.Text.Encoding]::UTF8.GetByteCount(($paths -join "`0") + "`0")
        Write-TestResult 'LicensePipeDrain_ExceedsPipeCapacity' ($inputBytes -gt 65536) "Expected more than 64 KiB of real path input; got $inputBytes bytes."

        $info = [System.Diagnostics.ProcessStartInfo]::new()
        $info.FileName = (Get-Command pwsh -ErrorAction Stop).Source
        foreach ($argument in @('-NoProfile', '-File', (Join-Path $repoPath 'scripts/agent-preflight.ps1'), '-PathList', $pathList)) {
            [void]$info.ArgumentList.Add($argument)
        }
        $info.WorkingDirectory = $repoPath
        $info.UseShellExecute = $false
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        $process = [System.Diagnostics.Process]::Start($info)
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        $completed = $process.WaitForExit(30000)
        if (-not $completed) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $output = $outputTask.GetAwaiter().GetResult() + $errorTask.GetAwaiter().GetResult()
        Write-TestResult 'LicensePipeDrain_CompletesWithinTimeout' $completed "Actual preflight did not complete within 30 seconds. Output: $output"
        if ($completed) {
            Write-TestResult 'LicensePipeDrain_LastFileChecked' ($process.ExitCode -eq 1 -and $output.Contains($paths[$fileCount - 1]) -and $output.Contains('License year header issues detected')) "Expected the last file's license mismatch, got exit $($process.ExitCode). Output: $output"
            Write-TestResult 'LicensePipeDrain_LibraryNotSkipped' (-not $output.Contains('Skipped the license year check')) "The actual license library must execute. Output: $output"
        }
    }
    finally {
        if ($null -ne $process) {
            if (-not $process.HasExited) {
                $process.Kill($true)
                $process.WaitForExit()
            }
            $process.Dispose()
        }
        Remove-Item -LiteralPath $repoPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-LicensePipeDrain
if ($LicensePipeOnly) {
    if ($script:TestsFailed -gt 0) {
        exit 1
    }
    exit 0
}

Write-Host 'Testing agent-preflight.ps1...' -ForegroundColor White

Write-Host "`nTest group: isolated runspace harness" -ForegroundColor Magenta
$harnessRepo = New-TestRepo -ConfigurePushDefaults
$harnessPath = Join-Path $harnessRepo 'scripts/runspace-harness.ps1'
try {
    foreach ($expectedExit in @(0, 1)) {
        Set-Content -LiteralPath $harnessPath -Value "Write-Host 'harness-output'; exit $expectedExit"
        $harnessResult = Invoke-IsolatedPreflight -ScriptPath $harnessPath
        Write-TestResult "RunspaceExit$expectedExit" ($harnessResult.ExitCode -eq $expectedExit -and $harnessResult.Output.Contains('harness-output')) 'Expected exact exit code and host output'
    }

    foreach ($control in @(
        @{ Name = 'TerminatingError'; Content = "throw 'harness-terminating-error'"; ExpectedError = 'harness-terminating-error' },
        @{ Name = 'NonterminatingError'; Content = "Write-Error 'harness-stream-error' -ErrorAction Continue; exit 0"; ExpectedError = 'harness-stream-error' }
    )) {
        Set-Content -LiteralPath $harnessPath -Value $control.Content
        $rejected = $false
        try {
            $null = Invoke-IsolatedPreflight -ScriptPath $harnessPath
        }
        catch {
            $rejected = $_.Exception.Message.Contains($control.ExpectedError)
        }
        Write-TestResult "Runspace$($control.Name)" $rejected 'Expected PowerShell error to fail the harness'
    }

    Set-Content -LiteralPath $harnessPath -Value "Write-Host 'harness-missing-exit'"
    $missingExitRejected = $false
    try {
        $null = Invoke-IsolatedPreflight -ScriptPath $harnessPath
    }
    catch {
        $missingExitRejected = $_.Exception.Message.Contains('did not set an exit code')
    }
    Write-TestResult 'RunspaceMissingExitFails' $missingExitRejected 'Expected a script without an explicit exit to fail the harness'

    Set-Content -LiteralPath $harnessPath -Value '$global:preflightHarnessState = 1; exit 1'
    $null = Invoke-IsolatedPreflight -ScriptPath $harnessPath
    Set-Content -LiteralPath $harnessPath -Value 'if (Get-Variable preflightHarnessState -Scope Global -ErrorAction SilentlyContinue) { exit 1 }; exit 0'
    $harnessResult = Invoke-IsolatedPreflight -ScriptPath $harnessPath
    Write-TestResult 'RunspacesAreIsolated' ($harnessResult.ExitCode -eq 0) 'Expected a fresh global session for each invocation'
}
finally {
    Remove-Item -LiteralPath $harnessRepo -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 1: No changed files should exit successfully
Write-Host "`nTest group: baseline behavior" -ForegroundColor Magenta
$repo1 = New-TestRepo -ConfigurePushDefaults
try {
    $result1 = Invoke-Preflight -RepoPath $repo1 -Arguments @() -UseCli
    Write-TestResult 'NoChanges_ExitCode0' ($result1.ExitCode -eq 0) "Expected exit code 0, got $($result1.ExitCode)"
    Write-TestResult 'NoChanges_Message' ($result1.Output -match 'No changed files detected') 'Expected no-changes message'
}
finally {
    Remove-Item -Path $repo1 -Recurse -Force -ErrorAction SilentlyContinue
}

# Test 2: Missing meta file should fail
Write-Host "`nTest group: missing meta detection" -ForegroundColor Magenta
foreach ($metaSubject in @('Runtime/MyFeature.cs', 'Samples~/Example/MyFeature.cs')) {
    $repo2 = New-TestRepo -ConfigurePushDefaults
    try {
        $runtimeDir = Join-Path $repo2 (Split-Path -Parent $metaSubject)
        New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
        $filePath = Join-Path $runtimeDir 'MyFeature.cs'
        Set-Content -Path $filePath -Value 'public sealed class MyFeature {}' -Encoding UTF8

        $result2 = Invoke-Preflight -RepoPath $repo2 -Arguments @('-Paths', $metaSubject) -UseCli:($metaSubject -eq 'Runtime/MyFeature.cs')
        Write-TestResult "MissingMeta_ExitCode1_$metaSubject" ($result2.ExitCode -eq 1) "Expected exit code 1, got $($result2.ExitCode)"
        Write-TestResult "MissingMeta_ErrorMessage_$metaSubject" ($result2.Output -match 'Missing \.meta files detected') 'Expected missing meta error message'
        Write-TestResult "MissingMeta_ListsPath_$metaSubject" ($result2.Output -match [regex]::Escape($metaSubject)) 'Expected missing path to be listed in output'
    }
    finally {
        Remove-Item -Path $repo2 -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Test 3: Fix mode should auto-generate missing meta files
Write-Host "`nTest group: auto-fix mode" -ForegroundColor Magenta
foreach ($sampleDirectory in @('Editor/Nested', 'Samples~/Example')) {
    $repo3 = New-TestRepo -ConfigurePushDefaults
    try {
        $editorNestedDir = Join-Path $repo3 $sampleDirectory
        New-Item -ItemType Directory -Path $editorNestedDir -Force | Out-Null
        $filePath = Join-Path $editorNestedDir 'Tool.cs'
        Set-Content -Path $filePath -Value 'public sealed class Tool {}' -Encoding UTF8

        Push-Location $repo3
        try {
            git add "$sampleDirectory/Tool.cs"
        }
        finally {
            Pop-Location
        }

        $result3 = Invoke-Preflight -RepoPath $repo3 -Arguments @('-Fix', '-Paths', "$sampleDirectory/Tool.cs")
        Write-TestResult 'FixMode_ExitCode0' ($result3.ExitCode -eq 0) "Expected exit code 0, got $($result3.ExitCode). Output: $($result3.Output)"
        Write-TestResult 'FixMode_FileMetaCreated' (Test-Path (Join-Path $repo3 "$sampleDirectory/Tool.cs.meta")) 'Expected file .meta to be created'
        Write-TestResult 'FixMode_DirMetaCreated' (Test-Path (Join-Path $repo3 "$sampleDirectory.meta")) 'Expected directory .meta to be created'
        $fileMetaContent3 = Get-Content -Path (Join-Path $repo3 "$sampleDirectory/Tool.cs.meta") -Raw
        Write-TestResult 'FixMode_FileMetaUsesMonoImporter' ($fileMetaContent3 -match 'MonoImporter:') 'Expected C# .meta to use MonoImporter'
        $agentPreflightContent3 = Get-Content -Path (Join-Path $repo3 'scripts/agent-preflight.ps1') -Raw
        Write-TestResult 'FixMode_MetaRecoveryDoesNotRequireBash' ($agentPreflightContent3 -notmatch 'bash .*generate-meta\.sh') 'Expected native PowerShell .meta generation, not bash generate-meta.sh'

        $staged3 = Get-StagedPaths -RepoPath $repo3
        Write-TestResult 'FixMode_FileMetaStaged' ($staged3 -contains "$sampleDirectory/Tool.cs.meta") 'Expected file .meta to be staged by -Fix mode'
        Write-TestResult 'FixMode_DirMetaStaged' ($staged3 -contains "$sampleDirectory.meta") 'Expected directory .meta to be staged by -Fix mode'
    }
    finally {
        Remove-Item -Path $repo3 -Recurse -Force -ErrorAction SilentlyContinue
    }
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

    $result3Newline = Invoke-Preflight -RepoPath $repo3Newline -Arguments @('-Fix') -UseCli
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

    $previousLockAttempts = [Environment]::GetEnvironmentVariable('GIT_LOCK_MAX_ATTEMPTS')
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
    Write-TestResult 'LockContention_EnvironmentRestored' ([Environment]::GetEnvironmentVariable('GIT_LOCK_MAX_ATTEMPTS') -eq $previousLockAttempts) 'Expected fixture override to be restored after the runspace'
}
finally {
    Remove-Item -Path $repo7 -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`nTest group: changed changelog semantic validation" -ForegroundColor Magenta
$changelogPrefix = "# Changelog`n`n## [Unreleased]`n`n### Added`n`n"
$validChangelog = $changelogPrefix + "- Add a useful feature.`n- Add another useful feature.`n"
$changelogCases = @(
    @{ Name = 'BlankBetweenEntries'; Body = $changelogPrefix + "- Add a useful feature.`n`n- Add another useful feature.`n"; ExitCode = 1; Diagnostic = 'Blank line between two entries' },
    @{ Name = 'LongEntry'; Body = $changelogPrefix + '- Add ' + ('useful ' * 50) + "features.`n"; ExitCode = 1; Diagnostic = 'rendered characters, over the 300 limit' },
    @{ Name = 'Valid'; Body = $validChangelog; ExitCode = 0; Diagnostic = 'CHANGELOG.md validation passed' },
    @{ Name = 'UnrelatedMarkdown'; Body = $changelogPrefix + '- Add ' + ('useful ' * 50) + "features.`n"; ExitCode = 0; Diagnostic = $null }
)
foreach ($case in $changelogCases) {
    foreach ($fixMode in @($false, $true)) {
        $changelogRepo = New-TestRepo -ConfigurePushDefaults
        try {
            Add-FakePrettierPackage -RepoPath $changelogRepo
            Add-FakeMarkdownlintPackage -RepoPath $changelogRepo
            $changelogPath = Join-Path $changelogRepo 'CHANGELOG.md'
            $readmePath = Join-Path $changelogRepo 'README.md'
            $baseline = if ($case.Name -eq 'UnrelatedMarkdown') { $case.Body } else { $changelogPrefix + "- Add the original feature.`n" }
            [System.IO.File]::WriteAllText($changelogPath, $baseline)
            [System.IO.File]::WriteAllText($readmePath, "# Fixture`n")
            Push-Location $changelogRepo
            try {
                git add .
                git -c user.email=test@example.com -c user.name=test commit -q -m 'fixture baseline'
                if ($case.Name -ne 'UnrelatedMarkdown') {
                    [System.IO.File]::WriteAllText($changelogPath, $case.Body)
                }
                [System.IO.File]::WriteAllText($readmePath, "# Changed fixture`n")
                git add -- CHANGELOG.md README.md
                $indexBefore = @(git ls-files --stage)
            }
            finally {
                Pop-Location
            }
            $changelogBefore = [System.IO.File]::ReadAllBytes($changelogPath)
            $arguments = if ($fixMode) { @('-Fix') } else { @() }
            $result = Invoke-Preflight -RepoPath $changelogRepo -Arguments $arguments
            $testName = "Changelog$($case.Name)$(if ($fixMode) { 'Fix' } else { 'Check' })"
            Write-TestResult "${testName}_ExitCode" ($result.ExitCode -eq $case.ExitCode) "Expected exit $($case.ExitCode), got $($result.ExitCode). Output: $($result.Output)"
            if ($null -ne $case.Diagnostic) {
                Write-TestResult "${testName}_Diagnostic" ($result.Output -match $case.Diagnostic) "Expected changelog diagnostic '$($case.Diagnostic)'. Output: $($result.Output)"
            }
            else {
                Write-TestResult "${testName}_Skipped" ($result.Output -notmatch '\[changelog-lint\]') "Unchanged CHANGELOG.md must not be linted. Output: $($result.Output)"
            }
            Push-Location $changelogRepo
            try {
                $indexAfter = @(git ls-files --stage)
            }
            finally {
                Pop-Location
            }
            Write-TestResult "${testName}_IndexPreserved" (($indexBefore -join "`n") -ceq ($indexAfter -join "`n")) 'Expected every staged path and blob to be preserved'
            Write-TestResult "${testName}_ChangelogPreserved" (Test-ByteArrayEqual -Expected $changelogBefore -Actual ([System.IO.File]::ReadAllBytes($changelogPath))) 'Semantic validation must not rewrite changelog prose'
        }
        finally {
            Remove-Item -Path $changelogRepo -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
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

# Test 8c: the file list handed to cspell must actually resolve
Write-Host "`nTest group: spelling file list resolves" -ForegroundColor Magenta
$repo8c = New-TestRepo -ConfigurePushDefaults
try {
    Set-Content -Path (Join-Path $repo8c 'README.md') -Value 'File list resolution.' -Encoding UTF8
    Add-FakePrettierPackage -RepoPath $repo8c
    Add-FakeMarkdownlintPackage -RepoPath $repo8c
    Add-FakeCspellPackage -RepoPath $repo8c -Mode VerifyFileList
    $result8c = Invoke-Preflight -RepoPath $repo8c -Arguments @('-Paths', 'README.md')

    # Before the fix this failed: the list lived in the system temp directory and held repo-relative
    # paths, so cspell resolved every one against /tmp, skipped them all, and still printed a clean
    # summary. A misspelling in a changed file passed the local gate and failed in CI.
    Write-TestResult 'SpellingFileList_Resolves' ($result8c.ExitCode -eq 0) "The spelling file list did not resolve. Output: $($result8c.Output)"
}
finally {
    Remove-Item -Path $repo8c -Recurse -Force -ErrorAction SilentlyContinue
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

# Test 8bb: -Fix must update an intentionally force-added, ignored staged file
# without making that path poison the formatter's complete staging batch.
Write-Host "`nTest group: ignored staged file auto-fix" -ForegroundColor Magenta
$repo8bb = New-TestRepo -ConfigurePushDefaults -GitIgnorePatterns @('progress/')
try {
    $progressDir = Join-Path $repo8bb 'progress'
    New-Item -ItemType Directory -Path $progressDir -Force | Out-Null
    $progressPath = Join-Path $progressDir 'session.md'
    Set-Content -Path $progressPath -Value '# Session evidence' -Encoding UTF8
    Add-FakePrettierPackage -RepoPath $repo8bb
    Add-FakeMarkdownlintPackage -RepoPath $repo8bb

    Push-Location $repo8bb
    try {
        git add -f progress/session.md
    }
    finally {
        Pop-Location
    }

    $result8bb = Invoke-Preflight `
        -RepoPath $repo8bb `
        -Arguments @('-Fix', '-Paths', 'progress/session.md')
    Write-TestResult 'IgnoredStagedFix_ExitCode0' ($result8bb.ExitCode -eq 0) "Expected exit code 0, got $($result8bb.ExitCode). Output: $($result8bb.Output)"

    Push-Location $repo8bb
    try {
        $stagedPaths = @(git diff --cached --name-only --diff-filter=ACMR)
    }
    finally {
        Pop-Location
    }
    Write-TestResult 'IgnoredStagedFix_RemainsStaged' ($stagedPaths -contains 'progress/session.md') 'Expected ignored progress log to remain staged after auto-fix'
}
finally {
    Remove-Item -Path $repo8bb -Recurse -Force -ErrorAction SilentlyContinue
}

# XML-only edits must receive the same formatter check and safe auto-fix as CI.
Write-Host "`nTest group: XML-only CSharpier formatting and partial staging" -ForegroundColor Magenta
$repoXml = New-TestRepo -ConfigurePushDefaults
try {
    $configurationDirectory = Join-Path $repoXml '.config'
    $null = New-Item -ItemType Directory -Path $configurationDirectory -Force
    $repositoryRoot = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
    Copy-Item (Join-Path $repositoryRoot '.config/dotnet-tools.json') (Join-Path $configurationDirectory 'dotnet-tools.json')
    Copy-Item (Join-Path $repositoryRoot '.editorconfig') (Join-Path $repoXml '.editorconfig')
    $xmlPath = Join-Path $configurationDirectory 'link.xml'
    [IO.File]::WriteAllText($xmlPath, '<linker><assembly fullname="Test.Reflection" preserve="all" /></linker>')
    [IO.File]::WriteAllText((Join-Path $repoXml 'scripts/lint-duplicate-usings.ps1'), "throw 'XML formatting must not run C# using checks.'")
    Push-Location $repoXml
    try {
        git add .config/link.xml
    }
    finally {
        Pop-Location
    }

    $xmlCheck = Invoke-Preflight -UseCli -RepoPath $repoXml -Arguments @('-Paths', '.config/link.xml')
    Write-TestResult 'XmlFormatting_UnformattedCheckFails' ($xmlCheck.ExitCode -eq 1 -and $xmlCheck.Output -match 'Was not formatted') "Expected real CSharpier to reject unformatted XML. Output: $($xmlCheck.Output)"
    $xmlFix = Invoke-Preflight -UseCli -RepoPath $repoXml -Arguments @('-Fix', '-Paths', '.config/link.xml')
    Write-TestResult 'XmlFormatting_FixPasses' ($xmlFix.ExitCode -eq 0) "Expected XML-only fix to succeed without C# checks. Output: $($xmlFix.Output)"
    $xmlFormatted = [IO.File]::ReadAllText($xmlPath)
    Write-TestResult 'XmlFormatting_ChangesFile' ($xmlFormatted -match "\r?\n$" -and $xmlFormatted.Contains('Test.Reflection')) 'Expected real CSharpier to format the XML while retaining its declaration'
    Push-Location $repoXml
    try {
        $xmlStagedBefore = git show ':.config/link.xml' | Out-String
        $xmlUnstagedPaths = @(git diff --name-only -- .config/link.xml)
    }
    finally {
        Pop-Location
    }
    Write-TestResult 'XmlFormatting_FixStagesResult' ($xmlUnstagedPaths.Count -eq 0 -and $xmlStagedBefore.Contains('Test.Reflection')) 'Expected formatted XML to replace its staged copy'
    $xmlRecheck = Invoke-Preflight -UseCli -RepoPath $repoXml -Arguments @('-Paths', '.config/link.xml')
    Write-TestResult 'XmlFormatting_FormattedCheckPasses' ($xmlRecheck.ExitCode -eq 0) "Expected formatted XML to pass. Output: $($xmlRecheck.Output)"

    $additionalXmlPaths = @('config', 'csproj', 'props', 'targets', 'slnx', 'xaml', 'axaml') | ForEach-Object { ".config/link.$_" }
    foreach ($additionalXmlPath in $additionalXmlPaths) {
        [IO.File]::WriteAllText((Join-Path $repoXml $additionalXmlPath), '<root><value>one</value></root>')
    }
    $additionalXmlCheck = Invoke-Preflight -UseCli -RepoPath $repoXml -Arguments (@('-Paths') + $additionalXmlPaths)
    $additionalXmlFilesReported = @($additionalXmlPaths | Where-Object { $additionalXmlCheck.Output -notmatch ([regex]::Escape($_) + ' - Was not formatted') }).Count -eq 0
    Write-TestResult 'XmlFormatting_SupportedExtensionsChecked' ($additionalXmlCheck.ExitCode -eq 1 -and $additionalXmlFilesReported) "Expected every supported XML extension to reach CSharpier. Output: $($additionalXmlCheck.Output)"
    $additionalXmlFix = Invoke-Preflight -UseCli -RepoPath $repoXml -Arguments (@('-Fix', '-Paths') + $additionalXmlPaths)
    Write-TestResult 'XmlFormatting_SupportedExtensionsFixed' ($additionalXmlFix.ExitCode -eq 0) "Expected supported XML extensions to format without C# checks. Output: $($additionalXmlFix.Output)"

    [IO.File]::WriteAllText((Join-Path $configurationDirectory 'unsupported.resx'), 'Not XML and not a supported formatter extension.')
    $unsupportedXmlCheck = Invoke-Preflight -UseCli -RepoPath $repoXml -Arguments @('-Paths', '.config/unsupported.resx')
    Write-TestResult 'XmlFormatting_UnsupportedExtensionExcluded' ($unsupportedXmlCheck.ExitCode -eq 0 -and $unsupportedXmlCheck.Output -notmatch 'Checking CSharpier') "Expected unsupported XML-like files to stay outside the formatter. Output: $($unsupportedXmlCheck.Output)"

    [IO.File]::WriteAllText($xmlPath, '<linker><assembly fullname="Test.Unstaged" preserve="all" /></linker>')
    $xmlWorktreeBefore = [IO.File]::ReadAllBytes($xmlPath)
    $xmlPartialFix = Invoke-Preflight -UseCli -RepoPath $repoXml -Arguments @('-Fix', '-Paths', '.config/link.xml')
    Write-TestResult 'XmlFormatting_PartialStageRefused' ($xmlPartialFix.ExitCode -eq 1 -and $xmlPartialFix.Output -match 'Refusing to auto-stage whole file') "Expected formatter to refuse partially staged XML. Output: $($xmlPartialFix.Output)"
    Write-TestResult 'XmlFormatting_PartialStageWorktreeUnchanged' (Test-ByteArrayEqual -Expected $xmlWorktreeBefore -Actual ([IO.File]::ReadAllBytes($xmlPath))) 'Expected partial-staging refusal before mutation'
    Push-Location $repoXml
    try {
        $xmlStagedAfter = git show ':.config/link.xml' | Out-String
    }
    finally {
        Pop-Location
    }
    Write-TestResult 'XmlFormatting_PartialStageIndexUnchanged' ($xmlStagedAfter -ceq $xmlStagedBefore) 'Expected formatter to retain staged XML while refusing unstaged edits'
}
finally {
    Remove-Item -Path $repoXml -Recurse -Force -ErrorAction SilentlyContinue
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

# Test group: bounded license readers release files before any GC or process exit.
$licenseReaderControls = {
    param([string]$SourceRoot)
    $tokens = $null
    $errors = $null
    $preflightAst = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $SourceRoot 'scripts/agent-preflight.ps1'), [ref]$tokens, [ref]$errors)
    if ($errors.Count -ne 0) { throw 'License reader preflight source must parse.' }
    $yearFunctions = @($preflightAst.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-LicenseHeaderYear' }, $false))
    if ($yearFunctions.Count -ne 1) { throw 'Expected one production license-year reader.' }
    . ([scriptblock]::Create($yearFunctions[0].Extent.Text))

    $linterAst = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $SourceRoot 'scripts/lint-license-headers.ps1'), [ref]$tokens, [ref]$errors)
    if ($errors.Count -ne 0) { throw 'License header linter source must parse.' }
    foreach ($function in @($linterAst.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false))) {
        . ([scriptblock]::Create($function.Extent.Text))
    }
    $fileLoops = @($linterAst.FindAll({ param($node) $node -is [System.Management.Automation.Language.ForEachStatementAst] -and $node.Variable.VariablePath.UserPath -eq 'file' -and $node.Condition.Extent.Text -eq '$csFiles' }, $true))
    if ($fileLoops.Count -ne 1) { throw 'Expected one production license-header file loop.' }
    # Execute the real complete loop body, including classification and early continues.
    $linterLoop = [scriptblock]::Create('foreach ($file in @($readerPath)) ' + $fileLoops[0].Body.Extent.Text)
    $readerRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('license-reader-controls-' + [guid]::NewGuid().ToString('N'))
    [void][System.IO.Directory]::CreateDirectory($readerRoot)
    try {
        $readerCases = @(
            @{ Name = 'Year.FirstLine'; Reader = 'Year'; Text = "// Copyright (c) 2025`r`n// second`r`n// third`r`n"; Expected = '2025'; Encoding = [System.Text.UTF8Encoding]::new($false) },
            @{ Name = 'Year.SecondLineIgnored'; Reader = 'Year'; Text = "// first`n// Copyright (c) 2025`n// third`n"; Expected = ''; Encoding = [System.Text.UTF8Encoding]::new($false) },
            @{ Name = 'Year.BlankFirstLine'; Reader = 'Year'; Text = "`n// Copyright (c) 2025`n"; Expected = ''; Encoding = [System.Text.UTF8Encoding]::new($false) },
            @{ Name = 'Year.Empty'; Reader = 'Year'; Text = ''; Expected = ''; Encoding = [System.Text.UTF8Encoding]::new($false) },
            @{ Name = 'Year.Utf8Bom'; Reader = 'Year'; Text = "// Copyright (c) 2025`n// second`n"; Expected = '2025'; Encoding = [System.Text.UTF8Encoding]::new($true) },
            @{ Name = 'Year.Utf16Bom'; Reader = 'Year'; Text = "// Copyright (c) 2025`n// second`n"; Expected = '2025'; Encoding = [System.Text.Encoding]::Unicode },
            @{ Name = 'Linter.MitAt20'; Reader = 'Linter'; Text = ((@('') * 19 + @('// MIT License', '// tail')) -join "`n"); Expected = 'Accepted'; Encoding = [System.Text.UTF8Encoding]::new($false) },
            @{ Name = 'Linter.MitAt21'; Reader = 'Linter'; Text = ((@('') * 20 + @('// MIT License', '// tail')) -join "`n"); Expected = 'Violation'; Encoding = [System.Text.UTF8Encoding]::new($false) },
            @{ Name = 'Linter.OptOut'; Reader = 'Linter'; Text = "// No license header required`n// tail`n"; Expected = 'Skipped'; Encoding = [System.Text.UTF8Encoding]::new($false) },
            @{ Name = 'Linter.Empty'; Reader = 'Linter'; Text = ''; Expected = 'Skipped'; Encoding = [System.Text.UTF8Encoding]::new($false) },
            @{ Name = 'Linter.Utf8Bom'; Reader = 'Linter'; Text = "// MIT License`n// tail`n"; Expected = 'Accepted'; Encoding = [System.Text.UTF8Encoding]::new($true) },
            @{ Name = 'Linter.Utf16Bom'; Reader = 'Linter'; Text = "// MIT License`n// tail`n"; Expected = 'Accepted'; Encoding = [System.Text.Encoding]::Unicode }
        )
        foreach ($readerCase in $readerCases) {
            $readerPath = Join-Path $readerRoot ($readerCase.Name + '.cs')
            [System.IO.File]::WriteAllText($readerPath, $readerCase.Text, $readerCase.Encoding)
            $originalBytes = [System.IO.File]::ReadAllBytes($readerPath)
            if ($readerCase.Reader -eq 'Year') {
                $actual = Get-LicenseHeaderYear -Path $readerPath
            } else {
                $repoRoot = $readerRoot
                $VerboseOutput = $false
                $linesToCheck = 20
                $optOutMarker = 'No license header required'
                $checkedCount = 0
                $skippedCount = 0
                $violations = @()
                . $linterLoop
                $actual = if ($skippedCount -eq 1) { 'Skipped' } elseif ($violations.Count -eq 1) { 'Violation' } else { 'Accepted' }
            }
            Write-TestResult "LicenseReader.$($readerCase.Name).Classification" ($actual -ceq $readerCase.Expected) "Expected '$($readerCase.Expected)', got '$actual'."
            $exclusive = $null
            $releaseError = ''
            try {
                $exclusive = [System.IO.File]::Open($readerPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
                $exclusive.Dispose()
                $exclusive = $null
                [System.IO.File]::WriteAllBytes($readerPath, $originalBytes)
                [System.IO.File]::Delete($readerPath)
            } catch { $releaseError = $_.Exception.Message }
            finally { if ($null -ne $exclusive) { $exclusive.Dispose() } }
            Write-TestResult "LicenseReader.$($readerCase.Name).ReleasedBeforeGC" ($releaseError -eq '' -and -not [System.IO.File]::Exists($readerPath)) "Immediate exclusive open/rewrite/delete failed: $releaseError"
        }
        $missingPath = Join-Path $readerRoot 'missing.cs'
        Write-TestResult 'LicenseReader.Year.MissingReturnsEmpty' ((Get-LicenseHeaderYear -Path $missingPath) -ceq '') 'Expected preflight read errors to return empty.'
        $readerPath = $missingPath
        $linterThrew = $false
        try { . $linterLoop } catch { $linterThrew = $true }
        Write-TestResult 'LicenseReader.Linter.MissingPropagates' $linterThrew 'Expected linter read errors to propagate.'
    }
    finally {
        Remove-Item -LiteralPath $readerRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
& $licenseReaderControls -SourceRoot (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))

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

Write-Host "`nTest group: changed LLM reference size routing" -ForegroundColor Magenta
$llmSizeCases = @(
    @{ Name = 'Reference.Direct.198'; Path = '.llm/references/size-fixture.md'; Lines = 198; ExpectedExit = 0 },
    @{ Name = 'Reference.Direct.200'; Path = '.llm/references/size-fixture.md'; Lines = 200; ExpectedExit = 1 },
    @{ Name = 'Reference.Direct.215'; Path = '.llm/references/size-fixture.md'; Lines = 215; ExpectedExit = 1 },
    @{ Name = 'Reference.Nested.198'; Path = '.llm/references/nested/size-fixture.md'; Lines = 198; ExpectedExit = 0 },
    @{ Name = 'Reference.Nested.200'; Path = '.llm/references/nested/size-fixture.md'; Lines = 200; ExpectedExit = 1 },
    @{ Name = 'Reference.Nested.215'; Path = '.llm/references/nested/size-fixture.md'; Lines = 215; ExpectedExit = 1 },
    @{ Name = 'Skill.198'; Path = '.llm/skills/size-fixture.md'; Lines = 198; ExpectedExit = 0 },
    @{ Name = 'Skill.200'; Path = '.llm/skills/size-fixture.md'; Lines = 200; ExpectedExit = 1 },
    @{ Name = 'Context.198'; Path = '.llm/context.md'; Lines = 198; ExpectedExit = 0 },
    @{ Name = 'Context.200'; Path = '.llm/context.md'; Lines = 200; ExpectedExit = 1 }
)
foreach ($sizeCase in $llmSizeCases) {
    $sizeRepo = New-TestRepo -ConfigurePushDefaults
    try {
        Add-FakePrettierPackage -RepoPath $sizeRepo
        Add-FakeMarkdownlintPackage -RepoPath $sizeRepo
        [void][System.IO.Directory]::CreateDirectory((Join-Path $sizeRepo '.llm/skills'))
        $sizePath = Join-Path $sizeRepo $sizeCase.Path
        [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $sizePath))
        $lines = @('# Size routing fixture')
        for ($line = 1; $line -lt $sizeCase.Lines; $line++) { $lines += "- Fixture line $line" }
        [System.IO.File]::WriteAllText($sizePath, ($lines -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
        $sourceRepo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        Copy-Item -LiteralPath (Join-Path $sourceRepo 'scripts/lint-skill-sizes.ps1') -Destination (Join-Path $sizeRepo 'scripts/lint-skill-sizes.ps1')
        Set-Content -LiteralPath (Join-Path $sizeRepo 'scripts/lint-llm-instructions.ps1') -Value @'
Param([switch]$Fix, [switch]$VerboseOutput)
exit 0
'@ -Encoding UTF8

        $sizeResult = Invoke-Preflight -RepoPath $sizeRepo -Arguments @('-Paths', $sizeCase.Path)
        Write-TestResult "LlmSizeRouting.$($sizeCase.Name).ExactExit" ($sizeResult.ExitCode -eq $sizeCase.ExpectedExit) "Expected exit $($sizeCase.ExpectedExit), got $($sizeResult.ExitCode). Output: $($sizeResult.Output)"
        if ($sizeCase.ExpectedExit -eq 1) {
            $diagnostic = if ($sizeCase.Path -eq '.llm/context.md') {
                "[context-size] ERROR: context.md: $($sizeCase.Lines) lines (max: 199) - MUST reduce"
            } else {
                "[skill-sizes] ERROR: $($sizeCase.Path): $($sizeCase.Lines) lines (max: 199) - MUST split"
            }
            $observedOracle = $sizeResult.Output.Contains($diagnostic)
        } else {
            $diagnostic = if ($sizeCase.Path -eq '.llm/context.md') {
                '[context-size] Summary: context.md checked (198 lines)'
            } else {
                '[skill-sizes] Summary: 1 files checked'
            }
            $observedOracle = $sizeResult.Output.Contains($diagnostic) -and $sizeResult.Output.Contains('All files within size limits')
        }
        Write-TestResult "LlmSizeRouting.$($sizeCase.Name).RealSizeOracleObserved" $observedOracle "Expected real size-linter diagnostic '$diagnostic'. Output: $($sizeResult.Output)"
    }
    finally {
        Remove-Item -LiteralPath $sizeRepo -Recurse -Force -ErrorAction SilentlyContinue
    }
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
$probeFixture = $null
$probeSentinelsOwned = $false
try {
    $helperScript = Join-Path (Join-Path $repo18 'scripts') 'git-push-defaults-helpers.ps1'
    Write-TestResult 'DotSource_HelperExists' (Test-Path -LiteralPath $helperScript) "Expected helper at $helperScript"

    # A fresh runspace isolates the loaded functions while retaining native git.
    # The separate configure-git-defaults cases retain the real CLI boundary.
    $parentLocation = (Get-Location).Path
    $parentExit = $LASTEXITCODE
    $inheritedEnvironment = @{}
    foreach ($name in @('PATH', 'GIT_CONFIG_GLOBAL', 'GIT_CONFIG_SYSTEM', 'GIT_CONFIG_NOSYSTEM')) {
        $inheritedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
    }
    if (Get-Variable PushDefaultsProbeState -Scope Global -ErrorAction SilentlyContinue) { throw 'Probe sentinel already exists' }
    if (Get-Command PushDefaultsProbeParent -CommandType Function -ErrorAction SilentlyContinue) { throw 'Probe function sentinel already exists' }
    $probeSentinelsOwned = $true
    $global:PushDefaultsProbeState = 'parent'
    function global:PushDefaultsProbeParent { 'parent' }
    $probe = @"
param([hashtable]`$ExpectedEnvironment)
Set-StrictMode -Version Latest
`$ErrorActionPreference = 'Stop'
if (Get-Variable PushDefaultsProbeState -Scope Global -ErrorAction SilentlyContinue) { exit 5 }
if (Get-Command PushDefaultsProbeParent -CommandType Function -ErrorAction SilentlyContinue) { exit 5 }
`$global:PushDefaultsProbeState = 'child'
function global:PushDefaultsProbeParent { 'child' }
if (`$PSScriptRoot -cne '$($repo18 -replace "'", "''")' -or (Get-Location).Path -cne '$($repo18 -replace "'", "''")') { exit 6 }
if ((Get-Command git).CommandType -ne 'Application') { exit 7 }
foreach (`$name in `$ExpectedEnvironment.Keys) {
    if ([Environment]::GetEnvironmentVariable(`$name) -cne `$ExpectedEnvironment[`$name]) { exit 8 }
}
Write-Host 'ISOLATION-OK'
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
    $probeFixture = Start-IsolatedFixture -ScriptPath $probeFile -WorkingDirectory $repo18 -Parameters @{ ExpectedEnvironment = $inheritedEnvironment }
    $probeResult = Complete-IsolatedFixture -Fixture $probeFixture
    $probeExit = $probeResult.ExitCode
    $probeJoined = $probeResult.Output
    Write-TestResult 'DotSource_Succeeded' ($probeExit -eq 0) "Expected exit 0 from probe; got $probeExit. Output: $probeJoined"
    Write-TestResult 'DotSource_ReportsOK' ($probeJoined -match '^OK$|\nOK$|\nOK\r?$|^OK\r?$') "Expected probe to print 'OK' on success. Output: $probeJoined"
    Write-TestResult 'DotSource.FreshFunctionsGlobalsPathsAndNativeGitEnvironment' ($probeExit -eq 0 -and $probeJoined.Contains('ISOLATION-OK')) 'Expected fresh function/global state, exact script/location and inherited environment with native git'
    Write-TestResult 'DotSource.ParentFunctionAndGlobalPreserved' ($global:PushDefaultsProbeState -ceq 'parent' -and (PushDefaultsProbeParent) -ceq 'parent' -and -not (Get-Command Set-RepoGitPushDefaults -CommandType Function -ErrorAction SilentlyContinue)) 'Expected probe functions and globals to remain isolated'
    Write-TestResult 'DotSource.ParentLocationAndExitPreserved' ((Get-Location).Path -ceq $parentLocation -and $LASTEXITCODE -eq $parentExit) 'Expected parent location and native status unchanged'
    $environmentPreserved = $true
    foreach ($name in $inheritedEnvironment.Keys) {
        if ([Environment]::GetEnvironmentVariable($name) -cne $inheritedEnvironment[$name]) { $environmentPreserved = $false }
    }
    Write-TestResult 'DotSource.ParentEnvironmentPreserved' $environmentPreserved 'Expected shared process environment to remain unchanged'
    Write-TestResult 'DotSource.FixtureDisposedAndClosed' ($probeFixture.Disposed -and $probeFixture.Runspace.RunspaceStateInfo.State -eq 'Closed') 'Expected completed fixture disposal'
    Test-IsolatedFixtureHarness
}
finally {
    try {
        if ($null -ne $probeFixture) { Stop-IsolatedFixture -Fixture $probeFixture }
    }
    finally {
        if ($probeSentinelsOwned) {
            Remove-Variable PushDefaultsProbeState -Scope Global -ErrorAction SilentlyContinue
            Remove-Item Function:\PushDefaultsProbeParent -ErrorAction SilentlyContinue
        }
        Remove-Item -Path $repo18 -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-TestResult 'DotSource.OwnedSentinelsRemoved' (-not (Get-Variable PushDefaultsProbeState -Scope Global -ErrorAction SilentlyContinue) -and -not (Get-Command PushDefaultsProbeParent -CommandType Function -ErrorAction SilentlyContinue)) 'Expected owned global variable and function cleanup before later cases'

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
