"""Exercise exact EOL repair planning, byte preservation and CLI delegation."""

from __future__ import annotations

import codecs
import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from check_eol import git_output
from normalize_eol import main

ROOT = Path(__file__).resolve().parents[2]


@pytest.mark.parametrize(
    ("name", "expected"),
    [
        ("Example.cs", b"a\r\nb\r\nc\r\n"),
        ("Example.md", b"a\nb\nc\n"),
        (".gitignore", b"a\r\nb\r\nc\r\n"),
        ("package.json", b"a\nb\nc\n"),
        (".github/example.json", b"a\nb\nc\n"),
    ],
)
def test_repairs_bom_and_mixed_endings(
    tmp_path: Path, capsys: pytest.CaptureFixture[str], name: str, expected: bytes
) -> None:
    path = tmp_path / name
    path.parent.mkdir(exist_ok=True)
    original = codecs.BOM_UTF8 + b"a\rb\r\nc\n"
    path.write_bytes(original)
    args = ["--repo-root", str(tmp_path), "--paths", name]

    assert main(args) == 0

    assert path.read_bytes() == expected
    assert "Files fixed: 1 (EOL:1, BOMRemoved:1)" in capsys.readouterr().out
    assert main(args) == 0
    assert "Files fixed: 0 (EOL:0, BOMRemoved:0)" in capsys.readouterr().out


def test_dry_run_reports_exact_paths_without_touching_bytes(tmp_path: Path) -> None:
    first = tmp_path / "région file.cs"
    second = tmp_path / "-scope.md"
    unchanged = tmp_path / "Clean.cs"
    report = tmp_path / "changes"
    first.write_bytes(b"change\n")
    second.write_bytes(b"change\r\n")
    unchanged.write_bytes(b"keep\r\n")

    assert (
        main(
            [
                "--repo-root",
                str(tmp_path),
                "--dry-run",
                "--modified-path-list",
                str(report),
                "--paths",
                first.name,
                "./" + second.name,
                unchanged.name,
                first.name,
            ]
        )
        == 2
    )

    assert report.read_bytes() == f"{first.name}\0{second.name}\0".encode()
    assert first.read_bytes() == b"change\n"
    assert second.read_bytes() == b"change\r\n"
    assert unchanged.read_bytes() == b"keep\r\n"


@pytest.mark.parametrize("malformed", [b"\xff\n", b"binary\0\n", b"\xff\xfea\0\n\0"])
def test_refuses_invalid_text_before_any_edit(tmp_path: Path, malformed: bytes) -> None:
    good = tmp_path / "Good.cs"
    bad = tmp_path / "Bad.cs"
    good.write_bytes(b"line\n")
    bad.write_bytes(malformed)

    assert main(["--repo-root", str(tmp_path), "--paths", good.name, bad.name]) == 1

    assert good.read_bytes() == b"line\n"
    assert bad.read_bytes() == malformed


def test_refuses_input_outside_the_repository(tmp_path: Path) -> None:
    root = tmp_path / "repo"
    root.mkdir()
    outside = tmp_path / "Outside.cs"
    outside.write_bytes(b"outside\n")

    assert main(["--repo-root", str(root), "--paths", str(outside)]) == 1

    assert outside.read_bytes() == b"outside\n"


@pytest.mark.parametrize("kind", ["file", "parent", "hardlink"])
def test_never_writes_through_a_link(tmp_path: Path, kind: str) -> None:
    root = tmp_path / "repo"
    root.mkdir()
    outside = tmp_path / "Outside.cs"
    outside.write_bytes(b"outside\n")
    link = root / "Link.cs"
    try:
        if kind == "hardlink":
            link.hardlink_to(outside)
        elif kind == "parent":
            link = root / "linked"
            link.symlink_to(tmp_path, target_is_directory=True)
            link /= outside.name
        else:
            link.symlink_to(outside)
    except OSError:
        if os.name != "nt":
            raise
        pytest.skip("Windows fixture cannot create a link")

    assert main(["--repo-root", str(root), "--paths", str(link)]) == 1

    assert outside.read_bytes() == b"outside\n"


@pytest.mark.parametrize("kind", ["file", "parent"])
def test_internal_links_report_and_repair_the_canonical_target(tmp_path: Path, kind: str) -> None:
    target = tmp_path / "actual" / "Target.cs"
    target.parent.mkdir()
    target.write_bytes(b"line\n")
    alias = tmp_path / "alias.cs"
    try:
        if kind == "file":
            alias.symlink_to(target)
        else:
            alias = tmp_path / "alias"
            alias.symlink_to(target.parent, target_is_directory=True)
            alias /= target.name
    except OSError:
        if os.name != "nt":
            raise
        pytest.skip("Windows fixture cannot create a symlink")
    report = tmp_path / "changes"
    assert (
        main(
            [
                "--repo-root",
                str(tmp_path),
                "--dry-run",
                "--modified-path-list",
                str(report),
                "--paths",
                str(alias),
            ]
        )
        == 2
    )
    assert report.read_bytes() == b"actual/Target.cs\0"
    assert target.read_bytes() == b"line\n"
    assert main(["--repo-root", str(tmp_path), "--paths", str(alias)]) == 0
    assert target.read_bytes() == b"line\r\n"


def test_never_overwrites_a_selected_input_with_its_report(tmp_path: Path) -> None:
    path = tmp_path / "Example.cs"
    path.write_bytes(b"line\n")

    assert (
        main(
            [
                "--repo-root",
                str(tmp_path),
                "--modified-path-list",
                str(path),
                "--paths",
                path.name,
            ]
        )
        == 1
    )

    assert path.read_bytes() == b"line\n"


def test_default_scope_repairs_tracked_files_without_staging(tmp_path: Path) -> None:
    git = shutil.which("git")
    assert git is not None
    subprocess.run([git, "init", "-q"], cwd=tmp_path, check=True, timeout=10)
    tracked = tmp_path / "Tracked.cs"
    untracked = tmp_path / "Untracked.cs"
    tracked.write_bytes(b"line\n")
    untracked.write_bytes(b"untracked\n")
    subprocess.run([git, "add", "--", tracked.name], cwd=tmp_path, check=True, timeout=10)
    before = git_output(tmp_path, ["ls-files", "-s", "-z"])

    assert main(["--repo-root", str(tmp_path)]) == 0

    assert git_output(tmp_path, ["ls-files", "-s", "-z"]) == before
    assert tracked.read_bytes() == b"line\r\n"
    assert untracked.read_bytes() == b"untracked\n"


def test_default_scope_refuses_git_discovery_failure(tmp_path: Path) -> None:
    path = tmp_path / "Example.cs"
    path.write_bytes(b"line\n")

    assert main(["--repo-root", str(tmp_path)]) == 1

    assert path.read_bytes() == b"line\n"


def test_powershell_launcher_keeps_all_paths_from_the_callers_directory(tmp_path: Path) -> None:
    shell = shutil.which("pwsh")
    assert shell is not None
    scripts = tmp_path / "scripts"
    scripts.mkdir()
    for name in (
        "normalize-eol.ps1",
        "normalize_eol.py",
        "check_eol.py",
        "cli_encoding.py",
        "csharp_sources.py",
    ):
        shutil.copyfile(ROOT / "scripts" / name, scripts / name)
    project = scripts / "python~"
    project.mkdir()
    for name in ("pyproject.toml", "uv.lock"):
        shutil.copyfile(ROOT / "scripts/python~" / name, project / name)
    nested = tmp_path / "nested"
    nested.mkdir()
    first = nested / "first file.cs"
    second = nested / "second file.md"
    first.write_bytes(b"first\n")
    second.write_bytes(b"second\r\n")
    report = tmp_path / "changes"

    result = subprocess.run(
        [
            shell,
            "-NoProfile",
            "-File",
            str(scripts / "normalize-eol.ps1"),
            "-DryRun",
            "-ModifiedPathList",
            str(report),
            "-Paths",
            first.name,
            second.name,
        ],
        cwd=nested,
        env={**os.environ, "UV_PROJECT_ENVIRONMENT": str(tmp_path / "launcher-env")},
        capture_output=True,
        encoding="utf-8",
        check=False,
        timeout=45,
    )

    assert result.returncode == 2, result.stdout + result.stderr
    assert report.read_bytes() == b"nested/first file.cs\0nested/second file.md\0"
    assert first.read_bytes() == b"first\n"
    assert second.read_bytes() == b"second\r\n"
