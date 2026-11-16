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
from typecheck_clean import npm_command

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
        target.write_text(content, encoding="utf-8", newline="\n")


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
    helper = scripts / "github-token.sh"
    mode_before = subprocess.run(
        [bash, "-c", 'test -x "$1"', "fixture", helper.as_posix()],
        check=False,
        capture_output=True,
    ).returncode
    permissions = subprocess.run(
        [
            bash,
            "-c",
            'chmod +x -- "$@"',
            "fixture",
            *(path.as_posix() for path in scripts.iterdir()),
        ],
        check=False,
        capture_output=True,
    )
    assert permissions.returncode == 0, permissions.stdout + permissions.stderr
    executable = subprocess.run(
        [bash, "-c", 'test -x "$1"', "fixture", helper.as_posix()],
        check=False,
        capture_output=True,
    )
    assert executable.returncode == 0, executable.stdout + executable.stderr
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
        env={**os.environ, "MSYS2_ARG_CONV_EXCL": "!/"},
        check=False,
        capture_output=True,
        timeout=30,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    expected_helper = subprocess.run(
        [
            bash,
            "-c",
            'printf "!%s/github-token.sh" "$(cd "$1" && pwd)"',
            "fixture",
            scripts.as_posix(),
        ],
        check=True,
        capture_output=True,
        text=True,
        timeout=30,
    ).stdout
    installed = git(root, "config", "--global", "--get-all", "credential.https://github.com.helper")
    assert installed.stdout.strip() == expected_helper
    before = {p.relative_to(root): p.read_bytes() for p in root.rglob("*") if p.is_file()}
    status, diagnostics = push.validate(root)
    if status:
        pytest.fail(
            "\n".join([f"Executable probe before chmod: {mode_before}", *diagnostics]),
            pytrace=False,
        )
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


def test_npm_pre_push_route_reaches_postcondition(tmp_path: Path) -> None:
    usable_bash()
    root = tmp_path / "repo"
    checker_name = "scripts/check-container-git-credentials.sh"
    prepare(
        root,
        {
            "config": dict(push.EXPECTED),
            "files": {
                checker_name: '#!/usr/bin/env bash\nprintf "owned-postcondition-%s\\n" "$1"\nexit 7\n'
            },
        },
    )
    assert (
        root / checker_name
    ).read_bytes() == b'#!/usr/bin/env bash\nprintf "owned-postcondition-%s\\n" "$1"\nexit 7\n'
    git(root, "config", "--local", "credential.helper", "vscode-remote-containers-fixture")
    command = [*npm_command(), "run", "validate:prepush", "--", "--", f"--repo-root={root}"]
    broken = subprocess.run(command, cwd=ROOT, check=False, capture_output=True, timeout=60)
    assert broken.returncode == 1, broken.stdout + broken.stderr
    assert b"cached-token credential helper" in broken.stdout
    assert b"owned-postcondition---quiet" in broken.stdout
    (root / checker_name).write_text(
        "#!/usr/bin/env bash\nexit 0\n", encoding="utf-8", newline="\n"
    )
    assert (root / checker_name).read_bytes() == b"#!/usr/bin/env bash\nexit 0\n"
    healthy = subprocess.run(command, cwd=ROOT, check=False, capture_output=True, timeout=60)
    assert healthy.returncode == 0, healthy.stdout + healthy.stderr
    assert b"Git push config and hook artifact checks passed" in healthy.stdout


@pytest.mark.parametrize("exit_code", [1, 2, 3, 128])
def test_helper_read_distinguishes_missing_key_from_git_failure(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, exit_code: int
) -> None:
    root = tmp_path / "repo"
    prepare(
        root,
        {
            "config": dict(push.EXPECTED),
            "files": {"scripts/check-container-git-credentials.sh": ""},
        },
    )
    original = push.run_git

    def command(
        executable: str, directory: Path, *arguments: str
    ) -> subprocess.CompletedProcess[str]:
        if arguments == ("config", "--get-all", "credential.helper"):
            return subprocess.CompletedProcess(arguments, exit_code, "", "fixture read failure")
        return original(executable, directory, *arguments)

    monkeypatch.setattr(push, "run_git", command)
    status, lines = push.validate(root)
    assert status == int(exit_code != 1)
    if exit_code != 1:
        assert any(f"credential.helper failed with exit {exit_code}" in line for line in lines)
        assert not any("checks passed" in line for line in lines)


@pytest.mark.parametrize("entrypoint", ["api", "python", "powershell"])
def test_review_explicit_root_ignores_other_repository_environment(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, entrypoint: str
) -> None:
    first, second = tmp_path / "first", tmp_path / "second"
    prepare(first, {"config": dict(push.EXPECTED), "files": {}})
    prepare(second, {"config": {**dict(push.EXPECTED), "push.default": "matching"}, "files": {}})
    before = [(root / ".git/config").read_bytes() for root in (first, second)]
    monkeypatch.setenv("GIT_DIR", str(first / ".git"))
    if entrypoint == "api":
        status, lines = push.validate(second)
        output = "\n".join(lines)
    else:
        if entrypoint == "python":
            arguments = [
                sys.executable,
                str(ROOT / "scripts/push_validation.py"),
                "--repo-root",
                str(second),
            ]
        else:
            executable = shutil.which("pwsh")
            assert executable is not None
            arguments = [
                executable,
                "-NoProfile",
                "-File",
                str(ROOT / "scripts/validate-git-push-config.ps1"),
                "-RepoRoot",
                str(second),
            ]
        result = subprocess.run(arguments, capture_output=True, text=True, timeout=30)
        status, output = result.returncode, result.stdout
    assert status == 1, output
    assert "push.default is 'matching'" in output
    assert [(root / ".git/config").read_bytes() for root in (first, second)] == before


def test_review_checker_uses_sanitized_environment_and_retains_config(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    usable_bash()
    first, second = tmp_path / "first", tmp_path / "second"
    configuration = {**dict(push.EXPECTED), "credential.helper": "!git-credential-helper"}
    for root in (first, second):
        prepare(root, {"config": configuration, "files": {}})
    scripts = second / "scripts"
    scripts.mkdir()
    (scripts / "check-container-git-credentials.sh").write_text(
        "#!/usr/bin/env bash\n"
        'test -z "$GIT_DIR" || exit 73\n'
        'test "$GIT_CONFIG_COUNT" = 1 || exit 74\n'
        'test "$GIT_CONFIG_KEY_0" = fixture.marker || exit 75\n'
        'test "$GIT_CONFIG_VALUE_0" = retained || exit 76\n'
        "printf checked > checker-ran\n",
        encoding="utf-8",
        newline="\n",
    )
    monkeypatch.setenv("GIT_DIR", str(first / ".git"))
    monkeypatch.setenv("GIT_CONFIG_COUNT", "1")
    monkeypatch.setenv("GIT_CONFIG_KEY_0", "fixture.marker")
    monkeypatch.setenv("GIT_CONFIG_VALUE_0", "retained")
    status, lines = push.validate(second)
    assert status == 0, lines
    assert (second / "checker-ran").read_text() == "checked"
