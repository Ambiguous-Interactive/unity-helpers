"""Exercise native version probes and the developer tool inventory."""

from __future__ import annotations

import os
import shutil
import signal
import socket
import subprocess
import sys
import tracemalloc
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import verify_devcontainer_tools as verifier


def test_main_restores_the_native_path_before_checking_tools(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("PATH", "locked-uv-environment")
    monkeypatch.setenv("UH_VERIFY_NATIVE_PATH", "native-command-path")
    monkeypatch.setenv("REMOTE_CONTAINERS", "1")
    monkeypatch.setattr(verifier, "configure_output", lambda: None)

    def verify(*, inside_container: bool) -> int:
        assert inside_container
        assert os.environ["PATH"] == "native-command-path"
        assert "UH_VERIFY_NATIVE_PATH" not in os.environ
        return 0

    monkeypatch.setattr(verifier, "verify", verify)
    assert verifier.main() == 0


@pytest.mark.skipif(os.name != "posix", reason="The legacy entry point is a POSIX launcher")
def test_bash_launcher_passes_the_native_path_to_locked_uv(tmp_path: Path) -> None:
    bash = shutil.which("bash")
    assert bash is not None
    uv = tmp_path / "uv"
    uv.write_text(
        '#!/usr/bin/env bash\nprintf "%s\\n" "$UH_VERIFY_NATIVE_PATH" "$@"\n', encoding="utf-8"
    )
    uv.chmod(0o755)
    native_path = os.pathsep.join((str(tmp_path), os.environ.get("PATH", os.defpath)))
    repository = Path(__file__).resolve().parents[2]
    scripts = repository / "scripts"
    result = subprocess.run(
        [bash, "scripts/verify-devcontainer-tools.sh"],
        cwd=repository,
        env={**os.environ, "PATH": native_path, "CDPATH": "."},
        capture_output=True,
        check=False,
        timeout=15,
    )
    assert result.returncode == 0
    assert result.stderr == b""
    assert result.stdout.splitlines() == [
        os.fsencode(native_path),
        b"run",
        b"--quiet",
        b"--project",
        os.fsencode(scripts / "python~"),
        b"--locked",
        b"--no-dev",
        b"python",
        os.fsencode(scripts / "verify_devcontainer_tools.py"),
    ]


def test_probe_keeps_first_line_and_exit_status() -> None:
    result = verifier.probe(
        sys.executable,
        "-c",
        "import sys; print('first', file=sys.stderr); print('second'); sys.exit(7)",
    )
    assert result == verifier.Version(7, "first")


@pytest.mark.parametrize(
    ("output", "expected"),
    [(b"first\r\nsecond", "first"), (b"first\r", "first\r"), (b"", "")],
)
def test_probe_normalizes_only_line_terminators(output: bytes, expected: str) -> None:
    result = verifier.probe(
        sys.executable, "-c", "import os,sys; os.write(1,bytes.fromhex(sys.argv[1]))", output.hex()
    )
    assert result == verifier.Version(0, expected)


def test_probe_retains_a_bounded_first_line_and_drains_the_rest() -> None:
    command = (
        "import sys\n"
        "output = sys.stdout.buffer\n"
        "for _ in range(256):\n    output.write(b'A' * 65536)\n"
        "output.write(b'\\n')\n"
        "for _ in range(256):\n    output.write(b'B' * 65536)\n"
        "sys.exit(7)\n"
    )
    tracemalloc.start()
    try:
        result = verifier.probe(sys.executable, "-c", command)
        _, peak = tracemalloc.get_traced_memory()
    finally:
        tracemalloc.stop()

    assert peak < 4 * 1024 * 1024
    assert result == verifier.Version(7, "A" * 65536)


def test_probe_bounds_a_silent_command(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(verifier, "TIMEOUT_SECONDS", 0.05)
    result = verifier.probe(sys.executable, "-c", "from time import sleep; sleep(60)")
    assert result == verifier.Version(124, "", timed_out=True)


def _descendant_arguments(listener: socket.socket) -> tuple[str, ...]:
    parent = "import subprocess, sys; sys.exit(subprocess.call([sys.executable, *sys.argv[1:]]))"
    child = (
        "import socket, sys\n"
        "with socket.create_connection(('127.0.0.1', int(sys.argv[1])), timeout=10) as connection:\n"
        "    connection.sendall(b'R')\n"
        "    connection.settimeout(None)\n"
        "    connection.recv(1)\n"
    )
    return ("-c", parent, "-c", child, str(listener.getsockname()[1]))


@pytest.mark.skipif(os.name != "posix", reason="POSIX process groups are verified on Unix")
def test_timed_out_probe_terminates_its_child(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(verifier, "TIMEOUT_SECONDS", 2)
    with socket.create_server(("127.0.0.1", 0)) as listener:
        listener.settimeout(5)
        result = verifier.probe(sys.executable, *_descendant_arguments(listener))
        connection, _ = listener.accept()
        with connection:
            connection.settimeout(5)
            assert connection.recv(1) == b"R"
            assert result == verifier.Version(124, "", timed_out=True)
            assert connection.recv(1) == b""


@pytest.mark.skipif(os.name != "posix", reason="POSIX process groups are verified on Unix")
def test_interrupted_probe_terminates_its_child() -> None:
    harness = (
        "import sys\nsys.path.insert(0, sys.argv[1])\n"
        "from verify_devcontainer_tools import probe\n"
        "try:\n    probe(sys.executable, *sys.argv[2:])\n"
        "except KeyboardInterrupt:\n    sys.exit(7)\n"
    )
    with (
        socket.create_server(("127.0.0.1", 0)) as listener,
        subprocess.Popen(
            [
                sys.executable,
                "-c",
                harness,
                str(Path(verifier.__file__).parent),
                *_descendant_arguments(listener),
            ]
        ) as process,
    ):
        try:
            listener.settimeout(5)
            connection, _ = listener.accept()
            with connection:
                connection.settimeout(5)
                assert connection.recv(1) == b"R"
                process.send_signal(signal.SIGINT)
                assert connection.recv(1) == b""
                assert process.wait(timeout=5) == 7
        finally:
            if process.poll() is None:
                process.kill()


def test_missing_system_tools_only_warn_outside_the_container(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    def probe(command: str, *args: str) -> verifier.Version | None:
        assert (command, *args) == ("yamllint", "--version")
        return None

    monkeypatch.setattr(verifier, "probe", probe)
    tool = verifier.Tool("yamllint", "yamllint", container_only=True)
    outside, outside_status = verifier.check(tool, inside_container=False)
    inside, inside_status = verifier.check(tool, inside_container=True)
    assert "not installed (optional)" in outside
    assert outside_status is verifier.Outcome.OPTIONAL_MISSING
    assert "NOT FOUND (REQUIRED)" in inside
    assert inside_status is verifier.Outcome.REQUIRED_FAILURE


@pytest.mark.parametrize(
    ("reply", "expected_suffix", "expected_status"),
    [
        (verifier.Version(0, "3.10.1"), "3.10.1 (via npx)", verifier.Outcome.PASSED),
        (
            verifier.Version(124, ""),
            "TIMED OUT - npx prettier --version",
            verifier.Outcome.REQUIRED_FAILURE,
        ),
        (verifier.Version(1, "missing script"), "npm install", verifier.Outcome.REQUIRED_FAILURE),
        (None, "npm install", verifier.Outcome.REQUIRED_FAILURE),
    ],
)
def test_package_probe_uses_local_resolution(
    monkeypatch: pytest.MonkeyPatch,
    reply: verifier.Version | None,
    expected_suffix: str,
    expected_status: verifier.Outcome,
) -> None:
    def probe(command: str, *args: str) -> verifier.Version | None:
        assert (command, *args) == ("npx", "--no-install", "prettier", "--version")
        return reply

    monkeypatch.setattr(verifier, "probe", probe)
    message, status = verifier.check(
        verifier.Tool("prettier", "prettier", "npm"), inside_container=True
    )
    assert expected_suffix in message
    assert status is expected_status


@pytest.mark.parametrize("pinned_available", [False, True])
def test_dotnet_reuses_its_version_and_falls_back_to_global(
    monkeypatch: pytest.MonkeyPatch, pinned_available: bool
) -> None:
    calls: list[tuple[str, ...]] = []

    def probe(command: str, *args: str) -> verifier.Version:
        calls.append((command, *args))
        if command == "dotnet" and not pinned_available:
            return verifier.Version(1, "old SDK")
        return verifier.Version(0, "1.4.0")

    monkeypatch.setattr(verifier, "probe", probe)
    message, status = verifier.check(
        verifier.Tool("csharpier", "csharpier", "dotnet"), inside_container=True
    )
    assert calls == [
        ("dotnet", "tool", "run", "csharpier", "--version"),
        *([] if pinned_available else [("csharpier", "--version")]),
    ]
    assert ("1.4.0 (via dotnet tool)" if pinned_available else "1.4.0 (global)") in message
    assert status is verifier.Outcome.PASSED


def test_inventory_reports_optional_warning_and_keeps_required_failure_order(
    monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    def probe(command: str, *args: str) -> verifier.Version | None:
        assert args
        return None if command == "actionlint" else verifier.Version(0, "fixture version")

    monkeypatch.setattr(verifier, "probe", probe)
    assert verifier.verify(inside_container=False) == 0
    host = capsys.readouterr().out
    assert "Host Machine" in host
    assert "1 optional tool(s) not installed." in host
    assert host.index("actionlint:") < host.index("shellcheck:") < host.index("markdownlint:")
    assert verifier.verify(inside_container=True) == 1
    container = capsys.readouterr().out
    assert "Dev Container" in container
    assert "actionlint:" in container
    assert "shellcheck:" not in container
