"""Verify timing budgets, result validation, and both command routes."""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast

import pytest
import yaml

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "unity"))
import report_slow_tests as slow

ROOT = Path(__file__).resolve().parents[2]
XML = """<test-run total="3" duration="123.5">
<test-suite type="TestFixture" fullname="A.SlowFixture" duration="100" total="1">
<test-case fullname="A.SlowFixture.Test" duration="60" /></test-suite>
<test-suite type="TestFixture" fullname="A.FastFixture" duration="3" total="2">
<test-case fullname="A.FastFixture.First" duration="1" />
<test-case fullname="A.FastFixture.Second" duration="2" /></test-suite></test-run>"""


@pytest.fixture
def results(tmp_path: Path) -> Path:
    path = tmp_path / "résults ü.xml"
    path.write_text(XML, encoding="utf-8")
    return path


def test_ranking_and_totals(results: Path, capsys: pytest.CaptureFixture[str]) -> None:
    assert slow.main(["--results-path", str(results)]) == 0
    output = capsys.readouterr().out
    assert output.index("A.SlowFixture") < output.index("A.FastFixture")
    assert "3 tests in 123.5s" in output
    assert "60.000s  A.SlowFixture.Test" in output
    assert "2.1 min" in output


@pytest.mark.parametrize("fail", [False, True])
@pytest.mark.parametrize("budget", [-1, 0, 3, 50, 100, 500])
def test_budget_checks_all_fixtures(
    results: Path, capsys: pytest.CaptureFixture[str], fail: bool, budget: int
) -> None:
    arguments = [
        "--results-path",
        str(results),
        "--top",
        "1",
        "--fixture-budget-seconds",
        str(budget),
    ]
    if fail:
        arguments.append("--fail-over-budget")
    assert slow.main(arguments) == int(fail and 0 < budget < 100)
    output = capsys.readouterr().out
    if 0 < budget < 100:
        assert ("::error::" if fail else "::warning::") in output
    else:
        assert "::error::" not in output
        assert "::warning::" not in output


def test_budget_applies_beyond_top_results(results: Path) -> None:
    report = slow.read_report(results)
    assert len(report.fixtures) == 2
    assert len(slow.ranked(report.fixtures, 1)) == 1
    assert (
        slow.main(
            [
                "--results-path",
                str(results),
                "--top",
                "1",
                "--fixture-budget-seconds",
                "2",
                "--fail-over-budget",
            ]
        )
        == 1
    )


def test_ties_use_ordinal_names() -> None:
    names = ["z", "á", "A", "a", "Z"]
    items = tuple(slow.Timing(name, 1) for name in names)
    assert [item.name for item in slow.ranked(items, 10)] == ["A", "Z", "a", "z", "á"]


@pytest.mark.parametrize("duration", ["bad", "NaN", "inf", "-1", "1,2", "1e9999"])
@pytest.mark.parametrize("target", ['duration="123.5"', 'duration="100"', 'duration="60"'])
def test_invalid_durations_fail_before_reporting(
    results: Path, duration: str, target: str, capsys: pytest.CaptureFixture[str]
) -> None:
    results.write_text(XML.replace(target, f'duration="{duration}"'), encoding="utf-8")
    assert slow.main(["--results-path", str(results), "--fail-over-budget"]) == 1
    assert "ERROR" in capsys.readouterr().out


@pytest.mark.parametrize("value", ["", " ", "0", "1.25", "1e2"])
def test_supported_durations(value: str) -> None:
    assert slow.seconds(value) == (float(value) if value.strip() else 0)


@pytest.mark.parametrize(
    "content",
    ["<invalid", "<other />", "<test-run />", "<test-run><test-case /></test-run>"],
)
def test_malformed_or_empty_results_fail(results: Path, content: str) -> None:
    results.write_text(content, encoding="utf-8")
    assert slow.main(["--results-path", str(results)]) == 1


def test_missing_and_unreadable_files(tmp_path: Path) -> None:
    assert slow.main(["--results-path", str(tmp_path / "missing.xml")]) == 1
    assert slow.main(["--results-path", str(tmp_path)]) == 1


def test_names_fall_back_and_skipped_durations_are_zero(results: Path) -> None:
    results.write_text(
        '<test-run total="1"><test-suite type="TestFixture" name="Skipped">'
        '<test-case name="Case" result="Skipped" /></test-suite></test-run>',
        encoding="utf-8",
    )
    report = slow.read_report(results)
    assert report.fixtures == (slow.Timing("Skipped", 0),)
    assert report.cases == (slow.Timing("Case", 0),)


def test_summary_appends_utf8_and_escapes_names(
    results: Path, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    results.write_text(XML.replace("A.SlowFixture", "Foo&lt;int&gt;|` ü\nline"), encoding="utf-8")
    summary = tmp_path / "summary.md"
    summary.write_text("existing\n", encoding="utf-8")
    monkeypatch.setenv("GITHUB_STEP_SUMMARY", str(summary))
    assert slow.main(["--results-path", str(results), "--step-summary"]) == 0
    text = summary.read_text(encoding="utf-8")
    assert text.startswith("existing\n### Slowest tests")
    assert "| Seconds | Fixture |" in text
    assert r"``Foo<int>\|` ü line``" in text


def test_summary_write_failure_is_reported(results: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("GITHUB_STEP_SUMMARY", str(results.parent))
    assert slow.main(["--results-path", str(results), "--step-summary"]) == 1


@pytest.mark.parametrize("legacy", [False, True])
@pytest.mark.parametrize("fail", [False, True])
@pytest.mark.parametrize(("top", "budget"), [(1, 50.5), (0, 50.5), (0, -1.0)])
def test_cli(
    results: Path, tmp_path: Path, legacy: bool, fail: bool, top: int, budget: float
) -> None:
    if legacy:
        shell = shutil.which("pwsh")
        assert shell is not None
        command = [
            shell,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/unity/report-slow-tests.ps1"),
            "-ResultsPath",
            str(results),
            "-Top",
            str(top),
            "-FixtureBudgetSeconds",
            str(budget),
            "-StepSummary",
        ]
        if fail:
            command.append("-FailOverBudget")
    else:
        command = [
            sys.executable,
            str(ROOT / "scripts/unity/report_slow_tests.py"),
            "--results-path",
            str(results),
            "--top",
            str(top),
            "--fixture-budget-seconds",
            str(budget),
            "--step-summary",
        ]
        if fail:
            command.append("--fail-over-budget")
    environment = dict(os.environ)
    environment.pop("GITHUB_STEP_SUMMARY", None)
    result = subprocess.run(
        command,
        cwd=tmp_path,
        env=environment,
        capture_output=True,
        text=True,
        encoding="utf-8",
        check=False,
        timeout=60,
    )
    assert result.returncode == int(fail and budget > 0), result.stdout + result.stderr
    assert f"Top {top} slowest fixtures" in result.stdout
    assert ("Fixture over 50.5s budget" in result.stdout) == (budget > 0)
    if top == 0:
        assert "A.SlowFixture.Test" not in result.stdout
    assert "résults ü.xml" in result.stdout


@pytest.mark.parametrize(
    ("option", "value"),
    [
        ("--top", "-1"),
        ("--fixture-budget-seconds", "nan"),
        ("--fixture-budget-seconds", "inf"),
    ],
)
def test_invalid_options_are_rejected(results: Path, option: str, value: str) -> None:
    with pytest.raises(SystemExit) as failure:
        slow.main(["--results-path", str(results), option, value])
    assert failure.value.code == 2


@pytest.mark.parametrize("job_name", ["unity-tests", "unity-tests-single-threaded"])
def test_ci_reporter_tools_are_provisioned_before_unity_lock(job_name: str) -> None:
    workflow = cast(
        "dict[str, dict[str, dict[str, object]]]",
        yaml.safe_load((ROOT / ".github/workflows/unity-tests.yml").read_text(encoding="utf-8")),
    )
    steps = cast("list[dict[str, object]]", workflow["jobs"][job_name]["steps"])
    setup_indices = [
        index
        for index, step in enumerate(steps)
        if step.get("uses") == "./.github/actions/setup-python-tools"
    ]
    assert setup_indices, f"{job_name} needs Python and uv for failure-path timing reports"
    setup_index = setup_indices[0]
    assert "if" not in steps[setup_index], "Reporter tooling must not depend on test success"
    checkout_index = next(i for i, step in enumerate(steps) if step.get("id") == "checkout")
    lock_index = next(i for i, step in enumerate(steps) if step.get("id") == "unity_lock")
    assert checkout_index < setup_index < lock_index


@pytest.mark.parametrize("result", ["Passed", "Failed", "Failed(Child)", "Inconclusive"])
@pytest.mark.parametrize("target", ['duration="123.5"', 'duration="100"', 'duration="60"'])
@pytest.mark.parametrize("duration", [None, "", " "])
def test_executed_results_require_duration_before_budget_reporting(
    results: Path,
    result: str,
    target: str,
    duration: str | None,
    capsys: pytest.CaptureFixture[str],
) -> None:
    replacement = f'result="{result}"'
    if duration is not None:
        replacement += f' duration="{duration}"'
    results.write_text(XML.replace(target, replacement), encoding="utf-8")
    assert (
        slow.main(
            ["--results-path", str(results), "--fixture-budget-seconds", "1", "--fail-over-budget"]
        )
        == 1
    )
    output = capsys.readouterr().out
    assert "Missing duration" in output
    assert "Slowest fixtures" not in output
