"""Preserve fixed layout policy, original CLI observations, and read-only execution."""

from __future__ import annotations

import json
import shutil
import subprocess
import sys
from pathlib import Path
from typing import NotRequired, TypedDict, cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import validate_github_pages_css as checker


class Case(TypedDict):
    name: str
    argument: NotRequired[str]
    content: str | None
    exit: int
    stdout: str
    stderr: str


class Corpus(TypedDict):
    provenance: dict[str, str]
    cases: list[Case]


CORPUS = cast(
    "Corpus",
    json.loads(
        (Path(__file__).parent / "fixtures/github-pages-css-v1.json").read_text(encoding="utf-8")
    ),
)


def base_css() -> str:
    content = CORPUS["cases"][0]["content"]
    assert content is not None
    return content


BASE = base_css()


@pytest.mark.parametrize("case", CORPUS["cases"], ids=lambda case: case["name"])
def test_frozen_original_cli(
    case: Case, tmp_path: Path, capsys: pytest.CaptureFixture[str], monkeypatch: pytest.MonkeyPatch
) -> None:
    path = tmp_path / "theme.css"
    before = case["content"].encode() if case["content"] is not None else None
    if before is not None:
        path.write_bytes(before)
    monkeypatch.chdir(tmp_path)
    argument = case.get("argument", str(path))
    assert checker.main([argument]) == case["exit"]
    output = capsys.readouterr()
    assert output.out.replace(argument, "<CSS>") == case["stdout"]
    assert output.err.replace(argument, "<CSS>") == case["stderr"]
    assert (path.read_bytes() if path.exists() else None) == before


def test_repository_theme_passes() -> None:
    assert checker.main([]) == 0


@pytest.mark.parametrize("suffix", ["", "\r\n", "\n"])
def test_declarations_in_another_selector_do_not_satisfy_header(
    tmp_path: Path, capsys: pytest.CaptureFixture[str], suffix: str
) -> None:
    path = tmp_path / "theme.css"
    path.write_text(
        BASE.replace("    float: none; /* OMIT-header-float */", "") + suffix, encoding="utf-8"
    )
    assert checker.main([str(path)]) == 1
    assert "MISSING: header float: none" in capsys.readouterr().out


@pytest.mark.parametrize("failure", ["read", "decode", "directory"])
def test_invalid_inputs_fail_visibly(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    failure: str,
) -> None:
    path = tmp_path / "theme.css"
    path.write_text(BASE, encoding="utf-8")
    if failure == "read":

        def refuse_read(_path: Path) -> bytes:
            raise PermissionError("controlled read refusal")

        monkeypatch.setattr(Path, "read_bytes", refuse_read)
    elif failure == "decode":
        path.write_bytes(b"\xff")
    else:
        path.unlink()
        path.mkdir()
    assert checker.main([str(path)]) == 1
    assert "ERROR:" in capsys.readouterr().out


@pytest.mark.parametrize(
    "script",
    ["scripts/validate-github-pages-css.sh", "scripts/tests/test-validate-github-pages-css.sh"],
)
@pytest.mark.skipif(
    sys.platform == "win32",
    reason="Windows calls Python directly; shell launchers require POSIX paths",
)
def test_shell_compatibility_launchers(script: str, tmp_path: Path) -> None:
    bash = shutil.which("bash")
    assert bash is not None
    path = tmp_path / "space λ.css"
    path.write_text(BASE, encoding="utf-8")
    arguments = ["-k", "test_repository_theme_passes"] if "/tests/" in script else [str(path)]
    result = subprocess.run(
        [bash, str(checker.ROOT / script), *arguments],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        encoding="utf-8",
        timeout=60,
        check=False,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert ("1 passed" if "/tests/" in script else "VALIDATION PASSED") in result.stdout


def test_real_python_cli_preserves_utf8_and_source(tmp_path: Path) -> None:
    path = tmp_path / "space λ.css"
    path.write_text(BASE, encoding="utf-8")
    before = path.read_bytes()
    result = subprocess.run(
        [sys.executable, str(checker.ROOT / "scripts/validate_github_pages_css.py"), str(path)],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        encoding="utf-8",
        timeout=30,
        check=False,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert str(path) in result.stdout
    assert path.read_bytes() == before


def test_large_comment_finishes_with_complete_report(tmp_path: Path) -> None:
    path = tmp_path / "large.css"
    path.write_text(BASE + "/*" + "x" * 1048576 + "*/\n", encoding="utf-8")
    before = path.read_bytes()
    result = subprocess.run(
        [sys.executable, str(checker.ROOT / "scripts/validate_github_pages_css.py"), str(path)],
        capture_output=True,
        text=True,
        encoding="utf-8",
        timeout=5,
        check=False,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "VALIDATION PASSED" in result.stdout
    assert "consider minification" in result.stdout
    assert path.read_bytes() == before


@pytest.mark.parametrize("kind", ["declarations", "media"])
def test_repeated_prefixes_have_bounded_cli_work(tmp_path: Path, kind: str) -> None:
    path = tmp_path / "repeated.css"
    content = (
        BASE + "/*" + "a:" * 262144 + ";*/\n"
        if kind == "declarations"
        else BASE.replace("min-width: 2000px", "min-width: 2001px") + "@media " * 65536
    )
    path.write_text(content, encoding="utf-8")
    result = subprocess.run(
        [sys.executable, str(checker.ROOT / "scripts/validate_github_pages_css.py"), str(path)],
        capture_output=True,
        text=True,
        encoding="utf-8",
        timeout=5,
        check=False,
    )
    assert result.returncode == (0 if kind == "declarations" else 1)
    assert (
        "VALIDATION PASSED" if kind == "declarations" else "MISSING: @media min-width: 2000px"
    ) in result.stdout
    assert path.read_text(encoding="utf-8") == content
