"""Exercise the actual maintenance dispatch resolver and its output boundary."""

from __future__ import annotations

import json
import os
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
    assert resolve_versions(path, "") == json.loads(path.read_text(encoding="utf-8"))["all"]


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
