"""Keep code-fence validation reporting, coverage and compatibility entry points live."""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import check_code_fence_syntax as checker


@pytest.mark.parametrize("fence", ["```", "````", "~~~", "~~~~", "    ```", "\t~~~"])
@pytest.mark.parametrize("language", ["csharp", "rust", "c++", "c-sharp", "language_name"])
def test_comma_attributes_report_the_exact_line(tmp_path: Path, fence: str, language: str) -> None:
    path = tmp_path / "nested" / "guide.md"
    path.parent.mkdir()
    path.write_text(f"# Guide\n\n{fence}{language},ignore,no_run\n", encoding="utf-8")
    count, findings = checker.scan(tmp_path)
    assert count == 1
    assert findings == [checker.Finding(path, 3, language, "ignore,no_run")]


@pytest.mark.parametrize(
    "line",
    [
        "```csharp",
        "~~~python",
        "```",
        '```csharp title="Example.cs"',
        "Prose ```csharp,ignore```",
        "one, two, three",
        "```1invalid,ignore",
    ],
)
def test_legal_lines_pass(tmp_path: Path, line: str) -> None:
    (tmp_path / "guide.md").write_text(line, encoding="utf-8")
    assert checker.scan(tmp_path) == (1, [])


def test_multiple_findings_and_files_are_all_counted(tmp_path: Path) -> None:
    (tmp_path / "z.md").write_text("```rust,no_run\n```python,ignore\n", encoding="utf-8")
    (tmp_path / "a.md").write_text("~~~csharp,ignore", encoding="utf-8")
    count, findings = checker.scan(tmp_path)
    assert count == 2
    assert [(entry.path.name, entry.line) for entry in findings] == [
        ("a.md", 1),
        ("z.md", 1),
        ("z.md", 2),
    ]


@pytest.mark.parametrize("missing", [False, True])
def test_missing_and_empty_corpora_fail(tmp_path: Path, missing: bool) -> None:
    directory = tmp_path / "absent" if missing else tmp_path
    reason = "Docs directory not found" if missing else "No markdown files found"
    with pytest.raises(ValueError, match=reason):
        checker.scan(directory)
    assert checker.main([str(directory)]) == 1


@pytest.mark.parametrize("newline", ["\n", "\r\n"])
def test_physical_lines_preserve_report_offsets(tmp_path: Path, newline: str) -> None:
    path = tmp_path / "guide.md"
    path.write_bytes(("text\u2028still one line" + newline + "```csharp,ignore").encode())
    assert checker.scan(tmp_path)[1][0].line == 2


def test_repository_docs_are_nonempty_and_valid() -> None:
    count, findings = checker.scan(checker.ROOT / "docs")
    assert count > 0
    assert findings == []


@pytest.mark.parametrize(
    "script",
    ["scripts/check-code-fence-syntax.sh", "scripts/tests/test-check-code-fence-syntax.sh"],
)
def test_shell_compatibility_launchers(script: str, tmp_path: Path) -> None:
    if sys.platform == "win32":
        pytest.skip(
            "Shell compatibility entry points use POSIX paths; Windows invokes Python directly"
        )
    bash = shutil.which("bash")
    if bash is None:
        pytest.skip("Bash is required for its compatibility launcher")
    capability = subprocess.run(
        [bash, "--version"], capture_output=True, text=True, timeout=15, check=False
    )
    if capability.returncode != 0:
        pytest.skip("The available Bash launcher cannot execute on this host")
    (tmp_path / "guide.md").write_text("```csharp\n", encoding="utf-8")
    arguments = (
        ["-k", "test_repository_docs_are_nonempty_and_valid"]
        if "/tests/" in script
        else [str(tmp_path)]
    )
    result = subprocess.run(
        [bash, str(checker.ROOT / script), *arguments],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        timeout=60,
        check=False,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert ("1 passed" if "/tests/" in script else "VALIDATION PASSED") in result.stdout


@pytest.mark.parametrize("index", range(7))
def test_frozen_original_output(
    index: int, tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    import json
    from typing import cast

    corpus = cast(
        "dict[str, object]",
        json.loads(
            (Path(__file__).parent / "fixtures/code-fence-syntax-v1.json").read_text(
                encoding="utf-8"
            )
        ),
    )
    case = cast("list[dict[str, object]]", corpus["cases"])[index]
    for name, content in cast("dict[str, str]", case["files"]).items():
        path = tmp_path / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
    assert checker.main([str(tmp_path)]) == case["exit"]
    output = capsys.readouterr()
    assert (
        output.out.replace(str(tmp_path), "<CORPUS>").replace(tmp_path.as_posix(), "<CORPUS>")
        == case["stdout"]
    )
    assert (
        output.err.replace(str(tmp_path), "<CORPUS>").replace(tmp_path.as_posix(), "<CORPUS>")
        == case["stderr"]
    )


@pytest.mark.parametrize("content", ["```csharp\n", "```csharp,ignore\n"])
def test_python_cli_from_foreign_directory(tmp_path: Path, content: str) -> None:
    (tmp_path / "guide.md").write_text(content, encoding="utf-8")
    result = subprocess.run(
        [sys.executable, str(checker.ROOT / "scripts/check_code_fence_syntax.py"), str(tmp_path)],
        cwd=tmp_path,
        text=True,
        capture_output=True,
        check=False,
        timeout=15,
    )
    assert result.returncode == (1 if "," in content else 0)
    assert ("VALIDATION FAILED" if "," in content else "VALIDATION PASSED") in result.stdout
