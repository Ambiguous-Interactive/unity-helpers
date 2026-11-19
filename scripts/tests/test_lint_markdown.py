"""Exercise Markdown inventory selection, bounded arguments, and the native lint command."""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import lint_markdown

ROOT = Path(__file__).resolve().parents[2]
TIMEOUT_SECONDS = 30
FENCE = "\x60" * 3


def write(root: Path, name: str, content: str = "# Valid\n") -> None:
    path = root / name
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8", newline="\n")


def git(root: Path, *arguments: str) -> None:
    executable = shutil.which("git")
    assert executable is not None
    result = subprocess.run(
        [executable, *arguments],
        cwd=root,
        check=False,
        capture_output=True,
        timeout=TIMEOUT_SECONDS,
    )
    assert result.returncode == 0, result.stderr


@pytest.fixture
def repository(tmp_path: Path) -> Path:
    root = tmp_path / "Markdown checkout"
    root.mkdir()
    git(root, "init", "--quiet")
    write(root, ".gitignore", "progress/\nnode_modules/\ntracked-ignore.md\n")
    write(root, ".markdownlintignore", "progress/\nnode_modules/\nexcluded.md\n")
    write(root, ".markdownlint.json", '{"default":true,"MD041":false}\n')
    write(root, "tracked-ignore.md")
    git(root, "add", "--force", "tracked-ignore.md")
    write(root, "tracked.md")
    git(root, "add", "tracked.md")
    write(root, "new document.markdown")
    for name in (
        "excluded.md",
        "progress/ignored.md",
        ".hidden.md",
        ".hidden/nested.md",
        "docs/.hidden/nested.md",
    ):
        write(root, name, f"{FENCE}\ninvalid\n{FENCE}\n")
    write(root, "upper.MD")
    write(root, "deleted.md")
    git(root, "add", "deleted.md")
    (root / "deleted.md").unlink()
    link = root / "node_modules"
    target = ROOT / "node_modules"
    if os.name == "nt":
        command = os.environ.get("COMSPEC", "cmd.exe")
        subprocess.run(
            [command, "/d", "/c", "mklink", "/J", str(link), str(target)],
            check=True,
            capture_output=True,
            timeout=TIMEOUT_SECONDS,
        )
    else:
        link.symlink_to(target, target_is_directory=True)
    return root


@pytest.mark.parametrize("insensitive", [False, True])
def test_candidates_filter_hidden_paths_and_preserve_node_order(insensitive: bool) -> None:
    paths = (
        "a.md",
        "a.md",
        "docs/a.markdown",
        ".hidden.md",
        "docs/.hidden/a.md",
        "a.txt",
        "UPPER.MD",
        "\ue000.md",
        "\U00010000.md",
        "",
    )
    expected = ["a.md", "docs/a.markdown", "\U00010000.md", "\ue000.md"]
    if insensitive:
        expected.insert(0, "UPPER.MD")
    assert (
        lint_markdown.markdown_candidates("\0".join(paths).encode(), insensitive=insensitive)
        == expected
    )


def test_inventory_skips_hidden_ignored_and_deleted_files(repository: Path) -> None:
    expected = ["new document.markdown", "tracked-ignore.md", "tracked.md"]
    if sys.platform in ("win32", "darwin"):
        expected.append("upper.MD")
    assert lint_markdown.discover_markdown_files(repository) == expected


def test_native_ignore_preserves_negation_and_unicode_case(repository: Path) -> None:
    write(repository, ".markdownlintignore", "docs/*\n!docs/keep.md\ncafé.md\n")
    paths = ["docs/skip.md", "docs/keep.md", "CAFÉ.md", "café.md", "valid.md"]
    assert lint_markdown.apply_markdownlint_ignore(repository, paths) == [
        "docs/keep.md",
        "valid.md",
    ]


def test_batch_uses_utf8_argument_budget() -> None:
    names = [f"docs/{index:03d}-{'é' * 100}.md" for index in range(500)]
    batches = lint_markdown.batch_markdown_files(names)
    assert len(batches) > 1
    assert [file for batch in batches for file in batch] == names
    assert all(sum(len(file.encode("utf-8")) + 3 for file in batch) <= 12_000 for batch in batches)
    with pytest.raises(ValueError, match="argument budget"):
        lint_markdown.batch_markdown_files(["a" * 12_000 + ".md"])


def test_native_lint_command_uses_git_inventory(
    repository: Path, capfd: pytest.CaptureFixture[str]
) -> None:
    expected = lint_markdown.discover_markdown_files(repository)
    assert lint_markdown.main(repository) == 0
    assert f"Linting {len(expected)} Markdown files" in capfd.readouterr().out

    write(repository, "new document.markdown", f"# Invalid\n\n{FENCE}\nmissing language\n{FENCE}\n")
    assert lint_markdown.main(repository) == 1
    assert "new document.markdown:3 error MD040" in capfd.readouterr().err

    for file in expected:
        (repository / file).unlink()
    assert lint_markdown.main(repository) == 1
    assert "zero subjects" in capfd.readouterr().err


def test_inventory_failure_fails_before_lint(
    tmp_path: Path, capfd: pytest.CaptureFixture[str]
) -> None:
    assert lint_markdown.main(tmp_path) == 1
    captured = capfd.readouterr()
    assert captured.out == ""
    assert "Markdown inventory failed" in captured.err


def test_legacy_lint_launcher_reports_actual_failure(repository: Path) -> None:
    launcher = "scripts/lint-markdown.mjs"
    for file in (
        launcher,
        "scripts/lint_markdown.py",
        "scripts/run_node_bin.py",
        "scripts/cli_encoding.py",
        "scripts/python~/pyproject.toml",
        "scripts/python~/uv.lock",
    ):
        destination = repository / file
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / file, destination)
    write(repository, "new document.markdown", f"# Invalid\n\n{FENCE}\nmissing language\n{FENCE}\n")
    node = shutil.which("node")
    assert node is not None
    result = subprocess.run(
        [node, str(repository / launcher)],
        cwd=repository,
        check=False,
        capture_output=True,
        timeout=TIMEOUT_SECONDS,
    )
    assert result.returncode == 1
    assert b"Linting " in result.stdout
    assert b"new document.markdown:3 error MD040" in result.stderr
