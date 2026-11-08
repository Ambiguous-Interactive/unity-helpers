"""Preserve skill and context size boundaries and exercise the real command-line contract."""

from __future__ import annotations

import codecs
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from lint_skill_sizes import classify, files_to_check, line_count, main

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/lint_skill_sizes.py"


def fixture(root: Path, name: str = ".llm/skills/example.md", count: int = 100) -> Path:
    path = root / name
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("line\n" * count, encoding="utf-8")
    return path


def repository(root: Path, *, skill: int | None = 100, context: int | None = 100) -> None:
    (root / ".llm/skills").mkdir(parents=True, exist_ok=True)
    if skill is not None:
        fixture(root, count=skill)
    if context is not None:
        fixture(root, ".llm/context.md", context)


@pytest.mark.parametrize(
    ("count", "severity", "code"),
    [
        (0, "OK", 0),
        (100, "OK", 0),
        (180, "OK", 0),
        (181, "WARNING", 0),
        (198, "WARNING", 0),
        (199, "CRITICAL", 0),
        (200, "ERROR", 1),
        (210, "ERROR", 1),
    ],
)
@pytest.mark.parametrize("target", ["skill", "context"])
def test_original_size_boundaries_and_prefixes(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    count: int,
    severity: str,
    code: int,
    target: str,
) -> None:
    repository(
        tmp_path,
        skill=count if target == "skill" else 100,
        context=count if target == "context" else 100,
    )
    assert main(["--repo-root", str(tmp_path), "--verbose"]) == code
    output = capsys.readouterr().out
    prefix = "[skill-sizes]" if target == "skill" else "[context-size]"
    assert f"{prefix} {severity}:" in output
    assert classify(count) == severity
    if count == 199:
        assert "AT the 199 line limit" in output
    if target == "context" and code:
        assert "[skill-sizes] ERROR:" not in output
    if target == "skill" and code:
        assert "[context-size] ERROR:" not in output


@pytest.mark.parametrize("target", ["skill", "context"])
def test_original_nonverbose_warning_is_silent_but_critical_is_visible(
    tmp_path: Path, capsys: pytest.CaptureFixture[str], target: str
) -> None:
    repository(
        tmp_path,
        skill=181 if target == "skill" else 100,
        context=181 if target == "context" else 100,
    )
    assert main(["--repo-root", str(tmp_path)]) == 0
    assert "WARNING:" not in capsys.readouterr().out
    fixture(tmp_path, ".llm/skills/example.md" if target == "skill" else ".llm/context.md", 199)
    assert main(["--repo-root", str(tmp_path)]) == 0
    assert "CRITICAL:" in capsys.readouterr().out


@pytest.mark.parametrize("target", ["skill", "context"])
def test_critical_failure_flag_is_enforced(tmp_path: Path, target: str) -> None:
    repository(
        tmp_path,
        skill=199 if target == "skill" else 100,
        context=199 if target == "context" else 100,
    )
    assert main(["--repo-root", str(tmp_path), "--fail-on-critical"]) == 1


def test_original_mixed_and_nested_files_are_all_checked(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    repository(tmp_path)
    fixture(tmp_path, ".llm/skills/nested/large.md", 210)
    fixture(tmp_path, ".llm/references/large-reference.md", 200)
    assert main(["--repo-root", str(tmp_path)]) == 1
    output = capsys.readouterr().out
    assert "nested/large.md: 210 lines" in output
    assert "references/large-reference.md: 200 lines" in output
    assert "Summary: 3 files checked" in output


def test_original_empty_skills_directory_still_checks_context(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    repository(tmp_path, skill=None)
    assert main(["--repo-root", str(tmp_path)]) == 0
    assert "context.md checked" in capsys.readouterr().out


def test_original_missing_context_warns_while_checking_skills(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    repository(tmp_path, context=None)
    assert main(["--repo-root", str(tmp_path)]) == 0
    assert "[context-size] WARNING:" in capsys.readouterr().out


def test_original_scoped_checks_do_not_read_unselected_files(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    repository(tmp_path, skill=100, context=210)
    assert main(["--repo-root", str(tmp_path), "--paths", ".llm/skills/example.md"]) == 0
    assert "[context-size]" not in capsys.readouterr().out
    assert main(["--repo-root", str(tmp_path), "--paths", ".llm/context.md"]) == 1
    assert "[context-size] ERROR:" in capsys.readouterr().out


def test_original_generated_index_is_exempt(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    repository(tmp_path)
    fixture(tmp_path, ".llm/skills/index.md", 600)
    assert main(["--repo-root", str(tmp_path), "--verbose"]) == 0
    assert "index.md" not in capsys.readouterr().out


@pytest.mark.parametrize(
    ("content", "expected"),
    [
        ("", 0),
        ("one", 1),
        ("one\n", 1),
        ("\n", 1),
        ("one\r\ntwo\r\n", 2),
        ("one\rtwo\r", 2),
        ("a\vB\fC\u0085D\u2028E", 1),
    ],
)
def test_line_count_matches_streamreader_boundaries(
    tmp_path: Path, content: str, expected: int
) -> None:
    path = tmp_path / "source.md"
    path.write_bytes(content.encode("utf-8"))
    assert line_count(path) == expected


@pytest.mark.parametrize(
    ("encoding", "bom"),
    [
        ("utf-8", codecs.BOM_UTF8),
        ("utf-16-le", codecs.BOM_UTF16_LE),
        ("utf-16-be", codecs.BOM_UTF16_BE),
        ("utf-32-le", codecs.BOM_UTF32_LE),
        ("utf-32-be", codecs.BOM_UTF32_BE),
    ],
)
def test_bom_encodings_match_original_get_content(
    tmp_path: Path, encoding: str, bom: bytes
) -> None:
    path = tmp_path / "source.md"
    path.write_bytes(bom + "雪\r\nsecond\n".encode(encoding))
    assert line_count(path) == 2


def test_unscoped_empty_corpus_and_missing_root_cannot_pass(tmp_path: Path) -> None:
    assert main(["--repo-root", str(tmp_path)]) == 1
    repository(tmp_path, skill=None, context=None)
    assert main(["--repo-root", str(tmp_path)]) == 1


def test_deleted_and_generated_only_scope_reports_no_validation(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    repository(tmp_path)
    assert (
        main(
            [
                "--repo-root",
                str(tmp_path),
                "--paths",
                ".llm/skills/deleted.md",
                ".llm/skills/index.md",
            ]
        )
        == 0
    )
    output = capsys.readouterr().out
    assert "no size checks ran" in output
    assert "All files within size limits" not in output


def test_scoped_duplicates_are_checked_once_and_paths_stay_data(tmp_path: Path) -> None:
    repository(tmp_path)
    path = fixture(tmp_path, ".llm/skills/雪 [example].md")
    files, context = files_to_check(
        tmp_path, [str(path), str(path), ".llm/skills/../skills/雪 [example].md"]
    )
    assert files == [path]
    assert context is False


def test_real_cli_keeps_multiple_paths_and_propagates_exit(tmp_path: Path) -> None:
    repository(tmp_path)
    first = fixture(tmp_path, ".llm/skills/first.md", 100)
    second = fixture(tmp_path, ".llm/references/second.md", 210)
    result = subprocess.run(
        [
            sys.executable,
            str(SCRIPT),
            "--repo-root",
            str(tmp_path),
            "--paths",
            str(first),
            str(second),
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        timeout=30,
        check=False,
    )
    assert result.returncode == 1
    assert "second.md: 210 lines" in result.stdout
    assert "Summary: 2 files checked" in result.stdout


def test_real_powershell_launcher_keeps_multiple_paths_and_exit_code(tmp_path: Path) -> None:
    shell = shutil.which("pwsh")
    if shell is None:
        pytest.skip("PowerShell is unavailable")
    repository(tmp_path)
    first = fixture(tmp_path, ".llm/skills/first.md", 100)
    second = fixture(tmp_path, ".llm/references/second.md", 199)
    result = subprocess.run(
        [
            shell,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/lint-skill-sizes.ps1"),
            "-RepoRoot",
            str(tmp_path),
            "-Paths",
            str(first),
            str(second),
            "-FailOnCritical",
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        timeout=60,
        check=False,
    )
    assert result.returncode == 1, result.stdout + result.stderr
    assert "Summary: 2 files checked" in result.stdout
    assert "CRITICAL:" in result.stdout


def test_linked_instruction_input_cannot_escape_scan(tmp_path: Path) -> None:
    repository(tmp_path)
    target = fixture(tmp_path, "outside.md", 210)
    link = tmp_path / ".llm/skills/link.md"
    try:
        link.symlink_to(target)
    except OSError:
        pytest.skip("Symlink creation is unavailable")
    assert main(["--repo-root", str(tmp_path)]) == 1
    assert main(["--repo-root", str(tmp_path), "--paths", str(link)]) == 1


def test_full_scan_refuses_nonregular_inputs_without_blocking(tmp_path: Path) -> None:
    maker = shutil.which("mkfifo")
    if maker is None:
        pytest.skip("A POSIX FIFO creator is unavailable")
    repository(tmp_path)
    path = tmp_path / ".llm/skills/pipe.md"
    created = subprocess.run([maker, str(path)], capture_output=True, check=False, timeout=10)
    if created.returncode != 0 or not path.is_fifo():
        pytest.skip("The creator did not produce a FIFO visible to native Python")
    result = subprocess.run(
        [sys.executable, str(SCRIPT), "--repo-root", str(tmp_path)],
        capture_output=True,
        text=True,
        check=False,
        timeout=10,
    )
    assert result.returncode == 1
    assert "not a regular file" in result.stdout


@pytest.mark.parametrize("script", [SCRIPT, ROOT / "scripts/lint_drawer_multiobject.py"])
def test_cli_errors_cannot_create_workflow_commands(tmp_path: Path, script: Path) -> None:
    missing = tmp_path / "missing\n::error::injected"
    arguments = (
        ["--repo-root", str(missing)]
        if script == SCRIPT
        else ["--repo-root", str(tmp_path), "--paths", str(missing / "BadDrawer.cs")]
    )
    result = subprocess.run(
        [sys.executable, str(script), *arguments],
        capture_output=True,
        text=True,
        check=False,
        timeout=30,
    )
    assert result.returncode == 1
    assert "\n::error::injected" not in result.stdout
    assert "%0A::error::injected" in result.stdout


def test_linked_repository_root_is_refused(tmp_path: Path) -> None:
    root = tmp_path / "repository"
    repository(root)
    link = tmp_path / "linked"
    try:
        link.symlink_to(root, target_is_directory=True)
    except OSError:
        pytest.skip("Directory links are unavailable on this host")
    with pytest.raises(ValueError, match="linked"):
        files_to_check(link, [".llm/skills/example.md"])


def test_legacy_test_launcher_runs_selected_control(tmp_path: Path) -> None:
    shell = shutil.which("pwsh")
    if shell is None:
        pytest.skip("PowerShell is unavailable")
    result = subprocess.run(
        [
            shell,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/tests/test-lint-skill-sizes.ps1"),
            "-TestFilter",
            "test_cli_errors_cannot_create_workflow_commands",
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "2 passed" in result.stdout


@pytest.mark.parametrize("count", [0, 100, 198])
def test_context_summary_preserves_observed_line_count(
    tmp_path: Path, capsys: pytest.CaptureFixture[str], count: int
) -> None:
    repository(tmp_path, skill=None, context=count)
    assert main(["--repo-root", str(tmp_path)]) == 0
    assert f"[context-size] Summary: context.md checked ({count} lines)" in capsys.readouterr().out
