"""Rank NUnit durations and enforce optional fixture budgets."""

from __future__ import annotations

import argparse
import math
import os
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from cli_encoding import configure_output


@dataclass(frozen=True)
class Timing:
    name: str
    seconds: float


@dataclass(frozen=True)
class Report:
    total: str
    seconds: float
    fixtures: tuple[Timing, ...]
    cases: tuple[Timing, ...]


def seconds(value: str, result_state: str = "") -> float:
    if not value.strip():
        outcome = result_state.split("(", 1)[0].split(":", 1)[0]
        if outcome in {"Passed", "Failed", "Inconclusive", "Warning"}:
            raise ValueError(f"Missing duration for executed result '{result_state}'")
        return 0.0
    try:
        result = float(value)
    except ValueError as error:
        raise ValueError(f"Unparseable duration '{value}'") from error
    if not math.isfinite(result) or result < 0:
        raise ValueError(f"Invalid duration '{value}': expected finite nonnegative seconds")
    return result


def read_report(path: Path) -> Report:
    root = ET.parse(path).getroot()  # noqa: S314 - local NUnit results; no external entity resolver.
    if root.tag != "test-run":
        raise ValueError("Expected an NUnit test-run root")
    fixtures: list[Timing] = []
    cases: list[Timing] = []
    for node in root.iter():
        if node.tag == "test-case":
            target = cases
        elif node.tag == "test-suite" and node.get("type") == "TestFixture":
            target = fixtures
        else:
            continue
        target.append(
            Timing(
                node.get("fullname") or node.get("name") or "",
                seconds(node.get("duration", ""), node.get("result", "")),
            )
        )
    if not fixtures or not cases:
        raise ValueError("Results require at least one test fixture and test case")
    return Report(
        root.get("total", ""),
        seconds(root.get("duration", ""), root.get("result", "")),
        tuple(fixtures),
        tuple(cases),
    )


def ranked(items: tuple[Timing, ...], top: int) -> list[Timing]:
    return sorted(items, key=lambda item: (-item.seconds, item.name))[:top]


def summary(path: Path, report: Report, fixtures: list[Timing]) -> str:
    lines = [
        f"### Slowest tests - {path.name}",
        "",
        (
            f"Run total: **{report.total}** tests in **{report.seconds:,.1f}s** "
            f"({report.seconds / 60:,.1f} min)"
        ),
        "",
        "| Seconds | Fixture |",
        "| --- | --- |",
    ]
    for fixture in fixtures:
        name = fixture.name.replace("|", r"\|").replace("\r", " ").replace("\n", " ")
        fence = "`"
        while fence in name:
            fence += "`"
        lines.append(f"| {fixture.seconds:,.2f} | {fence}{name}{fence} |")
    return "\n".join(lines) + "\n"


def nonnegative_integer(value: str) -> int:
    result = int(value)
    if result < 0:
        raise argparse.ArgumentTypeError("expected a nonnegative integer")
    return result


def finite_budget(value: str) -> float:
    result = float(value)
    if not math.isfinite(result):
        raise argparse.ArgumentTypeError("expected a finite fixture budget")
    return result


def main(arguments: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--results-path", required=True, type=Path)
    parser.add_argument("--top", type=nonnegative_integer, default=20)
    parser.add_argument("--fixture-budget-seconds", type=finite_budget, default=0.0)
    parser.add_argument("--fail-over-budget", action="store_true")
    parser.add_argument("--step-summary", action="store_true")
    options = parser.parse_args(arguments)
    path = Path(options.results_path)
    try:
        report = read_report(path)
    except FileNotFoundError:
        print(f"[slow-tests] ERROR: results file not found: {path}")
        return 1
    except (OSError, ET.ParseError, ValueError) as error:
        print(f"[slow-tests] ERROR: could not parse XML '{path}': {error}")
        return 1
    top = int(options.top)
    budget = float(options.fixture_budget_seconds)
    fixtures = ranked(report.fixtures, top)
    print("=" * 70)
    print(f"[slow-tests] {path}")
    print(
        f"[slow-tests] Run total: {report.total} tests in {report.seconds:,.1f}s "
        f"({report.seconds / 60:,.1f} min)"
    )
    print(f"[slow-tests] Top {top} slowest fixtures:")
    for fixture in fixtures:
        print(f"  {fixture.seconds:8,.2f}s  {fixture.name}")
    print(f"[slow-tests] Top {top} slowest test cases:")
    for case in ranked(report.cases, top):
        print(f"  {case.seconds:8,.3f}s  {case.name}")
    if options.step_summary:
        text = summary(path, report, fixtures)
        print(text)
        destination = os.environ.get("GITHUB_STEP_SUMMARY")
        if destination:
            try:
                with Path(destination).open("a", encoding="utf-8", newline="\n") as stream:
                    stream.write(text + "\n")
            except OSError as error:
                print(f"[slow-tests] ERROR: could not append step summary: {error}")
                return 1
    exceeded = sorted(
        (fixture for fixture in report.fixtures if 0 < budget < fixture.seconds),
        key=lambda item: (-item.seconds, item.name),
    )
    if exceeded:
        print(f"[slow-tests] {len(exceeded)} fixture(s) exceed the {budget:g}s budget:")
        severity = "error" if options.fail_over_budget else "warning"
        for fixture in exceeded:
            print(
                f"::{severity}::Fixture over {budget:g}s budget: {fixture.name} "
                f"took {fixture.seconds:.1f}s"
            )
    print("=" * 70)
    return int(bool(exceeded) and bool(options.fail_over_budget))


if __name__ == "__main__":
    sys.exit(main())
