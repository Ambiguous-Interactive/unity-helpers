"""Exercise hook rules against original CLI decisions and each diagnostic."""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path
from typing import cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import hook_validation as hooks

ROOT = Path(__file__).resolve().parents[2]
FIXTURE = cast(
    "dict[str, object]",
    json.loads((Path(__file__).parent / "fixtures/hook-validation-v1.json").read_bytes()),
)
CASES = cast("list[dict[str, object]]", FIXTURE["cases"])


@pytest.mark.parametrize("case", CASES, ids=lambda case: str(case["name"]))
def test_original_decisions(tmp_path: Path, case: dict[str, object]) -> None:
    files = cast("dict[str, str]", case["files"])
    directory = tmp_path / ".githooks"
    directory.mkdir()
    for name, content in files.items():
        (directory / name).write_text(content, encoding="utf-8")
    result = hooks.validate(tmp_path)
    expected = 1 if str(case["name"]).startswith("empty-") else case["status"]
    assert int(bool(result.errors)) == expected
    if case["status"] == 1:
        assert result.errors


@pytest.mark.parametrize("name", ["pre-commit.ps1", "pre-push.ps1", "pre-merge-commit.ps1"])
def test_directory_is_not_a_hook(tmp_path: Path, name: str) -> None:
    for hook, patterns, _, _ in hooks.RULES:
        target = tmp_path / ".githooks" / hook
        target.parent.mkdir(exist_ok=True)
        if hook == name:
            target.mkdir()
        else:
            target.write_text("\n".join(patterns), encoding="utf-8")
    result = hooks.validate(tmp_path)
    assert result.errors
    assert name in result.errors[0]


def test_current_hooks() -> None:
    result = hooks.validate(ROOT)
    assert not result.errors
    assert len(result.passed) == 3


def test_cli_rejects_unknown_arguments() -> None:
    result = subprocess.run(
        [sys.executable, str(ROOT / "scripts/hook_validation.py"), "--unexpected"],
        capture_output=True,
        text=True,
        check=False,
    )
    assert result.returncode != 0
    assert "unrecognized arguments" in result.stderr


def test_cli_verbose(tmp_path: Path) -> None:
    for hook, patterns, _, _ in hooks.RULES:
        target = tmp_path / ".githooks" / hook
        target.parent.mkdir(exist_ok=True)
        target.write_text("\n".join(patterns), encoding="utf-8")
    result = subprocess.run(
        [
            sys.executable,
            str(ROOT / "scripts/hook_validation.py"),
            "--repo-root",
            str(tmp_path),
            "--verbose",
        ],
        capture_output=True,
        check=False,
    )
    assert result.returncode == 0
    assert len(result.stdout.splitlines()) == 3
    assert b"\r" not in result.stdout


@pytest.mark.parametrize(
    "pattern", ["Get-Process -Id $PID", "Get-Command pwsh", "& $pwshPath", "$invokeArgs +="]
)
def test_forbidden_diagnostics(tmp_path: Path, pattern: str) -> None:
    for hook, patterns, _, _ in hooks.RULES:
        target = tmp_path / ".githooks" / hook
        target.parent.mkdir(exist_ok=True)
        target.write_text(
            "\n".join(patterns) + ("\n" + pattern if hook == "pre-merge-commit.ps1" else ""),
            encoding="utf-8",
        )
    assert "must delegate to pre-commit.ps1 in-process" in hooks.validate(tmp_path).errors[0]


@pytest.mark.parametrize(
    ("name", "pattern"),
    [(name, pattern) for name, patterns, _, _ in hooks.RULES for pattern in patterns],
)
def test_each_missing_pattern_is_reported(tmp_path: Path, name: str, pattern: str) -> None:
    for hook, patterns, _, _ in hooks.RULES:
        target = tmp_path / ".githooks" / hook
        target.parent.mkdir(exist_ok=True)
        target.write_text(
            "\n".join(value for value in patterns if hook != name or value != pattern),
            encoding="utf-8",
        )
    result = hooks.validate(tmp_path)
    assert result.errors
    assert f"  - {pattern}" in result.errors
    assert "missing 1" in result.errors[0]


def test_invalid_utf8_is_a_failure(tmp_path: Path) -> None:
    path = tmp_path / ".githooks" / "pre-commit.ps1"
    path.parent.mkdir()
    path.write_bytes(b"\xff")
    assert "Cannot read pre-commit.ps1" in hooks.validate(tmp_path).errors[0]


def test_original_receipt_has_all_subjects() -> None:
    assert len(CASES) == 24
    assert len({str(case["name"]) for case in CASES}) == 24
    assert sum(case["status"] == 1 for case in CASES) == 16
    assert {str(case["name"]) for case in CASES if str(case["name"]).startswith("empty-")} == {
        "empty-pre-commit.ps1",
        "empty-pre-push.ps1",
        "empty-pre-merge-commit.ps1",
    }


@pytest.mark.parametrize(
    "launcher",
    ["scripts/validate-hook-sync-calls.ps1", "scripts/tests/test-validate-hook-sync-calls.ps1"],
)
def test_legacy_launchers(tmp_path: Path, launcher: str) -> None:
    import shutil

    executable = shutil.which("pwsh")
    assert executable is not None
    command = [executable, "-NoProfile", "-File", str(ROOT / launcher)]
    if "scripts/tests/" in launcher:
        command.extend(["-TestFilter", "test_current_hooks"])
    else:
        command.extend(["-RepoRoot", str(ROOT), "-VerboseOutput"])
    result = subprocess.run(command, cwd=tmp_path, capture_output=True, check=False, timeout=60)
    assert result.returncode == 0, result.stdout + result.stderr
    assert (
        b"correctly" in result.stdout
        if "scripts/tests/" not in launcher
        else b"1 passed" in result.stdout
    )


@pytest.mark.parametrize("encoding", ["utf-8-sig", "utf-16", "utf-32"])
def test_bom_hook_encodings(tmp_path: Path, encoding: str) -> None:
    for hook, patterns, _, _ in hooks.RULES:
        target = tmp_path / ".githooks" / hook
        target.parent.mkdir(exist_ok=True)
        target.write_bytes("\n".join(patterns).encode(encoding))
    assert not hooks.validate(tmp_path).errors


@pytest.mark.parametrize("kind", ["null", "empty", "whitespace", "explicit"])
def test_review_native_empty_root_uses_wrapper_repository(tmp_path: Path, kind: str) -> None:
    import shutil

    executable = shutil.which("pwsh")
    assert executable is not None
    script = tmp_path / "root.ps1"
    script.write_text(
        "param([string]$Wrapper,[string]$Kind,[string]$Foreign)\n"
        "Set-Location -LiteralPath $Foreign\n"
        "$value=switch ($Kind) { null {$null}; empty {''}; whitespace {'   '}; explicit {$Foreign} }\n"
        "& $Wrapper -RepoRoot $value\nexit $LASTEXITCODE\n",
        encoding="utf-8",
    )
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(script),
            str(ROOT / "scripts/validate-hook-sync-calls.ps1"),
            kind,
            str(tmp_path),
        ],
        capture_output=True,
        text=True,
        timeout=30,
    )
    assert result.returncode == (1 if kind == "explicit" else 0), result.stdout + result.stderr
