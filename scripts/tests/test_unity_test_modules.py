"""Exercise the retained module policy, malformed JSON and public CLI boundaries."""

from __future__ import annotations

import json
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import lint_unity_test_modules as lint

ROOT = Path(__file__).resolve().parents[2]
MODULE = "com.unity.modules.animation"
RETAINED_MODULES = [
    "ai",
    "androidjni",
    "animation",
    "assetbundle",
    "audio",
    "cloth",
    "director",
    "imageconversion",
    "imgui",
    "jsonserialize",
    "particlesystem",
    "physics",
    "physics2d",
    "screencapture",
    "terrain",
    "terrainphysics",
    "tilemap",
    "ui",
    "uielements",
    "umbra",
    "unitywebrequest",
    "unitywebrequestassetbundle",
    "unitywebrequestaudio",
    "unitywebrequesttexture",
    "unitywebrequestwww",
    "vehicles",
    "video",
    "vr",
    "wind",
    "xr",
]


@pytest.mark.parametrize(
    "name", [*("com.unity.modules." + name for name in RETAINED_MODULES), "com.unity.ugui"]
)
def test_every_retained_package_is_allowed(name: str) -> None:
    assert not lint.validate({"modules": {name: "1.0.0"}})
    assert not lint.validate({"modules": {name: "1.0.0"}, "_evidence": {name: "used"}})


def test_allowed_packages_match_the_frozen_policy() -> None:
    assert frozenset(RETAINED_MODULES) == lint.BUILTINS
    assert frozenset(("com.unity.ugui",)) == lint.BUNDLED


@pytest.mark.parametrize(
    ("document", "message"),
    [
        ({}, "missing a 'modules' object"),
        ({"modules": {}}, "declares no modules"),
        ({"modules": {MODULE: ""}}, "empty version"),
        ({"modules": {MODULE: "  "}}, "empty version"),
        ({"modules": {MODULE: None}}, "string version"),
        ({"modules": {MODULE: 1}}, "string version"),
        ({"modules": {"com.unity.modules.grid": "1"}}, "com.unity.modules.tilemap"),
        ({"modules": {"com.unity.modules.physisc2d": "1"}}, "not a known Unity built-in"),
        ({"modules": {"com.unity.modules.Animation": "1"}}, "not a known Unity built-in"),
        ({"modules": {"com.unity.textmeshpro": "3.0.6"}}, "not allowed here"),
        ({"modules": {MODULE: "1"}, "_evidence": {}}, "no matching _evidence"),
        (
            {"modules": {MODULE: "1"}, "_evidence": {MODULE: "ok", "stale": "old"}},
            "not in the modules list",
        ),
    ],
)
def test_invalid_contract_reports_a_reason(document: dict[str, object], message: str) -> None:
    assert any(message in error for error in lint.validate(document))


@pytest.mark.parametrize(
    "document", [[], None, {"modules": []}, {"modules": {MODULE: "1"}, "_evidence": []}]
)
def test_malformed_object_shapes_fail(document: object) -> None:
    with pytest.raises(ValueError, match="object"):
        lint.validate(document)


@pytest.mark.parametrize(
    "content", ["{", "null", "[]", '{"modules":{},"modules":{}}', '{"modules":{"a":"1","a":"2"}}']
)
def test_malformed_and_duplicate_json_fails(content: str, tmp_path: Path) -> None:
    path = tmp_path / "modules.json"
    path.write_text(content, encoding="utf-8")
    assert lint.main(["--path", str(path)]) == 1


def test_real_manifest_and_missing_input(tmp_path: Path) -> None:
    assert lint.main(["--verbose"]) == 0
    assert lint.main(["--path", str(tmp_path / "missing.json")]) == 1
    path = tmp_path / "modules.json"
    path.write_bytes(b"\xff")
    assert lint.main(["--path", str(path)]) == 1
    path.write_text(json.dumps({"modules": {MODULE: "1.0.0"}}), encoding="utf-8-sig")
    assert lint.main(["--path", str(path)]) == 0


def test_manifest_data_cannot_inject_workflow_commands(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    path = tmp_path / "modules.json"
    path.write_text(json.dumps({"modules": {"bad%0A\r\n::error::injected": "1"}}), encoding="utf-8")
    assert lint.main(["--path", str(path)]) == 1
    output = capsys.readouterr().out
    assert "%250A%0D%0A::error::injected" in output
    assert len([line for line in output.splitlines() if line.startswith("::error::")]) == 1


@pytest.mark.parametrize("python", [True, False])
def test_cli_from_other_directory(python: bool, tmp_path: Path) -> None:
    if python:
        command = [sys.executable, str(ROOT / "scripts/lint_unity_test_modules.py"), "--path"]
    else:
        executable = shutil.which("pwsh")
        assert executable is not None
        command = [
            executable,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/lint-unity-test-modules.ps1"),
            "-Path",
        ]
    path = tmp_path / "modules.json"
    for document, expected in (
        ({"modules": {MODULE: "1"}}, 0),
        ({"modules": {"com.unity.modules.grid": "1"}}, 1),
    ):
        path.write_text(json.dumps(document), encoding="utf-8")
        result = subprocess.run(
            [*command, str(path)],
            cwd=tmp_path,
            text=True,
            capture_output=True,
            check=False,
            timeout=60,
        )
        assert result.returncode == expected, result.stdout + result.stderr


def test_suite_launcher_executes_a_real_case(tmp_path: Path) -> None:
    executable = shutil.which("pwsh")
    assert executable is not None
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/tests/test-lint-unity-test-modules.ps1"),
            "-TestFilter",
            "test_real_manifest_and_missing_input",
        ],
        cwd=tmp_path,
        text=True,
        capture_output=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "1 passed" in result.stdout
