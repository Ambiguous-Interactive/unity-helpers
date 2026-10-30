"""Check aggregation, process completion, and compatibility protocol controls."""

from __future__ import annotations

import concurrent.futures
import json
import os
import shlex
import signal
import subprocess
import sys
import time
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from check_runner import (
    Check,
    default_jobs,
    main,
    parse_arguments,
    run_check,
    run_checks,
    run_registry,
)


@pytest.mark.parametrize(
    ("command", "expected"), [("true", True), ("exit 3", False), ("exit 0", True)]
)
def test_exit_status_is_required(
    command: str, expected: bool, capsys: pytest.CaptureFixture[str]
) -> None:
    result = run_check(Check("subject", "Subject", command))
    assert result.ok is expected
    output = capsys.readouterr().out
    messages = [json.loads(line) for line in output.splitlines()]
    assert len(messages) == 1
    assert messages[0]["value"].count("::group::") == 1
    assert messages[0]["value"].count("::endgroup::") == 1


@pytest.mark.parametrize(
    ("command", "expected"),
    [
        ("printf '3 finished.\\n'", True),
        ("true", False),
        ("printf '3 finished.\\n'; exit 3", False),
        ("printf 'log: 3 finished.\\n'", False),
    ],
)
def test_completion_requires_marker_and_success(command: str, expected: bool) -> None:
    assert (
        run_check(
            Check(
                "subject",
                "Subject",
                command,
                completion_pattern=r"^[1-9][0-9]* finished\.\r?$",
            )
        ).ok
        is expected
    )


def test_all_results_are_retained_in_registry_order() -> None:
    checks = [
        Check(str(index), str(index), "exit 3" if index % 3 == 0 else "true") for index in range(12)
    ]
    results = run_checks(checks, 4)
    assert [result.id for result in results] == [check.id for check in checks]
    assert [result.ok for result in results] == [check.run == "true" for check in checks]


def test_exclusive_checks_finish_before_shared_work(tmp_path: Path) -> None:
    marker = shlex.quote(str(tmp_path / "exclusive"))
    checks = [
        Check("shared", "Shared", f"test -f {marker}"),
        Check("exclusive", "Exclusive", f"touch {marker}", exclusive=True),
    ]
    assert all(result.ok for result in run_checks(checks, 4))


@pytest.mark.parametrize(
    "arguments",
    [
        ["--jobs", "0"],
        ["--jobs", "-1"],
        ["--jobs", "1.5"],
        ["--jobs"],
        ["--jobs", ""],
        ["--jobs", "nan"],
        ["--jobs", "inf"],
    ],
)
def test_rejects_invalid_worker_counts(arguments: list[str]) -> None:
    assert parse_arguments(arguments)["jobsInvalid"] is not None


@pytest.mark.parametrize(
    ("value", "expected"), [("0x10", 16), ("0b10", 2), ("0o10", 8), ("1e1", 10), (" 2 ", 2)]
)
def test_preserves_numeric_worker_spellings(value: str, expected: int) -> None:
    assert parse_arguments(["--jobs", value])["jobs"] == expected


def test_default_worker_policy() -> None:
    assert 1 <= default_jobs({}) <= 4
    assert default_jobs({}) <= default_jobs({"GITHUB_ACTIONS": "true"})


def test_selection_preserves_unique_order() -> None:
    selection = parse_arguments(["--only", "a,b,a", "--only", "c", "--list", "--jobs", "2"])
    assert selection == {
        "only": ["a", "b", "c"],
        "list": True,
        "jobs": 2,
        "jobsInvalid": None,
    }


def test_unknown_and_empty_selections_fail() -> None:
    assert run_registry([Check("a", "A", "true")], "Tests", "run", ["--only", "missing"]) == 1
    assert run_registry([], "Tests", "run", []) == 1


def test_failed_spawn_keeps_diagnostics(
    monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    monkeypatch.setenv("PATH", "")
    assert not run_check(Check("missing", "Missing", "true")).ok
    output = capsys.readouterr().out
    assert "ENOENT" in output
    assert "spawn-error" in output


def test_summary_appends_failure_and_reproduction(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    summary = tmp_path / "summary"
    summary.write_text("prior\n", encoding="utf-8")
    monkeypatch.setenv("GITHUB_STEP_SUMMARY", str(summary))
    assert (
        run_registry(
            [Check("a", "A", "true"), Check("b", "B", "exit 4")],
            "Controls",
            "command",
            ["--jobs", "2"],
        )
        == 1
    )
    text = summary.read_text(encoding="utf-8")
    assert text.startswith("prior\n## Controls\n")
    assert "1/2 checks passed" in text
    assert "command --only b" in text


@pytest.mark.skipif(
    os.name == "nt",
    reason="POSIX SIGINT control; ordinary completion cases run on Windows",
)
def test_zero_exit_after_interrupt_cannot_qualify(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    child = tmp_path / "child.py"
    ready = tmp_path / "ready"
    child.write_text(
        "import os, signal, sys\nfrom pathlib import Path\nsignal.signal(signal.SIGINT, lambda *_: sys.exit(0))\nready = Path(sys.argv[1])\nready.with_suffix('.pending').write_text(str(os.getpid()))\nos.replace(ready.with_suffix('.pending'), ready)\nprint('before interruption', flush=True)\nsignal.pause()\nprint('1 finished.')\n",
        encoding="utf-8",
    )
    command = (
        f"exec {shlex.quote(sys.executable)} {shlex.quote(str(child))} {shlex.quote(str(ready))}"
    )
    check = Check("interrupted", "Interrupted", command, completion_pattern=r"^1 finished\.$")
    with concurrent.futures.ThreadPoolExecutor(max_workers=1) as executor:
        pending = executor.submit(run_check, check)
        try:
            deadline = time.monotonic() + 10
            while not ready.exists() and time.monotonic() < deadline:
                assert not pending.done(), "Child exited before readiness handshake"
                time.sleep(0.01)
            assert ready.exists(), "Child never published readiness"
            os.kill(int(ready.read_text()), signal.SIGINT)
            assert not pending.result(timeout=10).ok
            output = capsys.readouterr().out
            assert "before interruption" in output
            assert "exit 0 without required completion evidence" in output
        finally:
            if not pending.done() and ready.exists():
                os.kill(int(ready.read_text()), signal.SIGKILL)


@pytest.mark.parametrize(
    "payload",
    [
        "not-json",
        "[]",
        '{"operation":"unknown","checks":[]}',
        '{"operation":"runChecks","checks":[],"jobs":false}',
    ],
)
def test_protocol_rejects_invalid_requests(payload: str, monkeypatch: pytest.MonkeyPatch) -> None:
    import io

    monkeypatch.setattr(sys, "stdin", io.StringIO(payload))
    assert main() == 1


def test_protocol_through_real_cli(tmp_path: Path) -> None:
    script = Path(__file__).resolve().parents[1] / "check_runner.py"
    result = subprocess.run(
        [sys.executable, str(script)],
        input=json.dumps(
            {
                "operation": "runChecks",
                "checks": [{"id": "a", "name": "A", "run": "true"}],
                "jobs": 1,
            }
        ),
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=20,
    )
    assert result.returncode == 0, result.stderr
    messages = [json.loads(line) for line in result.stdout.splitlines()]
    assert [message["kind"] for message in messages] == ["output", "result"]
    assert messages[-1]["value"][0]["ok"] is True
