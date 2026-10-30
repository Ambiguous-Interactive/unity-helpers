"""File-based controls for Unity assembly ownership and version-define validation."""

from __future__ import annotations

import json
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from lint_asmdef import ROOT, SOURCE_ROOTS, check, version_expression


def repository(root: Path) -> Path:
    for name in SOURCE_ROOTS:
        (root / name).mkdir(parents=True)
    definition(root / "Runtime/Runtime.asmdef", {"name": "Runtime", "rootNamespace": "Runtime"})
    return root


def definition(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value), encoding="utf-8")


def version(
    package: str = "example.package", symbol: str = "PRESENT", expression: str = "1.0"
) -> dict[str, str]:
    return {"name": package, "define": symbol, "expression": expression}


@pytest.mark.parametrize(
    "expression",
    [
        None,
        "",
        " \t",
        "1",
        "14.0.0",
        "1.0-preview.1",
        "[1]",
        "(1,)",
        "(,2]",
        "(1,2]",
        "(,2)",
        "[1,2]",
        "(1,2)",
        "[1,2)",
        "1..2",
    ],
)
def test_supported_expressions(expression: str | None) -> None:
    assert version_expression(expression).valid


@pytest.mark.parametrize(
    "expression",
    [
        "[14.0.0,)",
        "[14.0.0,]",
        ">=14",
        " 14",
        "14 ",
        "(1)",
        "[1)",
        "(1]",
        "[]",
        "()",
        "[,]",
        "(,)",
        "[1",
        "1]",
        "x1",
        "1+build",
        "1.",
        "1-",
        "[1, 2]",
        "[x,2]",
        "[1,2.]",
        "\x1c",
        "\x1d",
        "\x1e",
        "\x1f",
        "1.\u03b1",
        "\u0661.0",
    ],
)
def test_unsupported_expressions(expression: str) -> None:
    assert not version_expression(expression).valid


def test_open_range_has_unity_specific_suggestion() -> None:
    result = version_expression("[14.0.0,)")
    assert "bare version '14.0.0'" in result.suggestion
    assert "silently never applied" in result.reason


@pytest.mark.parametrize("missing", SOURCE_ROOTS)
def test_every_source_root_is_required(
    tmp_path: Path, missing: str, capsys: pytest.CaptureFixture[str]
) -> None:
    root = repository(tmp_path)
    for path in (root / missing).iterdir():
        path.unlink()
    (root / missing).rmdir()
    assert check(root) == 1
    assert f"Source root not found: {missing}" in capsys.readouterr().out


def test_empty_subject_is_rejected(tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    root = repository(tmp_path)
    (root / "Runtime/Runtime.asmdef").unlink()
    assert check(root) == 1
    assert "no .asmdef files" in capsys.readouterr().out


@pytest.mark.parametrize("value", [None, [], "text", 4])
def test_non_object_json_is_rejected(tmp_path: Path, value: object) -> None:
    root = repository(tmp_path)
    definition(root / "Runtime/Runtime.asmdef", value)
    assert check(root) == 1


DEFINITION_ERRORS: list[tuple[dict[str, object], str]] = [
    ({}, "Missing required 'name'"),
    ({"name": "Other"}, "Name mismatch"),
    ({"name": 3}, "'name' must be a string"),
    ({"name": "Runtime", "references": ["Missing"]}, "Referenced assembly 'Missing'"),
    ({"name": "Runtime", "references": "Other"}, "references must be an array"),
    ({"name": "Runtime", "references": [3]}, "references entries must be strings"),
    ({"name": "Runtime", "versionDefines": {}}, "versionDefines must be an array"),
    ({"name": "Runtime", "versionDefines": [None]}, "versionDefines entries must be objects"),
    ({"name": "Runtime", "versionDefines": [{}]}, "entry missing 'name'"),
    ({"name": "Runtime", "versionDefines": [{"name": "example"}]}, "missing 'define' symbol"),
    (
        {"name": "Runtime", "versionDefines": [version(expression="[14.0.0,)")]},
        "invalid expression",
    ),
]


@pytest.mark.parametrize(("value", "message"), DEFINITION_ERRORS)
def test_definition_errors(
    tmp_path: Path, value: object, message: str, capsys: pytest.CaptureFixture[str]
) -> None:
    root = repository(tmp_path)
    definition(root / "Runtime/Runtime.asmdef", value)
    assert check(root) == 1
    assert message in capsys.readouterr().out


def test_namespace_warning_is_nonfatal(tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    root = repository(tmp_path)
    definition(root / "Runtime/Runtime.asmdef", {"name": "Runtime"})
    assert check(root) == 0
    assert "WARNING:" in capsys.readouterr().out


@pytest.mark.parametrize(
    "reference",
    [
        "Runtime",
        "runtime",
        "GUID:opaque",
        "UnityEngine",
        "UnityEditor",
        "Unity.Custom",
        "Sirenix.Serialization",
        "VContainer",
    ],
)
def test_references_and_asmrefs(tmp_path: Path, reference: str) -> None:
    root = repository(tmp_path)
    definition(root / "Editor/Editor.asmdef", {"name": "Editor", "references": [reference]})
    definition(root / "Tests/Tests.asmref", {"reference": reference})
    assert check(root) == 0


@pytest.mark.parametrize("value", [{}, {"reference": "Missing"}, {"reference": 3}])
def test_invalid_asmrefs(tmp_path: Path, value: object) -> None:
    root = repository(tmp_path)
    definition(root / "Tests/Tests.asmref", value)
    assert check(root) == 1


@pytest.mark.parametrize("content", ["{", "", '{"name":'])
def test_malformed_json(tmp_path: Path, content: str) -> None:
    root = repository(tmp_path)
    (root / "Runtime/Runtime.asmdef").write_text(content, encoding="utf-8")
    assert check(root) == 1


def test_bom_and_crlf_json(tmp_path: Path) -> None:
    root = repository(tmp_path)
    (root / "Runtime/Runtime.asmdef").write_bytes(b'\xef\xbb\xbf{\r\n"name": "Runtime"\r\n}\r\n')
    assert check(root) == 0


@pytest.mark.parametrize(
    ("source", "opens_package", "expected"),
    [
        ("#if NEW_API\n#endif", True, 1),
        ("#if PRESENT\n#elif NEW_API\n#endif", True, 1),
        ("#if PRESENT && NEW_API\n#endif", True, 1),
        ("#if NEW_API\n#endif", False, 0),
        ("// #if NEW_API", True, 0),
        ("/*\n#if NEW_API\n*/", True, 0),
        ("#if PRESENT\n#endif", True, 0),
    ],
)
def test_version_defines_are_local_to_the_owning_assembly(
    tmp_path: Path, source: str, opens_package: bool, expected: int
) -> None:
    root = repository(tmp_path)
    definition(
        root / "Editor/Editor.asmdef",
        {"name": "Editor", "versionDefines": [version(symbol="NEW_API")]},
    )
    definition(
        root / "Runtime/Runtime.asmdef",
        {"name": "Runtime", "versionDefines": [version()] if opens_package else []},
    )
    (root / "Runtime/Consumer.cs").write_text(source, encoding="utf-8")
    assert check(root) == expected


def test_nested_assembly_and_similar_directory_names_do_not_inherit_defines(tmp_path: Path) -> None:
    root = repository(tmp_path)
    definition(
        root / "Runtime/Runtime.asmdef",
        {"name": "Runtime", "versionDefines": [version(symbol="NEW_API")]},
    )
    definition(
        root / "Runtime/Nested/Nested.asmdef", {"name": "Nested", "versionDefines": [version()]}
    )
    definition(
        root / "Runtime/NestedSibling/Sibling.asmdef",
        {"name": "Sibling", "versionDefines": [version(symbol="NEW_API")]},
    )
    (root / "Runtime/NestedSibling/Good.cs").write_text("#if NEW_API\n#endif", encoding="utf-8")
    assert check(root) == 0
    (root / "Runtime/Nested/Bad.cs").write_text("#if NEW_API\n#endif", encoding="utf-8")
    assert check(root) == 1


@pytest.mark.parametrize(
    "excluded",
    [
        "obj",
        "OBJ",
        "bin",
        "BIN",
        "Library",
        "library",
        "Temp",
        "TEMP",
        "node_modules",
        "NODE_MODULES",
        ".git",
        ".GIT",
    ],
)
def test_excluded_directories_do_not_supply_definitions(tmp_path: Path, excluded: str) -> None:
    root = repository(tmp_path)
    definition(root / "Runtime" / excluded / "Invalid.asmdef", [])
    definition(root / "Runtime" / excluded / "Invalid.asmref", [])
    assert check(root) == 0


@pytest.mark.parametrize("included", ["binary", "LibraryAssets", "TempData", "objects"])
def test_exclusions_match_entire_directory_names(tmp_path: Path, included: str) -> None:
    root = repository(tmp_path)
    definition(root / "Runtime" / included / "Invalid.asmdef", [])
    assert check(root) == 1


@pytest.mark.skipif(sys.platform == "win32", reason="Dot names are Unix hidden-file semantics")
@pytest.mark.parametrize("relative", ["Runtime/.probe/Invalid.asmdef", "Runtime/.Invalid.asmdef"])
def test_unix_hidden_corpus_is_not_scanned(tmp_path: Path, relative: str) -> None:
    root = repository(tmp_path)
    definition(root / relative, [])
    assert check(root) == 0


@pytest.mark.skipif(sys.platform != "win32", reason="Hidden attributes are Windows file semantics")
def test_windows_hidden_attributes_are_not_scanned(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    root = repository(tmp_path)
    hidden_directory = root / "Runtime/Hidden"
    hidden_file = root / "Runtime/Hidden.asmdef"
    definition(hidden_directory / "Invalid.asmdef", [])
    definition(hidden_file, [])
    attrib = shutil.which("attrib")
    assert attrib is not None
    subjects = (hidden_directory, hidden_file)
    try:
        for path in subjects:
            subprocess.run([attrib, "+h", str(path)], check=True, timeout=30)
        assert check(root) == 0
        capsys.readouterr()
        definition(root / "Runtime/VisibleInvalid.asmdef", [])
        assert check(root) == 1
        assert "[Runtime/VisibleInvalid.asmdef]" in capsys.readouterr().out
    finally:
        for path in subjects:
            subprocess.run([attrib, "-h", str(path)], check=True, timeout=30)


def test_unreadable_source_does_not_hide_later_ownership_errors(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    root = repository(tmp_path)
    definition(
        root / "Editor/Editor.asmdef",
        {"name": "Editor", "versionDefines": [version(symbol="NEW_API")]},
    )
    definition(root / "Runtime/Runtime.asmdef", {"name": "Runtime", "versionDefines": [version()]})
    (root / "Runtime/AUnreadable.cs").write_bytes(b"\xff")
    (root / "Runtime/BUnreadable.cs").write_bytes(b"\xfe")
    (root / "Runtime/ZViolation.cs").write_text("#if NEW_API\n#endif", encoding="utf-8")
    assert check(root) == 1
    output = capsys.readouterr().out
    assert "[Runtime/AUnreadable.cs]" in output
    assert "[Runtime/BUnreadable.cs]" in output
    assert "code uses '#if NEW_API'" in output


def test_cli_reports_failure_from_another_directory(tmp_path: Path) -> None:
    root = repository(tmp_path / "consumer")
    (root / "Runtime/Runtime.asmdef").write_text("not JSON", encoding="utf-8")
    result = subprocess.run(
        [sys.executable, str(ROOT / "scripts/lint_asmdef.py"), "--repository", str(root)],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 1
    assert "Invalid JSON syntax" in result.stdout
