Param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestsPassed = 0
$script:TestsFailed = 0
$script:FailedTests = @()

$repoSourceRoot = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
$validatorSource = Join-Path $repoSourceRoot 'scripts/validate-git-push-config.ps1'
$gitPathHelpersSource = Join-Path $repoSourceRoot 'scripts/git-path-helpers.ps1'

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

function New-TestRepo {
    param(
        [switch]$ConfigurePushDefaults,
        [string[]]$GitIgnorePatterns
    )

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "validate-git-push-config-$([System.Guid]::NewGuid().ToString('N').Substring(0, 8))"
    $scriptsDir = Join-Path $tempRoot 'scripts'
    $hooksDir = Join-Path $tempRoot '.githooks'
    New-Item -ItemType Directory -Path $scriptsDir -Force | Out-Null
    New-Item -ItemType Directory -Path $hooksDir -Force | Out-Null

    Copy-Item $validatorSource (Join-Path $scriptsDir 'validate-git-push-config.ps1') -Force
    Copy-Item $gitPathHelpersSource (Join-Path $scriptsDir 'git-path-helpers.ps1') -Force
    Set-Content -Path (Join-Path $hooksDir 'pre-commit') -Value "#!/usr/bin/env bash`necho ok`n" -Encoding UTF8

    if ($null -ne $GitIgnorePatterns -and $GitIgnorePatterns.Count -gt 0) {
        Set-Content -Path (Join-Path $tempRoot '.gitignore') -Value $GitIgnorePatterns -Encoding UTF8
    }

    Push-Location $tempRoot
    try {
        git init -q
        git config user.email 'test@example.com'
        git config user.name 'Test'
        git add .
        git commit -q -m 'init'
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

function Invoke-Validator {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoPath,
        [string]$WorkingDirectory = $RepoPath
    )

    $validatorPath = Join-Path $RepoPath 'scripts/validate-git-push-config.ps1'
    Push-Location $WorkingDirectory
    try {
        $output = & pwsh -NoProfile -File $validatorPath 2>&1
        return @{
            ExitCode = $LASTEXITCODE
            Output = ($output -join "`n")
        }
    }
    finally {
        Pop-Location
    }
}

Write-Host 'Testing validate-git-push-config.ps1...' -ForegroundColor White

Write-Host "`nTest group: clean repository passes" -ForegroundColor Magenta
$repo1 = New-TestRepo -ConfigurePushDefaults
try {
    $result1 = Invoke-Validator -RepoPath $repo1
    Write-TestResult 'Pass_CleanRepoExitCodeZero' ($result1.ExitCode -eq 0) "Expected exit code 0, got $($result1.ExitCode). Output: $($result1.Output)"
    Write-TestResult 'Pass_CleanRepoReportsSuccess' ($result1.Output -match 'checks passed') "Expected success message. Output: $($result1.Output)"
}
finally {
    Remove-Item -Path $repo1 -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`nTest group: .githooks txt artifacts are detected" -ForegroundColor Magenta
$repo2 = New-TestRepo -ConfigurePushDefaults -GitIgnorePatterns @('.githooks/*.txt')
try {
    $artifactPath = Join-Path $repo2 '.githooks/pre-commit.txt'
    Set-Content -Path $artifactPath -Value 'redirected hook output' -Encoding UTF8

    $result2 = Invoke-Validator -RepoPath $repo2
    Write-TestResult 'Fail_GithooksTxtArtifactExitCodeNonZero' ($result2.ExitCode -ne 0) "Expected non-zero exit code, got $($result2.ExitCode). Output: $($result2.Output)"
    Write-TestResult 'Fail_GithooksTxtArtifactIsReported' ($result2.Output -match 'pre-commit\.txt') "Expected output to mention .githooks/pre-commit.txt. Output: $($result2.Output)"
    Write-TestResult 'Fail_GithooksTxtArtifactClassifiedAsGitignored' ($result2.Output -match 'gitignored') "Expected output to classify the artifact as gitignored. Output: $($result2.Output)"
}
finally {
    Remove-Item -Path $repo2 -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`nTest group: non-hook .githooks txt files are ignored by artifact scan" -ForegroundColor Magenta
$repo3 = New-TestRepo -ConfigurePushDefaults -GitIgnorePatterns @('.githooks/*.txt')
try {
    $artifactPath = Join-Path $repo3 '.githooks/notes.txt'
    Set-Content -Path $artifactPath -Value 'local note' -Encoding UTF8

    $result3 = Invoke-Validator -RepoPath $repo3
    Write-TestResult 'Pass_NonHookGithooksTxtExitCodeZero' ($result3.ExitCode -eq 0) "Expected exit code 0, got $($result3.ExitCode). Output: $($result3.Output)"
    Write-TestResult 'Pass_NonHookGithooksTxtNotReported' (-not ($result3.Output -match 'notes\.txt')) "Expected output not to mention .githooks/notes.txt. Output: $($result3.Output)"
}
finally {
    Remove-Item -Path $repo3 -Recurse -Force -ErrorAction SilentlyContinue
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
