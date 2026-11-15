"""Exercise the fence repairer against frozen PowerShell observations and real Git paths."""

from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path
from typing import TypedDict, cast

import pytest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))

from fix_markdown_fence_languages import main, markdown_paths, repair  # noqa: E402


class Observation(TypedDict):
    input: str
    output: str
    count: int


class Oracle(TypedDict):
    source: str
    sourceSha256: str
    observations: list[Observation]


ORACLE = cast(
    "Oracle",
    json.loads(
        (Path(__file__).parent / "fixtures/markdown-fence-original.json").read_text(
            encoding="utf-8"
        )
    ),
)


def test_original_source_identity() -> None:
    assert hashlib.sha256(ORACLE["source"].encode()).hexdigest() == ORACLE["sourceSha256"]
    assert len(ORACLE["observations"]) == 74
    assert "function Repair-MarkdownFenceLanguages" in ORACLE["source"]


@pytest.mark.parametrize("observation", ORACLE["observations"])
def test_frozen_original_observations(observation: Observation) -> None:
    assert repair(observation["input"]) == (observation["output"], observation["count"])
    assert repair(observation["output"])[1] == 0


def git(root: Path, *arguments: str) -> str:
    command = shutil.which("git")
    assert command is not None
    result = subprocess.run(
        [command, "-C", str(root), *arguments], capture_output=True, encoding="utf-8", check=True
    )
    return result.stdout


def test_staged_discovery_preserves_spaces_and_does_not_stage_repairs(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    git(tmp_path, "init", "--quiet")
    path = tmp_path / "a quoted ü document.md"
    original = "```\ngit status\n```\n"
    path.write_text(original, encoding="utf-8")
    git(tmp_path, "add", "--", path.name)
    unstaged = tmp_path / "unstaged.md"
    unstaged.write_text(original, encoding="utf-8")
    monkeypatch.chdir(tmp_path)
    assert main([]) == 0
    assert path.read_text(encoding="utf-8") == "```bash\ngit status\n```\n"
    assert unstaged.read_text(encoding="utf-8") == original
    assert git(tmp_path, "show", ":" + path.name) == original


def test_explicit_paths_keep_newlines_and_ignore_unselected_files(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    path = tmp_path / "source.MARKDOWN"
    other = tmp_path / "other.txt"
    original = b"\xef\xbb\xbf~~~\r\nusing System;\r\n~~~\r\n"
    path.write_bytes(original)
    other.write_bytes(original)
    monkeypatch.chdir(tmp_path)
    assert main(["--paths", str(path), str(other), str(tmp_path / "absent.md")]) == 0
    assert path.read_bytes() == b"~~~csharp\r\nusing System;\r\n~~~\r\n"
    assert other.read_bytes() == original


def test_selected_paths_are_not_replaced_by_a_whole_repository_scan(tmp_path: Path) -> None:
    valid = tmp_path / "valid.md"
    valid.write_text("```\nx: 1\n```\n", encoding="utf-8")
    assert markdown_paths(tmp_path, ["missing.md", "file.txt", " "]) == []
    assert markdown_paths(tmp_path, [valid.name, str(valid)]) == [valid]


def test_powershell_wrapper_forwards_every_path(tmp_path: Path) -> None:
    pwsh = shutil.which("pwsh")
    assert pwsh is not None
    paths = [tmp_path / "one.md", tmp_path / "two with space.md"]
    for path in paths:
        path.write_text("```\nx: 1\n```\n", encoding="utf-8")
    result = subprocess.run(
        [
            pwsh,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/fix-markdown-fence-languages.ps1"),
            "-Paths",
            *map(str, paths),
        ],
        cwd=tmp_path,
        capture_output=True,
        encoding="utf-8",
        check=False,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "across 2 file(s)" in result.stdout
    for path in paths:
        assert path.read_text(encoding="utf-8").startswith("```yaml\n")
