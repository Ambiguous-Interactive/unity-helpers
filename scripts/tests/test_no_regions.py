"""Exercise directive syntax, lossless repair, Git inventory and index isolation."""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from lint_no_regions import CI_VARIABLES, ROOTS, directives, main, rewrite, sources

ROOT = Path(__file__).resolve().parents[2]


def git(root: Path, *arguments: str) -> bytes:
    executable = shutil.which("git")
    assert executable is not None
    return subprocess.run(
        [executable, "-C", str(root), *arguments],
        check=True,
        capture_output=True,
        timeout=30,
    ).stdout


@pytest.fixture
def repository(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> Path:
    for name in CI_VARIABLES:
        monkeypatch.delenv(name, raising=False)
    git(tmp_path, "init", "--quiet")
    for name in ROOTS:
        (tmp_path / name).mkdir()
        (tmp_path / name / "Clean.cs").write_text("class Clean {}\n", encoding="utf-8")
    return tmp_path


@pytest.mark.parametrize("keyword", ["region", "endregion"])
@pytest.mark.parametrize("spacing", ["", " ", "\t"])
def test_only_actual_directives_are_removed(keyword: str, spacing: str) -> None:
    text = f"class C {{\n  #{spacing}{keyword} label\n}}\n"
    found = directives(text)
    assert found == [(10, len(text) - 2, 2)]
    assert rewrite(text, found) == "class C {\n}\n"


@pytest.mark.parametrize(
    "text",
    [
        "// #region comment\n",
        "/*\n#region comment\n#endregion\n*/\n",
        'string s = @"\n#region data\n#endregion\n";',
        'string s = $@"\n#region data\n#endregion\n";',
        'string s = """\n#region data\n#endregion\n""";',
        'string s = $$"""\n#region data\n#endregion\n""";',
        "#regioned not_a_directive\n",
        'string s = "#region data";',
        "char c = '#'; // region\n",
        "#\nregion invalid\n",
        "#\r\nendregion invalid\n",
    ],
)
def test_literals_comments_and_prefixes_are_preserved(text: str) -> None:
    assert directives(text) == []
    assert rewrite(text, directives(text)) == text


@pytest.mark.parametrize("newline", ["\n", "\r\n", "\r", "\x85", "\u2028", "\u2029"])
@pytest.mark.parametrize("bom", [False, True])
@pytest.mark.parametrize("final", [False, True])
def test_fix_preserves_encoding_newlines_and_literal_contents(
    repository: Path, newline: str, *, bom: bool, final: bool
) -> None:
    text = 'class C {\n#region real\nstring s = @"\n#region data\n";\n#endregion\n}'
    text = text.replace("\n", newline) + (newline if final else "")
    encoding = "utf-8-sig" if bom else "utf-8"
    path = repository / "Tests" / "Subject.cs"
    path.write_bytes(text.encode(encoding))
    assert main(["--repo-root", str(repository), "--paths", "Tests/Subject.cs", "--fix"]) == 0
    expected = text.replace(f"#region real{newline}", "").replace(f"#endregion{newline}", "")
    assert path.read_bytes() == expected.encode(encoding)


def test_fix_reads_all_inputs_before_writing(repository: Path) -> None:
    first = repository / "Tests" / "First.cs"
    original = b"#region real\nclass C {}\n#endregion\n"
    first.write_bytes(original)
    (repository / "Tests" / "Last.cs").write_bytes(b"\xff")
    assert main(["--repo-root", str(repository), "--fix"]) == 1
    assert first.read_bytes() == original


def test_staged_mode_uses_index_instead_of_worktree(repository: Path) -> None:
    path = repository / "Tests" / "Subject.cs"
    path.write_text("#region bad\n#endregion\n", encoding="utf-8")
    git(repository, "add", "--", "Tests/Subject.cs")
    path.write_text("class Clean {}", encoding="utf-8")
    assert main(["--repo-root", str(repository), "--staged-only"]) == 1
    index = git(repository, "show", ":Tests/Subject.cs")
    assert main(["--repo-root", str(repository), "--staged-only", "--fix"]) == 1
    assert git(repository, "show", ":Tests/Subject.cs") == index
    assert path.read_text(encoding="utf-8") == "class Clean {}"


def test_staged_clean_blob_is_not_failed_by_unstaged_regions(repository: Path) -> None:
    path = repository / "Tests" / "Subject.cs"
    path.write_text("class Clean {}", encoding="utf-8")
    git(repository, "add", "--", "Tests/Subject.cs")
    path.write_text("#region unstaged\n", encoding="utf-8")
    assert main(["--repo-root", str(repository), "--staged-only"]) == 0


def test_empty_staged_selection_is_allowed(repository: Path) -> None:
    assert sources(repository, None, staged=True) == []
    assert main(["--repo-root", str(repository), "--staged-only"]) == 0


def test_ignored_and_deleted_files_do_not_hide_untracked_sources(
    repository: Path,
) -> None:
    (repository / ".gitignore").write_text("Tests/Ignored.cs\n", encoding="utf-8")
    (repository / "Tests" / "Ignored.cs").write_bytes(b"\xff")
    path = repository / "Tests" / "Deleted.cs"
    path.write_text("class Deleted {}", encoding="utf-8")
    git(repository, "add", "--", "Tests/Deleted.cs")
    path.unlink()
    (repository / "Tests" / "Unicode-雪.cs").write_text("#region bad", encoding="utf-8")
    assert main(["--repo-root", str(repository)]) == 1
    names = [name for name, _ in sources(repository, None, staged=False)]
    assert "Tests/Unicode-雪.cs" in names
    assert "Tests/Ignored.cs" not in names
    assert "Tests/Deleted.cs" not in names


@pytest.mark.parametrize("name", ["Tests/Missing.cs", "../Outside.cs", "Tests/Clean.txt"])
def test_bad_explicit_paths_fail(repository: Path, name: str) -> None:
    assert main(["--repo-root", str(repository), "--paths", name]) == 1


@pytest.mark.parametrize("missing", ROOTS)
def test_missing_source_root_fails(repository: Path, missing: str) -> None:
    shutil.rmtree(repository / missing)
    assert main(["--repo-root", str(repository)]) == 1


def test_empty_corpus_fails(repository: Path) -> None:
    for path in repository.glob("*/*.cs"):
        path.unlink()
    assert main(["--repo-root", str(repository)]) == 1


def test_links_are_refused(repository: Path) -> None:
    path = repository / "Tests" / "Linked.cs"
    try:
        path.symlink_to(repository / "Runtime" / "Clean.cs")
    except OSError:
        pytest.skip("The host cannot create file symlinks")
    assert main(["--repo-root", str(repository), "--paths", "Tests/Linked.cs", "--fix"]) == 1


def test_ci_does_not_mutate_sources(repository: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("CI", "true")
    path = repository / "Tests" / "Subject.cs"
    path.write_bytes(b"#region real\n")
    assert main(["--repo-root", str(repository), "--fix"]) == 1
    assert path.read_bytes() == b"#region real\n"


@pytest.mark.skipif(os.name == "nt", reason="Windows does not permit newline filenames")
def test_command_like_newline_path_does_not_inject_annotations(
    repository: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    name = "Tests/newline\n::error::injected.cs"
    (repository / name).write_text("#region actual", encoding="utf-8")
    assert main(["--repo-root", str(repository), "--paths", name]) == 1
    output = capsys.readouterr().out
    assert "\n::error::injected" not in output
    assert "%0A%3A%3Aerror%3A%3Ainjected.cs:1:" in output


def test_production_cli_is_stdlib_only_and_works_from_a_foreign_directory(
    repository: Path, tmp_path: Path
) -> None:
    result = subprocess.run(
        [
            sys.executable,
            "-S",
            str(ROOT / "scripts/lint_no_regions.py"),
            "--repo-root",
            str(repository),
            "--verbose",
        ],
        cwd=tmp_path.parent,
        capture_output=True,
        text=True,
        check=False,
        timeout=30,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "Checked 4 C# files" in result.stdout


def test_powershell_launcher_forwards_multiple_literal_paths(
    repository: Path, tmp_path: Path
) -> None:
    executable = shutil.which("pwsh")
    if executable is None:
        pytest.skip("PowerShell is not installed")
    (repository / "Tests" / "Bad.cs").write_text("#region bad", encoding="utf-8")
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/lint-no-regions.ps1"),
            "-RepoRoot",
            str(repository),
            "-Paths",
            "Tests/Clean.cs",
            "Tests/Bad.cs",
            "-VerboseOutput",
        ],
        cwd=tmp_path.parent,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 1, result.stdout + result.stderr
    assert "Tests/Bad.cs:1:" in result.stdout
    assert "Checked 2 C# files" in result.stdout


@pytest.mark.skipif(os.name == "nt", reason="Windows forbids literal backslashes in filenames")
@pytest.mark.parametrize("fix", [False, True])
def test_discovered_git_backslash_names_are_literal(repository: Path, fix: bool) -> None:
    path = repository / "Tests" / "Literal\\Name.cs"
    path.write_bytes(b"#region actual\nclass C {}\n#endregion\n")
    arguments = ["--repo-root", str(repository)]
    if fix:
        arguments.append("--fix")
    assert main(arguments) == (0 if fix else 1)
    expected = b"class C {}\n" if fix else b"#region actual\nclass C {}\n#endregion\n"
    assert path.read_bytes() == expected


@pytest.mark.parametrize("newline", ["\n", "\r\n", "\r", "\x85", "\u2028", "\u2029"])
def test_repair_preserves_code_with_every_csharp_line_ending(newline: str) -> None:
    source = newline.join(["#region actual", "class C {}", "#endregion", ""])
    found = directives(source)
    assert rewrite(source, found) == newline.join(["class C {}", ""])
    assert [line for _, _, line in found] == [1, 3]


@pytest.mark.parametrize("newline", ["\n", "\r\n", "\r", "\x85", "\u2028", "\u2029"])
def test_directive_keyword_cannot_cross_a_csharp_line_ending(newline: str) -> None:
    prefix = "#" + newline + "region invalid" + newline
    source = prefix + newline.join(["#region actual", "class C {}", "#endregion", ""])
    assert rewrite(source, directives(source)) == prefix + "class C {}" + newline
