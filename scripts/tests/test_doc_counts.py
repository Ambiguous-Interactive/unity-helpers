"""Count and synchronization controls over a small physical documentation tree."""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from doc_counts import Counts, collect, display_count, main, sync, synchronized


def corpus(root: Path) -> None:
    for folder in ("Tests", "Runtime/Core/Random", "Editor"):
        (root / folder).mkdir(parents=True)
    (root / "Tests/Cases.cs").write_text(
        "[Test]\n[UnityTest]\n[TestCase(1)]\n[TestCaseSource(nameof(Cases))]\n[TestFixture]\n",
        encoding="utf-8",
    )
    (root / "Runtime/Core/Random/Example.cs").write_text(
        "public sealed class Example : AbstractRandom {}", encoding="utf-8"
    )
    (root / "Runtime/Core/Random/RandomComparer.cs").write_text(
        "public class RandomComparer : IRandom {}", encoding="utf-8"
    )
    (root / "Editor/Window.cs").write_text(
        'public class Window : EditorWindow { [MenuItem("Window/Example")] void Show() {} }',
        encoding="utf-8",
    )
    (root / "Editor/Menu.cs").write_text('[MenuItem("Tools/Example")]', encoding="utf-8")
    (root / "README.md").write_text(
        "9,000+ tests; 99 PRNG implementations; 40+ editor tools.\n", encoding="utf-8"
    )


@pytest.mark.parametrize(
    ("exact", "expected"),
    [
        (0, "0+"),
        (1, "1+"),
        (4, "4+"),
        (5, "5+"),
        (19, "15+"),
        (20, "20+"),
        (99, "90+"),
        (100, "100+"),
        (999, "900+"),
        (1000, "1,000+"),
        (28912, "28,000+"),
    ],
)
def test_rounding(exact: int, expected: str) -> None:
    assert display_count(exact) == expected


def test_counts_cover_every_kind_and_count_each_tool_file_once(tmp_path: Path) -> None:
    corpus(tmp_path)
    assert collect(tmp_path) == Counts(4, 1, 2)


def test_missing_source_root_is_failure(tmp_path: Path) -> None:
    corpus(tmp_path)
    (tmp_path / "Editor/Menu.cs").unlink()
    (tmp_path / "Editor/Window.cs").unlink()
    (tmp_path / "Editor").rmdir()
    assert main(["check", "--repo-root", str(tmp_path)]) == 1


def test_check_is_read_only_and_sync_has_exactly_one_final_lf(tmp_path: Path) -> None:
    corpus(tmp_path)
    readme = tmp_path / "README.md"
    readme.write_bytes(b"9,000+ tests\r\n\r\n")
    original = readme.read_bytes()
    counts = collect(tmp_path)
    assert sync(tmp_path, counts, check=True) == 1
    assert readme.read_bytes() == original
    assert sync(tmp_path, counts, check=False) == 0
    assert readme.read_bytes() == b"4+ tests\n"
    assert sync(tmp_path, counts, check=True) == 0


@pytest.mark.parametrize(
    "wording",
    [
        "99 PRNG implementations",
        "99 high-performance PRNG implementations",
        "99 high-performance pseudo-random number generators",
        "99 high-quality RNG implementations",
        "99 high-quality PRNGs",
        "99 high-quality random number generators",
    ],
)
def test_random_wordings(wording: str) -> None:
    assert synchronized(wording, Counts(12345, 17, 29)).startswith("15+")


@pytest.mark.parametrize(
    "wording",
    [
        "99+ editor tools",
        "99+ automation tools",
        "99+ tools for sprites",
        "99+ tools that help",
        "99+ sprite/texture/prefab utilities",
    ],
)
def test_tool_wordings(wording: str) -> None:
    assert synchronized(wording, Counts(12345, 17, 29)).startswith("20+")


def test_only_explicit_targets_change(tmp_path: Path) -> None:
    corpus(tmp_path)
    unrelated = tmp_path / "notes.md"
    unrelated.write_text("99,000+ tests", encoding="utf-8")
    assert main(["sync", "--repo-root", str(tmp_path)]) == 0
    assert unrelated.read_text(encoding="utf-8") == "99,000+ tests"


def test_no_documentation_is_not_a_pass(tmp_path: Path) -> None:
    corpus(tmp_path)
    (tmp_path / "README.md").unlink()
    assert main(["check", "--repo-root", str(tmp_path)]) == 1


def test_generate_json_has_independent_counts_and_clean_stdout(tmp_path: Path) -> None:
    corpus(tmp_path)
    script = Path(__file__).resolve().parents[1] / "doc_counts.py"
    result = subprocess.run(
        [sys.executable, str(script), "generate", "--repo-root", str(tmp_path)],
        cwd=tmp_path.parent,
        capture_output=True,
        text=True,
        check=False,
    )
    assert result.returncode == 0
    data = json.loads(result.stdout)
    assert data["testCount"] == {"exact": 4, "display": "4+"}
    assert data["prngCount"] == {"exact": 1, "display": "1+"}
    assert data["editorToolCount"] == {"exact": 2, "display": "2+"}
    assert "[doc-metadata]" in result.stderr


def test_malformed_source_encoding_refuses_counting(tmp_path: Path) -> None:
    corpus(tmp_path)
    (tmp_path / "Tests/Bad.cs").write_bytes(b"\xff\xff[Test]")
    assert main(["generate", "--repo-root", str(tmp_path)]) == 1


@pytest.mark.parametrize("encoding", ["utf-8-sig", "utf-16", "utf-32"])
@pytest.mark.parametrize("ending", ["\n", "\r\n", "\r"])
def test_bom_and_line_endings_preserve_counts(tmp_path: Path, encoding: str, ending: str) -> None:
    corpus(tmp_path)
    (tmp_path / "Tests/Cases.cs").write_bytes(
        (ending.join(("[Test]", "[UnityTest]", "[TestCase(1)]")) + ending).encode(encoding)
    )
    assert collect(tmp_path) == Counts(3, 1, 2)


def test_sources_are_counted_recursively(tmp_path: Path) -> None:
    corpus(tmp_path)
    nested = tmp_path / "Tests/Nested"
    nested.mkdir()
    (nested / "More.cs").write_text("[TestCase(1)]\n[TestCase(2)]\n", encoding="utf-8")
    assert collect(tmp_path) == Counts(6, 1, 2)


def test_documentation_directory_is_an_error(tmp_path: Path) -> None:
    corpus(tmp_path)
    (tmp_path / "README.md").unlink()
    (tmp_path / "README.md").mkdir()
    assert main(["sync", "--repo-root", str(tmp_path)]) == 1


def test_later_invalid_document_does_not_partially_rewrite_earlier_targets(
    tmp_path: Path,
) -> None:
    corpus(tmp_path)
    original = (tmp_path / "README.md").read_bytes()
    (tmp_path / "llms.txt").write_bytes(b"\xff\xff")
    assert main(["sync", "--repo-root", str(tmp_path)]) == 1
    assert (tmp_path / "README.md").read_bytes() == original


@pytest.mark.parametrize("within_repository", [False, True])
@pytest.mark.parametrize("mode", ["check", "sync"])
def test_linked_parent_cannot_redirect_a_documentation_write(
    tmp_path: Path, within_repository: bool, mode: str, capsys: pytest.CaptureFixture[str]
) -> None:
    root = tmp_path / "repository"
    corpus(root)
    outside = (root if within_repository else tmp_path) / "outside"
    outside.mkdir()
    target = outside / "readme.md"
    target.write_text("99,000+ tests", encoding="utf-8")
    try:
        (root / "docs").symlink_to(outside, target_is_directory=True)
    except OSError:
        pytest.skip("This host cannot create directory symlinks.")
    original = (root / "README.md").read_bytes()
    assert main([mode, "--repo-root", str(root)]) == 1
    assert "linked" in capsys.readouterr().err
    assert target.read_text(encoding="utf-8") == "99,000+ tests"
    assert (root / "README.md").read_bytes() == original


@pytest.mark.parametrize(
    "noise",
    [
        '// [MenuItem("Tools/Ghost")]\n/*\n[Test]\nclass Ghost : IRandom {}\n*/',
        'string example = @"\n[Test]\n[MenuItem(""Tools/Ghost"")]\nclass Ghost : IRandom {}\n";',
        'string example = """\n[Test]\n[MenuItem("Tools/Ghost")]\nclass Ghost : IRandom {}\n""";',
    ],
)
def test_comments_and_literals_do_not_change_source_counts(tmp_path: Path, noise: str) -> None:
    corpus(tmp_path)
    for folder in ("Tests", "Runtime/Core/Random", "Editor"):
        (tmp_path / folder / "Noise.cs").write_text(noise, encoding="utf-8")
    assert collect(tmp_path) == Counts(4, 1, 2)
