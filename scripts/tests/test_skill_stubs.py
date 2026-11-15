"""Frozen discovery-generator output and filesystem ownership controls."""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import TypedDict, cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from generate_agent_skill_stubs import GENERATOR, MARKER, generate, main


class Observation(TypedDict):
    name: str
    inputs: dict[str, str]
    status: int
    stdout: str
    error: str
    files: dict[str, str]


class Original(TypedDict):
    sourceCommit: str
    sourceSha256: str
    source: str
    observations: list[Observation]


ROOT = Path(__file__).resolve().parents[2]
ORIGINAL = cast(
    "Original",
    json.loads(
        (Path(__file__).parent / "fixtures/skill-stubs-original.json").read_text(encoding="utf-8")
    ),
)
CASES = [case for case in ORIGINAL["observations"] if case["name"] != "empty"]


def seed(root: Path, sources: dict[str, str] | None = None) -> None:
    directory = root / ".llm" / "skills"
    directory.mkdir(parents=True)
    for filename, text in (
        sources if sources is not None else {"demo.md": "<!-- trigger: demo | Demo -->"}
    ).items():
        (directory / filename).write_bytes(text.encode("utf-8"))


def snapshot(root: Path) -> dict[str, str]:
    return {
        path.relative_to(root).as_posix(): path.read_bytes().decode("utf-8")
        for destination in (".agents", ".claude")
        for path in (root / destination).rglob("SKILL.md")
    }


@pytest.mark.parametrize("case", CASES, ids=[case["name"] for case in CASES])
def test_frozen_original_output(
    tmp_path: Path, capsys: pytest.CaptureFixture[str], case: Observation
) -> None:
    seed(tmp_path, case["inputs"])

    status = main(["--write"], root=tmp_path)

    output = capsys.readouterr()
    assert status == case["status"]
    assert output.out == case["stdout"]
    if case["error"]:
        assert output.err.strip() == case["error"]
    assert snapshot(tmp_path) == case["files"]


def test_frozen_executable_replays_its_own_observation(tmp_path: Path) -> None:
    assert hashlib.sha256(ORIGINAL["source"].encode()).hexdigest() == ORIGINAL["sourceSha256"]
    case = CASES[0]
    seed(tmp_path, case["inputs"])
    script = tmp_path / GENERATOR
    script.parent.mkdir()
    script.write_bytes(ORIGINAL["source"].encode())
    node = shutil.which("node")
    assert node is not None

    process = subprocess.run(
        [node, str(script), "--write"],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=30,
    )

    assert process.returncode == case["status"]
    assert process.stdout == case["stdout"]
    assert snapshot(tmp_path) == case["files"]


@pytest.mark.parametrize("mode", ["--check", "--write"])
def test_zero_subjects_fail_before_removing_any_stubs(tmp_path: Path, mode: str) -> None:
    seed(tmp_path, {})
    retained = tmp_path / ".agents/skills/old/SKILL.md"
    retained.parent.mkdir(parents=True)
    retained.write_text(MARKER, encoding="utf-8")

    assert main([mode], root=tmp_path) == 1

    assert retained.read_text(encoding="utf-8") == MARKER
    original = next(case for case in ORIGINAL["observations"] if case["name"] == "empty")
    assert original["status"] == 0


@pytest.mark.parametrize("arguments", [[], ["--help"], ["--invalid"]])
def test_invalid_mode_keeps_usage_exit_code(
    tmp_path: Path, capsys: pytest.CaptureFixture[str], arguments: list[str]
) -> None:
    assert main(arguments, root=tmp_path) == 2
    assert capsys.readouterr().err == f"Usage: node {GENERATOR} --write|--check\n"


def test_check_is_read_only_and_write_repairs_drift(tmp_path: Path) -> None:
    seed(tmp_path)
    assert generate(tmp_path, write=True) == 0
    file = tmp_path / ".agents/skills/demo/SKILL.md"
    expected = file.read_bytes()
    file.write_text("stale", encoding="utf-8")

    assert generate(tmp_path, write=False) == 1
    assert file.read_bytes() == b"stale"
    assert generate(tmp_path, write=True) == 0
    assert file.read_bytes() == expected
    assert generate(tmp_path, write=False) == 0


def test_write_preserves_unchanged_file_timestamp(tmp_path: Path) -> None:
    seed(tmp_path)
    assert generate(tmp_path, write=True) == 0
    file = tmp_path / ".agents/skills/demo/SKILL.md"
    os.utime(file, ns=(1_000_000_000, 1_000_000_000))

    assert generate(tmp_path, write=True) == 0

    assert file.stat().st_mtime_ns == 1_000_000_000


@pytest.mark.parametrize("extra", [False, True])
@pytest.mark.parametrize("generated", [False, True])
def test_only_generated_single_file_stale_directories_are_deleted(
    tmp_path: Path, extra: bool, generated: bool
) -> None:
    seed(tmp_path)
    file = tmp_path / ".agents/skills/old/SKILL.md"
    file.parent.mkdir(parents=True)
    text = MARKER if generated else "User skill"
    file.write_text(text, encoding="utf-8")
    if extra:
        (file.parent / "notes.txt").write_text("User notes", encoding="utf-8")

    status = generate(tmp_path, write=True)

    removed = generated and not extra
    assert status == (0 if removed else 1)
    assert file.exists() != removed
    if not removed:
        assert file.read_text(encoding="utf-8") == text


@pytest.mark.skipif(
    os.name == "nt",
    reason="POSIX symbolic-link control; Windows junctions have a separate control",
)
@pytest.mark.parametrize("location", [".llm/skills", ".agents/skills", ".agents/skills/demo"])
def test_linked_directories_cannot_redirect_reads_or_writes(tmp_path: Path, location: str) -> None:
    root = tmp_path / "repository"
    outside = tmp_path / "outside"
    outside.mkdir()
    marker = outside / "SKILL.md"
    marker.write_text("User content", encoding="utf-8")
    if location != ".llm/skills":
        seed(root)
    link = root / location
    link.parent.mkdir(parents=True, exist_ok=True)
    link.symlink_to(outside, target_is_directory=True)

    assert main(["--write"], root=root) == 1

    assert marker.read_text(encoding="utf-8") == "User content"
    assert sorted(path.name for path in outside.iterdir()) == ["SKILL.md"]


@pytest.mark.skipif(os.name != "nt", reason="Windows junction control")
@pytest.mark.parametrize("location", [".llm/skills", ".agents/skills", ".agents/skills/demo"])
def test_windows_junctions_cannot_redirect_reads_or_writes(tmp_path: Path, location: str) -> None:
    root = tmp_path / "repository"
    outside = tmp_path / "outside"
    outside.mkdir()
    marker = outside / "SKILL.md"
    marker.write_text("User content", encoding="utf-8")
    if location != ".llm/skills":
        seed(root)
    link = root / location
    link.parent.mkdir(parents=True, exist_ok=True)
    command = shutil.which("cmd")
    assert command is not None
    process = subprocess.run(
        [command, "/d", "/c", "mklink", "/J", str(link), str(outside)],
        capture_output=True,
        text=True,
        check=False,
        timeout=30,
    )
    assert process.returncode == 0, process.stdout + process.stderr
    try:
        assert main(["--write"], root=root) == 1
        assert marker.read_text(encoding="utf-8") == "User content"
        assert sorted(path.name for path in outside.iterdir()) == ["SKILL.md"]
    finally:
        link.rmdir()


def test_real_legacy_launcher_reaches_the_python_generator() -> None:
    node = shutil.which("node")
    assert node is not None
    process = subprocess.run(
        [node, str(ROOT / GENERATOR), "--check"],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )
    assert process.returncode == 0, process.stdout + process.stderr
    assert "Validated" in process.stdout
