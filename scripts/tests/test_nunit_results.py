"""Exercise the local NUnit result gate and its shell entry points."""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "unity"))
from nunit_results import summarize

ROOT = Path(__file__).resolve().parents[2]


@pytest.mark.parametrize(
    ("data", "status", "message"),
    [
        (b'<test-run total="003" passed="3" failed="0"/>', 0, "Total:   003"),
        (b'<test-run total="008" passed="8" failed="0"/>', 0, "Total:   008"),
        (b'<test-run total="0" failed="0"/>', 1, "0 tests ran"),
        (b'<test-run total="3" failed="0" skipped="3"/>', 0, "Skipped: 3"),
        (b'<test-run total="3" failed="0" inconclusive="3"/>', 0, "Skipped: 3"),
        (b'<test-run total="8" failed="008"/>', 1, "008 test(s) failed"),
        (b'<test-run total="1" passed="1"/>', 1, "'failed' count"),
        (b'<test-run total="1" failed="bad"/>', 1, "'failed' count"),
        (b'<test-run total="-1" failed="0"/>', 1, "'total' count"),
        ('<test-run total="\uff19" failed="0"/>'.encode(), 1, "'total' count"),
        (b'<root total="1" failed="0"/>', 1, "NUnit test-run root"),
        (b'<test-run total="1" failed="0" result="Failed"/>', 1, "zero failed count"),
        (
            b'<test-run total="1" failed="0"><test-case result="error"/></test-run>',
            1,
            "FAILED: unknown",
        ),
        (
            b'<!DOCTYPE test-run [<!ENTITY count "1">]><test-run total="&count;" failed="0"/>',
            1,
            "document type",
        ),
        ('<test-run total="1" failed="0"/>'.encode("utf-16"), 0, "Total:   1"),
        (b'<test-run total="1"', 1, "invalid NUnit results"),
    ],
)
def test_nunit_result_status(
    tmp_path: Path, capsys: pytest.CaptureFixture[str], data: bytes, status: int, message: str
) -> None:
    document = tmp_path / "a result Ω.xml"
    document.write_bytes(data)
    assert summarize(str(document), "EditMode") == status
    output = capsys.readouterr()
    assert message in output.out
    assert output.err == ""
    assert document.read_bytes() == data


def test_preserves_summary_order_and_failed_test_names(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    path = tmp_path / "results.xml"
    path.write_text(
        '<test-run total="2" passed="0" failed="2" skipped="0">'
        '<test-suite><test-case name="Short" fullname="Suite.Fail Ω" result="Failed"/>'
        '<test-case name="Fallback&#xA;second line" result="error"/></test-suite></test-run>',
        encoding="utf-8",
    )
    assert summarize(str(path), "PlayMode") == 1
    assert capsys.readouterr().out == (
        "\n==> [run-tests] PlayMode Test Results Summary:\n"
        "    ----------------------------------------\n"
        "    Total:   2\n    Passed:  0\n    Failed:  2\n    Skipped: 0\n"
        "\n    Failed tests:\n      FAILED: Suite.Fail Ω\n      FAILED: Fallback\n      second line\n"
        "    ----------------------------------------\n\n"
        "==> [run-tests] PlayMode: 2 test(s) failed.\n"
    )


def test_missing_result_preserves_the_caller_path(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    monkeypatch.chdir(tmp_path)
    assert summarize("./absent.xml", "PlayMode") == 1
    assert capsys.readouterr().out == (
        "\n==> [run-tests] PlayMode: no results at ./absent.xml -- tests did not run.\n"
    )


@pytest.mark.skipif(os.name != "posix", reason="The Docker test launcher uses POSIX Bash")
def test_sourced_launcher_preserves_caller_directory_and_arguments(tmp_path: Path) -> None:
    bash = shutil.which("bash")
    assert bash is not None
    path = tmp_path / "- result Ω.xml"
    path.write_text('<test-run total="1" failed="0"/>', encoding="utf-8")
    script = ROOT / "scripts/unity/lib/nunit-results.sh"
    result = subprocess.run(
        [
            bash,
            "-c",
            'source "$1"; parse_nunit_results "$2" "$3"',
            "bash",
            str(script),
            path.name,
            "Edit Mode",
        ],
        cwd=tmp_path,
        env={**os.environ, "CDPATH": "."},
        capture_output=True,
        timeout=30,
        check=False,
    )
    assert result.returncode == 0, result.stderr.decode("utf-8")
    assert b"Edit Mode Test Results Summary:" in result.stdout
    assert result.stderr == b""


@pytest.mark.skipif(os.name != "posix", reason="The legacy test entry point uses POSIX Bash")
def test_legacy_suite_launcher_collects_registered_cases() -> None:
    bash = shutil.which("bash")
    assert bash is not None
    result = subprocess.run(
        [bash, str(ROOT / "scripts/tests/test-unity-nunit-results.sh"), "--collect-only", "-q"],
        cwd=ROOT,
        capture_output=True,
        timeout=30,
        check=False,
    )
    assert result.returncode == 0, result.stderr.decode("utf-8")
    assert b"test_nunit_results.py::test_nunit_result_status" in result.stdout


@pytest.mark.skipif(os.name != "posix", reason="The Docker test launcher uses POSIX Bash")
def test_runner_checks_uv_before_creating_or_entering_a_unity_project(tmp_path: Path) -> None:
    bash = shutil.which("bash")
    dirname = shutil.which("dirname")
    assert bash is not None
    assert dirname is not None
    bin_dir = tmp_path / "bin"
    bin_dir.mkdir()
    (bin_dir / "dirname").symlink_to(dirname)
    project = tmp_path / "project"
    result = subprocess.run(
        [bash, str(ROOT / "scripts/unity/run-tests.sh")],
        env={**os.environ, "PATH": str(bin_dir), "UNITY_TEST_PROJECT_DIR": str(project)},
        capture_output=True,
        timeout=15,
        check=False,
    )
    assert result.returncode == 1
    assert b"Install uv before running local Unity tests" in result.stderr
    assert result.stdout == b""
    assert not project.exists()


@pytest.mark.skipif(os.name != "posix", reason="The Docker test launcher uses POSIX Bash")
@pytest.mark.parametrize(
    ("report", "native_status", "status"),
    [
        ("", 0, 1),
        ("", 42, 42),
        ('<test-run total="1" failed="0"/>', 0, 0),
        ('<test-run total="1" failed="0"/>', 17, 17),
        ('<test-run total="0" failed="0"/>', 0, 1),
    ],
)
def test_runner_only_qualifies_the_current_result(
    tmp_path: Path, report: str, native_status: int, status: int
) -> None:
    bash = shutil.which("bash")
    assert bash is not None
    unity = tmp_path / "package/scripts/unity"
    library = unity / "lib"
    library.mkdir(parents=True)
    shutil.copy2(ROOT / "scripts/unity/run-tests.sh", unity)
    (library / "nunit-results.sh").write_text('source "$NUNIT_LIBRARY"\n', encoding="utf-8")
    (unity / "report-slow-tests.ps1").write_text("# Synthetic fixture\n", encoding="utf-8")
    create_project = unity / "create-test-project.sh"
    create_project.write_text("#!/usr/bin/env bash\nexit 0\n", encoding="utf-8")
    create_project.chmod(0o755)
    mock_docker = unity / "run-unity-docker.sh"
    mock_docker.write_text(
        "#!/usr/bin/env bash\n"
        'while [[ "$1" != "-testResults" ]]; do shift; done\n'
        'if [[ -n "$NUNIT_REPORT" ]]; then\n'
        '    printf "%s" "$NUNIT_REPORT" > "$UNITY_TEST_PROJECT_DIR${2#/project}"\n'
        'fi\nexit "$NUNIT_NATIVE_STATUS"\n',
        encoding="utf-8",
    )
    mock_docker.chmod(0o755)
    project = tmp_path / "project"
    results = project / "test-results"
    results.mkdir(parents=True)
    stable_report = results / "editmode-results.xml"
    old_report = '<test-run total="4" failed="0"/>'
    stable_report.write_text(old_report, encoding="utf-8")
    command = subprocess.run(
        [bash, str(unity / "run-tests.sh")],
        env={
            **os.environ,
            "UNITY_TEST_PROJECT_DIR": str(project),
            "NUNIT_LIBRARY": str(ROOT / "scripts/unity/lib/nunit-results.sh"),
            "NUNIT_REPORT": report,
            "NUNIT_NATIVE_STATUS": str(native_status),
        },
        cwd=tmp_path,
        capture_output=True,
        timeout=30,
        check=False,
    )
    assert command.returncode == status, command.stdout.decode("utf-8")
    assert stable_report.read_text(encoding="utf-8") == (report or old_report)
    assert list(results.iterdir()) == [stable_report]
