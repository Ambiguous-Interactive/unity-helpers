"""Preserve documentation-ignore policies using isolated real Git repositories."""

from __future__ import annotations

import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import lint_gitignore_docs as lint

ROOT = Path(__file__).resolve().parents[2]


@dataclass(frozen=True)
class Case:
    name: str
    ignore: str = ""
    docs: tuple[str, ...] = ()
    llm: tuple[str, ...] = ()
    nav: str = ""
    expected: int = 0
    diagnostic: str = ""


CASES = (
    Case("NoDocs_NoGitignore_Passes"),
    Case(
        "SafeGitignore_Passes",
        "node_modules/\n*.log\n.vs/",
        ("index.md", "features/inspector/overview.md"),
    ),
    Case(
        "SafeWildcards_Passes",
        "*.log\n*.tmp\n*.dll\nfailed-tests-*.txt",
        ("index.md", "features/editor-tools/failed-tests-exporter.md"),
    ),
    Case(
        "ValidMkdocsNav_Passes",
        "*.log",
        ("index.md", "features/guide.md"),
        nav="nav:\n  - Home: index.md\n  - Guide: features/guide.md",
    ),
    Case(
        "Regression_EmptyIgnoredSet_DoesNotCrash",
        "*.log\n*.tmp",
        ("index.md", "guides/getting-started.md"),
    ),
    Case(
        "BroadWildcard_Fails",
        "failed-tests-*",
        ("index.md", "features/editor-tools/failed-tests-exporter.md"),
        expected=1,
        diagnostic="failed-tests-exporter.md",
    ),
    Case("IgnoreAllMd_Fails", "*.md", ("index.md",), expected=1, diagnostic="index.md"),
    Case(
        "MissingNavFile_Fails",
        docs=("index.md",),
        nav="nav:\n  - Home: index.md\n  - Missing: missing.md",
        expected=1,
        diagnostic="Nav references missing file",
    ),
    Case(
        "GitignoredNavFile_Fails",
        "failed-tests-*",
        ("features/editor-tools/failed-tests-exporter.md",),
        nav="nav:\n  - Failed: features/editor-tools/failed-tests-exporter.md",
        expected=1,
        diagnostic="Nav references gitignored file",
    ),
    Case("NegationPattern_Passes", "*.md\n!docs/**", ("index.md", "guide.md")),
    Case("AnchoredPattern_Passes", "/Unity/*\n/build/*", ("index.md", "features/guide.md")),
    Case("EmptyNoDocs_Passes", "*.md"),
    Case(
        "ExtensionScopedWildcard_Passes",
        "failed-tests-*.txt\nfailed-tests-*.txt.meta",
        ("index.md", "features/editor-tools/failed-tests-exporter.md"),
    ),
    Case("SafeLlm_Passes", "*.log\n*.tmp", llm=("context.md", "skills/create-test.md")),
    Case(
        "BroadWildcard_MatchesLlm_Fails",
        "context.*",
        llm=("context.md", "skills/create-test.md"),
        expected=1,
        diagnostic="context.md",
    ),
    Case(
        "SafeDocsAndLlm_Passes",
        "*.log\nfailed-tests-*.txt",
        ("index.md", "features/editor-tools/failed-tests-exporter.md"),
        ("context.md", "skills/create-test.md"),
    ),
    Case(
        "LlmOnly_NoDocs_Fails",
        "context.*",
        llm=("context.md",),
        expected=1,
        diagnostic="context.md",
    ),
)


def git(root: Path, *arguments: str) -> bytes:
    executable = shutil.which("git")
    assert executable is not None
    result = subprocess.run(
        [executable, "-C", str(root), *arguments], capture_output=True, check=False, timeout=30
    )
    assert result.returncode == 0, result.stderr.decode("utf-8", errors="replace")
    return result.stdout


def write(root: Path, relative: str, content: str = "fixture") -> Path:
    path = root / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")
    return path


@pytest.fixture
def repository(tmp_path: Path) -> Path:
    root = tmp_path / "repository ü with spaces"
    root.mkdir()
    git(root, "init", "--quiet")
    git(root, "config", "core.excludesFile", str(tmp_path / "empty-global-ignore"))
    return root


@pytest.mark.parametrize("case", CASES, ids=[case.name for case in CASES])
def test_original_policies(
    repository: Path, case: Case, capsys: pytest.CaptureFixture[str]
) -> None:
    if case.ignore:
        write(repository, ".gitignore", case.ignore)
    for name in case.docs:
        write(repository, "docs/" + name)
    for name in case.llm:
        write(repository, ".llm/" + name)
    if case.nav:
        write(repository, "mkdocs.yml", case.nav)
    assert lint.check(repository, verbose=True) == case.expected
    output = capsys.readouterr().out
    if case.diagnostic:
        assert case.diagnostic in output
    assert "null-valued expression" not in output


def test_tracked_and_ignored_file_fails_without_changing_index(
    repository: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    write(repository, "progress.md")
    git(repository, "add", "--", "progress.md")
    write(repository, ".gitignore", "progress.md\n")
    index = repository / ".git/index"
    before = index.read_bytes()
    assert lint.check(repository) == 1
    assert "Tracked file is also gitignored: progress.md" in capsys.readouterr().out
    assert index.read_bytes() == before


def test_ignored_untracked_file_passes(repository: Path) -> None:
    write(repository, ".gitignore", "progress.md\n")
    write(repository, "progress.md")
    assert lint.check(repository) == 0


def test_negated_tracked_file_passes(repository: Path) -> None:
    write(repository, ".gitignore", "*.md\n!kept.md\n")
    write(repository, "kept.md")
    git(repository, "add", "--", "kept.md")
    assert lint.check(repository) == 0


def test_global_ignore_does_not_fail_tracked_policy(repository: Path, tmp_path: Path) -> None:
    write(repository, "progress.md")
    git(repository, "add", "--", "progress.md")
    global_ignore = tmp_path / "global-ignore"
    global_ignore.write_text("progress.md\n", encoding="utf-8")
    git(repository, "config", "core.excludesFile", str(global_ignore))
    assert lint.check(repository) == 0


@pytest.mark.parametrize("relative", ["progress/session.md", "progress.meta"])
def test_progress_artifact_fails_without_ignore_rule(
    repository: Path, relative: str, capsys: pytest.CaptureFixture[str]
) -> None:
    write(repository, relative)
    git(repository, "add", "--", relative)
    assert lint.check(repository) == 1
    assert relative in capsys.readouterr().out


@pytest.mark.parametrize("name", ["résumé with spaces.md", "line\nbreak.md", "[brackets].md"])
def test_git_paths_remain_nul_delimited(
    repository: Path, name: str, capsys: pytest.CaptureFixture[str]
) -> None:
    if sys.platform == "win32" and "\n" in name:
        pytest.skip("Windows does not accept line feeds in file names")
    write(repository, "docs/" + name)
    write(repository, ".gitignore", "*.md\n")
    assert lint.check(repository) == 1
    assert name in capsys.readouterr().out


def test_nested_ignore_and_negation_use_git_rules(repository: Path) -> None:
    write(repository, "docs/.gitignore", "*.md\n!kept.md\n")
    write(repository, "docs/kept.md")
    assert lint.check(repository) == 0
    write(repository, "docs/hidden.md")
    assert lint.check(repository) == 1


def test_nav_supports_quoted_paths_fragments_and_custom_root(repository: Path) -> None:
    write(repository, "manual/with spaces.md")
    write(
        repository,
        "mkdocs.yml",
        "docs_dir: manual\nnav:\n  - Guide: 'with spaces.md#anchor'\n  - External: https://example.com/missing.md\n",
    )
    assert lint.check(repository) == 0


def test_missing_quoted_nav_is_not_silently_skipped(
    repository: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    write(repository, "mkdocs.yml", "nav:\n  - Missing: 'missing file.md'\n")
    assert lint.check(repository) == 1
    assert "missing file.md" in capsys.readouterr().out


def test_recursive_yaml_nav_is_bounded(repository: Path) -> None:
    write(repository, "mkdocs.yml", "nav: &self\n  - Recursive: *self\n")
    assert lint.check(repository) == 0


@pytest.mark.parametrize("config", ["nav: [", "- invalid", "nav: [../../outside.md]"])
def test_invalid_or_escaping_navigation_fails(repository: Path, config: str) -> None:
    write(repository, "mkdocs.yml", config)
    assert lint.main(["--repo-root", str(repository)]) == 1


def test_missing_repository_is_not_a_success(tmp_path: Path) -> None:
    assert lint.main(["--repo-root", str(tmp_path / "missing")]) == 1
    assert lint.main(["--repo-root", str(tmp_path)]) == 1


def test_caller_subdirectory_still_checks_the_repository_root(repository: Path) -> None:
    child = repository / "nested"
    child.mkdir()
    write(repository, "docs/hidden.md")
    write(repository, ".gitignore", "*.md\n")
    assert lint.check(child) == 1


@pytest.mark.parametrize("launcher", [False, True])
@pytest.mark.parametrize("ignored", [False, True])
def test_actual_cli_from_another_directory(
    repository: Path, tmp_path: Path, launcher: bool, ignored: bool
) -> None:
    write(repository, "docs/index.md")
    write(repository, ".gitignore", "*.md\n" if ignored else "*.log\n")
    if launcher:
        shell = shutil.which("pwsh")
        if shell is None:
            pytest.skip("PowerShell is unavailable")
        command = [
            shell,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/lint-gitignore-docs.ps1"),
            "-RepoRoot",
            str(repository),
            "-VerboseOutput",
        ]
    else:
        command = [
            sys.executable,
            str(ROOT / "scripts/lint_gitignore_docs.py"),
            "--repo-root",
            str(repository),
            "--verbose",
        ]
    result = subprocess.run(
        command, cwd=tmp_path, capture_output=True, text=True, check=False, timeout=60
    )
    assert result.returncode == int(ignored), result.stdout + result.stderr
    assert "Scanned 1 protected files" in result.stdout


def test_legacy_test_launcher_runs_selected_control(tmp_path: Path) -> None:
    shell = shutil.which("pwsh")
    if shell is None:
        pytest.skip("PowerShell is unavailable")
    command = [
        shell,
        "-NoProfile",
        "-File",
        str(ROOT / "scripts/tests/test-gitignore-docs.ps1"),
        "-TestFilter",
        "test_ignored_untracked_file_passes",
    ]
    result = subprocess.run(
        command, cwd=tmp_path, capture_output=True, text=True, check=False, timeout=60
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "1 passed" in result.stdout
