#!/usr/bin/env pwsh
[CmdletBinding()]
param([switch]$RequireWindowsRegistry)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repo 'scripts/unity/lib/native-global-flags.ps1')
. (Join-Path $repo 'scripts/unity/lib/native-loader-flag-lease.ps1')
if ($RequireWindowsRegistry -and -not $IsWindows) { throw 'Windows registry controls were required' }
$source = Join-Path $repo 'scripts/unity/capture-native-fault.ps1'
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
foreach ($name in @('Invoke-NativeCaptureProcess', 'Get-NativeCaptureLayout', 'Get-NativeCaptureExitEvent', 'Get-NativeCaptureXmlOutcome', 'Read-NativeCaptureTail', 'Test-NativeCaptureInputsUnchanged')) {
    $function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    if ($null -eq $function) { throw "Missing shipping function $name" }
    . ([scriptblock]::Create($function.Extent.Text))
}
$root = Join-Path ([IO.Path]::GetTempPath()) ('native-capture-controls-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $root)
$passed = 0
function Assert-Capture([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    ++$script:passed
}
try {
    $editorInput = Join-Path $root 'editor-input.txt'
    $lockInput = Join-Path $root 'package-lock-input.json'
    [IO.File]::WriteAllText($editorInput, 'editor')
    [IO.File]::WriteAllText($lockInput, '{}')
    $inputRecord = @{ EditorSha256 = (Get-FileHash $editorInput -Algorithm SHA256).Hash; PackagesLockSha256 = (Get-FileHash $lockInput -Algorithm SHA256).Hash }
    Assert-Capture (Test-NativeCaptureInputsUnchanged -EditorPath $editorInput -PackageLockPath $lockInput -Record $inputRecord) 'Comparison recognizes unchanged inputs'
    [IO.File]::WriteAllText($lockInput, '{"changed":true}')
    Assert-Capture (-not (Test-NativeCaptureInputsUnchanged -EditorPath $editorInput -PackageLockPath $lockInput -Record $inputRecord)) 'Comparison refuses package-lock drift'
    [IO.File]::WriteAllText($lockInput, '{}')
    [IO.File]::WriteAllText($editorInput, 'changed editor')
    Assert-Capture (-not (Test-NativeCaptureInputsUnchanged -EditorPath $editorInput -PackageLockPath $lockInput -Record $inputRecord)) 'Comparison refuses editor drift'
    Remove-Item -LiteralPath $lockInput
    Assert-Capture (-not (Test-NativeCaptureInputsUnchanged -EditorPath $editorInput -PackageLockPath $lockInput -Record $inputRecord)) 'Comparison refuses missing inputs'
    $flagCases = @(
        @{ Value = '0x00000002'; Kind = 'String'; DisabledHex = '00000000' }
        @{ Value = '2'; Kind = 'String'; DisabledHex = '00000000' }
        @{ Value = '00000012'; Kind = 'String'; DisabledHex = '00000010' }
        @{ Value = 'FFFFFFFF'; Kind = 'String'; DisabledHex = 'FFFFFFFD' }
        @{ Value = 2; Kind = 'DWord'; DisabledHex = '00000000' }
        @{ Value = 18; Kind = 'DWord'; DisabledHex = '00000010' }
        @{ Value = -1; Kind = 'DWord'; DisabledHex = 'FFFFFFFD' }
        @{ Value = ([int]::MinValue + 2); Kind = 'DWord'; DisabledHex = '80000000' }
    )
    foreach ($case in $flagCases) {
        $disabledValue = Get-NativeLoaderDisabledValue -Value $case.Value -Kind $case.Kind
        $disabled = ConvertTo-NativeGlobalFlagObservation -Value $disabledValue -Kind $case.Kind
        Assert-Capture ($disabled.State -eq 'observed' -and $disabled.Hex -ceq $case.DisabledHex) 'Flag computation matches independent exact-bit expectations'
    }
    if ($IsWindows) {
        $registryRoot = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry64)
        $registryPath = 'Software\UnityHelpersNativeFlagControls-' + [Guid]::NewGuid().ToString('N')
        $registryKey = $null
        try {
            $registryKey = $registryRoot.CreateSubKey($registryPath)
            foreach ($case in $flagCases) {
                $kind = [Microsoft.Win32.RegistryValueKind]::$($case.Kind)
                $registryKey.SetValue('GlobalFlag', $case.Value, $kind)
                $lease = New-NativeLoaderFlagLease -Key $registryKey
                Assert-Capture (Test-NativeLoaderFlagValue -Key $registryKey -Kind $kind -Value $case.Value) 'Preparation does not mutate a value'
                $leaseFile = Join-Path $root ([Guid]::NewGuid().ToString('N') + '.json')
                Disable-NativeLoaderFlag -Lease $lease -Key $registryKey -LeasePath $leaseFile
                $savedLease = Read-NativeLoaderFlagLease -Path $leaseFile
                $disabled = ConvertTo-NativeGlobalFlagObservation -Value $registryKey.GetValue('GlobalFlag') -Kind $case.Kind
                $original = ConvertTo-NativeGlobalFlagObservation -Value $case.Value -Kind $case.Kind
                Assert-Capture ($disabled.State -eq 'observed' -and -not $disabled.LoaderSnapsEnabled -and [Convert]::ToUInt32($disabled.Hex, 16) -eq ([Convert]::ToUInt32($original.Hex, 16) -band 4294967293u)) 'Disabling changes only the loader-snaps bit, including signed DWORD values'
                Restore-NativeLoaderFlag -Lease $savedLease -Key $registryKey
                Restore-NativeLoaderFlag -Lease $savedLease -Key $registryKey
                Assert-Capture ($savedLease.State -eq 'restored' -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind $kind -Value $case.Value)) 'Restoration preserves exact string spelling, numeric bits and value kind; repeated restoration is safe'
                $registryKey.SetValue('GlobalFlag', (Get-NativeLoaderDisabledValue -Value $case.Value -Kind $case.Kind), $kind)
                $refused = $false
                try { Complete-NativeLoaderFlagLease -Lease $savedLease -Key $registryKey } catch { $refused = $true }
                Assert-Capture ($refused -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind $kind -Value (Get-NativeLoaderDisabledValue -Value $case.Value -Kind $case.Kind))) 'Automatic cleanup preserves an external edit after restoration finished'
                $registryKey.SetValue('GlobalFlag', $case.Value, $kind)
                $beforeWrite = [IO.File]::ReadAllText($leaseFile)
                $refused = $false
                try { Save-NativeLoaderFlagLease -Lease $savedLease -Path $leaseFile } catch { $refused = $true }
                Assert-Capture ($refused -and [IO.File]::ReadAllText($leaseFile) -ceq $beforeWrite) 'Original lease receipt cannot be overwritten'
                $refused = $false
                try { Disable-NativeLoaderFlag -Lease $savedLease -Key $registryKey -LeasePath $leaseFile } catch { $refused = $true }
                Assert-Capture ($refused -and $savedLease.State -eq 'prepared' -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind $kind -Value $case.Value)) 'A failed durable receipt write prevents the registry change'
            }
            $registryKey.SetValue('GlobalFlag', 2, [Microsoft.Win32.RegistryValueKind]::DWord)
            $lease = New-NativeLoaderFlagLease -Key $registryKey
            $registryKey.SetValue('GlobalFlag', 0, [Microsoft.Win32.RegistryValueKind]::DWord)
            $refused = $false
            try { Complete-NativeLoaderFlagLease -Lease $lease -Key $registryKey } catch { $refused = $true }
            Assert-Capture ($refused -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 0)) 'Automatic cleanup must preserve an external edit made before this lease writes anything'
            $registryKey.SetValue('GlobalFlag', 2, [Microsoft.Win32.RegistryValueKind]::DWord)
            $lease = New-NativeLoaderFlagLease -Key $registryKey
            foreach ($change in @(
                @{ Name = 'Machine'; Value = 'DifferentMachine' }
                @{ Name = 'RegistryKey'; Value = 'DifferentKey' }
                @{ Name = 'RegistryView'; Value = 'Registry32' }
                @{ Name = 'SchemaVersion'; Value = 2 }
                @{ Name = 'SchemaVersion'; Value = '1' }
                @{ Name = 'OriginalKind'; Value = 'QWord' }
                @{ Name = 'OriginalValue'; Value = 'invalid' }
                @{ Name = 'DisabledValue'; Value = 8 }
            )) {
                $damaged = $lease | ConvertTo-Json | ConvertFrom-Json
                $damaged.($change.Name) = $change.Value
                foreach ($operation in @('Disable-NativeLoaderFlag', 'Restore-NativeLoaderFlag')) {
                    $operationArguments = @{ Lease = $damaged; Key = $registryKey }
                    if ($operation -eq 'Disable-NativeLoaderFlag') { $operationArguments.LeasePath = Join-Path $root ([Guid]::NewGuid().ToString('N') + '.json') }
                    $refused = $false
                    try { & $operation @operationArguments } catch { $refused = $true }
                    Assert-Capture ($refused -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 2)) 'Invalid receipts cannot change registry values'
                }
            }
            $registryKey.SetValue('GlobalFlag', 6, [Microsoft.Win32.RegistryValueKind]::DWord)
            $refused = $false
            try { Disable-NativeLoaderFlag -Lease $lease -Key $registryKey -LeasePath (Join-Path $root 'changed-baseline.json') } catch { $refused = $true }
            Assert-Capture ($refused -and $registryKey.GetValue('GlobalFlag') -eq 6) 'A changed baseline is not overwritten'
            $registryKey.SetValue('GlobalFlag', 2, [Microsoft.Win32.RegistryValueKind]::DWord)
            Disable-NativeLoaderFlag -Lease $lease -Key $registryKey -LeasePath (Join-Path $root 'external-edit.json')
            $registryKey.SetValue('GlobalFlag', 8, [Microsoft.Win32.RegistryValueKind]::DWord)
            $refused = $false
            try { Restore-NativeLoaderFlag -Lease $lease -Key $registryKey } catch { $refused = $true }
            Assert-Capture ($refused -and $lease.State -eq 'conflict' -and $registryKey.GetValue('GlobalFlag') -eq 8) 'Concurrent edits are reported and retained'
            $registryKey.SetValue('GlobalFlag', '0x00000000', [Microsoft.Win32.RegistryValueKind]::String)
            $refused = $false
            try { Restore-NativeLoaderFlag -Lease $lease -Key $registryKey } catch { $refused = $true }
            Assert-Capture ($refused -and $registryKey.GetValueKind('GlobalFlag') -eq [Microsoft.Win32.RegistryValueKind]::String) 'A concurrent value-kind change is not overwritten'
            $registryKey.SetValue('GlobalFlag', 0, [Microsoft.Win32.RegistryValueKind]::DWord)
            Restore-NativeLoaderFlag -Lease $lease -Key $registryKey
            $readOnlyKey = $registryRoot.OpenSubKey($registryPath, $false)
            try {
                $refused = $false
                try { Disable-NativeLoaderFlag -Lease $lease -Key $readOnlyKey -LeasePath (Join-Path $root 'permission.json') } catch { $refused = $true }
                Assert-Capture ($refused -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 2)) 'Permission failure leaves the original value intact'
            } finally { $readOnlyKey.Dispose() }
            $permissionLease = Join-Path $root 'denied-open.json'
            $readOnlyKey = $registryRoot.OpenSubKey($registryPath, $false)
            try {
                $prepared = New-NativeLoaderFlagLease -Key $readOnlyKey
                Save-NativeLoaderFlagLease -Lease $prepared -Path ($permissionLease + '.prepared')
            } finally { $readOnlyKey.Dispose() }
            $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
            $restrictedSecurity = [Microsoft.Win32.RegistryAclExtensions]::GetAccessControl($registryKey)
            $denyWrite = [Security.AccessControl.RegistryAccessRule]::new($identity.User, [Security.AccessControl.RegistryRights]::SetValue, [Security.AccessControl.AccessControlType]::Deny)
            $restrictedSecurity.AddAccessRule($denyWrite)
            try {
                [Microsoft.Win32.RegistryAclExtensions]::SetAccessControl($registryKey, $restrictedSecurity)
                $deniedKey = $null
                $refused = $false
                try { $deniedKey = $registryRoot.OpenSubKey($registryPath, $true) } catch { $refused = $true }
                finally { if ($null -ne $deniedKey) { $deniedKey.Dispose() } }
                Assert-Capture ($refused) 'A real registry ACL denies opening a writable handle'
                foreach ($state in @('prepared', 'clear-requested', 'restored')) {
                    $recovered = Read-NativeLoaderFlagRecoveryLease -Path $permissionLease
                    $recovered.State = $state
                    Invoke-NativeLoaderFlagRecovery -BaseKey $registryRoot -KeyPath $registryPath -Lease $recovered -Apply
                    Assert-Capture (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 2) 'Exact unchanged recovery succeeds without registry write permission in every valid lease phase'
                }
                $registryKey.SetValue('GlobalFlag', 8, [Microsoft.Win32.RegistryValueKind]::DWord)
                $recovered = Read-NativeLoaderFlagRecoveryLease -Path $permissionLease
                $refused = $false
                try { Invoke-NativeLoaderFlagRecovery -BaseKey $registryRoot -KeyPath $registryPath -Lease $recovered -Apply } catch { $refused = $true }
                Assert-Capture ($refused -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 8)) 'Read-only recovery refuses a conflicting value without requesting a write'
                $registryKey.SetValue('GlobalFlag', 0, [Microsoft.Win32.RegistryValueKind]::DWord)
                $recovered.State = 'clear-requested'
                $refused = $false
                try { Invoke-NativeLoaderFlagRecovery -BaseKey $registryRoot -KeyPath $registryPath -Lease $recovered -Apply } catch { $refused = $true }
                Assert-Capture ($refused -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 0)) 'A real restoration still requires write permission and cannot claim success when denied'
            } finally {
                $restrictedSecurity.RemoveAccessRuleSpecific($denyWrite)
                [Microsoft.Win32.RegistryAclExtensions]::SetAccessControl($registryKey, $restrictedSecurity)
                $identity.Dispose()
            }
            Invoke-NativeLoaderFlagRecovery -BaseKey $registryRoot -KeyPath $registryPath -Lease $recovered -Apply
            Assert-Capture (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 2) 'Active recovery restores the exact original value once write permission is available'
            $interrupted = Join-Path $root 'interrupted.ps1'
            $interruptedLease = Join-Path $root 'interrupted-lease.json'
            [IO.File]::WriteAllText($interrupted, @'
param($LibraryRoot, $KeyPath, $LeasePath)
$ErrorActionPreference = 'Stop'
. (Join-Path $LibraryRoot 'native-global-flags.ps1')
. (Join-Path $LibraryRoot 'native-loader-flag-lease.ps1')
$base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry64)
$key = $base.OpenSubKey($KeyPath, $true)
$lease = New-NativeLoaderFlagLease -Key $key
Save-NativeLoaderFlagLease -Lease $lease -Path ($LeasePath + '.prepared')
Disable-NativeLoaderFlag -Lease $lease -Key $key -LeasePath $LeasePath
$key.Dispose()
$base.Dispose()
exit 7
'@)
            $interruptedProcess = @{}
            Invoke-NativeCaptureProcess -FilePath (Get-Process -Id $PID).Path -Arguments @('-NoProfile', '-File', $interrupted, (Join-Path $repo 'scripts/unity/lib'), $registryPath, $interruptedLease) -Directory $root -Seconds 20 -Observation $interruptedProcess | Out-Null
            Assert-Capture ($interruptedProcess.Reaped -and $interruptedProcess.RawExitCode -eq 7 -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 0)) 'An exited process leaves a real disabled value and a durable original lease'
            $activeReceipt = [IO.File]::ReadAllBytes($interruptedLease)
            [IO.File]::WriteAllText($interruptedLease, '{}')
            $refused = $false
            try { Read-NativeLoaderFlagRecoveryLease -Path $interruptedLease | Out-Null } catch { $refused = $true }
            Assert-Capture ($refused -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 0)) 'Damaged post-write evidence fails without a fallback or registry change'
            Remove-Item -LiteralPath $interruptedLease
            $unprovenLease = Read-NativeLoaderFlagRecoveryLease -Path $interruptedLease
            $refused = $false
            try { Complete-NativeLoaderFlagLease -Lease $unprovenLease -Key $registryKey } catch { $refused = $true }
            Assert-Capture ($refused -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 0)) 'Missing post-write evidence cannot use a prepared receipt to restore a changed value'
            [IO.File]::WriteAllBytes($interruptedLease, $activeReceipt)
            $recoveredLease = Read-NativeLoaderFlagRecoveryLease -Path $interruptedLease
            Complete-NativeLoaderFlagLease -Lease $recoveredLease -Key $registryKey
            Assert-Capture (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 2) 'A later process can restore the exact original value from the durable lease'
            $beforeWriteScript = Join-Path $root 'before-write.ps1'
            [IO.File]::WriteAllText($beforeWriteScript, @'
param($LibraryRoot, $KeyPath, $LeasePath, $StopMode)
$ErrorActionPreference = 'Stop'
. (Join-Path $LibraryRoot 'native-global-flags.ps1')
. (Join-Path $LibraryRoot 'native-loader-flag-lease.ps1')
$base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry64)
$key = $base.OpenSubKey($KeyPath, $true)
$lease = New-NativeLoaderFlagLease -Key $key
Save-NativeLoaderFlagLease -Lease $lease -Path ($LeasePath + '.prepared')
$key.Dispose()
$base.Dispose()
if ($StopMode -eq 'failure') { throw 'Deliberate failure before a flag write' }
Start-Sleep -Seconds 300
'@)
            foreach ($stopMode in @('failure', 'cancellation')) {
                $beforeWriteLease = Join-Path $root ($stopMode + '-lease.json')
                $beforeWriteProcess = @{}
                Invoke-NativeCaptureProcess -FilePath (Get-Process -Id $PID).Path -Arguments @('-NoProfile', '-File', $beforeWriteScript, (Join-Path $repo 'scripts/unity/lib'), $registryPath, $beforeWriteLease, $stopMode) -Directory $root -Seconds 5 -Observation $beforeWriteProcess | Out-Null
                Assert-Capture ($beforeWriteProcess.Reaped -and -not (Test-Path -LiteralPath $beforeWriteLease) -and (Test-Path -LiteralPath ($beforeWriteLease + '.prepared'))) 'A stopped pre-write process retains original evidence without an active write lease'
                if ($stopMode -eq 'failure') {
                    Assert-Capture ($beforeWriteProcess.RawExitCode -ne 0 -and $null -eq $beforeWriteProcess.KillReason) 'A deliberate pre-write failure exits naturally'
                } else {
                    Assert-Capture ($beforeWriteProcess.KillReason -ceq 'wall-clock-timeout') 'Cancellation kills and reaps the waiting pre-write process'
                }
                $preparedLease = Read-NativeLoaderFlagRecoveryLease -Path $beforeWriteLease
                Complete-NativeLoaderFlagLease -Lease $preparedLease -Key $registryKey
                Assert-Capture (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value 2) 'Recovery after a pre-write stop verifies the unchanged original value'
                foreach ($conflict in @(0, 8)) {
                    $registryKey.SetValue('GlobalFlag', $conflict, [Microsoft.Win32.RegistryValueKind]::DWord)
                    $preparedLease = Read-NativeLoaderFlagRecoveryLease -Path $beforeWriteLease
                    $refused = $false
                    try { Complete-NativeLoaderFlagLease -Lease $preparedLease -Key $registryKey } catch { $refused = $true }
                    Assert-Capture ($refused -and (Test-NativeLoaderFlagValue -Key $registryKey -Kind DWord -Value $conflict)) 'A prepared receipt cannot restore an externally changed or unproven disabled value'
                }
                $registryKey.SetValue('GlobalFlag', 2, [Microsoft.Win32.RegistryValueKind]::DWord)
            }
            $registryKey.SetValue('GlobalFlag', 0, [Microsoft.Win32.RegistryValueKind]::DWord)
            $refused = $false
            try { New-NativeLoaderFlagLease -Key $registryKey | Out-Null } catch { $refused = $true }
            Assert-Capture ($refused) 'An already-disabled flag is refused before comparison'
            $registryKey.SetValue('GlobalFlag', 2L, [Microsoft.Win32.RegistryValueKind]::QWord)
            $refused = $false
            try { New-NativeLoaderFlagLease -Key $registryKey | Out-Null } catch { $refused = $true }
            Assert-Capture ($refused) 'Unsupported registry kinds cannot prepare a comparison'
            $registryKey.DeleteValue('GlobalFlag')
            $refused = $false
            try { New-NativeLoaderFlagLease -Key $registryKey | Out-Null } catch { $refused = $true }
            Assert-Capture ($refused) 'Missing registry values cannot prepare a comparison'
        } finally {
            if ($null -ne $registryKey) { $registryKey.Dispose() }
            try { $registryRoot.DeleteSubKeyTree($registryPath, $false) } finally { $registryRoot.Dispose() }
        }
    } else { Write-Host 'Windows registry transaction controls are not run on this platform; require them separately on Windows.' }
    $envelopeFile = Join-Path $root 'envelope.json'
    $sampleLease = [ordered]@{ SchemaVersion = 1; Machine = 'Sample'; RegistryKey = 'Sample'; OriginalKind = 'String'; OriginalValue = '0x00000002'; DisabledValue = '0x00000000'; State = 'prepared' }
    Save-NativeLoaderFlagLease -Lease $sampleLease -Path $envelopeFile
    $recoveryPath = Join-Path $root 'recovery.json'
    Save-NativeLoaderFlagLease -Lease $sampleLease -Path ($recoveryPath + '.prepared')
    Assert-Capture ((Read-NativeLoaderFlagRecoveryLease -Path $recoveryPath).State -ceq 'prepared') 'Recovery reads the prepared receipt when no write lease exists'
    foreach ($badPath in @($recoveryPath, ($recoveryPath + '.prepared'))) {
        [IO.File]::WriteAllText($badPath, '{}')
        $refused = $false
        try { Read-NativeLoaderFlagRecoveryLease -Path $recoveryPath | Out-Null } catch { $refused = $true }
        Assert-Capture ($refused) 'A damaged active or prepared receipt cannot fall back to a successful recovery'
        Remove-Item -LiteralPath $badPath
    }
    $refused = $false
    try { Read-NativeLoaderFlagRecoveryLease -Path $recoveryPath | Out-Null } catch { $refused = $true }
    Assert-Capture ($refused) 'Absent recovery evidence cannot prove unchanged state'
    $sampleLease.State = 'clear-requested'
    Save-NativeLoaderFlagLease -Lease $sampleLease -Path ($recoveryPath + '.prepared')
    $refused = $false
    try { Read-NativeLoaderFlagRecoveryLease -Path $recoveryPath | Out-Null } catch { $refused = $true }
    Assert-Capture ($refused) 'The fallback receipt must prove the pre-write phase'
    Save-NativeLoaderFlagLease -Lease $sampleLease -Path $recoveryPath
    Assert-Capture ((Read-NativeLoaderFlagRecoveryLease -Path $recoveryPath).State -ceq 'clear-requested') 'An active write lease takes precedence over the prepared receipt'
    $envelope = Get-Content -LiteralPath $envelopeFile -Raw | ConvertFrom-Json
    Assert-Capture ((Read-NativeLoaderFlagLease -Path $envelopeFile).OriginalValue -ceq '0x00000002') 'Checksummed receipt round-trips exact original data'
    foreach ($bad in @(
        ($envelope | ConvertTo-Json).Replace($envelope.Sha256, ('0' * 64))
        ($envelope | ConvertTo-Json).Replace($envelope.Data, '%%%')
        ($envelope | ConvertTo-Json).Replace('"Version": 1', '"Version": 2')
        ('x' * 65537)
        '{}'
    )) {
        [IO.File]::WriteAllText($envelopeFile, $bad)
        $refused = $false
        try { Read-NativeLoaderFlagLease -Path $envelopeFile | Out-Null } catch { $refused = $true }
        Assert-Capture ($refused) 'Damaged, unsupported and excessive receipts are rejected before restoration'
    }
    foreach ($case in @(
        @{ Value = 0; Kind = 'DWord'; Hex = '00000000'; Enabled = $false }
        @{ Value = 2; Kind = 'DWord'; Hex = '00000002'; Enabled = $true }
        @{ Value = 16; Kind = 'DWord'; Hex = '00000010'; Enabled = $false }
        @{ Value = -1; Kind = 'DWord'; Hex = 'FFFFFFFF'; Enabled = $true }
        @{ Value = [int]::MinValue; Kind = 'DWord'; Hex = '80000000'; Enabled = $false }
        @{ Value = '0x00000002'; Kind = 'String'; Hex = '00000002'; Enabled = $true }
        @{ Value = 'aBcDeF00'; Kind = 'String'; Hex = 'ABCDEF00'; Enabled = $false }
        @{ Value = '00000002'; Kind = 'String'; Hex = '00000002'; Enabled = $true }
    )) {
        $flags = ConvertTo-NativeGlobalFlagObservation -Value $case.Value -Kind $case.Kind
        Assert-Capture ($flags.State -eq 'observed' -and $flags.Hex -eq $case.Hex -and $flags.LoaderSnapsEnabled -eq $case.Enabled) 'Registry flag types preserve all bits and distinguish loader snaps'
    }
    foreach ($case in @(
        @{ Value = '100000000'; Kind = 'String' }
        @{ Value = '0x'; Kind = 'String' }
        @{ Value = 'garbage'; Kind = 'String' }
        @{ Value = ' 2'; Kind = 'String' }
        @{ Value = ''; Kind = 'String' }
        @{ Value = $null; Kind = 'DWord' }
        @{ Value = 2L; Kind = 'QWord' }
        @{ Value = @('2'); Kind = 'MultiString' }
        @{ Value = '%TEMP%'; Kind = 'ExpandString' }
    )) {
        $flags = ConvertTo-NativeGlobalFlagObservation -Value $case.Value -Kind $case.Kind
        Assert-Capture ($flags.State -eq 'invalid' -and $null -eq $flags.Hex -and $null -eq $flags.LoaderSnapsEnabled) 'Invalid and unsupported registry values cannot claim disabled tracing'
    }
    $flags = Get-NativeGlobalFlagSnapshot
    Assert-Capture ($flags.Settings.Count -eq 2 -and $flags.Scope -eq 'registry-settings-only') 'Registry observations cover system and Unity settings without claiming process flags'
    foreach ($setting in $flags.Settings) {
        Assert-Capture ($setting.State -in @('observed', 'absent', 'unavailable', 'invalid')) 'Every registry read has an explicit state'
        if ($setting.State -ne 'observed') {
            Assert-Capture ($null -eq $setting.LoaderSnapsEnabled) 'Missing or inaccessible settings cannot claim disabled tracing'
        }
    }
    $missing = Get-NativeGlobalFlagRegistryObservation -Subject 'MissingControl' -KeyPath ('SOFTWARE\unity-helpers-missing-' + [Guid]::NewGuid().ToString('N'))
    Assert-Capture ($missing.State -eq $(if ($IsWindows) { 'absent' } else { 'unavailable' }) -and $null -eq $missing.LoaderSnapsEnabled) 'Real missing key and unsupported platform remain distinct'
    $captureSource = [IO.File]::ReadAllText($source)
    Assert-Capture ($captureSource.Contains('GlobalFlagRegistry = Get-NativeGlobalFlagSnapshot') -and $captureSource.Contains('WUH_EXIT_REQUEST;!gflag;r rcx')) 'Receipt includes registry settings and exit breakpoint observes effective target flags'
    $runnerAction = [IO.File]::ReadAllText((Join-Path $repo '.github/actions/print-self-hosted-runner-diagnostics/action.yml'))
    Assert-Capture ($runnerAction.Contains('. ./scripts/unity/lib/native-global-flags.ps1') -and $runnerAction.Contains('Get-NativeGlobalFlagSnapshot | ConvertTo-Json')) 'Regular runner diagnostics use the same read-only snapshot'
    $layout = Get-NativeCaptureLayout -Root (Join-Path $root 'layout')
    foreach ($directory in $layout.Values) { [void](New-Item -ItemType Directory -Path $directory) }
    Assert-Capture (@(Get-ChildItem -LiteralPath (Join-Path $root 'layout') -Directory).Count -eq 3) 'Default tool and capture directories coexist without collisions'
    $normal = Get-NativeCaptureExitEvent 'Last event: abc.123: Exit process 0:abc, code 0'
    Assert-Capture ($normal.Pid -eq 2748 -and $normal.ExitCodeHex -eq '00000000') 'Normal native exit provenance'
    $crash = Get-NativeCaptureExitEvent 'Last event: 6dc84.63204: Exit process 0:6dc84, code c0000005'
    Assert-Capture ($crash.ExitCodeHex -eq 'C0000005') 'Access violation exit provenance'
    Assert-Capture ($null -eq (Get-NativeCaptureExitEvent 'debugger returned zero')) 'Debugger exit cannot stand in for target exit'
    Assert-Capture ($null -eq (Get-NativeCaptureExitEvent "Last event: abc.123: Exit process 0:abc, code 0`nLast event: abd.123: Exit process 0:abd, code 0")) 'Ambiguous target exit rejected'

    $xml = Join-Path $root 'results.xml'
    Assert-Capture ((Get-NativeCaptureXmlOutcome $xml 'Fixture.Case').State -eq 'missing') 'Missing XML explicit'
    [IO.File]::WriteAllText($xml, '<test-run><test-case fullname="Fixture.Case" result="Passed" /></test-run>')
    Assert-Capture ((Get-NativeCaptureXmlOutcome $xml 'Fixture.Case').Result -eq 'Passed') 'Real single case observed'
    foreach ($bad in @('<test-run />', '<test-run><test-case', '<test-run><test-case fullname="Other.Case" result="Passed" /></test-run>', '<!DOCTYPE x [<!ENTITY secret SYSTEM "file:///unavailable">]><test-run>&secret;</test-run>')) {
        [IO.File]::WriteAllText($xml, $bad)
        $outcome = Get-NativeCaptureXmlOutcome $xml 'Fixture.Case'
        Assert-Capture ($outcome.State -eq 'invalid') 'Invalid or unrelated XML is recorded without interrupting evidence capture'
    }

    $snapshot = Join-Path $root 'writer.log'
    [IO.File]::WriteAllText($snapshot, (('x' * 800000) + 'END'))
    $writer = [IO.File]::Open($snapshot, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
    try { $tail = Read-NativeCaptureTail $snapshot } finally { $writer.Dispose() }
    Assert-Capture ($tail.Length -eq 524288 -and $tail.EndsWith('END')) 'Bounded snapshot permits active writer'

    $child = Join-Path $root 'child.ps1'
    [IO.File]::WriteAllText($child, '[Console]::Out.Write((''x'' * 2000000) + ''END''); [Console]::Error.Write((''y'' * 2000000) + ''ERROR_END''); exit 7')
    $observation = @{}
    $receipt = Join-Path $root 'process.json'
    $output = Invoke-NativeCaptureProcess -FilePath (Get-Process -Id $PID).Path -Arguments @('-NoProfile', '-File', $child) -Directory $root -Seconds 20 -Observation $observation -ReceiptPath $receipt
    Assert-Capture ($observation.Reaped -and $observation.RawExitCode -eq 7 -and $null -eq $observation.KillReason) "Actual nonzero process exit retained: $($observation | ConvertTo-Json -Compress); retained characters: $($output.Length)"
    Assert-Capture ($output.Length -le 524288 -and $output.Contains('END')) 'Both streams drain without unbounded retention'
    $saved = Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
    Assert-Capture ($saved.Pid -eq $observation.Pid -and $saved.RawExitCode -eq 7) 'Durable PID and terminal receipt agree'
    [IO.File]::WriteAllText($child, 'Start-Sleep -Seconds 30')
    $timeout = @{}
    Invoke-NativeCaptureProcess -FilePath (Get-Process -Id $PID).Path -Arguments @('-NoProfile', '-File', $child) -Directory $root -Seconds 1 -Observation $timeout | Out-Null
    Assert-Capture ($timeout.Reaped -and $timeout.KillReason -eq 'wall-clock-timeout' -and $null -ne $timeout.RawExitCode) 'Watchdog kill and actual OS exit remain distinct'
    $failed = @{}
    $rejected = $false
    try { Invoke-NativeCaptureProcess -FilePath (Join-Path $root 'absent.exe') -Arguments @() -Directory $root -Seconds 1 -Observation $failed | Out-Null } catch { $rejected = $true }
    Assert-Capture ($rejected -and -not $failed.Reaped -and $null -eq $failed.RawExitCode) 'Failed launch cannot fabricate an exit'

    $workflow = [IO.File]::ReadAllText((Join-Path $repo '.github/workflows/unity-tests.yml'))
    Assert-Capture ($workflow -match '(?s)native-diagnostics:.*?default: false.*?type: boolean') 'Diagnostic input defaults off'
    $captureStep = [regex]::Match($workflow, '(?s)- name: Capture native exit diagnostics.*?(?=\n      - name:)').Value
    Assert-Capture ($captureStep -match "inputs.native-diagnostics" -and $captureStep -match "steps.unity_lock.outputs.acquired == 'true'" -and $captureStep -match "steps.run_editmode.outcome == 'success'" -and $captureStep -match '!cancelled\(\)') 'Capture requires opt-in, live lock, and prepared project'
    Assert-Capture ($captureStep -match 'capture-native-fault.ps1' -and $captureStep -match 'native-diagnostics') 'Standalone driver is wired to uploaded EditMode artifacts'
    Assert-Capture ($captureStep -match 'UH_STREAM_TEST_RESULTS:\s*"1"') 'Capture retains the normal CI streaming callback setting'
    Assert-Capture ($workflow -match '(?s)native-loader-snaps-comparison:.*?default: false.*?type: boolean') 'Registry comparison is a separate explicit opt-in'
    $restoreStep = [regex]::Match($workflow, '(?s)- name: Restore native loader-snaps comparison.*?(?=\n      - name:)').Value
    Assert-Capture ($restoreStep.Contains('always()') -and $restoreStep.Contains("steps.unity_lock.outputs.acquired == 'true'") -and $restoreStep.Contains('restore-native-loader-snaps.ps1 -Apply')) 'Independent restoration runs under the existing lock, including failures and cancellation'
    Assert-Capture ($captureSource.Contains("@('direct-before', 'direct-disabled', 'direct-restored')") -and $captureSource.Contains('finally') -and $captureSource.Contains('-LeasePath') -and $captureSource.Contains('Complete-NativeLoaderFlagLease')) 'Comparison records three direct phases and retains durable and finally recovery paths'
    $prepareOffset = $captureSource.IndexOf("Save-NativeLoaderFlagLease -Lease `$record.LoaderFlagLease")
    Assert-Capture ($prepareOffset -gt 0 -and $prepareOffset -lt $captureSource.IndexOf('if ([string]::IsNullOrWhiteSpace($DebuggerDirectory))')) 'The original receipt is durable before debugger setup and direct-before execution'
    Assert-Capture ($captureSource.IndexOf('$readOnlyFlagKey = $flagBase.OpenSubKey($flagPath, $false)') -lt $prepareOffset -and $prepareOffset -lt $captureSource.IndexOf('$flagKey = $flagBase.OpenSubKey($flagPath, $true)')) 'The prepared receipt precedes the first request for registry write access'
    $restoreSource = [IO.File]::ReadAllText((Join-Path $repo 'scripts/unity/restore-native-loader-snaps.ps1'))
    Assert-Capture ($restoreSource.Contains('Read-NativeLoaderFlagRecoveryLease') -and $restoreSource.Contains('Invoke-NativeLoaderFlagRecovery')) 'Independent recovery uses the phase-aware receipt and cleanup policy'
    Write-Host "$passed native capture controls passed"
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force
}
