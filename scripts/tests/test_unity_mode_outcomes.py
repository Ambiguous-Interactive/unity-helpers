"""Exercise Unity mode outcomes through the rule and both command entry points."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from itertools import combinations
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "unity"))
import assert_test_mode_outcomes as gate

ROOT = Path(__file__).resolve().parents[2]
PYTHON_CLI = ROOT / "scripts/unity/assert_test_mode_outcomes.py"
POWERSHELL_CLI = ROOT / "scripts/unity/assert-test-mode-outcomes.ps1"
SELECTIONS = [subset for count in range(1, 4) for subset in combinations(gate.MODES, count)]


def outcomes(*selected: str) -> dict[str, str]:
    return {
        "SELECTED_MODES": json.dumps(selected),
        **{
            f"{mode.upper()}_{stage}": "success" if mode in selected else "skipped"
            for mode in gate.MODES
            for stage in gate.STAGES
        },
    }


@pytest.mark.parametrize("selection", SELECTIONS)
def test_every_nonempty_selection(selection: tuple[str, ...]) -> None:
    gate.assert_mode_outcomes(outcomes(*selection))


@pytest.mark.parametrize("raw", ['"EDITMODE"', '["EDITMODE"]', '["editmode"]'])
def test_single_mode_and_case_compatibility(raw: str) -> None:
    environment = {name: value.upper() for name, value in outcomes("editmode").items()}
    environment["SELECTED_MODES"] = raw
    gate.assert_mode_outcomes(environment)


@pytest.mark.parametrize(
    ("raw", "message"),
    [
        ("", "Expecting value"),
        ("[", "Expecting value"),
        ("[]", "must be a non-empty set"),
        ('["editmode","editmode"]', "must be a non-empty set"),
        ('["editmode","EDITMODE"]', "must be a non-empty set"),
        ('["editmode","other"]', "Unknown selected Unity modes: other"),
        ("null", "must be a JSON array or a single mode string"),
        ('{"editmode":true}', "must be a JSON array or a single mode string"),
        ('["editmode",null]', "must be strings"),
    ],
)
def test_invalid_selection_reports_the_cause(raw: str, message: str) -> None:
    environment = outcomes("editmode")
    environment["SELECTED_MODES"] = raw
    with pytest.raises(ValueError, match=message):
        gate.assert_mode_outcomes(environment)


@pytest.mark.parametrize("mode", gate.MODES)
@pytest.mark.parametrize("stage", gate.STAGES)
def test_every_selected_stage_is_required(mode: str, stage: str) -> None:
    environment = outcomes(mode)
    environment.pop(f"{mode.upper()}_{stage}")
    with pytest.raises(ValueError, match=rf"{mode}/{stage}= \(expected success\)"):
        gate.assert_mode_outcomes(environment)


def test_outcomes_report_all_selected_and_unselected_failures_in_order() -> None:
    environment = outcomes("playmode", "standalone")
    environment["EDITMODE_VERIFY"] = "success"
    environment["PLAYMODE_REDACT"] = "cancelled"
    environment["STANDALONE_UPLOAD"] = "failure"
    with pytest.raises(ValueError, match=r"^Unity mode failures:") as error:
        gate.assert_mode_outcomes(environment)
    assert str(error.value) == (
        "Unity mode failures: editmode/VERIFY=success (expected skipped); "
        "playmode/REDACT=cancelled (expected success); standalone/UPLOAD=failure (expected success)"
    )


@pytest.mark.parametrize("controller", [None, "", "SUCCESS", "failure", "cancelled"])
def test_combined_controller_requires_exact_success(controller: str | None) -> None:
    environment = outcomes("editmode")
    if controller is not None:
        environment["CONTROLLER"] = controller
    with pytest.raises(ValueError, match="The combined controller did not complete"):
        gate.assert_mode_outcomes(environment, require_controller=True)
    gate.assert_mode_outcomes(environment)


def test_successful_controller_still_checks_modes() -> None:
    environment = {**outcomes("editmode"), "CONTROLLER": "success"}
    gate.assert_mode_outcomes(environment, require_controller=True)
    environment["EDITMODE_RUN"] = "failure"
    with pytest.raises(ValueError, match="editmode/RUN=failure"):
        gate.assert_mode_outcomes(environment, require_controller=True)


def run_cli(
    command: list[str], variables: dict[str, str], directory: Path
) -> subprocess.CompletedProcess[str]:
    environment = {**os.environ, **variables}
    environment.pop("VIRTUAL_ENV", None)
    return subprocess.run(
        command,
        cwd=directory,
        env=environment,
        capture_output=True,
        text=True,
        check=False,
        timeout=30,
    )


@pytest.mark.parametrize(
    ("changes", "message"),
    [
        ({}, ""),
        (
            {"EDITMODE_UPLOAD": "failure"},
            "Unity mode failures: editmode/UPLOAD=failure (expected success)",
        ),
        (
            {"SELECTED_MODES": '["editmode","EDITMODE"]'},
            "Selected Unity modes must be a non-empty set",
        ),
    ],
)
def test_powershell_entry_point_retains_python_exit_and_diagnostic(
    changes: dict[str, str], message: str, tmp_path: Path
) -> None:
    shell = shutil.which("pwsh")
    if shell is None:
        pytest.skip("The PowerShell compatibility entry point requires pwsh.")
    environment = {**outcomes("editmode"), **changes}
    directory = tmp_path / "directory with spaces"
    directory.mkdir()
    direct = run_cli([sys.executable, str(PYTHON_CLI)], environment, directory)
    adapter = run_cli([shell, "-NoProfile", "-File", str(POWERSHELL_CLI)], environment, directory)
    assert adapter.returncode == direct.returncode == int(bool(message))
    assert adapter.stdout == direct.stdout == ""
    assert adapter.stderr == direct.stderr
    assert message in direct.stderr


def test_cli_controller_failure_uses_stderr(tmp_path: Path) -> None:
    result = run_cli(
        [sys.executable, str(PYTHON_CLI), "--require-controller"],
        {**outcomes("editmode"), "CONTROLLER": "failure"},
        tmp_path,
    )
    assert result.returncode == 1
    assert result.stdout == ""
    assert result.stderr == "The combined controller did not complete.\n"
