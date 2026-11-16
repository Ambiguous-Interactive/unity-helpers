"""Replay original Bash and dot-sourced PowerShell push-default contracts."""

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
import configure_git_defaults as defaults

ROOT = Path(__file__).resolve().parents[2]
FIXTURE = cast(
    "dict[str, object]",
    json.loads((Path(__file__).parent / "fixtures/push-defaults-v1.json").read_bytes()),
)
CASES = cast("list[dict[str, object]]", FIXTURE["cases"])


@pytest.fixture(autouse=True)
def isolate_git(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    for key in list(os.environ):
        if key.startswith("GIT_"):
            monkeypatch.delenv(key, raising=False)
    for scope in ("GLOBAL", "SYSTEM"):
        path = tmp_path / scope
        path.write_text("", encoding="utf-8")
        monkeypatch.setenv("GIT_CONFIG_" + scope, str(path))


def normalized(value: str, root: Path) -> str:
    return value.replace(str(root), "<ROOT>").replace("\r\n", "\n").strip()


def git(root: Path, *arguments: str) -> subprocess.CompletedProcess[bytes]:
    executable = shutil.which("git")
    assert executable is not None
    return subprocess.run(
        [executable, "-C", str(root), *arguments], check=False, capture_output=True
    )


@pytest.mark.parametrize("case", CASES, ids=lambda case: str(case["name"]))
def test_original_profiles(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, case: dict[str, object]
) -> None:
    root = tmp_path / "repo"
    shape = case["shape"]
    if shape == "file":
        root.write_text("fixture", encoding="utf-8")
    elif shape != "missing":
        root.mkdir()
    if shape in ("fresh", "existing"):
        assert git(root, "init", "--quiet").returncode == 0
        assert git(root, "config", "--local", "fixture.keep", "authored").returncode == 0
        if shape == "existing":
            git(root, "config", "--local", "push.autoSetupRemote", "false")
            git(root, "config", "--local", "push.default", "matching")
    calls: list[str] = []
    scenario = case["scenario"]

    def stub(arguments: list[str]) -> subprocess.CompletedProcess[bytes]:
        args = arguments[1:]
        calls.append("\0".join(normalized(value, root) for value in args) + "\0")
        code = 0
        output = b""
        if args == ["--version"]:
            if scenario == "failed-version":
                code = 1
            else:
                version = "2.36.0" if scenario == "old-git" else "2.44.0"
                output = (
                    b"nonsense\n"
                    if scenario == "garbage-version"
                    else f"git version {version}\n".encode()
                )
        else:
            assert args[:2] == ["-C", str(root)]
            args = args[2:]
            if args[0] == "rev-parse":
                code = 1 if scenario == "not-repo" else 0
                output = b".git\n" if code == 0 else b""
            else:
                assert args[:2] == ["config", "--local"]
                read = args[2] == "--get"
                key = args[3] if read else args[2]
                which = "first" if key == "push.autoSetupRemote" else "second"
                if read:
                    code = 1 if scenario == "missing-" + which else 0
                    value = "true" if which == "first" else "simple"
                    if scenario == "uppercase":
                        value = value.upper()
                    if scenario == "spaced":
                        value = "  " + value + " "
                    newline = "\r\n" if scenario in ("crlf", "spaced") else "\n"
                    output = (value + newline).encode() if code == 0 else b""
                elif scenario == "fail-" + which:
                    code = 2
        return subprocess.CompletedProcess(arguments, code, output, b"")

    if scenario is not None:
        monkeypatch.setattr(defaults, "run_git", stub)
    result = defaults.configure(str(root), str(case["profile"]))
    assert result.exit_code == case["status"]
    rendered = "\n".join(defaults.render(result.messages, str(case["profile"])))
    assert normalized(rendered, root) == case["stdout"]
    if case["result"] is not None:
        legacy = result.legacy_result()
        legacy["Errors"] = [normalized(value, root) for value in result.errors]
        assert legacy == case["result"]
        assert (79 if result.last_git_exit_code is None else result.last_git_exit_code) == case[
            "last_git_exit_code"
        ]
    assert calls == case["git_calls"]
    for scope in ("GLOBAL", "SYSTEM"):
        assert Path(os.environ["GIT_CONFIG_" + scope]).read_bytes() == b""
    if shape in ("fresh", "existing"):
        for key, expected in cast("dict[str, str]", case["after"]).items():
            actual = git(root, "config", "--local", "--get", key)
            assert actual.stdout.decode().strip() == expected


def test_original_receipt_has_both_profiles() -> None:
    assert len(CASES) == 34
    assert {str(case["profile"]) for case in CASES} == {"bash", "powershell"}
    assert sum(case["status"] != 0 for case in CASES) > 10


@pytest.mark.parametrize(
    "case",
    [case for case in CASES if case["profile"] == "powershell"],
    ids=lambda case: str(case["name"]),
)
def test_native_result_and_exit_state(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, case: dict[str, object]
) -> None:
    root = tmp_path / "repo"
    shape = case["shape"]
    if shape == "file":
        root.write_text("fixture", encoding="utf-8")
    elif shape != "missing":
        root.mkdir()
    if shape in ("fresh", "existing"):
        assert git(root, "init", "--quiet").returncode == 0
        git(root, "config", "--local", "fixture.keep", "authored")
        if shape == "existing":
            git(root, "config", "--local", "push.autoSetupRemote", "false")
            git(root, "config", "--local", "push.default", "matching")
    if case["scenario"] is not None:
        if os.name == "nt":
            pytest.skip(
                "The captured executable Git shim is POSIX; real native profiles remain covered."
            )
        directory = tmp_path / "bin"
        directory.mkdir()
        stub = directory / "git"
        stub.write_text(cast("str", FIXTURE["git_stub_script"]), encoding="utf-8")
        stub.chmod(0o755)
        monkeypatch.setenv("PATH", str(directory) + os.pathsep + os.environ["PATH"])
        monkeypatch.setenv("UH_GIT_SCENARIO", str(case["scenario"]))
        monkeypatch.setenv("UH_GIT_LOG", str(tmp_path / "calls"))
    harness = tmp_path / "helper.ps1"
    harness.write_text(
        "param([string]$Helper,[string]$Root)\n"
        "$global:LASTEXITCODE=79\n"
        ". $Helper\n"
        "$r=Set-RepoGitPushDefaults -RepoRoot $Root\n"
        "if (-not ($r -is [hashtable])) { exit 97 }\n"
        "[Console]::Out.WriteLine('RESULT_JSON:' + ($r | ConvertTo-Json -Compress -Depth 6))\n"
        "[Console]::Out.WriteLine('LAST_CODE:' + $LASTEXITCODE)\n"
        "[Console]::Out.WriteLine('AFTER_HELPER')\n"
        "if ($r.Success) { exit 0 }; exit 1\n",
        encoding="utf-8",
    )
    executable = shutil.which("pwsh")
    assert executable is not None
    observed = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(harness),
            str(ROOT / "scripts/git-push-defaults-helpers.ps1"),
            str(root),
        ],
        check=False,
        capture_output=True,
        timeout=60,
    )
    output = observed.stdout.decode()
    assert observed.returncode == case["status"], output + observed.stderr.decode()
    assert "AFTER_HELPER" in output
    lines = output.splitlines()
    metadata = cast(
        "dict[str, object]",
        json.loads(
            next(line for line in lines if line.startswith("RESULT_JSON:")).removeprefix(
                "RESULT_JSON:"
            )
        ),
    )
    metadata["Errors"] = [
        normalized(value, root) for value in cast("list[str]", metadata["Errors"])
    ]
    assert metadata == case["result"]
    last = int(
        next(line for line in lines if line.startswith("LAST_CODE:")).removeprefix("LAST_CODE:")
    )
    assert last == case["last_git_exit_code"]


def test_installer_retains_original_normalized_git_reads(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    if os.name == "nt":
        pytest.skip(
            "The original installer shim is POSIX; typed policy and native PowerShell run separately."
        )
    directory = tmp_path / "bin"
    directory.mkdir()
    stub = directory / "git"
    stub.write_text(cast("str", FIXTURE["install_hooks_git_stub_script"]), encoding="utf-8")
    stub.chmod(0o755)
    monkeypatch.setenv("PATH", str(directory) + os.pathsep + os.environ["PATH"])
    executable = shutil.which("bash")
    assert executable is not None
    result = subprocess.run(
        [executable, str(ROOT / "scripts/install-hooks.sh"), "--check"],
        check=False,
        capture_output=True,
        timeout=30,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert b"Git hooks path: .githooks" in result.stdout
    assert b"push.autoSetupRemote: true" in result.stdout
    assert b"push.default: simple" in result.stdout
    assert b"unset" not in result.stdout


def test_configuration_lock_is_preserved(tmp_path: Path) -> None:
    root = tmp_path / "repo"
    root.mkdir()
    assert git(root, "init", "--quiet").returncode == 0
    lock = root / ".git/config.lock"
    lock.write_text("held by another writer", encoding="utf-8")
    before = (root / ".git/config").read_bytes()
    result = defaults.configure(str(root), "powershell")
    assert not result.success
    assert result.errors == ["Failed to set push.autoSetupRemote"]
    assert lock.read_text() == "held by another writer"
    assert (root / ".git/config").read_bytes() == before


def test_late_write_failure_retains_observed_partial_configuration(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    root = tmp_path / "repo"
    root.mkdir()
    assert git(root, "init", "--quiet").returncode == 0
    assert git(root, "config", "--local", "push.default", "matching").returncode == 0
    original = defaults.run_git

    def command(arguments: list[str]) -> subprocess.CompletedProcess[bytes]:
        if arguments[-2:] == ["push.default", "simple"]:
            return subprocess.CompletedProcess(
                arguments, 2, b"", b"controlled second-write failure"
            )
        return original(arguments)

    monkeypatch.setattr(defaults, "run_git", command)
    result = defaults.configure(str(root), "powershell")
    assert not result.success
    assert result.errors == ["Failed to set push.default"]
    assert git(root, "config", "--local", "--get", "push.autoSetupRemote").stdout.strip() == b"true"
    assert git(root, "config", "--local", "--get", "push.default").stdout.strip() == b"matching"


@pytest.mark.parametrize(
    ("payload", "status"),
    [
        ("not-json", 0),
        (json.dumps({"result": {"Success": True}}), 1),
        (json.dumps({"result": {"Success": False}}), 0),
        (
            json.dumps(
                {
                    "result": {
                        "Success": True,
                        "Errors": [],
                        "Values": {"push.autoSetupRemote": "true", "push.default": "simple"},
                    },
                    "messages": [["unknown", "message"]],
                    "last_git_exit_code": 0,
                }
            ),
            0,
        ),
    ],
)
def test_native_protocol_failure_returns_a_failure_table(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, payload: str, status: int
) -> None:
    harness = tmp_path / "protocol.ps1"
    harness.write_text(
        "param([string]$Helper,[string]$Root)\n"
        "function uv { Write-Output $env:UH_UV_PAYLOAD; $global:LASTEXITCODE=[int]$env:UH_UV_STATUS }\n"
        "$global:LASTEXITCODE=79\n"
        ". $Helper\n"
        "$r=Set-RepoGitPushDefaults -RepoRoot $Root\n"
        "if (-not ($r -is [hashtable]) -or $r.Success -or @($r.Errors).Count -eq 0) { exit 97 }\n"
        "if ($LASTEXITCODE -ne 79) { exit 98 }\n"
        "Write-Output 'AFTER_HELPER'\n",
        encoding="utf-8",
    )
    monkeypatch.setenv("UH_UV_PAYLOAD", payload)
    monkeypatch.setenv("UH_UV_STATUS", str(status))
    executable = shutil.which("pwsh")
    assert executable is not None
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(harness),
            str(ROOT / "scripts/git-push-defaults-helpers.ps1"),
            str(tmp_path),
        ],
        capture_output=True,
        check=False,
        timeout=30,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert b"AFTER_HELPER" in result.stdout


def test_windows_powershell_51_dot_source_contract(tmp_path: Path) -> None:
    if os.name != "nt":
        pytest.skip("Windows PowerShell 5.1 is a Windows-only host contract.")
    executable = Path(os.environ["WINDIR"]) / "System32/WindowsPowerShell/v1.0/powershell.exe"
    assert executable.is_file()
    root = tmp_path / "literal [owner]" / "repo"
    root.mkdir(parents=True)
    assert git(root, "init", "--quiet").returncode == 0
    harness = tmp_path / "host.ps1"
    harness.write_text(
        "param([string]$Helper,[string]$Root)\n"
        ". $Helper\n"
        "$r=Set-RepoGitPushDefaults -RepoRoot $Root\n"
        "if (-not ($r -is [hashtable]) -or -not $r.Success) { exit 97 }\n"
        "if ($r.Values['push.autoSetupRemote'] -ne 'true' -or $r.Values['push.default'] -ne 'simple') { exit 98 }\n"
        "Write-Output 'AFTER_HELPER'\n",
        encoding="utf-8",
    )
    result = subprocess.run(
        [
            str(executable),
            "-NoProfile",
            "-File",
            str(harness),
            str(ROOT / "scripts/git-push-defaults-helpers.ps1"),
            str(root),
        ],
        check=False,
        capture_output=True,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert b"AFTER_HELPER" in result.stdout


@pytest.mark.parametrize(
    "launcher",
    [
        "scripts/configure-git-defaults.ps1",
        "scripts/configure-git-defaults.sh",
        "scripts/tests/test-configure-git-defaults.sh",
    ],
)
def test_legacy_launchers(tmp_path: Path, launcher: str) -> None:
    is_bash = launcher.endswith(".sh")
    if is_bash and os.name == "nt":
        pytest.skip("The Bash compatibility entry point is a POSIX host contract.")
    executable = shutil.which("bash" if is_bash else "pwsh")
    assert executable is not None
    command = [executable]
    if not is_bash:
        command.extend(["-NoProfile", "-File"])
    command.append(str(ROOT / launcher))
    if "scripts/tests/" in launcher:
        command.extend(["-k", "test_original_receipt_has_both_profiles"])
    else:
        root = tmp_path / "repo"
        root.mkdir()
        assert git(root, "init", "--quiet").returncode == 0
        command.extend([str(root)] if is_bash else ["-RepoRoot", str(root)])
    result = subprocess.run(command, check=False, capture_output=True, timeout=60)
    assert result.returncode == 0, result.stdout + result.stderr
    assert (
        b"1 passed" in result.stdout
        if "scripts/tests/" in launcher
        else b"push.default=simple" in result.stdout
    )


def test_missing_git_returns_failure_without_configuration(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    root = tmp_path / "repo"
    root.mkdir()
    assert git(root, "init", "--quiet").returncode == 0
    before = (root / ".git/config").read_bytes()

    def unavailable(_command: str) -> None:
        return None

    monkeypatch.setattr(shutil, "which", unavailable)
    result = defaults.configure(str(root), "powershell")
    assert not result.success
    assert result.errors == ["git is not installed or not on PATH."]
    assert (root / ".git/config").read_bytes() == before


def test_git_timeout_is_visible(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    def timeout(arguments: list[str]) -> subprocess.CompletedProcess[bytes]:
        raise subprocess.TimeoutExpired(arguments, 30)

    monkeypatch.setattr(defaults, "run_git", timeout)
    result = defaults.configure(str(tmp_path), "powershell")
    assert not result.success
    assert any("Could not configure local Git push defaults" in error for error in result.errors)


@pytest.mark.parametrize("missing_uv", [False, True])
def test_native_failure_preserves_prior_exit_state(tmp_path: Path, missing_uv: bool) -> None:
    harness = tmp_path / "exit-state.ps1"
    setup = (
        "$env:PATH=''; $global:LASTEXITCODE=79"
        if missing_uv
        else ("Remove-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue")
    )
    check = (
        "if ($LASTEXITCODE -ne 79) { exit 98 }"
        if missing_uv
        else (
            "if ($null -ne (Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue)) { exit 98 }"
        )
    )
    harness.write_text(
        "param([string]$Helper,[string]$Root)\n"
        ". $Helper\n" + setup + "\n"
        "$r=Set-RepoGitPushDefaults -RepoRoot $Root\n"
        "if (-not ($r -is [hashtable]) -or $r.Success -or @($r.Errors).Count -eq 0) { exit 97 }\n"
        + check
        + "\nWrite-Output 'AFTER_HELPER'\n",
        encoding="utf-8",
    )
    executable = shutil.which("pwsh")
    assert executable is not None
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(harness),
            str(ROOT / "scripts/git-push-defaults-helpers.ps1"),
            str(tmp_path / "missing"),
        ],
        check=False,
        capture_output=True,
        timeout=30,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert b"AFTER_HELPER" in result.stdout
