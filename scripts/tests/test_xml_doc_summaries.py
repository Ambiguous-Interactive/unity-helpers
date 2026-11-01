"""Keep XML summary detection, filesystem coverage and both CLI routes executable."""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import lint_xml_doc_summaries as lint

ORPHANED = "class Widget {\n/// <summary>Stale.</summary>\n/// <summary>Live.</summary>\npublic void Draw() {}\n}"
SEPARATE = "/// <summary>First.</summary>\nvoid A() {}\n/// <summary>Second.</summary>\nvoid B() {}"


@pytest.mark.parametrize("separator", ["\n", "\r", "\r\n"])
def test_orphan_reports_first_line_and_count(separator: str) -> None:
    assert lint.analyze_file(ORPHANED.replace("\n", separator)) == [
        lint.Violation(2, 2, "/// <summary>Stale.</summary>")
    ]


@pytest.mark.parametrize(
    "interior",
    [
        "[Obsolete]",
        "#if UNITY_EDITOR\n#endif",
        "[Obsolete]\n#pragma warning disable CS0618",
    ],
)
def test_attributes_and_directives_remain_in_member(interior: str) -> None:
    source = f"/// <summary>Old.</summary>\n{interior}\n/// <summary>New.</summary>\nvoid A() {{}}"
    assert lint.analyze_file(source) == [lint.Violation(1, 2, "/// <summary>Old.</summary>")]


@pytest.mark.parametrize(
    "source",
    [
        SEPARATE,
        "class Widget {}",
        "/// <summary>Only one.</summary>\nvoid A() {}",
        "/// <summary/>\n/// <summary />\nvoid A() {}",
        'string example = @"\n/// <summary>A</summary>\n/// <summary>B</summary>";',
        'string example = @"""quoted""\n/// <summary>A</summary>\n/// <summary>B</summary>";',
        "/*\n/// <summary>A</summary>\n/// <summary>B</summary>\n*/",
        'string value = "/// <summary>A</summary> <summary>B</summary>";',
        "/// <summary>\n/// <code>\n/// <summary>Example.</summary>\n/// </code>\n/// </summary>\nvoid A() {}",
        "/// <summary>\n/// <code>\n/// &lt;summary&gt;Example.&lt;/summary&gt;\n/// </code>\n/// </summary>\nvoid A() {}",
        "/// <summary>A</summary>\n\n/// <summary>B</summary>\nvoid A() {}",
    ],
)
def test_samples_and_separate_members_are_clean(source: str) -> None:
    assert lint.analyze_file(source) == []


def test_multiple_openings_on_one_line() -> None:
    assert lint.analyze_file("/// <summary>A</summary> <summary>B</summary>\nvoid A() {}") == [
        lint.Violation(1, 2, "/// <summary>A</summary> <summary>B</summary>")
    ]


@pytest.mark.parametrize("legacy", [False, True])
@pytest.mark.parametrize(("content", "expected"), [(ORPHANED, 1), (SEPARATE, 0), ("", 0)])
def test_real_cli_reports_sources(
    tmp_path: Path, legacy: bool, content: str, expected: int
) -> None:
    source = tmp_path / "Widget.cs"
    source.write_text(content, encoding="utf-8")
    if legacy:
        node = shutil.which("node")
        assert node is not None
        command = [node, str(lint.ROOT / "scripts/lint-xml-doc-summaries.js")]
    else:
        command = [sys.executable, str(lint.ROOT / "scripts/lint_xml_doc_summaries.py")]
    result = subprocess.run(
        [*command, "--verbose"],
        cwd=tmp_path,
        env={**os.environ, "XML_DOC_SUMMARY_ROOTS": str(tmp_path)},
        text=True,
        encoding="utf-8",
        capture_output=True,
        check=False,
        timeout=30,
    )
    assert result.returncode == expected, result.stderr
    assert "Widget.cs:2" in result.stderr if expected else "1 file(s) clean" in result.stdout


@pytest.mark.parametrize("kind", ["empty", "missing", "excluded", "invalid-utf8"])
def test_absent_or_unreadable_subjects_fail(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    kind: str,
) -> None:
    root = tmp_path
    if kind == "missing":
        root = root / "missing"
    elif kind == "excluded":
        root = tmp_path / "bin"
        root.mkdir()
        (root / "Ignored.cs").write_text(ORPHANED, encoding="utf-8")
        root = tmp_path
    elif kind == "invalid-utf8":
        (root / "Broken.cs").write_bytes(b"\xff")
    monkeypatch.setenv("XML_DOC_SUMMARY_ROOTS", str(root))
    assert lint.main([]) == 1
    assert capsys.readouterr().err


def test_only_excluded_vendored_sources_fail(tmp_path: Path) -> None:
    vendor = tmp_path / "Runtime/Utils/SevenZip"
    vendor.mkdir(parents=True)
    (vendor / "Vendor.cs").write_text(ORPHANED, encoding="utf-8")
    assert lint.collect_files(["Runtime"], tmp_path) == []
    adjacent = vendor.with_name("SevenZipExtra")
    adjacent.mkdir()
    subject = adjacent / "Owned.cs"
    subject.write_text(ORPHANED, encoding="utf-8")
    assert lint.collect_files(["Runtime"], tmp_path) == [subject]


def test_overlapping_roots_are_counted_once(tmp_path: Path) -> None:
    child = tmp_path / "child"
    child.mkdir()
    source = child / "Subject.cs"
    source.write_text(SEPARATE, encoding="utf-8")
    assert lint.collect_files([str(tmp_path), str(child)]) == [source]


def test_foreign_drive_diagnostic_uses_absolute_path(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    def foreign_drive(*_arguments: object) -> str:
        raise ValueError("different drives")

    monkeypatch.setattr(os.path, "relpath", foreign_drive)
    assert lint.display_path(Path(lint.__file__)) == Path(lint.__file__).as_posix()


@pytest.mark.parametrize(
    ("sample", "expected_count"),
    [
        ("/// <code><summary>Example.</summary></code>", 0),
        ("/// <code>\n/// <summary>Example.</summary></code><summary>Another API.</summary>", 2),
        (
            "/// <CODE language='xml'><summary>Example.</summary></CODE><code/><summary>Another API.</summary>",
            2,
        ),
        ("/// <code><summary>One.</summary></code><code><summary>Two.</summary></code>", 0),
    ],
)
def test_inline_code_samples_neither_add_nor_hide_member_summaries(
    sample: str, expected_count: int
) -> None:
    source = "/// <summary>API.</summary>\n" + sample + "\nvoid A() {}"
    expected = (
        [lint.Violation(1, expected_count, "/// <summary>API.</summary>")] if expected_count else []
    )
    assert lint.analyze_file(source) == expected
