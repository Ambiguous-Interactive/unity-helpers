"""Exercise every changelog diagnostic and the direct and PowerShell routes."""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import lint_changelog as lint

VALID = "# Changelog\n\n## [Unreleased]\n\n### Added\n\n- Add an entry.\n\n## [1.2.0] - 2026-08-01\n\n### Fixed\n\n- Fix an entry.\n\n## [1.1.0] - 2026-07-01\n\n### Added\n\n- Add an older entry.\n"


@pytest.mark.parametrize(
    ("old", "new", "diagnostic"),
    [
        ("## [Unreleased]\n", "", "Missing required '## [Unreleased]' section"),
        ("2026-08-01", "not-a-date", "Malformed version header"),
        ("2026-08-01", "2026-13-45", "Invalid date format"),
        ("### Added", "###Added", "missing space after"),
        ("- Add an entry.", "-", "Empty bullet list item"),
        ("- Add an entry.", "- First.\n\n- Second.", "Blank line between two entries"),
        ("[1.1.0]", "[1.2.0]", "Duplicate version [1.2.0]"),
    ],
)
def test_error_rules(old: str, new: str, diagnostic: str) -> None:
    report = lint.analyze(VALID.replace(old, new))
    assert any(diagnostic in error for error in report.errors), report.errors


def test_version_before_unreleased_is_rejected() -> None:
    report = lint.analyze(VALID.replace("## [Unreleased]", "## [0.1.0]\n\n## [Unreleased]"))
    assert any("appears before [Unreleased] section" in error for error in report.errors)


@pytest.mark.parametrize(
    ("old", "new", "diagnostic"),
    [
        ("# Changelog", "# Changes", "Expected '# Changelog'"),
        ("### Added", "### Tweaked", "Non-standard change type"),
        ("## [1.2.0] - 2026-08-01", "## [1.2.0]", "has no date"),
        ("[1.1.0]", "[1.3.0]", "versions should be in descending order"),
        ("## [1.1.0]", "## 1.1.0", "Version should be in brackets"),
    ],
)
def test_warnings_do_not_fail(old: str, new: str, diagnostic: str) -> None:
    report = lint.analyze(VALID.replace(old, new))
    assert not report.errors
    assert any(diagnostic in warning for warning in report.warnings), report.warnings


@pytest.mark.parametrize("ending", ["\n", "\r", "\r\n"])
def test_line_endings(ending: str) -> None:
    assert not lint.analyze(VALID.replace("\n", ending)).errors


@pytest.mark.parametrize("character", ["a", "é", "🦄"])
@pytest.mark.parametrize("count", [2, 3, 4])
def test_rendered_length_uses_utf16_units(character: str, count: int) -> None:
    content = character * count
    units = lint.utf16_length(content)
    source = "# Changelog\n## [Unreleased]\n### Added\n- " + content
    assert not lint.analyze(source, units).errors
    assert any(
        f"over the {units - 1} limit" in error for error in lint.analyze(source, units - 1).errors
    )


def test_links_and_issue_references_do_not_count() -> None:
    source = "- **Add** `A` [guide](https://example.test/very-long-url) ([#1](https://example.test/1), [#2](https://example.test/2))."
    assert lint.rendered_entry(source) == "Add A guide"


def test_released_long_entries_and_loose_lists_remain_accepted() -> None:
    source = VALID.replace("- Fix an entry.", "- " + "a" * 500 + "\n\n- Another released entry.")
    assert not lint.analyze(source).errors


def test_empty_and_heading_only_inputs_are_not_accepted() -> None:
    assert lint.analyze("").errors
    assert lint.analyze("# Changelog").errors
    report = lint.analyze("# Changelog\n## [Unreleased]")
    assert not report.errors
    assert any("no change type subsections" in warning for warning in report.warnings)


@pytest.mark.parametrize("legacy", [False, True])
@pytest.mark.parametrize("valid", [False, True])
def test_cli(tmp_path: Path, legacy: bool, valid: bool) -> None:
    path = tmp_path / "changes ü.md"
    path.write_text(VALID if valid else "# Changelog", encoding="utf-8")
    if legacy:
        shell = shutil.which("pwsh")
        assert shell is not None
        command = [
            shell,
            "-NoProfile",
            "-File",
            str(lint.ROOT / "scripts/lint-changelog.ps1"),
            "-ChangelogPath",
            str(path),
            "-VerboseOutput",
        ]
    else:
        command = [
            sys.executable,
            str(lint.ROOT / "scripts/lint_changelog.py"),
            "--changelog-path",
            str(path),
            "--verbose",
        ]
    result = subprocess.run(
        command,
        cwd=tmp_path,
        text=True,
        encoding="utf-8",
        capture_output=True,
        check=False,
        timeout=30,
    )
    assert result.returncode == (0 if valid else 1), result.stderr
    assert "validation passed" in result.stdout if valid else "Missing required" in result.stdout


@pytest.mark.parametrize("kind", ["missing", "directory", "invalid-utf8"])
def test_unreadable_changelog(
    tmp_path: Path, kind: str, capsys: pytest.CaptureFixture[str]
) -> None:
    path = tmp_path / "changes.md"
    if kind == "directory":
        path.mkdir()
    elif kind == "invalid-utf8":
        path.write_bytes(b"\xff")
    assert lint.main(["--changelog-path", str(path)]) == 1
    assert "not found or unreadable" in capsys.readouterr().out


@pytest.mark.parametrize("valid", [False, True])
def test_fresh_legacy_runspace_preserves_exit_without_error_stream(
    tmp_path: Path, valid: bool
) -> None:
    shell = shutil.which("pwsh")
    assert shell is not None
    changelog = tmp_path / "changes.md"
    changelog.write_text(VALID if valid else "# Changelog", encoding="utf-8")
    probe = tmp_path / "runspace.ps1"
    probe.write_text(
        "param([string]$Launcher, [string]$Changelog, [int]$Expected)\n"
        "$ErrorActionPreference = 'Stop'\n"
        "$runner = [System.Management.Automation.PowerShell]::Create()\n"
        "try {\n"
        "  $null = $runner.AddCommand($Launcher).AddParameter('ChangelogPath', $Changelog)\n"
        "  $output = @($runner.Invoke())\n"
        "  if ($runner.Streams.Error.Count -ne 0) { throw ('Unexpected PowerShell error stream: ' + ($runner.Streams.Error -join '; ')) }\n"
        "  $code = $runner.Runspace.SessionStateProxy.GetVariable('LASTEXITCODE')\n"
        "  if ($null -eq $code -or $code -ne $Expected) { throw ('Wrong child status: ' + $code) }\n"
        "  if (($output -join '\n') -notmatch 'changelog-lint') { throw 'Missing actual linter output.' }\n"
        "  Write-Output ('VALIDATION:' + $code)\n"
        "} finally { $runner.Dispose() }\n",
        encoding="utf-8",
    )
    expected = 0 if valid else 1
    result = subprocess.run(
        [
            shell,
            "-NoProfile",
            "-NonInteractive",
            "-File",
            str(probe),
            "-Launcher",
            str(lint.ROOT / "scripts/lint-changelog.ps1"),
            "-Changelog",
            str(changelog),
            "-Expected",
            str(expected),
        ],
        env={
            **os.environ,
            "VIRTUAL_ENV": str(tmp_path / "unrelated-environment"),
            "UV_PROJECT_ENVIRONMENT": str(tmp_path / "fresh-project-environment"),
        },
        text=True,
        encoding="utf-8",
        capture_output=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stderr
    assert f"VALIDATION:{expected}" in result.stdout
