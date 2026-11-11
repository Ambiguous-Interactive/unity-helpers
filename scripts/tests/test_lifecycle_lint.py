"""Replay the original linter's frozen corpus and exercise source/repair boundaries."""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import TypedDict, cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from lint_tests import (
    HELPERS,
    AssemblyScope,
    Source,
    analyze,
    main,
    repair_null_assertions,
    selected_sources,
    split_arguments,
    write_repair,
)

ROOT = Path(__file__).resolve().parents[2]
CORPUS_PATH = Path(__file__).parent / "fixtures" / "test-lifecycle-v1.json"


class LegacyCase(TypedDict):
    paths: list[str] | None
    files: dict[str, str]
    after: dict[str, str]
    fix: bool
    exit: int
    findings: list[list[str | int]]


def original_cases() -> list[LegacyCase]:
    result = cast("list[LegacyCase]", json.loads(CORPUS_PATH.read_text(encoding="utf-8")))
    assert len(result) == 144
    return result


@pytest.mark.parametrize("case", original_cases(), ids=lambda case: str(case["paths"]))
def test_original_inputs_diagnostics_and_repairs(case: LegacyCase, tmp_path: Path) -> None:
    for name, text in case["files"].items():
        path = tmp_path / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(text.encode("utf-8"))
    scope = AssemblyScope(tmp_path)
    findings: list[list[str | int]] = []
    failed = False
    for path in selected_sources(tmp_path, case["paths"]):
        name = path.relative_to(tmp_path).as_posix()
        text = case["files"][name]
        if case["fix"]:
            actual = repair_null_assertions(text)
            assert actual == case["after"][name]
            source = Source(actual)
            for match in re.finditer(r"(?m)^\s*Assert\.Is(?:Not)?Null\s*\(", source.code):
                findings.append([name, source.line_at(match.start()), "UNH005"])
                failed = True
        else:
            for finding in analyze(text, name, editor_only=scope.editor_only(path)):
                findings.append([name, finding.line, finding.code])
                failed |= not finding.advisory
    assert sorted(findings) == sorted(case["findings"])
    assert failed == bool(case["exit"])


@pytest.mark.parametrize("helper", HELPERS)
def test_allowlisted_helpers_exist(helper: str) -> None:
    assert (ROOT / helper).is_file()


@pytest.mark.parametrize("newline", ["\n", "\r\n", "\r", "\x85", "\u2028", "\u2029"])
@pytest.mark.parametrize("bom", [False, True])
@pytest.mark.parametrize("final", [False, True])
def test_fix_preserves_unrelated_bytes(
    tmp_path: Path, newline: str, *, bom: bool, final: bool
) -> None:
    path = tmp_path / "Tests" / "Subject.cs"
    path.parent.mkdir()
    text = 'class C {\nvoid Run() {\n    Assert.IsNotNull(GetValue(), "é,✓");\n}\n}'
    text = text.replace("\n", newline) + (newline if final else "")
    encoding = "utf-8-sig" if bom else "utf-8"
    path.write_bytes(text.encode(encoding))
    assert (
        main(
            [
                "--repo-root",
                str(tmp_path),
                "--paths",
                "Tests/Subject.cs",
                "--fix-null-checks",
            ]
        )
        == 0
    )
    expected = text.replace(
        'Assert.IsNotNull(GetValue(), "é,✓")',
        'Assert.IsTrue(GetValue() != null, "é,✓")',
    )
    assert path.read_bytes() == expected.encode(encoding)


@pytest.mark.parametrize(
    "expression",
    [
        "condition ? first : second",
        "first ?? second",
        "value = GetValue()",
        "() => GetValue()",
    ],
)
def test_fix_preserves_expression_precedence(expression: str) -> None:
    assert (
        repair_null_assertions(f"Assert.IsNotNull({expression});")
        == f"Assert.IsTrue(({expression}) != null);"
    )


@pytest.mark.parametrize(
    "text",
    [
        "// Assert.IsNull(target);\n",
        "/*\nAssert.IsNotNull(target);\n*/",
        'string s = @"\nAssert.IsNull(target);\n";',
        'string s = """\nAssert.IsNotNull(target);\n""";',
        'string s = $@"\nAssert.IsNull(target);\n";',
        'string s = $$"""\nAssert.IsNull(target);\n""";',
        'string s = "Object.Destroy(target)";',
        "// Object.Destroy(target);\n",
        '/* new GameObject("data"); Assert.IsNull(target); */',
    ],
)
def test_literals_and_comments_are_neither_repaired_nor_reported(text: str) -> None:
    assert repair_null_assertions(text) == text
    assert analyze(text, "Tests/Runtime/Subject.cs", editor_only=False) == []


@pytest.mark.parametrize(
    "arguments",
    [
        'Create<A, B>(), "a,b"',
        'Get("a,b"), "message"',
        'a < b, "message"',
        'a<b && c>d, "message"',
        'Create<A, List<B>>(), "message"',
    ],
)
def test_argument_splitting_respects_literals_generics_and_comparisons(
    arguments: str,
) -> None:
    assert len(split_arguments(arguments)) == 2


def test_missing_selected_source_and_empty_full_scope_fail(tmp_path: Path) -> None:
    assert main(["--repo-root", str(tmp_path)]) == 1
    (tmp_path / "Tests").mkdir()
    assert main(["--repo-root", str(tmp_path)]) == 1
    assert main(["--repo-root", str(tmp_path), "--paths", "Tests/Missing.cs"]) == 1


def test_fix_validates_full_batch_before_any_write(tmp_path: Path) -> None:
    path = tmp_path / "Tests" / "First.cs"
    path.parent.mkdir()
    original = b"Assert.IsNotNull(target);"
    path.write_bytes(original)
    (path.parent / "Last.cs").write_bytes(b"\xff")
    assert main(["--repo-root", str(tmp_path), "--fix-null-checks"]) == 1
    assert path.read_bytes() == original


def test_outside_source_is_refused(tmp_path: Path) -> None:
    root = tmp_path / "repository"
    root.mkdir()
    outside = tmp_path / "Outside.cs"
    outside.write_text("Assert.IsNull(target);", encoding="utf-8")
    assert main(["--repo-root", str(root), "--paths", str(outside), "--fix-null-checks"]) == 1
    assert outside.read_text(encoding="utf-8") == "Assert.IsNull(target);"


def test_missing_helper_fails_before_repair(tmp_path: Path) -> None:
    (tmp_path / "package.json").write_text("{}", encoding="utf-8")
    assert main(["--repo-root", str(tmp_path)]) == 1


def test_linked_source_is_refused(tmp_path: Path) -> None:
    original = tmp_path / "Original.cs"
    original.write_text("Assert.IsNull(target);", encoding="utf-8")
    linked = tmp_path / "Linked.cs"
    try:
        linked.symlink_to(original)
    except OSError:
        pytest.skip("Symlink creation is unavailable")
    assert main(["--repo-root", str(tmp_path), "--paths", str(linked), "--fix-null-checks"]) == 1
    assert original.read_text(encoding="utf-8") == "Assert.IsNull(target);"


def test_hard_link_repair_is_refused_before_batch_writes(tmp_path: Path) -> None:
    first = tmp_path / "First.cs"
    first.write_text("Assert.IsNull(target);", encoding="utf-8")
    last = tmp_path / "Last.cs"
    try:
        os.link(first, last)
    except OSError:
        pytest.skip("Hard-link creation is unavailable")
    assert (
        main(
            [
                "--repo-root",
                str(tmp_path),
                "--paths",
                str(first),
                str(last),
                "--fix-null-checks",
            ]
        )
        == 1
    )
    assert first.read_text(encoding="utf-8") == "Assert.IsNull(target);"


def test_replacement_failure_preserves_source_and_removes_temporary(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    path = tmp_path / "Subject.cs"
    path.write_bytes(b"original")

    def fail_replace(self: Path, target: str | Path) -> Path:
        raise OSError(f"replacement refused: {self} -> {target}")

    monkeypatch.setattr(Path, "replace", fail_replace)
    with pytest.raises(OSError, match="replacement refused"):
        write_repair(path, b"new")
    assert path.read_bytes() == b"original"
    assert list(tmp_path.iterdir()) == [path]


@pytest.mark.parametrize("name", ["white space.cs", "é.cs", "brackets[1].cs", "line\nname.cs"])
def test_literal_filenames_are_scanned(tmp_path: Path, name: str) -> None:
    if os.name == "nt" and "\n" in name:
        pytest.skip("Windows filenames cannot contain a newline")
    path = tmp_path / name
    path.write_text("Assert.IsNull(target);", encoding="utf-8")
    assert main(["--repo-root", str(tmp_path), "--paths", str(path)]) == 1


def test_multiline_invocation_is_matched_once() -> None:
    text = "Object.DestroyImmediate(\n    GetObject(GetIndex(1))\n);"
    found = analyze(text, "Tests/Runtime/Subject.cs", editor_only=False)
    assert [(item.code, item.line) for item in found] == [("UNH001", 1)]


@pytest.mark.parametrize("multiple", [False, True])
def test_powershell_launcher_preserves_foreign_directory_and_multiple_paths(
    tmp_path: Path, *, multiple: bool
) -> None:
    executable = shutil.which("pwsh")
    uv = shutil.which("uv")
    if executable is None or uv is None:
        pytest.skip("PowerShell and uv are required for compatibility launcher coverage")
    first = tmp_path / "First.cs"
    first.write_text("class Clean {}", encoding="utf-8")
    paths = [str(first)]
    if multiple:
        last = tmp_path / "Last.cs"
        last.write_text("Assert.IsNull(target);", encoding="utf-8")
        paths.append(str(last))
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/lint-tests.ps1"),
            "-Paths",
            *paths,
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        encoding="utf-8",
        timeout=60,
        check=False,
    )
    assert result.returncode == int(multiple), result.stdout + result.stderr
    assert ("UNH005" in result.stdout) == multiple


def test_named_first_argument_requires_manual_repair(tmp_path: Path) -> None:
    path = tmp_path / "Subject.cs"
    text = "Assert.IsNotNull(anObject: target);"
    path.write_text(text, encoding="utf-8")
    assert main(["--repo-root", str(tmp_path), "--paths", str(path), "--fix-null-checks"]) == 1
    assert path.read_text(encoding="utf-8") == text


def test_multiline_testcase_names_cannot_escape_naming_rule() -> None:
    text = 'new TestCaseData(1).SetName(\n    "Contains_Underscore"\n);'
    found = analyze(text, "Tests/Runtime/Subject.cs", editor_only=False)
    assert [(item.code, item.line) for item in found] == [("UNH004", 1)]


def test_folder_fallback_is_not_cached_as_an_assembly_wide_answer(
    tmp_path: Path,
) -> None:
    scope = AssemblyScope(tmp_path)
    editor = tmp_path / "Tests/Editor/Editor.cs"
    runtime = tmp_path / "Tests/Runtime/Runtime.cs"
    editor.parent.mkdir(parents=True)
    runtime.parent.mkdir(parents=True)
    editor.touch()
    runtime.touch()
    assert scope.editor_only(editor)
    assert not scope.editor_only(runtime)
    assert scope.editor_only(editor)


@pytest.mark.parametrize(
    ("condition", "safe"),
    [
        ("UNITY_EDITOR", True),
        ("UNITY_EDITOR // rationale", True),
        ("UNITY_EDITOR /* rationale */", True),
        ("FEATURE /* UNITY_EDITOR */", False),
        ("true // UNITY_EDITOR", False),
        ("FEATURE && UNITY_EDITOR", True),
        ("(UNITY_EDITOR || FEATURE) && UNITY_EDITOR", True),
        ("UNITY_EDITOR == true", True),
        ("UNITY_EDITOR != false", True),
        ("!!UNITY_EDITOR", True),
        ("false", True),
        ("UNITY_EDITOR || FEATURE", False),
        ("FEATURE || UNITY_EDITOR", False),
        ("UNITY_EDITOR && FEATURE || OTHER", False),
        ("!UNITY_EDITOR", False),
        ("UNITY_EDITOR == false", False),
        ("true", False),
        ("malformed(", False),
    ],
)
def test_editor_guard_must_exclude_every_possible_player_branch(
    condition: str, *, safe: bool
) -> None:
    text = f"#if {condition}\nusing UnityEditor;\n#endif"
    found = analyze(text, "Tests/Runtime/Subject.cs", editor_only=False)
    assert bool(found) != safe
    assert all(item.code == "UNH011" for item in found)


def test_previous_elif_branch_prevents_editor_reference_in_else() -> None:
    text = "#if UNITY_EDITOR\n#elif !UNITY_EDITOR\n#else\nusing UnityEditor;\n#endif"
    assert analyze(text, "Tests/Runtime/Subject.cs", editor_only=False) == []


def test_testname_code_inside_literal_is_not_a_declaration() -> None:
    text = 'string source = """\nnew TestCaseData(1).SetName("Not_A_Test");\n[TestCaseSource("Not_A_Source")]\n""";'
    assert analyze(text, "Tests/Runtime/Subject.cs", editor_only=False) == []


@pytest.mark.parametrize("newline", ["\n", "\r\n", "\r", "\x85", "\u2028", "\u2029"])
def test_directives_and_diagnostics_respect_csharp_line_separators(
    newline: str,
) -> None:
    text = newline.join(
        [
            "#if UNITY_EDITOR",
            "using UnityEditor;",
            "#endif",
            "Assert.IsNotNull(target);",
        ]
    )
    found = analyze(text, "Tests/Runtime/Subject.cs", editor_only=False)
    assert [(item.code, item.line) for item in found] == [("UNH005", 4)]


def test_string_literal_cannot_suppress_an_actual_code_violation() -> None:
    text = 'Object.DestroyImmediate(target, "UNH-SUPPRESS");'
    found = analyze(text, "Tests/Runtime/Subject.cs", editor_only=False)
    assert [(item.code, item.line) for item in found] == [("UNH001", 1)]


@pytest.mark.parametrize("newline", ["\n", "\r\n", "\r", "\x85", "\u2028", "\u2029"])
def test_comment_line_breaks_preserve_following_diagnostic_line(newline: str) -> None:
    text = newline.join(["/* first", "second */", "using UnityEditor;"])
    found = analyze(text, "Tests/Runtime/Subject.cs", editor_only=False)
    assert [(item.code, item.line) for item in found] == [("UNH011", 3)]


def test_trailing_line_comment_requires_manual_null_repair() -> None:
    text = "Assert.IsNotNull(target // explanation\n);"
    assert repair_null_assertions(text) == text


def test_powershell_test_launcher_forwards_filter(tmp_path: Path) -> None:
    executable = shutil.which("pwsh")
    if executable is None:
        pytest.skip("PowerShell is not installed")
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/tests/test-lint-tests.ps1"),
            "-TestFilter",
            "test_multiline_testcase_names_cannot_escape_naming_rule",
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "1 passed" in result.stdout
    assert "deselected" in result.stdout
