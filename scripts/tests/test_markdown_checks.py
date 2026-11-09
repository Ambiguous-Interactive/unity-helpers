"""Run the standalone documentation checks through their actual command boundaries."""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast

import pytest
import yaml

ROOT = Path(__file__).resolve().parents[2]
CHECKS = (
    "check_markdown_links.py",
    "check_markdown_url_encoding.py",
    "validate_markdown_links.py",
)


def test_mkdocs_output_is_unity_excluded_and_pages_uploads_that_output() -> None:
    config = cast(
        "dict[str, object]",
        yaml.load(
            (ROOT / "mkdocs.yml").read_text(encoding="utf-8"),
            Loader=yaml.BaseLoader,  # noqa: S506 - BaseLoader constructs only basic nodes.
        ),
    )
    output = Path(str(config.get("site_dir", "site")))
    assert not output.is_absolute()
    assert any(part.startswith(".") or part.endswith("~") for part in output.parts)
    assert ".." not in output.parts
    workflow = (ROOT / ".github/workflows/deploy-pages.yml").read_text(encoding="utf-8")
    assert f"path: ./{output.as_posix()}\n" in workflow
    assert f'"{output.as_posix()}/index.html"' in workflow
    assert "mkdocs build --strict" in workflow


@pytest.mark.skipif(sys.platform == "win32", reason="The documentation workflow runs Bash on Linux")
@pytest.mark.parametrize("state", ["ready", "empty", "missing"])
def test_docs_workflow_checks_the_actual_deployable_output(tmp_path: Path, state: str) -> None:
    workflow = cast(
        "dict[str, object]",
        yaml.load(
            (ROOT / ".github/workflows/validate-docs.yml").read_text(encoding="utf-8"),
            Loader=yaml.BaseLoader,  # noqa: S506 - BaseLoader constructs only basic nodes.
        ),
    )
    jobs = cast("dict[str, dict[str, object]]", workflow["jobs"])
    steps = cast("list[dict[str, object]]", jobs["build-mkdocs-test"]["steps"])
    selected = [step for step in steps if step.get("name") == "Verify build output"]
    assert len(selected) == 1
    legacy = tmp_path / "site"
    legacy.mkdir()
    (legacy / "index.html").write_text("Old output", encoding="utf-8")
    if state != "missing":
        output = tmp_path / ".artifacts/site"
        output.mkdir(parents=True)
        if state == "ready":
            (output / "index.html").write_text("Current output", encoding="utf-8")
    bash_path = shutil.which("bash")
    assert bash_path is not None, "Bash is required to exercise the actual workflow step"
    result = subprocess.run(
        [bash_path, "-e", "-o", "pipefail", "-c", str(selected[0]["run"])],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=20,
    )
    assert result.returncode == (0 if state == "ready" else 1), result.stdout + result.stderr


def run(name: str, root: Path) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [sys.executable, str(ROOT / ".github/scripts" / name), str(root)],
        cwd=root.parent,
        capture_output=True,
        text=True,
        check=False,
        timeout=20,
    )


@pytest.mark.parametrize("name", CHECKS)
def test_valid_documentation(name: str, tmp_path: Path) -> None:
    (tmp_path / "target.md").write_text(
        "# Target\n\n## Repeated\n\n## Repeated\n", encoding="utf-8"
    )
    (tmp_path / "source.md").write_text(
        "# Source\n\n[Read the target](./target.md#repeated-1)\n"
        "[Jump here](#source)\n[External](https://example.com/a+b.md)\n"
        "![Image](./missing.png)\n",
        encoding="utf-8",
    )
    result = run(name, tmp_path)
    assert result.returncode == 0, result.stdout + result.stderr
    assert result.stdout == ""
    assert result.stderr == ""


@pytest.mark.parametrize("name", CHECKS)
@pytest.mark.parametrize("kind", ["missing", "empty", "no_markdown", "excluded_only"])
def test_refuses_empty_or_missing_corpora(name: str, kind: str, tmp_path: Path) -> None:
    root = tmp_path / "input"
    if kind != "missing":
        root.mkdir()
    if kind == "no_markdown":
        (root / "data.txt").write_text("text", encoding="utf-8")
    if kind == "excluded_only":
        excluded = root / "node_modules"
        excluded.mkdir()
        (excluded / "doc.md").write_text("# Hidden", encoding="utf-8")
    result = run(name, root)
    assert result.returncode == 1
    assert "not found" in result.stderr or "No markdown sources" in result.stderr


@pytest.mark.parametrize("name", CHECKS)
def test_unreadable_source_is_a_failure(name: str, tmp_path: Path) -> None:
    (tmp_path / "broken.md").write_bytes(b"\xff\xfe\xff")
    result = run(name, tmp_path)
    assert result.returncode == 1
    assert "Cannot read markdown source" in result.stderr
    assert "broken.md" in result.stderr


@pytest.mark.parametrize("name", CHECKS)
@pytest.mark.parametrize("directory", [".git", "node_modules", ".vs"])
def test_prunes_excluded_directories(name: str, directory: str, tmp_path: Path) -> None:
    (tmp_path / "valid.md").write_text("# Visible", encoding="utf-8")
    hidden = tmp_path / "nested" / directory
    hidden.mkdir(parents=True)
    (hidden / "broken.md").write_bytes(b"\xff")
    result = run(name, tmp_path)
    assert result.returncode == 0, result.stdout + result.stderr


@pytest.mark.parametrize(
    ("name", "content", "diagnostic"),
    [
        ("check_markdown_links.py", "[target.md](./target.md)", "human-readable"),
        ("check_markdown_links.py", "[some/target](./target.md)", "human-readable"),
        ("check_markdown_url_encoding.py", "[A readable title](./a+b.md)", "Unencoded"),
        ("check_markdown_url_encoding.py", "[image]: ./a+b.png", "link definition"),
        (
            "validate_markdown_links.py",
            "[A readable title](./missing.md)",
            "target file not found",
        ),
        (
            "validate_markdown_links.py",
            "[A readable title](#missing)",
            "dangling anchor",
        ),
        (
            "validate_markdown_links.py",
            "[A readable title](./target.md#missing)",
            "dangling anchor",
        ),
    ],
)
def test_reports_real_link_defects(
    name: str, content: str, diagnostic: str, tmp_path: Path
) -> None:
    (tmp_path / "target.md").write_text("# Target\n", encoding="utf-8")
    (tmp_path / "source.md").write_text(content, encoding="utf-8")
    result = run(name, tmp_path)
    assert result.returncode == 1
    assert diagnostic in result.stdout
    assert "source.md:1:" in result.stdout


@pytest.mark.parametrize("name", CHECKS)
def test_encoded_unicode_paths_and_titles(name: str, tmp_path: Path) -> None:
    (tmp_path / "two caf\u00e9 words.md").write_text("# Target\n", encoding="utf-8")
    source = tmp_path / "source.md"
    source.write_text(
        '[Read this guide](./two%20caf%C3%A9%20words.md#target "a title")\n',
        encoding="utf-8",
    )
    before = source.read_bytes()
    result = run(name, tmp_path)
    assert result.returncode == 0, result.stdout + result.stderr
    assert source.read_bytes() == before


def test_validator_accepts_explicit_files_and_setext_anchors(tmp_path: Path) -> None:
    source = tmp_path / "source.md"
    source.write_text("Title\n=====\n\n[Jump](#title)\n", encoding="utf-8")
    result = run("validate_markdown_links.py", source)
    assert result.returncode == 0, result.stdout + result.stderr


@pytest.mark.parametrize("name", CHECKS)
@pytest.mark.parametrize("directory", ["blocked", ".git", "node_modules", ".vs"])
def test_directory_listing_errors_fail_unless_pruned(
    name: str, directory: str, tmp_path: Path
) -> None:
    (tmp_path / "visible.md").write_text("# Visible\n", encoding="utf-8")
    denied = tmp_path / directory
    denied.mkdir()
    (denied / "hidden.md").write_text("# Hidden\n", encoding="utf-8")
    code = """
import os
from pathlib import Path
import runpy
import sys
original = os.scandir
denied = Path(sys.argv.pop()).resolve()
def scandir(path):
    if Path(path).resolve() == denied:
        raise PermissionError(13, 'controlled listing failure', str(path))
    return original(path)
os.scandir = scandir
sys.argv = sys.argv[1:]
runpy.run_path(sys.argv[0], run_name='__main__')
"""
    result = subprocess.run(
        [
            sys.executable,
            "-c",
            code,
            str(ROOT / ".github/scripts" / name),
            str(tmp_path),
            str(denied),
        ],
        capture_output=True,
        text=True,
        check=False,
        timeout=20,
    )
    assert result.returncode == (1 if directory == "blocked" else 0), result.stdout + result.stderr
    if directory == "blocked":
        assert "controlled listing failure" in result.stderr
        assert "blocked" in result.stderr
