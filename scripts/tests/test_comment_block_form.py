"""Exercise comment detection, conversion, discovery and both command boundaries."""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from lint_comment_block_form import ROOTS, analyze, files_to_check, main, rewrite

ROOT = Path(__file__).resolve().parents[2]


def in_type(body: str) -> str:
    return (
        "namespace Fixture\n{\n    class Subject\n    {\n        void Run()\n        {\n"
        + body
        + "\n        }\n    }\n}\n"
    )


def populated(root: Path) -> None:
    for name in ROOTS:
        directory = root / name
        directory.mkdir(parents=True)
        (directory / "Clean.cs").write_text(in_type("            int value = 0;"), encoding="utf-8")


@pytest.mark.parametrize("indent", [8, 12, 16])
@pytest.mark.parametrize(
    "body",
    [
        "// first\n// second",
        "// first\n//\n// third",
        "// cspell:ignore word\n// actual reason",
    ],
)
def test_reports_ordinary_runs(indent: int, body: str) -> None:
    text = in_type("\n".join(" " * indent + line for line in body.splitlines()))
    found = analyze(text)
    assert len(found) == 1
    assert found[0].line == 7
    assert found[0].end_line == 6 + len(body.splitlines())
    assert found[0].indent == indent
    assert found[0].convertible


@pytest.mark.parametrize(
    "body",
    [
        "            // one line",
        "            /*\n                // first\n                // second\n            */",
        "            int a = 0; // first\n            int b = 0; // second",
        "            // UNH-SUPPRESS UNH003\n            // cspell:ignore word",
        "            // ReSharper disable Warning\n            // resharper disable Warning",
        '            string a = "// first";\n            string b = "// second";',
        '            string a = """\n            // first\n            // second\n            """;',
        '            string a = @"\n            // first\n            // second\n            ";',
        "        /// <summary>First\n        /// Second</summary>",
        "    // namespace first\n    // namespace second",
        "// MIT License\n// Full license text",
        "            // first\n\n            // second",
        "            // first\n            /// documentation\n            // second",
    ],
)
def test_does_not_report_lookalikes_or_exceptions(body: str) -> None:
    assert analyze(in_type(body)) == []


@pytest.mark.parametrize("newline", ["\n", "\r\n"])
@pytest.mark.parametrize("final", [False, True])
def test_conversion_preserves_newlines_and_multiple_run_positions(
    newline: str, final: bool
) -> None:
    text = in_type(
        "            // first\n            // second\n            int n = 0;\n            // third\n            // fourth"
    )
    text = text.rstrip("\n") + ("\n" if final else "")
    text = text.replace("\n", newline)
    fixed, declined = rewrite(text, analyze(text))
    assert declined == 0
    assert fixed.count("/*") == 2
    assert fixed.count("*/") == 2
    assert "// first" not in fixed
    assert analyze(fixed) == []
    assert fixed.endswith(newline) == final
    assert ("\r\n" in fixed) == (newline == "\r\n")


@pytest.mark.parametrize("delimiter", ["/*", "*/"])
def test_declines_unsafe_conversion(delimiter: str) -> None:
    text = in_type(f"            // first {delimiter}\n            // second")
    found = analyze(text)
    assert len(found) == 1
    assert not found[0].convertible
    assert rewrite(text, found) == (text, 1)


@pytest.mark.parametrize("root", ROOTS)
@pytest.mark.parametrize("explicit", [False, True])
def test_full_and_explicit_scans_report_each_root(
    tmp_path: Path, capsys: pytest.CaptureFixture[str], root: str, explicit: bool
) -> None:
    populated(tmp_path)
    subject = tmp_path / root / "Subject.cs"
    subject.write_text(in_type("            // first\n            // second"), encoding="utf-8")
    arguments = ["--repo-root", str(tmp_path)]
    if explicit:
        arguments += ["--paths", f"{root}/Subject.cs"]
    assert main(arguments) == 1
    assert f"{root}/Subject.cs:7" in capsys.readouterr().out


@pytest.mark.parametrize("root", ROOTS)
def test_empty_root_cannot_pass(
    tmp_path: Path, capsys: pytest.CaptureFixture[str], root: str
) -> None:
    populated(tmp_path)
    (tmp_path / root / "Clean.cs").unlink()
    assert main(["--repo-root", str(tmp_path)]) == 1
    assert root in capsys.readouterr().out


def test_summary_and_fix(tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    populated(tmp_path)
    assert main(["--repo-root", str(tmp_path)]) == 0
    output = capsys.readouterr().out
    for root in ROOTS:
        assert f"{root}/ 1" in output
    subject = tmp_path / "Runtime/Clean.cs"
    subject.write_text(in_type("            // first\n            // second"), encoding="utf-8")
    assert main(["--repo-root", str(tmp_path), "--fix"]) == 0
    assert "/*" in subject.read_text(encoding="utf-8")
    assert main(["--repo-root", str(tmp_path)]) == 0


@pytest.mark.parametrize("newline", [False, True])
def test_git_discovery_preserves_unicode_and_ignores(tmp_path: Path, newline: bool) -> None:
    if newline and sys.platform == "win32":
        pytest.skip("Windows filenames cannot contain a newline")
    populated(tmp_path)
    ignored = tmp_path / "Generator~/Ignored.cs"
    ignored.write_text("bad", encoding="utf-8")
    separator = "\n" if newline else " "
    untracked = tmp_path / f"Generator~/Untracked ü{separator}name.cs"
    untracked.write_text("good", encoding="utf-8")
    (tmp_path / ".gitignore").write_text("Generator~/Ignored.cs\n", encoding="utf-8")
    git = shutil.which("git")
    assert git is not None
    subprocess.run(
        [git, "init", "-q", str(tmp_path)],
        check=True,
        capture_output=True,
        timeout=15,
    )
    subprocess.run(
        [git, "-C", str(tmp_path), "add", "Runtime", "Editor", "Tests"],
        check=True,
        capture_output=True,
        timeout=15,
    )
    paths = files_to_check(tmp_path, None, fixture=False)
    assert untracked in paths
    assert ignored not in paths
    assert len(paths) == 5


@pytest.mark.parametrize("name", ["Runtime/Missing.cs", "../Outside.cs"])
def test_explicit_missing_or_escaping_paths_fail(tmp_path: Path, name: str) -> None:
    populated(tmp_path)
    assert main(["--repo-root", str(tmp_path), "--paths", name]) == 1


def test_all_inputs_are_read_before_fixing(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    populated(tmp_path)
    first = tmp_path / "Runtime/Clean.cs"
    original = in_type("            // first\n            // second").encode()
    first.write_bytes(original)
    (tmp_path / "Tests/Clean.cs").write_bytes(b"\xff")
    assert main(["--repo-root", str(tmp_path), "--fix"]) == 1
    assert first.read_bytes() == original
    assert "Tests/Clean.cs" in capsys.readouterr().out


def test_linked_input_is_not_rewritten(tmp_path: Path) -> None:
    populated(tmp_path)
    original = in_type("            // first\n            // second").encode()
    target = tmp_path / "outside.cs"
    target.write_bytes(original)
    link = tmp_path / "Runtime/Link.cs"
    try:
        link.symlink_to(target)
    except OSError:
        pytest.skip("Links are unavailable")
    assert main(["--repo-root", str(tmp_path), "--paths", "Runtime/Link.cs", "--fix"]) == 1
    assert target.read_bytes() == original


@pytest.mark.parametrize("legacy", [False, True])
def test_cli_launches_from_foreign_directory(tmp_path: Path, legacy: bool) -> None:
    populated(tmp_path)
    if legacy:
        shell = shutil.which("pwsh")
        if shell is None:
            pytest.skip("PowerShell is unavailable")
        command = [
            shell,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/lint-comment-block-form.ps1"),
            "-RepoRoot",
            str(tmp_path),
            "-Paths",
            "Runtime/Clean.cs",
            "Editor/Clean.cs",
        ]
    else:
        command = [
            sys.executable,
            str(ROOT / "scripts/lint_comment_block_form.py"),
            "--repo-root",
            str(tmp_path),
        ]
    result = subprocess.run(
        command, cwd=tmp_path, capture_output=True, text=True, check=False, timeout=60
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "Runtime/ 1" in result.stdout
    assert "Editor/ 1" in result.stdout


def test_legacy_javascript_test_launcher_runs_selected_control(tmp_path: Path) -> None:
    node = shutil.which("node")
    if node is None:
        pytest.skip("Node is unavailable")
    result = subprocess.run(
        [
            node,
            str(ROOT / "scripts/tests/test-lint-comment-block-form.js"),
            "-k",
            "test_reports_ordinary_runs",
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "9 passed" in result.stdout


@pytest.mark.parametrize("absolute", [False, True])
def test_vendored_source_stays_excluded(tmp_path: Path, absolute: bool) -> None:
    populated(tmp_path)
    path = tmp_path / "Runtime/Utils/SevenZip/Upstream.cs"
    path.parent.mkdir(parents=True)
    path.write_text(in_type("            // first\n            // second"), encoding="utf-8")
    selected = str(path) if absolute else path.relative_to(tmp_path).as_posix()
    assert main(["--repo-root", str(tmp_path), "--paths", selected]) == 0


def test_unknown_arguments_are_rejected() -> None:
    with pytest.raises(SystemExit) as result:
        main(["--unknown-option"])
    assert result.value.code == 2


def test_git_discovery_skips_unstaged_deletions(tmp_path: Path) -> None:
    populated(tmp_path)
    git = shutil.which("git")
    assert git is not None
    subprocess.run([git, "init", "-q", str(tmp_path)], check=True, capture_output=True, timeout=15)
    subprocess.run(
        [git, "-C", str(tmp_path), "add", *ROOTS], check=True, capture_output=True, timeout=15
    )
    deleted = tmp_path / "Runtime/Clean.cs"
    deleted.unlink()
    paths = files_to_check(tmp_path, None, fixture=False)
    assert deleted not in paths
    assert len(paths) == 3
    with pytest.raises((OSError, ValueError)):
        files_to_check(tmp_path, ["Runtime/Clean.cs"], fixture=False)


@pytest.mark.parametrize("bom", [b"", b"\xef\xbb\xbf"])
@pytest.mark.parametrize("newline", ["\n", "\r\n"])
def test_conversion_retains_encoding_marker(tmp_path: Path, bom: bytes, newline: str) -> None:
    populated(tmp_path)
    source = tmp_path / "Runtime/Clean.cs"
    original = in_type("            // first\n            // second").replace("\n", newline)
    source.write_bytes(bom + original.encode("utf-8"))
    assert main(["--repo-root", str(tmp_path), "--paths", "Runtime/Clean.cs", "--fix"]) == 0
    expected = in_type(
        "            /*\n                first\n                second\n            */"
    )
    assert source.read_bytes() == bom + expected.replace("\n", newline).encode("utf-8")


def test_git_discovery_rejects_dangling_links(tmp_path: Path) -> None:
    populated(tmp_path)
    target = tmp_path / "outside.cs"
    target.write_text("content", encoding="utf-8")
    link = tmp_path / "Runtime/Link.cs"
    try:
        link.symlink_to(target)
    except OSError:
        pytest.skip("Links are unavailable")
    git = shutil.which("git")
    assert git is not None
    subprocess.run([git, "init", "-q", str(tmp_path)], check=True, capture_output=True, timeout=15)
    subprocess.run(
        [git, "-C", str(tmp_path), "add", *ROOTS], check=True, capture_output=True, timeout=15
    )
    target.unlink()
    with pytest.raises(ValueError, match="linked"):
        files_to_check(tmp_path, None, fixture=False)


@pytest.mark.parametrize("prefix", ["$@", "@$"])
@pytest.mark.parametrize(
    "expression",
    [
        'Pick("a")',
        'Pick(@"a")',
        'Pick($"{Pick("a")}")',
        'Pick(/* } */ "a")',
        'new { Value = Pick("a") }.Value',
    ],
)
@pytest.mark.parametrize("newline", ["\n", "\r\n"])
def test_interpolated_verbatim_literals_keep_comment_lookalikes(
    prefix: str, expression: str, newline: str
) -> None:
    body = (
        f'            string value = {prefix}"{{{{escaped}}}} {{{expression}}}\n'
        "            // literal first\n"
        "            // literal second\n"
        '            ";\n'
        "            // actual first\n"
        "            // actual second"
    )
    source = in_type(body).replace("\n", newline)
    found = analyze(source)
    assert len(found) == 1
    assert found[0].line == 11
    fixed, declined = rewrite(source, found)
    assert declined == 0
    assert fixed.startswith(source[: source.index("            // actual first")])
    assert "// literal first" + newline + "            // literal second" in fixed
    assert "// actual first" not in fixed
    assert analyze(fixed) == []
