"""Check documentation references against isolated source and assembly inventories."""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import lint_doc_identifiers as gate


def source(root: Path, relative: str, text: str) -> Path:
    path = root / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")
    return path


def fixture(root: Path) -> None:
    source(
        root,
        "Runtime/Thing.cs",
        "namespace WallstopStudios.UnityHelpers.Core.Attributes {}",
    )
    source(root, "Runtime/Runtime.asmdef", '{"name":"WallstopStudios.UnityHelpers"}')


@pytest.mark.parametrize(
    ("text", "usings", "assemblies", "errors"),
    [
        ("using WallstopStudios.UnityHelpers.Core.Attributes;", 1, 0, 0),
        ("using WallstopStudios.UnityHelpers.Core;", 1, 0, 0),
        ("using UnityEngine;", 0, 0, 0),
        ('<assembly fullname="WallstopStudios.UnityHelpers">', 0, 1, 0),
        ("using WallstopStudios.UnityHelpers.Core.Attribute;", 1, 0, 1),
        ('<assembly fullname="WallstopStudios.UnityHelpers.Runtime">', 0, 1, 1),
        ("using WallstopStudios.Nope;\nusing WallstopStudios.AlsoNope;", 2, 0, 2),
        (
            '<assembly fullname="WallstopStudios.Nope"/><assembly fullname="WallstopStudios.Other"/>',
            0,
            2,
            2,
        ),
    ],
)
def test_reference_contract(
    text: str, usings: int, assemblies: int, errors: int, tmp_path: Path
) -> None:
    fixture(tmp_path)
    source(tmp_path, "docs/nested/guide.md", text)
    result = gate.analyze(tmp_path)
    assert result.documents == 1
    assert result.namespaces == 4
    assert (result.usings, result.assemblies) == (usings, assemblies)
    assert len(result.violations) == errors
    for violation in result.violations:
        assert violation.startswith("docs/nested/guide.md:")


@pytest.mark.parametrize("root", gate.SOURCE_ROOTS)
def test_every_source_root_supplies_namespaces(root: str, tmp_path: Path) -> None:
    source(tmp_path, f"{root}/Only.cs", "namespace WallstopStudios.OnlyHere {}")
    source(tmp_path, "docs/guide.md", "using WallstopStudios.OnlyHere;")
    assert gate.analyze(tmp_path).violations == []


@pytest.mark.parametrize("folder", [*gate.SKIPPED, ".hidden"])
def test_hidden_and_generated_folders_cannot_satisfy_references(
    folder: str, tmp_path: Path
) -> None:
    fixture(tmp_path)
    source(tmp_path, f"Runtime/{folder}/Ignored.cs", "namespace WallstopStudios.Ignored {}")
    source(tmp_path, "docs/guide.md", "using WallstopStudios.Ignored;")
    assert len(gate.analyze(tmp_path).violations) == 1


@pytest.mark.parametrize("value", ["not json", "[]", "null", '{"name":false}', '{"name":""}'])
def test_invalid_assembly_cannot_satisfy_a_reference(value: str, tmp_path: Path) -> None:
    fixture(tmp_path)
    source(tmp_path, "Runtime/Runtime.asmdef", value)
    source(tmp_path, "docs/guide.md", '<assembly fullname="WallstopStudios.UnityHelpers"/>')
    assert len(gate.analyze(tmp_path).violations) == 1


@pytest.mark.parametrize("missing", ["docs", "sources", "both"])
def test_empty_inventory_is_rejected(
    missing: str, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    if missing != "both":
        if missing != "sources":
            fixture(tmp_path)
        if missing != "docs":
            source(tmp_path, "docs/guide.md", "# Guide")
    monkeypatch.setenv("DOC_IDENTIFIER_ROOT", str(tmp_path))
    assert gate.main() == 1


def test_unreadable_source_fails_instead_of_passing_an_empty_scan(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    fixture(tmp_path)
    source(tmp_path, "docs/guide.md", "# Guide")

    def deny(_path: Path, _extensions: tuple[str, ...]) -> list[Path]:
        raise PermissionError("controlled denied traversal")

    monkeypatch.setattr(gate, "files_under", deny)
    monkeypatch.setenv("DOC_IDENTIFIER_ROOT", str(tmp_path))
    assert gate.main() == 1


def test_cli_works_outside_the_repository(tmp_path: Path) -> None:
    fixture(tmp_path)
    source(tmp_path, "docs/café.md", "using WallstopStudios.UnityHelpers.Core.Attributes;")
    source(tmp_path, "docs/other.md", "# More")
    result = subprocess.run(
        [sys.executable, str(gate.ROOT / "scripts/lint_doc_identifiers.py")],
        cwd=tmp_path,
        env=dict(os.environ, DOC_IDENTIFIER_ROOT=str(tmp_path)),
        capture_output=True,
        encoding="utf-8",
        timeout=20,
        check=False,
    )
    assert result.returncode == 0, result.stderr
    assert "across 2 document(s) all resolve" in result.stdout


def test_repository_references_resolve() -> None:
    result = gate.analyze(gate.ROOT)
    assert result.documents > 0
    assert result.usings > 0
    assert result.violations == []


@pytest.mark.parametrize("valid", [False, True])
def test_node_compatibility_launcher_preserves_reports(valid: bool, tmp_path: Path) -> None:
    node = shutil.which("node")
    if node is None:
        pytest.skip("Node is required for its compatibility entry point")
    fixture(tmp_path)
    namespace = (
        "WallstopStudios.UnityHelpers.Core.Attributes" if valid else "WallstopStudios.Missing"
    )
    source(tmp_path, "docs/guide.md", f"using {namespace};")
    result = subprocess.run(
        [node, str(gate.ROOT / "scripts/lint-doc-identifiers.js")],
        cwd=tmp_path,
        env=dict(os.environ, DOC_IDENTIFIER_ROOT=str(tmp_path)),
        capture_output=True,
        encoding="utf-8",
        check=False,
        timeout=30,
    )
    assert result.returncode == (0 if valid else 1), result.stderr
