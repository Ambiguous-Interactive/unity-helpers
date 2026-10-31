"""Exercise reflected-attribute discovery and stripping annotation boundaries."""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import lint_preserve_attributes as gate


def source(root: Path, relative: str, text: str) -> Path:
    path = root / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")
    return path


@pytest.mark.parametrize(
    ("annotations", "preserved"),
    [
        ("[AttributeUsage(AttributeTargets.Class)]", False),
        ("[Preserve]\n[AttributeUsage(AttributeTargets.Class)]", True),
        ("[Preserve]\n// reason\n[AttributeUsage(AttributeTargets.Class)]", True),
        (
            "[Preserve]\n[AttributeUsage(\nAttributeTargets.Class,\nInherited = true\n)]",
            True,
        ),
        (
            "/// docs\n[Preserve]\n/* reason */\n[AttributeUsage(\nAttributeTargets.Class\n)]",
            True,
        ),
        (
            "#if UNITY_5_3_OR_NEWER\n[Preserve]\n#endif\n[AttributeUsage(AttributeTargets.Class)]",
            True,
        ),
        (
            "#if !UNITY_5_3_OR_NEWER\n[Preserve]\n#endif\n[AttributeUsage(AttributeTargets.Class)]",
            False,
        ),
        (
            "[Preserve]\npublic int Other;\n[AttributeUsage(AttributeTargets.Class)]",
            False,
        ),
        ("#if OTHER_SYMBOL\n[Preserve]\n#endif", False),
    ],
)
def test_annotation_boundaries(annotations: str, preserved: bool, tmp_path: Path) -> None:
    source(
        tmp_path,
        "Reader.cs",
        "Attribute.GetCustomAttribute(type, typeof(FixtureAttribute));",
    )
    source(
        tmp_path,
        "Fixture.cs",
        annotations + "\npublic sealed class FixtureAttribute : Attribute {}",
    )
    checked, failures = gate.analyze(tmp_path)
    assert checked == ["FixtureAttribute"]
    assert bool(failures) is not preserved


@pytest.mark.parametrize(
    "read",
    [
        "type.GetCustomAttribute<FixtureAttribute>();",
        "type.GetCustomAttributes<FixtureAttribute>();",
        "type.IsDefined<FixtureAttribute>();",
        "field.IsAttributeDefined<FixtureAttribute>(out _, inherit: false);",
        "field.TryGetAttributeSafe<FixtureAttribute>(out _);",
        "Attribute.GetCustomAttribute(type, typeof(FixtureAttribute));",
        "field.TryGetAttributeSafe(out FixtureAttribute marker);",
    ],
)
def test_every_reflection_shape_requires_annotation(read: str, tmp_path: Path) -> None:
    source(tmp_path, "Reader.cs", read)
    source(tmp_path, "Fixture.cs", "public sealed class FixtureAttribute : Attribute {}")
    checked, failures = gate.analyze(tmp_path)
    assert checked == ["FixtureAttribute"]
    assert len(failures) == 1
    assert "not marked [Preserve]" in failures[0]


def test_generic_read_cannot_hide_behind_a_healthy_attribute(tmp_path: Path) -> None:
    source(
        tmp_path,
        "Reads.cs",
        "type.GetCustomAttribute<MissingAttribute>();\n"
        "Attribute.GetCustomAttribute(type, typeof(HealthyAttribute));",
    )
    source(
        tmp_path,
        "Healthy.cs",
        "[Preserve]\npublic class HealthyAttribute : Attribute {}",
    )
    source(tmp_path, "Missing.cs", "public class MissingAttribute : Attribute {}")
    checked, failures = gate.analyze(tmp_path)
    assert checked == ["HealthyAttribute", "MissingAttribute"]
    assert len(failures) == 1
    assert "MissingAttribute" in failures[0]


@pytest.mark.parametrize("attribute", list(gate.NOT_OURS))
def test_foreign_attributes_do_not_require_package_declarations(
    attribute: str, tmp_path: Path
) -> None:
    source(
        tmp_path,
        "Reader.cs",
        f"Attribute.GetCustomAttribute(type, typeof({attribute}));",
    )
    assert gate.analyze(tmp_path) == ([], [])


def test_unknown_foreign_attribute_is_reported(tmp_path: Path) -> None:
    source(tmp_path, "Reader.cs", "type.GetCustomAttribute<UnknownAttribute>();")
    checked, failures = gate.analyze(tmp_path)
    assert checked == []
    assert len(failures) == 1
    assert "declared nowhere" in failures[0]


def test_relational_subclasses_are_required_without_a_concrete_read(
    tmp_path: Path,
) -> None:
    source(
        tmp_path,
        "Fixture.cs",
        "public class FixtureAttribute : BaseRelationalComponentAttribute {}",
    )
    checked, failures = gate.analyze(tmp_path)
    assert checked == ["FixtureAttribute"]
    assert len(failures) == 1


def test_generic_type_parameters_are_not_treated_as_missing_types(
    tmp_path: Path,
) -> None:
    source(tmp_path, "Reader.cs", "type.GetCustomAttribute<TAttribute>();")
    assert gate.analyze(tmp_path) == ([], [])


@pytest.mark.parametrize("missing", [False, True])
def test_empty_or_missing_root_fails(
    missing: bool, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    root = tmp_path / "missing" if missing else tmp_path
    monkeypatch.setenv("PRESERVE_ATTRIBUTES_ROOT", str(root))
    assert gate.main() == 1


def test_cli_uses_utf8_sources_and_a_foreign_working_directory(tmp_path: Path) -> None:
    source(tmp_path, "café/Read.cs", "type.GetCustomAttribute<FixtureAttribute>();")
    source(
        tmp_path,
        "Fixture.cs",
        "[Preserve]\npublic class FixtureAttribute : Attribute {}",
    )
    result = subprocess.run(
        [sys.executable, str(gate.ROOT / "scripts/lint_preserve_attributes.py")],
        cwd=tmp_path,
        env=dict(os.environ, PRESERVE_ATTRIBUTES_ROOT=str(tmp_path)),
        capture_output=True,
        encoding="utf-8",
        timeout=20,
        check=False,
    )
    assert result.returncode == 0, result.stderr
    assert "1 runtime-reflected attribute(s) are preserved" in result.stdout


def test_repository_attributes_remain_preserved() -> None:
    checked, failures = gate.analyze(gate.ROOT / "Runtime")
    assert len(checked) >= 15
    assert "ValidateAssignmentAttribute" in checked
    assert "PoolPurgePolicyAttribute" in checked
    assert failures == []


@pytest.mark.parametrize("preserved", [False, True])
def test_node_compatibility_launcher_preserves_reports(preserved: bool, tmp_path: Path) -> None:
    node = shutil.which("node")
    if node is None:
        pytest.skip("Node is required for its compatibility entry point")
    source(tmp_path, "Reader.cs", "type.GetCustomAttribute<FixtureAttribute>();")
    annotations = "[Preserve]\n" if preserved else ""
    source(tmp_path, "Fixture.cs", annotations + "public class FixtureAttribute : Attribute {}")
    result = subprocess.run(
        [node, str(gate.ROOT / "scripts/lint-preserve-attributes.js")],
        cwd=tmp_path,
        env=dict(os.environ, PRESERVE_ATTRIBUTES_ROOT=str(tmp_path)),
        capture_output=True,
        encoding="utf-8",
        check=False,
        timeout=30,
    )
    assert result.returncode == (0 if preserved else 1), result.stderr
