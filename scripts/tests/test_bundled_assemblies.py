"""Exercise shipped BCL ownership and every compatibility-gate rejection with files."""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from lint_bundled_assemblies import EXPECTED_CONSTRAINTS, ROOT, check


def fixture(root: Path, *, flow: bool) -> Path:
    root.mkdir()
    for name, constraints in EXPECTED_CONSTRAINTS.items():
        assembly(root, name, constraints, flow=flow)
    return root


def assembly(root: Path, name: str, constraints: list[str], *, flow: bool = False) -> None:
    dll = root / f"{name}.dll"
    dll.write_bytes(b"")
    block = (
        f"  defineConstraints: [{', '.join(repr(item) for item in constraints)}]"
        if flow or not constraints
        else "  defineConstraints:\n" + "\n".join(f"  - '{item}'" for item in constraints)
    )
    dll.with_name(dll.name + ".meta").write_text(
        "fileFormatVersion: 2\nPluginImporter:\n" + block + "\n  isPreloaded: 0\n",
        encoding="utf-8",
    )


def test_shipped_importers() -> None:
    assert check(ROOT / "Runtime/Binaries", verbose=True) == 0


@pytest.mark.parametrize("flow", [False, True])
def test_complete_fixture(tmp_path: Path, flow: bool) -> None:
    assert check(fixture(tmp_path / "binaries", flow=flow)) == 0


@pytest.mark.parametrize("missing", [False, True])
def test_missing_or_empty_directory(
    tmp_path: Path, missing: bool, capsys: pytest.CaptureFixture[str]
) -> None:
    root = tmp_path / "binaries"
    if not missing:
        root.mkdir()
    assert check(root) == 1
    assert ("not found" if missing else "No DLLs found") in capsys.readouterr().out


@pytest.mark.parametrize("flow", [False, True])
@pytest.mark.parametrize(
    ("mutation", "message"),
    [
        ("unsafe", "is recorded as deliberately not shipped"),
        ("unknown", "is not classified by"),
        ("missing-meta", "Missing importer metadata"),
        ("wrong-constraint", "must carry defineConstraints"),
        ("dropped-constraint", "but carries [(none)]"),
        ("unexpected-constraint", "must carry defineConstraints [(none)]"),
        ("missing-dll", "is classified as shipped but no longer exists"),
    ],
)
def test_each_rejection(
    tmp_path: Path,
    flow: bool,
    mutation: str,
    message: str,
    capsys: pytest.CaptureFixture[str],
) -> None:
    root = fixture(tmp_path / "binaries", flow=flow)
    if mutation == "unsafe":
        assembly(root, "System.Runtime.CompilerServices.Unsafe", [])
    elif mutation == "unknown":
        assembly(root, "Newtonsoft.Json", [])
    elif mutation == "missing-meta":
        (root / "System.Text.Json.dll.meta").unlink()
    elif mutation == "wrong-constraint":
        assembly(root, "System.Text.Json", ["UNITY_2021_3_OR_NEWER"], flow=flow)
    elif mutation == "dropped-constraint":
        assembly(root, "System.Text.Json", [], flow=flow)
    elif mutation == "unexpected-constraint":
        assembly(root, "System.IO.Pipelines", ["!UNITY_6000_5_OR_NEWER"], flow=flow)
    elif mutation == "missing-dll":
        (root / "System.Text.Json.dll").unlink()
    else:
        pytest.fail(f"Unknown fixture mutation: {mutation}")
    assert check(root) == 1
    assert message in capsys.readouterr().out


@pytest.mark.parametrize("name", sorted(EXPECTED_CONSTRAINTS))
def test_each_classified_assembly_is_required(tmp_path: Path, name: str) -> None:
    root = fixture(tmp_path / "binaries", flow=False)
    (root / f"{name}.dll").unlink()
    assert check(root) == 1


def test_assembly_names_match_without_case(tmp_path: Path) -> None:
    root = fixture(tmp_path / "binaries", flow=False)
    for path in root.iterdir():
        path.rename(path.with_name(path.name.lower()))
    assert check(root) == 0
    assembly(root, "system.runtime.compilerservices.unsafe", [])
    assert check(root) == 1


@pytest.mark.parametrize("legacy", [False, True])
def test_cli_from_another_directory(tmp_path: Path, legacy: bool) -> None:
    root = fixture(tmp_path / "binaries with spaces", flow=True)
    command = (
        [
            "pwsh",
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/lint-bundled-assemblies.ps1"),
            "-BinariesRoot",
        ]
        if legacy
        else [
            sys.executable,
            str(ROOT / "scripts/lint_bundled_assemblies.py"),
            "--binaries-root",
        ]
    )
    result = subprocess.run(
        [*command, str(root)],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "4 bundled assemblies classified and constrained" in result.stdout
    (root / "System.Text.Json.dll.meta").unlink()
    rejected = subprocess.run(
        [*command, str(root)],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )
    assert rejected.returncode == 1, rejected.stdout + rejected.stderr
    assert "Missing importer metadata" in rejected.stdout
