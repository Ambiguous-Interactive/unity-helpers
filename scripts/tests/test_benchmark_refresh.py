from __future__ import annotations

import json
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

SCRIPT_DIRECTORY = Path(__file__).resolve().parents[1] / "unity"
sys.path.insert(0, str(SCRIPT_DIRECTORY))

from evaluate_perf_refresh import evaluate_refresh, inventory  # noqa: E402

EDIT = "results-6000.6.7f1-editmode.xml"
PLAY = "results-6000.6.7f1-playmode.xml"
EXTRA = "results-extra-playmode.xml"
TIMING = """
<test-run>
  <test-case fullname="Benchmark.Fixture" result="Passed">
    <output><![CDATA[Sort took 12.34 ms]]></output>
  </test-case>
</test-run>
"""
NO_TIMING = '<test-run><test-case fullname="Benchmark.Fixture" result="Passed" /></test-run>'


@pytest.mark.parametrize(
    ("status", "allowed", "expected", "actual", "complete", "invalid", "reasons"),
    [
        ("success", True, [EDIT, PLAY], [PLAY, EDIT], True, False, []),
        (
            "failure",
            True,
            [EDIT],
            [EDIT],
            False,
            False,
            ["benchmark aggregate result is failure"],
        ),
        (
            "success",
            False,
            [EDIT],
            [EDIT],
            False,
            False,
            ["this pinned dispatch is diagnostic-only and cannot replace the canonical baseline"],
        ),
        (
            "success",
            True,
            [EDIT],
            [PLAY],
            False,
            True,
            [
                f"result identities differ (missing: {EDIT}; unexpected: {PLAY})",
                "results with no metrics: none",
            ],
        ),
        (
            "success",
            True,
            [EDIT, EDIT],
            [EDIT],
            False,
            True,
            ["result identities differ (missing: none; unexpected: none)"],
        ),
        (
            "success",
            True,
            [],
            [],
            False,
            True,
            ["the expected result matrix is empty"],
        ),
    ],
)
def test_matrix_decision(
    status: str,
    allowed: bool,
    expected: list[str],
    actual: list[str],
    complete: bool,
    invalid: bool,
    reasons: list[str],
) -> None:
    decision = evaluate_refresh(status, allowed, expected, actual, dict.fromkeys(actual, 1))
    assert decision["complete"] is complete
    assert decision["invalidSuccessfulMatrix"] is invalid
    assert decision["reasons"] == reasons
    assert decision["expectedFiles"] == sorted(expected)
    assert decision["actualFiles"] == sorted(actual)


def test_decision_uses_original_utf16_order_and_missing_metric_rule() -> None:
    supplementary = "results-\U00010000.xml"
    private_use = "results-\ue000.xml"
    decision = evaluate_refresh(
        "success",
        True,
        [private_use, supplementary],
        [private_use, supplementary],
        {private_use: 2},
    )
    assert decision["actualFiles"] == [supplementary, private_use]
    assert decision["zeroMetricFiles"] == [supplementary]
    assert decision["filesWithMetrics"] == 1
    assert decision["reasons"] == [f"results with no metrics: {supplementary}"]


def test_inventory_matches_result_names_without_recursing(tmp_path: Path) -> None:
    candidate = tmp_path / "candidate"
    assert inventory(str(candidate)) == []
    candidate.mkdir()
    for name in (EDIT.upper(), "notes.xml", "results-data.txt"):
        (candidate / name).write_text(TIMING, encoding="utf-8")
    ignored = candidate / EXTRA
    ignored.mkdir()
    (ignored / PLAY).write_text(TIMING, encoding="utf-8")
    assert inventory(str(candidate)) == [EDIT.upper()]


def test_inventory_excludes_symbolic_link(tmp_path: Path) -> None:
    data = tmp_path / "actual.xml"
    data.write_text(TIMING, encoding="utf-8")
    try:
        (tmp_path / PLAY).symlink_to(data)
    except OSError as error:
        pytest.skip(f"symbolic-link creation unavailable: {error}")
    assert inventory(str(tmp_path)) == []


def run_script(candidate: Path, report: Path, expected: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [
            sys.executable,
            str(SCRIPT_DIRECTORY / "evaluate_perf_refresh.py"),
            "--benchmark-result",
            "success",
            "--allow-baseline-refresh",
            "true",
            "--expected-files",
            expected,
            "--candidate-dir",
            str(candidate),
            "--output",
            str(report),
        ],
        capture_output=True,
        text=True,
        check=False,
        timeout=30,
    )


@pytest.mark.parametrize("play_output", [TIMING, NO_TIMING])
def test_cli_counts_metrics_per_result_file(tmp_path: Path, play_output: str) -> None:
    assert shutil.which("node"), "The performance tools contract requires Node."
    candidate = tmp_path / "results with spaces"
    candidate.mkdir()
    (candidate / EDIT).write_text(TIMING, encoding="utf-8")
    (candidate / PLAY).write_text(play_output, encoding="utf-8")
    report = tmp_path / "decision.json"
    result = run_script(candidate, report, json.dumps([PLAY, EDIT]))
    assert (result.returncode, result.stdout, result.stderr) == (0, "", "")
    complete = play_output == TIMING
    assert json.loads(report.read_text(encoding="utf-8")) == {
        "complete": complete,
        "invalidSuccessfulMatrix": not complete,
        "reasons": [] if complete else [f"results with no metrics: {PLAY}"],
        "expectedFiles": [EDIT, PLAY],
        "actualFiles": [EDIT, PLAY],
        "expectedCount": 2,
        "actualCount": 2,
        "filesWithMetrics": 2 if complete else 1,
        "missingFiles": [],
        "unexpectedFiles": [],
        "zeroMetricFiles": [] if complete else [PLAY],
    }


def test_cli_rejects_empty_successful_matrix(tmp_path: Path) -> None:
    report = tmp_path / "decision.json"
    result = run_script(tmp_path, report, "[]")
    assert (result.returncode, result.stdout, result.stderr) == (0, "", "")
    decision = json.loads(report.read_text(encoding="utf-8"))
    assert decision["complete"] is False
    assert decision["invalidSuccessfulMatrix"] is True
    assert decision["reasons"] == ["the expected result matrix is empty"]


@pytest.mark.parametrize("expected", ["null", "{}", "[7]", '["unterminated'])
def test_cli_rejects_invalid_expectation_without_overwriting_decision(
    tmp_path: Path, expected: str
) -> None:
    report = tmp_path / "decision.json"
    report.write_text("original", encoding="utf-8")
    result = run_script(tmp_path, report, expected)
    assert (result.returncode, result.stdout) == (1, "")
    assert result.stderr
    assert report.read_bytes() == b"original"
