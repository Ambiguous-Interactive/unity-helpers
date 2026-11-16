"""Replay original release helpers and verify isolated publication failures."""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import release_tools as release
from check_runner import mapping

ROOT = Path(__file__).resolve().parents[2]


def corpus(name: str) -> list[dict[str, object]]:
    raw = cast(
        "list[object]",
        json.loads((ROOT / "scripts/tests/fixtures" / name).read_text(encoding="utf-8")),
    )
    return [mapping(row) for row in raw]


@pytest.mark.parametrize("case", corpus("release-helpers-v1.json"))
def test_original_helper(case: dict[str, object]) -> None:
    args = {key: str(value) for key, value in mapping(case["arguments"]).items()}
    if case["operation"] == "next" and "\n" in args["CurrentVersion"]:
        assert case["result"] == "1.2.4"
        with pytest.raises(ValueError, match="Invalid semver"):
            release.next_version(args["CurrentVersion"], args["Bump"], args["Version"])
        return

    def invoke() -> object:
        operation = case["operation"]
        if operation == "next":
            return release.next_version(args["CurrentVersion"], args["Bump"], args["Version"])
        if operation == "rotate":
            text, rotated = release.rotate_changelog(args["Content"], args["Version"], args["Date"])
            return {"Content": text, "Rotated": rotated}
        if operation == "section":
            return release.changelog_section(args["Content"], args["Version"])
        if operation == "package":
            return release.package_version(
                args["Content"], args["CurrentVersion"], args["NextVersion"]
            )
        assert operation == "lock"
        return release.lock_version(args["Content"], args["NextVersion"])

    if case["error"] is not None:
        with pytest.raises(
            ValueError,
            match="Int32" if "Int32" in str(case["error"]) else re.escape(str(case["error"])),
        ) as error:
            invoke()
        if "Int32" in str(case["error"]):
            assert "Int32" in str(error.value)
        else:
            assert str(error.value) == case["error"]
    else:
        assert invoke() == case["result"]


@pytest.mark.parametrize("case", corpus("upm-changelog-v1.json"))
def test_original_upm_bytes(case: dict[str, object], tmp_path: Path) -> None:
    package = tmp_path / "package.json"
    package.write_bytes(str(case["package"]).encode())
    (tmp_path / "CHANGELOG.md").write_bytes(str(case["changelog"]).encode())
    if case["exit"]:
        with pytest.raises(
            ValueError,
            match="changelogUrl" if case["name"] == "empty-url" else "no release-note content",
        ):
            release.sync_upm(tmp_path)
    else:
        assert release.sync_upm(tmp_path) == 0
        assert release.sync_upm(tmp_path, check=True) == 0
    assert package.read_bytes().decode() == case["result"]


def fixture(root: Path) -> None:
    (root / "package.json").write_text(
        '{\n  "name": "com.example.fixture",\n  "version": "1.2.3",\n  "changelogUrl": "https://example.com/notes",\n  "displayName": "Fixture"\n}\n',
        encoding="utf-8",
    )
    (root / "package-lock.json").write_text(
        '{"version":"0.0.1","packages":{"":{"version":"0.0.1"},"node_modules/x":{"version":"4.0.0"}}}',
        encoding="utf-8",
    )
    (root / "CHANGELOG.md").write_text(
        "# Changelog\n\n## [Unreleased]\n\n### Added\n\n- New.\n\n## [1.2.3]\n\n- Old.\n",
        encoding="utf-8",
    )


def snapshot(root: Path) -> dict[str, bytes]:
    return {path.name: path.read_bytes() for path in root.iterdir() if path.is_file()}


def test_prepare_dry_run_and_publish(tmp_path: Path) -> None:
    fixture(tmp_path)
    before = snapshot(tmp_path)
    assert release.prepare(tmp_path, "minor", "", "2026-06-30", dry_run=True) == 0
    assert snapshot(tmp_path) == before
    assert release.prepare(tmp_path, "minor", "", "2026-06-30") == 0
    package = json.loads((tmp_path / "package.json").read_bytes())
    lock = json.loads((tmp_path / "package-lock.json").read_bytes())
    assert package["version"] == "1.3.0"
    assert lock["version"] == lock["packages"][""]["version"] == "1.3.0"
    assert lock["packages"]["node_modules/x"]["version"] == "4.0.0"
    assert "## [1.3.0] - 2026-06-30" in (tmp_path / "CHANGELOG.md").read_text()
    assert release.release_notes(tmp_path, "1.3.0") == "### Added\n\n- New.\n"
    notes = release.release_notes(tmp_path, "1.3.0", footer=True)
    assert "com.example.fixture@1.3.0" in notes
    assert "Old." not in notes
    assert "each with a `.sha256` checksum." in notes


@pytest.mark.parametrize("fault", ["date", "version", "empty", "duplicate", "lock", "redirected"])
def test_prepare_failure_leaves_inputs(tmp_path: Path, fault: str) -> None:
    fixture(tmp_path)
    version, date = "", "2026-06-30"
    if fault == "date":
        date = "yesterday"
    elif fault == "version":
        version = "1.2.3"
    elif fault == "empty":
        path = tmp_path / "CHANGELOG.md"
        path.write_text(path.read_text().replace("- New.", ""), encoding="utf-8")
    elif fault == "duplicate":
        path = tmp_path / "package.json"
        path.write_text(
            path.read_text().replace('"displayName": "Fixture"', '"nested":{"version":"1.2.3"}'),
            encoding="utf-8",
        )
    elif fault == "lock":
        (tmp_path / "package-lock.json").write_text("{bad", encoding="utf-8")
    else:
        path = tmp_path / "package-lock.json"
        outside = tmp_path / "outside"
        path.rename(outside)
        try:
            path.symlink_to(outside)
        except OSError:
            pytest.skip("Creating symlinks requires host permission")
    before = snapshot(tmp_path)
    with pytest.raises(
        ValueError,
        match={
            "date": "YYYY-MM-DD",
            "version": "strictly greater",
            "empty": "no release-note content",
            "duplicate": "exactly one",
            "lock": "Expecting",
            "redirected": "linked skill path",
        }[fault],
    ):
        release.prepare(tmp_path, "patch", version, date)
    assert snapshot(tmp_path) == before


def test_upm_check_is_read_only_and_case_sensitive(tmp_path: Path) -> None:
    fixture(tmp_path)
    before = snapshot(tmp_path)
    with pytest.raises(ValueError, match="is stale"):
        release.sync_upm(tmp_path, check=True)
    assert snapshot(tmp_path) == before
    assert release.sync_upm(tmp_path) == 0
    assert release.sync_upm(tmp_path, check=True) == 0
    path = tmp_path / "CHANGELOG.md"
    path.write_text(path.read_text().replace("Old.", "old."), encoding="utf-8")
    with pytest.raises(ValueError, match="is stale"):
        release.sync_upm(tmp_path, check=True)


def test_utf16_budget_and_pointer() -> None:
    text = "- 😀\n" * 1700
    value = release.upm_value(text, "https://example.com/notes")
    assert release.utf16_length(value) <= 8000
    assert value.endswith("\n\n(Truncated. Full changelog: https://example.com/notes)")
    assert "\ufffd" not in value
    assert release.upm_value("x" * 8000, "url") == "x" * 8000
    assert release.upm_value("😀" * 4000, "url") == "😀" * 4000


def test_prepare_preserves_hardlinked_authored_alias(tmp_path: Path) -> None:
    fixture(tmp_path)
    source = tmp_path / "package.json"
    alias = tmp_path / "authored.json"
    os.link(source, alias)
    original = alias.read_bytes()
    assert release.prepare(tmp_path, "patch", "", "2026-06-30") == 0
    assert alias.read_bytes() == original
    assert source.read_bytes() != original


def test_actual_cli_notes_and_empty_explicit_version(tmp_path: Path) -> None:
    fixture(tmp_path)
    script = ROOT / "scripts/release_tools.py"
    result = subprocess.run(
        [
            sys.executable,
            str(script),
            "prepare",
            "--repo-root",
            str(tmp_path),
            "--bump",
            "minor",
            "--version",
            "",
            "--date",
            "2026-06-30",
        ],
        capture_output=True,
        check=False,
        timeout=30,
    )
    assert result.returncode == 0, result.stderr
    output = tmp_path / "notes ü.md"
    result = subprocess.run(
        [
            sys.executable,
            str(script),
            "notes",
            "--repo-root",
            str(tmp_path),
            "--version",
            "1.3.0",
            "--output-path",
            str(output),
            "--footer",
        ],
        capture_output=True,
        check=False,
        timeout=30,
    )
    assert result.returncode == 0, result.stderr
    assert output.read_bytes().decode() == release.release_notes(tmp_path, "1.3.0", footer=True)
    result = subprocess.run(
        [
            sys.executable,
            str(script),
            "section",
            "--repo-root",
            str(tmp_path),
            "--version",
            "9.9.9",
        ],
        capture_output=True,
        check=False,
        timeout=30,
    )
    assert result.returncode == 1
    assert b"has no '## [9.9.9]' section" in result.stderr


@pytest.mark.parametrize("tool", ["prepare-release", "write-release-notes", "sync-upm-changelog"])
def test_legacy_release_launchers(tool: str, tmp_path: Path) -> None:
    fixture(tmp_path)
    executable = shutil.which("pwsh")
    if executable is None:
        pytest.skip("PowerShell is not installed on this host")
    arguments = {
        "prepare-release": ["-Bump", "minor", "-Version", "", "-Date", "2026-06-30"],
        "write-release-notes": ["-Version", "1.2.3", "-Footer"],
        "sync-upm-changelog": [],
    }[tool]
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(ROOT / f"scripts/release-tools/{tool}.ps1"),
            "-RepoRoot",
            str(tmp_path),
            *arguments,
        ],
        cwd=tmp_path,
        capture_output=True,
        check=False,
        timeout=30,
    )
    assert result.returncode == 0, result.stderr
    if tool == "prepare-release":
        assert json.loads((tmp_path / "package.json").read_bytes())["version"] == "1.3.0"
    elif tool == "write-release-notes":
        assert (
            result.stdout.decode().replace("\r\n", "\n")
            == release.release_notes(tmp_path, "1.2.3", footer=True) + "\n"
        )
    else:
        assert release.sync_upm(tmp_path, check=True) == 0


def test_legacy_test_launcher_selects_real_control(tmp_path: Path) -> None:
    executable = shutil.which("pwsh")
    if executable is None:
        pytest.skip("PowerShell is not installed on this host")
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/tests/test-release-tools.ps1"),
            "-TestFilter",
            "test_utf16_budget_and_pointer",
        ],
        cwd=tmp_path,
        capture_output=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stderr
    assert b"1 passed" in result.stdout


@pytest.mark.parametrize("version", ["1.2.1٢", "1.2.3\n", "2.0.0\r", "1.٢.3"])
def test_release_versions_require_complete_ascii_components(version: str) -> None:
    with pytest.raises(ValueError, match="Invalid semver"):
        release.semver(version)


def test_unicode_explicit_version_leaves_release_inputs(tmp_path: Path) -> None:
    fixture(tmp_path)
    before = snapshot(tmp_path)
    with pytest.raises(ValueError, match="Invalid semver"):
        release.prepare(tmp_path, "patch", "1.2.1٢", "2026-06-30")
    assert snapshot(tmp_path) == before


def test_dangling_lockfile_refused_before_release_writes(tmp_path: Path) -> None:
    fixture(tmp_path)
    path = tmp_path / "package-lock.json"
    path.unlink()
    try:
        path.symlink_to(tmp_path / "missing-lock.json")
    except OSError:
        pytest.skip("Creating symlinks requires host permission")
    before = {name: (tmp_path / name).read_bytes() for name in ("package.json", "CHANGELOG.md")}
    with pytest.raises(ValueError, match="linked skill path"):
        release.prepare(tmp_path, "patch", "", "2026-06-30")
    assert {name: (tmp_path / name).read_bytes() for name in before} == before
    assert path.is_symlink()


@pytest.mark.parametrize("marker", ["```", "~~~"])
@pytest.mark.parametrize("suffix", ["literal", "indent"])
def test_changelog_literal_markers_do_not_close_fences(marker: str, suffix: str) -> None:
    false_close = marker + "literal" if suffix == "literal" else "    " + marker
    content = f"# Changelog\n\n## [Unreleased]\n\n- Example:\n\n{marker}text\n{false_close}\n## [Unreleased]\n{marker}\n\n- Still new.\n\n## [1.2.3]\n\n- Old.\n"
    rotated, changed = release.rotate_changelog(content, "1.2.4", "2026-06-30")
    assert changed
    assert false_close in rotated
    notes = release.changelog_section(rotated, "1.2.4")
    assert "## [Unreleased]" in notes
    assert "- Still new." in notes
    assert "- Old." not in notes
