"""Exercise native version probes and the developer tool inventory."""

from __future__ import annotations

import os
import sys
import time
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import verify_devcontainer_tools as verifier


def test_probe_keeps_first_line_and_exit_status() -> None:
    result = verifier.probe(
        sys.executable,
        "-c",
        "import sys; print('first', file=sys.stderr); print('second'); sys.exit(7)",
    )
    assert result == verifier.Version(7, "first")


def test_probe_bounds_a_silent_command(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(verifier, "TIMEOUT_SECONDS", 0.05)
    result = verifier.probe(sys.executable, "-c", "from time import sleep; sleep(60)")
    assert result == verifier.Version(124, "", timed_out=True)


@pytest.mark.skipif(os.name != "posix", reason="POSIX process groups are verified on Unix")
def test_timed_out_probe_terminates_its_child(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr(verifier, "TIMEOUT_SECONDS", 0.5)
    started = tmp_path / "started"
    late_write = tmp_path / "late-write"
    child = "from pathlib import Path; from time import sleep; import sys; sleep(0.8); Path(sys.argv[1]).touch()"
    parent = (
        "from pathlib import Path; from subprocess import Popen; from time import sleep; import sys; "
        "Popen([sys.executable, '-c', sys.argv[1], sys.argv[2]]); Path(sys.argv[3]).touch(); sleep(60)"
    )
    result = verifier.probe(sys.executable, "-c", parent, child, str(late_write), str(started))
    assert result == verifier.Version(124, "", timed_out=True)
    assert started.is_file()
    time.sleep(1)
    assert not late_write.exists()


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
