"""Compare read-only push validation with captured PowerShell decisions."""

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
import push_validation as push

ROOT = Path(__file__).resolve().parents[2]
FIXTURE = cast(
    "dict[str, object]",
    json.loads((Path(__file__).parent / "fixtures/push-validation-v1.json").read_bytes()),
)
CASES = cast("list[dict[str, object]]", FIXTURE["cases"])


@pytest.fixture(autouse=True)
def isolate_git(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    for key in list(os.environ):
        if key.startswith("GIT_"):
            monkeypatch.delenv(key, raising=False)
    for scope in ("GLOBAL", "SYSTEM"):
        target = tmp_path / scope
        target.write_text("", encoding="utf-8")
        monkeypatch.setenv("GIT_CONFIG_" + scope, str(target))


def git(root: Path, *arguments: str) -> subprocess.CompletedProcess[str]:
    executable = shutil.which("git")
    assert executable is not None
    return subprocess.run(
        [executable, "-C", str(root), *arguments], check=True, capture_output=True, text=True
    )


def prepare(root: Path, case: dict[str, object]) -> None:
    root.mkdir()
    git(root, "init", "--quiet")
    for key, value in cast("dict[str, str]", case["config"]).items():
        git(root, "config", "--local", key, value)
    for name, content in cast("dict[str, str]", case["files"]).items():
        target = root / name
        target.parent.mkdir(exist_ok=True)
        target.write_text(content, encoding="utf-8")


def normalized(lines: list[str], root: Path) -> list[str]:
    return sorted(
        line.strip().replace(str(root), "<ROOT>").replace("\\", "/")
        for line in lines
        if line.strip()
    )


@pytest.mark.parametrize("case", CASES, ids=lambda case: str(case["name"]))
def test_original_decisions(tmp_path: Path, case: dict[str, object]) -> None:
    root = tmp_path / "repo"
    prepare(root, case)
    before = {p.relative_to(root): p.read_bytes() for p in root.rglob("*") if p.is_file()}
    status, lines = push.validate(root)
    expected = (
        cast("dict[str, object]", case["inferred_case_insensitive_filesystem"])
        if case["name"] == "upper-extension" and (root / "pre-commit.txt").is_file()
        else case
    )
    assert status == expected["status"]
    assert normalized(lines, root) == expected["stdout_lines"]
    assert expected["stderr_lines"] == []
    assert {p.relative_to(root): p.read_bytes() for p in root.rglob("*") if p.is_file()} == before


def test_original_receipt_has_subjects() -> None:
    assert len(CASES) == 39
    assert any(case["name"] == "clean" and case["status"] == 0 for case in CASES)
    assert sum(case["status"] != 0 for case in CASES) > 20


def test_missing_git_reports_failure(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    def unavailable(_name: str) -> None:
        return None

    monkeypatch.setattr(shutil, "which", unavailable)
    status, lines = push.validate(tmp_path)
    assert status == 1
    assert any("git is required" in line for line in lines)


def test_unavailable_root_reports_failure(tmp_path: Path) -> None:
    status, lines = push.validate(tmp_path / "missing")
    assert status == 1
    assert lines


def test_unknown_arguments_fail() -> None:
    result = subprocess.run(
        [sys.executable, str(ROOT / "scripts/push_validation.py"), "--unknown"],
        check=False,
        capture_output=True,
    )
    assert result.returncode != 0
    assert b"unrecognized arguments" in result.stderr


def test_original_literal_filesystem_case_semantics(tmp_path: Path) -> None:
    (tmp_path / "pre-commit.TXT").write_text("artifact", encoding="utf-8")
    probe = tmp_path / "probe.ps1"
    probe.write_text(
        "param([string]$Candidate) "
        "if (Test-Path -LiteralPath $Candidate -PathType Leaf) { exit 0 }; exit 1",
        encoding="utf-8",
    )
    executable = shutil.which("pwsh")
    assert executable is not None
    result = subprocess.run(
        [executable, "-NoProfile", "-File", str(probe), str(tmp_path / "pre-commit.txt")],
        check=False,
        capture_output=True,
        timeout=30,
    )
    assert (result.returncode == 0) == (tmp_path / "pre-commit.txt").is_file()


def usable_bash() -> str:
    executable = shutil.which("bash")
    if executable is None:
        pytest.skip(
            "Bash is unavailable; this integration covers the container-only postcondition."
        )
    result = subprocess.run(
        [executable, "-c", 'test -f "$1"', "--", str(ROOT / "scripts/github-token.sh")],
        check=False,
        capture_output=True,
        timeout=30,
    )
    if result.returncode:
        pytest.skip(
            "The available Bash cannot access host files; the container integration is inapplicable."
        )
    return executable


def test_real_container_credential_postcondition(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    bash = usable_bash()
    root = tmp_path / "repo"
    prepare(root, {"config": dict(push.EXPECTED), "files": {}})
    scripts = root / "scripts"
    scripts.mkdir()
    for name in (
        "check-container-git-credentials.sh",
        "github-token.sh",
        "normalize-container-git-config.sh",
    ):
        target = scripts / name
        shutil.copyfile(ROOT / "scripts" / name, target)
        target.chmod(0o755)
    git(
        root,
        "config",
        "--system",
        "--add",
        "credential.helper",
        "!f() { node /tmp/vscode-remote-containers-test.js git-credential-helper $*; }; f",
    )
    status, lines = push.validate(root)
    assert status == 1
    assert any("cached-token credential helper" in line for line in lines)
    assert any("normalize-container-git-config.sh" in line for line in lines)
    result = subprocess.run(
        [bash, str(scripts / "normalize-container-git-config.sh")],
        cwd=root,
        check=False,
        capture_output=True,
        timeout=30,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    before = {p.relative_to(root): p.read_bytes() for p in root.rglob("*") if p.is_file()}
    assert push.validate(root)[0] == 0
    assert {p.relative_to(root): p.read_bytes() for p in root.rglob("*") if p.is_file()} == before
    Path(os.environ["GIT_CONFIG_GLOBAL"]).write_text("", encoding="utf-8")
    Path(os.environ["GIT_CONFIG_SYSTEM"]).write_text("", encoding="utf-8")
    git(root, "config", "--system", "--add", "credential.helper", "manager")
    assert push.validate(root)[0] == 0
    Path(os.environ["GIT_CONFIG_SYSTEM"]).write_text("", encoding="utf-8")
    git(
        root,
        "config",
        "--local",
        "--add",
        "credential.helper",
        "!f() { node /tmp/vscode-remote-containers-test.js git-credential-helper $*; }; f",
    )
    monkeypatch.chdir(tmp_path)
    assert push.validate(root)[0] == 1


def test_no_bash_reports_only_an_applicable_container_failure(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    root = tmp_path / "repo"
    prepare(
        root,
        {
            "config": dict(push.EXPECTED),
            "files": {"scripts/check-container-git-credentials.sh": "exit 0"},
        },
    )
    original = shutil.which

    def which(name: str) -> str | None:
        return None if name == "bash" else original(name)

    monkeypatch.setattr(shutil, "which", which)
    assert push.validate(root)[0] == 0
    git(root, "config", "--local", "credential.helper", "vscode-remote-containers-fixture")
    status, lines = push.validate(root)
    assert status == 1
    assert any("bash is unavailable" in line for line in lines)


@pytest.mark.parametrize(
    "launcher",
    ["scripts/validate-git-push-config.ps1", "scripts/tests/test-validate-git-push-config.ps1"],
)
def test_legacy_launchers(tmp_path: Path, launcher: str) -> None:
    executable = shutil.which("pwsh")
    assert executable is not None
    command = [executable, "-NoProfile", "-File", str(ROOT / launcher)]
    if "scripts/tests/" in launcher:
        command.extend(["-TestFilter", "test_original_receipt_has_subjects"])
    else:
        root = tmp_path / "repo"
        prepare(root, {"config": dict(push.EXPECTED), "files": {}})
        command.extend(["-RepoRoot", str(root)])
    result = subprocess.run(command, cwd=tmp_path, check=False, capture_output=True, timeout=60)
    assert result.returncode == 0, result.stdout + result.stderr
    assert (
        b"1 passed" in result.stdout
        if "scripts/tests/" in launcher
        else b"checks passed" in result.stdout
    )


def test_check_ignore_error_remains_manual_review(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    root = tmp_path / "repo"
    prepare(
        root,
        {
            "config": dict(push.EXPECTED),
            "files": {".githooks/pre-commit": "hook", "pre-commit.txt": "artifact"},
        },
    )
    original = push.run_git

    def command(
        executable: str, directory: Path, *arguments: str
    ) -> subprocess.CompletedProcess[str]:
        if arguments[0] == "check-ignore":
            return subprocess.CompletedProcess(arguments, 128, "", "fixture error")
        return original(executable, directory, *arguments)

    monkeypatch.setattr(push, "run_git", command)
    status, lines = push.validate(root)
    assert status == 1
    assert any("NOT gitignored" in line for line in lines)
    assert any("git check-ignore exit 128" in line for line in lines)
    assert (root / "pre-commit.txt").read_text() == "artifact"


def test_git_timeout_cannot_pass(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    def fail(_executable: str, _root: Path, *_arguments: str) -> subprocess.CompletedProcess[str]:
        raise subprocess.TimeoutExpired("git config", 30)

    monkeypatch.setattr(push, "run_git", fail)
    status, lines = push.validate(tmp_path)
    assert status == 1
    assert any("Cannot complete read-only Git push checks" in line for line in lines)


def test_cli_artifact_diagnostics_are_deterministic(tmp_path: Path) -> None:
    root = tmp_path / "repo"
    prepare(
        root,
        {
            "config": dict(push.EXPECTED),
            "files": {
                ".githooks/pre-commit.ps1": "hook",
                ".githooks/PRE-COMMIT.sh": "hook",
                "pre-commit.txt": "artifact",
                "PRE-COMMIT.txt": "artifact",
            },
        },
    )
    results = [
        subprocess.run(
            [sys.executable, str(ROOT / "scripts/push_validation.py"), "--repo-root", str(root)],
            env={**os.environ, "PYTHONHASHSEED": str(seed)},
            check=False,
            capture_output=True,
        )
        for seed in (1, 2, 3)
    ]
    assert all(result.returncode == 1 for result in results)
    assert len({result.stdout for result in results}) == 1
