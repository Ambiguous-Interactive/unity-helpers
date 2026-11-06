"""Exercise import scopes, lexical masking, physical inputs and the retained CLI."""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import lint_duplicate_usings as lint

ROOT = Path(__file__).resolve().parents[2]


@pytest.mark.parametrize(
    ("source", "count"),
    [
        ("", 0),
        ("using System;", 0),
        ("using System; using System;", 1),
        ("namespace N { using System; using System; class C {} }", 1),
        ("namespace N; using System; using System; class C {}", 1),
        ("namespace A { namespace B { using System; using System; } }", 1),
        ("namespace A { using System; } namespace B { using System; }", 0),
        ("namespace A { using System; namespace B { using System; } }", 0),
        ("using System; namespace N { using System; }", 0),
        ("using IO = System.IO; using IO=System.IO;", 1),
        ("using static System.Math; using static System.Math;", 1),
        ("using static Generic<int>; using static Generic < int >;", 1),
        ("extern alias Lib; using Lib::X; using Lib :: X;", 1),
        ("using System; using static System;", 0),
        ("using A = System.IO; using B = System.IO;", 0),
        ("using A = List<int>; using A=List < int >;", 1),
        ("using System . IO; using System.IO;", 1),
        ("using System.\nIO; using System.IO;", 1),
        ("using System /* comment */ . IO; using System.IO;", 1),
        ("using A = System.\nIO; using A=System.IO;", 1),
        ("global using System; global using System;", 1),
        ("extern alias Lib; using X; using X;", 1),
        ("namespace N { extern alias Lib; using X; using X; }", 1),
        ("extern alias Lib; using X; namespace N { using X; }", 0),
        (
            "class C { void F() { using Stream s = Open(); using Stream s = Open(); } }",
            0,
        ),
        ("class C { void F() { using (Open()) {} using (Open()) {} } }", 0),
        ("// using System;\nusing System;", 0),
        ("/* using System;\nusing System; */ using System;", 0),
        ('class C { string s = @"using System;\nusing System;"; }', 0),
        ('class C { string s = """\nusing System;\nusing System;\n"""; }', 0),
        (
            'namespace A { class C { string s = "}"; } } namespace B { using X; using X; }',
            1,
        ),
        ("namespace A { class C {} } namespace B { using X; using X; }", 1),
        ("#if SYMBOL\nnamespace A { using X; using X; }\n#endif", 1),
        ("using X; /* a */ using X; using X;", 2),
        ("using X; using x;", 0),
    ],
)
def test_scope_and_lexical_cases(source: str, count: int) -> None:
    assert len(lint.analyze(source)) == count


@pytest.mark.parametrize("newline", ["\n", "\r\n", "\r"])
def test_diagnostic_line_and_first_line(
    newline: str, tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    path = tmp_path / "Input.cs"
    path.write_bytes(newline.join(("namespace N", "{", "using X;", "using X;", "}")).encode())
    assert lint.main(["--paths", str(path)]) == 1
    output = capsys.readouterr().out
    assert "line=4" in output
    assert "first declaration at line 3" in output
    assert "scope 'N'" in output


def test_physical_corpus_and_required_nonempty_roots(tmp_path: Path) -> None:
    assert lint.main(["--repo-root", str(tmp_path)]) == 1
    runtime = tmp_path / "Runtime"
    runtime.mkdir()
    assert lint.main(["--repo-root", str(tmp_path)]) == 1
    (runtime / "Input.cs").write_text("using X;", encoding="utf-8")
    assert lint.main(["--repo-root", str(tmp_path)]) == 0
    nested = runtime / "Nested"
    nested.mkdir()
    (nested / "Second.cs").write_text("using X; using X;", encoding="utf-8")
    assert lint.main(["--repo-root", str(tmp_path)]) == 1


def test_explicit_paths_are_all_checked_and_deduplicated(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    first = tmp_path / "入力.cs"
    second = tmp_path / "Second.cs"
    first.write_text("using X;", encoding="utf-8-sig")
    second.write_text("using X; using X;", encoding="utf-8")
    assert lint.main(["--paths", str(first), str(second), str(second), "--verbose"]) == 1
    assert "Checked 2 files" in capsys.readouterr().out
    assert lint.main(["--paths", str(tmp_path / "missing.cs")]) == 1


def test_read_failure_does_not_hide_sibling_finding(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    broken = tmp_path / "Broken.cs"
    broken.write_bytes(b"\xff")
    sibling = tmp_path / "Sibling.cs"
    sibling.write_text("using X; using X;", encoding="utf-8")
    assert lint.main(["--paths", str(broken), str(sibling)]) == 1
    assert "UNH007" in capsys.readouterr().out


def git(repository: Path, *arguments: str) -> None:
    executable = shutil.which("git")
    assert executable is not None
    subprocess.run(
        [executable, "-C", str(repository), *arguments],
        check=True,
        capture_output=True,
        timeout=30,
    )


def test_staged_scan_is_anchored_and_reads_index(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    git(tmp_path, "init")
    source = tmp_path / "Input.cs"
    source.write_text("using X; using X;", encoding="utf-8")
    git(tmp_path, "add", "Input.cs")
    source.write_text("using X;", encoding="utf-8")
    monkeypatch.chdir(tmp_path.parent)
    assert lint.main(["--repo-root", str(tmp_path), "--staged-only"]) == 1
    git(tmp_path, "add", "Input.cs")
    assert lint.main(["--repo-root", str(tmp_path), "--staged-only"]) == 0


def test_compatibility_launcher_from_other_directory(tmp_path: Path) -> None:
    executable = shutil.which("pwsh")
    assert executable is not None
    first = tmp_path / "One.cs"
    second = tmp_path / "Second.cs"
    first.write_text("using X;", encoding="utf-8")
    second.write_text("using X; using X;", encoding="utf-8")
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/lint-duplicate-usings.ps1"),
            "-Paths",
            str(first),
            str(second),
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 1, result.stdout + result.stderr
    assert "UNH007" in result.stdout


def test_suite_launcher_executes_cases(tmp_path: Path) -> None:
    executable = shutil.which("pwsh")
    assert executable is not None
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/tests/test-lint-duplicate-usings.ps1"),
            "-TestFilter",
            "test_diagnostic_line_and_first_line",
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "3 passed" in result.stdout


def test_python_cli_reports_and_clears_a_real_finding(tmp_path: Path) -> None:
    path = tmp_path / "Input.cs"
    command = [
        sys.executable,
        str(ROOT / "scripts/lint_duplicate_usings.py"),
        "--paths",
        str(path),
    ]
    for source, expected in (("using X; using X;", 1), ("using X;", 0)):
        path.write_text(source, encoding="utf-8")
        result = subprocess.run(
            command,
            cwd=tmp_path,
            capture_output=True,
            text=True,
            check=False,
            timeout=30,
        )
        assert result.returncode == expected, result.stdout + result.stderr
        assert ("UNH007" in result.stdout) == bool(expected)
