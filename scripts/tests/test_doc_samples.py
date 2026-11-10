"""Cover marked compiler corpora, wrapper scopes, path safety and legacy commands."""

from __future__ import annotations

import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import extract_doc_samples as extractor

ROOT = Path(__file__).resolve().parents[2]


def tree(tmp_path: Path, body: str, *, marker: str = extractor.MARKER) -> tuple[Path, Path]:
    docs = tmp_path / "docs"
    docs.mkdir(exist_ok=True)
    (docs / "page.md").write_text(f"# Page\n\n{marker}\n```csharp\n{body}\n```\n", encoding="utf-8")
    return tmp_path, tmp_path / "out"


@pytest.mark.parametrize(
    ("body", "expected", "forbidden"),
    [
        (
            "public sealed class Example { public int Value; }",
            "class Example",
            "class DocSample",
        ),
        (
            "[WShowIf(nameof(HasShield))]\npublic int Shield;\nprivate bool HasShield;",
            "class DocSample",
            "DocSampleUsage",
        ),
        ("void Awake() { int local = 1; }", "class DocSample", "DocSampleUsage"),
        (
            "int count = 1;\nDebug.Log(count);",
            "private void DocSampleUsage()",
            "IEnumerator DocSampleUsage",
        ),
        (
            "yield return null;",
            "IEnumerator DocSampleUsage()",
            "private void DocSampleUsage",
        ),
        (
            "await System.Threading.Tasks.Task.Yield();",
            "async System.Threading.Tasks.Task DocSampleUsage()",
            "private void DocSampleUsage",
        ),
        ("void Awake() => Debug.Log(1);", "class DocSample", "DocSampleUsage"),
        (
            "return Compute();",
            "private void DocSampleUsage()",
            "IEnumerator DocSampleUsage",
        ),
        (
            "public enum Stance { Idle = 0 }\npublic Stance Current;",
            "class DocSample",
            "DocSampleUsage",
        ),
        (
            "public enum Stance { Idle = 0 }\nStance current = Stance.Idle;",
            "private void DocSampleUsage()",
            "IEnumerator DocSampleUsage",
        ),
        (
            "int local = 1;\nvoid OnDestroy() { Debug.Log(local); }",
            "private void DocSampleUsage()",
            "IEnumerator DocSampleUsage",
        ),
        ("public int Count;\n// trailing", "class DocSample", "DocSampleUsage"),
        (
            'public string Text = "} // done";\npublic int Count;',
            "class DocSample",
            "DocSampleUsage",
        ),
        (
            'public string Text = @"} // done\nmore";\npublic int Count;',
            "class DocSample",
            "DocSampleUsage",
        ),
        (
            'public string Text = """\n} // done\n""";\npublic int Count;',
            "class DocSample",
            "DocSampleUsage",
        ),
        (
            "public sealed class Example { }\nusing System;",
            "class Example",
            "class DocSample",
        ),
    ],
)
def test_wrapper_shapes_preserve_the_authored_source(
    body: str, expected: str, forbidden: str
) -> None:
    sample = extractor.Sample(ROOT / "docs/page.md", 5, tuple(body.split("\n")))
    unit = extractor.render(sample, "S_page_5", ROOT)
    assert expected in unit
    assert forbidden not in unit
    assert "namespace WallstopStudios.UnityHelpers.DocSamples.S_page_5" in unit
    for line in body.splitlines():
        assert line in unit
    assert unit.count("using System;") == 1
    assert unit.index("using System;") < unit.index(expected)


@pytest.mark.parametrize("fence", ["```csharp", "```cs", "~~~~csharp", "````csharp"])
@pytest.mark.parametrize("ending", ["\n", "\r\n", "\r"])
def test_commonmark_fences_and_exact_locations(fence: str, ending: str) -> None:
    close = "~" * 4 if fence.startswith("~") else "`" * (4 if fence.startswith("````") else 3)
    text = ending.join(("# Page", "", extractor.MARKER, "", fence, "public int Value;", close))
    samples, errors = extractor.samples_in(ROOT / "docs/page.md", text, extractor.Counts())
    assert errors == []
    assert len(samples) == 1
    assert samples[0].line == 6
    assert samples[0].body == ("public int Value;",)


def test_marker_inside_other_code_does_not_claim_a_sample() -> None:
    text = "````text\n" + extractor.MARKER + "\n```csharp\nint value = 1;\n```\n````\n"
    samples, problems = extractor.samples_in(ROOT / "docs/page.md", text, extractor.Counts())
    assert samples == []
    assert problems == []


@pytest.mark.parametrize("invalid", ["text", "```text\nwrong\n```", "", "    indented code"])
def test_unbound_marker_is_an_error(invalid: str) -> None:
    _, problems = extractor.samples_in(
        ROOT / "docs/page.md", extractor.MARKER + "\n\n" + invalid, extractor.Counts()
    )
    assert len(problems) == 1
    assert "adds nothing" in problems[0]


@pytest.mark.parametrize(
    "body",
    [
        "// ...",
        "/* ... */",
        "int x; // (existing code)",
        "...",
        "[assembly: Attribute]",
    ],
)
def test_claimed_invalid_samples_do_not_erase_existing_output(tmp_path: Path, body: str) -> None:
    root, output = tree(tmp_path, body)
    output.mkdir()
    retained = output / "user.txt"
    retained.write_bytes(b"preserve")
    assert extractor.extract(root, output, require_both=False) == 1
    assert retained.read_bytes() == b"preserve"
    assert list(output.iterdir()) == [retained]


def test_empty_and_unmarked_corpora_fail_and_count_shapes(tmp_path: Path) -> None:
    root, output = tree(tmp_path, "public int Value;", marker="")
    assert extractor.extract(root, output, require_both=False) == 1
    counts = extractor.Counts()
    text = "```cs\nclass Example {}\n```\n```cs\npublic int Value;\n```\n```cs\nDebug.Log(1);\n```\n```cs\n// comment\n```\n"
    samples, problems = extractor.samples_in(root / "docs/page.md", text, counts)
    assert not samples
    assert not problems
    assert counts == extractor.Counts(1, 1, 1, 1)


def test_runtime_editor_partition_and_both_guards(tmp_path: Path) -> None:
    root, output = tree(tmp_path, "public int RuntimeValue;")
    assert extractor.extract(root, output, require_both=True) == 1
    page = root / "docs/page.md"
    page.write_text(
        page.read_text(encoding="utf-8")
        + "\n"
        + extractor.EDITOR_MARKER
        + "\n```cs\npublic int EditorValue;\n```\n",
        encoding="utf-8",
    )
    assert extractor.extract(root, output, require_both=True) == 0
    assert len(list(output.glob("*.cs"))) == 1
    assert len(list((output / "editor").glob("*.cs"))) == 1
    page.write_text(
        extractor.EDITOR_MARKER + "\n```cs\npublic int EditorValue;\n```\n", encoding="utf-8"
    )
    assert extractor.extract(root, output, require_both=True) == 1


def test_colliding_sanitized_paths_are_refused_before_writes(tmp_path: Path) -> None:
    root, output = tree(tmp_path, "public int Value;")
    body = (root / "docs/page.md").read_bytes()
    (root / "docs/a-b.md").write_bytes(body)
    (root / "docs/a_b.md").write_bytes(body)
    assert extractor.extract(root, output, require_both=False) == 1
    assert not output.exists()


def test_stale_generated_files_removed_but_unrelated_files_preserved(
    tmp_path: Path,
) -> None:
    root, output = tree(tmp_path, "public int Value;")
    assert extractor.extract(root, output, require_both=False) == 0
    original = next(output.glob("*.cs"))
    unrelated = output / "handwritten.cs"
    unrelated.write_text("preserve", encoding="utf-8")
    page = root / "docs/page.md"
    page.write_text("\n" + page.read_text(encoding="utf-8"), encoding="utf-8")
    assert extractor.extract(root, output, require_both=False) == 0
    assert not original.exists()
    assert unrelated.read_text(encoding="utf-8") == "preserve"
    target = next(path for path in output.glob("*.cs") if path != unrelated)
    target.write_text("preserve this too", encoding="utf-8")
    with pytest.raises(ValueError, match="unrelated"):
        extractor.extract(root, output, require_both=False)
    assert target.read_text(encoding="utf-8") == "preserve this too"


@pytest.mark.parametrize("editor_only", [False, True])
@pytest.mark.parametrize("content", [b"\xff unrelated", "unrelated".encode("utf-16")])
def test_cleanup_preserves_unrelated_non_utf8_files(
    tmp_path: Path, editor_only: bool, content: bytes
) -> None:
    root, output = tree(tmp_path, "public int Value;")
    directory = output / "editor" if editor_only else output
    directory.mkdir(parents=True)
    unrelated = directory / "unrelated.cs"
    unrelated.write_bytes(content)
    assert extractor.extract(root, output, require_both=False) == 0
    assert unrelated.read_bytes() == content


@pytest.mark.parametrize("ending", [b"\n", b"\r\n", b"\r"])
def test_cleanup_recognizes_only_the_generated_header(tmp_path: Path, ending: bytes) -> None:
    root, output = tree(tmp_path, "public int Value;")
    output.mkdir()
    stale = output / "stale.cs"
    stale.write_bytes(b"// <auto-generated />" + ending + b"// Extracted from old\xff")
    assert extractor.extract(root, output, require_both=False) == 0
    assert not stale.exists()


@pytest.mark.parametrize("editor_only", [False, True])
def test_rewriting_generated_hard_links_preserves_external_files(
    tmp_path: Path, editor_only: bool
) -> None:
    marker = extractor.EDITOR_MARKER if editor_only else extractor.MARKER
    root, output = tree(tmp_path, "public int Before;", marker=marker)
    assert extractor.extract(root, output, require_both=False) == 0
    target = next(output.rglob("*.cs"))
    foreign = tmp_path / "external.cs"
    foreign.hardlink_to(target)
    original = foreign.read_bytes()
    assert target.samefile(foreign)
    page = root / "docs/page.md"
    page.write_text(page.read_text(encoding="utf-8").replace("Before", "After"), encoding="utf-8")
    assert extractor.extract(root, output, require_both=False) == 0
    assert foreign.read_bytes() == original
    assert "public int After;" in target.read_text(encoding="utf-8")
    assert not target.samefile(foreign)


def test_failed_replacement_preserves_output_and_removes_temporary_files(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    root, output = tree(tmp_path, "public int Before;")
    assert extractor.extract(root, output, require_both=False) == 0
    target = next(output.glob("*.cs"))
    original = target.read_bytes()
    page = root / "docs/page.md"
    page.write_text(page.read_text(encoding="utf-8").replace("Before", "After"), encoding="utf-8")

    def refuse_replacement(source: Path, destination: Path) -> Path:
        assert destination == target
        assert "public int After;" in source.read_text(encoding="utf-8")
        raise OSError("replacement refused")

    monkeypatch.setattr(Path, "replace", refuse_replacement)
    with pytest.raises(OSError, match="replacement refused"):
        extractor.extract(root, output, require_both=False)
    assert target.read_bytes() == original
    assert sorted(path.name for path in output.iterdir()) == [target.name, "editor"]


@pytest.mark.parametrize("kind", ["source", "directory", "output", "generated"])
def test_links_are_refused(tmp_path: Path, kind: str) -> None:
    root, output = tree(tmp_path, "public int Value;")
    foreign = tmp_path / "foreign"
    foreign.mkdir()
    source = foreign / "page.md"
    source.write_text("preserve", encoding="utf-8")
    try:
        if kind == "source":
            (root / "docs/page.md").unlink()
            (root / "docs/page.md").symlink_to(source)
        elif kind == "directory":
            (root / "docs/linked").symlink_to(foreign, target_is_directory=True)
        elif kind == "output":
            output.symlink_to(foreign, target_is_directory=True)
        else:
            output.mkdir()
            (output / "S_docs_page_5.cs").symlink_to(source)
    except OSError:
        pytest.skip("Symlinks unavailable")
    with pytest.raises(ValueError, match=r"[Ll]ink|Unsafe"):
        extractor.extract(root, output, require_both=False)
    assert source.read_text(encoding="utf-8") == "preserve"


@pytest.mark.parametrize("destination", [".", "docs", "docs/generated"])
def test_output_cannot_replace_documentation(tmp_path: Path, destination: str) -> None:
    root, _ = tree(tmp_path, "public int Value;")
    with pytest.raises(ValueError, match="source tree"):
        extractor.extract(root, root / destination, require_both=False)


def test_bom_unicode_and_foreign_working_directory_launcher(tmp_path: Path) -> None:
    root, output = tree(tmp_path, "public int Value;")
    page = root / "docs/page.md"
    page.rename(root / "docs/é space.MD")
    page = root / "docs/é space.MD"
    page.write_text(page.read_text(encoding="utf-8"), encoding="utf-8-sig")
    environment = dict(os.environ, DOC_SAMPLES_ROOT=str(root), DOC_SAMPLES_OUT=str(output))
    node = shutil.which("node")
    assert node is not None
    result = subprocess.run(
        [node, str(ROOT / "scripts/extract-doc-samples.js")],
        env=environment,
        cwd=tmp_path,
        text=True,
        capture_output=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    unit = next(output.glob("*.cs")).read_text(encoding="utf-8")
    assert "docs/é space.MD:5" in unit
    assert "Runtime samples: 1; Editor-only samples: 0" in result.stdout


def test_missing_and_invalid_utf8_inputs_fail(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="root"):
        extractor.extract(tmp_path, tmp_path / "out", require_both=False)
    root, output = tree(tmp_path, "public int Value;")
    (root / "docs/page.md").write_bytes(b"\xff")
    with pytest.raises(UnicodeError):
        extractor.extract(root, output, require_both=False)
    assert not output.exists()


def test_actual_repository_compiler_corpus(tmp_path: Path) -> None:
    output = tmp_path / "samples~"
    assert extractor.extract(ROOT, output, require_both=True) == 0
    assert len(list(output.glob("*.cs"))) >= 349
    assert len(list((output / "editor").glob("*.cs"))) >= 16


@pytest.mark.skipif(
    os.environ.get("DOC_SAMPLES_EDITOR_COMPILER_CONTROL") != "1",
    reason="Explicit compiler control runs in typecheck gates",
)
def test_editor_compiler_control(tmp_path: Path) -> None:
    editor = tmp_path / "editor"
    editor.mkdir()
    (editor / "EditorControl.cs").write_text(
        """namespace WallstopStudios.UnityHelpers.DocSamples.EditorControl
{
    internal static class EditorControl
    {
        internal static readonly System.Type Anchor = typeof(WallstopStudios.UnityHelpers.Editor.Settings.UnityHelpersSettings);
        internal static object Missing() => UnityEditor.Compilation.CompilationPipeline.DefinitelyMissingDocSampleEditorMember();
    }
}
""",
        encoding="utf-8",
    )
    dotnet = shutil.which("dotnet")
    assert dotnet is not None
    result = subprocess.run(
        [
            dotnet,
            "build",
            "Generator~/WallstopStudios.UnityHelpers.DocSamplesCheck/WallstopStudios.UnityHelpers.DocSamplesCheck.csproj",
            "--no-restore",
            "--nologo",
            "-v",
            "quiet",
            "-p:DocSamplesEditor=true",
            "-p:BuildProjectReferences=false",
            f"-p:DocSamplesDir={tmp_path}",
        ],
        cwd=ROOT,
        text=True,
        capture_output=True,
        check=False,
        timeout=60,
    )
    output = result.stdout + result.stderr
    errors = re.findall(r"error (CS\d+):", output)
    assert result.returncode == 1, output
    assert errors, output
    assert set(errors) == {"CS0117"}, output
    assert "DefinitelyMissingDocSampleEditorMember" in output
    print("[doc-samples] Editor compiler control reported CS0117 as expected.")


def test_legacy_suite_launcher_selects_real_python_tests(tmp_path: Path) -> None:
    node = shutil.which("node")
    assert node is not None
    result = subprocess.run(
        [
            node,
            str(ROOT / "scripts/tests/test-extract-doc-samples.js"),
            "-k",
            "marker_inside_other_code",
        ],
        cwd=tmp_path,
        text=True,
        capture_output=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "1 passed" in result.stdout


def test_using_text_inside_a_multiline_literal_stays_in_place() -> None:
    body = 'public string Text = @"\nusing System;\n";'
    unit = extractor.render(
        extractor.Sample(ROOT / "docs/page.md", 1, tuple(body.split("\n"))), "literal", ROOT
    )
    assert body in unit


@pytest.mark.parametrize("literal", ['"..."', '@"\n[assembly: Example]\n"'])
def test_literal_data_does_not_refute_a_compilation_marker(literal: str) -> None:
    body = f"public string Text = {literal};"
    text = extractor.MARKER + "\n```cs\n" + body + "\n```\n"
    samples, problems = extractor.samples_in(ROOT / "docs/page.md", text, extractor.Counts())
    assert problems == []
    assert len(samples) == 1


def test_dot_segments_cannot_bypass_the_documentation_output_boundary(tmp_path: Path) -> None:
    source = tmp_path / "source"
    source.mkdir()
    root, _ = tree(source, "public int Value;")
    extra = tmp_path / "extra"
    extra.mkdir()
    lexical_root = extra / ".." / "source"
    with pytest.raises(ValueError, match="source tree"):
        extractor.extract(lexical_root, root / "docs", require_both=False)
    assert list((root / "docs").glob("*.cs")) == []


def test_main_normalizes_root_before_selecting_corpus_guards(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    source = tmp_path / "source"
    source.mkdir()
    root, output = tree(source, "public int Value;", marker=extractor.EDITOR_MARKER)
    extra = tmp_path / "extra"
    extra.mkdir()
    monkeypatch.setattr(extractor, "ROOT", root)
    monkeypatch.setenv("DOC_SAMPLES_ROOT", str(extra / ".." / "source"))
    monkeypatch.setenv("DOC_SAMPLES_OUT", str(output))
    monkeypatch.delenv("DOC_SAMPLES_REQUIRE_BOTH", raising=False)
    assert extractor.main([]) == 1
    assert not output.exists()


if __name__ == "__main__":
    if sys.argv[1:] == ["--editor-compiler-control"]:
        os.environ["DOC_SAMPLES_EDITOR_COMPILER_CONTROL"] = "1"
        raise SystemExit(pytest.main([__file__, "-k", "editor_compiler_control", "-q", "-s"]))
    raise SystemExit(pytest.main([__file__, *sys.argv[1:]]))
