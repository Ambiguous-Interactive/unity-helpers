"""Keep release validation order, literal arguments and step outputs stable."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts/ci"))
from verify_release_tag import main, verify

ROOT = Path(__file__).resolve().parents[2]
CORPUS = cast(
    "dict[str, object]",
    json.loads((ROOT / "scripts/tests/fixtures/release-tag-v1.json").read_text(encoding="utf-8")),
)
CASES = cast("list[dict[str, object]]", CORPUS["cases"])


@pytest.mark.parametrize("case", CASES)
def test_original_cli(case: dict[str, object], tmp_path: Path) -> None:
    manifest = tmp_path / "package ü.json"
    if case["manifest"] is not None:
        manifest.write_text(json.dumps(case["manifest"]), encoding="utf-8")
    output = tmp_path / "outputs"
    output.write_text("existing=value\n", encoding="utf-8")
    result = subprocess.run(
        [
            sys.executable,
            str(ROOT / "scripts/ci/verify_release_tag.py"),
            "--tag",
            str(case["tag"]),
            "--source-ref",
            str(case["source_ref"]),
            "--package-json",
            str(manifest),
        ],
        env={**os.environ, "GITHUB_OUTPUT": str(output)},
        cwd=tmp_path,
        check=False,
        capture_output=True,
        encoding="utf-8",
        timeout=30,
    )
    assert result.returncode == case["exit"]
    assert result.stdout == case["stdout"]
    assert result.stderr == case["stderr"]
    assert output.read_text(encoding="utf-8") == case["outputs"]


@pytest.mark.parametrize(
    "manifest",
    [
        None,
        [],
        "string",
        1,
        {},
        {"name": "pkg"},
        {"version": "3.5.2"},
        {"name": 2, "version": "3.5.2"},
        {"name": "pkg", "version": True},
    ],
)
def test_invalid_manifest_shape(manifest: object, tmp_path: Path) -> None:
    source = tmp_path / "package.json"
    source.write_text(json.dumps(manifest), encoding="utf-8")
    with pytest.raises(ValueError, match="name/version is missing"):
        verify("3.5.2", "main", source)


@pytest.mark.parametrize(
    "name", ["package\ntag=other", "package\rpackage-version=other", "package\0name"]
)
def test_metadata_cannot_inject_outputs(
    name: str, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    source = tmp_path / "package.json"
    source.write_text(json.dumps({"name": name, "version": "3.5.2"}), encoding="utf-8")
    output = tmp_path / "outputs"
    output.write_bytes(b"existing=value\n")
    monkeypatch.setenv("GITHUB_OUTPUT", str(output))
    assert main(["--tag", "3.5.2", "--source-ref", "main", "--package-json", str(source)]) == 1
    assert output.read_bytes() == b"existing=value\n"
    assert (
        capsys.readouterr().out
        == "::error::package.json name/version must be single-line strings.\n"
    )


@pytest.mark.parametrize("content", ["{", "", '{"name": "pkg", "version": "3.5.2",}', "\udcff"])
def test_malformed_manifest_has_no_outputs(
    content: str,
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    source = tmp_path / "package.json"
    source.write_bytes(content.encode("utf-8", errors="surrogateescape"))
    output = tmp_path / "outputs"
    monkeypatch.setenv("GITHUB_OUTPUT", str(output))
    assert main(["--tag", "3.5.2", "--source-ref", "main", "--package-json", str(source)]) == 1
    assert not output.exists()
    assert capsys.readouterr().out.startswith("::error::")


def test_bom_unicode_and_no_output(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    source = tmp_path / "package.json"
    source.write_text(json.dumps({"name": "package ü", "version": "3.5.2"}), encoding="utf-8-sig")
    monkeypatch.delenv("GITHUB_OUTPUT", raising=False)
    assert main(["--tag", "3.5.2", "--source-ref", "main", "--package-json", str(source)]) == 0
    assert capsys.readouterr().out == "Release tag 3.5.2 matches package ü@3.5.2.\n"


def test_output_failure_fails_the_command(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    source = tmp_path / "package.json"
    source.write_text('{"name":"pkg","version":"3.5.2"}', encoding="utf-8")
    monkeypatch.setenv("GITHUB_OUTPUT", str(tmp_path))
    assert main(["--tag", "3.5.2", "--source-ref", "main", "--package-json", str(source)]) == 1
    assert capsys.readouterr().out.startswith("::error::")


def test_legacy_launcher_foreign_directory_and_unbound_argument(tmp_path: Path) -> None:
    pwsh = shutil.which("pwsh")
    assert pwsh is not None
    source = tmp_path / "package ü.json"
    source.write_text('{"name":"pkg","version":"3.5.2"}', encoding="utf-8")
    command = [
        pwsh,
        "-NoProfile",
        "-File",
        str(ROOT / "scripts/ci/verify-release-tag.ps1"),
        "-Tag",
        "3.5.2",
        "-SourceRef",
        "main",
        "-PackageJsonPath",
        str(source),
    ]
    environment = {**os.environ, "GITHUB_OUTPUT": str(tmp_path / "outputs")}
    result = subprocess.run(
        command,
        cwd=tmp_path,
        env=environment,
        check=False,
        capture_output=True,
        encoding="utf-8",
        timeout=30,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert result.stdout == "Release tag 3.5.2 matches pkg@3.5.2.\n"
    result = subprocess.run(
        [*command, "unexpected-value"],
        cwd=tmp_path,
        env=environment,
        check=False,
        capture_output=True,
        encoding="utf-8",
        timeout=30,
    )
    assert result.returncode == 64
    assert "Unbound arguments: unexpected-value" in result.stderr


def test_legacy_test_launcher_reaches_selected_control() -> None:
    pwsh = shutil.which("pwsh")
    assert pwsh is not None
    result = subprocess.run(
        [
            pwsh,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/tests/test-verify-release-tag.ps1"),
            "-TestFilter",
            "test_invalid_manifest_shape",
        ],
        cwd=ROOT,
        check=False,
        capture_output=True,
        encoding="utf-8",
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "9 passed" in result.stdout
