"""Exercise actual host maintenance functions with isolated CLI outcomes."""

from __future__ import annotations

import json
import shutil
import subprocess
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "scripts/unity/ensure-editor.ps1"
IMPORT = r"""
param([string]$Source, [string]$Initial = '', [string]$Updated = '', [string]$Hold = 'false',
      [string]$Verb = 'install', [string]$Modules = '[]', [string]$Stall = 'false',
      [string]$Wall = 'false', [int]$ExitCode = 6)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($Source, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Invalid maintenance source' }
function Import-Definition([string]$Name) {
    $node = $ast.Find({ param($candidate)
        $candidate -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $candidate.Name -eq $Name
    }, $true)
    if ($null -eq $node) { throw "Missing $Name" }
    Invoke-Expression "function script:$Name $($node.Body.Extent.Text)"
}
"""


def probe(tmp_path: Path, body: str, arguments: list[str]) -> subprocess.CompletedProcess[str]:
    shell = shutil.which("pwsh")
    if shell is None:
        pytest.skip("PowerShell is required for host maintenance controls")
    script = tmp_path / "probe.ps1"
    script.write_text(IMPORT + body, encoding="utf-8")
    return subprocess.run(
        [
            shell,
            "-NoProfile",
            "-NonInteractive",
            "-File",
            str(script),
            "-Source",
            str(SOURCE),
            *arguments,
        ],
        capture_output=True,
        encoding="utf-8",
        check=False,
        timeout=30,
    )


@pytest.mark.parametrize(
    ("verb", "modules", "expected"),
    [
        ("install", [], ["install", "6000.6.7f1", "--yes"]),
        (
            "install",
            ["windows-il2cpp"],
            ["install", "6000.6.7f1", "--yes", "--accept-eula", "-m", "windows-il2cpp"],
        ),
        (
            "install",
            ["android"],
            ["install", "6000.6.7f1", "--yes", "--accept-eula", "--childModules", "-m", "android"],
        ),
        (
            "install-modules",
            ["windows-il2cpp"],
            [
                "install-modules",
                "-e",
                "6000.6.7f1",
                "--yes",
                "--accept-eula",
                "-m",
                "windows-il2cpp",
            ],
        ),
        (
            "install-modules",
            ["android"],
            [
                "install-modules",
                "-e",
                "6000.6.7f1",
                "--yes",
                "--accept-eula",
                "--childModules",
                "-m",
                "android",
            ],
        ),
    ],
)
def test_install_vectors_explicitly_confirm_mutation(
    verb: str, modules: list[str], expected: list[str], tmp_path: Path
) -> None:
    result = probe(
        tmp_path,
        r"""
Import-Definition 'Get-UnityCliModuleInstallArguments'
$ids = @(ConvertFrom-Json $Modules)
$arguments = @(Get-UnityCliModuleInstallArguments -Verb $Verb -Version '6000.6.7f1' -ModuleIds $ids)
ConvertTo-Json -InputObject $arguments -Compress
""",
        ["-Verb", verb, "-Modules", json.dumps(modules)],
    )
    assert result.returncode == 0, result.stderr
    assert json.loads(result.stdout) == expected


@pytest.mark.parametrize(
    ("initial", "updated", "hold", "installs", "updates", "success"),
    [
        ("0.1.0-beta.5", "1.0.0-beta.15", False, 1, 1, True),
        ("Unity CLI 0.1.0-beta.8", "1.0.0-beta.15", False, 1, 1, True),
        ("1.0.0-beta.15", "1.0.0-beta.15", False, 0, 1, True),
        ("1.0.0-beta.8", "1.0.0-beta.15", True, 0, 0, True),
        ("0.1.0-beta.5", "0.1.0-beta.5", False, 1, 0, False),
    ],
)
def test_legacy_cli_is_replaced_before_claiming_it_is_current(
    initial: str,
    updated: str,
    hold: bool,
    installs: int,
    updates: int,
    success: bool,
    tmp_path: Path,
) -> None:
    result = probe(
        tmp_path,
        r"""
Import-Definition 'Update-UnityCliStandalone'
$script:UnityCliUpdateCompleted = $false
$script:UnityCliVersionText = ''
$script:version = $Initial
$script:installs = 0
$script:updates = 0
function Get-UnityCliVersionText { return $script:version }
function Test-UnityCliCompatibilityHoldActive { return $Hold -eq 'true' }
function Install-UnityCliStandalone { $script:installs++; $script:version = $Updated }
function Update-SessionPathFromRegistry { }
function Get-Command { param($Name, $ErrorAction) return @{Source = 'isolated-unity.exe'} }
function Write-CiNotice { param([string]$Message) }
function Get-UnityCliUpdateTimeoutSeconds { return 30 }
function Get-UnityCliUpdateRetryAttempts { return 1 }
function Get-UnityCliUpdateStallSeconds { return 30 }
function Get-EnsureEditorRetryDelaySeconds { return 0 }
function Invoke-WithRetry { param($MaxAttempts, $DelaySeconds, [scriptblock]$Action) & $Action }
function Invoke-UnityCliCaptureWithTimeout {
    param($Arguments, $TimeoutSeconds, $TimeoutKnob, $StallSeconds, $StallKnob)
    if ($Arguments[0] -ne 'self-update') { throw 'Unexpected legacy update alias' }
    $script:updates++
    return @{ExitCode = 0; Output = @()}
}
$success = $true
try { Update-UnityCliStandalone; Update-UnityCliStandalone }
catch { $success = $false }
ConvertTo-Json -InputObject @{Success = $success; Installs = $script:installs; Updates = $script:updates} -Compress
""",
        ["-Initial", initial, "-Updated", updated, "-Hold", str(hold).lower()],
    )
    assert result.returncode == 0, result.stderr
    assert json.loads(result.stdout) == {
        "Success": success,
        "Installs": installs,
        "Updates": updates,
    }


@pytest.mark.parametrize(
    ("stall", "wall", "code", "recovered"),
    [
        (False, False, 6, True),
        (False, False, 124, True),
        (False, False, 125, True),
        (True, False, 125, False),
        (False, True, 124, False),
        (True, False, 6, False),
    ],
)
def test_interrupted_installer_cannot_recover_from_visible_executable(
    stall: bool, wall: bool, code: int, recovered: bool, tmp_path: Path
) -> None:
    result = probe(
        tmp_path,
        r"""
Import-Definition 'Install-UnityEditorWithCiModules'
function Assert-UnityProvisioningBudgetCanFit { param($Operation, $MinimumSeconds) }
function Get-UnityCiModuleIds { param($Profile) return @() }
function Write-CiNotice { param($Message) }
function Get-UnityCliModuleInstallArguments { param($Verb, $Version, $ModuleIds) return @('install', $Version, '--yes') }
function Invoke-UnityCliCapture {
    param($Arguments)
    return @{Success = $false; ExitCode = $ExitCode; Output = @('Partial install');
             StallKilled = ($Stall -eq 'true'); TimedOutWallClock = ($Wall -eq 'true')}
}
function Test-UnityCliWriterKindSchemaFailure { param($Output) return $false }
function Resolve-InstalledEditor { param($Version, $Root, [switch]$ManagedOnly) return '/owned/Editor/Unity.exe' }
function Get-MissingUnityCiModuleGroups { param($EditorPath, $Profile) return @() }
function Get-CollapsedCliOutputTail { param($Output, $MaxLines) return 'Partial install' }
function Write-UnityCliInstallFailureAnnotation { param($Version, $Output, $ExitCode, $Arguments) }
function Write-ModuleInstallFailureDiagnostics {
    param($Version, $Output, $ExitCode, $Arguments, $Root, $TimedOut, $StallKilled, $TimedOutWallClock, $TimeoutSeconds, $StallSeconds)
}
function Write-InstalledEditorDiagnostics { param($Version, $Root, $Reason) }
function Get-EnsureEditorInstallTimeoutSeconds { return 2700 }
function Get-EnsureEditorProgressStallSeconds { return 0 }
$recovered = $true
try { $null = Install-UnityEditorWithCiModules -Version '6000.6.7f1' -InstallRoot '/owned' -Reason 'control' -Profile 'EditorOnly' }
catch { $recovered = $false }
ConvertTo-Json -InputObject $recovered -Compress
""",
        ["-Stall", str(stall).lower(), "-Wall", str(wall).lower(), "-ExitCode", str(code)],
    )
    assert result.returncode == 0, result.stderr
    assert json.loads(result.stdout) is recovered


def test_quiet_editor_unpack_uses_absolute_deadline_by_default(tmp_path: Path) -> None:
    result = probe(
        tmp_path,
        r"""
Import-Definition 'Get-EnsureEditorProgressStallSeconds'
Remove-Item Env:UH_ENSURE_EDITOR_PROGRESS_STALL_SECONDS -ErrorAction SilentlyContinue
function Get-UnityProvisioningProfile { return 'EditorOnly' }
function Write-CiNotice { param($Message) }
$default = Get-EnsureEditorProgressStallSeconds
$env:UH_ENSURE_EDITOR_PROGRESS_STALL_SECONDS = '3'
$override = Get-EnsureEditorProgressStallSeconds
ConvertTo-Json -InputObject @($default, $override) -Compress
""",
        [],
    )
    assert result.returncode == 0, result.stderr
    assert json.loads(result.stdout) == [0, 3]


@pytest.mark.parametrize("phase", ["Install-UnityAndroidModules", "Ensure-UnityCiModules"])
@pytest.mark.parametrize("interrupted", [False, True])
def test_module_publication_conditions_reject_interrupted_processes(
    phase: str, interrupted: bool, tmp_path: Path
) -> None:
    result = probe(
        tmp_path,
        r"""
$function = $ast.Find({ param($candidate)
    $candidate -is [Management.Automation.Language.FunctionDefinitionAst] -and
    $candidate.Name -eq $Verb
}, $true)
if ($null -eq $function) { throw 'Missing module install function' }
$pattern = if ($Verb -eq 'Install-UnityAndroidModules') { '\$missingAndroid.Count -eq 0' } else { '\$missingCoreAfter.Count -gt 0' }
$statement = $function.Find({ param($candidate)
    $candidate -is [Management.Automation.Language.IfStatementAst] -and
    $candidate.Clauses[0].Item1.Extent.Text -match $pattern
}, $true)
if ($null -eq $statement) { throw 'Missing module publication condition' }
$missingAndroid = @()
$missingCoreAfter = @()
$result = @{StallKilled = ($Stall -eq 'true'); TimedOutWallClock = $false}
$accepted = [bool](& ([scriptblock]::Create($statement.Clauses[0].Item1.Extent.Text)))
if ($Verb -eq 'Ensure-UnityCiModules') { $accepted = -not $accepted }
ConvertTo-Json -InputObject $accepted -Compress
""",
        ["-Verb", phase, "-Stall", str(interrupted).lower()],
    )
    assert result.returncode == 0, result.stderr
    assert json.loads(result.stdout) is not interrupted


def test_stale_install_cleanup_is_explicitly_confirmed(tmp_path: Path) -> None:
    result = probe(
        tmp_path,
        r"""
Import-Definition 'Invoke-UnityVersionUninstallForRepair'
$script:arguments = @()
function Invoke-UnityCliCapture {
    param($Arguments)
    $script:arguments = $Arguments
    return @{Success = $true}
}
$null = Invoke-UnityVersionUninstallForRepair -Version '6000.6.7f1' -Reason 'stale record'
ConvertTo-Json -InputObject $script:arguments -Compress
""",
        [],
    )
    assert result.returncode == 0, result.stderr
    assert json.loads(result.stdout) == ["uninstall", "6000.6.7f1", "--yes"]
