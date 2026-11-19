"""Verify TestU01 decisions and the report and build entry points."""

from __future__ import annotations

import json
import os
import shlex
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts" / "random-quality"
sys.path.insert(0, str(SCRIPTS))
from testu01_report import DECISIVE, extremity, verdict  # noqa: E402

CONTROL = """========= Summary results of SmallCrush =========

 Version:          TestU01 1.2.3
 Generator:        wallstop-stream
 Number of statistics:  15
 Total CPU time:   00:00:06.33
 The following tests gave p-values outside [0.001, 0.9990]:
 (eps  means a value < 1.0e-300):
 (eps1 means a value < 1.0e-15):

       Test                          p-value
 ----------------------------------------------
  1  BirthdaySpacings                 eps
  2  Collision                      1 - eps1
  6  MaxOft                         5.6e-16
  8  MatrixRank                       eps
 10  RandomWalk1 H                  1.4e-11
 10  RandomWalk1 M                   1.8e-5
 ----------------------------------------------
 All other tests were passed
"""

MARGINAL = """========= Summary results of SmallCrush =========

 Number of statistics:  15
 The following tests gave p-values outside [0.001, 0.9990]:

       Test                          p-value
 ----------------------------------------------
  3  Gap                             7.2e-4
 ----------------------------------------------
 All other tests were passed
"""

CLEAN = """========= Summary results of SmallCrush =========

 Number of statistics:  15
 Total CPU time:   00:00:06.21

 All tests were passed
"""

ALL_FAILING = """========= Summary results of SmallCrush =========

 Version:          TestU01 1.2.3
 Generator:        wallstop-stream
 Number of statistics:  15
 Total CPU time:   00:00:05.05
 The following tests gave p-values outside [0.001, 0.9990]:
 (eps  means a value < 1.0e-300):
 (eps1 means a value < 1.0e-15):

       Test                          p-value
 ----------------------------------------------
  1  BirthdaySpacings                 eps
  2  Collision                        eps
  3  Gap                              eps
  4  SimpPoker                        eps
  5  CouponCollector                  eps
  6  MaxOft                           eps
  6  MaxOft AD                      1 - eps1
  7  WeightDistrib                    eps
  8  MatrixRank                       eps
  9  HammingIndep                     eps
 10  RandomWalk1 H                    eps
 10  RandomWalk1 M                    eps
 10  RandomWalk1 J                    eps
 10  RandomWalk1 R                    eps
 10  RandomWalk1 C                    eps
 ----------------------------------------------
"""


def test_recorded_control_and_marginal_statistics() -> None:
    result = verdict(CONTROL)
    assert result.ran_battery
    assert result.failed
    assert [row.test for row in result.decisive] == [
        "BirthdaySpacings",
        "Collision",
        "MaxOft",
        "MatrixRank",
        "RandomWalk1 H",
    ]
    assert [row.test for row in result.marginal] == ["RandomWalk1 M"]
    assert result.decisive[-1].extremity < DECISIVE <= result.marginal[0].extremity
    marginal = verdict(MARGINAL)
    assert marginal.ran_battery
    assert not marginal.failed
    assert [row.test for row in marginal.marginal] == ["Gap"]


@pytest.mark.parametrize("newline", ["\n", "\r\n"])
def test_clean_summary(newline: str) -> None:
    result = verdict(CLEAN.replace("\n", newline))
    assert result.ran_battery
    assert not result.failed


def test_decisive_high_side_value() -> None:
    result = verdict(CONTROL.replace("5.6e-16", "1 - 1.4e-11"))
    assert result.failed
    assert result.decisive[2].raw == "1 - 1.4e-11"


@pytest.mark.parametrize("raw", ["nonsense", "", "1 - nonsense", "-0.5", "2.0", "1e999", "0x0"])
def test_refuse_invalid_probabilities(raw: str) -> None:
    assert extremity(raw) is None


def test_high_side_values_use_nearest_endpoint() -> None:
    assert extremity("1 - eps1") == 1e-16
    assert extremity("1 - 7.2e-4") == 7.2e-4
    assert extremity("0.9999") == pytest.approx(1e-4)
    assert extremity("1 - 0.9999") == pytest.approx(1e-4)


@pytest.mark.parametrize(
    "report_text",
    [
        "",
        "All tests were passed",
        CLEAN.replace("Number of statistics:  15", "Number of statistics:  0"),
        CLEAN.replace("Number of statistics:  15", "Number of statistics:  9007199254740992"),
        CLEAN.replace("Number of statistics:  15", ""),
        CONTROL.replace("All other tests were passed", ""),
        CONTROL.replace("5.6e-16", "nonsense"),
        CONTROL.replace("5.6e-16", "2"),
        CONTROL.replace("5.6e-16", "1e999"),
        CONTROL.replace("5.6e-16", "0x0"),
        CONTROL.replace("5.6e-16", "1 - 2"),
        CONTROL.replace("p-values outside", "p-valuesdriver diagnostic\n outside"),
        CONTROL.replace("RandomWalk1 M                   1.8e-5", "RandomWalk1 M"),
        CONTROL.replace("All other tests were passed", "All tests were passed"),
        CLEAN + CONTROL,
        CLEAN + "wallstop-testu01: input stream exhausted after 12 words\n",
        CLEAN + "unfinished trailing output\n",
    ],
)
def test_partial_or_malformed_reports_cannot_pass(report_text: str) -> None:
    assert not verdict(report_text).ran_battery


def test_nearly_all_failing_reports_without_footer() -> None:
    def without_line(text: str, name: str) -> str:
        return "".join(line for line in text.splitlines(keepends=True) if name not in line)

    nearly = without_line(ALL_FAILING, "MatrixRank")
    assert len(verdict(ALL_FAILING).decisive) == 15
    assert len(verdict(nearly).decisive) == 14
    assert not verdict(without_line(nearly, "HammingIndep")).ran_battery
    assert not verdict(ALL_FAILING.rstrip().rstrip("-")).ran_battery


@pytest.mark.parametrize(
    ("name", "first", "last", "diagnostic", "status", "expected"),
    [
        ("interleaved", *CONTROL.split(" outside", 1), "driver diagnostic\n", 0, 0),
        ("clean", CLEAN, "", "", 0, 1),
        ("marginal", MARGINAL, "", "", 0, 1),
        ("empty", "", "", "", 0, 1),
        ("failed", CONTROL, "", "driver failure\n", 2, 1),
        ("exhausted", CONTROL, "", "input stream exhausted\n", 3, 1),
    ],
)
@pytest.mark.skipif(os.name == "nt", reason="The build control requires a POSIX Bash host.")
def test_build_control_preserves_streams(
    tmp_path: Path, name: str, first: str, last: str, diagnostic: str, status: int, expected: int
) -> None:
    if name == "interleaved":
        last = " outside" + last
    scripts = tmp_path / "scripts"
    build = tmp_path / "build"
    sandbox = scripts / "random-quality"
    sandbox.mkdir(parents=True)
    build.mkdir()
    python_project = scripts / "python~"
    python_project.mkdir()
    for filename in ("pyproject.toml", "uv.lock"):
        shutil.copyfile(ROOT / "scripts" / "python~" / filename, python_project / filename)
    for filename in ("build-testu01.sh", "testu01_report.py"):
        shutil.copyfile(SCRIPTS / filename, sandbox / filename)
    host_name = "WallstopStudios.UnityHelpers.RandomQuality"
    host = tmp_path / "Generator~" / host_name / "bin" / "Release" / "net9.0" / host_name
    host.parent.mkdir(parents=True)
    host.write_text("#!/usr/bin/env bash\nexit 0\n", encoding="utf-8")
    host.chmod(0o755)
    driver = build / "wallstop-testu01"
    driver.write_text(
        "#!/usr/bin/env bash\n"
        f"printf '%s' {shlex.quote(first)}\n"
        f"printf '%s' {shlex.quote(diagnostic)} >&2\n"
        f"printf '%s' {shlex.quote(last)}\n"
        f"exit {status}\n",
        encoding="utf-8",
    )
    driver.chmod(0o755)
    bash = shutil.which("bash")
    assert bash is not None
    result = subprocess.run(
        [bash, str(sandbox / "build-testu01.sh")],
        cwd=tmp_path,
        env={**os.environ, "TESTU01_BUILD_ROOT": str(build), "UV_PYTHON": sys.executable},
        capture_output=True,
        text=True,
        timeout=30,
        check=False,
    )
    assert result.returncode == expected, result.stdout + result.stderr
    assert (build / "self-test/report.txt").read_text(encoding="utf-8") == first + last
    assert (build / "self-test/driver.stderr.log").read_text(encoding="utf-8") == diagnostic
    assert ("Self-test passed" in result.stdout) is (expected == 0)


@pytest.mark.parametrize(
    ("files", "expected", "message"),
    [
        ({}, 1, "holds no reports"),
        ({"Known": CLEAN, "Weak": MARGINAL}, 0, "this is inconclusive"),
        ({"Weak": CONTROL, "Known": MARGINAL}, 0, "recorded-weak generator(s) failed"),
        ({"Known": CONTROL}, 1, "produced a decisive TestU01 failure"),
        ({"Unknown": CLEAN}, 1, "Not in the manifest: Unknown"),
        ({"NoExpectation": CLEAN}, 1, "Not in the manifest: NoExpectation"),
        ({"Known": ""}, 1, "contain no battery summary: Known"),
        ({"Known": MARGINAL}, 0, "marginal p-value(s), which is expected noise"),
    ],
)
def test_report_cli(tmp_path: Path, files: dict[str, str], expected: int, message: str) -> None:
    reports = tmp_path / "path with spaces"
    reports.mkdir()
    for generator, report_text in files.items():
        (reports / f"{generator}.txt").write_text(report_text, encoding="utf-8")
    manifest = tmp_path / "manifest.json"
    manifest.write_text(
        json.dumps(
            {
                "generators": [
                    {
                        "name": "Known",
                        "widths": {"32": {"expected": "pass"}},
                    },
                    {"name": "Weak", "widths": {"32": {"expected": "fail"}}},
                    {"name": "NoExpectation", "widths": {"32": {}}},
                ]
            }
        ),
        encoding="utf-8",
    )
    result = subprocess.run(
        [sys.executable, str(SCRIPTS / "testu01_report.py"), str(reports), str(manifest)],
        capture_output=True,
        text=True,
        timeout=30,
        check=False,
    )
    assert result.returncode == expected
    assert message in result.stdout + result.stderr
