"""Replay the original launcher and exercise actual child stdin, arguments and exit status."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import TypedDict, cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import run_node_bin as runner


class Case(TypedDict):
    name: str
    args: list[str]
    manifest: object
    body: str | None
    input: str
    code: int
    stdout: str
    stderr: str


def cases() -> list[Case]:
    fixture = Path(__file__).parent / "fixtures/node-bin-original.json"
    return cast("list[Case]", cast("dict[str, object]", json.loads(fixture.read_text()))["cases"])


def install(root: Path, case: Case) -> Path:
    scripts = root / "scripts"
    scripts.mkdir(parents=True)
    shutil.copyfile(Path(runner.__file__), scripts / "run_node_bin.py")
    shutil.copyfile(Path(runner.__file__).with_name("cli_encoding.py"), scripts / "cli_encoding.py")
    package = root / "node_modules/prettier"
    package.mkdir(parents=True)
    manifest = case["manifest"]
    if manifest is not None:
        (package / "package.json").write_text(
            manifest if isinstance(manifest, str) else json.dumps(manifest), encoding="utf-8"
        )
    bodies = {
        "echo": "console.log(JSON.stringify({args:process.argv.slice(2),cwd:process.cwd()}));",
        "seven": "process.exit(7);",
        "stdin": "process.stdin.pipe(process.stdout);",
    }
    body = case["body"]
    if body is not None:
        (package / "cli.js").write_text(bodies[body], encoding="utf-8")
    return scripts / "run_node_bin.py"


@pytest.mark.parametrize("case", cases(), ids=lambda case: case["name"])
def test_original_cli_observations(case: Case, tmp_path: Path) -> None:
    script = install(tmp_path, case)
    result = subprocess.run(
        [sys.executable, str(script), *case["args"]],
        cwd=Path(runner.__file__).parents[1],
        input=case["input"],
        capture_output=True,
        encoding="utf-8",
        timeout=30,
        check=False,
    )
    assert result.returncode == case["code"]
    stdout = result.stdout.replace(str(tmp_path), "$ROOT").replace("\\\\", "/")
    stderr = result.stderr.replace(str(tmp_path), "$ROOT")
    if case["name"] == "missing-file":
        stderr = stderr.replace("\\", "/")
    if case["body"] == "echo" and case["args"][1:] != ["--check-install", "ignored"]:
        actual = cast("dict[str, object]", json.loads(result.stdout))
        expected = cast("dict[str, object]", json.loads(case["stdout"]))
        assert actual["args"] == expected["args"]
        assert Path(cast("str", actual["cwd"])) == tmp_path
    else:
        assert stdout == case["stdout"]
    if case["name"] == "invalid-json":
        assert stderr.startswith("Failed to read $ROOT")
    else:
        assert stderr == case["stderr"]


@pytest.mark.parametrize(
    "manifest",
    [
        [],
        None,
        3,
        {"bin": 3},
        {"bin": {"prettier": []}},
        {"bin": "bad\0path"},
        {"bin": {"prettier": "bad\0path"}},
    ],
)
def test_invalid_bin_shapes_fail_without_traceback(manifest: object, tmp_path: Path) -> None:
    package = tmp_path / "node_modules/prettier"
    package.mkdir(parents=True)
    (package / "package.json").write_text(json.dumps(manifest), encoding="utf-8")
    assert runner.main(["prettier"], root=tmp_path) == 1


def test_missing_node_fails_after_install_check(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    case = next(case for case in cases() if case["name"] == "string-bin")
    install(tmp_path, case)
    monkeypatch.delenv("UH_NODE_EXECUTABLE", raising=False)
    monkeypatch.setenv("PATH", "")
    assert runner.main(["prettier", "--check-install"], root=tmp_path) == 0
    assert runner.main(["prettier"], root=tmp_path) == 1


def test_node_start_failure_returns_one(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    case = next(case for case in cases() if case["name"] == "string-bin")
    install(tmp_path, case)
    monkeypatch.setenv("UH_NODE_EXECUTABLE", str(tmp_path / "absent-node"))
    assert runner.main(["prettier"], root=tmp_path) == 1


def test_forwarded_arguments_are_never_shell_commands(tmp_path: Path) -> None:
    case = next(case for case in cases() if case["name"] == "string-bin")
    script = install(tmp_path, case)
    arguments = ["space name", 'quote"value', "$(touch unsafe)", "& echo unsafe", "é漢字"]
    environment = dict(os.environ)
    environment["PYTHONUTF8"] = "1"
    result = subprocess.run(
        [sys.executable, str(script), "prettier", *arguments],
        capture_output=True,
        encoding="utf-8",
        env=environment,
        timeout=30,
        check=False,
    )
    assert result.returncode == 0
    assert cast("dict[str, object]", json.loads(result.stdout))["args"] == arguments
    assert not (tmp_path / "unsafe").exists()


def test_manifest_access_failure_returns_a_diagnostic(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    def denied(_path: Path) -> bool:
        raise PermissionError("Metadata access denied")

    monkeypatch.setattr(Path, "exists", denied)
    assert runner.main(["prettier"], root=tmp_path) == 1
    assert "Failed to read" in capsys.readouterr().err
