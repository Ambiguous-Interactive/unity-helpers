"""Exercise the actual maintenance dispatch resolver and its output boundary."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast

import pytest
import yaml

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "unity"))
from runner_maintenance_matrix import PROFILES, ROOT, main, resolve_versions


def config(root: Path, value: object) -> Path:
    path = root / "versions.json"
    path.write_text(json.dumps(value), encoding="utf-8")
    return path


def test_default_matches_canonical_versions() -> None:
    path = ROOT / ".github/unity-versions.json"
    document = json.loads(path.read_text(encoding="utf-8"))
    assert resolve_versions(path, "") == list(
        dict.fromkeys(document["all"] + document["maintenance"])
    )
    assert "6000.6.7f1" in resolve_versions(path, "")
    assert "6000.6.7f1" not in document["all"]


def test_default_includes_downstream_editors_without_duplicates(tmp_path: Path) -> None:
    path = config(tmp_path, {"all": ["2021.3.45f1"], "maintenance": ["6000.6.7f1", "2021.3.45f1"]})
    assert resolve_versions(path, "") == ["2021.3.45f1", "6000.6.7f1"]
    assert resolve_versions(path, "2022.3.45f1") == ["2022.3.45f1"]


@pytest.mark.parametrize("value", [None, False, "6000.6.7f1", [False], ["bad"]])
def test_invalid_maintenance_configuration_is_rejected(value: object, tmp_path: Path) -> None:
    with pytest.raises(ValueError, match=r"Unity"):
        resolve_versions(config(tmp_path, {"all": ["2021.3.45f1"], "maintenance": value}), "")


@pytest.mark.parametrize("selection", ["6000.6.7f1", " 6000.6.7f1 ", "6000.6.7f1,6000.6.7f1"])
def test_explicit_version_requires_no_config(selection: str, tmp_path: Path) -> None:
    assert resolve_versions(tmp_path / "missing", selection) == ["6000.6.7f1"]


def test_preserves_order_and_removes_duplicates(tmp_path: Path) -> None:
    assert resolve_versions(tmp_path / "missing", "2021.3.45f1, 6000.6.7f1\n2021.3.45f1") == [
        "2021.3.45f1",
        "6000.6.7f1",
    ]


@pytest.mark.parametrize(
    "value",
    [
        None,
        [],
        {},
        {"all": []},
        {"all": [True]},
        {"all": "6000.6.7f1"},
        {"all": ["bad"]},
    ],
)
def test_invalid_configuration_is_rejected(value: object, tmp_path: Path) -> None:
    with pytest.raises(ValueError, match=r"Unity|Select"):
        resolve_versions(config(tmp_path, value), "")


@pytest.mark.parametrize(
    "value",
    [
        "latest",
        "6000.6.7f0",
        "6000.6.7f1;echo",
        "../6000.6.7f1",
        "6000.6.7f1\nprofile=Full",
        ",",
        "6000.6.7f1,",
        "x" * 2049,
        ",".join(["6000.6.7f1"] * 17),
    ],
)
def test_invalid_operator_selection_is_rejected(value: str, tmp_path: Path) -> None:
    with pytest.raises(ValueError, match=r"Unity|Select"):
        resolve_versions(tmp_path / "missing", value)


@pytest.mark.parametrize("profile", PROFILES)
def test_output_is_appended_after_validation(profile: str, tmp_path: Path) -> None:
    output = tmp_path / "output"
    output.write_text("previous=value\n", encoding="utf-8")
    assert main(["--versions", "6000.6.7f1", "--profile", profile, "--output", str(output)]) == 0
    assert output.read_text(encoding="utf-8") == (
        'previous=value\nmatrix={"unity-version":["6000.6.7f1"]}\n' + f"profile={profile}\n"
    )


def test_invalid_input_preserves_output(tmp_path: Path) -> None:
    output = tmp_path / "output"
    output.write_text("existing\n", encoding="utf-8")
    assert main(["--versions", "bad", "--output", str(output)]) == 1
    assert output.read_text(encoding="utf-8") == "existing\n"


def test_cli_uses_workflow_environment_from_another_directory(tmp_path: Path) -> None:
    environment = dict(
        os.environ,
        UH_MAINTENANCE_VERSIONS="6000.6.7f1",
        UH_MAINTENANCE_PROFILE="EditorOnly",
    )
    result = subprocess.run(
        [sys.executable, str(ROOT / "scripts/unity/runner_maintenance_matrix.py")],
        cwd=tmp_path,
        env=environment,
        capture_output=True,
        text=True,
        check=False,
        timeout=20,
    )
    assert result.returncode == 0, result.stderr
    assert result.stdout == 'matrix={"unity-version":["6000.6.7f1"]}\nprofile=EditorOnly\n'


def test_bad_profile_cannot_emit_outputs(tmp_path: Path) -> None:
    output = tmp_path / "output"
    with pytest.raises(SystemExit) as error:
        main(["--versions", "6000.6.7f1", "--profile", "bad", "--output", str(output)])
    assert error.value.code == 2
    assert not output.exists()


def test_unwritable_output_fails(tmp_path: Path) -> None:
    assert main(["--versions", "6000.6.7f1", "--output", str(tmp_path)]) == 1


def workflow_jobs() -> dict[str, dict[str, object]]:
    document = cast(
        "dict[object, object]",
        yaml.safe_load(
            (ROOT / ".github/workflows/runner-bootstrap.yml").read_text(encoding="utf-8")
        ),
    )
    return cast("dict[str, dict[str, object]]", document["jobs"])


def dependency_closure(job: str, jobs: dict[str, dict[str, object]]) -> set[str]:
    found: set[str] = set()
    pending = [job]
    while pending:
        current = pending.pop()
        raw = jobs[current].get("needs", [])
        parents = [raw] if isinstance(raw, str) else cast("list[str]", raw)
        for parent in parents:
            if parent not in found:
                found.add(parent)
                pending.append(parent)
    return found


def test_matrix_validation_precedes_both_host_jobs() -> None:
    jobs = workflow_jobs()
    for job in ("bootstrap-host", "maintain-unity"):
        assert "matrix-config" in dependency_closure(job, jobs), job


def test_removed_validation_edges_are_detected() -> None:
    jobs = workflow_jobs()
    mutant = {name: dict(job) for name, job in jobs.items()}
    for job in mutant.values():
        raw = job.get("needs", [])
        parents = [raw] if isinstance(raw, str) else cast("list[str]", raw)
        job["needs"] = [parent for parent in parents if parent != "matrix-config"]
    assert not all(
        "matrix-config" in dependency_closure(job, mutant)
        for job in ("bootstrap-host", "maintain-unity")
    )


@pytest.mark.parametrize(
    ("document", "requested", "expected"),
    [
        (
            {"all": ["2021.3.45f1"], "maintenance": ["6000.6.7f1"]},
            "",
            ["2021.3.45f1", "6000.6.7f1"],
        ),
        ({"all": ["2021.3.45f1"]}, "", ["2021.3.45f1"]),
        ({"all": ["2021.3.45f1"], "maintenance": []}, "", ["2021.3.45f1"]),
        ({"all": ["2021.3.45f1"], "maintenance": ["2021.3.45f1"]}, "", ["2021.3.45f1"]),
        ({"all": ["2021.3.45f1"]}, "6000.6.7f1,2022.3.45f1", ["6000.6.7f1", "2022.3.45f1"]),
        ({"all": ["2021.3.45f1"], "maintenance": ["bad"]}, "6000.6.7f1", ["6000.6.7f1"]),
        ({"all": ["2021.3.45f1"], "maintenance": ["bad"]}, "", None),
        ({"all": [], "maintenance": ["6000.6.7f1"]}, "", None),
        ({"all": ["2021.3.45f1"], "maintenance": None}, "", None),
        ({"all": ["2021.3.45f1"], "maintenance": "6000.6.7f1"}, "", None),
        ({"all": ["2021.3.45f1"], "maintenance": [False]}, "", None),
        ({"all": ["2021.3.45f1"], "maintenance": ["6000.6.7f1"] * 16}, "", None),
        ({"all": "2021.3.45f1"}, "", None),
        ({"all": ["2021.3.45f1"]}, "bad", None),
        ({"all": ["2021.3.45f1"]}, "6000.6.7f1,", None),
    ],
)
def test_manual_and_workflow_version_selection_agree(
    document: dict[str, object], requested: str, expected: list[str] | None, tmp_path: Path
) -> None:
    shell = shutil.which("pwsh")
    if shell is None:
        pytest.skip("PowerShell is required to exercise the Windows compatibility entry point")
    github = tmp_path / ".github"
    github.mkdir()
    path = github / "unity-versions.json"
    path.write_text(json.dumps(document), encoding="utf-8")
    probe = tmp_path / "resolve.ps1"
    probe.write_text(
        "param([string]$MaintenanceScript, [string]$Root, [string]$Requested)\n"
        "$ErrorActionPreference = 'Stop'\n"
        ". $MaintenanceScript\n"
        "try {\n"
        "  $values = @(Resolve-RunnerMaintenanceUnityVersions -UnityVersions @($Requested) -RepoRoot $Root)\n"
        "  Write-Output ('RESULT:' + (ConvertTo-Json -InputObject $values -Compress))\n"
        "} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }\n",
        encoding="utf-8",
    )
    result = subprocess.run(
        [
            shell,
            "-NoProfile",
            "-NonInteractive",
            "-File",
            str(probe),
            "-MaintenanceScript",
            str(ROOT / "scripts/unity/maintain-windows-runner.ps1"),
            "-Root",
            str(tmp_path),
            "-Requested",
            requested,
        ],
        capture_output=True,
        encoding="utf-8",
        check=False,
        timeout=30,
    )
    if expected is None:
        assert result.returncode != 0, result.stdout
        with pytest.raises(ValueError, match=r"Unity|Select"):
            resolve_versions(path, requested)
    else:
        assert result.returncode == 0, result.stderr
        output = next(
            line.removeprefix("RESULT:")
            for line in result.stdout.splitlines()
            if line.startswith("RESULT:")
        )
        assert json.loads(output) == expected
        assert resolve_versions(path, requested) == expected
