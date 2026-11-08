"""Exercise extractor parsing, source locations, generation, and CLI ownership."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import extract_code_samples as extractor

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/extract_code_samples.py"


@pytest.mark.parametrize("ending", ["\n", "\r\n", "\r"])
def test_multiple_blocks_report_actual_opening_lines(ending: str) -> None:
    lines = [
        "# Examples",
        "```csharp",
        "public class First { }",
        "```",
        "",
        "Some text.",
        "```json",
        "{}",
        "```",
        "",
        "```cs",
        "public struct Last { }",
        "```",
    ]
    samples = extractor.extract_code_blocks(ending.join(lines), Path("examples.md"))
    assert [(sample.language, sample.line_number) for sample in samples] == [
        ("csharp", 2),
        ("json", 7),
        ("cs", 11),
    ]
    assert samples[-1].code == "public struct Last { }\n"


@pytest.mark.parametrize(
    ("code", "complete", "snippet"),
    [
        ("public class Example { }", True, False),
        ("public struct Example { }", True, False),
        ("public interface IExample { }", True, False),
        ("using System;\nvoid Run() { }", True, False),
        ("value = 1;", False, True),
        ("value.Run();", False, True),
        ("// ...\npublic class Example { }", False, False),
        ("nothing useful", False, False),
    ],
)
def test_classification_preserves_generation_policy(
    code: str, complete: bool, snippet: bool
) -> None:
    assert extractor.is_complete_sample(code, include_partial=False) is complete
    assert extractor.is_standalone_snippet(code) is snippet


def test_partial_option_changes_only_completeness() -> None:
    code = "public class Example { /* ... */ }"
    assert not extractor.is_complete_sample(code, include_partial=False)
    assert extractor.is_complete_sample(code, include_partial=True)


def test_wrapper_preserves_existing_imports_and_class() -> None:
    code = "using System;\npublic class Example { }\n"
    assert extractor.wrap_in_compilable_class(code, "Unused") == code
    without_imports = "public class Example { }\n"
    assert extractor.wrap_in_compilable_class(without_imports, "Unused") == (
        extractor.COMMON_USINGS + "\n" + without_imports
    )
    wrapped = extractor.wrap_in_compilable_class("value.Run();\n", "A-B")
    assert "class A_B : MonoBehaviour" in wrapped
    assert "        value.Run();" in wrapped
    assert "private void DocumentationExample()" in wrapped


def test_walk_is_sorted_and_prunes_excluded_directories(tmp_path: Path) -> None:
    for relative in [
        "z.md",
        "nested/B.MD",
        "nested/a.md",
        "artifacts/ignored.md",
        ".git/ignored.md",
    ]:
        path = tmp_path / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("content", encoding="utf-8")
    assert [
        path.relative_to(tmp_path).as_posix() for path in extractor.markdown_files(tmp_path)
    ] == [
        "z.md",
        "nested/B.MD",
        "nested/a.md",
    ]


def test_walk_does_not_follow_directory_symlinks(tmp_path: Path) -> None:
    other = tmp_path / "other"
    other.mkdir()
    (other / "example.md").write_text("example", encoding="utf-8")
    link = tmp_path / "linked"
    try:
        link.symlink_to(other, target_is_directory=True)
    except OSError:
        pytest.skip("This host cannot create directory symlinks.")
    assert extractor.markdown_files(tmp_path) == [other / "example.md"]


def test_missing_root_and_failed_walk_refuse_empty_success(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="Scan root"):
        extractor.markdown_files(tmp_path / "missing")
    with pytest.raises(PermissionError):
        extractor.raise_walk_error(PermissionError("blocked"))


def run_cli(
    root: Path, *arguments: str, launcher: Path = SCRIPT
) -> subprocess.CompletedProcess[str]:
    executable = shutil.which("node") if launcher.suffix == ".js" else sys.executable
    assert executable is not None
    environment = dict(os.environ, CODE_SAMPLES_ROOT=str(root))
    return subprocess.run(
        [
            executable,
            str(launcher),
            "--output-dir",
            str(root / "artifacts"),
            *arguments,
        ],
        cwd=root.parent,
        env=environment,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )


def read_report(root: Path) -> extractor.ExtractionReport:
    return cast(
        "extractor.ExtractionReport",
        json.loads((root / "artifacts/extraction-report.json").read_text(encoding="utf-8")),
    )


@pytest.mark.parametrize("mode", ["--extract-only", "--verbose"])
def test_cli_reports_and_generates_csharp_only(tmp_path: Path, mode: str) -> None:
    source = tmp_path / "docs/examples.md"
    source.parent.mkdir()
    source.write_text(
        "# Examples\n```json\n{}\n```\n\n```cs\npublic class First { }\n```\n"
        "\n```csharp\nvalue.Run();\n```\n\n```csharp\n...\n```\n",
        encoding="utf-8",
    )
    result = run_cli(tmp_path, mode)
    assert result.returncode == 0, result.stdout + result.stderr
    report = read_report(tmp_path)
    assert (
        report["totalFiles"],
        report["totalSamples"],
        report["completeSamples"],
    ) == (1, 3, 1)
    assert report["partialSamples"] == 2
    assert [sample["line"] for sample in report["samples"]] == [6, 10, 14]
    assert [sample["language"] for sample in report["samples"]] == [
        "cs",
        "csharp",
        "csharp",
    ]
    files = list((tmp_path / "artifacts").glob("*.cs"))
    assert len(files) == (0 if mode == "--extract-only" else 2)
    assert sum(sample["generatedFile"] is not None for sample in report["samples"]) == len(files)
    if mode == "--verbose":
        assembly = json.loads(next((tmp_path / "artifacts").glob("*.asmdef")).read_text())
        assert assembly["includePlatforms"] == ["Editor"]
        assert assembly["autoReferenced"] is False


@pytest.mark.parametrize("content", ["prose only", "```json\n{}\n```\n"])
def test_empty_csharp_corpus_fails_but_retains_report(tmp_path: Path, content: str) -> None:
    (tmp_path / "README.md").write_text(content, encoding="utf-8")
    result = run_cli(tmp_path, "--extract-only")
    assert result.returncode == 1
    assert "No C# code samples" in result.stderr
    assert read_report(tmp_path)["totalSamples"] == 0


def test_generation_refuses_colliding_names_before_writes(tmp_path: Path) -> None:
    samples = [
        extractor.CodeSample("cs", "value.Run();", tmp_path / name, 1, "", is_snippet=True)
        for name in ["a-b.md", "a_b.md"]
    ]
    # The first pair deliberately remains distinct; punctuation maps to the same basename below.
    assert extractor.sample_name(samples[0].file_path, 1) != extractor.sample_name(
        samples[1].file_path, 1
    )
    samples[0].file_path = tmp_path / "a.b.md"
    output = tmp_path / "generated"
    with pytest.raises(ValueError, match="collision"):
        extractor.generate_compilable_files(samples, output)
    assert not output.exists()


def test_node_compatibility_launcher_from_foreign_directory(tmp_path: Path) -> None:
    (tmp_path / "README.md").write_text("```cs\npublic class Example {}\n```", encoding="utf-8")
    result = run_cli(tmp_path, "--extract-only", launcher=ROOT / "scripts/extract-code-samples.js")
    assert result.returncode == 0, result.stdout + result.stderr
    assert read_report(tmp_path)["totalSamples"] == 1


def test_cli_missing_argument_is_an_error(tmp_path: Path) -> None:
    result = run_cli(tmp_path, "--scan-root")
    assert result.returncode == 2
    assert "expected one argument" in result.stderr


def test_default_repository_scan_retains_real_csharp_subjects(tmp_path: Path) -> None:
    output = tmp_path / "artifacts"
    assert extractor.main(["--extract-only", "--output-dir", str(output)]) == 0
    report = cast(
        "extractor.ExtractionReport",
        json.loads((output / "extraction-report.json").read_text()),
    )
    assert report["totalFiles"] > 0
    assert report["totalSamples"] > 0
    assert report["completeSamples"] > 0


def test_powershell_compatibility_launcher_preserves_filter(tmp_path: Path) -> None:
    shell = shutil.which("pwsh")
    if shell is None:
        pytest.skip("PowerShell compatibility requires pwsh.")
    result = subprocess.run(
        [
            shell,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/tests/test-extract-code-samples.ps1"),
            "-TestFilter",
            "partial_option_changes_only_completeness",
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "1 passed" in result.stdout


def test_repository_inventory_ignores_local_artifacts_and_keeps_untracked_docs(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    git = shutil.which("git")
    assert git is not None
    subprocess.run(
        [git, "init", "--quiet", str(tmp_path)],
        check=True,
        capture_output=True,
        timeout=10,
    )
    (tmp_path / ".gitignore").write_text("ignored/\nartifacts/\n", encoding="utf-8")
    ignored = tmp_path / "ignored"
    ignored.mkdir()
    (ignored / "invalid.md").write_bytes(b"\xff")
    (tmp_path / "café.Md").write_text("```cs\npublic class Example {}\n```", encoding="utf-8")
    monkeypatch.setattr(extractor, "ROOT", tmp_path)
    assert extractor.markdown_files(tmp_path) == [tmp_path / "café.Md"]
    assert (
        extractor.main(
            [
                "--scan-root",
                str(tmp_path),
                "--extract-only",
                "--output-dir",
                str(tmp_path / "artifacts"),
            ]
        )
        == 0
    )
    assert read_report(tmp_path)["totalSamples"] == 1


def test_repository_inventory_failure_is_not_an_empty_success(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr(extractor, "ROOT", tmp_path)
    with pytest.raises(ValueError, match="Cannot inventory"):
        extractor.markdown_files(tmp_path)
    assert (
        extractor.main(
            [
                "--scan-root",
                str(tmp_path),
                "--extract-only",
                "--output-dir",
                str(tmp_path / "artifacts"),
            ]
        )
        == 1
    )
    assert not (tmp_path / "artifacts").exists()


def test_file_symlinks_are_not_read(tmp_path: Path) -> None:
    actual = tmp_path / "actual.txt"
    actual.write_text("content", encoding="utf-8")
    link = tmp_path / "linked.md"
    try:
        link.symlink_to(actual)
    except OSError:
        pytest.skip("This host cannot create file symlinks.")
    assert extractor.markdown_files(tmp_path) == []


def test_cached_repository_paths_do_not_follow_replaced_directory_links(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    git = shutil.which("git")
    assert git is not None
    subprocess.run([git, "init", "--quiet", str(tmp_path)], check=True, timeout=10)
    docs = tmp_path / "docs"
    docs.mkdir()
    (docs / "sample.md").write_text("owned", encoding="utf-8")
    subprocess.run([git, "-C", str(tmp_path), "add", "docs/sample.md"], check=True, timeout=10)
    ignored = tmp_path / "ignored"
    ignored.mkdir()
    (ignored / "sample.md").write_text("unowned", encoding="utf-8")
    (tmp_path / ".gitignore").write_text("ignored/\n", encoding="utf-8")
    shutil.rmtree(docs)
    try:
        docs.symlink_to(ignored, target_is_directory=True)
    except OSError:
        pytest.skip("This host cannot create directory symlinks.")
    monkeypatch.setattr(extractor, "ROOT", tmp_path)
    assert extractor.markdown_files(tmp_path) == []


def test_generation_refuses_case_collisions_on_every_platform(tmp_path: Path) -> None:
    samples = [
        extractor.CodeSample("cs", "value.Run();", tmp_path / name, 1, "", is_snippet=True)
        for name in ["Example.md", "example.md"]
    ]
    output = tmp_path / "generated"
    with pytest.raises(ValueError, match="collision"):
        extractor.generate_compilable_files(samples, output)
    assert not output.exists()


@pytest.mark.parametrize(
    "declaration", ["public struct Example { }", "public interface IExample { }"]
)
def test_complete_value_and_interface_types_are_not_nested_inside_a_method(
    declaration: str,
) -> None:
    wrapped = extractor.wrap_in_compilable_class(declaration, "Unused")
    assert wrapped == extractor.COMMON_USINGS + "\n" + declaration
    assert "DocumentationExample" not in wrapped
