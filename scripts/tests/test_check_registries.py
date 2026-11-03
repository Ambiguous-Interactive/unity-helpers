"""Preserve check selection, failure gates, and native CLI behavior during migration."""

from __future__ import annotations

import json
import subprocess
import sys
from dataclasses import asdict
from pathlib import Path
from typing import Literal

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import check_runner
from check_registries import ROOT, checks_for, load_checks, main


def write_registry(root: Path, name: str, checks: object) -> None:
    destination = root / "scripts" / f"{name}-checks.json"
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(checks), encoding="utf-8")


def entry(identifier: str = "subject", command: str = "exit 0") -> dict[str, str]:
    return {"id": identifier, "name": "Subject", "run": command}


@pytest.mark.parametrize("registry", ["lint", "contracts"])
def test_cli_lists_exact_order_without_a_node_process(
    registry: Literal["lint", "contracts"], tmp_path: Path
) -> None:
    result = subprocess.run(
        [sys.executable, str(ROOT / "scripts/check_registries.py"), registry, "--list"],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=20,
    )
    assert result.returncode == 0, result.stderr
    assert result.stdout.splitlines() == [check.id for check in checks_for(registry)]
    assert '{"kind"' not in result.stdout


@pytest.mark.parametrize("registry", ["lint", "contracts"])
@pytest.mark.parametrize("direct", [False, True])
def test_direct_selection_retains_every_check_and_policy(
    registry: Literal["lint", "contracts"], direct: bool
) -> None:
    original = checks_for(registry)
    selected = checks_for(registry, direct=direct)
    assert len(original) == len(selected) > 0
    scripts = json.loads((ROOT / "package.json").read_text(encoding="utf-8"))["scripts"]
    for before, after in zip(original, selected, strict=True):
        assert asdict(before) | {"run": after.run} == asdict(after)
        expected = (
            scripts[before.run[8:]] if direct and before.run.startswith("npm run ") else before.run
        )
        assert after.run == expected


def test_hooks_precede_shared_checks_and_retain_exclusion_policy() -> None:
    checks = checks_for("contracts", include_hooks=True)
    assert checks == [*load_checks("hooks"), *load_checks("contracts")]
    assert len({check.id for check in checks}) == len(checks)
    assert all(check.exclusive for check in checks if check.id == "npm-package-changelog")
    completion = [check for check in checks if check.completion_pattern is not None]
    assert any(check.id == "unity-acceptance-runner" for check in completion)


@pytest.mark.parametrize(
    "contents",
    [[], [entry(), entry()], [entry("")], [entry(command=" ")], [entry() | {"name": ""}], {}],
)
def test_rejects_empty_duplicate_or_invalid_registry(contents: object, tmp_path: Path) -> None:
    write_registry(tmp_path, "lint", contents)
    with pytest.raises(ValueError, match=r"[Cc]heck (registry|array)"):
        load_checks("lint", tmp_path)


def test_duplicate_ids_across_hook_and_contract_registries_are_rejected(tmp_path: Path) -> None:
    write_registry(tmp_path, "hooks", [entry()])
    write_registry(tmp_path, "contracts", [entry()])
    with pytest.raises(ValueError, match="duplicate"):
        checks_for("contracts", include_hooks=True, root=tmp_path)


@pytest.mark.parametrize("value", [None, 5, "", " "])
def test_missing_or_invalid_package_command_fails_closed(value: object, tmp_path: Path) -> None:
    write_registry(tmp_path, "contracts", [entry(command="npm run absent")])
    (tmp_path / "package.json").write_text(
        json.dumps({"scripts": {"absent": value}}), encoding="utf-8"
    )
    with pytest.raises(ValueError, match="No package script resolves"):
        checks_for("contracts", direct=True, root=tmp_path)


def test_non_npm_commands_survive_direct_selection(tmp_path: Path) -> None:
    write_registry(tmp_path, "lint", [entry(command="python subject.py --flag")])
    (tmp_path / "package.json").write_text('{"scripts": {}}', encoding="utf-8")
    assert checks_for("lint", direct=True, root=tmp_path)[0].run == "python subject.py --flag"


@pytest.mark.parametrize("arguments", [[], ["unknown"], ["lint", "--include-hook-regressions"]])
def test_invalid_cli_fails_closed(arguments: list[str], monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(check_runner, "OUTPUT_FORMAT", "json")
    assert main(arguments) == 1


@pytest.mark.parametrize("arguments", [["--only", "absent"], ["--jobs", "0"], ["--jobs", "nan"]])
def test_selection_errors_return_failure_without_running_checks(
    arguments: list[str], monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    monkeypatch.setattr(check_runner, "OUTPUT_FORMAT", "json")
    assert main(["contracts", *arguments]) == 1
    assert "::error::" in capsys.readouterr().out


@pytest.mark.parametrize("field", [{"exclusive": "true"}, {"completionPatern": "done"}])
def test_misspelled_or_mistyped_safety_policy_fails_closed(
    field: dict[str, str], tmp_path: Path
) -> None:
    write_registry(tmp_path, "contracts", [entry() | field])
    with pytest.raises(ValueError, match=r"(configuration field|boolean)"):
        checks_for("contracts", root=tmp_path)
