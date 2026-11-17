"""Compare typed workspace decisions with the original host policy and retention guards."""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "unity"))
import project_workspace as workspace

REPOSITORY = Path(__file__).resolve().parents[2]
FIXTURE = cast(
    "dict[str, object]",
    json.loads((Path(__file__).parent / "fixtures/project-workspace-v1.json").read_bytes()),
)
CASES: list[dict[str, object]] = [
    *cast("list[dict[str, object]]", FIXTURE["cases"]),
    {
        "name": "native-ignored-mode-character",
        "action": "leaf",
        "parameters": {"Version": "6000", "Mode": "pla\u00adymode", "ShareEditorProject": True},
    },
    {
        "name": "native-join-leading-child",
        "action": "workspace",
        "parameters": {"RepoRoot": "@ROOT@/repo", "Version": "/6000", "Mode": "editmode"},
    },
    {
        "name": "native-join-relative-dot",
        "action": "workspace",
        "parameters": {"RepoRoot": "./repo", "Version": "6000", "Mode": "editmode"},
    },
]


@pytest.fixture(scope="module")
def observed(
    tmp_path_factory: pytest.TempPathFactory,
) -> tuple[list[dict[str, object]], list[dict[str, object]]]:
    root = tmp_path_factory.mktemp("workspace-oracle")
    original = root / "original.ps1"
    original.write_bytes(str(FIXTURE["original_source"]).encode())
    assert hashlib.sha256(original.read_bytes()).hexdigest() == FIXTURE["original_source_sha256"]
    driver = root / "capture.ps1"
    driver.write_text(str(FIXTURE["capture_driver"]), encoding="utf-8")
    cases = json.dumps(CASES).replace("@ROOT@", root.as_posix())
    executable = shutil.which("pwsh")
    assert executable is not None
    results: list[list[dict[str, object]]] = []
    for helper in (original, REPOSITORY / "scripts/unity/lib/project-workspace.ps1"):
        result = subprocess.run(
            [executable, "-NoProfile", "-File", str(driver), "-Helper", str(helper)],
            input=cases,
            capture_output=True,
            text=True,
            check=True,
            timeout=90,
        )
        results.append(cast("list[dict[str, object]]", json.loads(result.stdout)))
    return results[0], results[1]


@pytest.mark.parametrize("index", range(len(CASES)), ids=[str(case["name"]) for case in CASES])
def test_original_host_decisions(
    index: int, observed: tuple[list[dict[str, object]], list[dict[str, object]]]
) -> None:
    before, after = observed
    assert after[index]["name"] == before[index]["name"]
    assert after[index]["value"] == before[index]["value"]
    old_error = str(before[index]["error"])
    new_error = str(after[index]["error"])
    assert bool(new_error) == bool(old_error)
    if old_error:
        assert old_error in new_error


@pytest.mark.parametrize("size", [-1.0, float("nan"), float("inf")])
def test_invalid_prune_sizes_fail_closed(size: float) -> None:
    with pytest.raises(ValueError, match="sizes must be finite and nonnegative"):
        workspace.prune_plan([("unsafe", size)], 0, 1)


def test_prune_stops_before_unused_size() -> None:
    assert workspace.prune_plan([("enough", 1), ("unused", float("nan"))], 0, 1) == ["enough"]


@pytest.mark.parametrize(("free", "minimum"), [(float("nan"), 1), (0, float("inf"))])
def test_invalid_space_observations_fail_closed(free: float, minimum: float) -> None:
    with pytest.raises(ValueError, match="space values must be finite"):
        workspace.prune_plan([], free, minimum)


@pytest.mark.parametrize("encoding", ["utf-8-sig", "utf-16", "utf-32"])
def test_retention_reads_native_provenance_encodings(tmp_path: Path, encoding: str) -> None:
    project = tmp_path / "projects/current"
    editor = project / "Assets/Editor"
    editor.mkdir(parents=True)
    configurator = editor / "UhCiTestConfigurator.cs"
    configurator.write_bytes("public static class UhCiTestConfigurator {}".encode(encoding))
    residue = project / ("Assets/OdinLiveAddress-" + "a" * 32)
    residue.mkdir()
    plan = workspace.retention_plan(str(project))
    assert plan is not None
    assert plan["Reasons"] == ["Assets/" + residue.name]
    assert project.is_dir()
    assert not Path(str(plan["RetainedRoot"])).exists()


@pytest.mark.parametrize(
    "location",
    ["Assets", "Assets/Editor", "Library/WallstopOdinMigrationApply", "retained-projects"],
)
def test_linked_ancestors_are_refused(tmp_path: Path, location: str) -> None:
    project = tmp_path / "projects/current"
    project.mkdir(parents=True)
    target = tmp_path / "target"
    target.mkdir()
    link = tmp_path / location if location == "retained-projects" else project / location
    link.parent.mkdir(parents=True, exist_ok=True)
    try:
        link.symlink_to(target, target_is_directory=True)
    except OSError as error:
        if os.name == "nt":
            pytest.skip(f"Host does not permit owned symlink fixtures: {error}")
        raise
    with pytest.raises(ValueError, match="refuses linked paths"):
        workspace.retention_plan(str(project))
    assert target.is_dir()
    assert project.is_dir()


def test_protected_malformed_candidate_is_unused(tmp_path: Path) -> None:
    executable = shutil.which("pwsh")
    assert executable is not None
    script = tmp_path / "unused.ps1"
    helper = REPOSITORY / "scripts/unity/lib/project-workspace.ps1"
    script.write_text(
        "param([string]$Helper)\n$ErrorActionPreference = 'Stop'\n. $Helper\nGet-PersistentProjectPruneOrder -Candidates @(@{Name='current'; LastUsed='invalid'; SizeBytes='invalid'}) -FreeBytes 0 -MinimumFreeBytes 1 -KeepName CURRENT\n",
        encoding="utf-8",
    )
    result = subprocess.run(
        [executable, "-NoProfile", "-File", str(script), "-Helper", str(helper)],
        capture_output=True,
        text=True,
        timeout=30,
    )
    assert result.returncode == 0, result.stderr
    assert not result.stdout.strip()


def test_native_prune_leaves_unused_malformed_sizes_untouched(tmp_path: Path) -> None:
    executable = shutil.which("pwsh")
    assert executable is not None
    script = tmp_path / "unused-size.ps1"
    helper = REPOSITORY / "scripts/unity/lib/project-workspace.ps1"
    script.write_text(
        "param([string]$Helper)\n$ErrorActionPreference = 'Stop'\n. $Helper\nGet-PersistentProjectPruneOrder -Candidates @(@{Name='enough'; LastUsed='2020-01-01'; SizeBytes=1}, @{Name='unused'; LastUsed='2021-01-01'; SizeBytes='invalid'}) -FreeBytes 0 -MinimumFreeBytes 1\n",
        encoding="utf-8",
    )
    result = subprocess.run(
        [executable, "-NoProfile", "-File", str(script), "-Helper", str(helper)],
        capture_output=True,
        text=True,
        timeout=30,
    )
    assert result.returncode == 0, result.stderr
    assert result.stdout.strip() == "enough"


def test_original_native_retention_and_runner_guards(tmp_path: Path) -> None:
    executable = shutil.which("pwsh")
    assert executable is not None
    source = str(FIXTURE["original_driver"])
    assert hashlib.sha256(source.encode()).hexdigest() == FIXTURE["original_driver_sha256"]
    binding = "$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\\..')).Path"
    assert source.count(binding) == 1
    source = source.replace(binding, "$repoRoot = $env:UH_WORKSPACE_CONTROL_ROOT")
    driver = tmp_path / "test-project-workspace.ps1"
    driver.write_bytes(source.encode())
    environment = dict(os.environ, UH_WORKSPACE_CONTROL_ROOT=str(REPOSITORY))
    result = subprocess.run(
        [executable, "-NoProfile", "-File", str(driver)],
        env=environment,
        capture_output=True,
        text=True,
        timeout=90,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "Passed: 63" in result.stdout
    assert "All project-workspace tests passed." in result.stdout


def test_legacy_test_launcher(tmp_path: Path) -> None:
    executable = shutil.which("pwsh")
    assert executable is not None
    launcher = REPOSITORY / "scripts/tests/test-project-workspace.ps1"
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(launcher),
            "-TestFilter",
            "prune_stops_before_unused_size",
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        timeout=90,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "1 passed" in result.stdout


@pytest.mark.parametrize(
    "data",
    [
        "{",
        "[]",
        "null",
        '{"action":"unknown"}',
        '{"action":"leaf","version":3}',
        '{"action":"leaf","share_editor":1}',
        '{"action":"prune-plan","candidates":{}}',
        '{"action":"prune-plan","candidates":[3]}',
    ],
)
def test_cli_protocol_errors_are_visible(data: str) -> None:
    result = subprocess.run(
        [sys.executable, str(REPOSITORY / "scripts/unity/project_workspace.py")],
        input=data,
        capture_output=True,
        text=True,
        timeout=30,
    )
    assert result.returncode == 1
    response = cast("dict[str, object]", json.loads(result.stdout))
    assert response["value"] is None
    assert response["error"]


@pytest.mark.parametrize("size", [None, False, True])
def test_native_scalar_size_conversion(size: object) -> None:
    assert workspace.prune_plan([("candidate", size)], 0, 1) == ["candidate"]
    assert workspace.number(size) == int(bool(size))


@pytest.mark.skipif(os.name == "nt", reason="POSIX directory traversal permission fixture")
def test_unreadable_residue_pointer_fails_visibly(tmp_path: Path) -> None:
    project = tmp_path / "projects/current"
    residue = project / "Library/WallstopOdinMigrationApply"
    residue.mkdir(parents=True)
    (residue / "active.txt").write_text("retain", encoding="utf-8")
    residue.chmod(0)
    try:
        with pytest.raises(PermissionError):
            workspace.retention_plan(str(project))
    finally:
        residue.chmod(0o700)


@pytest.mark.parametrize(
    ("parameter", "value"),
    [("FreeBytes", "PositiveInfinity"), ("MinimumFreeBytes", "NegativeInfinity")],
)
def test_native_space_guard_precedes_early_return(
    tmp_path: Path, parameter: str, value: str
) -> None:
    executable = shutil.which("pwsh")
    assert executable is not None
    script = tmp_path / "nonfinite.ps1"
    script.write_text(
        "param([string]$Helper)\n$ErrorActionPreference = 'Stop'\n. $Helper\n"
        f"$parameters = @{{ Candidates=@(); FreeBytes=1; MinimumFreeBytes=0 }}; $parameters.{parameter} = [double]::{value}\nGet-PersistentProjectPruneOrder @parameters\n",
        encoding="utf-8",
    )
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(script),
            "-Helper",
            str(REPOSITORY / "scripts/unity/lib/project-workspace.ps1"),
        ],
        capture_output=True,
        text=True,
        timeout=30,
    )
    assert result.returncode != 0
    assert "space values must be finite" in result.stderr


@pytest.mark.parametrize(
    ("present", "failure"), [(False, False), (True, False), (False, True), (True, True)]
)
def test_native_adapter_preserves_caller_exit_status(
    tmp_path: Path, present: bool, failure: bool
) -> None:
    executable = shutil.which("pwsh")
    assert executable is not None
    script = tmp_path / "exit-status.ps1"
    setup = (
        "$global:LASTEXITCODE=41"
        if present
        else "Remove-Variable LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue"
    )
    call = (
        "Resolve-UnityProjectWorkspace -RepoRoot . -Version 6000 -Mode editmode -GenerationFingerprint invalid"
        if failure
        else "Get-UnityProjectLeafName -Version 6000 -Mode editmode"
    )
    script.write_text(
        "param([string]$Helper)\n$ErrorActionPreference='Stop'\n. $Helper\n"
        + setup
        + "\n$failed=$false\ntry { $null="
        + call
        + " } catch { $failed=$true }\n$exitVariable=Get-Variable LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue\n@{ present=($null -ne $exitVariable); value=$(if ($null -ne $exitVariable) {$exitVariable.Value}); failed=$failed } | ConvertTo-Json -Compress\n",
        encoding="utf-8",
    )
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(script),
            "-Helper",
            str(REPOSITORY / "scripts/unity/lib/project-workspace.ps1"),
        ],
        capture_output=True,
        text=True,
        timeout=30,
    )
    assert result.returncode == 0, result.stderr
    assert json.loads(result.stdout) == {
        "present": present,
        "value": 41 if present else None,
        "failed": failure,
    }


def test_native_adapter_reads_actual_child_status_with_local_shadow(tmp_path: Path) -> None:
    executable = shutil.which("pwsh")
    assert executable is not None
    script = tmp_path / "shadow.ps1"
    script.write_text(
        "param([string]$Helper)\n$ErrorActionPreference='Stop'\n. $Helper\nfunction Invoke-Fixture { $local:LASTEXITCODE=73; Get-UnityProjectLeafName -Version 6000 -Mode editmode }\nInvoke-Fixture\n",
        encoding="utf-8",
    )
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(script),
            "-Helper",
            str(REPOSITORY / "scripts/unity/lib/project-workspace.ps1"),
        ],
        capture_output=True,
        text=True,
        timeout=30,
    )
    assert result.returncode == 0, result.stderr
    assert result.stdout.strip() == "6000-editmode"


def test_review_prune_planning_failure_is_best_effort_and_preserves_files(tmp_path: Path) -> None:
    projects = tmp_path / "projects"
    project = projects / "old"
    project.mkdir(parents=True)
    retained = project / "owned.txt"
    retained.write_bytes(b"preserve")
    script = tmp_path / "prune-failure.ps1"
    script.write_text(
        "param([string]$Helper,[string]$Projects)\n"
        "$ErrorActionPreference='Stop'\n. $Helper\n"
        "function Get-PersistentProjectPruneOrder { throw 'Owned planning failure.' }\n"
        "$deleted=@(Invoke-PersistentProjectPrune -ProjectsRoot $Projects -KeepName current -MinimumFreeGb 1000000000000)\n"
        "@{deleted=$deleted; continued=$true} | ConvertTo-Json -Compress\n",
        encoding="utf-8",
    )
    executable = shutil.which("pwsh")
    assert executable is not None
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(script),
            str(REPOSITORY / "scripts/unity/lib/project-workspace.ps1"),
            str(projects),
        ],
        capture_output=True,
        text=True,
        timeout=30,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert json.loads(result.stdout.splitlines()[-1]) == {"deleted": [], "continued": True}
    assert retained.read_bytes() == b"preserve"
    assert "Owned planning failure" in result.stdout
