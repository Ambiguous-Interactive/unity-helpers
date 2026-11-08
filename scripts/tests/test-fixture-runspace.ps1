param([switch]$VerboseOutput)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:TestsPassed = 0
$script:TestsFailed = 0
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('fixture controls ' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
. (Join-Path $PSScriptRoot 'isolated-fixture-runspace.ps1')

function Write-TestResult {
    param([string]$TestName, [bool]$Passed, [string]$Message = '')
    if ($Passed) {
        ++$script:TestsPassed
        Write-Host "[PASS] $TestName"
    } else {
        ++$script:TestsFailed
        Write-Host "[FAIL] $TestName $Message"
    }
}

function New-FixtureRoot {
    $directory = Join-Path $tempRoot ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $directory 'scripts') | Out-Null
    return $directory
}

function Test-OverlappingFixtures {
    $parentLocation = (Get-Location).Path
    $releasePath = Join-Path $tempRoot 'release-controls'
    $controls = @()
    try {
        foreach ($label in @('first', 'second')) {
            $directory = New-FixtureRoot
            $path = Join-Path $directory 'scripts/overlap-control.ps1'
            $readyPath = Join-Path $directory 'ready'
            Set-Content -LiteralPath $path -Value @'
param($Label, $ReadyPath, $ReleasePath)
if (Get-Variable OverlapFixtureState -Scope Global -ErrorAction SilentlyContinue) { exit 7 }
$global:OverlapFixtureState = $Label
Set-Content -LiteralPath $ReadyPath -Value $Label
$deadline = [DateTime]::UtcNow.AddSeconds(20)
while (-not (Test-Path -LiteralPath $ReleasePath)) {
    if ([DateTime]::UtcNow -gt $deadline) { throw 'overlap control timed out' }
    Start-Sleep -Milliseconds 10
}
if ($global:OverlapFixtureState -cne $Label) { exit 7 }
Write-Host "label:$Label"
Write-Host "working:$((Get-Location).Path)"
Write-Host "script:$PSScriptRoot"
Write-Host "command:$PSCommandPath"
exit 0
'@
            $fixture = Start-IsolatedFixture -ScriptPath $path -WorkingDirectory $directory -Parameters @{
                Label = $label; ReadyPath = $readyPath; ReleasePath = $releasePath
            }
            $controls += @{ Label = $label; Directory = $directory; Path = $path; ReadyPath = $readyPath; Fixture = $fixture }
        }
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        while (@($controls | Where-Object { -not (Test-Path -LiteralPath $_.ReadyPath) }).Count -gt 0 -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 10
        }
        $overlap = @($controls | Where-Object {
            (Test-Path -LiteralPath $_.ReadyPath) -and $_.Fixture.Runner.InvocationStateInfo.State -eq 'Running'
        }).Count -eq 2
        Write-TestResult 'Runspace.IndependentFixturesOverlap' $overlap
        Set-Content -LiteralPath $releasePath -Value 'release'
        foreach ($control in $controls) {
            $result = Complete-IsolatedFixture -Fixture $control.Fixture
            $expectedOutput = @(
                "label:$($control.Label)", "working:$($control.Directory)",
                "script:$(Split-Path -Parent $control.Path)", "command:$($control.Path)"
            ) -join [Environment]::NewLine
            Write-TestResult "Runspace.OverlappingGlobalLocationAndScriptPath[$($control.Label)]" (
                $result.ExitCode -eq 0 -and $result.Output -ceq $expectedOutput
            ) "exit $($result.ExitCode); output: $($result.Output)"
            Write-TestResult "Runspace.DisposedAfterCompletion[$($control.Label)]" (
                $control.Fixture.Disposed -and $control.Fixture.Runspace.RunspaceStateInfo.State -eq 'Closed'
            )
        }
        Write-TestResult 'Runspace.ParentLocationPreserved' ((Get-Location).Path -ceq $parentLocation)
    }
    finally {
        $cleanupErrors = @()
        foreach ($control in $controls) {
            try { Stop-IsolatedFixture -Fixture $control.Fixture }
            catch { $cleanupErrors += $_.Exception.Message }
        }
        if ($cleanupErrors.Count -gt 0) { throw ($cleanupErrors -join '; ') }
    }

    foreach ($control in @(
        @{ Name = 'ErrorExitZero'; Content = "Write-Error 'async-error' -ErrorAction Continue; exit 0"; Diagnostic = 'PowerShell error' },
        @{ Name = 'ErrorExitOne'; Content = "Write-Error 'async-error' -ErrorAction Continue; exit 1"; Diagnostic = 'PowerShell error' },
        @{ Name = 'Throw'; Content = "throw 'async-throw'; exit 0"; Diagnostic = 'async-throw' }
    )) {
        $path = Join-Path $tempRoot 'async-error-control.ps1'
        Set-Content -LiteralPath $path -Value $control.Content
        $fixture = Start-IsolatedFixture -ScriptPath $path -WorkingDirectory $tempRoot
        $rejected = $false
        try { $null = Complete-IsolatedFixture -Fixture $fixture }
        catch { $rejected = $_.Exception.Message.Contains($control.Diagnostic) }
        finally { Stop-IsolatedFixture -Fixture $fixture }
        Write-TestResult "Runspace.AsyncRejects$($control.Name)AndDisposes" (
            $rejected -and $fixture.Disposed -and $fixture.Runspace.RunspaceStateInfo.State -eq 'Closed'
        )
    }

    $path = Join-Path $tempRoot 'async-cancel-control.ps1'
    $readyPath = Join-Path $tempRoot 'cancel-ready'
    Set-Content -LiteralPath $path -Value @'
param($ReadyPath)
Set-Content -LiteralPath $ReadyPath -Value 'ready'
Start-Sleep -Seconds 20
exit 0
'@
    $activeCancellationSafe = $true
    for ($iteration = 0; $iteration -lt 20; $iteration++) {
        Remove-Item -LiteralPath $readyPath -Force -ErrorAction SilentlyContinue
        $fixture = Start-IsolatedFixture -ScriptPath $path -Parameters @{ ReadyPath = $readyPath }
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds(10)
            while (-not (Test-Path -LiteralPath $readyPath) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 10 }
            $wasRunning = (Test-Path -LiteralPath $readyPath) -and $fixture.Runner.InvocationStateInfo.State -eq 'Running'
            Stop-IsolatedFixture -Fixture $fixture
            $activeCancellationSafe = $activeCancellationSafe -and $wasRunning -and $fixture.Disposed -and
                $fixture.Invocation.IsCompleted -and $fixture.Runspace.RunspaceStateInfo.State -eq 'Closed'
        }
        finally { Stop-IsolatedFixture -Fixture $fixture }
    }
    Write-TestResult 'Runspace.CancelsAndDisposesActiveFixture' $activeCancellationSafe

    $immediateCancellationSafe = $true
    for ($iteration = 0; $iteration -lt 32; $iteration++) {
        $fixture = Start-IsolatedFixture -ScriptPath $path -Parameters @{ ReadyPath = $readyPath }
        try {
            Stop-IsolatedFixture -Fixture $fixture
            $immediateCancellationSafe = $immediateCancellationSafe -and $fixture.Disposed -and
                $fixture.Invocation.IsCompleted -and $fixture.Runspace.RunspaceStateInfo.State -eq 'Closed'
        }
        finally { Stop-IsolatedFixture -Fixture $fixture }
    }
    Write-TestResult 'Runspace.ImmediateCancellationCompletesBeforeDisposal' $immediateCancellationSafe

    Remove-Item -LiteralPath $readyPath -Force -ErrorAction SilentlyContinue
    $locationBefore = (Get-Location).Path
    $setupRejected = $false
    try {
        $fixture = Start-IsolatedFixture -ScriptPath $path -Parameters @{ ReadyPath = $readyPath } -WorkingDirectory (Join-Path $tempRoot 'missing-directory')
        Stop-IsolatedFixture -Fixture $fixture
    }
    catch { $setupRejected = $_.Exception.Message.Contains('does not exist') }
    Write-TestResult 'Runspace.RejectsMissingWorkingDirectory' (
        $setupRejected -and -not (Test-Path -LiteralPath $readyPath) -and (Get-Location).Path -eq $locationBefore
    )
}

try {
    Test-IsolatedFixtureHarness
    Test-OverlappingFixtures
} finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force
}
Write-Host "Passed: $script:TestsPassed; Failed: $script:TestsFailed"
if ($script:TestsFailed -ne 0) { exit 1 }
exit 0
