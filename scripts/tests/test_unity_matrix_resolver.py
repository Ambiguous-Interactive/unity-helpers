"""Replay frozen JavaScript decisions and exercise the matrix command's file contract."""

from __future__ import annotations

import hashlib
import json
import os
import subprocess
import sys
from functools import partial
from itertools import product
from pathlib import Path
from typing import TYPE_CHECKING

import pytest

if TYPE_CHECKING:
    from collections.abc import Callable

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "unity"))
import resolve_test_matrix as resolver

ROOT = Path(__file__).resolve().parents[2]
CLI = ROOT / "scripts/unity/resolve_test_matrix.py"
CANONICAL = ["2021.3.45f1", "2022.3.45f1", "6000.5.2f1", "6000.6.0f1"]
MATRIX_ENV = {
    "INPUT_UNITY_VERSION": "",
    "INPUT_TEST_MODE": "",
    "INPUT_ACCEPTANCE": "",
    "INPUT_UNITY_COVERAGE": "",
    "GITHUB_EVENT_NAME": "",
    "AGENT_CHECK_BASE": "",
    "GITHUB_OUTPUT": "",
    "GITHUB_STEP_SUMMARY": "",
}
FIXTURE_HASH = "44ea0dc9385509b14c403653254fd79e6649df3b2dbd7ce59d9d9326c5218840"
FAILURE = "[resolve-test-matrix] "


def record(operation: Callable[[], object], lines: list[str]) -> None:
    try:
        payload = {"result": operation()}
    except ValueError as error:
        payload = {"error": str(error)}
    lines.append(json.dumps(payload, separators=(",", ":"), ensure_ascii=False))


def test_frozen_javascript_results() -> None:
    versions: tuple[object, ...] = (
        CANONICAL,
        [CANONICAL[0], CANONICAL[3]],
        [CANONICAL[3]],
        [CANONICAL[1]],
        [],
        [CANONICAL[3], CANONICAL[3]],
        [" " + CANONICAL[3]],
        [3, None],
        {},
        None,
        ["\u03b1.\u03b2", CANONICAL[3]],
    )
    requests = (
        "",
        *CANONICAL,
        "  6000.6.0f1 ",
        "\ufeff6000.6.0f1\u00a0",
        "6000.7.24f1",
        "\u03b1.\u03b2",
    )
    modes = (
        "",
        "all",
        "editmode",
        "playmode",
        "standalone",
        " browser ",
        " PLAYMODE ",
        " playmode ",
    )
    acceptance = (
        "",
        "none",
        "nullableconsumer",
        "wproto",
        "intmap",
        "sentinel",
        "serialization",
        "unknown",
        "\t wproto ",
    )
    changed_files: tuple[list[str], ...] = (
        [],
        ["README.md"],
        ["docs/guides/first-launch.md"],
        ["Runtime/Calculation.cs"],
        ["Editor/Calculation.cs"],
        ["Runtime/Serialization/WProto.cs"],
        ["unknown.xyz"],
        ["Tests/Runtime/Calculation.cs"],
        ["docs/note.md", "Runtime/Core/Pool.cs"],
    )
    lines: list[str] = []
    for candidates in versions:
        for version, mode, requested in product(requests, modes, acceptance):
            record(
                partial(resolver.resolve_test_matrix, candidates, version, mode, requested), lines
            )
        record(partial(resolver.latest_version, candidates), lines)
        record(partial(resolver.resolve_compatibility_matrix, candidates), lines)
    for files in changed_files:
        record(partial(resolver.resolve_pull_request_matrix, CANONICAL, files), lines)
    assert len(lines) == 7_159
    assert hashlib.sha256(("\n".join(lines) + "\n").encode("utf-8")).hexdigest() == FIXTURE_HASH


def run_cli(environment: dict[str, str], directory: Path) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(
        [sys.executable, str(CLI)],
        cwd=directory,
        env={**os.environ, **MATRIX_ENV, **environment},
        check=False,
        capture_output=True,
        timeout=20,
    )


@pytest.mark.parametrize(
    ("event", "coverage", "test_mode", "expected_modes"),
    [
        ("push", "", "editmode", ["editmode", "playmode", "standalone"]),
        (
            "workflow_dispatch",
            "compatibility",
            "standalone",
            ["editmode", "playmode", "standalone"],
        ),
        ("workflow_dispatch", "", "playmode", ["playmode"]),
    ],
)
def test_cli_appends_ordered_lf_matrix_and_summary(
    tmp_path: Path,
    event: str,
    coverage: str,
    test_mode: str,
    expected_modes: list[str],
) -> None:
    out, summary = tmp_path / "matrix output", tmp_path / "step summary"
    out.write_bytes(b"previous=kept\n")
    summary.write_bytes(b"# Retained\n\n")
    versions: list[str] = json.loads((ROOT / ".github/unity-versions.json").read_bytes())["all"]
    assert len(versions) >= 2
    expected = (
        versions[:-1]
        if coverage == "compatibility"
        else versions[-1:]
        if event == "push"
        else versions
    )
    result = run_cli(
        {
            "GITHUB_EVENT_NAME": event,
            "INPUT_UNITY_COVERAGE": coverage,
            "INPUT_TEST_MODE": test_mode,
            "GITHUB_OUTPUT": str(out),
            "GITHUB_STEP_SUMMARY": str(summary),
        },
        tmp_path,
    )
    assert (result.returncode, result.stdout, result.stderr) == (0, b"", b"")
    exclusions = [
        {"unity-version": candidate} for candidate in versions if candidate not in expected
    ]
    selected = {
        "unity-versions": expected,
        "unity-exclusions": exclusions,
        "test-modes": expected_modes,
    }
    assert out.read_bytes() == b"previous=kept\n" + "".join(
        key + "=" + json.dumps(value, separators=(",", ":")) + "\n"
        for key, value in selected.items()
    ).encode("utf-8")
    assert summary.read_bytes() == (
        "# Retained\n\n### Selected Unity coverage\n\n"
        + json.dumps(selected, separators=(",", ":"))
        + "\n\nSINGLE_THREADED follows the selected modes. Package export smoke also runs. "
        "Unselected versions and modes are not validated by this run. Older editors run in Unity "
        "Compatibility; default manual dispatch retains full qualification.\n"
    ).encode("utf-8")


@pytest.mark.parametrize(
    ("environment", "error"),
    [
        ({"INPUT_UNITY_VERSION": "9999.1.1f1"}, "Unsupported Unity version: 9999.1.1f1"),
        ({"INPUT_TEST_MODE": "browser"}, "Unsupported Unity test mode: browser"),
        (
            {"GITHUB_EVENT_NAME": "pull_request"},
            "PR matrix selection requires the immutable PR base SHA",
        ),
        (
            {"GITHUB_EVENT_NAME": "pull_request", "AGENT_CHECK_BASE": "0" * 40},
            "Affected inventory failed:",
        ),
        ({"GITHUB_OUTPUT": ""}, "GITHUB_OUTPUT is required to publish the Unity test matrix."),
    ],
)
def test_invalid_matrix_keeps_both_output_files(
    tmp_path: Path, environment: dict[str, str], error: str
) -> None:
    out, summary = tmp_path / "output", tmp_path / "summary"
    for path in (out, summary):
        path.write_bytes(b"retained\n")
    result = run_cli(
        {"GITHUB_OUTPUT": str(out), "GITHUB_STEP_SUMMARY": str(summary), **environment}, tmp_path
    )
    assert result.returncode == 1
    assert result.stdout == b""
    assert result.stderr.decode("utf-8").startswith(FAILURE + error)
    assert out.read_bytes() == summary.read_bytes() == b"retained\n"


def test_summary_failure_retains_already_published_matrix(tmp_path: Path) -> None:
    out, summary = tmp_path / "output", tmp_path / "summary-directory"
    summary.mkdir()
    result = run_cli({"GITHUB_OUTPUT": str(out), "GITHUB_STEP_SUMMARY": str(summary)}, tmp_path)
    assert result.returncode == 1
    assert result.stderr.decode("utf-8").startswith(FAILURE)
    assert out.read_bytes().startswith(b"unity-versions=")
    assert out.read_bytes().endswith(b'test-modes=["editmode","playmode","standalone"]\n')


def test_both_matrix_steps_run_the_provisioned_locked_command() -> None:
    for filename in ("unity-tests.yml", "unity-compatibility.yml"):
        workflow = (ROOT / ".github/workflows" / filename).read_text(encoding="utf-8")
        resolve = workflow.index("      - name: Resolve dispatch overrides")
        step = workflow[resolve:].split("\n      - name: ")[0]
        assert "AGENT_CHECK_BASE: ${{ github.event.pull_request.base.sha }}" in step
        assert (
            "run: uv run --quiet --project scripts/python~ --locked --no-dev "
            "python scripts/unity/resolve_test_matrix.py"
        ) in step
        assert workflow.index("Setup Python tooling for matrix validation") < resolve
