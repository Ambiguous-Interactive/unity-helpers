"""Check real YAML rules, literal Git paths, staged bytes and legacy invocation."""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from lint_yaml import main, sources

ROOT = Path(__file__).resolve().parents[2]
CONFIG = b"extends: default\nrules:\n  document-start: disable\n  truthy: disable\n"


def git(root: Path, *arguments: str) -> bytes:
    return subprocess.run(
        [str(shutil.which("git")), "-C", str(root), *arguments],
        capture_output=True,
        check=True,
        timeout=20,
    ).stdout


@pytest.fixture
def repository(tmp_path: Path) -> Path:
    root = tmp_path / "YAML repository café"
    root.mkdir()
    git(root, "init", "--quiet")
    git(root, "config", "core.autocrlf", "false")
    (root / ".yamllint.yaml").write_bytes(CONFIG)
    (root / ".gitignore").write_text(".yamllint.yaml\nignored/\n", encoding="utf-8")
    return root


@pytest.mark.parametrize("name", ["one.yml", "two.yaml", "with spaces café.yml", "--help.yml"])
@pytest.mark.parametrize("bad", [False, True])
def test_real_yaml_rules_and_literal_names(
    repository: Path, name: str, bad: bool, capsys: pytest.CaptureFixture[str]
) -> None:
    (repository / name).write_bytes(b"key: [\n" if bad else b"key: value\n")
    assert main(["--repo-root", str(repository), "--paths", str(repository / name)]) == int(bad)
    if bad:
        assert f"{name}:" in capsys.readouterr().out


@pytest.mark.skipif(sys.platform == "win32", reason="Windows paths forbid newlines")
def test_nul_inventory_and_diagnostics_preserve_newline_filename(
    repository: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    name = "first\n::error file=injected.yml"
    (repository / name).write_bytes(b"key: [\n")
    assert main(["--repo-root", str(repository)]) == 1
    output = capsys.readouterr().out
    assert "first%0A::error file=injected.yml:" in output
    assert "\n::error" not in output


def test_inventory_checks_tracked_and_untracked_without_ignored_trees(
    repository: Path,
) -> None:
    (repository / "tracked.yml").write_bytes(b"key: value\n")
    git(repository, "add", "--", "tracked.yml")
    (repository / "untracked.yaml").write_bytes(b"key: value\n")
    (repository / "ignored").mkdir()
    (repository / "ignored" / "invalid.yml").write_bytes(b"\xff")
    assert [name for name, _ in sources(repository, None, staged=False)] == [
        "tracked.yml",
        "untracked.yaml",
    ]
    assert main(["--repo-root", str(repository)]) == 0


@pytest.mark.parametrize("rename", [False, True])
def test_inventory_ignores_unstaged_deletions_but_preserves_explicit_and_index_checks(
    repository: Path, rename: bool
) -> None:
    retained = repository / "retained.yml"
    retained.write_bytes(b"key: value\n")
    removed = repository / "removed café.yml"
    removed.write_bytes(b"key: [\n")
    git(repository, "add", "--", retained.name, removed.name)
    if rename:
        renamed = repository / "renamed.yaml"
        removed.rename(renamed)
        assert main(["--repo-root", str(repository)]) == 1
        renamed.write_bytes(b"key: value\n")
    else:
        removed.unlink()
    assert main(["--repo-root", str(repository)]) == 0
    assert main(["--repo-root", str(repository), "--paths", removed.name]) == 1
    assert main(["--repo-root", str(repository), "--staged-only"]) == 1


@pytest.mark.parametrize("indexed_bad", [False, True])
@pytest.mark.parametrize("deleted", [False, True])
def test_staged_mode_lints_index_even_when_worktree_differs_or_is_absent(
    repository: Path, indexed_bad: bool, deleted: bool
) -> None:
    path = repository / "subject.yml"
    path.write_bytes(b"key: [\n" if indexed_bad else b"key: value\n")
    git(repository, "add", "--", path.name)
    if deleted:
        path.unlink()
    else:
        path.write_bytes(b"key: value\n" if indexed_bad else b"key: [\n")
    before = git(repository, "ls-files", "--stage", "-z")
    assert main(["--repo-root", str(repository), "--staged-only"]) == int(indexed_bad)
    assert git(repository, "ls-files", "--stage", "-z") == before


def test_staged_explicit_paths_are_literal_not_pathspecs(repository: Path) -> None:
    (repository / "part[1].yml").write_bytes(b"key: value\n")
    (repository / "part1.yml").write_bytes(b"key: [\n")
    git(repository, "add", "--", "part[1].yml", "part1.yml")
    assert main(["--repo-root", str(repository), "--staged-only", "--paths", "part[1].yml"]) == 0


def test_empty_staged_selection_is_expected_but_empty_repository_fails(
    repository: Path,
) -> None:
    assert main(["--repo-root", str(repository), "--staged-only"]) == 0
    assert main(["--repo-root", str(repository)]) == 1


@pytest.mark.parametrize("problem", ["missing", "directory", "invalid-utf8"])
def test_explicit_inputs_cannot_disappear_silently(repository: Path, problem: str) -> None:
    path = repository / "subject.yml"
    if problem == "directory":
        path.mkdir()
    elif problem == "invalid-utf8":
        path.write_bytes(b"\xff")
    assert main(["--repo-root", str(repository), "--paths", "subject.yml"]) == 1


@pytest.mark.parametrize("problem", ["missing", "invalid", "unknown-rule"])
def test_config_failures_are_reported(repository: Path, problem: str) -> None:
    config = repository / ".yamllint.yaml"
    if problem == "missing":
        config.unlink()
    else:
        config.write_bytes(
            b"rules: [\n" if problem == "invalid" else b"rules: {no-such-rule: enable}\n"
        )
    (repository / "subject.yml").write_bytes(b"key: value\n")
    assert main(["--repo-root", str(repository)]) == 1


def test_ignores_and_per_rule_ignores_use_repository_relative_paths(
    repository: Path,
) -> None:
    (repository / ".yamllint.yaml").write_bytes(CONFIG + b"ignore: 'skipped.yml'\n")
    (repository / "skipped.yml").write_bytes(b"key: [\n")
    (repository / "subject.yml").write_bytes(b"key: value\n")
    assert main(["--repo-root", str(repository)]) == 0
    (repository / ".yamllint.yaml").write_bytes(
        b"rules:\n  trailing-spaces:\n    ignore: 'subject.yml'\n"
    )
    (repository / "subject.yml").write_bytes(b"key: value  \n")
    (repository / "skipped.yml").unlink()
    assert main(["--repo-root", str(repository)]) == 0


def test_warnings_keep_non_strict_exit_contract(repository: Path) -> None:
    (repository / ".yamllint.yaml").write_bytes(b"rules:\n  trailing-spaces:\n    level: warning\n")
    (repository / "subject.yml").write_bytes(b"key: value  \n")
    assert main(["--repo-root", str(repository)]) == 0


@pytest.mark.parametrize("parent", [False, True])
def test_linked_files_and_directories_are_rejected(repository: Path, parent: bool) -> None:
    outside = repository.parent / "outside"
    outside.mkdir()
    (outside / "subject.yml").write_bytes(b"key: value\n")
    link = repository / ("linked" if parent else "subject.yml")
    try:
        link.symlink_to(outside if parent else outside / "subject.yml", target_is_directory=parent)
    except OSError as error:
        pytest.skip(f"This host cannot create symbolic links: {error}")
    name = "linked/subject.yml" if parent else "subject.yml"
    assert main(["--repo-root", str(repository), "--paths", name]) == 1


def test_git_inventory_failure_cannot_pass(
    repository: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setenv("PATH", "")
    assert main(["--repo-root", str(repository)]) == 1


def test_multiple_paths_and_bom_preserve_all_subjects(repository: Path) -> None:
    (repository / "one.yml").write_bytes(b"\xef\xbb\xbfkey: value\n")
    (repository / "two.yaml").write_bytes(b"key: [\n")
    assert main(["--repo-root", str(repository), "--paths", "one.yml", "two.yaml"]) == 1


@pytest.mark.parametrize("launcher", ["python", "powershell"])
def test_foreign_directory_launcher_reports_every_explicit_file(
    tmp_path: Path, launcher: str
) -> None:
    if launcher == "powershell" and shutil.which("pwsh") is None:
        pytest.skip("PowerShell is unavailable")
    if launcher == "powershell":
        command = [
            "pwsh",
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/lint-yaml.ps1"),
            "-Paths",
        ]
    else:
        command = [sys.executable, str(ROOT / "scripts/lint_yaml.py"), "--paths"]
    command += [str(ROOT / ".yamllint.yaml"), str(ROOT / "does-not-exist.yml")]
    result = subprocess.run(command, cwd=tmp_path, capture_output=True, timeout=60, check=False)
    assert result.returncode == 1
    assert b"does-not-exist.yml" in result.stdout + result.stderr


def test_environment_does_not_change_input_scope(
    repository: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setenv("YAMLLINT_CONFIG_FILE", str(repository / "ignored-config.yml"))
    (repository / "subject.yml").write_bytes(b"key: [\n")
    assert main(["--repo-root", str(repository)]) == 1


def test_staged_blob_does_not_follow_an_unstaged_worktree_link(
    repository: Path,
) -> None:
    path = repository / "subject.yml"
    path.write_bytes(b"key: value\n")
    git(repository, "add", "--", path.name)
    outside = repository.parent / "outside.yml"
    outside.write_bytes(b"key: [\n")
    path.unlink()
    try:
        path.symlink_to(outside)
    except OSError as error:
        pytest.skip(f"This host cannot create symbolic links: {error}")
    assert main(["--repo-root", str(repository), "--staged-only"]) == 0
    assert outside.read_bytes() == b"key: [\n"


@pytest.mark.parametrize("content", [b"key: [\n", b"\xff"])
def test_config_ignored_selection_is_counted_as_an_explicit_exclusion(
    repository: Path, capsys: pytest.CaptureFixture[str], content: bytes
) -> None:
    (repository / ".yamllint.yaml").write_bytes(CONFIG + b"ignore: 'subject.yml'\n")
    (repository / "subject.yml").write_bytes(content)
    assert main(["--repo-root", str(repository), "--verbose"]) == 0
    assert "Checked 0 YAML file(s), ignored 1" in capsys.readouterr().out


def test_staged_link_is_not_treated_as_yaml_content(repository: Path) -> None:
    outside = repository.parent / "outside.yml"
    outside.write_bytes(b"key: value\n")
    path = repository / "subject.yml"
    try:
        path.symlink_to(outside)
    except OSError as error:
        pytest.skip(f"This host cannot create symbolic links: {error}")
    git(repository, "add", "--", path.name)
    assert main(["--repo-root", str(repository), "--staged-only"]) == 1
