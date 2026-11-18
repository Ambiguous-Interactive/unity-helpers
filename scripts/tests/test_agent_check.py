"""Replay frozen task plans and exercise real Git, CLI and process boundaries."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import agent_check
from check_runner import mapping, strings

ROOT = Path(__file__).resolve().parents[2]
CORPUS = mapping(
    cast(
        "object",
        json.loads(
            (ROOT / "scripts/tests/fixtures/agent-check-v1.json").read_text(encoding="utf-8")
        ),
    )
)
RAW_CASES = CORPUS["cases"]
assert isinstance(RAW_CASES, list)
CASES = [mapping(case) for case in cast("list[object]", RAW_CASES)]


@pytest.mark.parametrize("case", CASES)
def test_original_plan(case: dict[str, object]) -> None:
    tier = case["tier"]
    assert isinstance(tier, str)
    assert agent_check.select(strings(case["files"]), tier) == case["expected"]


@pytest.mark.parametrize("file", ["../outside", "/outside", "C:/outside", "a/./b", "", "a\0b"])
def test_invalid_paths(file: str) -> None:
    with pytest.raises(ValueError, match="repository-relative"):
        agent_check.select([file], "pr")


@pytest.mark.parametrize("args", [["--tier"], ["--base"], ["--files"], ["--base", "--plan"]])
def test_missing_cli_values(args: list[str]) -> None:
    with pytest.raises(ValueError, match="Missing"):
        agent_check.main(args)


def test_invalid_tier_and_reuse() -> None:
    with pytest.raises(ValueError, match="Unknown validation tier"):
        agent_check.select([], "bad")
    with pytest.raises(ValueError, match="Unknown argument"):
        agent_check.main(["--reuse", "--plan", "--files", "docs/guide.md"])


def git(root: Path, *args: str) -> str:
    executable = shutil.which("git")
    assert executable is not None
    return subprocess.run(
        [executable, *args], cwd=root, check=True, capture_output=True, encoding="utf-8", timeout=30
    ).stdout


def initialize(root: Path) -> None:
    git(root, "init", "-q")
    git(root, "config", "user.name", "Test")
    git(root, "config", "user.email", "test@example.com")


def test_committed_renamed_deleted_untracked_unicode(tmp_path: Path) -> None:
    initialize(tmp_path)
    (tmp_path / "old.cs").write_text("old", encoding="utf-8")
    git(tmp_path, "add", ".")
    git(tmp_path, "commit", "-qm", "base")
    base = git(tmp_path, "rev-parse", "HEAD").strip()
    git(tmp_path, "mv", "old.cs", "new.cs")
    git(tmp_path, "commit", "-qm", "rename")
    (tmp_path / "new.cs").unlink()
    for name in ("untracked ü.cs", "\ue000.cs", "😀.cs"):
        (tmp_path / name).write_text("new", encoding="utf-8")
    assert agent_check.changed_files(base, tmp_path) == [
        "new.cs",
        "old.cs",
        "untracked ü.cs",
        "😀.cs",
        "\ue000.cs",
    ]
    for invalid in ("", "--all", "nonexistent-revision"):
        with pytest.raises((ValueError, subprocess.CalledProcessError)):
            agent_check.changed_files(invalid, tmp_path)


def test_ignored_live_settings_and_short_circuit(tmp_path: Path) -> None:
    initialize(tmp_path)
    (tmp_path / ".gitignore").write_text("settings.txt\n", encoding="utf-8")
    git(tmp_path, "add", ".")
    git(tmp_path, "commit", "-qm", "base")
    settings = tmp_path / "settings.txt"
    command = [
        sys.executable,
        "-c",
        "from pathlib import Path; raise SystemExit(7 if Path('settings.txt').read_text() == 'bad' else 0)",
    ]
    settings.write_text("good", encoding="utf-8")
    assert agent_check.execute_checks([command], tmp_path) == 0
    settings.write_text("bad", encoding="utf-8")
    assert git(tmp_path, "diff", "HEAD", "--") == ""
    assert (
        agent_check.execute_checks(
            [command, [sys.executable, "-c", "from pathlib import Path; Path('wrong').touch()"]],
            tmp_path,
        )
        == 7
    )
    assert not (tmp_path / "wrong").exists()


def test_cli_and_legacy_foreign_directory(tmp_path: Path) -> None:
    node = shutil.which("node")
    assert node is not None
    args = ["--tier", "pr", "--plan", "--json", "--files", "docs/ü guide.md"]
    direct = subprocess.run(
        [sys.executable, str(ROOT / "scripts/agent_check.py"), *args],
        cwd=tmp_path,
        check=True,
        capture_output=True,
        encoding="utf-8",
        timeout=30,
    )
    legacy = subprocess.run(
        [node, str(ROOT / "scripts/agent-check.js"), *args],
        cwd=tmp_path,
        check=True,
        capture_output=True,
        encoding="utf-8",
        timeout=30,
    )
    assert json.loads(direct.stdout) == json.loads(legacy.stdout)
    assert json.loads(direct.stdout)["unity"] == "none"


def test_npm_uses_callers_entrypoint(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    node = shutil.which("node")
    assert node is not None
    script = tmp_path / "npm entry.js"
    script.write_text(
        "process.exit(process.argv[2] === 'literal argument' ? 0 : 9);", encoding="utf-8"
    )
    monkeypatch.setenv("npm_execpath", str(script))
    assert agent_check.execute_checks([["npm", "literal argument"]], tmp_path) == 0


@pytest.mark.parametrize("stdout", ["fragment", "line\n"])
def test_exported_main_preserves_child_output_and_status(tmp_path: Path, stdout: str) -> None:
    node = shutil.which("node")
    assert node is not None
    child = tmp_path / "npm-child.js"
    child.write_text(
        "process.stdout.write("
        + json.dumps(stdout)
        + ");process.stderr.write('child diagnostic');process.exit(7);",
        encoding="utf-8",
    )
    result = subprocess.run(
        [
            node,
            "-e",
            "const {main}=require(process.argv[1]);process.exitCode=main(['--tier','iterate','--files','docs/guide.md']);",
            str(ROOT / "scripts/agent-check.js"),
        ],
        cwd=tmp_path,
        env={**os.environ, "npm_execpath": str(child)},
        capture_output=True,
        check=False,
        timeout=30,
    )
    assert result.returncode == 7, result.stderr
    assert stdout.encode() in result.stdout
    assert b"child diagnostic" in result.stderr


def test_legacy_test_launcher_reaches_selected_control() -> None:
    node = shutil.which("node")
    assert node is not None
    result = subprocess.run(
        [
            node,
            str(ROOT / "scripts/tests/test-agent-check.js"),
            "-k",
            "test_invalid_tier_and_reuse",
        ],
        cwd=ROOT,
        check=False,
        capture_output=True,
        encoding="utf-8",
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "1 passed" in result.stdout


@pytest.mark.parametrize("commands", [[[]], [[""]], [[" "]]])
def test_invalid_commands_fail_before_execution(commands: list[list[str]], tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="nonempty command"):
        agent_check.execute_checks(commands, tmp_path)


def test_registry_failure_is_visible(
    monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    import check_registries

    def fail(_base: str) -> list[str]:
        raise ValueError("Affected inventory failed: missing revision")

    monkeypatch.setattr(agent_check, "changed_files", fail)
    assert check_registries.main(["lint", "--base", "invalid", "--list"]) == 1
    assert "Affected inventory failed" in capsys.readouterr().err


def test_native_cli_shallow_clone(tmp_path: Path) -> None:
    initialize(tmp_path)
    (tmp_path / ".gitignore").write_text(".venv/\n__pycache__/\n", encoding="utf-8")
    sources = [
        "scripts/agent-check.js",
        "scripts/agent_check.py",
        "scripts/check_registries.py",
        "scripts/check_runner.py",
        "scripts/cli_encoding.py",
        "scripts/unity/resolve-test-matrix.js",
        "scripts/unity/resolve_test_matrix.py",
        "scripts/python~/pyproject.toml",
        "scripts/python~/uv.lock",
        ".github/unity-versions.json",
    ]
    for source in sources:
        destination = tmp_path / source
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / source, destination)
    git(tmp_path, "add", ".")
    git(tmp_path, "commit", "-qm", "base")
    base = git(tmp_path, "rev-parse", "HEAD").strip()
    (tmp_path / "Runtime").mkdir()
    (tmp_path / "Runtime/Calculation.cs").write_text("class Calculation {}", encoding="utf-8")
    git(tmp_path, "add", ".")
    git(tmp_path, "commit", "-qm", "task")
    checkout = tmp_path / "checkout"
    git(tmp_path, "clone", "--quiet", "--no-local", "--depth", "2", str(tmp_path), str(checkout))
    assert (checkout / ".git/shallow").is_file()
    output = tmp_path / "outputs"
    environment = {
        **os.environ,
        "GITHUB_EVENT_NAME": "pull_request",
        "AGENT_CHECK_BASE": base,
        "GITHUB_OUTPUT": str(output),
        "GITHUB_STEP_SUMMARY": "",
    }
    node = shutil.which("node")
    assert node is not None
    command = [node, str(checkout / "scripts/unity/resolve-test-matrix.js")]
    result = subprocess.run(
        command, env=environment, check=False, capture_output=True, encoding="utf-8", timeout=60
    )
    assert result.returncode == 0, result.stderr
    published = dict(line.split("=", 1) for line in output.read_text(encoding="utf-8").splitlines())
    assert json.loads(published["test-modes"]) == ["editmode", "playmode"]
    canonical = json.loads((ROOT / ".github/unity-versions.json").read_text(encoding="utf-8"))[
        "all"
    ]
    assert json.loads(published["unity-versions"]) == [canonical[-1]]
    assert json.loads(published["unity-exclusions"]) == [
        {"unity-version": version} for version in canonical[:-1]
    ]
    environment["AGENT_CHECK_BASE"] = ""
    result = subprocess.run(
        command, env=environment, check=False, capture_output=True, encoding="utf-8", timeout=60
    )
    assert result.returncode != 0
    assert "immutable PR base" in result.stderr
