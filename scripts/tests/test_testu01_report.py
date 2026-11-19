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
from testu01_report import DECISIVE, extremity, read_expectations, verdict  # noqa: E402

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


@pytest.mark.parametrize("launcher", ["python", "bash"])
@pytest.mark.parametrize(
    ("name", "first", "last", "diagnostic", "status", "expected"),
    [
        ("interleaved", *CONTROL.split(" outside", 1), "driver diagnostic\n", 0, 0),
        ("clean", CLEAN, "", "", 0, 1),
        ("marginal", MARGINAL, "", "", 0, 1),
        ("empty", "", "", "", 0, 1),
        ("failed", CONTROL, "", "driver failure\n", 2, 1),
        ("exhausted", CONTROL, "", "input stream exhausted\n", 3, 1),
        ("exhausted-zero", CONTROL, "", "input stream exhausted\n", 0, 1),
        ("host-exit", CONTROL, "", "", 0, 0),
    ],
)
@pytest.mark.skipif(os.name == "nt", reason="The build control requires a POSIX Bash host.")
def test_build_control_preserves_streams(
    tmp_path: Path,
    launcher: str,
    name: str,
    first: str,
    last: str,
    diagnostic: str,
    status: int,
    expected: int,
) -> None:
    if name == "interleaved":
        last = " outside" + last
    build = tmp_path / "build"
    sandbox = copy_build_entry_points(tmp_path)
    build.mkdir()
    host_name = "WallstopStudios.UnityHelpers.RandomQuality"
    host = tmp_path / "Generator~" / host_name / "bin" / "Release" / "net9.0" / host_name
    producer_diagnostic = "producer exited early\n" if name == "host-exit" else ""
    producer_status = 9 if producer_diagnostic else 0
    write_command(
        host, f"printf '%s' {shlex.quote(producer_diagnostic)} >&2\nexit {producer_status}"
    )
    write_command(
        build / "wallstop-testu01",
        f"printf '%s' {shlex.quote(first)}\n"
        f"printf '%s' {shlex.quote(diagnostic)} >&2\n"
        f"printf '%s' {shlex.quote(last)}\n"
        f"exit {status}",
    )
    bash = shutil.which("bash")
    assert bash is not None
    command = (
        [bash, str(sandbox / "build-testu01.sh")]
        if launcher == "bash"
        else [sys.executable, str(sandbox / "build_testu01.py")]
    )
    result = subprocess.run(
        command,
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
    assert (build / "self-test/host.stderr.log").read_text(encoding="utf-8") == producer_diagnostic
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


@pytest.mark.parametrize("invalid", ["NaN", "Infinity", "-Infinity"])
def test_manifest_rejects_non_json_constants(tmp_path: Path, invalid: str) -> None:
    manifest = tmp_path / "manifest.json"
    manifest.write_text('{"generators": [], "extra": ' + invalid + "}", encoding="utf-8")
    with pytest.raises(ValueError, match="Invalid JSON constant"):
        read_expectations(manifest)


def test_redirected_report_uses_utf8_with_lf(tmp_path: Path) -> None:
    reports = tmp_path / "reports"
    reports.mkdir()
    name = "測定"
    (reports / f"{name}.txt").write_text(MARGINAL, encoding="utf-8")
    manifest = tmp_path / "manifest.json"
    manifest.write_text(
        json.dumps({"generators": [{"name": name, "widths": {"32": {"expected": "pass"}}}]}),
        encoding="utf-8",
    )
    result = subprocess.run(
        [sys.executable, str(SCRIPTS / "testu01_report.py"), str(reports), str(manifest)],
        env={**os.environ, "PYTHONIOENCODING": "cp1252"},
        capture_output=True,
        timeout=30,
        check=False,
    )
    assert result.returncode == 0, result.stderr
    assert name.encode() in result.stdout
    assert b"\r\n" not in result.stdout


def write_command(path: Path, body: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("#!/usr/bin/env bash\n" + body + "\n", encoding="utf-8")
    path.chmod(0o755)


def copy_build_entry_points(root: Path) -> Path:
    sandbox = root / "scripts" / "random-quality"
    sandbox.mkdir(parents=True)
    python_project = root / "scripts" / "python~"
    python_project.mkdir()
    for filename in ("pyproject.toml", "uv.lock"):
        shutil.copyfile(ROOT / "scripts" / "python~" / filename, python_project / filename)
    for filename in ("build-testu01.sh", "build_testu01.py", "testu01_report.py"):
        shutil.copyfile(SCRIPTS / filename, sandbox / filename)
    return sandbox


def cold_build_fixture(root: Path, mode: str) -> tuple[Path, dict[str, str]]:
    sandbox = copy_build_entry_points(root)
    tools = root / "mock tools"
    settings = root / "fixture files"
    build = root / "build folder"
    reports = root / "report folder"
    host_name = "WallstopStudios.UnityHelpers.RandomQuality"
    host = root / "Generator~" / host_name / "bin" / "Release" / "net9.0" / host_name
    write_command(
        settings / "driver",
        'IFS= read -r payload\n[[ "$payload" = "stream from host" ]] || exit 8\n'
        "printf '%s' " + shlex.quote(CONTROL),
    )
    write_command(
        settings / "host",
        "printf 'stream from host\\n'\nprintf 'host diagnostic\\n' >&2",
    )
    write_command(
        settings / "configure",
        'printf "%s\\n" "$1"\nprintf "configure diagnostic\\n" >&2\n'
        'if [[ "$BUILD_FIXTURE_MODE" = "configure-failed" ]]; then exit 9; fi',
    )
    write_command(tools / "curl", 'printf "%s\\n" "$4" > "$3"')
    write_command(
        tools / "unzip",
        'if [[ "$BUILD_FIXTURE_MODE" = "missing-source" ]]; then exit 0; fi\n'
        'destination="$5/TestU01-1.2.3"\n'
        'mkdir -p "$destination"\ncp "$FIXTURE_CONFIGURE" "$destination/configure"',
    )
    write_command(tools / "nproc", "printf '3\\n'")
    write_command(
        tools / "make",
        'printf "%s\\n" "$*"\nprintf "make diagnostic\\n" >&2',
    )
    write_command(
        tools / "gcc",
        'if [[ "$1" = "-dumpversion" ]]; then printf "12.2\\n"; exit 0; fi\n'
        'if [[ "$BUILD_FIXTURE_MODE" = "link-failed" ]]; then exit 11; fi\n'
        'while [[ "$1" != "-o" ]]; do shift; done\ncp "$FIXTURE_DRIVER" "$2"',
    )
    write_command(
        tools / "dotnet",
        'mkdir -p "$(dirname "$FIXTURE_HOST_DEST")"\ncp "$FIXTURE_HOST" "$FIXTURE_HOST_DEST"',
    )
    environment = {
        **os.environ,
        "PATH": str(tools) + os.pathsep + os.environ["PATH"],
        "UV_PYTHON": sys.executable,
        "TESTU01_BUILD_ROOT": str(build),
        "TESTU01_CONTROL_REPORT_DIRECTORY": str(reports),
        "TESTU01_URL": "fixture.zip",
        "BUILD_FIXTURE_MODE": mode,
        "FIXTURE_CONFIGURE": str(settings / "configure"),
        "FIXTURE_DRIVER": str(settings / "driver"),
        "FIXTURE_HOST": str(settings / "host"),
        "FIXTURE_HOST_DEST": str(host),
    }
    return sandbox, environment


@pytest.mark.parametrize(
    ("mode", "expected"),
    [("success", 0), ("missing-source", 1), ("configure-failed", 9), ("link-failed", 11)],
)
@pytest.mark.skipif(os.name == "nt", reason="The TestU01 native build uses POSIX host tools.")
def test_cold_build_preserves_steps_and_failure_status(
    tmp_path: Path, mode: str, expected: int
) -> None:
    sandbox, environment = cold_build_fixture(tmp_path, mode)
    result = subprocess.run(
        [sys.executable, str(sandbox / "build_testu01.py")],
        cwd=tmp_path,
        env=environment,
        capture_output=True,
        text=True,
        timeout=30,
        check=False,
    )
    assert result.returncode == expected, result.stdout + result.stderr
    build = Path(environment["TESTU01_BUILD_ROOT"])
    reports = Path(environment["TESTU01_CONTROL_REPORT_DIRECTORY"])
    assert (build / "TestU01.zip").read_text(encoding="utf-8") == "fixture.zip\n"
    if mode == "missing-source":
        assert "archive did not contain a TestU01-*" in result.stdout
        assert not (build / "configure.log").exists()
        return
    assert (build / "configure.log").read_text(encoding="utf-8") == (
        f"--prefix={build / 'install'}\nconfigure diagnostic\n"
    )
    if mode == "configure-failed":
        assert not (build / "make.log").exists()
        return
    assert (build / "make.log").read_text(encoding="utf-8") == "-j3\nmake diagnostic\n"
    assert (build / "install.log").read_text(encoding="utf-8") == "install\nmake diagnostic\n"
    if mode == "link-failed":
        assert not reports.exists()
        return
    assert "Self-test passed" in result.stdout
    assert (reports / "report.txt").read_text(encoding="utf-8") == CONTROL
    assert (reports / "driver.stderr.log").read_bytes() == b""
    assert (reports / "host.stderr.log").read_bytes() == b"host diagnostic\n"
