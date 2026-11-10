"""Cover Dependabot structure, current grouping syntax and compatibility launchers."""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from lint_dependabot import INTERVALS, main, validate

ROOT = Path(__file__).resolve().parents[2]
BASIC = """version: 2
updates:
  - package-ecosystem: npm
    directory: /
    schedule:
      interval: weekly
"""
MULTI = """version: 2
multi-ecosystem-groups:
  infrastructure:
    schedule:
      interval: weekly
updates:
  - package-ecosystem: docker
    directory: /
    patterns: [nginx, redis, postgres]
    multi-ecosystem-group: infrastructure
  - package-ecosystem: terraform
    directory: /
    patterns: [aws, terraform-*]
    multi-ecosystem-group: infrastructure
"""


@pytest.mark.parametrize("version", ["2", '"2"', "'2'", "2 # required"])
def test_version_scalar_forms_and_order_are_accepted(version: str) -> None:
    assert validate(BASIC.replace("version: 2", "version: " + version)) == []
    reordered = BASIC.removeprefix("version: 2\n") + "version: " + version + "\n"
    assert validate(reordered) == []


@pytest.mark.parametrize("interval", sorted(INTERVALS))
def test_current_schedule_intervals_are_accepted(interval: str) -> None:
    text = BASIC.replace("weekly", interval)
    if interval == "cron":
        text += '      cronjob: "0 9 * * *"\n'
    assert validate(text) == []


@pytest.mark.parametrize(
    "text",
    [
        BASIC,
        MULTI,
        "version: 2\nupdates: [{package-ecosystem: npm, directory: /, schedule: {interval: daily}}]",
        "registries: {}\n" + BASIC,
        BASIC + "    groups:\n      updates:\n        patterns: ['*']\n",
        BASIC + "    groups:\n      updates:\n        dependency-type: development\n",
        BASIC + "    groups:\n      updates:\n        update-types: [minor, patch]\n",
        BASIC + "    groups:\n      updates: {}\n",
        BASIC.replace("directory: /", "directories: [/, /src/*]"),
        "version: 2\nupdates: []\n",
        BASIC.replace("version: 2", '"version": 2'),
        BASIC.replace("updates:", "# comments\nupdates: # configuration"),
        BASIC.replace("  - package", "- package").replace("    ", "  ").replace("      ", "    "),
        MULTI.replace("infrastructure:\n", "infrastructure: # group schedule\n"),
        "defaults: &weekly {interval: weekly}\n"
        + BASIC.replace("schedule:\n      interval: weekly", "schedule: *weekly"),
        "defaults: &entry {package-ecosystem: npm, directory: /, schedule: {interval: weekly}}\nversion: 2\nupdates:\n  - <<: *entry\n",
        "first: &first {directory: /first}\nsecond: &second {directory: /second}\n"
        + BASIC.replace("directory: /", "<<: [*first, *second]"),
    ],
)
def test_supported_mapping_layouts_and_groups_pass(text: str) -> None:
    assert validate(text) == []


@pytest.mark.parametrize(
    ("text", "code"),
    [
        (BASIC.replace("version: 2\n", ""), "DEP001"),
        (BASIC.replace("version: 2", "version: 3"), "DEP001"),
        (BASIC.replace("version: 2", "registries: {version: 2}"), "DEP001"),
        (BASIC + "multi-ecosystem-groups: []\n", "DEP002"),
        (
            MULTI.replace(
                "multi-ecosystem-group: infrastructure",
                "multi-ecosystem-group: unknown",
            ),
            "DEP003",
        ),
        (
            MULTI.replace("multi-ecosystem-group: infrastructure", "multi-ecosystem-group: []"),
            "DEP003",
        ),
        (BASIC + "    patterns: ['*']\n", "DEP004"),
        (MULTI.replace("    patterns: [nginx, redis, postgres]\n", ""), "DEP004"),
        (MULTI.replace("patterns: [nginx, redis, postgres]", "patterns: []"), "DEP004"),
        (BASIC.replace("    schedule:\n      interval: weekly\n", ""), "DEP005"),
        (BASIC.replace("interval: weekly", "interval: []"), "DEP005"),
        (BASIC.replace("interval: weekly", "interval: never"), "DEP005"),
        (BASIC.replace("interval: weekly", "interval: cron"), "DEP005"),
        (MULTI.replace("interval: weekly", "interval: never"), "DEP005"),
        (BASIC + "    groups: []\n", "DEP006"),
        (BASIC + "    groups:\n      updates: null\n", "DEP006"),
        (BASIC + "    groups:\n      updates:\n        patterns: scalar\n", "DEP006"),
        (BASIC + "    groups:\n      updates:\n        patterns: [123]\n", "DEP006"),
        ("version: 2\n", "DEP007"),
        ("version: 2\nupdates: {}\n", "DEP007"),
        ("", "DEP008"),
        ("[invalid", "DEP008"),
        (BASIC + "version: 2\n", "DEP008"),
        (BASIC + "other: &cycle {again: *cycle}\n", "DEP008"),
        (
            BASIC + "other: !!python/object/apply:os.system ['never execute']\n",
            "DEP008",
        ),
        (BASIC + "other: {<<: 12}\n", "DEP008"),
        (BASIC + "other: {<<: [12]}\n", "DEP008"),
        (BASIC + "other: {[complex]: key}\n", "DEP008"),
        (BASIC.replace("package-ecosystem: npm", "package-ecosystem: 123"), "DEP008"),
        (BASIC.replace("directory: /", "directory: relative"), "DEP008"),
        (BASIC.replace("directory: /", "directories: [relative]"), "DEP008"),
        (
            BASIC.replace("directory: /", "directory: /\n    directories: [/src]"),
            "DEP008",
        ),
        (BASIC + "---\n" + BASIC, "DEP008"),
    ],
)
def test_bad_shapes_and_references_fail(text: str, code: str) -> None:
    assert code in {problem.code for problem in validate(text)}


def test_alias_merges_keep_explicit_keys_and_first_mapping_precedence() -> None:
    text = (
        "first: &first {directory: /first, package-ecosystem: npm}\n"
        "second: &second {directory: bad, package-ecosystem: docker}\n"
        "version: 2\nupdates:\n  - <<: [*first, *second]\n"
        "    schedule: {interval: weekly}\n"
    )
    assert validate(text) == []
    reversed_merge = text.replace("<<: [*first, *second]", "<<: [*second, *first]")
    assert validate(reversed_merge)
    assert (
        validate(
            reversed_merge.replace(
                "<<: [*second, *first]",
                "<<: [*second, *first]\n    directory: /override",
            )
        )
        == []
    )


def test_group_diagnostic_keeps_the_actual_filter_line() -> None:
    text = BASIC + "    groups:\n      updates:\n        patterns: broken\n"
    problem = next(problem for problem in validate(text) if problem.code == "DEP006")
    assert problem.line == 9


def test_all_explicit_inputs_are_read_before_reporting(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    (tmp_path / "Bad.yml").write_text("version: 3", encoding="utf-8")
    assert main(["--repo-root", str(tmp_path), "--paths", "Bad.yml", "Missing.yml"]) == 1
    assert "DEP001" not in capsys.readouterr().out


def test_default_config_and_multiple_literal_paths(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    (tmp_path / ".github").mkdir()
    (tmp_path / ".github/dependabot.yml").write_text(BASIC, encoding="utf-8-sig")
    assert main(["--repo-root", str(tmp_path), "--verbose"]) == 0
    (tmp_path / "first [雪].yml").write_text(MULTI, encoding="utf-8")
    (tmp_path / "second.yml").write_text(BASIC.replace("weekly", "never"), encoding="utf-8")
    assert main(["--repo-root", str(tmp_path), "--paths", "first [雪].yml", "second.yml"]) == 1
    assert "DEP005" in capsys.readouterr().out


@pytest.mark.parametrize("name", ["Missing.yml", "folder.yml", "Invalid.yml"])
def test_missing_directory_and_bad_encoding_inputs_fail(tmp_path: Path, name: str) -> None:
    (tmp_path / "folder.yml").mkdir()
    (tmp_path / "Invalid.yml").write_bytes(b"\xff")
    assert main(["--repo-root", str(tmp_path), "--paths", name]) == 1


def test_linked_inputs_fail(tmp_path: Path) -> None:
    target = tmp_path / "Target.yml"
    target.write_text(BASIC, encoding="utf-8")
    linked = tmp_path / "Linked.yml"
    try:
        linked.symlink_to(target)
    except OSError:
        pytest.skip("The host cannot create file symlinks")
    assert main(["--repo-root", str(tmp_path), "--paths", "Linked.yml"]) == 1


@pytest.mark.skipif(os.name == "nt", reason="Windows forbids newline filenames")
def test_newline_names_do_not_emit_annotations(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    name = "new\n::error::injected.yml"
    (tmp_path / name).write_text("version: 3", encoding="utf-8")
    assert main(["--repo-root", str(tmp_path), "--paths", name]) == 1
    assert "\n::error::injected" not in capsys.readouterr().out


def test_powershell_launcher_forwards_multiple_paths_from_a_foreign_directory(
    tmp_path: Path,
) -> None:
    executable = shutil.which("pwsh")
    if executable is None:
        pytest.skip("PowerShell is not installed")
    first = tmp_path / "one.yml"
    second = tmp_path / "two.yml"
    first.write_text(BASIC, encoding="utf-8")
    second.write_text(BASIC.replace("weekly", "never"), encoding="utf-8")
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/lint-dependabot.ps1"),
            "-Paths",
            str(first),
            str(second),
            "-VerboseOutput",
        ],
        cwd=tmp_path.parent,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 1, result.stdout + result.stderr
    assert "DEP005" in result.stdout
    assert "Checked 2 configuration(s)" in result.stdout


def test_python_cli_rejects_invalid_configuration(tmp_path: Path) -> None:
    path = tmp_path / "dependabot.yml"
    path.write_text(BASIC.replace("weekly", "never"), encoding="utf-8")
    result = subprocess.run(
        [
            sys.executable,
            str(ROOT / "scripts/lint_dependabot.py"),
            "--paths",
            str(path),
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=30,
    )
    assert result.returncode == 1, result.stdout + result.stderr
    assert "DEP005" in result.stdout


def test_powershell_test_launcher_forwards_filter(tmp_path: Path) -> None:
    executable = shutil.which("pwsh")
    if executable is None:
        pytest.skip("PowerShell is not installed")
    result = subprocess.run(
        [
            executable,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/tests/test-lint-dependabot.ps1"),
            "-TestFilter",
            "test_version_scalar_forms_and_order_are_accepted",
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "4 passed" in result.stdout
    assert "deselected" in result.stdout
